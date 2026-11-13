// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.Globalization;
    using System.IO;
    using UnityEngine;

    public sealed partial class FPWorkbenchPoint3DWindow
    {
        private Texture2D AnnotatePointImage(Texture2D cloud, Camera camera, Rect plot, int width, int height, ImageProvenance metadata)
        {
            using var canvas = new FPWorkbenchImageCanvas(); canvas.BackgroundColor = presentation.WhiteBackground ? Color.white : new Color(.1f, .1f, .1f); canvas.TextColor = presentation.WhiteBackground ? Color.black : Color.white; canvas.Image(cloud, plot);
            var schema = new FPWorkbenchTableView(dataset, program); var frame = batch.Prepared.Frame; var b = batch.View.Bounds;
            metadata.AxisLabels = new string[3]; metadata.AxisUnits = new string[3];
            metadata.AxisBounds = new[] { b.MinimumX, b.MaximumX, b.MinimumY, b.MaximumY, b.MinimumZ, b.MaximumZ };
            double[] minimum = { b.MinimumX, b.MinimumY, b.MinimumZ }, maximum = { b.MaximumX, b.MaximumY, b.MaximumZ };
            Color[] colors = { new Color(1, .4f, .35f), new Color(.35f, .9f, .45f), new Color(.35f, .65f, 1) };
            string[] axis = { "X", "Y", "Z" };
            for (int i = 0; i < 3; i++) {
                int c = batch.Columns[i]; metadata.AxisLabels[i] = schema.ColumnName(c);
                metadata.AxisUnits[i] = program == null ? "unspecified" : c < dataset.ColumnCount ? program.SourceUnits(c) : program.Units(c - dataset.ColumnCount);
                float y = 96 - i * 26;
                canvas.Quad(45, y - 4, 12, 8, colors[i]);
                string units = metadata.AxisUnits[i];
                string label = metadata.AxisLabels[i];
                if (units != "unspecified" && label.IndexOf('[') < 0) label += " (" + units + ")";
                canvas.Text(axis[i] + ": " + label, 65, y, TextAnchor.MiddleLeft);
                string low = minimum[i].ToString("G5", CultureInfo.InvariantCulture), high = maximum[i].ToString("G5", CultureInfo.InvariantCulture);
                if (low == high && minimum[i] != maximum[i]) { low = minimum[i].ToString("G17", CultureInfo.InvariantCulture); high = maximum[i].ToString("G17", CultureInfo.InvariantCulture); }
                canvas.Text("[" + low + ", " + high + "]", width - 45, y, TextAnchor.MiddleRight);
            }
            // Reuse the same uniform source-to-local presentation frame and the export camera; data is untouched.
            Vector3 Local(double x, double y, double z) => new Vector3((float)((x - frame.OriginX) / frame.SourceUnitsPerLocalUnit), (float)((y - frame.OriginY) / frame.SourceUnitsPerLocalUnit), (float)((z - frame.OriginZ) / frame.SourceUnitsPerLocalUnit));
            Vector3 Project(Vector3 local) {
                var p = camera.WorldToViewportPoint(local); return new Vector3(plot.x + p.x * plot.width, plot.y + p.y * plot.height, p.z);
            }
            bool Inside(Vector3 p) => p.z > camera.nearClipPlane && plot.Contains(new Vector2(p.x, p.y));
            var corners = new Vector3[8]; var local = new Vector3[8];
            for (int i = 0; i < 8; i++) { local[i] = Local((i & 1) == 0 ? b.MinimumX : b.MaximumX, (i & 2) == 0 ? b.MinimumY : b.MaximumY, (i & 4) == 0 ? b.MinimumZ : b.MaximumZ); corners[i] = Project(local[i]); }
            for (int i = 0; i < 8; i++) for (int a = 0; a < 3; a++) {
                int j = i ^ (1 << a); if (!AxisVisible(a) || j < i || !ProjectSegment(camera, plot, local[i], local[j], out var first, out var last, false)) continue;
                canvas.Line(first.x, first.y, last.x, last.y, new Color(.4f, .4f, .4f));
            }
            for (int a = 0; a < 3; a++) {
                var end = corners[1 << a]; if (!AxisVisible(a)) continue;
                if (ProjectSegment(camera, plot, local[0], local[1 << a], out var first, out var last, false)) canvas.Line(first.x, first.y, last.x, last.y, colors[a], 2);
                if (showAxisValues && ProjectSegment(camera, plot, local[0], local[1 << a], out var tickStart, out var tickEnd, false)) {
                    var delta = tickEnd - tickStart; var normal = new Vector3(-delta.y, delta.x).normalized;
                    Vector3 previous = new Vector3(float.PositiveInfinity, float.PositiveInfinity);
                    for (int tick = 0; tick <= 4; tick++) {
                        var point = Project(AxisTick(a, tick, out var text)); if (!Inside(point)) continue;
                        if (Vector3.Distance(previous, point) < Math.Max(45, text.Length * 7)) continue;
                        canvas.Line(point.x - normal.x * 3, point.y - normal.y * 3, point.x + normal.x * 3, point.y + normal.y * 3, colors[a]);
                        canvas.Text(text, point.x + normal.x * 12, point.y + normal.y * 12); previous = point;
                    }
                }
                if (!Inside(end)) continue;
                canvas.Text(axis[a], Mathf.Clamp(end.x + 15, plot.x + 12, plot.xMax - 12), Mathf.Clamp(end.y + 15, plot.y + 12, plot.yMax - 12));
            }
            if (showOrigin && b.MinimumX <= 0 && b.MaximumX >= 0 && b.MinimumY <= 0 && b.MaximumY >= 0 && b.MinimumZ <= 0 && b.MaximumZ >= 0) {
                var zero = Project(SourceLocal(0, 0, 0));
                if (Inside(zero)) {
                    canvas.Line(zero.x - 6, zero.y, zero.x + 6, zero.y, presentation.WhiteBackground ? Color.black : Color.white, 2);
                    canvas.Line(zero.x, zero.y - 6, zero.x, zero.y + 6, presentation.WhiteBackground ? Color.black : Color.white, 2);
                    canvas.Text("(0, 0, 0)", zero.x + 8, zero.y - 12, TextAnchor.MiddleLeft);
                }
            }
            string source = Path.GetFileNameWithoutExtension(dataset.SourcePath); if (source.Length > 64) source = source.Substring(0, 61) + "...";
            float LabelPosition(FPWorkbenchLabelAlignment a, float min, float max) => a == FPWorkbenchLabelAlignment.Left ? min : a == FPWorkbenchLabelAlignment.Right ? max : (min + max) / 2;
            TextAnchor LabelAnchor(FPWorkbenchLabelAlignment a) => a == FPWorkbenchLabelAlignment.Left ? TextAnchor.MiddleLeft : a == FPWorkbenchLabelAlignment.Right ? TextAnchor.MiddleRight : TextAnchor.MiddleCenter;
            canvas.TextColor = FPWorkbenchPlotView.TextColor(presentation, presentation.CustomTitleColor, presentation.TitleColor);
            canvas.Text(string.IsNullOrEmpty(presentation.Title) ? source + " | 3D point cloud" : presentation.Title, LabelPosition(presentation.TitleAlignment, 10, width - 10), height - Mathf.Max(24, presentation.TitleFontSize / 2f + 4), LabelAnchor(presentation.TitleAlignment), fontSize: presentation.TitleFontSize);
            if (!string.IsNullOrEmpty(presentation.XLabel)) { canvas.TextColor = FPWorkbenchPlotView.TextColor(presentation, presentation.CustomXLabelColor, presentation.XLabelColor); canvas.Text(presentation.XLabel, LabelPosition(presentation.XLabelAlignment, plot.x, plot.xMax), plot.y + 14, LabelAnchor(presentation.XLabelAlignment), fontSize: presentation.XLabelFontSize); }
            if (!string.IsNullOrEmpty(presentation.YLabel)) {
                canvas.TextColor = FPWorkbenchPlotView.TextColor(presentation, presentation.CustomYLabelColor, presentation.YLabelColor);
                FPWorkbenchPlotView.ExportYLabel(canvas, presentation.YLabel, presentation, plot.x + 14, plot.y, plot.yMax);
            }
            canvas.TextColor = presentation.WhiteBackground ? Color.black : Color.white;
            canvas.Text("Bound XYZ values; no trajectory reconstruction. Invalid source rows: " + metadata.InvalidSourceRows.ToString("N0", CultureInfo.InvariantCulture), width / 2f, 20);
            pointsOfInterest.Export(canvas, plot, width, height, InterestAxisKey, p => ProjectInterest(p, camera, plot, true), graphSelection.Color, sourceSheetName);
            return canvas.Render(width, height);
        }
    }
}
