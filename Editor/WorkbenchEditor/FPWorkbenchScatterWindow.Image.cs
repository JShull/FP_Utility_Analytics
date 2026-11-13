// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.Threading.Tasks;
    using UnityEditor;
    using UnityEngine;

    public sealed partial class FPWorkbenchScatterWindow
    {
        [SerializeField] private int imageWidth = 1600, imageHeight = 1200;
        [SerializeField] private bool showImageExport;

        private FPWorkbenchEditorImageJob imageJob;
        private FPWorkbenchEditorImageJob ImageJob => imageJob ??= new FPWorkbenchEditorImageJob(Repaint);
        [Serializable] private sealed class ScatterImageProvenance : FPWorkbenchImageProvenance
        {
            public string Format = "FuzzPhyte.DataWorkbench.ScatterImage", SourcePath, ViewDatasetId;
            public int Version = 1, RenderedPoints;
            public long MatchingPoints, InvalidSourceRows, OriginUtcTicks;
            public bool Sampled;
            public long[] DisplayedRows;
            public FPWorkbenchFigureDocument Figure;
            public AlignedOrigin Alignment;
            public FPWorkbenchSourceDocument[] AlignmentSources;
        }
        private Task<FPWorkbenchImageExportResult> ExportPng(string directory, int width, int height)
        {
            if (width < 320 || height < 240 || width > 4096 || height > 4096 || (long)width * height > 4194304)
                throw new ArgumentOutOfRangeException("Use 320–4096 width, 240–4096 height and at most 4,194,304 pixels.");
            if (Busy || view == null || cache == null || !view.Bounds.Equals(RequestedBounds()) || completedBudget != maximumPoints || failedQuery)
                throw new InvalidOperationException("Wait for a successful current scatter view before exporting.");
            string output = Path.GetFullPath(directory), parent = Path.GetDirectoryName(Path.GetFullPath(directory));
            if (!output.EndsWith(".fpfigure", StringComparison.OrdinalIgnoreCase) || Directory.Exists(output) || File.Exists(output)) throw new IOException("Choose a new .fpfigure output directory.");
            if (!Directory.Exists(parent)) throw new DirectoryNotFoundException(parent);
            using var reservation = FPWorkbenchEditorImageReservation.Acquire(width, height, checked(4096L + view.Points.Count * 4L));
            var figure = DescribeFigure(); figure.PointBudget = completedBudget; figure.Validate();
            var metadata = new ScatterImageProvenance { Width = width, Height = height, Figure = figure, OriginUtcTicks = cache.OriginUtcTicks, SourcePath = dataset.SourcePath,
                ViewDatasetId = view.DatasetId, MatchingPoints = view.MatchingCount, InvalidSourceRows = cache.InvalidCount, RenderedPoints = view.Points.Count,
                Sampled = view.Decimated, DisplayedRows = new long[view.Points.Count], Alignment = hasAlignedOrigin ? alignedOrigin : null,
                AlignmentSources = hasAlignedRecipe ? new[] { alignedLeftReference, alignedRightReference } : null };
            for (int i = 0; i < metadata.DisplayedRows.Length; i++) metadata.DisplayedRows[i] = view.Points[i].Row;
            Texture2D image = null;
            try {
                image = RenderScatterImage(width, height);
                return reservation.Publish(output, image.GetPixels32(), width, height, metadata, imageJob?.Token ?? default);
            }
            finally { if (image != null) DestroyImmediate(image); }
        }
        private Texture2D RenderScatterImage(int width, int height)
        {
            var bounds = view.Bounds;
            var xMap = new FPWorkbenchAxisMapping(bounds.MinimumX, bounds.MaximumX, settings.XScale);
            var yMap = new FPWorkbenchAxisMapping(bounds.MinimumY, bounds.MaximumY, settings.YScale);
            using var canvas = new FPWorkbenchImageCanvas(); canvas.BackgroundColor = settings.WhiteBackground ? Color.white : new Color(.1f, .1f, .1f); canvas.TextColor = settings.WhiteBackground ? Color.black : Color.white; var plot = new Rect(90, 80, width - 125 - pointsOfInterest.BandWidth(width, height, InterestAxisKey), height - 145);
            float X(double value) => plot.x + (float)xMap.Fraction(value) * plot.width;
            float Y(double value) { double f = yMap.Fraction(value); return plot.y + (float)(settings.InvertY ? 1 - f : f) * plot.height; }
            canvas.Line(plot.x, plot.y, plot.xMax, plot.y, Color.gray); canvas.Line(plot.x, plot.y, plot.x, plot.yMax, Color.gray);
            foreach (double tick in FPWorkbenchAxis.Ticks(bounds.MinimumX, bounds.MaximumX, settings.XScale, settings.XTickSpacing)) {
                float x = X(tick); if (settings.Grid) canvas.Line(x, plot.y, x, plot.yMax, new Color(.3f, .3f, .3f));
                canvas.Text(tick.ToString("G5", CultureInfo.InvariantCulture), x, plot.y - 17);
            }
            foreach (double tick in FPWorkbenchAxis.Ticks(bounds.MinimumY, bounds.MaximumY, settings.YScale, settings.YTickSpacing)) {
                float y = Y(tick); if (settings.Grid) canvas.Line(plot.x, y, plot.xMax, y, new Color(.3f, .3f, .3f));
                canvas.Text(tick.ToString("G5", CultureInfo.InvariantCulture), plot.x - 8, y, TextAnchor.MiddleRight);
            }
            float LabelPosition(FPWorkbenchLabelAlignment a, float min, float max) => a == FPWorkbenchLabelAlignment.Left ? min : a == FPWorkbenchLabelAlignment.Right ? max : (min + max) / 2;
            TextAnchor LabelAnchor(FPWorkbenchLabelAlignment a) => a == FPWorkbenchLabelAlignment.Left ? TextAnchor.MiddleLeft : a == FPWorkbenchLabelAlignment.Right ? TextAnchor.MiddleRight : TextAnchor.MiddleCenter;
            canvas.TextColor = FPWorkbenchPlotView.TextColor(settings, settings.CustomTitleColor, settings.TitleColor);
            canvas.Text(string.IsNullOrEmpty(settings.Title) ? names[appliedY] + " vs " + XCoordinateName : settings.Title, LabelPosition(settings.TitleAlignment, 10, width - 10), height - Mathf.Max(22, settings.TitleFontSize / 2f + 4), LabelAnchor(settings.TitleAlignment), fontSize: settings.TitleFontSize);
            canvas.TextColor = FPWorkbenchPlotView.TextColor(settings, settings.CustomXLabelColor, settings.XLabelColor);
            canvas.Text(string.IsNullOrEmpty(settings.XLabel) ? XCoordinateName : settings.XLabel, LabelPosition(settings.XLabelAlignment, plot.x, plot.xMax), 38, LabelAnchor(settings.XLabelAlignment), fontSize: settings.XLabelFontSize);
            canvas.TextColor = FPWorkbenchPlotView.TextColor(settings, settings.CustomYLabelColor, settings.YLabelColor);
            FPWorkbenchPlotView.ExportYLabel(canvas, string.IsNullOrEmpty(settings.YLabel) ? names[appliedY] : settings.YLabel, settings, Mathf.Max(16, settings.YLabelFontSize / 2f + 3), plot.y, plot.yMax);
            canvas.TextColor = settings.WhiteBackground ? Color.black : Color.white;
            foreach (var point in view.Points) {
                float size = settings.MarkerSize, x = X(point.X), y = Y(point.Y);
                // Match the viewer's plot clipping, including markers centered on an edge.
                float left = Math.Max(plot.x, x - size / 2), bottom = Math.Max(plot.y, y - size / 2);
                canvas.Quad(left, bottom, Math.Min(plot.xMax, x + size / 2) - left, Math.Min(plot.yMax, y + size / 2) - bottom, pointColor);
            }
            pointsOfInterest.Export(canvas, plot, width, height, InterestAxisKey, p => ProjectInterest(p, plot, true), graphSelection.Color, sourceSheetName);
            return canvas.Render(width, height);
        }
        private void DrawImageExport()
        {
            imageJob?.Draw();
            showImageExport = EditorGUILayout.Foldout(showImageExport, "Image export", true); if (!showImageExport) return;
            using (new EditorGUILayout.VerticalScope()) {
                imageWidth = EditorGUILayout.IntField("PNG width", imageWidth); imageHeight = EditorGUILayout.IntField("Height", imageHeight);
                using (new EditorGUI.DisabledScope(Busy || view == null || FPWorkbenchImageExport.IsActive)) if (GUILayout.Button("Export PNG…")) {
                    string path = EditorUtility.SaveFilePanel("Export scatter image and provenance", "", "Scatter", "fpfigure");
                    if (!string.IsNullOrEmpty(path)) { int w = imageWidth, h = imageHeight; EditorApplication.delayCall += () => {
                        if (this == null) return;
                        ImageJob.Start(() => ExportPng(path, w, h));
                    }; }
                }
            }
        }
    }
}
