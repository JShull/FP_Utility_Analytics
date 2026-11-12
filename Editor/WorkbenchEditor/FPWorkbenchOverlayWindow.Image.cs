// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.IO;
    using System.Threading.Tasks;
    using UnityEditor;
    using UnityEngine;

    public sealed partial class FPWorkbenchOverlayWindow
    {
        [SerializeField] private int imageWidth = 1600, imageHeight = 1200;
        [SerializeField] private bool showImageExport;

        private FPWorkbenchEditorImageJob imageJob;
        private FPWorkbenchEditorImageJob ImageJob => imageJob ??= new FPWorkbenchEditorImageJob(Repaint);
        [Serializable] private sealed class OverlayImageProvenance : FPWorkbenchImageProvenance
        {
            public string Format = "FuzzPhyte.DataWorkbench.OverlayImage", SourcePath;
            public int Version = 1, OmittedLogBins;
            public int[] DisplayedBins;
            public long OriginUtcTicks;
            public bool Aggregated;
            public FPWorkbenchFigureDocument Figure;
        }
        private Task<FPWorkbenchImageExportResult> ExportPng(string directory, int width, int height)
        {
            if (width < 440 || height < 360 || width > 4096 || height > 4096 || (long)width * height > 4194304)
                throw new ArgumentOutOfRangeException("Use 440–4096 width, 360–4096 height and at most 4,194,304 pixels.");
            if (Busy || view == null || dataset == null || view.StartX != settings.StartX || view.EndX != settings.EndX || view.Series.Count != columns.Count)
                throw new InvalidOperationException("Wait for a completed current overlay before exporting.");
            string output = Path.GetFullPath(directory), parent = Path.GetDirectoryName(Path.GetFullPath(directory));
            if (!output.EndsWith(".fpfigure", StringComparison.OrdinalIgnoreCase) || Directory.Exists(output) || File.Exists(output)) throw new IOException("Choose a new .fpfigure output directory.");
            if (!Directory.Exists(parent)) throw new DirectoryNotFoundException(parent);
            long geometryVertices = 4096; foreach (var series in view.Series) geometryVertices = checked(geometryVertices + series.Bins.Length * 12L);
            using var reservation = FPWorkbenchEditorImageReservation.Acquire(width, height, geometryVertices);
            var figure = DescribeFigure(); figure.Validate();
            var metadata = new OverlayImageProvenance { Width = width, Height = height, Figure = figure, SourcePath = dataset.SourcePath,
                DisplayedBins = new int[view.Series.Count], OriginUtcTicks = view.Series[0].OriginUtcTicks };
            var legends = new string[columns.Count];
            for (int i = 0; i < legends.Length; i++) { metadata.DisplayedBins[i] = view.Series[i].Bins.Length; legends[i] = yNames[columns[i]] + (axes.Right[i] ? " (R)" : " (L)"); }
            Texture2D image = null;
            try {
                image = FPWorkbenchFigureWindow.RenderSeriesImage(view.Series[0], settings, "Multi-series figure", width, height,
                    out metadata.Aggregated, out metadata.OmittedLogBins, view, colors, axes, legends);
                return reservation.Publish(output, image.GetPixels32(), width, height, metadata, imageJob?.Token ?? default);
            }
            finally { if (image != null) DestroyImmediate(image); }
        }
        private void DrawImageExport()
        {
            imageJob?.Draw();
            showImageExport = EditorGUILayout.Foldout(showImageExport, "Image export", true); if (!showImageExport) return;
            using (new EditorGUILayout.HorizontalScope()) {
                imageWidth = EditorGUILayout.IntField("PNG width", imageWidth); imageHeight = EditorGUILayout.IntField("Height", imageHeight);
                using (new EditorGUI.DisabledScope(Busy || view == null || FPWorkbenchImageExport.IsActive)) if (GUILayout.Button("Export PNG…")) {
                    string path = EditorUtility.SaveFilePanel("Export overlay image and provenance", "", "Overlay", "fpfigure");
                    if (!string.IsNullOrEmpty(path)) { int w = imageWidth, h = imageHeight; EditorApplication.delayCall += () => {
                        if (this == null) return;
                        ImageJob.Start(() => ExportPng(path, w, h));
                    }; }
                }
            }
        }
    }
}
