// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using UnityEditor;
    using UnityEngine;

    internal sealed class FPWorkbenchColumnDragPayload
    {
        public const string Key = "FuzzPhyte.Workbench.Column.v1";
        public string Workbook, Dataset, ColumnId, DerivedId, FormulaFingerprint;
        public int Column;
    }
    public sealed partial class FPDataWorkbenchWindow
    {
        private const string ColumnDragKey = FPWorkbenchColumnDragPayload.Key;
        private Sheet dragColumnSheet;
        private int dragColumn = -1, dragColumnControl;
        private Vector2 dragColumnStart;
        private string dragStableColumn, dragDerivedId;
        private void ColumnDragInput()
        {
            dragColumnControl = GUIUtility.GetControlID(731659, FocusType.Passive);
            if (dragColumnSheet == null || GUIUtility.hotControl != dragColumnControl) return;
            var e = Event.current;
            if (e.type == EventType.MouseDrag && (e.mousePosition - dragColumnStart).sqrMagnitude >= 16)
            {
                var data = dragColumnSheet.Dataset;
                bool derived = !string.IsNullOrEmpty(dragDerivedId);
                int current = derived ? DerivedDropColumn(dragColumnSheet, dragStableColumn, dragDerivedId) : dragColumn;
                if (current < 0 || current >= DisplayColumnCount(dragColumnSheet))
                { GUIUtility.hotControl = 0; dragColumnSheet = null; dragColumn = -1; status = "Column is no longer available to drag."; e.Use(); return; }
                DragAndDrop.PrepareStartDrag(); DragAndDrop.objectReferences = Array.Empty<UnityEngine.Object>();
                DragAndDrop.SetGenericData(ColumnDragKey, new FPWorkbenchColumnDragPayload { Workbook = workbookId,
                    Dataset = data.DatasetId, Column = current, ColumnId = dragStableColumn, DerivedId = dragDerivedId,
                    FormulaFingerprint = derived ? dragColumnSheet.TableProgram.Fingerprint : null });
                DragAndDrop.StartDrag(derived ? dragColumnSheet.Table.ColumnName(current) : data.GetColumnName(current));
                GUIUtility.hotControl = 0; dragColumnSheet = null; dragColumn = -1; e.Use();
            }
            else if (e.type == EventType.MouseUp || e.type == EventType.Ignore || e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            { GUIUtility.hotControl = 0; dragColumnSheet = null; dragColumn = -1; }
        }
        private void ColumnDragHeader(Sheet sheet, int column, Rect rect)
        {
            var e = Event.current;
            if (e.type != EventType.MouseDown || e.button != 0 || !rect.Contains(e.mousePosition) || GUIUtility.hotControl != 0) return;
            dragColumnSheet = sheet; dragColumn = column; dragColumnStart = e.mousePosition;
            dragStableColumn = column >= sheet.Dataset.ColumnCount ? sheet.Table.ColumnId(column) : new FPWorkbenchCell(sheet.Dataset.DatasetId, 0, column).ColumnId;
            dragDerivedId = column >= sheet.Dataset.ColumnCount ? sheet.TableProgram.ColumnId(column - sheet.Dataset.ColumnCount) : "";
            GUIUtility.hotControl = dragColumnControl; e.Use();
        }
        private bool StageColumnDrop(Sheet sheet, string workbook, string dataset, string columnId, int column, bool x)
        {
            if (sheet?.Dataset == null || workbook != workbookId || dataset != sheet.Dataset.DatasetId || column < 0 ||
                column >= sheet.Dataset.ColumnCount || columnId != new FPWorkbenchCell(sheet.Dataset.DatasetId, 0, column).ColumnId) return false;
            sheet.Binding ??= new FPWorkbenchSeriesBinding();
            EnsurePlotUndo(sheet);
            if (x) { bool sameUtc = sheet.Binding.XMode == FPWorkbenchXMode.UtcDateTime && sheet.Binding.XColumn == column; if (!sameUtc) sheet.Binding.XMode = FPWorkbenchXMode.Numeric; sheet.Binding.XColumn = column; sheet.Binding.XDerivedId = ""; }
            else { sheet.PlotColumn = column; sheet.Binding.YDerivedId = ""; }
            StartSeries(sheet, true);
            if (error == null) status = "Column assigned to " + (x ? "X" : "Y") + "; updating plot…";
            Repaint(); return true;
        }
        private static int DerivedDropColumn(Sheet sheet, string columnId, string derivedId)
        {
            if (sheet?.TableProgram == null || string.IsNullOrEmpty(derivedId)) return -1;
            for (int c = 0; c < sheet.TableProgram.ColumnCount; c++)
                if (sheet.TableProgram.ColumnId(c) == derivedId)
                { int column = sheet.Dataset.ColumnCount + c; return sheet.Table.ColumnId(column) == columnId ? column : -1; }
            return -1;
        }
        private bool StageDerivedColumnDrop(Sheet sheet, string workbook, string dataset, string columnId, string derivedId, bool x)
        {
            if (sheet?.Dataset == null || workbook != workbookId || dataset != sheet.Dataset.DatasetId) return false;
            EnsureTableSchema(sheet); int column = DerivedDropColumn(sheet, columnId, derivedId);
            if (column < 0) return false;
            EnsurePlotUndo(sheet);
            if (x) { sheet.Binding.XMode = FPWorkbenchXMode.Numeric; sheet.Binding.XColumn = 0; sheet.Binding.XDerivedId = derivedId; }
            else { sheet.PlotColumn = column; sheet.Binding.YDerivedId = derivedId; }
            StartSeries(sheet, true);
            if (error == null) status = "Calculated column assigned to " + (x ? "numeric X" : "Y") + "; updating plot…"; Repaint(); return true;
        }
        internal void DrawPlotColumnDrops(string sheetId, Rect rect)
        {
            var sheet = sheets.Find(s => s.Id == sheetId); if (sheet?.Dataset != null) DrawColumnDropTargets(sheet, rect);
        }
        private void DrawColumnDropTargets(Sheet sheet, Rect plot)
        {
            if (!(DragAndDrop.GetGenericData(ColumnDragKey) is FPWorkbenchColumnDragPayload payload)) return;
            var e = Event.current;
            if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform && e.type != EventType.Repaint) return;
            if (!plot.Contains(e.mousePosition)) return;
            Rect x = new Rect(plot.x + 8, plot.y + 30, Math.Max(40, (plot.width - 24) / 2), 30);
            Rect y = new Rect(x.xMax + 8, x.y, x.width, x.height);
            GUI.Box(x, "Assign numeric X"); GUI.Box(y, "Assign Y");
            if (e.type == EventType.Repaint) return;
            bool derived = !string.IsNullOrEmpty(payload.DerivedId);
            bool accepted = payload.Workbook == workbookId && payload.Dataset == sheet.Dataset.DatasetId && (derived
                ? DerivedDropColumn(sheet, payload.ColumnId, payload.DerivedId) >= 0
                : payload.Column >= 0 && payload.Column < sheet.Dataset.ColumnCount && payload.ColumnId == sheet.Dataset.Cell(0, payload.Column).ColumnId);
            bool hitX = x.Contains(e.mousePosition), hitY = y.Contains(e.mousePosition);
            DragAndDrop.visualMode = accepted && (hitX || hitY) ? DragAndDropVisualMode.Link : DragAndDropVisualMode.Rejected;
            if (e.type == EventType.DragPerform)
            {
                if (accepted && (hitX || hitY))
                {
                    DragAndDrop.AcceptDrag();
                    if (derived) StageDerivedColumnDrop(sheet, payload.Workbook, payload.Dataset, payload.ColumnId, payload.DerivedId, hitX);
                    else StageColumnDrop(sheet, payload.Workbook, payload.Dataset, payload.ColumnId, payload.Column, hitX);
                }
                else error = "Drop onto X/Y for this source. Cross-sheet alignment is not configured yet.";
                DragAndDrop.SetGenericData(ColumnDragKey, null);
            }
            e.Use(); Repaint();
        }
    }
}
