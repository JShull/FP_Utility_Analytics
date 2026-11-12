// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using UnityEditor;
    using UnityEngine;
    public sealed partial class FPDataWorkbenchWindow
    {
        [SerializeField] private bool clipboardHeaders = true;
        private Task<string> clipboardWork;
        private FPWorkbenchEditorResources.ScanReservation clipboardMemory;
        private CancellationTokenSource clipboardCancellation;
        private void SelectTableRange(Sheet sheet, long row, int column, bool extend)
        {
            try {
                EnsureTableSchema(sheet);
                string id = sheet.SchemaIds[column]; long first = row, end = row + 1; string[] columns;
                if (!extend || sheet.Selection == null || string.IsNullOrEmpty(sheet.RangeAnchorColumn)) {
                    sheet.RangeAnchorRow = row; sheet.RangeAnchorColumn = id; columns = new[] { id };
                } else {
                    int anchorSchema = Array.IndexOf(sheet.SchemaIds, sheet.RangeAnchorColumn);
                    int[] rangeOrder = FrozenCount(sheet) > 0 ? WindowOrder(sheet) : sheet.ColumnOrder;
                    int anchor = Array.IndexOf(rangeOrder, anchorSchema), current = Array.IndexOf(rangeOrder, column);
                    if (anchor < 0 || current < 0) throw new InvalidOperationException("The range anchor is hidden or removed. Click a visible cell to start a new selection.");
                    int left = Math.Min(anchor, current), count = Math.Abs(anchor - current) + 1;
                    if (count > 32) throw new InvalidOperationException("Select at most 32 columns for a table range.");
                    columns = new string[count]; for (int c = 0; c < count; c++) columns[c] = sheet.SchemaIds[rangeOrder[left + c]];
                    first = Math.Min(row, sheet.RangeAnchorRow); end = Math.Max(row, sheet.RangeAnchorRow) + 1;
                }
                var next = new FPWorkbenchTableSelection { DatasetId = sheet.Dataset.DatasetId, FirstRow = first, EndRow = end, ColumnIds = columns };
                next.Resolve(sheet.PageTable); sheet.Selection = next; Repaint();
            } catch (Exception ex) { error = ex.Message; }
        }
        private void DrawTableSelection(Sheet sheet, long row, int column, Rect rect)
        {
            var selection = sheet.Selection;
            if (selection != null && selection.DatasetId == sheet.Dataset.DatasetId && row >= selection.FirstRow && row < selection.EndRow &&
                Array.IndexOf(selection.ColumnIds, sheet.SchemaIds[column]) >= 0) EditorGUI.DrawRect(rect, new Color(.3f, .55f, 1, .22f));
        }
        private void BeginClipboard(Sheet sheet)
        {
            if (clipboardWork != null || sheet?.Selection == null) return;
            try {
                EnsureTableSchema(sheet);
                if (!FPWorkbenchEditorResources.TryAcquireScan(FPWorkbenchEditorResources.SourceBytes(sheet.Dataset, 12L * 1024 * 1024), out clipboardMemory))
                    throw new InvalidOperationException("Shared copy memory/background capacity is busy; retry when current work finishes.");
                clipboardCancellation = new CancellationTokenSource();
                clipboardWork = FPWorkbenchTableClipboard.BuildAsync(sheet.PageTable, sheet.Selection, clipboardHeaders,
                    sheet.ShowOriginal ? null : SnapshotFor(sheet.Dataset.DatasetId), clipboardCancellation.Token);
                status = "Preparing bounded " + (sheet.ShowOriginal ? "Original" : "Effective") + " range copy in background…";
            } catch (Exception ex) { error = ex.Message; CancelClipboard(); }
        }
        private void CancelClipboard()
        {
            Cancel(clipboardCancellation); clipboardCancellation = null;
            if (clipboardMemory != null) clipboardMemory.ReleaseAfter(clipboardWork); else Observe(clipboardWork);
            clipboardMemory = null; clipboardWork = null;
        }
        private bool TickClipboard()
        {
            if (clipboardWork == null || !clipboardWork.IsCompleted) return false;
            var task = clipboardWork; clipboardWork = null;
            try { EditorGUIUtility.systemCopyBuffer = task.GetAwaiter().GetResult(); status = "Selected range copied as quoted TSV; values/formulas were frozen at the copy request."; }
            catch (OperationCanceledException) { status = "Range copy cancelled; clipboard retained."; }
            catch (Exception ex) { error = "Range copy failed; clipboard retained. " + ex.Message; }
            clipboardMemory?.Dispose(); clipboardMemory = null;
            clipboardCancellation?.Dispose(); clipboardCancellation = null; return true;
        }
        private void DrawRangeToolbar(Sheet sheet)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar)) {
                var selected = sheet.Selection;
                GUILayout.Label(selected == null ? "Click a cell; Shift-click to select a range" : "Selection: rows " + (selected.FirstRow + 1) + "–" + selected.EndRow + " • " + selected.ColumnIds.Length + " columns", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace(); clipboardHeaders = GUILayout.Toggle(clipboardHeaders, "Headers", EditorStyles.toolbarButton, GUILayout.Width(58));
                using (new EditorGUI.DisabledScope(selected == null || clipboardWork != null))
                    if (GUILayout.Button(new GUIContent("Copy TSV", "Copy at most 256 rows / 32 columns / 1 MiB UTF-16 text. Larger selections use materialized CSV export."), EditorStyles.toolbarButton, GUILayout.Width(72))) BeginClipboard(sheet);
                using (new EditorGUI.DisabledScope(selected == null)) if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(42))) { sheet.Selection = null; sheet.RangeAnchorColumn = null; }
                if (clipboardWork != null && GUILayout.Button("Cancel copy", EditorStyles.toolbarButton, GUILayout.Width(80))) CancelClipboard();
            }
            var input = Event.current;
            if (input.type == EventType.KeyDown && input.keyCode == KeyCode.C && (input.control || input.command) && !EditorGUIUtility.editingTextField && sheet.Selection != null) { BeginClipboard(sheet); input.Use(); }
        }
    }
}
