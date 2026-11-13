// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using UnityEditor;
    using UnityEngine;
    public sealed partial class FPWorkbenchOverlayWindow
    {
        private FPWorkbenchGraphSelection graphSelection;
        private Task<(int Series, FPWorkbenchSeriesPoint Point)?> picking;
        private CancellationTokenSource pickCancel;
        private FPWorkbenchEditorResources.ScanReservation pickMemory;
        private FPWorkbenchOverlay pickOverlay;
        private FPWorkbenchOverlayView pickView;
        private (int Series, FPWorkbenchSeriesPoint Point)? selection;
        private string pickSettings;
        private FPWorkbenchPlotSettings[] PickMappings()
        {
            var result = new FPWorkbenchPlotSettings[view.Series.Count];
            for (int i = 0; i < result.Length; i++) {
                bool right = axes.Right[i]; var source = right ? axes.RightSettings : settings; var limits = axes.Resolve(view, settings, right);
                var value = JsonUtility.FromJson<FPWorkbenchPlotSettings>(JsonUtility.ToJson(settings));
                value.LockY = true; value.MinimumY = limits.Minimum; value.MaximumY = limits.Maximum; value.YScale = source.YScale; value.InvertY = source.InvertY;
                result[i] = value;
            }
            return result;
        }
        private void CancelPick(bool clear)
        {
            pickCancel?.Cancel(); pickCancel?.Dispose(); pickCancel = null;
            if (pickMemory != null) pickMemory.ReleaseAfter(picking); else if (picking != null) Observe(picking);
            pickMemory = null; picking = null;
            if (clear) { selection = null; graphSelection?.Clear(); }
        }
        private bool TickPick()
        {
            if (selection.HasValue && overlay != pickOverlay) { CancelPick(true); return true; }
            if (picking == null) return false;
            if (overlay != pickOverlay || view != pickView || Busy || pickSettings != JsonUtility.ToJson(settings) + JsonUtility.ToJson(axes)) { CancelPick(true); return true; }
            if (!picking.IsCompleted) return false;
            var work = picking; picking = null; pickMemory?.Dispose(); pickMemory = null; pickCancel?.Dispose(); pickCancel = null;
            try {
                selection = work.GetAwaiter().GetResult();
                if (selection.HasValue) {
                    var binding = appliedBinding.Copy(dataset.ColumnCount); int i = selection.Value.Series;
                    binding.YColumn = view.YColumns[i]; binding.YDerivedId = view.YDerivedIds[i];
                    graphSelection.Select(FPWorkbenchGraphSelection.Scalar(workbook, sourceSheetId, datasetId, binding, selection.Value.Point.Row));
                }
            } catch (OperationCanceledException) { } catch (Exception ex) { error = ex.Message; }
            return true;
        }
        private void DrawPick(Rect rect)
        {
            if (view == null) return;
            bool dual = Array.Exists(axes.Right, v => v); Rect area = FPWorkbenchPlotView.PlotRect(rect, settings, dual);
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && FPWorkbenchPlotView.ContainsPoint(area, Event.current.mousePosition) && !Busy) {
                CancelPick(true);
                if (FPWorkbenchEditorResources.TryAcquireScan(4L * 1024 * 1024, out pickMemory)) {
                    try {
                        pickOverlay = overlay; pickView = view; pickSettings = JsonUtility.ToJson(settings) + JsonUtility.ToJson(axes);
                        pickCancel = new CancellationTokenSource(); Vector2 mouse = Event.current.mousePosition;
                        picking = overlay.PickDisplayedPointAsync(view, PickMappings(), (mouse.x - area.x) / area.width, (mouse.y - area.y) / area.height, area.width, area.height, pickCancel.Token);
                    } catch (Exception ex) { CancelPick(true); error = ex.Message; }
                }
                Event.current.Use(); Repaint();
            }
            if (!selection.HasValue || Event.current.type != EventType.Repaint) return;
            var selected = selection.Value;
            try {
                var mapping = PickMappings()[selected.Series]; var p = selected.Point;
                var x = new FPWorkbenchAxisMapping(view.StartX, view.EndX, mapping.XScale); var y = new FPWorkbenchAxisMapping(mapping.MinimumY, mapping.MaximumY, mapping.YScale);
                if (p.X < view.StartX || p.X > view.EndX || p.Y < mapping.MinimumY || p.Y > mapping.MaximumY) return;
                double fy = y.Fraction(p.Y); graphSelection.Ring(new Vector2(area.x + (float)x.Fraction(p.X) * area.width, area.y + (float)(mapping.InvertY ? fy : 1 - fy) * area.height));
            } catch (ArgumentException) { }
        }
        private void DrawSelectionControls()
        {
            if (!selection.HasValue) return;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar)) {
                graphSelection.Legend("Row " + (selection.Value.Point.Row + 1).ToString("N0") + " | Y: " + selection.Value.Point.Y.ToString("G9"));
                if (GUILayout.Button("Clear point", EditorStyles.toolbarButton, GUILayout.Width(80))) CancelPick(true);
            }
        }
    }
}
