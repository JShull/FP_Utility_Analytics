// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Tests
{
    using System;
    using System.Collections;
    using System.IO;
    using System.Reflection;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using FuzzPhyte.Utility.Analytics.WorkBench.Editor;
    public sealed class FPWorkbenchColumnProjectionWindowTests
    {
        [UnityTest] public IEnumerator ClosingCopyAndExportRetainsAdmissionUntilWorkersTerminate()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = typeof(FPDataWorkbenchWindow);
            var resources = type.Assembly.GetType("FuzzPhyte.Utility.Analytics.WorkBench.Editor.FPWorkbenchEditorResources");
            var scans = (FPWorkbenchMemoryBudget)resources.GetField("Scans", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var memory = (FPWorkbenchMemoryBudget)resources.GetField("Memory", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            long before = memory.UsedBytes;
            var copy = new System.Threading.Tasks.TaskCompletionSource<string>();
            var export = new System.Threading.Tasks.TaskCompletionSource<FPWorkbenchCsvExportResult>();
            var window = ScriptableObject.CreateInstance<FPDataWorkbenchWindow>();
            window.ShowUtility();
            object[] first = { 1024L, null }, second = { 2048L, null };
            try {
                var acquire = resources.GetMethod("TryAcquireScan", BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That((bool)acquire.Invoke(null, first), Is.True);
                Assert.That((bool)acquire.Invoke(null, second), Is.True);
                type.GetField("clipboardMemory", flags).SetValue(window, first[1]);
                type.GetField("exportMemory", flags).SetValue(window, second[1]);
                type.GetField("clipboardWork", flags).SetValue(window, copy.Task);
                type.GetField("exportWork", flags).SetValue(window, export.Task);
                window.Close();
                Assert.That(scans.ActiveWorkers, Is.EqualTo(2));
                Assert.That(memory.UsedBytes, Is.EqualTo(before + 3072));
                copy.SetCanceled(); export.SetCanceled();
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (scans.ActiveWorkers != 0 && DateTime.UtcNow < deadline) yield return null;
                Assert.That(scans.ActiveWorkers, Is.Zero);
                Assert.That(memory.UsedBytes, Is.EqualTo(before));
            } finally {
                copy.TrySetCanceled(); export.TrySetCanceled();
                window.Close(); UnityEngine.Object.DestroyImmediate(window);
                // Failed setup must not leave an unattached reservation in the shared pool.
                if (copy.Task.IsCompleted) (first[1] as IDisposable)?.Dispose();
                if (export.Task.IsCompleted) (second[1] as IDisposable)?.Dispose();
            }
        }
        [UnityTest] public IEnumerator GridProjectionPreservesSourceTargetsUndoNavigationAndWorkbookLayout()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPProjectGrid-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            string originalClipboard = EditorGUIUtility.systemCopyBuffer;
            FPDataWorkbenchWindow window = null, restored = null; string cache = null, preference = null;
            try {
                string csv = Path.Combine(root, "input.csv"); File.WriteAllText(csv, "a,b,c,d,e,f" + Guid.NewGuid().ToString("N") + "\n1,2,3,4,5,6\n10,20,30,40,50,60\n");
                window = ScriptableObject.CreateInstance<FPDataWorkbenchWindow>(); window.ShowUtility(); window.position = new Rect(20, 20, 1350, 750);
                var type = window.GetType(); type.GetMethod("AddCsv", flags).Invoke(window, new object[] { csv });
                var sheets = (IList)type.GetField("sheets", flags).GetValue(window); var sheet = sheets[0]; var st = sheet.GetType();
                var deadline = DateTime.UtcNow.AddSeconds(15);
                while ((st.GetField("ColumnOrder").GetValue(sheet) == null) && DateTime.UtcNow < deadline) yield return null;
                var data = (FPWorkbenchCsvDataset)st.GetField("Dataset").GetValue(sheet); Assert.That(data, Is.Not.Null); cache = data.CacheDirectory;
                preference = "FuzzPhyte.Analytics.Workbench.ColumnWidths." + data.DatasetId;
                var layout = (FPWorkbenchColumnLayout)st.GetField("Columns").GetValue(sheet);
                string[] ids = (string[])st.GetField("SchemaIds").GetValue(sheet);
                void Change(string id, int move, bool? hide) => type.GetMethod("ChangeColumnProjection", flags).Invoke(window, new object[] { sheet, id, move, hide });
                Undo.IncrementCurrentGroup(); Change(ids[5], -5, null); Undo.FlushUndoRecordObjects();
                Assert.That((int[])st.GetField("ColumnOrder").GetValue(sheet), Is.EqualTo(new[] { 5, 0, 1, 2, 3, 4 }));
                Undo.PerformUndo(); Assert.That((int[])st.GetField("ColumnOrder").GetValue(sheet), Is.EqualTo(new[] { 0, 1, 2, 3, 4, 5 }));
                Undo.PerformRedo(); Assert.That(((int[])st.GetField("ColumnOrder").GetValue(sheet))[0], Is.EqualTo(5));
                Change(ids[1], 0, true); st.GetField("PlotColumn").SetValue(sheet, 2);
                FPWorkbenchTablePage page = null;
                while (DateTime.UtcNow < deadline) {
                    page = (FPWorkbenchTablePage)type.GetField("virtualPage", flags).GetValue(window);
                    if (page != null && page.Columns.Length == 5 && page.Columns[0] == 5) break; yield return null;
                }
                Assert.That(page, Is.Not.Null); Assert.That(page.Columns, Is.EqualTo(new[] { 5, 0, 2, 3, 4 })); Assert.That(page.Values[0][0].Text, Is.EqualTo("6"));
                window.Repaint(); yield return null;
                var rect = (Rect)type.GetField("tableRect", flags).GetValue(window);
                var point = new Vector2(rect.x + layout.RowHeaderWidth + 10, rect.y + 30);
                window.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = point });
                window.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = point }); yield return null;
                Assert.That((int)type.GetField("selectedColumn", flags).GetValue(window), Is.EqualTo(5));
                Assert.That((string)type.GetField("selectedOriginal", flags).GetValue(window), Is.EqualTo("6"));
                type.GetMethod("SelectTableRange", flags).Invoke(window, new object[] { sheet, 1L, 2, true });
                var selection = (FPWorkbenchTableSelection)st.GetField("Selection").GetValue(sheet);
                Assert.That(selection.ColumnIds, Is.EqualTo(new[] { ids[5], ids[0], ids[2] })); Assert.That(selection.EndRow, Is.EqualTo(2));
                var resources = type.Assembly.GetType("FuzzPhyte.Utility.Analytics.WorkBench.Editor.FPWorkbenchEditorResources");
                var scans = (FPWorkbenchMemoryBudget)resources.GetField("Scans", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                Assert.That(scans.TryAcquire(1, out var firstScan), Is.True);
                Assert.That(scans.TryAcquire(1, out var secondScan), Is.True);
                string refusedOutput = Path.Combine(root, "refused.fpexport");
                string beforeCopy = EditorGUIUtility.systemCopyBuffer;
                try {
                    type.GetMethod("BeginClipboard", flags).Invoke(window, new object[] { sheet });
                    type.GetMethod("BeginTableSelectionExport", flags).Invoke(window, new object[] { sheet, refusedOutput });
                    Assert.That(type.GetField("clipboardWork", flags).GetValue(window), Is.Null);
                    Assert.That(type.GetField("exportWork", flags).GetValue(window), Is.Null);
                    Assert.That(EditorGUIUtility.systemCopyBuffer, Is.EqualTo(beforeCopy));
                    Assert.That(Directory.Exists(refusedOutput), Is.False);
                    Assert.That(scans.ActiveWorkers, Is.EqualTo(2));
                } finally { firstScan.Dispose(); secondScan.Dispose(); }
                type.GetField("clipboardHeaders", flags).SetValue(window, false); type.GetMethod("BeginClipboard", flags).Invoke(window, new object[] { sheet });
                while (type.GetField("clipboardWork", flags).GetValue(window) != null && DateTime.UtcNow < deadline) yield return null;
                Assert.That(EditorGUIUtility.systemCopyBuffer, Is.EqualTo("6\t1\t3\n60\t10\t30\n"));
                var document = (FPWorkbenchDocument)type.GetMethod("CaptureWorkbook", flags).Invoke(window, null);
                string workbook = Path.Combine(root, "layout.fpworkbook"); var save = FPWorkbenchDocumentFile.SaveAsync(document, workbook);
                while (!save.IsCompleted) yield return null; save.GetAwaiter().GetResult();
                var load = FPWorkbenchDocumentFile.LoadAsync(workbook); while (!load.IsCompleted) yield return null;
                Assert.That(load.GetAwaiter().GetResult().Document.Sheets[0].Columns.Projection.Resolve(ids), Is.EqualTo(new[] { 5, 0, 2, 3, 4 }));
                Assert.That(load.Result.Document.Sheets[0].Selection.ColumnIds, Is.EqualTo(selection.ColumnIds));
                string selectedOutput = Path.Combine(root, "selected.fpexport");
                type.GetMethod("BeginTableSelectionExport", flags).Invoke(window, new object[] { sheet, selectedOutput });
                selection.FirstRow = 1; // Later navigation/selection changes must not retarget an already requested export.
                while (type.GetField("exportWork", flags).GetValue(window) != null && DateTime.UtcNow < deadline) yield return null;
                Assert.That(File.Exists(Path.Combine(selectedOutput, "data.csv")), Is.True);
                var provenance = JsonUtility.FromJson<FPWorkbenchCsvExportResult>(File.ReadAllText(Path.Combine(selectedOutput, "provenance.json")));
                Assert.That(provenance.ColumnIds, Is.EqualTo(selection.ColumnIds)); Assert.That(provenance.FirstRow, Is.EqualTo(0)); Assert.That(provenance.EndRow, Is.EqualTo(2));
                var exported = FPWorkbenchCsvDataset.ImportAsync(Path.Combine(selectedOutput, "data.csv"), Path.Combine(root, "selection-cache"), new FPWorkbenchCsvProfile());
                while (!exported.IsCompleted) yield return null;
                var exportedPage = exported.GetAwaiter().GetResult().ReadPageAsync(0, 2, 0, 3); while (!exportedPage.IsCompleted) yield return null;
                Assert.That(exportedPage.GetAwaiter().GetResult().Values[0], Is.EqualTo(new[] { "6", "1", "3" }));
                Assert.That(exportedPage.Result.Values[1], Is.EqualTo(new[] { "60", "10", "30" }));

                type.GetMethod("StartSeries", flags).Invoke(window, new object[] { sheet, true });
                while (st.GetField("Series").GetValue(sheet) == null && DateTime.UtcNow < deadline) yield return null;
                var series = (FPWorkbenchSeriesCache)st.GetField("Series").GetValue(sheet); Assert.That(series, Is.Not.Null);
                var plotSettings = (FPWorkbenchPlotSettings)st.GetField("Plot").GetValue(sheet);
                plotSettings.StartX = -10; plotSettings.EndX = 10; plotSettings.LockY = true; plotSettings.MinimumY = -50; plotSettings.MaximumY = 100; plotSettings.InvertY = true;
                var frameBudget = (FPWorkbenchMemoryBudget)resources.GetField("Memory", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                Assert.That(frameBudget.TryAcquire(frameBudget.LimitBytes - frameBudget.UsedBytes, out var frameBlocker), Is.True);
                try {
                    type.GetMethod("BeginSelectionFrame", flags).Invoke(window, new object[] { sheet });
                    Assert.That(type.GetField("pendingSelectionFrame", flags).GetValue(window), Is.Not.Null);
                    Assert.That(type.GetField("selectionFrameWork", flags).GetValue(window), Is.Null);
                    Assert.That(plotSettings.StartX, Is.EqualTo(-10)); Assert.That(plotSettings.EndX, Is.EqualTo(10));
                } finally { frameBlocker.Dispose(); }
                while ((type.GetField("selectionFrameWork", flags).GetValue(window) != null || type.GetField("pendingSelectionFrame", flags).GetValue(window) != null) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(plotSettings.StartX, Is.EqualTo(1)); Assert.That(plotSettings.EndX, Is.EqualTo(2));
                Assert.That(plotSettings.MinimumY, Is.EqualTo(-50)); Assert.That(plotSettings.MaximumY, Is.EqualTo(100)); Assert.That(plotSettings.InvertY, Is.True);
                Assert.That(series.Binding.YColumn, Is.EqualTo(5));
                type.GetMethod("BeginSelectionFrame", flags).Invoke(window, new object[] { sheet }); plotSettings.StartX = 55; plotSettings.EndX = 65;
                while (type.GetField("selectionFrameWork", flags).GetValue(window) != null && DateTime.UtcNow < deadline) yield return null;
                Assert.That(plotSettings.StartX, Is.EqualTo(55)); Assert.That(plotSettings.EndX, Is.EqualTo(65));
                Assert.That((bool)type.GetMethod("RequestFigureRow", flags).Invoke(window, new object[] { data.DatasetId, 1L, new[] { 1 } }), Is.True);
                Assert.That(layout.Projection.Hidden, Is.Empty); Assert.That((int)st.GetField("FirstColumn").GetValue(sheet), Is.EqualTo(2));
                Assert.That((long)st.GetField("FirstRow").GetValue(sheet), Is.EqualTo(1));
                Assert.That(((FPWorkbenchSeriesBinding)st.GetField("Binding").GetValue(sheet)).YColumn, Is.EqualTo(document.Sheets[0].Binding.YColumn));
                Undo.IncrementCurrentGroup();
                type.GetMethod("ChangeFrozenColumn", flags).Invoke(window, new object[] { sheet, ids[0], true });
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); Assert.That(layout.Projection.Frozen, Is.Empty);
                Undo.PerformRedo(); Assert.That(layout.Projection.Frozen, Is.EqualTo(new[] { ids[0] }));
                type.GetMethod("ChangeFrozenColumn", flags).Invoke(window, new object[] { sheet, ids[4], true });
                st.GetField("FirstColumn").SetValue(sheet, 1); window.Repaint();
                deadline = DateTime.UtcNow.AddSeconds(10);
                while (DateTime.UtcNow < deadline) {
                    page = (FPWorkbenchTablePage)type.GetField("virtualPage", flags).GetValue(window);
                    if (page != null && page.Columns.Length == 5 && page.Columns[0] == 0 && page.Columns[2] == 1) break;
                    yield return null;
                }
                Assert.That(page.Columns, Is.EqualTo(new[] { 0, 4, 1, 2, 3 }));
                Assert.That(page.Values[0][0].Text, Is.EqualTo("10")); Assert.That(page.Values[0][1].Text, Is.EqualTo("50"));
                type.GetMethod("SelectTableRange", flags).Invoke(window, new object[] { sheet, 1L, 0, false });
                type.GetMethod("SelectTableRange", flags).Invoke(window, new object[] { sheet, 1L, 2, true });
                Assert.That(((FPWorkbenchTableSelection)st.GetField("Selection").GetValue(sheet)).ColumnIds, Is.EqualTo(new[] { ids[0], ids[4], ids[1], ids[2] }));
                var widths = (float[])type.GetMethod("FitTableColumns", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { sheet, 300f });
                Assert.That(widths.Length, Is.GreaterThan(2)); Assert.That(widths[0] + widths[1], Is.LessThanOrEqualTo(150.01f));
                var frozenDocument = (FPWorkbenchDocument)type.GetMethod("CaptureWorkbook", flags).Invoke(window, null);
                var frozenSave = FPWorkbenchDocumentFile.SaveAsync(frozenDocument, Path.Combine(root, "frozen.fpworkbook"));
                while (!frozenSave.IsCompleted) yield return null; frozenSave.GetAwaiter().GetResult();
                var frozenLoad = FPWorkbenchDocumentFile.LoadAsync(Path.Combine(root, "frozen.fpworkbook"));
                while (!frozenLoad.IsCompleted) yield return null;
                Assert.That(frozenLoad.GetAwaiter().GetResult().Document.Sheets[0].Columns.Projection.Frozen, Is.EqualTo(new[] { ids[0], ids[4] }));
                Assert.That((bool)type.GetMethod("RequestFigureRow", flags).Invoke(window, new object[] { data.DatasetId, 0L, new[] { 4, 3 } }), Is.True);
                Assert.That(st.GetField("FirstColumn").GetValue(sheet), Is.EqualTo(3), "Pinned columns do not count toward scrolling offsets.");
                type.GetMethod("PreserveWorkingMetadata", flags).Invoke(window, null);
                string reloadJson = EditorJsonUtility.ToJson(window);
                window.Close(); UnityEngine.Object.DestroyImmediate(window); window = null;
                restored = ScriptableObject.CreateInstance<FPDataWorkbenchWindow>();
                type.GetMethod("OnDisable", flags).Invoke(restored, null);
                EditorJsonUtility.FromJsonOverwrite(reloadJson, restored);
                type.GetMethod("OnEnable", flags).Invoke(restored, null); restored.ShowUtility(); restored.position = new Rect(20, 20, 1350, 750);
                deadline = DateTime.UtcNow.AddSeconds(15);
                while (DateTime.UtcNow < deadline) {
                    page = (FPWorkbenchTablePage)type.GetField("virtualPage", flags).GetValue(restored);
                    if (page != null && page.Columns.Length == 3 && page.Columns[0] == 0 && page.Columns[2] == 3) break;
                    yield return null;
                }
                Assert.That(page, Is.Not.Null); Assert.That(page.Columns, Is.EqualTo(new[] { 0, 4, 3 }));
                Assert.That(page.Values[0][0].Text, Is.EqualTo("1")); Assert.That(page.Values[0][1].Text, Is.EqualTo("5"));
                var restoredSheet = ((IList)type.GetField("sheets", flags).GetValue(restored))[0];
                var restoredLayout = (FPWorkbenchColumnLayout)st.GetField("Columns").GetValue(restoredSheet);
                Assert.That(restoredLayout.Projection.Frozen, Is.EqualTo(new[] { ids[0], ids[4] }));
                Assert.That(st.GetField("FirstColumn").GetValue(restoredSheet), Is.EqualTo(3));
            } finally {
                EditorGUIUtility.systemCopyBuffer = originalClipboard;
                if (window != null) { window.Close(); UnityEngine.Object.DestroyImmediate(window); }
                if (restored != null) { restored.Close(); UnityEngine.Object.DestroyImmediate(restored); }
                if (preference != null) EditorPrefs.DeleteKey(preference);
                if (cache != null && Directory.Exists(cache)) Directory.Delete(cache, true);
                Directory.Delete(root, true);
            }
        }
    }
}
