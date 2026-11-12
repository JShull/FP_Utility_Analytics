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
        [UnityTest] public IEnumerator GridProjectionPreservesSourceTargetsUndoNavigationAndWorkbookLayout()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPProjectGrid-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            string originalClipboard = EditorGUIUtility.systemCopyBuffer;
            FPDataWorkbenchWindow window = null; string cache = null, preference = null;
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
                type.GetMethod("BeginSelectionFrame", flags).Invoke(window, new object[] { sheet });
                while (type.GetField("selectionFrameWork", flags).GetValue(window) != null && DateTime.UtcNow < deadline) yield return null;
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
            } finally {
                EditorGUIUtility.systemCopyBuffer = originalClipboard;
                if (window != null) { window.Close(); UnityEngine.Object.DestroyImmediate(window); }
                if (preference != null) EditorPrefs.DeleteKey(preference);
                if (cache != null && Directory.Exists(cache)) Directory.Delete(cache, true);
                Directory.Delete(root, true);
            }
        }
    }
}
