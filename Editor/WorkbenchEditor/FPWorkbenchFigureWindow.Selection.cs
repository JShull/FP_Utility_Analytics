// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using UnityEditor;
    using UnityEngine;

    public sealed partial class FPWorkbenchFigureWindow
    {
        private Task<FPWorkbenchSeriesPoint?> pointWork;
        private CancellationTokenSource pointCancel;
        private FPWorkbenchEditorResources.ScanReservation pointMemory;
        private FPWorkbenchSeriesCache pointSeries;
        private FPWorkbenchSeriesView pointView;
        private string pointSettings, pointStatus;
        private FPWorkbenchSeriesPoint? selectedPoint;
        private Rect inspectionRect;
        private FPWorkbenchGraphSelection graphSelection;
        private void CancelPointInspection(bool clear)
        {
            pointCancel?.Cancel(); pointCancel?.Dispose(); pointCancel = null;
            if (pointMemory != null) pointMemory.ReleaseAfter(pointWork);
            else if (pointWork != null) pointWork.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            pointMemory = null; pointWork = null;
            if (clear) { graphSelection?.Clear(); selectedPoint = null; pointSeries = null; pointView = null; pointStatus = null; }
        }
        private bool TickPointInspection(FPWorkbenchFigureSession session)
        {
            if ((pointWork != null || selectedPoint.HasValue) && (session.Series != pointSeries || session.Error != null ||
                pointWork != null && (session.IsBusy || session.View != pointView || JsonUtility.ToJson(session.Settings) != pointSettings))) { CancelPointInspection(true); return true; }
            if (pointWork == null || !pointWork.IsCompleted) return false;
            var task = pointWork; pointWork = null; pointMemory?.Dispose(); pointMemory = null;
            pointCancel?.Dispose(); pointCancel = null;
            try { selectedPoint = task.GetAwaiter().GetResult();
                if (selectedPoint.HasValue) {
                    int separator = figureId.LastIndexOf(':'); string workbook = separator < 0 ? "" : figureId.Substring(0, separator);
                    graphSelection.Select(FPWorkbenchGraphSelection.Scalar(workbook, document.Id, session.DatasetId, session.Series.Binding, selectedPoint.Value.Row));
                }
                pointStatus = selectedPoint.HasValue ? null : "No displayed source sample near that click; zoom in to inspect."; }
            catch (OperationCanceledException) { }
            catch (Exception ex) { pointStatus = ex.Message; }
            return true;
        }
        private void DrawPointInspectionControls()
        {
            if (pointWork != null) GUILayout.Label("Finding source sample…", EditorStyles.miniLabel);
            else if (!string.IsNullOrEmpty(pointStatus)) GUILayout.Label(pointStatus, EditorStyles.miniLabel);
            if (!selectedPoint.HasValue) return;
            var point = selectedPoint.Value;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar)) {
                var oldColor = GUI.color; GUI.color = graphSelection.Color;
                GUILayout.Label(new GUIContent("● Row " + (point.Row + 1).ToString("N0") + " | Y: " + point.Y.ToString("G9", CultureInfo.InvariantCulture),
                    "X: " + point.X.ToString("R", CultureInfo.InvariantCulture) + "; Y: " + point.Y.ToString("R", CultureInfo.InvariantCulture)), EditorStyles.miniLabel, GUILayout.MinWidth(0)); GUI.color = oldColor;
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Clear point", EditorStyles.toolbarButton)) CancelPointInspection(true);
            }
        }
        private void DrawPointInspection(Rect rect, FPWorkbenchFigureSession session)
        {
            if (Event.current.type == EventType.Repaint) inspectionRect = rect;
            // Hit-test the graph the user actually saw, even if a status row changes this event's layout.
            var painted = inspectionRect.width > 0 ? inspectionRect : rect;
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && !session.IsBusy && session.Error == null && session.Series != null &&
                FPWorkbenchPlotView.TryScalarAxes(painted, session.View, session.Settings, out var area, out _, out _) && FPWorkbenchPlotView.ContainsPoint(area, Event.current.mousePosition)) {
                CancelPointInspection(true); GUIUtility.keyboardControl = 0;
                if (!FPWorkbenchEditorResources.TryAcquireScan(4L * 1024 * 1024, out pointMemory)) pointStatus = "Shared capacity is busy; retry after current work finishes.";
                else {
                    try {
                        pointSeries = session.Series; pointView = session.View; pointSettings = JsonUtility.ToJson(session.Settings);
                        pointCancel = new CancellationTokenSource(); var mouse = Event.current.mousePosition;
                        pointWork = pointSeries.PickDisplayedPointAsync(pointView, session.Settings, (mouse.x - area.x) / area.width,
                            (mouse.y - area.y) / area.height, area.width, area.height, cancellation: pointCancel.Token);
                    } catch (Exception ex) { CancelPointInspection(true); pointStatus = ex.Message; }
                }
                Event.current.Use(); Repaint();
            }
            if (Event.current.type != EventType.Repaint || !selectedPoint.HasValue ||
                !FPWorkbenchPlotView.TryProjectScalar(rect, session.View, session.Settings, selectedPoint.Value, out var screen)) return;
            graphSelection.Ring(screen);
        }
    }
}
