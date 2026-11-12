// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using UnityEngine;
    public sealed partial class FPDataWorkbenchWindow
    {
        private Task<FPWorkbenchSeriesRowExtent> selectionFrameWork;
        private FPWorkbenchSeriesCache selectionFrameSeries;
        private Sheet selectionFrameSheet;
        private CancellationTokenSource selectionFrameCancellation;
        private double selectionFrameStart, selectionFrameEnd;
        private void BeginSelectionFrame(Sheet sheet)
        {
            if (sheet?.Selection == null || sheet.Series == null || sheet.FigureLease == null || sheet.FigureLease.Session.IsBuilding) return;
            try {
                CancelSelectionFrame(); sheet.Selection.Resolve(sheet.PageTable);
                selectionFrameSheet = sheet; selectionFrameSeries = sheet.Series;
                selectionFrameStart = sheet.Plot.StartX; selectionFrameEnd = sheet.Plot.EndX;
                selectionFrameCancellation = new CancellationTokenSource();
                selectionFrameWork = selectionFrameSeries.ReadRowExtentAsync(sheet.Selection.FirstRow, sheet.Selection.EndRow, selectionFrameCancellation.Token);
                status = "Reading selected-row X endpoints from the plot cache…";
            } catch (Exception ex) { error = ex.Message; CancelSelectionFrame(); }
        }
        private void CancelSelectionFrame()
        {
            Cancel(selectionFrameCancellation); selectionFrameCancellation = null; Observe(selectionFrameWork); selectionFrameWork = null; selectionFrameSeries = null; selectionFrameSheet = null;
        }
        private bool TickSelectionFrame()
        {
            if (selectionFrameWork == null || !selectionFrameWork.IsCompleted) return false;
            var work = selectionFrameWork; var sheet = selectionFrameSheet; var series = selectionFrameSeries; selectionFrameWork = null;
            try {
                var extent = work.GetAwaiter().GetResult();
                if (!sheets.Contains(sheet) || extent.DatasetId != sheet.Dataset.DatasetId || !ReferenceEquals(sheet.Series, series) || sheet.FigureLease == null || sheet.FigureLease.Session.IsBuilding ||
                    sheet.Selection == null || sheet.Selection.FirstRow != extent.FirstRow || sheet.Selection.EndRow != extent.EndRow ||
                    sheet.Plot.StartX != selectionFrameStart || sheet.Plot.EndX != selectionFrameEnd) status = "Selection framing discarded because the plot changed; retry on the current view.";
                else {
                    var fitted = new FPWorkbenchPlotSettings { XScale = sheet.Plot.XScale }; fitted.Fit(extent.FirstX, extent.LastX);
                    if (double.IsNaN(fitted.StartX) || double.IsInfinity(fitted.StartX) || double.IsNaN(fitted.EndX) || double.IsInfinity(fitted.EndX) || fitted.EndX <= fitted.StartX || sheet.Plot.XScale == FPWorkbenchAxisScale.Log10 && fitted.StartX <= 0)
                        throw new InvalidOperationException("Selected X extent cannot form a finite increasing viewport.");
                    sheet.Plot.StartX = fitted.StartX; sheet.Plot.EndX = fitted.EndX; sheet.Plot.HasRange = true;
                    status = "Framed selected source-row X extent. This changes X navigation only; equal-X neighbors and padded single-point ranges may also be visible.";
                }
            } catch (OperationCanceledException) { }
            catch (Exception ex) { error = "Selection framing failed; viewport retained. " + ex.Message; }
            selectionFrameCancellation?.Dispose(); selectionFrameCancellation = null; selectionFrameSheet = null; selectionFrameSeries = null; return true;
        }
    }
}
