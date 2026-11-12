// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Tests
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using FuzzPhyte.Utility.Analytics.WorkBench.Editor;

    public sealed class FPWorkbenchRowRangeWindowTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        [UnityTest] public IEnumerator SheetRangeUpdatesEveryFigureFamilyAndPersistsWithoutChangingOtherSheet()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPRowWindow-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            var window = ScriptableObject.CreateInstance<FPDataWorkbenchWindow>(); var type = window.GetType(); FPWorkbenchFigureWindow detached = null;
            object Field(object target, string name) => target.GetType().GetField(name, target is FPDataWorkbenchWindow ? Private : BindingFlags.Public | BindingFlags.Instance).GetValue(target);
            FPWorkbenchFigureDocument Describe(UnityEngine.Object view) => (FPWorkbenchFigureDocument)view.GetType().GetMethod("DescribeFigure", Private).Invoke(view, null);
            object Sheet(int index) => ((IList)Field(window, "sheets"))[index];
            FPWorkbenchCsvDataset Data(object sheet) => (FPWorkbenchCsvDataset)Field(sheet, "Dataset");
            var views = new List<Tuple<Type, string>>();
            UnityEngine.Object Find(Tuple<Type, string> id) => Resources.FindObjectsOfTypeAll(id.Item1).FirstOrDefault(v => Describe(v).Id == id.Item2);
            bool Ready() {
                foreach (var id in views) { var view = Find(id); if (view == null || (bool)view.GetType().GetProperty("Busy", Private).GetValue(view)) return false; }
                var sheets = (IList)Field(window, "sheets"); if (sheets.Count == 0) return false;
                var lease = Field(sheets[0], "FigureLease"); if (lease == null) return false;
                var session = lease.GetType().GetProperty("Session").GetValue(lease);
                return !(bool)session.GetType().GetProperty("IsBusy").GetValue(session) && session.GetType().GetProperty("View").GetValue(session) != null;
            }
            try {
                string path = Path.Combine(root, "source.csv"); var lines = new[] { "x,y,z" }.Concat(Enumerable.Range(0, 80).Select(i => i + "," + (i * 2) + "," + (i * 3))).ToArray(); File.WriteAllLines(path, lines);
                window.ShowUtility(); window.position = new Rect(40, 40, 1100, 850);
                var deadline = DateTime.UtcNow.AddSeconds(30);
                type.GetMethod("AddCsv", Private).Invoke(window, new object[] { path });
                while (Data(Sheet(0)) == null && DateTime.UtcNow < deadline) yield return null;
                Assert.That(Data(Sheet(0)), Is.Not.Null);
                type.GetMethod("AddCsv", Private).Invoke(window, new object[] { path });
                while (Data(Sheet(1)) == null && DateTime.UtcNow < deadline) yield return null;
                var first = Sheet(0); var second = Sheet(1); var data = Data(first); string sheetId = (string)Field(first, "Id"), otherId = (string)Field(second, "Id");
                string workbook = (string)Field(window, "workbookId"); var binding = (FPWorkbenchSeriesBinding)Field(first, "Binding"); binding.XMode = FPWorkbenchXMode.Row;
                first.GetType().GetField("PlotColumn").SetValue(first, 1); type.GetMethod("StartSeries", Private).Invoke(window, new object[] { first, true });
                var point = FPWorkbenchPoint3DWindow.Open(workbook, data, 0, 1, 2, rows: binding.Rows, sheetId: sheetId); views.Add(Tuple.Create(point.GetType(), Describe(point).Id));
                var scatter = FPWorkbenchScatterWindow.Open(workbook, data, 0, 1, rows: binding.Rows, sheetId: sheetId); views.Add(Tuple.Create(scatter.GetType(), Describe(scatter).Id));
                var overlay = FPWorkbenchOverlayWindow.Open(workbook, data, binding, 1, sheetId: sheetId); views.Add(Tuple.Create(overlay.GetType(), Describe(overlay).Id));
                var histogram = FPWorkbenchHistogramWindow.Open(data, 1, workbookId: workbook, rows: binding.Rows, sheetId: sheetId); views.Add(Tuple.Create(histogram.GetType(), Describe(histogram).Id));
                var other = FPWorkbenchHistogramWindow.Open(data, 1, workbookId: workbook, sheetId: otherId); views.Add(Tuple.Create(other.GetType(), Describe(other).Id));
                var legacy = FPWorkbenchHistogramWindow.Open(data, 1, workbookId: workbook); views.Add(Tuple.Create(legacy.GetType(), Describe(legacy).Id));
                var document = (FPWorkbenchSheetDocument)type.GetMethod("DescribeSheet", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new[] { first });
                detached = FPWorkbenchFigureWindow.Open(workbook + ":" + sheetId, document, data);
                var originalWindows = views.ToDictionary(id => id.Item2, id => Find(id));
                type.GetMethod("ApplyRowRange", Private).Invoke(window, new object[] { first, new FPWorkbenchRowRange { Enabled = true, FirstRow = 10, EndRow = 20 } });
                while (!Ready() && DateTime.UtcNow < deadline) yield return null;
                Assert.That(Ready(), Is.True, Field(window, "error")?.ToString());
                foreach (var id in views) {
                    var view = Find(id); Assert.That(view, Is.SameAs(originalWindows[id.Item2]), "Range changes must preserve the existing dockable window."); var figure = Describe(view);
                    if (figure.SourceSheetId == otherId) { Assert.That(figure.Rows.Enabled, Is.False); Assert.That(((FPWorkbenchHistogramResult)view.GetType().GetField("result", Private).GetValue(view)).IncludedCount, Is.EqualTo(80)); continue; }
                    Assert.That(figure.SourceSheetId, Is.EqualTo(sheetId)); Assert.That(figure.Rows.FirstRow, Is.EqualTo(10)); Assert.That(figure.Rows.EndRow, Is.EqualTo(20));
                    if (view is FPWorkbenchHistogramWindow) Assert.That(((FPWorkbenchHistogramResult)view.GetType().GetField("result", Private).GetValue(view)).IncludedCount, Is.EqualTo(10));
                    else if (view is FPWorkbenchOverlayWindow) Assert.That(((FPWorkbenchOverlayView)view.GetType().GetField("view", Private).GetValue(view)).Series[0].Bins.Sum(b => b.Count), Is.EqualTo(10));
                    else if (view is FPWorkbenchScatterWindow) Assert.That(((FPWorkbenchPointView)view.GetType().GetField("view", Private).GetValue(view)).MatchingCount, Is.EqualTo(10));
                    else { var batch = view.GetType().GetField("batch", Private).GetValue(view); Assert.That(((FPWorkbenchPointView)batch.GetType().GetField("View").GetValue(batch)).MatchingCount, Is.EqualTo(10)); }
                }
                Assert.That(((FPWorkbenchSeriesCache)Field(first, "Series")).RowCount, Is.EqualTo(10));
                UnityEditor.Undo.FlushUndoRecordObjects(); UnityEditor.Undo.PerformUndo();
                while (!Ready() && DateTime.UtcNow < deadline) yield return null;
                Assert.That(((FPWorkbenchSeriesBinding)Field(first, "Binding")).Rows.Enabled, Is.False);
                UnityEditor.Undo.PerformRedo();
                while (!Ready() && DateTime.UtcNow < deadline) yield return null;
                Assert.That(Ready(), Is.True); Assert.That(((FPWorkbenchSeriesBinding)Field(first, "Binding")).Rows.FirstRow, Is.EqualTo(10));
                foreach (var id in views) Assert.That(Find(id), Is.SameAs(originalWindows[id.Item2]));
                var captured = (FPWorkbenchDocument)type.GetMethod("CaptureWorkbook", Private).Invoke(window, null); string saved = Path.Combine(root, "range.fpworkbook");
                var saving = FPWorkbenchDocumentFile.SaveAsync(captured, saved); while (!saving.IsCompleted) yield return null; saving.GetAwaiter().GetResult(); var savedBytes = File.ReadAllBytes(saved);
                type.GetMethod("OpenWorkbook", Private).Invoke(window, new object[] { saved });
                while ((Field(window, "workbookLoad") != null || Field(window, "pendingWorkbookLoad") != null) && DateTime.UtcNow < deadline) yield return null;
                first = Sheet(0); second = Sheet(1); Assert.That(((FPWorkbenchSeriesBinding)Field(first, "Binding")).Rows.FirstRow, Is.EqualTo(10)); Assert.That(((FPWorkbenchSeriesBinding)Field(second, "Binding")).Rows.Enabled, Is.False);
                Assert.That(File.ReadAllBytes(saved), Is.EqualTo(savedBytes));
                type.GetMethod("NudgeRowRange", Private).Invoke(window, new object[] { first, 1, 0 }); Assert.That(((FPWorkbenchSeriesBinding)Field(first, "Binding")).Rows.FirstRow, Is.EqualTo(11));
                type.GetMethod("ApplyRowRange", Private).Invoke(window, new object[] { first, new FPWorkbenchRowRange() }); Assert.That(((FPWorkbenchSeriesBinding)Field(first, "Binding")).Rows.Enabled, Is.False);
                Assert.That(File.ReadAllLines(path), Is.EqualTo(lines)); Assert.That(Field(window, "error"), Is.Null);
            }
            finally {
                if (detached != null) detached.Close();
                type.GetMethod("CloseOwnedFigures", Private).Invoke(window, new object[] { null }); window.Close();
                Directory.Delete(root, true);
            }
        }
    }
}
