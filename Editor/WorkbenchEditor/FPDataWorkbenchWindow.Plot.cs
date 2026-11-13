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
            SidebarSection("Table plot");
            if (GUILayout.Button("Open Plot Window")) OpenSheetFigure(sheet);
            SidebarSection("Figures from this sheet");
            if (GUILayout.Button("Open 3D Figure"))
            {
                try { FPWorkbenchPoint3DWindow.Open(workbookId, sheet.Dataset, 0, Math.Min(1, DisplayColumnCount(sheet) - 1), Math.Min(2, DisplayColumnCount(sheet) - 1), sheet.TableProgram, sheet.ShowOriginal ? null : SnapshotFor(sheet.Dataset.DatasetId), sheet.Binding.Rows, sheet.Id); }
                catch (Exception ex) { error = ex.Message; }
            }
            if (GUILayout.Button("Open Scatter Figure"))
            {
                try
                {
                    int x = sheet.Binding.XMode == FPWorkbenchXMode.Numeric ? (sheet.Binding.HasDerivedX ? sheet.Dataset.ColumnCount + sheet.Binding.ResolveDerivedX(sheet.TableProgram) : sheet.Binding.XColumn) : sheet.Binding.XMode == FPWorkbenchXMode.UtcDateTime ? sheet.Binding.XColumn : 0;
                    FPWorkbenchScatterWindow.Open(workbookId, sheet.Dataset, x, sheet.PlotColumn, sheet.TableProgram, sheet.ShowOriginal ? null : SnapshotFor(sheet.Dataset.DatasetId), rows: sheet.Binding.Rows, sheetId: sheet.Id, xBinding: sheet.Binding);
                }
                catch (Exception ex) { error = ex.Message; }
            }
            if (GUILayout.Button("Open Histogram Figure"))
            {
                try
                {
                    bool derived = sheet.PlotColumn >= sheet.Dataset.ColumnCount;
                    FPWorkbenchHistogramWindow.Open(sheet.Dataset, derived ? 0 : sheet.PlotColumn, sheet.ShowOriginal ? null : SnapshotFor(sheet.Dataset.DatasetId), workbookId,
                        sheet.TableProgram, derived ? sheet.TableProgram.ColumnId(sheet.PlotColumn - sheet.Dataset.ColumnCount) : null, sheet.Binding.Rows, sheet.Id);
                }
                catch (Exception ex) { error = ex.Message; }
            }
            if (GUILayout.Button("Open Multi-series Figure"))
            {
                try
                {
                    bool derived = sheet.PlotColumn >= sheet.Dataset.ColumnCount;
                    FPWorkbenchOverlayWindow.Open(workbookId, sheet.Dataset, sheet.Binding, derived ? 0 : sheet.PlotColumn, sheet.ShowOriginal ? null : SnapshotFor(sheet.Dataset.DatasetId),
                        sheet.TableProgram, derived ? sheet.TableProgram.ColumnId(sheet.PlotColumn - sheet.Dataset.ColumnCount) : null, sheet.Id);
                }
                catch (Exception ex) { error = ex.Message; }
            }
        }
        private void OpenSheetFigure(Sheet sheet) => FPWorkbenchPlotSettingsWindow.Open(this);
        internal FPWorkbenchFigureWindow GetOrCreatePlotFigure()
        {
            var sheet = Current; if (sheet?.Dataset == null) return null;
            if (sheet.FigureLease == null) StartSeries(sheet, false);
            if (sheet.FigureLease == null) return null;
            string key = workbookId + ":" + sheet.Id;
            foreach (var existing in Resources.FindObjectsOfTypeAll<FPWorkbenchFigureWindow>())
                if (existing.PlotSettingsId == key) { existing.Show(); return existing; }
            return FPWorkbenchFigureWindow.Open(key, DescribeSheet(sheet), sheet.Dataset, sheet.TableProgram);
        }

        internal string ActivePlotSheetId => Current?.Id;
        internal static bool FindPlotOwner(string key, out FPDataWorkbenchWindow owner, out string sheetId)
        {
            foreach (var candidate in Resources.FindObjectsOfTypeAll<FPDataWorkbenchWindow>())
                foreach (var sheet in candidate.sheets)
                    if (key == candidate.workbookId + ":" + sheet.Id) { owner = candidate; sheetId = sheet.Id; return true; }
            owner = null; sheetId = null; return false;
        }
        internal string PlotSheetName(string id) => sheets.Find(s => s.Id == id)?.Name;
        internal bool HasPlotSheet(string id) => sheets.Exists(s => s.Id == id);
        internal bool RevealScalarPoint(string id, FPWorkbenchSeriesBinding binding, FPWorkbenchSeriesPoint point)
        {
            int index = sheets.FindIndex(s => s.Id == id && s.Dataset != null);
            if (index < 0) return false;
            active = index; CancelPlotPick(true); RevealPlotPoint(sheets[index], binding, point); Focus(); return true;
        }
        internal void DrawPlotSettings(FPWorkbenchPlotSettingsWindow panel, string id)
        {
            var sheet = sheets.Find(s => s.Id == id);
            if (sheet?.Dataset == null) { EditorGUILayout.HelpBox("Waiting for this sheet's source to finish loading.", MessageType.Info); return; }
            EnsureTableSchema(sheet); EnsurePlotUndo(sheet);
            if (panel.BindingSection()) {
                if (FPWorkbenchPlotSettingsWindow.BindingControls(sheet.Binding, sheet.Dataset.ColumnCount, sheet.TableProgram,
                    sheet.ColumnNames, sheet.TableNames ?? sheet.ColumnNames, ref sheet.PlotColumn)) StartSeries(sheet, true);
                var rows = sheet.Binding.Rows;
                GUILayout.Label(rows.Enabled ? "Inherited sheet rows: " + (rows.FirstRow + 1).ToString("N0") + " – " + rows.EndRow.ToString("N0") : "Inherited sheet rows: all rows", EditorStyles.miniLabel);
            }
            if (sheet.FigureLease != null && sheet.FigureLease.Session.IsBusy)
                EditorGUILayout.HelpBox(sheet.FigureLease.Session.WaitingForMemory ? "Waiting for shared capacity…" : "Updating cached plot…", MessageType.Info);
            if (sheet.FigureLease?.Session.Error != null) EditorGUILayout.HelpBox(sheet.FigureLease.Session.Error.Message, MessageType.Error);
            sheet.PresentationUndo ??= new FPWorkbenchPresentationUndo(Repaint);
            sheet.PresentationUndo.Begin(sheet.Plot);
            try {
                panel.PresentationControls(sheet.Plot, sheet.Series, sheet.FigureLease?.Session.View, () => BeginSelectionFrame(sheet),
                    sheet.Selection != null && sheet.FigureLease != null && !sheet.FigureLease.Session.IsBuilding);
            } finally { sheet.PresentationUndo.End(); }
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
        }
    }
}
