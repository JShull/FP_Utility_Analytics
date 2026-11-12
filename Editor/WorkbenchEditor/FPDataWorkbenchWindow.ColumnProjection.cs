// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using UnityEditor;
    using UnityEngine;
    public sealed partial class FPDataWorkbenchWindow
    {
        private static int VisibleColumnCount(Sheet sheet) => sheet.ColumnOrder?.Length ?? DisplayColumnCount(sheet);
        private static int SchemaColumn(Sheet sheet, int position) => sheet.ColumnOrder == null ? position : sheet.ColumnOrder[position];
        private static bool UsesProjection(Sheet sheet)
        {
            if (sheet.ColumnOrder == null) return false;
            if (sheet.ColumnOrder.Length != DisplayColumnCount(sheet)) return true;
            for (int c = 0; c < sheet.ColumnOrder.Length; c++) if (sheet.ColumnOrder[c] != c) return true;
            return false;
        }
        private bool PageColumnsMatch(Sheet sheet, FPWorkbenchTablePage candidate)
        {
            int count = Math.Min(visibleColumns, VisibleColumnCount(sheet) - sheet.FirstColumn);
            if (candidate.Columns == null || candidate.Columns.Length != count) return false;
            for (int c = 0; c < count; c++) if (candidate.Columns[c] != SchemaColumn(sheet, sheet.FirstColumn + c)) return false;
            return true;
        }
        private void ApplyColumnProjection(Sheet sheet, FPWorkbenchColumnProjection projection)
        {
            projection.BindSchema(sheet.SchemaIds); sheet.Columns.Projection = projection; sheet.ColumnOrder = projection.Resolve(sheet.SchemaIds);
            sheet.FirstColumn = Math.Min(sheet.FirstColumn, sheet.ColumnOrder.Length - 1); SaveColumns(sheet);
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
            if (position > 0) menu.AddItem(new GUIContent("Move column left"), false, () => ChangeColumnProjection(sheet, id, -1, null)); else menu.AddDisabledItem(new GUIContent("Move column left"));
            if (position + 1 < sheet.ColumnOrder.Length) menu.AddItem(new GUIContent("Move column right"), false, () => ChangeColumnProjection(sheet, id, 1, null)); else menu.AddDisabledItem(new GUIContent("Move column right"));
            if (sheet.ColumnOrder.Length > 1) menu.AddItem(new GUIContent("Hide column"), false, () => ChangeColumnProjection(sheet, id, 0, true)); else menu.AddDisabledItem(new GUIContent("Hide column"));
            menu.ShowAsContext(); input.Use();
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
