// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.Globalization;
    using UnityEditor;
    using UnityEngine;

    internal static class FPWorkbenchPlotView
    {
        public const string UtcFormatTooltip = "Complete UTC date: dd.MM.yyyy, yyyy-MM-dd, MM/dd/yyyy, dd/MM/yyyy or yyyy/MM/dd; then one space and HH:mm:ss, optionally . or , plus 1–7 required f digits. No missing-year/current-date or timezone inference.";
        public static void LabelControls(FPWorkbenchPlotSettings settings)
        {
            FPWorkbenchPlotPresetGUI.Draw(settings);
            settings.XScale = (FPWorkbenchAxisScale)EditorGUILayout.EnumPopup("X scale", settings.XScale);
            settings.YScale = (FPWorkbenchAxisScale)EditorGUILayout.EnumPopup("Y scale", settings.YScale);
            EditorGUILayout.LabelField("Log10 requires positive limits; grid spacing is in decades.", EditorStyles.miniLabel);
            settings.Title = EditorGUILayout.TextField("Title", settings.Title);
            settings.XLabel = EditorGUILayout.TextField("X label", settings.XLabel);
            settings.YLabel = EditorGUILayout.TextField("Y label", settings.YLabel);
            PresentationControls(settings);
        }
        internal static void PresentationControls(FPWorkbenchPlotSettings s)
        {
            s.WhiteBackground = EditorGUILayout.Toggle("White background", s.WhiteBackground);
            s.TitleFontSize = EditorGUILayout.IntSlider("Title size", s.TitleFontSize, 8, 64);
            s.TitleAlignment = (FPWorkbenchLabelAlignment)EditorGUILayout.EnumPopup("Title alignment", s.TitleAlignment);
            s.CustomTitleColor = EditorGUILayout.Toggle("Custom title color", s.CustomTitleColor);
            if (s.CustomTitleColor) s.TitleColor = EditorGUILayout.ColorField("Title color", s.TitleColor);
            s.XLabelFontSize = EditorGUILayout.IntSlider("X label size", s.XLabelFontSize, 8, 64);
            s.XLabelAlignment = (FPWorkbenchLabelAlignment)EditorGUILayout.EnumPopup("X label alignment", s.XLabelAlignment);
            s.CustomXLabelColor = EditorGUILayout.Toggle("Custom X label color", s.CustomXLabelColor);
            if (s.CustomXLabelColor) s.XLabelColor = EditorGUILayout.ColorField("X label color", s.XLabelColor);
            s.YLabelFontSize = EditorGUILayout.IntSlider("Y label size", s.YLabelFontSize, 8, 64);
            s.YLabelAlignment = (FPWorkbenchLabelAlignment)EditorGUILayout.EnumPopup("Y label alignment", s.YLabelAlignment);
            s.YLabelDirection = (FPWorkbenchYLabelDirection)EditorGUILayout.EnumPopup("Y label direction", s.YLabelDirection);
            if (s.YLabelDirection == FPWorkbenchYLabelDirection.StackedTopToBottom)
                s.StackedYCharacterSpacing = EditorGUILayout.Slider("Character spacing", s.StackedYCharacterSpacing, 0, 20);
            s.CustomYLabelColor = EditorGUILayout.Toggle("Custom Y label color", s.CustomYLabelColor);
            if (s.CustomYLabelColor) s.YLabelColor = EditorGUILayout.ColorField("Y label color", s.YLabelColor);
        }
        internal static Color TextColor(FPWorkbenchPlotSettings s, bool custom, Color selected) => custom ? selected : s.WhiteBackground ? Color.black : Color.white;
        internal static GUIStyle LabelStyle(FPWorkbenchPlotSettings s, int size, FPWorkbenchLabelAlignment alignment, bool bold, bool custom = false, Color selected = default)
        {
            var style = new GUIStyle(bold ? EditorStyles.boldLabel : EditorStyles.label) { fontSize = Mathf.Clamp(size, 8, 64), alignment = alignment == FPWorkbenchLabelAlignment.Left ? TextAnchor.MiddleLeft : alignment == FPWorkbenchLabelAlignment.Right ? TextAnchor.MiddleRight : TextAnchor.MiddleCenter, clipping = TextClipping.Clip };
            style.normal.textColor = TextColor(s, custom, selected); return style;
        }
        internal static string StackLabel(string value)
        {
            var parts = new System.Collections.Generic.List<string>();
            var elements = StringInfo.GetTextElementEnumerator(value ?? "");
            while (elements.MoveNext()) { string part = elements.GetTextElement(); parts.Add(part == "\r" || part == "\n" || part == "\r\n" ? " " : part); }
            return string.Join("\n", parts);
        }
        internal static void DrawYLabel(string value, FPWorkbenchPlotSettings settings, float x, float top, float height)
        {
            if (string.IsNullOrEmpty(value)) return;
            var style = LabelStyle(settings, settings.YLabelFontSize, settings.YLabelAlignment, false, settings.CustomYLabelColor, settings.YLabelColor);
            if (settings.YLabelDirection == FPWorkbenchYLabelDirection.StackedTopToBottom) {
                var characters = StackLabel(value).Split('\n');
                float size = Mathf.Clamp(settings.YLabelFontSize, 8, 64), step = size * 1.2f + Mathf.Clamp(settings.StackedYCharacterSpacing, 0, 20);
                float total = size * 1.2f + Math.Max(0, characters.Length - 1) * step;
                float offset = settings.YLabelAlignment == FPWorkbenchLabelAlignment.Left ? 0 : settings.YLabelAlignment == FPWorkbenchLabelAlignment.Right ? height - total : (height - total) / 2;
                style.alignment = TextAnchor.MiddleCenter;
                GUI.BeginClip(new Rect(x - 40, top, 80, height));
                try { for (int i = 0; i < characters.Length; i++) GUI.Label(new Rect(0, offset + i * step, 80, size * 1.2f), characters[i], style); }
                finally { GUI.EndClip(); }
                return;
            }
            var old = GUI.matrix; var pivot = new Vector2(x, top + height / 2);
            try { GUIUtility.RotateAroundPivot(settings.YLabelDirection == FPWorkbenchYLabelDirection.TopToBottom ? 90 : -90, pivot);
                GUI.Label(new Rect(pivot.x - height / 2, pivot.y - Mathf.Max(16, settings.YLabelFontSize / 2f + 2), height, Mathf.Max(32, settings.YLabelFontSize + 4)), value, style);
            } finally { GUI.matrix = old; }
        }
        internal static void ExportYLabel(FPWorkbenchImageCanvas canvas, string value, FPWorkbenchPlotSettings settings, float x, float bottom, float top)
        {
            if (string.IsNullOrEmpty(value)) return;
            if (settings.YLabelDirection == FPWorkbenchYLabelDirection.StackedTopToBottom) {
                var characters = StackLabel(value).Split('\n');
                float size = Mathf.Clamp(settings.YLabelFontSize, 8, 64), step = size * 1.2f + Mathf.Clamp(settings.StackedYCharacterSpacing, 0, 20);
                float total = size * 1.2f + Math.Max(0, characters.Length - 1) * step;
                if (characters.Length > 256 || total > top - bottom) throw new ArgumentException("Increase PNG height, shorten the Y label, or reduce its font size or character spacing to fit the stacked label.");
                float stackTop = settings.YLabelAlignment == FPWorkbenchLabelAlignment.Left ? top : settings.YLabelAlignment == FPWorkbenchLabelAlignment.Right ? bottom + total : (top + bottom + total) / 2;
                for (int i = 0; i < characters.Length; i++) canvas.Text(characters[i], x, stackTop - size * .6f - i * step, TextAnchor.MiddleCenter, fontSize: settings.YLabelFontSize);
                return;
            }
            bool down = settings.YLabelDirection == FPWorkbenchYLabelDirection.TopToBottom;
            var a = settings.YLabelAlignment; float start = down ? top : bottom, end = down ? bottom : top;
            float position = a == FPWorkbenchLabelAlignment.Left ? start : a == FPWorkbenchLabelAlignment.Right ? end : (start + end) / 2;
            var horizontal = a == FPWorkbenchLabelAlignment.Left ? TextAnchor.MiddleLeft : a == FPWorkbenchLabelAlignment.Right ? TextAnchor.MiddleRight : TextAnchor.MiddleCenter;
            canvas.Text(value, x, position, horizontal, down ? -90 : 90, settings.YLabelFontSize);
        }
        public static void StyleControls(FPWorkbenchPlotSettings settings, bool compact = false, string styleLabel = "Plot style")
        {
            if (compact) settings.Style = (FPWorkbenchPlotStyle)EditorGUILayout.EnumPopup(settings.Style, GUILayout.Width(75));
            else
            {
                settings.Style = (FPWorkbenchPlotStyle)EditorGUILayout.EnumPopup(styleLabel, settings.Style);
                using (new EditorGUI.DisabledScope(settings.Style == FPWorkbenchPlotStyle.Points))
                    settings.LineWidth = EditorGUILayout.Slider("Line width", settings.LineWidth, 1, 8);
                settings.MarkerSize = EditorGUILayout.Slider("Point size", settings.MarkerSize, 1, 16);
            }
        }
        internal static bool ContainsPoint(Rect rect, Vector2 point) => point.x >= rect.x && point.x <= rect.xMax && point.y >= rect.y && point.y <= rect.yMax;
        internal static long GeometryVertices(FPWorkbenchSeriesView view, FPWorkbenchPlotStyle style)
        {
            long count = 0; foreach (var bin in view.Bins) count += bin.Count == 1 && bin.InvalidCount == 0 ? (style == FPWorkbenchPlotStyle.Points ? 4 : style == FPWorkbenchPlotStyle.Step ? 12 : 8) : (style == FPWorkbenchPlotStyle.Points ? 16 : style == FPWorkbenchPlotStyle.Step ? 52 : 36);
            return count;
        }
        internal static Rect PlotRect(Rect rect, FPWorkbenchPlotSettings settings, bool dual = false)
        {
            float left = string.IsNullOrEmpty(settings.YLabel) ? 75 : Math.Max(95, settings.YLabelFontSize + 65);
            float bottom = string.IsNullOrEmpty(settings.XLabel) ? 50 : Math.Max(70, settings.XLabelFontSize + 42);
            float top = Math.Max(25, settings.TitleFontSize + 5);
            return new Rect(rect.x + left, rect.y + top, Math.Max(1, rect.width - left - (dual ? 95 : 15)), Math.Max(1, rect.height - top - bottom));
        }
        internal static bool TryScalarAxes(Rect rect, FPWorkbenchSeriesView view, FPWorkbenchPlotSettings settings,
            out Rect area, out FPWorkbenchAxisMapping x, out FPWorkbenchAxisMapping y)
        {
            area = PlotRect(rect, settings); x = y = null;
            if (view == null || double.IsNaN(view.Minimum)) return false;
            try {
                double min = settings.LockY ? settings.MinimumY : view.Minimum, max = settings.LockY ? settings.MaximumY : view.Maximum;
                if (min == max) FPWorkbenchAxis.ExpandConstant(min, settings.YScale, out min, out max);
                x = new FPWorkbenchAxisMapping(view.StartX, view.EndX, settings.XScale); y = new FPWorkbenchAxisMapping(min, max, settings.YScale); return true;
            } catch (ArgumentException) { return false; }
        }
        internal static bool TryProjectScalar(Rect rect, FPWorkbenchSeriesView view, FPWorkbenchPlotSettings settings, FPWorkbenchSeriesPoint point, out Vector2 screen)
        {
            screen = default;
            if (!point.IsValid || !TryScalarAxes(rect, view, settings, out var area, out var x, out var y)) return false;
            try { double fy = y.Fraction(point.Y); screen = new Vector2(area.x + (float)x.Fraction(point.X) * area.width, area.y + (float)(settings.InvertY ? fy : 1 - fy) * area.height); return screen.x >= area.x && screen.x <= area.xMax && screen.y >= area.y && screen.y <= area.yMax; }
            catch (ArgumentOutOfRangeException) { return false; }
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
            EditorGUI.DrawRect(rect, settings.WhiteBackground ? Color.white : new Color(.1f, .1f, .1f));
            GUI.Label(new Rect(rect.x + 5, rect.y, rect.width - 10, Mathf.Max(20, settings.TitleFontSize + 4)), string.IsNullOrEmpty(settings.Title) ? label : settings.Title, LabelStyle(settings, settings.TitleFontSize, settings.TitleAlignment, true, settings.CustomTitleColor, settings.TitleColor));
            if (summary == null || double.IsNaN(overlay == null ? summary.Minimum : overlay.Minimum))
            { GUI.Label(new Rect(rect.x + 10, rect.y + 25, rect.width, 20), summary == null ? "Choose a numeric column and generate its plot." : "No valid numeric samples in this range; inspect values or calculation errors."); return; }
            FPWorkbenchAxisRange leftRange = default, rightRange = default;
            bool dual = overlay != null && axes != null && Array.Exists(axes.Right, side => side);
            if (overlay != null && axes != null)
            {
                try { leftRange = axes.Resolve(overlay, settings, false); rightRange = axes.Resolve(overlay, settings, true); }
                catch (ArgumentException ex) { GUI.Label(rect, ex.Message); return; }
            }
            Rect plot = PlotRect(rect, settings, dual);
            if (!string.IsNullOrEmpty(settings.XLabel))
                GUI.Label(new Rect(plot.x, plot.yMax + 20, plot.width, Mathf.Max(18, settings.XLabelFontSize + 4)), new GUIContent(settings.XLabel, settings.XLabel), LabelStyle(settings, settings.XLabelFontSize, settings.XLabelAlignment, false, settings.CustomXLabelColor, settings.XLabelColor));
            DrawYLabel(settings.YLabel, settings, rect.x + Mathf.Max(12, settings.YLabelFontSize / 2f + 3), plot.y, plot.height);
            if (dual && !string.IsNullOrEmpty(axes.RightSettings.YLabel))
            {
                Matrix4x4 matrix = GUI.matrix; Vector2 pivot = new Vector2(rect.xMax - 12, plot.center.y);
                try { GUIUtility.RotateAroundPivot(-90, pivot); GUI.Label(new Rect(pivot.x - plot.height * .5f, pivot.y - 9, plot.height, 18), new GUIContent(axes.RightSettings.YLabel, axes.RightSettings.YLabel), EditorStyles.centeredGreyMiniLabel); }
                finally { GUI.matrix = matrix; }
            }
            double minimum = settings.LockY ? settings.MinimumY : (overlay?.Minimum ?? summary.Minimum), maximum = settings.LockY ? settings.MaximumY : (overlay?.Maximum ?? summary.Maximum);
            if (overlay != null && axes != null) { minimum = leftRange.Minimum; maximum = leftRange.Maximum; }
            if (double.IsNaN(minimum) || double.IsInfinity(minimum) || double.IsNaN(maximum) || double.IsInfinity(maximum) || maximum < minimum) { GUI.Label(rect, "Enter finite increasing Y limits."); return; }
            FPWorkbenchAxisMapping xMap, leftMap = null, rightMap = null;
            try {
                if (maximum == minimum) FPWorkbenchAxis.ExpandConstant(minimum, settings.YScale, out minimum, out maximum);
                xMap = new FPWorkbenchAxisMapping(summary.StartX, summary.EndX, settings.XScale);
                if (overlay == null || axes == null || leftRange.HasValues) leftMap = new FPWorkbenchAxisMapping(minimum, maximum, settings.YScale);
                if (dual && rightRange.HasValues) rightMap = new FPWorkbenchAxisMapping(rightRange.Minimum, rightRange.Maximum, axes.RightSettings.YScale);
            } catch (ArgumentException ex) { GUI.Label(new Rect(rect.x + 5, rect.y + 25, rect.width - 10, 40), ex.Message + " Set positive limits for logarithmic axes."); return; }
            float Y(double v) { double fraction = leftMap.Fraction(v); return plot.y + (float)(settings.InvertY ? fraction : 1 - fraction) * plot.height; }
            float X(double v) => plot.x + (float)xMap.Fraction(Math.Max(summary.StartX, Math.Min(summary.EndX, v))) * plot.width;
            try
            {
                foreach (double tick in overlay != null && axes != null && !leftRange.HasValues ? Array.Empty<double>() : FPWorkbenchAxis.Ticks(minimum, maximum, settings.YScale, settings.YTickSpacing))
                {
                    float y = Y(tick);
                    if (settings.Grid) EditorGUI.DrawRect(new Rect(plot.x, y, plot.width, 1), settings.WhiteBackground ? new Color(0, 0, 0, .18f) : new Color(1, 1, 1, .12f));
                    GUI.Label(new Rect(plot.x - 73, y - 8, 70, 18), tick.ToString("G5", CultureInfo.InvariantCulture), LabelStyle(settings, 11, FPWorkbenchLabelAlignment.Left, false));
                }
                if (dual && rightRange.HasValues)
                    foreach (double tick in FPWorkbenchAxis.Ticks(rightRange.Minimum, rightRange.Maximum, axes.RightSettings.YScale, axes.RightSettings.YTickSpacing))
                    {
                        double fraction = rightMap.Fraction(tick);
                        float y = plot.y + (float)(axes.RightSettings.InvertY ? fraction : 1 - fraction) * plot.height;
                        if (axes.RightSettings.Grid) EditorGUI.DrawRect(new Rect(plot.x, y, plot.width, 1), settings.WhiteBackground ? new Color(0, 0, 0, .12f) : new Color(1, 1, 1, .07f));
                        GUI.Label(new Rect(plot.xMax + 4, y - 8, 70, 18), tick.ToString("G5", CultureInfo.InvariantCulture), LabelStyle(settings, 11, FPWorkbenchLabelAlignment.Left, false));
                    }
                foreach (double tick in FPWorkbenchAxis.Ticks(summary.StartX, summary.EndX, settings.XScale, settings.XTickSpacing))
                {
                    float x = X(tick);
                    if (settings.Grid) EditorGUI.DrawRect(new Rect(x, plot.y, 1, plot.height), settings.WhiteBackground ? new Color(0, 0, 0, .18f) : new Color(1, 1, 1, .12f));
                    GUI.Label(new Rect(x - 20, plot.yMax + 2, 70, 18), tick.ToString("G6", CultureInfo.InvariantCulture), LabelStyle(settings, 11, FPWorkbenchLabelAlignment.Left, false));
                }
            }
            catch (ArgumentException ex) { GUI.Label(new Rect(plot.x, plot.y, plot.width, 20), ex.Message); }
            bool aggregated = false; int logOmitted = 0;
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
                var yMap = useRight ? rightMap : leftMap; var yScale = useRight ? axes.RightSettings.YScale : settings.YScale;
                float SeriesY(double value) { double fraction = yMap.Fraction(Math.Max(seriesMin, Math.Min(seriesMax, value))); return plot.y + (float)(invert ? fraction : 1 - fraction) * plot.height; }
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
                    if (settings.XScale == FPWorkbenchAxisScale.Log10 && bin.FirstX <= 0 || yScale == FPWorkbenchAxisScale.Log10 && bin.Minimum <= 0) { logOmitted++; continuous = false; continue; }
                    float x = (X(bin.FirstX) + X(bin.LastX)) * .5f;
                    float low = Mathf.Clamp(SeriesY(bin.Minimum), plot.y, plot.yMax), high = Mathf.Clamp(SeriesY(bin.Maximum), plot.y, plot.yMax);
                    bool exact = bin.Count == 1 && bin.InvalidCount == 0;
                    if (!exact)
                    {
                        aggregated = true;
                        if (bin.RepresentativeCount > 0) {
                            FPWorkbenchSeriesPoint? prior = null;
                            for (int j = bin.RepresentativeFirst; j < bin.RepresentativeFirst + bin.RepresentativeCount; j++) {
                                var point = current.Representatives[j];
                                if (point.Y < seriesMin || point.Y > seriesMax) { prior = null; continue; }
                                float marker = settings.MarkerSize;
                                EditorGUI.DrawRect(new Rect(X(point.X) - marker * .5f, SeriesY(point.Y) - marker * .5f, marker, marker), Handles.color);
                                if (settings.Style != FPWorkbenchPlotStyle.Points && bin.InvalidCount == 0 && bin.GapCount == 0 && prior.HasValue) {
                                    if (settings.Style == FPWorkbenchPlotStyle.Step) {
                                        Line(X(prior.Value.X), SeriesY(prior.Value.Y), X(point.X), SeriesY(prior.Value.Y)); Line(X(point.X), SeriesY(prior.Value.Y), X(point.X), SeriesY(point.Y));
                                    } else Line(X(prior.Value.X), SeriesY(prior.Value.Y), X(point.X), SeriesY(point.Y));
                                }
                                prior = point;
                            }
                        } else {
                            float marker = settings.MarkerSize;
                            if (settings.Style == FPWorkbenchPlotStyle.Points) {
                                EditorGUI.DrawRect(new Rect(x - marker * .5f, low - marker * .5f, marker, marker), Handles.color);
                                EditorGUI.DrawRect(new Rect(x - marker * .5f, high - marker * .5f, marker, marker), Handles.color);
                            } else if (bin.InvalidCount == 0 && bin.GapCount == 0 && bin.FirstY >= seriesMin && bin.FirstY <= seriesMax && bin.LastY >= seriesMin && bin.LastY <= seriesMax) {
                                if (settings.Style == FPWorkbenchPlotStyle.Step) { Line(X(bin.FirstX), SeriesY(bin.FirstY), X(bin.LastX), SeriesY(bin.FirstY)); Line(X(bin.LastX), SeriesY(bin.FirstY), X(bin.LastX), SeriesY(bin.LastY)); }
                                else Line(X(bin.FirstX), SeriesY(bin.FirstY), X(bin.LastX), SeriesY(bin.LastY));
                            }
                        }
                        if (settings.Style != FPWorkbenchPlotStyle.Points) {
                            var rangeColor = Handles.color; Handles.color = new Color(rangeColor.r, rangeColor.g, rangeColor.b, .3f);
                            Line(x, low, x, high); Handles.color = rangeColor;
                        }
                    }
                    else
                    {
                        float size = settings.MarkerSize;
                        EditorGUI.DrawRect(new Rect(X(bin.FirstX) - size * .5f, SeriesY(bin.FirstY) - size * .5f, size, size), Handles.color);
                    }

                    if (settings.Style != FPWorkbenchPlotStyle.Points && continuous && bin.InvalidCount == 0 && bin.GapCount == 0 && previous.InvalidCount == 0 && previous.EndIndex == bin.FirstIndex && bin.FirstY >= seriesMin && bin.FirstY <= seriesMax && previous.LastY >= seriesMin && previous.LastY <= seriesMax)
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
                if (logOmitted > 0) GUI.Label(new Rect(plot.x + 4, plot.y + 20, plot.width - 8, 18), "Log10: " + logOmitted + " nonpositive/mixed envelopes omitted; zoom for individual samples", EditorStyles.miniLabel);
                if (aggregated) GUI.Label(new Rect(plot.x + 4, plot.y + 2, plot.width - 8, 18), settings.Style == FPWorkbenchPlotStyle.Points ? "Represented source samples • zoom for all samples" : "Faint ranges: binned min/max • markers: source samples • zoom for all samples", EditorStyles.miniLabel);
            }
            GUI.Label(new Rect(plot.x, plot.yMax + (string.IsNullOrEmpty(settings.XLabel) ? 20 : 40), plot.width, 18), (summary.OriginUtcTicks == 0 ? "X: bound numeric/row units" : "Elapsed s from " + new DateTime(summary.OriginUtcTicks, DateTimeKind.Utc).ToString("yyyy-MM-dd HH:mm:ss.ffffff 'UTC'", CultureInfo.InvariantCulture)) + "; cache records read: " + summary.RecordsRead, EditorStyles.miniLabel);
        }
    }
}
