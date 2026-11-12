// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using UnityEditor;
    using UnityEngine;
    public sealed partial class FPDataWorkbenchWindow
    {
        private static int VisibleColumnCount(Sheet sheet) => sheet.ColumnOrder?.Length ?? DisplayColumnCount(sheet);
        private static int FrozenCount(Sheet sheet) => sheet.Columns.Projection?.Frozen?.Length ?? 0;
        private static int ScrollingCount(Sheet sheet) => VisibleColumnCount(sheet) - FrozenCount(sheet);
        private static int[] WindowOrder(Sheet sheet)
        {
            if (FrozenCount(sheet) == 0) return sheet.ColumnOrder;
            if (sheet.FrozenViewport == null || sheet.FrozenViewportFirst != sheet.FirstColumn || !ReferenceEquals(sheet.FrozenViewportOrder, sheet.ColumnOrder) ||
                !ReferenceEquals(sheet.FrozenViewportIds, sheet.Columns.Projection.Frozen)) {
                sheet.FrozenViewport = sheet.Columns.Projection.ResolveWindow(sheet.SchemaIds, sheet.FirstColumn, 32);
                sheet.FrozenViewportFirst = sheet.FirstColumn; sheet.FrozenViewportOrder = sheet.ColumnOrder; sheet.FrozenViewportIds = sheet.Columns.Projection.Frozen;
            }
            return sheet.FrozenViewport;
        }
        private static int WindowCount(Sheet sheet, int count) => Math.Min(count, FrozenCount(sheet) == 0 ? VisibleColumnCount(sheet) - sheet.FirstColumn : WindowOrder(sheet).Length);
        private static int SchemaColumn(Sheet sheet, int position) => FrozenCount(sheet) > 0 ? WindowOrder(sheet)[position - sheet.FirstColumn] : sheet.ColumnOrder == null ? position : sheet.ColumnOrder[position];
        private static float[] FitTableColumns(Sheet sheet, float available)
        {
            int frozen = FrozenCount(sheet);
            if (frozen == 0) return sheet.Columns.Fit(available, sheet.FirstColumn, order: sheet.ColumnOrder);
            int[] order = WindowOrder(sheet); var widths = new float[frozen]; float used = 0;
            for (int c = 0; c < frozen; c++) { widths[c] = sheet.Columns.ManualWidths[order[c]] > 0 ? sheet.Columns.ManualWidths[order[c]] : 110; used += widths[c]; }
            // Frozen identifiers share at most half the width while scrolling data remains reachable.
            float budget = ScrollingCount(sheet) > 0 ? available * .5f : available;
            float scale = used > budget ? budget / used : 1;
            for (int c = 0; c < frozen; c++) widths[c] *= scale;
            used *= scale;
            if (order.Length == frozen) return widths;
            int[] scrolling = new int[order.Length - frozen]; Array.Copy(order, frozen, scrolling, 0, scrolling.Length);
            float[] tail = sheet.Columns.Fit(Math.Max(1, available - used), 0, maximumVisible: 32 - frozen, order: scrolling);
            Array.Resize(ref widths, frozen + tail.Length); Array.Copy(tail, 0, widths, frozen, tail.Length); return widths;
        }
        private static bool UsesProjection(Sheet sheet)
        {
            if (sheet.ColumnOrder == null) return false;
            if (FrozenCount(sheet) > 0) return true;
            if (sheet.ColumnOrder.Length != DisplayColumnCount(sheet)) return true;
            for (int c = 0; c < sheet.ColumnOrder.Length; c++) if (sheet.ColumnOrder[c] != c) return true;
            return false;
        }
        private bool PageColumnsMatch(Sheet sheet, FPWorkbenchTablePage candidate)
        {
            int count = WindowCount(sheet, visibleColumns);
            if (candidate.Columns == null || candidate.Columns.Length != count) return false;
            for (int c = 0; c < count; c++) if (candidate.Columns[c] != SchemaColumn(sheet, sheet.FirstColumn + c)) return false;
            return true;
        }
        private void ApplyColumnProjection(Sheet sheet, FPWorkbenchColumnProjection projection)
        {
            projection.BindSchema(sheet.SchemaIds); sheet.Columns.Projection = projection; sheet.ColumnOrder = projection.Resolve(sheet.SchemaIds);
            sheet.FirstColumn = Math.Min(sheet.FirstColumn, Math.Max(0, ScrollingCount(sheet) - 1)); SaveColumns(sheet);
            if (sheet == Current) { CancelVirtualPage(); page = null; failedPage = null; }
            Repaint();
        }
        private void ChangeColumnProjection(Sheet sheet, string id, int move, bool? hidden)
        {
            if (!sheets.Contains(sheet)) return;
            try {
                EnsureTableSchema(sheet);
                string before = JsonUtility.ToJson(sheet.Columns.Projection);
                var next = JsonUtility.FromJson<FPWorkbenchColumnProjection>(before);
                if (hidden.HasValue) next.SetHidden(id, hidden.Value);
                else if (id == null) { next.Order = (string[])sheet.SchemaIds.Clone(); }
                else {
                    int visible = Array.IndexOf(sheet.ColumnOrder, Array.IndexOf(sheet.SchemaIds, id));
                    int other = Mathf.Clamp(visible + move, 0, sheet.ColumnOrder.Length - 1);
                    next.Move(id, Array.IndexOf(next.Order, sheet.SchemaIds[sheet.ColumnOrder[other]]));
                }
                sheet.ProjectionUndo ??= new FPWorkbenchColumnProjectionUndo(value => { if (sheets.Contains(sheet)) ApplyColumnProjection(sheet, value); });
                sheet.ProjectionUndo.Record(before, JsonUtility.ToJson(next)); ApplyColumnProjection(sheet, next);
            } catch (Exception ex) { error = ex.Message; }
        }
        private void ColumnHeaderMenu(Sheet sheet, int column, Rect rect)
        {
            var input = Event.current; if (input.type != EventType.ContextClick || !rect.Contains(input.mousePosition)) return;
            var menu = new GenericMenu(); string id = sheet.SchemaIds[column]; int position = Array.IndexOf(sheet.ColumnOrder, column);
            bool frozen = Array.IndexOf(sheet.Columns.Projection.Frozen, id) >= 0;
            if (frozen || FrozenCount(sheet) < FPWorkbenchColumnProjection.MaximumFrozenColumns)
                menu.AddItem(new GUIContent(frozen ? "Unfreeze column" : "Freeze identifier column"), false, () => ChangeFrozenColumn(sheet, id, !frozen));
            else menu.AddDisabledItem(new GUIContent("Freeze identifier column (eight maximum)"));
            if (position > 0) menu.AddItem(new GUIContent("Move column left"), false, () => ChangeColumnProjection(sheet, id, -1, null)); else menu.AddDisabledItem(new GUIContent("Move column left"));
            if (position + 1 < sheet.ColumnOrder.Length) menu.AddItem(new GUIContent("Move column right"), false, () => ChangeColumnProjection(sheet, id, 1, null)); else menu.AddDisabledItem(new GUIContent("Move column right"));
            if (sheet.ColumnOrder.Length > 1) menu.AddItem(new GUIContent("Hide column"), false, () => ChangeColumnProjection(sheet, id, 0, true)); else menu.AddDisabledItem(new GUIContent("Hide column"));
            menu.ShowAsContext(); input.Use();
        }
        private void ChangeFrozenColumn(Sheet sheet, string id, bool frozen)
        {
            if (!sheets.Contains(sheet)) return;
            try {
                string before = JsonUtility.ToJson(sheet.Columns.Projection);
                var next = JsonUtility.FromJson<FPWorkbenchColumnProjection>(before); next.SetFrozen(id, frozen);
                sheet.ProjectionUndo ??= new FPWorkbenchColumnProjectionUndo(value => { if (sheets.Contains(sheet)) ApplyColumnProjection(sheet, value); });
                sheet.ProjectionUndo.Record(before, JsonUtility.ToJson(next)); ApplyColumnProjection(sheet, next);
            } catch (Exception ex) { error = ex.Message; }
        }
        private void ShowColumnVisibility(Sheet sheet)
        {
            var menu = new GenericMenu(); menu.AddItem(new GUIContent("Reset display order"), false, () => ChangeColumnProjection(sheet, null, 0, null));
            foreach (string hiddenId in sheet.Columns.Projection.Hidden) {
                string id = hiddenId; int column = Array.IndexOf(sheet.SchemaIds, id);
                menu.AddItem(new GUIContent("Show/" + (column + 1) + ": " + sheet.PageTable.ColumnName(column).Replace('/', '∕')), false, () => ChangeColumnProjection(sheet, id, 0, false));
            }
            if (sheet.Columns.Projection.Hidden.Length == 0) menu.AddDisabledItem(new GUIContent("No hidden columns"));
            menu.ShowAsContext();
        }
    }
}
