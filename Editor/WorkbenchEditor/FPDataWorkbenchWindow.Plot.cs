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
        private static int PlotColumnForBinding(Sheet sheet, FPWorkbenchSeriesBinding binding)
            => binding.HasDerivedY ? sheet.Dataset.ColumnCount + binding.ResolveDerivedY(sheet.TableProgram) : binding.YColumn;
        private bool TickPlot(Sheet current)
        {
            bool changed = false;
            foreach (var sheet in sheets)
            {
                if (sheet.FigureLease == null)
                {
                    if (sheet.Dataset != null && sheet.ShowPlot) StartSeries(sheet, false);
                    else continue;
                }
                if (sheet.FigureLease == null) continue;
                var session = sheet.FigureLease.Session;
                session.ApplyCalculations(sheet.TableProgram);
                session.ApplyChanges(sheet.ShowOriginal ? null : SnapshotFor(sheet.Dataset.DatasetId));
                changed |= session.Poll(); sheet.Series = session.Series; sheet.Plot = session.Settings;
                if (sheet.FigureRevision != session.Revision) { sheet.FigureRevision = session.Revision; changed = true; }
                if (session.Error != null) error = session.Error.Message;
                if (sheet == current)
                {
                    summary = session.View; summarySheet = sheet.Id; requestedSeries = session.Series;
                }
            }
            return changed | TickSelectionFrame();
        }
        private void StartSeries(Sheet sheet, bool resetView)
        {
            if (sheet.Dataset.RowCount == 0) { error = "This sheet has no data rows."; sheet.ShowPlot = false; return; }
            try
            {
                EnsureTableSchema(sheet);
                EnsurePlotUndo(sheet);
                if (sheet.PlotColumn >= sheet.Dataset.ColumnCount)
                { sheet.Binding.YDerivedId = sheet.TableProgram.ColumnId(sheet.PlotColumn - sheet.Dataset.ColumnCount); sheet.Binding.YColumn = 0; }
                else { sheet.Binding.YDerivedId = ""; sheet.Binding.YColumn = sheet.PlotColumn; }
                sheet.Binding.Copy(sheet.Dataset.ColumnCount);
                if (sheet.FigureLease == null)
                {
                    if (resetView) sheet.Plot.HasRange = false;
                    sheet.FigureLease = FPWorkbenchFigureRegistry.Retain(workbookId + ":" + sheet.Id, sheet.Dataset, sheet.Binding, sheet.Plot, sheet.ShowOriginal ? null : SnapshotFor(sheet.Dataset.DatasetId), sheet.TableProgram);
                }
                else { sheet.FigureLease.Session.ApplyCalculations(sheet.TableProgram); sheet.FigureLease.Session.ApplyBinding(sheet.Binding, resetView); }
                sheet.Plot = sheet.FigureLease.Session.Settings;
                sheet.ShowPlot = true; error = null;
                RecordPlotBinding(sheet);
            }
            catch (Exception ex) { error = ex.Message; sheet.ShowPlot = false; }
        }
        private void DrawPlotControls(Sheet sheet)
        {
            sheet.Binding ??= new FPWorkbenchSeriesBinding(); sheet.Plot ??= new FPWorkbenchPlotSettings();
            EnsurePlotUndo(sheet);
            GUILayout.Label("Series binding", EditorStyles.boldLabel);
            sheet.PlotColumn = EditorGUILayout.Popup("Y column", Mathf.Clamp(sheet.PlotColumn, 0, DisplayColumnCount(sheet) - 1), sheet.TableNames ?? sheet.ColumnNames);
            if (GUILayout.Button("Open 3D Point Window"))
            {
                try { FPWorkbenchPoint3DWindow.Open(workbookId, sheet.Dataset, 0, Math.Min(1, DisplayColumnCount(sheet) - 1), Math.Min(2, DisplayColumnCount(sheet) - 1), sheet.TableProgram, sheet.ShowOriginal ? null : SnapshotFor(sheet.Dataset.DatasetId)); }
                catch (Exception ex) { error = ex.Message; }
            }
            if (GUILayout.Button("Open Scatter Window"))
            {
                try
                {
                    int x = sheet.Binding.XMode == FPWorkbenchXMode.Numeric ? (sheet.Binding.HasDerivedX ? sheet.Dataset.ColumnCount + sheet.Binding.ResolveDerivedX(sheet.TableProgram) : sheet.Binding.XColumn) : 0;
                    FPWorkbenchScatterWindow.Open(workbookId, sheet.Dataset, x, sheet.PlotColumn, sheet.TableProgram, sheet.ShowOriginal ? null : SnapshotFor(sheet.Dataset.DatasetId));
                }
                catch (Exception ex) { error = ex.Message; }
            }
            if (GUILayout.Button("Open Histogram Window"))
            {
                try
                {
                    bool derived = sheet.PlotColumn >= sheet.Dataset.ColumnCount;
                    FPWorkbenchHistogramWindow.Open(sheet.Dataset, derived ? 0 : sheet.PlotColumn, sheet.ShowOriginal ? null : SnapshotFor(sheet.Dataset.DatasetId), workbookId,
                        derived ? sheet.TableProgram : null, derived ? sheet.TableProgram.ColumnId(sheet.PlotColumn - sheet.Dataset.ColumnCount) : null);
                }
                catch (Exception ex) { error = ex.Message; }
            }
            if (GUILayout.Button("Open Multi-series Window"))
            {
                try
                {
                    bool derived = sheet.PlotColumn >= sheet.Dataset.ColumnCount;
                    FPWorkbenchOverlayWindow.Open(workbookId, sheet.Dataset, sheet.Binding, derived ? 0 : sheet.PlotColumn, sheet.ShowOriginal ? null : SnapshotFor(sheet.Dataset.DatasetId),
                        sheet.TableProgram, derived ? sheet.TableProgram.ColumnId(sheet.PlotColumn - sheet.Dataset.ColumnCount) : null);
                }
                catch (Exception ex) { error = ex.Message; }
            }
            var mode = (FPWorkbenchXMode)EditorGUILayout.EnumPopup("X axis", sheet.Binding.XMode);
            if (mode != sheet.Binding.XMode) { sheet.Binding.XMode = mode; sheet.Binding.XDerivedId = ""; }
            if (sheet.Binding.XMode == FPWorkbenchXMode.Numeric)
            {
                int shown = sheet.Binding.HasDerivedX ? sheet.Dataset.ColumnCount + sheet.Binding.ResolveDerivedX(sheet.TableProgram) : sheet.Binding.XColumn;
                int selected = EditorGUILayout.Popup("X column", shown, sheet.TableNames ?? sheet.ColumnNames);
                if (selected >= sheet.Dataset.ColumnCount) { sheet.Binding.XDerivedId = sheet.TableProgram.ColumnId(selected - sheet.Dataset.ColumnCount); sheet.Binding.XColumn = 0; }
                else { sheet.Binding.XDerivedId = ""; sheet.Binding.XColumn = selected; }
            }
            else if (sheet.Binding.XMode == FPWorkbenchXMode.UtcDateTime)
                sheet.Binding.XColumn = EditorGUILayout.Popup("UTC time column", sheet.Binding.XColumn, sheet.ColumnNames);
            if (sheet.Binding.XMode == FPWorkbenchXMode.UtcDateTime)
            {
                sheet.Binding.DateColumn = EditorGUILayout.Popup("UTC date column", sheet.Binding.DateColumn, sheet.ColumnNames);
                sheet.Binding.UtcFormat = EditorGUILayout.TextField(new GUIContent("UTC format", FPWorkbenchPlotView.UtcFormatTooltip), sheet.Binding.UtcFormat);
            }
            sheet.Binding.MaximumGap = EditorGUILayout.DoubleField(new GUIContent("Gap threshold", "0 disables gap detection. Units follow X; UTC uses seconds."), sheet.Binding.MaximumGap);
            if (GUILayout.Button("Apply binding / build plot")) StartSeries(sheet, true);
            if (sheet.FigureLease != null && sheet.FigureLease.Session.IsBusy)
            {
                GUILayout.Label(sheet.FigureLease.Session.IsBuilding ? "Building/verifying series cache…" : "Reading cached view…", EditorStyles.miniLabel);
                if (GUILayout.Button("Close plot")) { sheet.FigureLease.Dispose(); sheet.FigureLease = null; sheet.Series = null; sheet.ShowPlot = false; summary = null; }
            }
            if (sheet.Series != null)
            {
                sheet.PresentationUndo ??= new FPWorkbenchPresentationUndo(Repaint);
                sheet.PresentationUndo.Begin(sheet.Plot);
                GUILayout.Label("Plot view", EditorStyles.boldLabel);
                if (GUILayout.Button("Open Figure Window"))
                    FPWorkbenchFigureWindow.Open(workbookId + ":" + sheet.Id, DescribeSheet(sheet), sheet.Dataset, sheet.TableProgram);
                FPWorkbenchPlotView.LabelControls(sheet.Plot);
                sheet.Plot.StartX = EditorGUILayout.DoubleField("X start", sheet.Plot.StartX);
                sheet.Plot.EndX = EditorGUILayout.DoubleField("X end", sheet.Plot.EndX);
                sheet.Plot.InvertY = EditorGUILayout.Toggle("Invert Y (depth)", sheet.Plot.InvertY);
                FPWorkbenchPlotView.StyleControls(sheet.Plot);
                sheet.Plot.Grid = EditorGUILayout.Toggle("Grid", sheet.Plot.Grid);
                sheet.Plot.XTickSpacing = EditorGUILayout.DoubleField("X grid spacing", sheet.Plot.XTickSpacing);
                sheet.Plot.YTickSpacing = EditorGUILayout.DoubleField("Y grid spacing", sheet.Plot.YTickSpacing);
                sheet.Plot.LockY = EditorGUILayout.Toggle("Lock Y", sheet.Plot.LockY);
                if (sheet.Plot.LockY)
                {
                    sheet.Plot.MinimumY = EditorGUILayout.DoubleField("Y minimum", sheet.Plot.MinimumY);
                    sheet.Plot.MaximumY = EditorGUILayout.DoubleField("Y maximum", sheet.Plot.MaximumY);
                }
                using (new UnityEditor.EditorGUI.DisabledScope(sheet.Selection == null || sheet.FigureLease == null || sheet.FigureLease.Session.IsBuilding))
                    if (GUILayout.Button(new GUIContent("Frame selected table rows", "Navigate X to selected row timestamps; does not filter samples or change the bound columns. Equal-X neighbors may be visible."))) BeginSelectionFrame(sheet);
                if (GUILayout.Button("Fit X")) sheet.Plot.Fit(sheet.Series.FirstX, sheet.Series.LastX);
                if (GUILayout.Button("Fit Y") && summary != null && summarySheet == sheet.Id && !double.IsNaN(summary.Minimum))
                { sheet.Plot.MinimumY = summary.Minimum; sheet.Plot.MaximumY = summary.Maximum; }
                sheet.PresentationUndo.End();
                EditorGUILayout.HelpBox(sheet.Series.Binding.XMode == FPWorkbenchXMode.UtcDateTime
                    ? "X shows elapsed seconds from the declared UTC origin. Source measurements remain unchanged."
                    : "Numeric/row X is used exactly as bound. Applying a new binding rebuilds or reuses its cache.", MessageType.Info);
            }
        }
    }
}
