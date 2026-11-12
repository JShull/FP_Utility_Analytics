// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Tests
{
    using System;
    using System.Collections;
    using System.IO;
    using System.Reflection;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using FuzzPhyte.Utility.Analytics.WorkBench.Editor;

    public sealed class FPWorkbenchDerivedOverlayWindowTests
    {
        [UnityTest] public IEnumerator MixedOverlayFreezesDefinitionsAndReopensSavedStableBindings()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            string root = Path.Combine(Path.GetTempPath(), "FPDerivedOverlayWindow-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            string source = Path.Combine(root, "source.csv"); File.WriteAllText(source, "x,y\n0,1\n1,2\n2,3\n");
            var import = FPWorkbenchCsvDataset.ImportAsync(source, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile()); while (!import.IsCompleted) yield return null;
            Assert.That(import.IsFaulted, Is.False); var data = import.Result;
            var definitions = new FPWorkbenchFormulaSet { DatasetId = data.DatasetId };
            definitions.Columns.Add(new FPWorkbenchDerivedColumn { Name = "Sum", Expression = "C1 + C2" }); definitions.Columns.Add(new FPWorkbenchDerivedColumn { Name = "Difference", Expression = "C2 - C1" });
            var program = FPWorkbenchFormulaProgram.Compile(data, definitions); var changes = new FPWorkbenchChangeSet { DatasetId = data.DatasetId };
            changes.Set(data.Cell(2, 1), "3", "9", FPWorkbenchEditKind.Number, "fixture");
            var common = new FPWorkbenchSeriesBinding { XMode = FPWorkbenchXMode.Numeric, XColumn = 0 };
            FPWorkbenchOverlayWindow window = null, restored = null; var type = typeof(FPWorkbenchOverlayWindow);
            FPWorkbenchOverlayView View(FPWorkbenchOverlayWindow target) => (FPWorkbenchOverlayView)type.GetField("view", flags).GetValue(target);
            bool Busy(FPWorkbenchOverlayWindow target) => (bool)type.GetProperty("Busy", flags).GetValue(target);
            try
            {
                window = FPWorkbenchOverlayWindow.Open("fixture", data, common, 0, changes.Snapshot(), program, program.ColumnId(0));
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while ((View(window) == null || Busy(window)) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(View(window), Is.Not.Null); Assert.That(View(window).Maximum, Is.EqualTo(11));
                definitions.Columns[0].Expression = "C1 + C2 + 100"; changes.Set(data.Cell(2, 1), "3", "99", FPWorkbenchEditKind.Number, "later changes");
                var edited = FPWorkbenchFormulaProgram.Compile(data, definitions); string difference = program.ColumnId(1), columnId = data.DatasetId + ":derived:" + difference;
                var canDrop = type.GetMethod("CanAcceptDerivedDrop", flags);
                Assert.That(canDrop.Invoke(window, new object[] { "fixture", data.DatasetId, columnId, difference, edited.Fingerprint }), Is.False);
                Assert.That(canDrop.Invoke(window, new object[] { "fixture", data.DatasetId, columnId, difference, program.Fingerprint }), Is.True);
                Assert.That(type.GetMethod("AddColumn", flags).Invoke(window, new object[] { "fixture", data.DatasetId, data.Cell(0, 1).ColumnId, 1 }), Is.True);
                while ((View(window) == null || Busy(window)) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(type.GetMethod("AddDerivedColumn", flags).Invoke(window, new object[] { "fixture", data.DatasetId, columnId, difference }), Is.True);
                while ((View(window) == null || Busy(window)) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(View(window).Series.Count, Is.EqualTo(3)); Assert.That(View(window).Series[0].Maximum, Is.EqualTo(11));
                Assert.That(View(window).Series[1].Maximum, Is.EqualTo(9)); Assert.That(View(window).Series[2].Maximum, Is.EqualTo(7));
                var figure = (FPWorkbenchFigureDocument)type.GetMethod("DescribeFigure", flags).Invoke(window, null); figure.Validate();
                Assert.That(figure.YDerivedIds, Is.EqualTo(new[] { program.ColumnId(0), "", difference }));
                var document = new FPWorkbenchDocument(); document.Sources.Add(new FPWorkbenchSourceDocument { DatasetId = data.DatasetId, SourcePath = source, CacheDirectory = data.CacheDirectory, Import = data.ImportProfile }); document.Figures.Add(figure);
                string path = Path.Combine(root, "mixed.fpworkbook"); var saving = FPWorkbenchDocumentFile.SaveAsync(document, path); while (!saving.IsCompleted) yield return null; Assert.That(saving.IsFaulted, Is.False);
                var loading = FPWorkbenchDocumentFile.LoadAsync(path); while (!loading.IsCompleted) yield return null; Assert.That(loading.IsFaulted, Is.False);
                window.Close(); window = null;
                restored = (FPWorkbenchOverlayWindow)type.GetMethod("OpenSaved", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { document.Id, loading.Result.SourceDatasets[0], loading.Result.Document.Figures[0] });
                while ((View(restored) == null || Busy(restored)) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(View(restored), Is.Not.Null); Assert.That(View(restored).Series.Count, Is.EqualTo(3)); Assert.That(View(restored).Maximum, Is.EqualTo(11));
                Assert.That(View(restored).FormulaFingerprint, Is.EqualTo(program.Fingerprint));
            }
            finally
            {
                if (window != null) { window.Close(); UnityEngine.Object.DestroyImmediate(window); }
                if (restored != null) { restored.Close(); UnityEngine.Object.DestroyImmediate(restored); }
                Directory.Delete(root, true);
            }
        }
    }
}
