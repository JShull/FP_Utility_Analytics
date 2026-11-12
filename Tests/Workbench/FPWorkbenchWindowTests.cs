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

    public sealed class FPWorkbenchWindowTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        [UnityTest] public IEnumerator WindowImportsAndPublishesOnlyRequestedPage()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPWorkbenchWindow-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string csv = Path.Combine(root, "window.csv");
            using (var writer = File.CreateText(csv))
            { writer.WriteLine("time,depth"); for (int i = 0; i < 1000; i++) writer.WriteLine(i + "," + (i * 2)); }
            var window = ScriptableObject.CreateInstance<FPDataWorkbenchWindow>();
            string cache = null;
            try
            {
                window.ShowUtility(); window.position = new Rect(50, 50, 1000, 700);
                window.GetType().GetMethod("AddCsv", Private).Invoke(window, new object[] { csv });
                var sheets = (IList)window.GetType().GetField("sheets", Private).GetValue(window);
                object sheet = sheets[0]; Type type = sheet.GetType();
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (type.GetField("Dataset").GetValue(sheet) == null && DateTime.UtcNow < deadline) yield return null;
                var dataset = (FPWorkbenchCsvDataset)type.GetField("Dataset").GetValue(sheet);
                Assert.That(dataset, Is.Not.Null); cache = dataset.CacheDirectory;
                Assert.That(dataset.RowCount, Is.EqualTo(1000));
                type.GetField("PlotColumn").SetValue(sheet, 1);
                var binding = (FPWorkbenchSeriesBinding)type.GetField("Binding").GetValue(sheet);
                binding.XMode = FPWorkbenchXMode.Numeric; binding.XColumn = 0;
                window.GetType().GetMethod("StartSeries", Private).Invoke(window, new object[] { sheet, true });
                while (window.GetType().GetField("summary", Private).GetValue(window) == null && DateTime.UtcNow < deadline) yield return null;
                var summary = (FPWorkbenchSeriesView)window.GetType().GetField("summary", Private).GetValue(window);
                Assert.That(summary, Is.Not.Null); Assert.That(summary.Maximum, Is.EqualTo(1998));
                var plot = (FPWorkbenchPlotSettings)type.GetField("Plot").GetValue(sheet);
                Assert.That(plot.Navigate(.5, .5, 0), Is.True);
                while (DateTime.UtcNow < deadline)
                {
                    summary = (FPWorkbenchSeriesView)window.GetType().GetField("summary", Private).GetValue(window);
                    if (summary.StartX == plot.StartX && summary.EndX == plot.EndX) break;
                    yield return null;
                }
                Assert.That(summary.StartX, Is.EqualTo(plot.StartX)); Assert.That(summary.EndX, Is.EqualTo(plot.EndX));
                type.GetField("FirstRow").SetValue(sheet, 800L);
                FPWorkbenchPage page = null;
                while (DateTime.UtcNow < deadline)
                {
                    page = (FPWorkbenchPage)window.GetType().GetField("page", Private).GetValue(window);
                    if (page != null && page.FirstRow == 800) break;
                    yield return null;
                }
                Assert.That(page, Is.Not.Null); Assert.That(page.FirstRow, Is.EqualTo(800));
                Assert.That(page.Values[0][1], Is.EqualTo("1600")); Assert.That(page.RowCount, Is.LessThanOrEqualTo(128));
                Assert.That(window.GetType().GetField("error", Private).GetValue(window), Is.Null);
                var layout = (FPWorkbenchColumnLayout)type.GetField("Columns").GetValue(sheet);
                var apply = window.GetType().GetMethod("ApplyCorrection", Private);
                apply.Invoke(window, new object[] { sheet, 500L, 1, "1000", "9999", FPWorkbenchEditKind.Number, "qualification correction", false });
                deadline = DateTime.UtcNow.AddSeconds(15);
                while (DateTime.UtcNow < deadline)
                {
                    summary = (FPWorkbenchSeriesView)window.GetType().GetField("summary", Private).GetValue(window);
                    if (summary != null && summary.Maximum == 9999) break;
                    yield return null;
                }
                Assert.That(summary.Maximum, Is.EqualTo(9999));
                UnityEditor.Undo.PerformUndo();
                while (DateTime.UtcNow < deadline)
                {
                    summary = (FPWorkbenchSeriesView)window.GetType().GetField("summary", Private).GetValue(window);
                    if (summary != null && summary.Maximum == 1498) break;
                    yield return null;
                }
                Assert.That(summary.Maximum, Is.EqualTo(1498));
                UnityEditor.Undo.PerformRedo();
                while (DateTime.UtcNow < deadline)
                {
                    summary = (FPWorkbenchSeriesView)window.GetType().GetField("summary", Private).GetValue(window);
                    if (summary != null && summary.Maximum == 9999) break;
                    yield return null;
                }
                Assert.That(summary.Maximum, Is.EqualTo(9999));
                Assert.That(sheet.GetType().GetField("FirstRow").GetValue(sheet), Is.EqualTo(800));
                Assert.That(plot.StartX, Is.EqualTo(249.75));
                apply.Invoke(window, new object[] { sheet, 500L, 1, "1000", "", FPWorkbenchEditKind.Text, "", true });
                while (DateTime.UtcNow < deadline)
                {
                    summary = (FPWorkbenchSeriesView)window.GetType().GetField("summary", Private).GetValue(window);
                    if (summary != null && summary.Maximum == 1498) break;
                    yield return null;
                }
                Assert.That(summary.Maximum, Is.EqualTo(1498));
                string preference = "FuzzPhyte.Analytics.Workbench.ColumnWidths." + dataset.DatasetId;
                string old = UnityEditor.EditorPrefs.GetString(preference, "");
                try
                {
                    var table = (Rect)window.GetType().GetField("tableRect", Private).GetValue(window);
                    var widths = (float[])window.GetType().GetField("columnPixels", Private).GetValue(window);
                    float originalWidth = widths[0];
                    var handle = new Vector2(table.x + 76 + originalWidth, table.y + 10);
                    window.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = handle });
                    Assert.That((int)window.GetType().GetField("sizingColumn", Private).GetValue(window), Is.EqualTo(0));
                    window.SendEvent(new Event { type = EventType.MouseDrag, button = 0, mousePosition = handle + new Vector2(35, 60) });
                    Assert.That(layout.ManualWidths[0], Is.EqualTo(originalWidth + 35).Within(.01));
                    window.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = handle + new Vector2(35, 60) });
                    float rowWidth = layout.RowHeaderWidth;
                    var rowEdge = new Vector2(table.x + rowWidth, table.y + 10);
                    window.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = rowEdge });
                    Assert.That((int)window.GetType().GetField("sizingColumn", Private).GetValue(window), Is.EqualTo(-2));
                    window.SendEvent(new Event { type = EventType.MouseDrag, button = 0, mousePosition = rowEdge + new Vector2(20, 50) });
                    Assert.That(layout.RowHeaderWidth, Is.EqualTo(rowWidth + 20).Within(.01));
                    window.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = rowEdge + new Vector2(20, 50) });
                    layout.ManualWidths[0] = 245; layout.ManualWidths[1] = 95;
                    typeof(FPDataWorkbenchWindow).GetMethod("SaveColumns", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new[] { sheet });
                    type.GetField("Columns").SetValue(sheet, null);
                    typeof(FPDataWorkbenchWindow).GetMethod("RestoreColumns", Private).Invoke(window, new[] { sheet });
                    layout = (FPWorkbenchColumnLayout)type.GetField("Columns").GetValue(sheet);
                    Assert.That(layout.ManualWidths[0], Is.EqualTo(245));
                    window.GetType().GetMethod("ResetColumn", Private).Invoke(window, new object[] { sheet, 0 });
                    Assert.That(layout.ManualWidths[0], Is.Zero); Assert.That(layout.ManualWidths[1], Is.EqualTo(95));
                    var saved = JsonUtility.FromJson<FPWorkbenchColumnLayout>(UnityEditor.EditorPrefs.GetString(preference));
                    Assert.That(saved.ManualWidths[0], Is.Zero); Assert.That(saved.ManualWidths[1], Is.EqualTo(95));
                    table = (Rect)window.GetType().GetField("tableRect", Private).GetValue(window);
                    var indexButton = new Vector2(table.x + 15, table.y + 10);
                    window.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = indexButton });
                    window.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = indexButton });
                    Assert.That(layout.ShowRowHeader, Is.False);
                    window.Repaint(); yield return null;
                    var restore = (Rect)window.GetType().GetField("rowHeaderRestoreRect", Private).GetValue(window);
                    window.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = restore.center });
                    window.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = restore.center });
                    Assert.That(layout.ShowRowHeader, Is.True);
                    var thumb = (Rect)window.GetType().GetField("rowScrollThumb", Private).GetValue(window);
                    var track = (Rect)window.GetType().GetField("rowScrollTrack", Private).GetValue(window);
                    long beforeScroll = (long)type.GetField("FirstRow").GetValue(sheet);
                    window.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = thumb.center });
                    var outside = new Vector2(track.xMax + 200, track.yMax + 50);
                    window.SendEvent(new Event { type = EventType.MouseDrag, button = 0, mousePosition = outside });
                    long afterScroll = (long)type.GetField("FirstRow").GetValue(sheet);
                    Assert.That(afterScroll, Is.GreaterThan(beforeScroll)); Assert.That(afterScroll, Is.LessThan(1000));
                    window.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = outside });
                    Assert.That(window.GetType().GetField("rowScrollSheet", Private).GetValue(window), Is.Null);
                }
                finally { if (old.Length == 0) UnityEditor.EditorPrefs.DeleteKey(preference); else UnityEditor.EditorPrefs.SetString(preference, old); }
            }
            finally
            {
                window.Close(); UnityEngine.Object.DestroyImmediate(window);
                if (Directory.Exists(root)) Directory.Delete(root, true);
                if (cache != null && Directory.Exists(cache)) Directory.Delete(cache, true);
            }
        }
        [Test] public void RuntimeAssemblyHasNoEditorAssemblyDependencies()
        {
            foreach (var reference in typeof(FPWorkbenchCsvDataset).Assembly.GetReferencedAssemblies())
                Assert.That(reference.Name, Does.Not.Contain("Editor"), "Runtime data engine must compile without Editor hooks.");
        }
        [UnityTest] public IEnumerator WorkbookOpenFailureRetainsCurrentSheetsAndSuccessfulOpenRestoresThem()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPWorkbenchOpen-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root); string csv = Path.Combine(root, "source.csv"); File.WriteAllText(csv, "x,y\n0,3\n1,4\n");
            var window = ScriptableObject.CreateInstance<FPDataWorkbenchWindow>(); string cache = null;
            try
            {
                window.ShowUtility(); window.position = new Rect(10, 10, 900, 650);
                var type = window.GetType(); type.GetMethod("AddCsv", Private).Invoke(window, new object[] { csv });
                var deadline = DateTime.UtcNow.AddSeconds(10);
                var sheetsField = type.GetField("sheets", Private); var list = (IList)sheetsField.GetValue(window); object sheet = list[0];
                while (sheet.GetType().GetField("Dataset").GetValue(sheet) == null && DateTime.UtcNow < deadline) yield return null;
                var dataset = (FPWorkbenchCsvDataset)sheet.GetType().GetField("Dataset").GetValue(sheet);
                Assert.That(dataset, Is.Not.Null); cache = dataset.CacheDirectory;
                string id = (string)sheet.GetType().GetField("Id").GetValue(sheet);
                string path = Path.Combine(root, "saved.fpworkbook");
                type.GetMethod("SaveWorkbook", Private).Invoke(window, new object[] { path });
                while (type.GetField("workbookSave", Private).GetValue(window) != null && DateTime.UtcNow < deadline) yield return null;
                Assert.That(File.Exists(path), Is.True);
                string bad = Path.Combine(root, "bad.fpworkbook"); File.WriteAllText(bad, "{}");
                type.GetMethod("OpenWorkbook", Private).Invoke(window, new object[] { bad });
                while (type.GetField("workbookLoad", Private).GetValue(window) != null && DateTime.UtcNow < deadline) yield return null;
                Assert.That(((IList)sheetsField.GetValue(window))[0], Is.SameAs(sheet));
                Assert.That(type.GetField("error", Private).GetValue(window), Is.Not.Null);
                sheet.GetType().GetField("Name").SetValue(sheet, "Unsaved name");
                type.GetMethod("OpenWorkbook", Private).Invoke(window, new object[] { path });
                while (type.GetField("workbookLoad", Private).GetValue(window) != null && DateTime.UtcNow < deadline) yield return null;
                var restored = ((IList)sheetsField.GetValue(window))[0];
                Assert.That(restored.GetType().GetField("Id").GetValue(restored), Is.EqualTo(id));
                Assert.That(restored.GetType().GetField("Name").GetValue(restored), Is.EqualTo("source"));
                Assert.That(type.GetField("error", Private).GetValue(window), Is.Null);
            }
            finally
            {
                window.Close(); UnityEngine.Object.DestroyImmediate(window);
                if (Directory.Exists(root)) Directory.Delete(root, true);
                if (cache != null && Directory.Exists(cache)) Directory.Delete(cache, true);
            }
        }
        [UnityTest] public IEnumerator UnsavedColumnMetadataSurvivesSerializationAndCacheReopen()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPWorkbenchReload-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root); string csv = Path.Combine(root, "source.csv"); File.WriteAllText(csv, "x,y\n0,3\n1,4\n");
            var original = ScriptableObject.CreateInstance<FPDataWorkbenchWindow>(); FPDataWorkbenchWindow restored = null;
            string cache = null, preference = null, previous = "";
            try
            {
                original.ShowUtility(); original.position = new Rect(10, 10, 900, 650);
                var type = original.GetType(); type.GetMethod("AddCsv", Private).Invoke(original, new object[] { csv });
                var deadline = DateTime.UtcNow.AddSeconds(10); var sheetsField = type.GetField("sheets", Private);
                object sheet = ((IList)sheetsField.GetValue(original))[0];
                while (sheet.GetType().GetField("Dataset").GetValue(sheet) == null && DateTime.UtcNow < deadline) yield return null;
                var data = (FPWorkbenchCsvDataset)sheet.GetType().GetField("Dataset").GetValue(sheet); Assert.That(data, Is.Not.Null); cache = data.CacheDirectory;
                preference = "FuzzPhyte.Analytics.Workbench.ColumnWidths." + data.DatasetId; previous = UnityEditor.EditorPrefs.GetString(preference, "");
                var layout = (FPWorkbenchColumnLayout)sheet.GetType().GetField("Columns").GetValue(sheet);
                layout.ManualWidths[0] = 444;
                type.GetMethod("SaveColumns", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new[] { sheet });
                layout.ManualWidths[0] = 222; // A loaded workbook/unsaved state has priority over local defaults.
                type.GetMethod("ApplyCorrection", Private).Invoke(original, new object[] { sheet, 1L, 1, "4", "40", FPWorkbenchEditKind.Number, "unsaved correction", false });
                type.GetMethod("PreserveWorkingMetadata", Private).Invoke(original, null);
                string serialized = UnityEditor.EditorJsonUtility.ToJson(original);
                restored = ScriptableObject.CreateInstance<FPDataWorkbenchWindow>();
                type.GetMethod("OnDisable", Private).Invoke(restored, null);
                UnityEditor.EditorJsonUtility.FromJsonOverwrite(serialized, restored);
                type.GetMethod("OnEnable", Private).Invoke(restored, null);
                object restoredSheet = ((IList)sheetsField.GetValue(restored))[0];
                while (restoredSheet.GetType().GetField("Dataset").GetValue(restoredSheet) == null && DateTime.UtcNow < deadline) yield return null;
                Assert.That(restoredSheet.GetType().GetField("Dataset").GetValue(restoredSheet), Is.Not.Null);
                var restoredLayout = (FPWorkbenchColumnLayout)restoredSheet.GetType().GetField("Columns").GetValue(restoredSheet);
                Assert.That(restoredLayout.ManualWidths[0], Is.EqualTo(222));
                Assert.That(restoredSheet.GetType().GetField("Id").GetValue(restoredSheet), Is.EqualTo(sheet.GetType().GetField("Id").GetValue(sheet)));
                var corrections = (System.Collections.Generic.List<FPWorkbenchChangeSet>)type.GetField("corrections", Private).GetValue(restored);
                Assert.That(corrections.Count, Is.EqualTo(1));
                Assert.That(corrections[0].Snapshot().TryGet(data.Cell(1, 1), "4", out string effective, out _), Is.True);
                Assert.That(effective, Is.EqualTo("40"));
            }
            finally
            {
                original.Close(); UnityEngine.Object.DestroyImmediate(original);
                if (restored != null) UnityEngine.Object.DestroyImmediate(restored);
                if (preference != null) { if (previous.Length == 0) UnityEditor.EditorPrefs.DeleteKey(preference); else UnityEditor.EditorPrefs.SetString(preference, previous); }
                if (Directory.Exists(root)) Directory.Delete(root, true);
                if (cache != null && Directory.Exists(cache)) Directory.Delete(cache, true);
            }
        }
        [Test] public void ColumnCapacityUsesAvailableWidthAndBoundsRequests()
        {
            var method = typeof(FPDataWorkbenchWindow).GetMethod("ColumnCapacity", BindingFlags.Static | BindingFlags.NonPublic);
            int Capacity(float width, int count) => (int)method.Invoke(null, new object[] { width, count });
            Assert.That(Capacity(500, 40), Is.EqualTo(3));
            Assert.That(Capacity(1200, 40), Is.EqualTo(10));
            Assert.That(Capacity(10000, 40), Is.EqualTo(32));
            Assert.That(Capacity(1200, 2), Is.EqualTo(2));
            Assert.That(Capacity(10, 40), Is.EqualTo(1));
            var scroll = typeof(FPDataWorkbenchWindow).GetMethod("ScrollRow", BindingFlags.Static | BindingFlags.NonPublic);
            long Row(double fraction, long count) => (long)scroll.Invoke(null, new object[] { fraction, count, 20 });
            Assert.That(Row(0, 1000), Is.Zero); Assert.That(Row(1, 1000), Is.EqualTo(980));
            Assert.That(Row(.5, 1000), Is.EqualTo(490));
            Assert.That(Row(.5, (long)int.MaxValue * 10), Is.GreaterThan(int.MaxValue));
            Assert.That(Row(1, long.MaxValue), Is.EqualTo(long.MaxValue - 20));
        }
        [UnityTest] public IEnumerator ResizingWindowRefreshesVisibleColumnPage()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPWorkbenchResize-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root); string csv = Path.Combine(root, "wide.csv");
            var headers = new string[40]; var values = new string[40];
            for (int i = 0; i < 40; i++) { headers[i] = "Column " + i; values[i] = i.ToString(); }
            File.WriteAllText(csv, string.Join(",", headers) + "\n" + string.Join(",", values) + "\n");
            var window = ScriptableObject.CreateInstance<FPDataWorkbenchWindow>(); string cache = null;
            try
            {
                window.ShowUtility(); window.position = new Rect(30, 30, 800, 650);
                window.GetType().GetMethod("AddCsv", Private).Invoke(window, new object[] { csv });
                var deadline = DateTime.UtcNow.AddSeconds(10); FPWorkbenchPage page = null;
                var pageField = window.GetType().GetField("page", Private);
                while (DateTime.UtcNow < deadline)
                {
                    page = (FPWorkbenchPage)pageField.GetValue(window);
                    if (page != null && page.Values[0].Length >= 2 && page.Values[0].Length <= 5) break;
                    yield return null;
                }
                Assert.That(window.position.width, Is.LessThanOrEqualTo(900), "The native utility window must start narrow for the resize test.");
                Assert.That(page, Is.Not.Null); int initialColumns = page.Values[0].Length;
                var sheets = (IList)window.GetType().GetField("sheets", Private).GetValue(window); object sheet = sheets[0];
                cache = ((FPWorkbenchCsvDataset)sheet.GetType().GetField("Dataset").GetValue(sheet)).CacheDirectory;
                window.position = new Rect(30, 30, 1500, 650); window.Repaint();
                while (DateTime.UtcNow < deadline)
                { page = (FPWorkbenchPage)pageField.GetValue(window); if (page != null && page.Values[0].Length > initialColumns) break; yield return null; }
                Assert.That(page.Values[0].Length, Is.GreaterThan(initialColumns));
                Assert.That(page.Values[0].Length, Is.LessThanOrEqualTo(32));
                Assert.That(page.Values[0][page.Values[0].Length - 1], Is.EqualTo((page.Values[0].Length - 1).ToString()));
                Assert.That(window.GetType().GetField("error", Private).GetValue(window), Is.Null);
                var buttons = (Rect[])window.GetType().GetField("columnButtonRects", Private).GetValue(window);
                void Click(int index)
                {
                    window.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = buttons[index].center });
                    window.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = buttons[index].center });
                }
                Click(2); Assert.That(sheet.GetType().GetField("FirstColumn").GetValue(sheet), Is.EqualTo(1));
                Click(1); Assert.That(sheet.GetType().GetField("FirstColumn").GetValue(sheet), Is.EqualTo(0));
                int windowColumns = (int)window.GetType().GetField("visibleColumns", Private).GetValue(window);
                Click(3); Assert.That(sheet.GetType().GetField("FirstColumn").GetValue(sheet), Is.EqualTo(windowColumns));
                Click(0); Assert.That(sheet.GetType().GetField("FirstColumn").GetValue(sheet), Is.EqualTo(0));
            }
            finally
            {
                window.Close(); UnityEngine.Object.DestroyImmediate(window);
                if (Directory.Exists(root)) Directory.Delete(root, true);
                if (cache != null && Directory.Exists(cache)) Directory.Delete(cache, true);
            }
        }
    }
}
