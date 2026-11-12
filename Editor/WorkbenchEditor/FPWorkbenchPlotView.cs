// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.Globalization;
    using UnityEditor;
    using UnityEngine;

    internal static class FPWorkbenchPlotView
    {
        public static void LabelControls(FPWorkbenchPlotSettings settings)
        {
            FPWorkbenchPlotPresetGUI.Draw(settings);
            settings.Title = EditorGUILayout.TextField("Title", settings.Title);
            settings.XLabel = EditorGUILayout.TextField("X label", settings.XLabel);
            settings.YLabel = EditorGUILayout.TextField("Y label", settings.YLabel);
        }
        public static void StyleControls(FPWorkbenchPlotSettings settings, bool compact = false)
        {
            if (compact) settings.Style = (FPWorkbenchPlotStyle)EditorGUILayout.EnumPopup(settings.Style, GUILayout.Width(75));
            else
            {
                settings.Style = (FPWorkbenchPlotStyle)EditorGUILayout.EnumPopup("Plot style", settings.Style);
                settings.LineWidth = EditorGUILayout.Slider("Line width", settings.LineWidth, 1, 8);
                settings.MarkerSize = EditorGUILayout.Slider("Point size", settings.MarkerSize, 1, 16);
            }
        }
        public static void Draw(Rect rect, FPWorkbenchSeriesView summary, FPWorkbenchPlotSettings settings, string label, FPWorkbenchOverlayView overlay = null, Color[] colors = null, FPWorkbenchOverlayAxes axes = null)
        {
            int navigation = GUIUtility.GetControlID(85132, FocusType.Passive);
            var input = Event.current;
            if (input.type == EventType.ScrollWheel && rect.Contains(input.mousePosition))
            { settings.Navigate(input.shift ? 1 : Math.Exp(input.delta.y * .1), (input.mousePosition.x - rect.x) / Math.Max(1, rect.width), input.shift ? input.delta.y * .05 : 0); input.Use(); }
            if (input.type == EventType.MouseDown && input.button == 2 && rect.Contains(input.mousePosition)) { GUIUtility.hotControl = navigation; input.Use(); }
            if (GUIUtility.hotControl == navigation)
            {
                if (input.type == EventType.MouseDrag) { settings.Navigate(1, 0, -input.delta.x / Math.Max(1, rect.width)); input.Use(); }
                else if (input.type == EventType.MouseUp) { GUIUtility.hotControl = 0; input.Use(); }
            }
            EditorGUI.DrawRect(rect, new Color(.1f, .1f, .1f));
            GUI.Label(new Rect(rect.x + 5, rect.y, rect.width - 10, 20), string.IsNullOrEmpty(settings.Title) ? label : settings.Title, EditorStyles.boldLabel);
            if (summary == null || double.IsNaN(overlay == null ? summary.Minimum : overlay.Minimum))
            { GUI.Label(new Rect(rect.x + 10, rect.y + 25, rect.width, 20), summary == null ? "Choose a numeric column and generate its plot." : "No valid numeric samples in this range; inspect values or calculation errors."); return; }
            FPWorkbenchAxisRange leftRange = default, rightRange = default;
            bool dual = overlay != null && axes != null && Array.Exists(axes.Right, side => side);
            if (overlay != null && axes != null)
            {
                try { leftRange = axes.Resolve(overlay, settings, false); rightRange = axes.Resolve(overlay, settings, true); }
                catch (ArgumentException ex) { GUI.Label(rect, ex.Message); return; }
            }
            float left = string.IsNullOrEmpty(settings.YLabel) ? 75 : 95;
            float bottom = string.IsNullOrEmpty(settings.XLabel) ? 50 : 70;
            Rect plot = new Rect(rect.x + left, rect.y + 25, Math.Max(1, rect.width - left - (dual ? 95 : 15)), Math.Max(1, rect.height - 25 - bottom));
            if (!string.IsNullOrEmpty(settings.XLabel))
                GUI.Label(new Rect(plot.x, plot.yMax + 20, plot.width, 18), new GUIContent(settings.XLabel, settings.XLabel), EditorStyles.centeredGreyMiniLabel);
            if (!string.IsNullOrEmpty(settings.YLabel))
            {
                Matrix4x4 matrix = GUI.matrix;
                Vector2 pivot = new Vector2(rect.x + 12, plot.center.y);
                try
                {
                    GUIUtility.RotateAroundPivot(-90, pivot);
                    GUI.Label(new Rect(pivot.x - plot.height * .5f, pivot.y - 9, plot.height, 18), new GUIContent(settings.YLabel, settings.YLabel), EditorStyles.centeredGreyMiniLabel);
                }
                finally { GUI.matrix = matrix; }
            }
            if (dual && !string.IsNullOrEmpty(axes.RightSettings.YLabel))
            {
                Matrix4x4 matrix = GUI.matrix; Vector2 pivot = new Vector2(rect.xMax - 12, plot.center.y);
                try { GUIUtility.RotateAroundPivot(-90, pivot); GUI.Label(new Rect(pivot.x - plot.height * .5f, pivot.y - 9, plot.height, 18), new GUIContent(axes.RightSettings.YLabel, axes.RightSettings.YLabel), EditorStyles.centeredGreyMiniLabel); }
                finally { GUI.matrix = matrix; }
            }
            double minimum = settings.LockY ? settings.MinimumY : (overlay?.Minimum ?? summary.Minimum), maximum = settings.LockY ? settings.MaximumY : (overlay?.Maximum ?? summary.Maximum);
            if (overlay != null && axes != null) { minimum = leftRange.Minimum; maximum = leftRange.Maximum; }
            if (double.IsNaN(minimum) || double.IsInfinity(minimum) || double.IsNaN(maximum) || double.IsInfinity(maximum) || maximum < minimum) { GUI.Label(rect, "Enter finite increasing Y limits."); return; }
            if (maximum == minimum) { minimum -= Math.Max(1, Math.Abs(minimum) * .01); maximum += Math.Max(1, Math.Abs(maximum) * .01); }
            float Y(double v) { double fraction = (v - minimum) / (maximum - minimum); return plot.y + (float)(settings.InvertY ? fraction : 1 - fraction) * plot.height; }
            float X(double v) => plot.x + (float)((v - summary.StartX) / (summary.EndX - summary.StartX)) * plot.width;
            try
            {
                foreach (double tick in overlay != null && axes != null && !leftRange.HasValues ? Array.Empty<double>() : FPWorkbenchPlot.Ticks(minimum, maximum, settings.YTickSpacing))
                {
                    float y = Y(tick);
                    if (settings.Grid) EditorGUI.DrawRect(new Rect(plot.x, y, plot.width, 1), new Color(1, 1, 1, .12f));
                    GUI.Label(new Rect(plot.x - 73, y - 8, 70, 18), tick.ToString("G5", CultureInfo.InvariantCulture), EditorStyles.miniLabel);
                }
                if (dual && rightRange.HasValues)
                    foreach (double tick in FPWorkbenchPlot.Ticks(rightRange.Minimum, rightRange.Maximum, axes.RightSettings.YTickSpacing))
                    {
                        double fraction = (tick - rightRange.Minimum) / (rightRange.Maximum - rightRange.Minimum);
                        float y = plot.y + (float)(axes.RightSettings.InvertY ? fraction : 1 - fraction) * plot.height;
                        if (axes.RightSettings.Grid) EditorGUI.DrawRect(new Rect(plot.x, y, plot.width, 1), new Color(1, 1, 1, .07f));
                        GUI.Label(new Rect(plot.xMax + 4, y - 8, 70, 18), tick.ToString("G5", CultureInfo.InvariantCulture), EditorStyles.miniLabel);
                    }
                foreach (double tick in FPWorkbenchPlot.Ticks(summary.StartX, summary.EndX, settings.XTickSpacing))
                {
                    float x = X(tick);
                    if (settings.Grid) EditorGUI.DrawRect(new Rect(x, plot.y, 1, plot.height), new Color(1, 1, 1, .12f));
                    GUI.Label(new Rect(x - 20, plot.yMax + 2, 70, 18), tick.ToString("G6", CultureInfo.InvariantCulture), EditorStyles.miniLabel);
                }
            }
            catch (ArgumentException ex) { GUI.Label(new Rect(plot.x, plot.y, plot.width, 20), ex.Message); }
            bool aggregated = false;
            if (Event.current.type == EventType.Repaint)
            {
                Handles.BeginGUI(); Color old = Handles.color; Handles.color = new Color(0, .7f, 1);
                var segment = new Vector3[2];
                for (int seriesIndex = 0; seriesIndex < (overlay?.Series.Count ?? 1); seriesIndex++)
                {
                var current = overlay == null ? summary : overlay.Series[seriesIndex];
                bool useRight = overlay != null && axes != null && axes.Right[seriesIndex];
                var range = useRight ? rightRange : leftRange;
                double seriesMin = overlay != null && axes != null ? range.Minimum : minimum, seriesMax = overlay != null && axes != null ? range.Maximum : maximum;
                bool invert = useRight ? axes.RightSettings.InvertY : settings.InvertY;
                float SeriesY(double value) { double fraction = (value - seriesMin) / (seriesMax - seriesMin); return plot.y + (float)(invert ? fraction : 1 - fraction) * plot.height; }
                Handles.color = colors != null && seriesIndex < colors.Length ? colors[seriesIndex] : new Color(0, .7f, 1);
                FPWorkbenchSeriesBin previous = default; bool continuous = false;
                void Line(float ax, float ay, float bx, float by)
                {
                    segment[0] = new Vector3(ax, ay); segment[1] = new Vector3(bx, by);
                    Handles.DrawAAPolyLine(settings.LineWidth, segment);
                }
                for (int i = 0; i < current.Bins.Length; i++)
                {
                    var bin = current.Bins[i]; if (bin.Count == 0 || bin.Maximum < seriesMin || bin.Minimum > seriesMax) { continuous = false; continue; }
                    float x = plot.x + (float)(((bin.FirstX + (bin.LastX - bin.FirstX) * .5) - summary.StartX) / (summary.EndX - summary.StartX)) * plot.width;
                    float low = Mathf.Clamp(SeriesY(bin.Minimum), plot.y, plot.yMax), high = Mathf.Clamp(SeriesY(bin.Maximum), plot.y, plot.yMax);
                    bool exact = bin.Count == 1 && bin.InvalidCount == 0;
                    if (!exact)
                    {
                        aggregated = true;
                        Line(x, low, x, high);
                        if (Math.Abs(low - high) < 1) EditorGUI.DrawRect(new Rect(x, low, 2, 1), Handles.color);
                    }
                    else if (settings.Style == FPWorkbenchPlotStyle.Points)
                    {
                        float size = settings.MarkerSize;
                        EditorGUI.DrawRect(new Rect(X(bin.FirstX) - size * .5f, SeriesY(bin.FirstY) - size * .5f, size, size), Handles.color);
                    }
                    else EditorGUI.DrawRect(new Rect(X(bin.FirstX), SeriesY(bin.FirstY), 1, 1), Handles.color);
                    if (settings.Style != FPWorkbenchPlotStyle.Points && continuous && bin.Count == 1 && previous.Count == 1 && bin.InvalidCount == 0 && bin.GapCount == 0 && previous.InvalidCount == 0 && previous.EndIndex == bin.FirstIndex && bin.FirstY >= seriesMin && bin.FirstY <= seriesMax && previous.LastY >= seriesMin && previous.LastY <= seriesMax)
                    {
                        if (settings.Style == FPWorkbenchPlotStyle.Step)
                        {
                            Line(X(previous.LastX), SeriesY(previous.LastY), X(bin.FirstX), SeriesY(previous.LastY));
                            Line(X(bin.FirstX), SeriesY(previous.LastY), X(bin.FirstX), SeriesY(bin.FirstY));
                        }
                        else Line(X(previous.LastX), SeriesY(previous.LastY), X(bin.FirstX), SeriesY(bin.FirstY));
                    }
                    previous = bin; continuous = bin.GapCount == 0;
                }
                }
                Handles.color = old; Handles.EndGUI();
                if (aggregated) GUI.Label(new Rect(plot.x + 4, plot.y + 2, plot.width - 8, 18), "Extrema envelopes • zoom in for individual samples", EditorStyles.miniLabel);
            }
            GUI.Label(new Rect(plot.x, plot.yMax + (string.IsNullOrEmpty(settings.XLabel) ? 20 : 40), plot.width, 18), (summary.OriginUtcTicks == 0 ? "X: bound numeric/row units" : "Elapsed s from " + new DateTime(summary.OriginUtcTicks, DateTimeKind.Utc).ToString("yyyy-MM-dd HH:mm:ss.ffffff 'UTC'", CultureInfo.InvariantCulture)) + "; cache records read: " + summary.RecordsRead, EditorStyles.miniLabel);
        }
    }
}
