// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;

    public sealed partial class FPDataWorkbenchWindow
    {
        private void DrawRowRangeOverlay(Sheet sheet, long row, Rect rect)
        {
            if (!sheet.Binding.Rows.Contains(row)) EditorGUI.DrawRect(rect, new Color(.1f, .1f, .1f, .5f));
        }
        private void DrawRowRangeControls(Sheet sheet)
        {
            SidebarSection("Row Range");
            var rows = sheet.Binding.Rows; long count = sheet.Dataset.RowCount;
            if (sheet.RowRangeDraftEnd == 0) { sheet.RowRangeDraftFirst = rows.First(count) + 1; sheet.RowRangeDraftEnd = rows.End(count); }
            EditorGUILayout.LabelField("Used by plots", rows.Enabled ? (rows.FirstRow + 1).ToString("N0") + " - " + rows.EndRow.ToString("N0") : "All source rows");
            sheet.RowRangeDraftFirst = EditorGUILayout.LongField("First row (inclusive)", sheet.RowRangeDraftFirst);
            sheet.RowRangeDraftEnd = EditorGUILayout.LongField("Last row (inclusive)", sheet.RowRangeDraftEnd);
            using (new EditorGUI.DisabledScope(count == 0))
            using (new EditorGUILayout.HorizontalScope()) {
                if (GUILayout.Button("Apply row range")) {
                    if (sheet.RowRangeDraftFirst < 1) error = "First row must be at least 1, matching the table.";
                    else ApplyRowRange(sheet, new FPWorkbenchRowRange { Enabled = true, FirstRow = sheet.RowRangeDraftFirst - 1, EndRow = sheet.RowRangeDraftEnd });
                }
                if (GUILayout.Button("Use all rows")) ApplyRowRange(sheet, new FPWorkbenchRowRange());
            }
            EditorGUILayout.HelpBox("Row numbers match the table. Figures from this sheet inherit the span; excluded rows stay inspectable and appear dimmed. Previously unlinked figures for this source inherit on first Apply.", MessageType.Info);
        }
        private void DrawRowRangeTableControls(Sheet sheet)
        {
            var rows = sheet.Binding.Rows; long count = sheet.Dataset.RowCount;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar)) {
                string text = rows.Enabled ? "Plot rows: " + (rows.FirstRow + 1).ToString("N0") + " - " + rows.EndRow.ToString("N0") + " / " + count.ToString("N0") + " (outside dimmed)" : "Plot rows: all " + count.ToString("N0");
                GUILayout.Label(new GUIContent(text, "Source rows remain visible. Change the plotting span in Row Range after Corrections, or nudge an active boundary here."), EditorStyles.miniLabel, GUILayout.MinWidth(0), GUILayout.ExpandWidth(true));
                if (rows.Enabled) {
                    if (GUILayout.Button(new GUIContent("F-", "Include one earlier row"), EditorStyles.toolbarButton, GUILayout.Width(28))) NudgeRowRange(sheet, -1, 0);
                    if (GUILayout.Button(new GUIContent("F+", "Start one row later"), EditorStyles.toolbarButton, GUILayout.Width(28))) NudgeRowRange(sheet, 1, 0);
                    if (GUILayout.Button(new GUIContent("L-", "End one row earlier"), EditorStyles.toolbarButton, GUILayout.Width(28))) NudgeRowRange(sheet, 0, -1);
                    if (GUILayout.Button(new GUIContent("L+", "Include one later row"), EditorStyles.toolbarButton, GUILayout.Width(28))) NudgeRowRange(sheet, 0, 1);
                    if (GUILayout.Button("Go first", EditorStyles.toolbarButton, GUILayout.Width(54))) sheet.FirstRow = rows.FirstRow;
                    if (GUILayout.Button("Go last", EditorStyles.toolbarButton, GUILayout.Width(54))) sheet.FirstRow = rows.EndRow - 1;
                }
            }
        }
        private void NudgeRowRange(Sheet sheet, int firstDelta, int endDelta)
        {
            var rows = sheet.Binding.Rows; long count = sheet.Dataset.RowCount;
            long first = Math.Max(0, Math.Min(rows.End(count) - 1, rows.First(count) + firstDelta));
            long end = endDelta > 0 && rows.End(count) == count ? count : Math.Max(first + 1, Math.Min(count, rows.End(count) + endDelta));
            ApplyRowRange(sheet, new FPWorkbenchRowRange { Enabled = true, FirstRow = first, EndRow = end });
        }
        private void ApplyRowRange(Sheet sheet, FPWorkbenchRowRange candidate)
        {
            try {
                candidate.Validate(sheet.Dataset.RowCount); CaptureOpenFigures(); EnsurePlotUndo(sheet); error = null;
                sheet.Binding.Rows = candidate.Copy(); sheet.RowRangeDraftFirst = candidate.First(sheet.Dataset.RowCount) + 1; sheet.RowRangeDraftEnd = candidate.End(sheet.Dataset.RowCount);
                ApplyInheritedFigureRows(sheet);
                if (sheet.ShowPlot || sheet.FigureLease != null) StartSeries(sheet, true);
                else {
                    using var shared = FPWorkbenchFigureRegistry.TryRetain(workbookId + ":" + sheet.Id);
                    shared?.Session.ApplyBinding(sheet.Binding, true); RecordPlotBinding(sheet);
                }
                if (error == null) status = "Sheet plot row range applied; original rows remain unchanged and inspectable."; Repaint();
            } catch (Exception ex) { error = ex.Message; }
        }
        private void UpdateOpenFigureRows(FPWorkbenchFigureDocument figure)
        {
            foreach (var w in Resources.FindObjectsOfTypeAll<FPWorkbenchHistogramWindow>()) if (w.WorkbookId == workbookId && w.FigureId == figure.Id) w.ApplySheetRows(figure);
            foreach (var w in Resources.FindObjectsOfTypeAll<FPWorkbenchOverlayWindow>()) if (w.WorkbookId == workbookId && w.FigureId == figure.Id) w.ApplySheetRows(figure);
            foreach (var w in Resources.FindObjectsOfTypeAll<FPWorkbenchScatterWindow>()) if (w.WorkbookId == workbookId && w.FigureId == figure.Id) w.ApplySheetRows(figure);
            foreach (var w in Resources.FindObjectsOfTypeAll<FPWorkbenchPoint3DWindow>()) if (w.WorkbookId == workbookId && w.FigureId == figure.Id) w.ApplySheetRows(figure);
        }
        private void ApplyInheritedFigureRows(Sheet sheet)
        {
            foreach (var old in savedFigures.ToArray()) {
                if (old.Kind == FPWorkbenchFigureKind.AlignedScatter || old.DatasetId != sheet.Dataset.DatasetId ||
                    !string.IsNullOrEmpty(old.SourceSheetId) && old.SourceSheetId != sheet.Id) continue;
                var copy = JsonUtility.FromJson<FPWorkbenchFigureDocument>(JsonUtility.ToJson(old));
                SetFigureRows(copy, sheet); UpdateOpenFigureRows(copy); StoreFigure(copy);
            }
        }
        private static void SetFigureRows(FPWorkbenchFigureDocument figure, Sheet sheet)
        {
            figure.SourceSheetId = sheet.Id; figure.Rows = sheet.Binding.Rows.Copy(); figure.Binding.Rows = figure.Rows.Copy();
            if (figure.Kind == FPWorkbenchFigureKind.Histogram) {
                figure.FirstRow = figure.Rows.First(sheet.Dataset.RowCount); figure.EndRow = figure.Rows.End(sheet.Dataset.RowCount); figure.FitHistogramOnOpen = true;
            }
            figure.Plot.HasRange = false;
        }
        internal static void DrawFigureRowScope(string sheetId, FPWorkbenchRowRange rows, long count)
        {
            if (string.IsNullOrEmpty(sheetId) && !rows.Enabled) return;
            GUILayout.Label((string.IsNullOrEmpty(sheetId) ? "Figure rows: " : "Inherited sheet rows: ") +
                (rows.Enabled ? (rows.FirstRow + 1).ToString("N0") + " - " + rows.EndRow.ToString("N0") : "all " + count.ToString("N0")) + " (table row numbers)", EditorStyles.miniLabel);
        }
    }
}
