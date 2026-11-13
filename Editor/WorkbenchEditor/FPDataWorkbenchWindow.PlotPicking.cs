// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using UnityEditor;
    using UnityEngine;

    public sealed partial class FPDataWorkbenchWindow
    {
        private Task<FPWorkbenchSeriesPoint?> plotPickWork;
        private CancellationTokenSource plotPickCancel;
        private FPWorkbenchEditorResources.ScanReservation plotPickMemory;
        private Sheet plotPickSheet, pickedPlotSheet;
        private FPWorkbenchSeriesCache plotPickSeries, pickedPlotSeries;
        private FPWorkbenchSeriesView plotPickView;
        private string plotPickSettings;
        private FPWorkbenchSeriesPoint? pickedPlotPoint;
        private Rect embeddedPlotRect;
        private bool linkedCellsOnly, linkedRowHeader;

        private void CancelPlotPick(bool clear)
        {
            Cancel(plotPickCancel); plotPickCancel = null;
            if (plotPickMemory != null) plotPickMemory.ReleaseAfter(plotPickWork); else Observe(plotPickWork);
            plotPickMemory = null; plotPickWork = null; plotPickSheet = null; plotPickSeries = null; plotPickView = null;
            if (!clear) return;
            pickedPlotPoint = null; pickedPlotSheet = null; pickedPlotSeries = null;
            if (linkedCellsOnly) { linkedDataset = null; linkedRow = -1; linkedColumns = Array.Empty<int>(); linkedCellsOnly = linkedRowHeader = false; }
        }
        private bool TickPlotPick()
        {
            if (pickedPlotPoint.HasValue && (Current != pickedPlotSheet || Current?.Series != pickedPlotSeries || !Current.Binding.Rows.Contains(pickedPlotPoint.Value.Row))) CancelPlotPick(true);
            if (!pickedPlotPoint.HasValue && Current?.PlotSelectedPoint != null && Current.Series == Current.PlotSelectedSeries) {
                pickedPlotPoint = Current.PlotSelectedPoint; pickedPlotSheet = Current; pickedPlotSeries = Current.Series;
            }
            if (plotPickWork == null) return false;
            if (Current != plotPickSheet || Current?.Series != plotPickSeries || Current.FigureLease?.Session.IsBusy == true || Current.FigureLease?.Session.Error != null || !ReferenceEquals(summary, plotPickView) || JsonUtility.ToJson(Current.Plot) != plotPickSettings) { CancelPlotPick(true); return true; }
            if (!plotPickWork.IsCompleted) return false;
            var task = plotPickWork; var sheet = plotPickSheet; var series = plotPickSeries;
            plotPickWork = null; plotPickMemory?.Dispose(); plotPickMemory = null;
            try {
                var selected = task.GetAwaiter().GetResult();
                if (!selected.HasValue) { status = "No displayed source sample near that click. Zoom in to inspect individual samples."; return true; }
                pickedPlotPoint = selected; pickedPlotSheet = sheet; pickedPlotSeries = series;
                sheet.PlotSelectedPoint = selected; sheet.PlotSelectedSeries = series;
                RevealPlotPoint(sheet, series.Binding, selected.Value);
                sheet.GraphSelection ??= new FPWorkbenchGraphSelection(this);
                sheet.GraphSelection.Select(FPWorkbenchGraphSelection.Scalar(workbookId, sheet.Id, sheet.Dataset.DatasetId, series.Binding, selected.Value.Row));
            } catch (OperationCanceledException) { }
            catch (Exception ex) { error = "Point inspection failed: " + ex.Message; }
            return true;
        }
        private void BeginPlotPick(Sheet sheet, Rect rect, Vector2 mouse)
        {
            if (sheet.Series == null || sheet.FigureLease == null || sheet.FigureLease.Session.IsBusy || sheet.FigureLease.Session.Error != null || summary == null ||
                !FPWorkbenchPlotView.TryScalarAxes(rect, summary, sheet.Plot, out var area, out _, out _) || !FPWorkbenchPlotView.ContainsPoint(area, mouse)) return;
            ClearEmbeddedSelection(sheet); GUIUtility.keyboardControl = 0;
            if (!FPWorkbenchEditorResources.TryAcquireScan(4L * 1024 * 1024, out plotPickMemory)) { status = "Point inspection is waiting for shared capacity; retry when current work finishes."; return; }
            try {
                plotPickSheet = sheet; plotPickSeries = sheet.Series; plotPickView = summary; plotPickSettings = JsonUtility.ToJson(sheet.Plot);
                plotPickCancel = new CancellationTokenSource();
                plotPickWork = sheet.Series.PickDisplayedPointAsync(summary, sheet.Plot, (mouse.x - area.x) / area.width, (mouse.y - area.y) / area.height, area.width, area.height, cancellation: plotPickCancel.Token);
                status = "Finding the displayed source sample in the plot cache...";
            } catch (Exception ex) { CancelPlotPick(true); error = ex.Message; }
        }
        private void DrawPlotPickControls(Sheet sheet)
        {
            if (!pickedPlotPoint.HasValue || pickedPlotSheet != sheet) return;
            var point = pickedPlotPoint.Value;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar)) {
                var previousColor = GUI.color; GUI.color = sheet.GraphSelection?.Color ?? Color.yellow;
                string label = "Point row " + (point.Row + 1).ToString("N0") + " | Y: " + point.Y.ToString("G9", CultureInfo.InvariantCulture) + " | X: " + point.X.ToString("G9", CultureInfo.InvariantCulture);
                GUILayout.Label(new GUIContent("● " + label, "Y: " + point.Y.ToString("R", CultureInfo.InvariantCulture) + "; X: " + point.X.ToString("R", CultureInfo.InvariantCulture) + ". Original source row; UTC X uses the labeled elapsed-time origin."), EditorStyles.miniLabel, GUILayout.MinWidth(0), GUILayout.ExpandWidth(true)); GUI.color = previousColor;
                if (GUILayout.Button("Clear point", EditorStyles.toolbarButton, GUILayout.Width(80))) ClearEmbeddedSelection(sheet);
            }
        }
        private void DrawPlotPointSelection(Sheet sheet, Rect rect, bool currentView)
        {
            if (Event.current.type == EventType.Repaint) embeddedPlotRect = rect;
            if (currentView && Event.current.type == EventType.MouseDown && Event.current.button == 0 && FPWorkbenchPlotView.ContainsPoint(FPWorkbenchPlotView.PlotRect(rect, sheet.Plot), Event.current.mousePosition)) {
                BeginPlotPick(sheet, rect, Event.current.mousePosition); Event.current.Use();
            }
            if (Event.current.type != EventType.Repaint || !currentView || !pickedPlotPoint.HasValue || pickedPlotSheet != sheet || pickedPlotSeries != sheet.Series ||
                !FPWorkbenchPlotView.TryProjectScalar(rect, summary, sheet.Plot, pickedPlotPoint.Value, out var point)) return;
            sheet.GraphSelection?.Ring(point);
        }
        private void ClearEmbeddedSelection(Sheet sheet)
        {
            sheet.GraphSelection?.Clear(); sheet.PlotSelectedPoint = null; sheet.PlotSelectedSeries = null;
            CancelPlotPick(true);
        }
        private void RevealPlotPoint(Sheet sheet, FPWorkbenchSeriesBinding binding, FPWorkbenchSeriesPoint point)
        {
            EnsureTableSchema(sheet);
            int y = binding.HasDerivedY ? sheet.Dataset.ColumnCount + binding.ResolveDerivedY(sheet.TableProgram) : binding.YColumn;
            var columns = new SortedSet<int> { y };
            if (binding.XMode == FPWorkbenchXMode.Numeric) columns.Add(binding.HasDerivedX ? sheet.Dataset.ColumnCount + binding.ResolveDerivedX(sheet.TableProgram) : binding.XColumn);
            else if (binding.XMode == FPWorkbenchXMode.UtcDateTime) { columns.Add(binding.DateColumn); columns.Add(binding.XColumn); }
            linkedDataset = sheet.Dataset.DatasetId; linkedRow = point.Row; linkedColumns = new int[columns.Count]; columns.CopyTo(linkedColumns);
            linkedCellsOnly = true; linkedRowHeader = binding.XMode == FPWorkbenchXMode.Row;
            pendingLinkedDataset = null; pendingLinkedColumns = null;
            var projection = JsonUtility.FromJson<FPWorkbenchColumnProjection>(JsonUtility.ToJson(sheet.Columns.Projection)); string before = JsonUtility.ToJson(projection);
            foreach (int column in columns) projection.SetHidden(sheet.SchemaIds[column], false);
            if (before != JsonUtility.ToJson(projection)) {
                sheet.ProjectionUndo ??= new FPWorkbenchColumnProjectionUndo(value => { if (sheets.Contains(sheet)) ApplyColumnProjection(sheet, value); });
                sheet.ProjectionUndo.Record(before, JsonUtility.ToJson(projection)); ApplyColumnProjection(sheet, projection);
            }
            int offset = 0; bool frozen = Array.IndexOf(sheet.Columns.Projection.Frozen, sheet.SchemaIds[y]) >= 0;
            if (!frozen) foreach (int column in sheet.ColumnOrder) { if (Array.IndexOf(sheet.Columns.Projection.Frozen, sheet.SchemaIds[column]) >= 0) continue; if (column == y) break; offset++; }
            sheet.FirstRow = point.Row; if (!frozen) sheet.FirstColumn = offset;
            selectedDataset = null; page = null; failedPage = null; CancelVirtualPage();
            status = "Picked table row " + (point.Row + 1) + "; Y column brought into view. Axis-value cells are boxed when visible. Overview picks actual extrema/endpoints; zoom for individual samples." + (binding.XMode == FPWorkbenchXMode.Row ? " Row X is zero-based; table row numbers are one-based." : "");
            Repaint();
        }
    }
}
