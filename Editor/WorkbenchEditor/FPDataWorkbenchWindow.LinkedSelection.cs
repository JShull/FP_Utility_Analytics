// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using UnityEditor;
    using UnityEngine;

    public sealed partial class FPDataWorkbenchWindow
    {
        [SerializeField] private string linkedDataset;
        [SerializeField] private long linkedRow = -1;
        [SerializeField] private int[] linkedColumns = Array.Empty<int>();
        private string pendingLinkedDataset;
        private long pendingLinkedRow;
        private int[] pendingLinkedColumns;
        internal static bool RevealFigureRow(string owner, string dataset, long row, int[] sourceColumns)
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<FPDataWorkbenchWindow>())
                if (window.workbookId == owner) return window.RequestFigureRow(dataset, row, sourceColumns);
            return false;
        }
        private bool RequestFigureRow(string dataset, long row, int[] columns)
        {
            if (row < 0 || columns == null || columns.Length > 4096) return false;
            var source = registeredSources.Find(s => s.DatasetId == dataset);
            if (source == null) return false;
            int existing = sheets.FindIndex(s => s.Dataset?.DatasetId == dataset);
            if (existing < 0 && sheets.Count >= 8) { error = "Close a sheet before revealing another source (eight open tabs maximum)."; return false; }
            pendingLinkedDataset = dataset; pendingLinkedRow = row; pendingLinkedColumns = (int[])columns.Clone();
            OpenRegisteredSheet(source); TickLinkedSelection(); Focus(); return true;
        }
        private bool TickLinkedSelection()
        {
            if (pendingLinkedDataset == null) return false;
            int index = sheets.FindIndex(s => s.Dataset?.DatasetId == pendingLinkedDataset);
            if (index < 0)
            {
                if (sourceOpening == null) { pendingLinkedDataset = null; pendingLinkedColumns = null; }
                return false;
            }
            var sheet = sheets[index];
            if (pendingLinkedRow >= sheet.Dataset.RowCount) { error = "Linked row is outside the verified source."; pendingLinkedDataset = null; pendingLinkedColumns = null; return true; }
            foreach (int column in pendingLinkedColumns)
                if (column < 0 || column >= sheet.Dataset.ColumnCount) { error = "Linked source column is outside the verified schema."; pendingLinkedDataset = null; pendingLinkedColumns = null; return true; }
            linkedDataset = pendingLinkedDataset; linkedRow = pendingLinkedRow; linkedColumns = pendingLinkedColumns;
            pendingLinkedDataset = null; pendingLinkedColumns = null; active = index;
            EnsureTableSchema(sheet);
            var projection = JsonUtility.FromJson<FPWorkbenchColumnProjection>(JsonUtility.ToJson(sheet.Columns.Projection));
            string before = JsonUtility.ToJson(projection);
            foreach (int column in linkedColumns) projection.SetHidden(sheet.SchemaIds[column], false);
            if (before != JsonUtility.ToJson(projection)) {
                sheet.ProjectionUndo ??= new FPWorkbenchColumnProjectionUndo(value => { if (sheets.Contains(sheet)) ApplyColumnProjection(sheet, value); });
                sheet.ProjectionUndo.Record(before, JsonUtility.ToJson(projection)); ApplyColumnProjection(sheet, projection);
            }
            int first = int.MaxValue; foreach (int column in linkedColumns) first = Math.Min(first, Array.IndexOf(sheet.ColumnOrder, column));
            sheet.FirstRow = linkedRow; sheet.FirstColumn = first == int.MaxValue ? 0 : first;
            selectedDataset = null; page = null; failedPage = null; CancelVirtualPage();
            status = "Figure source row " + (linkedRow + 1) + " highlighted. Table uses its current Original/Effective layer; the figure retains its frozen snapshot.";
            Repaint(); return true;
        }
        private static int Minimum(int[] values) { int result = int.MaxValue; foreach (int value in values) result = Math.Min(result, value); return result; }
        private void DrawLinkedRow(Sheet sheet, long row, Rect rect)
        {
            if (sheet.Dataset.DatasetId == linkedDataset && row == linkedRow) EditorGUI.DrawRect(rect, new Color(.2f, .85f, .95f, .12f));
        }
        private void DrawLinkedCell(Sheet sheet, long row, int column, Rect rect)
        {
            if (sheet.Dataset.DatasetId != linkedDataset || row != linkedRow || Array.IndexOf(linkedColumns, column) < 0) return;
            var color = new Color(.2f, .85f, .95f, .7f);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 2), color); EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 2, rect.width, 2), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 2, rect.height), color); EditorGUI.DrawRect(new Rect(rect.xMax - 2, rect.y, 2, rect.height), color);
        }
    }
}
