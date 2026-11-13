// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.Threading.Tasks;
    using UnityEditor;
    using UnityEngine;

    public sealed partial class FPWorkbenchFigureWindow
    {
        [SerializeField] private int imageWidth = 1600, imageHeight = 1200;
        [SerializeField] private bool showImageExport;

        private FPWorkbenchEditorImageJob imageJob;
        private FPWorkbenchEditorImageJob ImageJob => imageJob ??= new FPWorkbenchEditorImageJob(Repaint);
        [Serializable] private sealed class ScalarImageProvenance : FPWorkbenchImageProvenance
        {
            public string Format = "FuzzPhyte.DataWorkbench.ScalarImage", CorrectionsJson, CalculationsJson;
            public int Version = 1, DisplayedBins, OmittedLogBins;
            public long OriginUtcTicks;
            public bool Aggregated;
            public FPWorkbenchSheetDocument Sheet;
        }
        private Task<FPWorkbenchImageExportResult> ExportPng(string directory, int width, int height)
        {
            if (width < 320 || height < 240 || width > 4096 || height > 4096 || (long)width * height > 4194304)
                throw new ArgumentOutOfRangeException("Use 320–4096 width, 240–4096 height and at most 4,194,304 pixels.");
            var session = lease?.Session; var view = session?.View;
            if (view == null || session.IsBusy || session.Error != null || session.Series == null || view.StartX != session.Settings.StartX || view.EndX != session.Settings.EndX)
                throw new InvalidOperationException("Wait for a successful current scalar view before exporting.");
            string output = Path.GetFullPath(directory), parent = Path.GetDirectoryName(Path.GetFullPath(directory));
            if (!output.EndsWith(".fpfigure", StringComparison.OrdinalIgnoreCase) || Directory.Exists(output) || File.Exists(output)) throw new IOException("Choose a new .fpfigure output directory.");
            if (!Directory.Exists(parent)) throw new DirectoryNotFoundException(parent);
            using var reservation = FPWorkbenchEditorImageReservation.Acquire(width, height, checked(4096L + FPWorkbenchPlotView.GeometryVertices(view, session.Settings.Style)));
            var sheet = JsonUtility.FromJson<FPWorkbenchSheetDocument>(JsonUtility.ToJson(document));
            sheet.Binding = session.Series.Binding; sheet.Plot = session.Settings;
            var metadata = new ScalarImageProvenance { Width = width, Height = height, Sheet = sheet, DisplayedBins = view.Bins.Length,
                OriginUtcTicks = view.OriginUtcTicks, CorrectionsJson = session.ChangeSnapshot?.Json,
                CalculationsJson = session.CalculationProgram == null ? null : JsonUtility.ToJson(session.CalculationProgram.Definition) };
            Texture2D image = null;
            try {
                image = RenderScalarImage(view, session.Settings, session.YName, width, height, out metadata.Aggregated, out metadata.OmittedLogBins);
                return reservation.Publish(output, image.GetPixels32(), width, height, metadata, imageJob?.Token ?? default);
            }
            finally { if (image != null) DestroyImmediate(image); }
        }
        private static Texture2D RenderScalarImage(FPWorkbenchSeriesView view, FPWorkbenchPlotSettings settings, string label, int width, int height, out bool aggregated, out int logOmitted)
            => RenderSeriesImage(view, settings, label, width, height, out aggregated, out logOmitted);
        internal static Texture2D RenderSeriesImage(FPWorkbenchSeriesView view, FPWorkbenchPlotSettings settings, string label, int width, int height, out bool aggregated, out int logOmitted,
            FPWorkbenchOverlayView overlay = null, Color[] colors = null, FPWorkbenchOverlayAxes axes = null, string[] legends = null, FPWorkbenchFigurePoints interests = null, string interestKey = "", Color? interestColor = null, string sheetName = "")
        {
            double min = settings.LockY ? settings.MinimumY : view.Minimum, max = settings.LockY ? settings.MaximumY : view.Maximum;
            if ((overlay == null || axes == null) && min == max) FPWorkbenchAxis.ExpandConstant(min, settings.YScale, out min, out max);
            bool hasLeft = true, dual = overlay != null && axes != null && Array.Exists(axes.Right, side => side);
            FPWorkbenchAxisRange rightRange = default;
            if (overlay != null && axes != null) {
                var left = axes.Resolve(overlay, settings, false); hasLeft = left.HasValues; min = left.Minimum; max = left.Maximum;
                if (dual) rightRange = axes.Resolve(overlay, settings, true);
            }
            var xMap = new FPWorkbenchAxisMapping(view.StartX, view.EndX, settings.XScale);
            var yMap = hasLeft ? new FPWorkbenchAxisMapping(min, max, settings.YScale) : null;
            var rightMap = dual && rightRange.HasValues ? new FPWorkbenchAxisMapping(rightRange.Minimum, rightRange.Maximum, axes.RightSettings.YScale) : null;
            int legendRows = legends == null ? 0 : (legends.Length + 1) / 2;
            using var canvas = new FPWorkbenchImageCanvas(); Rect plot = new Rect(90, 80 + legendRows * 18, width - (dual ? 185 : 125), height - 145 - legendRows * 18);
            float interestBand = interests?.BandWidth(width, height, interestKey) ?? 0; plot.width -= interestBand;
            float X(double v) => plot.x + (float)xMap.Fraction(Math.Max(view.StartX, Math.Min(view.EndX, v))) * plot.width;
            float Y(double v) { double f = yMap.Fraction(Math.Max(min, Math.Min(max, v))); return plot.y + (float)(settings.InvertY ? 1 - f : f) * plot.height; }
            Color grid = new Color(.3f, .3f, .3f), color = new Color(0, .7f, 1);
            canvas.Line(plot.x, plot.y, plot.xMax, plot.y, Color.gray); canvas.Line(plot.x, plot.y, plot.x, plot.yMax, Color.gray);
            foreach (double tick in FPWorkbenchAxis.Ticks(view.StartX, view.EndX, settings.XScale, settings.XTickSpacing)) {
                float x = X(tick); if (settings.Grid) canvas.Line(x, plot.y, x, plot.yMax, grid);
                canvas.Text(tick.ToString("G6", CultureInfo.InvariantCulture), x, plot.y - 17);
            }
            if (hasLeft) foreach (double tick in FPWorkbenchAxis.Ticks(min, max, settings.YScale, settings.YTickSpacing)) {
                float y = Y(tick); if (settings.Grid) canvas.Line(plot.x, y, plot.xMax, y, grid);
                canvas.Text(tick.ToString("G5", CultureInfo.InvariantCulture), plot.x - 8, y, TextAnchor.MiddleRight);
            }
            if (dual && rightRange.HasValues) {
                canvas.Line(plot.xMax, plot.y, plot.xMax, plot.yMax, Color.gray);
                foreach (double tick in FPWorkbenchAxis.Ticks(rightRange.Minimum, rightRange.Maximum, axes.RightSettings.YScale, axes.RightSettings.YTickSpacing)) {
                    double f = rightMap.Fraction(tick); float y = plot.y + (float)(axes.RightSettings.InvertY ? 1 - f : f) * plot.height;
                    if (axes.RightSettings.Grid) canvas.Line(plot.x, y, plot.xMax, y, grid);
                    canvas.Text(tick.ToString("G5", CultureInfo.InvariantCulture), plot.xMax + 8, y, TextAnchor.MiddleLeft);
                }
                canvas.Text(axes.RightSettings.YLabel, width - interestBand - 16, plot.center.y, angle: 90);
            }
            canvas.Text(string.IsNullOrEmpty(settings.Title) ? label : settings.Title, width / 2f, height - 22);
            canvas.Text(settings.YLabel, 16, plot.center.y, angle: 90);
            canvas.Text(settings.XLabel, plot.center.x, legendRows * 18 + 38);
            aggregated = false; logOmitted = 0; bool continuous = false; FPWorkbenchSeriesBin previous = default;
            for (int seriesIndex = 0; seriesIndex < (overlay?.Series.Count ?? 1); seriesIndex++) {
            var current = overlay == null ? view : overlay.Series[seriesIndex];
            bool right = overlay != null && axes != null && axes.Right[seriesIndex];
            double seriesMin = right ? rightRange.Minimum : min, seriesMax = right ? rightRange.Maximum : max;
            var map = right ? rightMap : yMap; var scale = right ? axes.RightSettings.YScale : settings.YScale;
            bool invert = right ? axes.RightSettings.InvertY : settings.InvertY;
            float SeriesY(double v) { double f = map.Fraction(Math.Max(seriesMin, Math.Min(seriesMax, v))); return plot.y + (float)(invert ? 1 - f : f) * plot.height; }
            color = colors != null && seriesIndex < colors.Length ? colors[seriesIndex] : new Color(0, .7f, 1);
            continuous = false; previous = default;
            foreach (var bin in current.Bins) {
                if (bin.Count == 0 || bin.Maximum < seriesMin || bin.Minimum > seriesMax) { continuous = false; continue; }
                if (settings.XScale == FPWorkbenchAxisScale.Log10 && bin.FirstX <= 0 || scale == FPWorkbenchAxisScale.Log10 && bin.Minimum <= 0) { logOmitted++; continuous = false; continue; }
                float x = (X(bin.FirstX) + X(bin.LastX)) * .5f;
                bool exact = bin.Count == 1 && bin.InvalidCount == 0;
                if (!exact) {
                    aggregated = true;
                    if (bin.RepresentativeCount > 0) {
                        FPWorkbenchSeriesPoint? prior = null;
                        for (int j = bin.RepresentativeFirst; j < bin.RepresentativeFirst + bin.RepresentativeCount; j++) {
                            var point = current.Representatives[j]; if (point.Y < seriesMin || point.Y > seriesMax) { prior = null; continue; }
                            float marker = settings.MarkerSize;
                            canvas.Quad(X(point.X) - marker / 2, SeriesY(point.Y) - marker / 2, marker, marker, color);
                            if (settings.Style != FPWorkbenchPlotStyle.Points && bin.InvalidCount == 0 && bin.GapCount == 0 && prior.HasValue) {
                                if (settings.Style == FPWorkbenchPlotStyle.Step) {
                                    canvas.Line(X(prior.Value.X), SeriesY(prior.Value.Y), X(point.X), SeriesY(prior.Value.Y), color, settings.LineWidth);
                                    canvas.Line(X(point.X), SeriesY(prior.Value.Y), X(point.X), SeriesY(point.Y), color, settings.LineWidth);
                                } else canvas.Line(X(prior.Value.X), SeriesY(prior.Value.Y), X(point.X), SeriesY(point.Y), color, settings.LineWidth);
                            }
                            prior = point;
                        }
                    } else if (settings.Style == FPWorkbenchPlotStyle.Points) {
                        float marker = settings.MarkerSize;
                        canvas.Quad(x - marker / 2, SeriesY(bin.Minimum) - marker / 2, marker, marker, color);
                        canvas.Quad(x - marker / 2, SeriesY(bin.Maximum) - marker / 2, marker, marker, color);
                    } else if (bin.InvalidCount == 0 && bin.GapCount == 0 && bin.FirstY >= seriesMin && bin.FirstY <= seriesMax && bin.LastY >= seriesMin && bin.LastY <= seriesMax) {
                        if (settings.Style == FPWorkbenchPlotStyle.Step) {
                            canvas.Line(X(bin.FirstX), SeriesY(bin.FirstY), X(bin.LastX), SeriesY(bin.FirstY), color, settings.LineWidth);
                            canvas.Line(X(bin.LastX), SeriesY(bin.FirstY), X(bin.LastX), SeriesY(bin.LastY), color, settings.LineWidth);
                        } else canvas.Line(X(bin.FirstX), SeriesY(bin.FirstY), X(bin.LastX), SeriesY(bin.LastY), color, settings.LineWidth);
                    }
                    if (settings.Style != FPWorkbenchPlotStyle.Points) canvas.Line(x, SeriesY(bin.Minimum), x, SeriesY(bin.Maximum), new Color(color.r, color.g, color.b, .3f), settings.LineWidth);
                }
                else { float size = settings.MarkerSize; canvas.Quad(X(bin.FirstX) - size / 2, SeriesY(bin.FirstY) - size / 2, size, size, color); }
                if (settings.Style != FPWorkbenchPlotStyle.Points && continuous && bin.InvalidCount == 0 && bin.GapCount == 0 && previous.InvalidCount == 0 && previous.EndIndex == bin.FirstIndex && bin.FirstY >= seriesMin && bin.FirstY <= seriesMax && previous.LastY >= seriesMin && previous.LastY <= seriesMax) {
                    if (settings.Style == FPWorkbenchPlotStyle.Step) { canvas.Line(X(previous.LastX), SeriesY(previous.LastY), X(bin.FirstX), SeriesY(previous.LastY), color, settings.LineWidth); canvas.Line(X(bin.FirstX), SeriesY(previous.LastY), X(bin.FirstX), SeriesY(bin.FirstY), color, settings.LineWidth); }
                    else canvas.Line(X(previous.LastX), SeriesY(previous.LastY), X(bin.FirstX), SeriesY(bin.FirstY), color, settings.LineWidth);
                }
                previous = bin; continuous = bin.GapCount == 0;
            }
            }
            canvas.Text(logOmitted > 0 ? "Log10: " + logOmitted + " mixed/nonpositive bins omitted" : aggregated ? (settings.Style == FPWorkbenchPlotStyle.Points ? "Represented source samples; zoom for all samples" : "Faint ranges: binned min/max; markers: source samples; zoom for all samples") : "Individual cached samples", plot.center.x, height - 43);
            canvas.Text(view.OriginUtcTicks == 0 ? "X: bound numeric/row units" : "Elapsed s from " + new DateTime(view.OriginUtcTicks, DateTimeKind.Utc).ToString("yyyy-MM-dd HH:mm:ss.ffffff 'UTC'", CultureInfo.InvariantCulture), plot.center.x, legendRows * 18 + 17);
            if (legends != null) for (int i = 0; i < legends.Length; i++) {
                float x = 45 + (i % 2) * (width / 2f), y = 14 + (i / 2) * 18;
                canvas.Quad(x, y - 3, 10, 6, colors[i]); canvas.Text(legends[i], x + 15, y, TextAnchor.MiddleLeft);
            }
            if (interests != null) interests.Export(canvas, plot, width, height, interestKey, p => {
                if (overlay == null || p.Series >= overlay.Series.Count) return null;
                bool right = axes != null && axes.Right[p.Series]; var mapping = right ? axes.RightSettings : settings;
                var limits = axes.Resolve(overlay, settings, right);
                return FPWorkbenchFigurePoints.ProjectXY(p, plot, view.StartX, view.EndX, limits.Minimum, limits.Maximum, settings.XScale, mapping.YScale, mapping.InvertY, true);
            }, interestColor ?? Color.yellow, sheetName);
            return canvas.Render(width, height);
        }
        private void DrawImageExport(FPWorkbenchFigureSession session)
        {
            imageJob?.Draw();
            showImageExport = EditorGUILayout.Foldout(showImageExport, "Image export", true); if (!showImageExport) return;
            using (new EditorGUILayout.HorizontalScope()) {
                imageWidth = EditorGUILayout.IntField("PNG width", imageWidth); imageHeight = EditorGUILayout.IntField("Height", imageHeight);
                using (new EditorGUI.DisabledScope(session.IsBusy || session.View == null || FPWorkbenchImageExport.IsActive)) if (GUILayout.Button("Export PNG…")) {
                    string path = EditorUtility.SaveFilePanel("Export scalar image and provenance", "", "ScalarFigure", "fpfigure");
                    if (!string.IsNullOrEmpty(path)) { int w = imageWidth, h = imageHeight; EditorApplication.delayCall += () => {
                        if (this == null) return;
                        ImageJob.Start(() => ExportPng(path, w, h));
                    }; }
                }
            }
        }
    }
}
