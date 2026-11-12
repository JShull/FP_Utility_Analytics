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

    public sealed class FPWorkbenchSavedFigureWindowTests
    {
        [UnityTest] public IEnumerator ClosedFigureSurvivesWorkbookSaveLoadAndReopensByIdentity()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            string root = Path.Combine(Path.GetTempPath(), "FPSavedFigure-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            string source = Path.Combine(root, "source.csv"); File.WriteAllText(source, "x,depth\n0,1\n1,2\n2,3\n");
            var owner = ScriptableObject.CreateInstance<FPDataWorkbenchWindow>(); FPWorkbenchHistogramWindow histogram = null;
            string cache = null; var ownerType = owner.GetType();
            FPWorkbenchMemoryBudget.Reservation firstScan = null, secondScan = null;
            try
            {
                owner.ShowUtility(); owner.position = new Rect(50, 50, 900, 650); ownerType.GetMethod("AddCsv", flags).Invoke(owner, new object[] { source });
                object sheet = ((IList)ownerType.GetField("sheets", flags).GetValue(owner))[0]; var deadline = DateTime.UtcNow.AddSeconds(10);
                while (sheet.GetType().GetField("Dataset").GetValue(sheet) == null && DateTime.UtcNow < deadline) yield return null;
                var dataset = (FPWorkbenchCsvDataset)sheet.GetType().GetField("Dataset").GetValue(sheet); Assert.That(dataset, Is.Not.Null); cache = dataset.CacheDirectory;
                string workbook = (string)ownerType.GetField("workbookId", flags).GetValue(owner);
                histogram = FPWorkbenchHistogramWindow.Open(dataset, 1, workbookId: workbook);
                var histogramType = histogram.GetType();
                while (histogramType.GetField("result", flags).GetValue(histogram) == null && DateTime.UtcNow < deadline) yield return null;
                Assert.That(histogramType.GetField("result", flags).GetValue(histogram), Is.Not.Null);
                histogramType.GetField("density", flags).SetValue(histogram, true); histogram.Close(); histogram = null;
                ownerType.GetMethod("CloseCurrentSheet", flags).Invoke(owner, null);
                var document = (FPWorkbenchDocument)ownerType.GetMethod("CaptureWorkbook", flags).Invoke(owner, null);
                Assert.That(document.Figures.Count, Is.EqualTo(1)); Assert.That(document.Figures[0].Density, Is.True);
                Assert.That(document.Sheets.Count, Is.Zero); Assert.That(document.Sources.Count, Is.EqualTo(1));
                string id = document.Figures[0].Id; string path = Path.Combine(root, "saved.fpworkbook");
                var saving = FPWorkbenchDocumentFile.SaveAsync(document, path); while (!saving.IsCompleted) yield return null; Assert.That(saving.IsFaulted, Is.False);
                var loading = FPWorkbenchDocumentFile.LoadAsync(path); while (!loading.IsCompleted) yield return null; Assert.That(loading.IsFaulted, Is.False);
                ownerType.GetMethod("ApplyWorkbook", flags).Invoke(owner, new object[] { loading.Result });
                Assert.That(((IList)ownerType.GetField("sheets", flags).GetValue(owner)).Count, Is.Zero);
                ((IDictionary)ownerType.GetField("admittedSources", flags).GetValue(owner)).Clear();
                var resources = ownerType.Assembly.GetType("FuzzPhyte.Utility.Analytics.WorkBench.Editor.FPWorkbenchEditorResources");
                var scans = (FPWorkbenchMemoryBudget)resources.GetField("Scans", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                Assert.That(scans.TryAcquire(1, out firstScan), Is.True); Assert.That(scans.TryAcquire(1, out secondScan), Is.True);
                ownerType.GetMethod("OpenSavedFigure", flags).Invoke(owner, new object[] { loading.Result.Document.Figures[0] });
                Assert.That(ownerType.GetField("pendingSourceOpening", flags).GetValue(owner), Is.Not.Null);
                Assert.That(ownerType.GetField("sourceOpening", flags).GetValue(owner), Is.Null);
                ownerType.GetMethod("CancelSourceOpening", flags).Invoke(owner, null);
                Assert.That(ownerType.GetField("pendingSourceOpening", flags).GetValue(owner), Is.Null);
                ownerType.GetMethod("OpenSavedFigure", flags).Invoke(owner, new object[] { loading.Result.Document.Figures[0] });
                firstScan.Dispose(); secondScan.Dispose(); firstScan = null; secondScan = null;
                while (histogram == null && DateTime.UtcNow < deadline)
                {
                    foreach (var candidate in Resources.FindObjectsOfTypeAll<FPWorkbenchHistogramWindow>())
                        if ((string)histogramType.GetField("figureId", flags).GetValue(candidate) == id) histogram = candidate;
                    yield return null;
                }
                Assert.That(histogram, Is.Not.Null);
                while (histogramType.GetField("result", flags).GetValue(histogram) == null && DateTime.UtcNow < deadline) yield return null;
                Assert.That(histogramType.GetField("result", flags).GetValue(histogram), Is.Not.Null); Assert.That((bool)histogramType.GetField("density", flags).GetValue(histogram), Is.True);
                ownerType.GetMethod("OpenSavedFigure", flags).Invoke(owner, new object[] { loading.Result.Document.Figures[0] });
                int count = 0; foreach (var candidate in Resources.FindObjectsOfTypeAll<FPWorkbenchHistogramWindow>()) if ((string)histogramType.GetField("figureId", flags).GetValue(candidate) == id) count++;
                Assert.That(count, Is.EqualTo(1), "Opening an existing saved figure focuses its view.");
                ownerType.GetMethod("OpenRegisteredSheet", flags).Invoke(owner, new object[] { loading.Result.Document.Sources[0] });
                Assert.That(((IList)ownerType.GetField("sheets", flags).GetValue(owner)).Count, Is.EqualTo(1));
                var reopenedSheet = ((IList)ownerType.GetField("sheets", flags).GetValue(owner))[0];
                Assert.That(reopenedSheet.GetType().GetField("Loading").GetValue(reopenedSheet), Is.Null, "Reopening a verified source must reuse its index, not import again.");
            }
            finally
            {
                firstScan?.Dispose(); secondScan?.Dispose();
                if (histogram != null) { histogram.Close(); UnityEngine.Object.DestroyImmediate(histogram); }
                owner.Close(); UnityEngine.Object.DestroyImmediate(owner);
                if (cache != null && Directory.Exists(cache)) Directory.Delete(cache, true); Directory.Delete(root, true);
            }
        }
    }
}
