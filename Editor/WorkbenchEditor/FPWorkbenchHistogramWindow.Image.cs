// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.Threading.Tasks;
    using UnityEditor;
    using UnityEngine;

    public sealed partial class FPWorkbenchHistogramWindow
    {
        [SerializeField] private int imageWidth = 1600, imageHeight = 1200;
        [SerializeField] private bool showImageExport;
        private FPWorkbenchEditorImageJob imageJob;
        private FPWorkbenchEditorImageJob ImageJob => imageJob ??= new FPWorkbenchEditorImageJob(Repaint);
        [Serializable] private sealed class HistogramImageProvenance : FPWorkbenchImageProvenance
        {
            public string Format = "FuzzPhyte.DataWorkbench.HistogramImage", SourcePath;
            public int Version = 1;
            public long Included, Invalid, Below, Above;
            public double[] Edges;
            public long[] Counts;
            public FPWorkbenchFigureDocument Figure;
        }
        private Task<FPWorkbenchImageExportResult> ExportPng(string directory, int width, int height)
        {
            if (width < 320 || height < 240 || width > 4096 || height > 4096 || (long)width * height > 4194304)
                throw new ArgumentOutOfRangeException("Use 320–4096 width, 240–4096 height and at most 4,194,304 pixels.");
            if (Busy || result == null || dataset == null) throw new InvalidOperationException("Wait for a completed histogram before exporting.");
            string output = Path.GetFullPath(directory), parent = Path.GetDirectoryName(Path.GetFullPath(directory));
            if (!output.EndsWith(".fpfigure", StringComparison.OrdinalIgnoreCase) || Directory.Exists(output) || File.Exists(output))
                throw new IOException("Choose a new .fpfigure output directory.");
            if (!Directory.Exists(parent)) throw new DirectoryNotFoundException(parent);
            using var reservation = FPWorkbenchEditorImageReservation.Acquire(width, height, checked(4096L + result.Counts.Count * 4L));
            var figure = DescribeFigure(); figure.Validate();
            var metadata = new HistogramImageProvenance { SourcePath = dataset.SourcePath, Width = width, Height = height, Figure = figure,
                Included = result.IncludedCount, Invalid = result.InvalidCount, Below = result.UnderflowCount, Above = result.OverflowCount,
                Edges = new double[result.Edges.Count], Counts = new long[result.Counts.Count] };
            for (int i = 0; i < metadata.Edges.Length; i++) metadata.Edges[i] = result.Edges[i];
            for (int i = 0; i < metadata.Counts.Length; i++) metadata.Counts[i] = result.Counts[i];
            Texture2D image = null;
            try {
                image = RenderHistogramImage(width, height);
                return reservation.Publish(output, image.GetPixels32(), width, height, metadata, imageJob?.Token ?? default);
            }
            finally { if (image != null) DestroyImmediate(image); }
        }
        private Texture2D RenderHistogramImage(int width, int height)
        {
            using var canvas = new FPWorkbenchImageCanvas(); canvas.BackgroundColor = presentation.WhiteBackground ? Color.white : new Color(.1f, .1f, .1f); canvas.TextColor = presentation.WhiteBackground ? Color.black : Color.white;
            Rect plot = new Rect(85, 65, width - 115 - pointsOfInterest.BandWidth(width, height, InterestAxisKey), height - 120);
            double peak = 0;
            for (int i = 0; i < result.Counts.Count; i++) peak = Math.Max(peak, density ? result.Density(i) : result.Counts[i]);
            if (double.IsNaN(peak) || double.IsInfinity(peak)) throw new OverflowException("Histogram image has nonfinite density.");
            if (peak <= 0) peak = 1;
            canvas.Line(plot.x, plot.y, plot.x, plot.yMax, Color.gray); canvas.Line(plot.x, plot.y, plot.xMax, plot.y, Color.gray);
            for (int i = 0; i <= 4; i++) {
                float y = plot.y + plot.height * i / 4; if (grid) canvas.Line(plot.x, y, plot.xMax, y, Color.gray);
                canvas.Text((peak * i / 4).ToString("G4", CultureInfo.InvariantCulture), plot.x - 8, y, TextAnchor.MiddleRight);
                double value = result.Edges[0] + (result.Edges[result.Edges.Count - 1] - result.Edges[0]) * i / 4;
                canvas.Text(value.ToString("G5", CultureInfo.InvariantCulture), plot.x + plot.width * i / 4, plot.y - 18);
            }
            float LabelPosition(FPWorkbenchLabelAlignment a, float min, float max) => a == FPWorkbenchLabelAlignment.Left ? min : a == FPWorkbenchLabelAlignment.Right ? max : (min + max) / 2;
            TextAnchor LabelAnchor(FPWorkbenchLabelAlignment a) => a == FPWorkbenchLabelAlignment.Left ? TextAnchor.MiddleLeft : a == FPWorkbenchLabelAlignment.Right ? TextAnchor.MiddleRight : TextAnchor.MiddleCenter;
            canvas.TextColor = FPWorkbenchPlotView.TextColor(presentation, presentation.CustomTitleColor, presentation.TitleColor);
            canvas.Text(string.IsNullOrEmpty(presentation.Title) ? columnName + " — Histogram" : presentation.Title, LabelPosition(presentation.TitleAlignment, 10, width - 10), height - Mathf.Max(22, presentation.TitleFontSize / 2f + 4), LabelAnchor(presentation.TitleAlignment), fontSize: presentation.TitleFontSize);
            canvas.TextColor = FPWorkbenchPlotView.TextColor(presentation, presentation.CustomXLabelColor, presentation.XLabelColor);
            canvas.Text(string.IsNullOrEmpty(presentation.XLabel) ? columnName : presentation.XLabel, LabelPosition(presentation.XLabelAlignment, plot.x, plot.xMax), 20, LabelAnchor(presentation.XLabelAlignment), fontSize: presentation.XLabelFontSize);
            canvas.TextColor = FPWorkbenchPlotView.TextColor(presentation, presentation.CustomYLabelColor, presentation.YLabelColor);
            FPWorkbenchPlotView.ExportYLabel(canvas, string.IsNullOrEmpty(presentation.YLabel) ? (density ? "Density" : "Count") : presentation.YLabel, presentation, Mathf.Max(16, presentation.YLabelFontSize / 2f + 3), plot.y, plot.yMax);
            canvas.TextColor = presentation.WhiteBackground ? Color.black : Color.white;
            float binWidth = plot.width / result.Counts.Count;
            for (int i = 0; i < result.Counts.Count; i++) {
                float h = (float)((density ? result.Density(i) : result.Counts[i]) / peak * plot.height);
                canvas.Quad(plot.x + binWidth * i, plot.y, binWidth, h, new Color(.15f, .65f, .9f));
            }
            pointsOfInterest.Export(canvas, plot, width, height, InterestAxisKey, p => ProjectInterest(p, plot, true), Color.yellow, sourceSheetName);
            return canvas.Render(width, height);
        }
        private void DrawImageExport()
        {
            imageJob?.Draw();
            showImageExport = EditorGUILayout.Foldout(showImageExport, "Image export", true); if (!showImageExport) return;
            using (new EditorGUILayout.VerticalScope()) {
                imageWidth = EditorGUILayout.IntField("PNG width", imageWidth); imageHeight = EditorGUILayout.IntField("Height", imageHeight);
                using (new EditorGUI.DisabledScope(Busy || result == null || FPWorkbenchImageExport.IsActive)) if (GUILayout.Button("Export PNG…")) {
                    string path = EditorUtility.SaveFilePanel("Export histogram image and provenance", "", "Histogram", "fpfigure");
                    if (!string.IsNullOrEmpty(path)) { int w = imageWidth, h = imageHeight; EditorApplication.delayCall += () => {
                        if (this == null) return;
                        ImageJob.Start(() => ExportPng(path, w, h));
                    }; }
                }
            }
        }
    }
}
