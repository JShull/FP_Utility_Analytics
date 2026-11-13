// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Tests
{
    using System;
    using System.Collections;
    using System.Globalization;
    using System.IO;
    using System.Reflection;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using FuzzPhyte.Utility.Analytics.WorkBench.Editor;
    public sealed class FPWorkbenchGraphSelectionTests
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private static object Get(object value, string field) => value.GetType().GetField(field, Flags).GetValue(value);
        private static object Call(object value, string method, params object[] args) => value.GetType().GetMethod(method, Flags).Invoke(value, args);
        private static Color[] Colors(string owner, string dataset, long row, string column) => (Color[])typeof(FPDataWorkbenchWindow).Assembly
            .GetType("FuzzPhyte.Utility.Analytics.WorkBench.Editor.FPWorkbenchGraphSelection").GetMethod("CellColors", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { owner, dataset, row, column });
        private static Vector2 ScatterClick(FPWorkbenchScatterWindow window, FPWorkbenchSpatialPoint point)
        {
            var rect = (Rect)Get(window, "inspectionRect"); var b = ((FPWorkbenchPointView)Get(window, "view")).Bounds;
            var area = new Rect(rect.x + 80, rect.y + 25, rect.width - 95, rect.height - 85);
            var p = new Vector2(area.x + (float)((point.X - b.MinimumX) / (b.MaximumX - b.MinimumX)) * area.width,
                area.y + (float)(1 - (point.Y - b.MinimumY) / (b.MaximumY - b.MinimumY)) * area.height);
            return p + window.rootVisualElement.worldBound.position;
        }
        private static void Click(FPWorkbenchScatterWindow window, int index)
        {
            var point = ((FPWorkbenchPointView)Get(window, "view")).Points[index]; Vector2 mouse = ScatterClick(window, point);
            window.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = mouse });
            window.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = mouse });
        }
        [UnityTest] public IEnumerator ScatterPicksAutoNavigateWithoutCoveringGraphAndRetainIndependentOverlappingCells()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPGraphSelections-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            var owner = ScriptableObject.CreateInstance<FPDataWorkbenchWindow>(); FPWorkbenchScatterWindow first = null, second = null;
            try {
                var lines = new string[13]; lines[0] = "x,y,z";
                for (int i = 0; i < 12; i++) lines[i + 1] = i + "," + (i * i) + "," + (i * 3 + 4);
                string source = Path.Combine(root, "source.csv"); File.WriteAllLines(source, lines); owner.ShowUtility(); Call(owner, "AddCsv", source);
                var sheet = ((IList)Get(owner, "sheets"))[0]; var st = sheet.GetType(); var deadline = DateTime.UtcNow.AddSeconds(15);
                while (st.GetField("Dataset").GetValue(sheet) == null && DateTime.UtcNow < deadline) yield return null;
                var dataset = (FPWorkbenchCsvDataset)st.GetField("Dataset").GetValue(sheet); Assert.That(dataset, Is.Not.Null);
                string workbook = (string)Get(owner, "workbookId"), id = (string)st.GetField("Id").GetValue(sheet);
                first = FPWorkbenchScatterWindow.Open(workbook, dataset, 0, 1, sheetId: id); first.position = new Rect(60, 60, 900, 650);
                second = FPWorkbenchScatterWindow.Open(workbook, dataset, 0, 2, sheetId: id); second.position = new Rect(1000, 60, 900, 650);
                while ((Get(first, "view") == null || Get(second, "view") == null) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(Get(first, "view"), Is.Not.Null); Assert.That(Get(second, "view"), Is.Not.Null);
                first.Focus(); first.Repaint(); yield return null; yield return null; Click(first, 5); yield return null;
                Assert.That(Get(first, "selected"), Is.True); Assert.That(st.GetField("FirstRow").GetValue(sheet), Is.EqualTo(5));
                Assert.That(st.GetField("FirstColumn").GetValue(sheet), Is.EqualTo(1), "Y is prioritized."); Assert.That(EditorWindow.focusedWindow, Is.SameAs(first));
                string x = dataset.Cell(0, 0).ColumnId, y = dataset.Cell(0, 1).ColumnId, z = dataset.Cell(0, 2).ColumnId;
                Assert.That(Colors(workbook, dataset.DatasetId, 5, x).Length, Is.EqualTo(1)); Assert.That(Colors(workbook, dataset.DatasetId, 5, y).Length, Is.EqualTo(1));
                second.Focus(); second.Repaint(); yield return null; yield return null; Click(second, 6); yield return null;
                Assert.That(st.GetField("FirstRow").GetValue(sheet), Is.EqualTo(6)); Assert.That(st.GetField("FirstColumn").GetValue(sheet), Is.EqualTo(2));
                Assert.That(Colors(workbook, dataset.DatasetId, 5, y).Length, Is.EqualTo(1), "Another graph's pick must remain.");
                Assert.That(Colors(workbook, dataset.DatasetId, 6, z).Length, Is.EqualTo(1)); Assert.That(EditorWindow.focusedWindow, Is.SameAs(second));
                Click(second, 5); yield return null;
                var overlap = Colors(workbook, dataset.DatasetId, 5, x); Assert.That(overlap.Length, Is.EqualTo(2)); Assert.That(overlap[0], Is.Not.EqualTo(overlap[1]));
                Assert.That(Colors(workbook, dataset.DatasetId, 5, null).Length, Is.EqualTo(2), "Row headers carry both graph colors.");
                second.Close(); second = null;
                Assert.That(Colors(workbook, dataset.DatasetId, 5, x).Length, Is.EqualTo(1)); Assert.That(Colors(workbook, dataset.DatasetId, 5, z), Is.Empty);
                first.Close(); first = null; Assert.That(Colors(workbook, dataset.DatasetId, 5, x), Is.Empty);
                Assert.That(File.ReadAllLines(source), Is.EqualTo(lines));
            } finally { if (first != null) first.Close(); if (second != null) second.Close(); owner.Close(); Directory.Delete(root, true); }
        }
        [UnityTest] public IEnumerator OverlayPickUsesEachSeriesAxisAndPreservesOriginalRows()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPOverlayPicking-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try {
                string source = Path.Combine(root, "source.csv"); File.WriteAllText(source, "x,a,b\n0,0,1000\n1,2,1200\n2,4,1500\n3,6,1800\n4,8,2000\n");
                var import = FPWorkbenchCsvDataset.ImportAsync(source, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile());
                while (!import.IsCompleted) yield return null; var data = import.GetAwaiter().GetResult();
                var build = FPWorkbenchOverlay.BuildAsync(data, new FPWorkbenchSeriesBinding { XMode = FPWorkbenchXMode.Numeric, XColumn = 0,
                    Rows = new FPWorkbenchRowRange { Enabled = true, FirstRow = 1, EndRow = 4 } }, new[] { 1, 2 });
                while (!build.IsCompleted) yield return null; var overlay = build.GetAwaiter().GetResult();
                var query = overlay.QueryAsync(1, 3, 100); while (!query.IsCompleted) yield return null; var view = query.GetAwaiter().GetResult();
                var mappings = new[] { new FPWorkbenchPlotSettings { LockY = true, MinimumY = 0, MaximumY = 10 },
                    new FPWorkbenchPlotSettings { LockY = true, MinimumY = 1000, MaximumY = 2000 } };
                var right = overlay.PickDisplayedPointAsync(view, mappings, .5, .5, 800, 600); while (!right.IsCompleted) yield return null;
                var chosen = right.GetAwaiter().GetResult(); Assert.That(chosen.HasValue, Is.True); Assert.That(chosen.Value.Series, Is.EqualTo(1));
                Assert.That(chosen.Value.Point.Row, Is.EqualTo(2)); Assert.That(chosen.Value.Point.Y, Is.EqualTo(1500));
                mappings[0].InvertY = true;
                var left = overlay.PickDisplayedPointAsync(view, mappings, .5, .4, 800, 600); while (!left.IsCompleted) yield return null;
                chosen = left.GetAwaiter().GetResult(); Assert.That(chosen.HasValue, Is.True); Assert.That(chosen.Value.Series, Is.EqualTo(0));
                Assert.That(chosen.Value.Point.Row, Is.EqualTo(2)); Assert.That(chosen.Value.Point.Y, Is.EqualTo(4));
                using var cancel = new System.Threading.CancellationTokenSource(); cancel.Cancel();
                var stopped = overlay.PickDisplayedPointAsync(view, mappings, .5, .4, 800, 600, cancel.Token); while (!stopped.IsCompleted) yield return null;
                bool cancelled = false; try { stopped.GetAwaiter().GetResult(); } catch (OperationCanceledException) { cancelled = true; }
                Assert.That(cancelled, Is.True);
            } finally { Directory.Delete(root, true); }
        }
        [UnityTest] public IEnumerator SummaryMarkersUseActualExtremumCoordinatesAndOriginalRows()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPRepresentatives-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try {
                var lines = new string[12001]; lines[0] = "x,y";
                for (int i = 0; i < 12000; i++) lines[i + 1] = i + "," + (i == 400 ? "bad" : i == 125 ? "100" : (i % 17).ToString(CultureInfo.InvariantCulture));
                string path = Path.Combine(root, "source.csv"); File.WriteAllLines(path, lines);
                var import = FPWorkbenchCsvDataset.ImportAsync(path, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile()); while (!import.IsCompleted) yield return null; var data = import.GetAwaiter().GetResult();
                var build = FPWorkbenchSeriesCache.BuildAsync(data, new FPWorkbenchSeriesBinding { XMode = FPWorkbenchXMode.Numeric, XColumn = 0, YColumn = 1,
                    Rows = new FPWorkbenchRowRange { Enabled = true, FirstRow = 100, EndRow = 11000 } });
                while (!build.IsCompleted) yield return null; var cache = build.GetAwaiter().GetResult();
                var query = cache.QueryAsync(100, 10999, 16); while (!query.IsCompleted) yield return null; var view = query.GetAwaiter().GetResult();
                Assert.That(view.Representatives.Length, Is.GreaterThan(0)); Assert.That(view.Representatives.Length, Is.LessThanOrEqualTo(view.Bins.Length * 4));
                bool found = false; long previous = -1;
                foreach (var point in view.Representatives) { Assert.That(point.Row, Is.GreaterThan(previous)); previous = point.Row;
                    Assert.That(point.X, Is.EqualTo(point.Row), "Summary markers retain the sample's actual X, not a bin center.");
                    if (point.Row == 125) { found = true; Assert.That(point.Y, Is.EqualTo(100)); }
                }
                Assert.That(found, Is.True); Assert.That(view.RecordsRead, Is.LessThan(500), "Query remains bounded by summary blocks and represented samples.");
                foreach (var bin in view.Bins) Assert.That(bin.RepresentativeFirst + bin.RepresentativeCount, Is.LessThanOrEqualTo(view.Representatives.Length));
            } finally { Directory.Delete(root, true); }
        }
    }
}
