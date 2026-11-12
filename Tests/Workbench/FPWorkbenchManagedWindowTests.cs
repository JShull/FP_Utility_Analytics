// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Tests
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using FuzzPhyte.Utility.Analytics.WorkBench.Editor;

    public sealed class FPWorkbenchManagedWindowTests
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
        private static object Get(object owner, string name) => owner.GetType().GetField(name, Instance | BindingFlags.Public).GetValue(owner);
        private static object Call(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, Instance).Invoke(owner, args);
        private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Instance).SetValue(owner, value);
        private static IEnumerator Until(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!condition() && DateTime.UtcNow < deadline) yield return null;
            Assert.That(condition(), Is.True, "Workbench operation did not complete within the fixture deadline.");
        }
        [UnityTest] public IEnumerator NormalImportSharedViewsDetachedPlotAndWorkbookRecoverAfterEligibleEviction()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPManagedWindow-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            var manager = typeof(FPDataWorkbenchWindow).Assembly.GetType("FuzzPhyte.Utility.Analytics.WorkBench.Editor.FPWorkbenchEditorCache");
            var storeField = manager.GetField("store", Static); var openingField = manager.GetField("opening", Static);
            var settingsProperty = manager.GetProperty("Settings", Static);
            var oldStore = storeField.GetValue(null); var oldOpening = openingField.GetValue(null); var oldSettings = settingsProperty.GetValue(null);
            FPWorkbenchDiskCacheStore store = null; FPDataWorkbenchWindow window = null; FPWorkbenchHistogramWindow histogram = null;
            var plots = new List<EditorWindow>();
            bool installed = false;
            try {
                var opening = FPWorkbenchDiskCacheStore.OpenAsync(Path.Combine(root, "managed"), 64L * 1024 * 1024);
                yield return Until(() => opening.IsCompleted); store = opening.GetAwaiter().GetResult();
                storeField.SetValue(null, store); openingField.SetValue(null, Task.FromResult(new FPWorkbenchManagedCache(store)));
                settingsProperty.SetValue(null, new FPWorkbenchCacheSettings { Root = store.Root, QuotaBytes = store.QuotaBytes }); installed = true;
                string csv = Path.Combine(root, "original.csv"), workbook = Path.Combine(root, "saved.fpworkbook");
                const string original = "x,y,z\n1,2,3\n4,5,6\n"; File.WriteAllText(csv, original);
                window = ScriptableObject.CreateInstance<FPDataWorkbenchWindow>(); window.ShowUtility(); Set(window, "workbookName", "Managed fixture");
                Call(window, "AddCsv", csv); var sheets = (IList)Get(window, "sheets");
                yield return Until(() => sheets.Count == 1 && Get(sheets[0], "Dataset") != null);
                var first = sheets[0]; var data = (FPWorkbenchCsvDataset)Get(first, "Dataset");
                Assert.That(data.CacheDirectory, Does.StartWith(Path.Combine(store.Root, "entries") + Path.DirectorySeparatorChar));
                var refusedSettings = (Task)manager.GetMethod("ApplyAsync", Static).Invoke(null, new object[] { new FPWorkbenchCacheSettings { Root = store.Root, QuotaBytes = store.QuotaBytes } });
                yield return Until(() => refusedSettings.IsCompleted);
                Assert.That(refusedSettings.IsFaulted, Is.True, "Settings changes cannot close a store with live table leases.");
                Assert.That(refusedSettings.Exception.GetBaseException(), Is.TypeOf<IOException>());
                Call(window, "AddCsv", csv);
                yield return Until(() => sheets.Count == 2 && Get(sheets[1], "Dataset") != null);
                Assert.That(((FPWorkbenchCsvDataset)Get(sheets[1], "Dataset")).CacheDirectory, Is.EqualTo(data.CacheDirectory), "Multiple views reuse the same verified source cache.");
                Call(window, "ApplyCorrection", first, 0L, 1, "2", "7", FPWorkbenchEditKind.Number, "managed fixture", false);
                var formulas = new FPWorkbenchFormulaSet { DatasetId = data.DatasetId };
                formulas.Columns.Add(new FPWorkbenchDerivedColumn { Name = "Double X", Expression = "C1 * 2" });
                Assert.That((bool)Call(window, "ApplyCalculations", first, formulas), Is.True);
                ((FPWorkbenchColumnLayout)Get(first, "Columns")).ManualWidths[0] = 177;
                histogram = FPWorkbenchHistogramWindow.Open(data, 1, workbookId: (string)Get(window, "workbookId"));
                yield return Until(() => Get(histogram, "result") != null);
                Call(window, "SaveWorkbook", workbook);
                yield return Until(() => Get(window, "workbookSave") == null);
                Assert.That(File.Exists(workbook), Is.True, (string)Get(window, "error")); byte[] saved = File.ReadAllBytes(workbook);
                window.Close(); UnityEngine.Object.DestroyImmediate(window); window = null;
                var held = store.TrimAsync(0); yield return Until(() => held.IsCompleted);
                Assert.That(held.GetAwaiter().GetResult().RemovedEntries, Is.Zero, "The detached histogram pins the cache after all table views close.");
                histogram.Close(); UnityEngine.Object.DestroyImmediate(histogram); histogram = null;
                yield return Until(() => store.IsIdle);
                var trim = store.TrimAsync(0); yield return Until(() => trim.IsCompleted);
                Assert.That(trim.GetAwaiter().GetResult().WithinTarget, Is.True);
                Assert.That(Directory.Exists(data.CacheDirectory), Is.False);
                window = ScriptableObject.CreateInstance<FPDataWorkbenchWindow>(); window.ShowUtility();
                Call(window, "OpenWorkbook", workbook);
                yield return Until(() => Get(window, "workbookLoad") == null && Get(window, "pendingWorkbookLoad") == null);
                sheets = (IList)Get(window, "sheets"); Assert.That(sheets.Count, Is.EqualTo(2), (string)Get(window, "error"));
                var recovered = (FPWorkbenchCsvDataset)Get(sheets[0], "Dataset");
                Assert.That(recovered.CacheDirectory, Is.Not.EqualTo(data.CacheDirectory));
                Assert.That(((FPWorkbenchColumnLayout)Get(sheets[0], "Columns")).ManualWidths[0], Is.EqualTo(177));
                Assert.That(((List<FPWorkbenchChangeSet>)Get(window, "corrections"))[0].Cells.Count, Is.EqualTo(1));
                Assert.That(((List<FPWorkbenchSourceDocument>)Get(window, "registeredSources"))[0].HasCalculations, Is.True);
                Assert.That(((List<FPWorkbenchFigureDocument>)Get(window, "savedFigures")).Count, Is.EqualTo(1));
                Assert.That(File.ReadAllBytes(workbook), Is.EqualTo(saved)); Assert.That(File.ReadAllText(csv), Is.EqualTo(original));
                string owner = (string)Get(window, "workbookId");
                var captured = (FPWorkbenchDocument)Call(window, "CaptureWorkbook");
                plots.Add(FPWorkbenchFigureWindow.Open(Guid.NewGuid().ToString("N"), captured.Sheets[0], recovered));
                plots.Add(FPWorkbenchOverlayWindow.Open(owner, recovered, new FPWorkbenchSeriesBinding(), 1));
                plots.Add(FPWorkbenchScatterWindow.Open(owner, recovered, 0, 1));
                plots.Add(FPWorkbenchPoint3DWindow.Open(owner, recovered, 0, 1, 2));
                window.Close(); UnityEngine.Object.DestroyImmediate(window); window = null;
                for (int i = 0; i < plots.Count; i++) {
                    trim = store.TrimAsync(0); yield return Until(() => trim.IsCompleted);
                    Assert.That(trim.GetAwaiter().GetResult().RemovedEntries, Is.Zero, "Each remaining detached view owns its cache independently.");
                    plots[i].Close(); UnityEngine.Object.DestroyImmediate(plots[i]); plots[i] = null;
                }
                yield return Until(() => store.IsIdle); trim = store.TrimAsync(0); yield return Until(() => trim.IsCompleted); trim.GetAwaiter().GetResult();
                File.WriteAllText(csv, "x,y,z\n9,2,3\n4,5,6\n");
                window = ScriptableObject.CreateInstance<FPDataWorkbenchWindow>(); window.ShowUtility(); Set(window, "workbookName", "Sentinel workbook");
                Call(window, "OpenWorkbook", workbook);
                yield return Until(() => Get(window, "workbookLoad") == null && Get(window, "pendingWorkbookLoad") == null);
                Assert.That(Get(window, "workbookName"), Is.EqualTo("Sentinel workbook"));
                Assert.That(((IList)Get(window, "sheets")).Count, Is.Zero);
                Assert.That((string)Get(window, "error"), Does.Contain("identity changed"));
                Call(window, "AddCsv", csv); Call(window, "CloseCurrentSheet");
                yield return Until(() => store.IsIdle);
                Assert.That(store.ReservedBytes, Is.Zero, "Closing during import releases worker reservations.");
                trim = store.TrimAsync(0); yield return Until(() => trim.IsCompleted);
                Assert.That(trim.GetAwaiter().GetResult().WithinTarget, Is.True, "Cancelled imports leave no unowned generated payload.");
            } finally {
                foreach (var plot in plots) if (plot != null) { plot.Close(); UnityEngine.Object.DestroyImmediate(plot); }
                if (histogram != null) { histogram.Close(); UnityEngine.Object.DestroyImmediate(histogram); }
                if (window != null) { window.Close(); UnityEngine.Object.DestroyImmediate(window); }
                if (installed) { storeField.SetValue(null, oldStore); openingField.SetValue(null, oldOpening); settingsProperty.SetValue(null, oldSettings); }
                if (store != null) {
                    var deadline = DateTime.UtcNow.AddSeconds(5); while (!store.IsIdle && DateTime.UtcNow < deadline) Task.Delay(10).GetAwaiter().GetResult();
                    store.Dispose();
                }
                Directory.Delete(root, true);
            }
        }
    }
}
