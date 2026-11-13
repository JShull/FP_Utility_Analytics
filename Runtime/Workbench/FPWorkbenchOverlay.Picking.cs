// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    public sealed partial class FPWorkbenchOverlay
    {
        /// <summary>Nearest represented source sample across the displayed series, with independent Y axes.</summary>
        public async Task<(int Series, FPWorkbenchSeriesPoint Point)?> PickDisplayedPointAsync(FPWorkbenchOverlayView view,
            FPWorkbenchPlotSettings[] mappings, double mouseX, double mouseY, double width, double height, CancellationToken cancellation = default)
        {
            if (view == null || view.Series == null || view.Series.Count != series.Length || mappings == null || mappings.Length != series.Length) throw new ArgumentException("Overlay pick requires one current mapping per series.");
            var snapshots = new FPWorkbenchPlotSettings[mappings.Length];
            for (int i = 0; i < mappings.Length; i++) { if (mappings[i] == null) throw new ArgumentException("Overlay axis mapping is missing."); snapshots[i] = new FPWorkbenchPlotSettings(); snapshots[i].CopyFrom(mappings[i]); }
            (int Series, FPWorkbenchSeriesPoint Point)? best = null; double distance = 100;
            for (int i = 0; i < series.Length; i++) {
                cancellation.ThrowIfCancellationRequested();
                var p = await series[i].PickDisplayedPointAsync(view.Series[i], snapshots[i], mouseX, mouseY, width, height, cancellation: cancellation).ConfigureAwait(false);
                if (!p.HasValue) continue;
                var settings = snapshots[i]; var x = new FPWorkbenchAxisMapping(view.StartX, view.EndX, settings.XScale);
                var y = new FPWorkbenchAxisMapping(settings.MinimumY, settings.MaximumY, settings.YScale);
                double dx = (x.Fraction(p.Value.X) - mouseX) * width, fy = y.Fraction(p.Value.Y);
                double dy = ((settings.InvertY ? fy : 1 - fy) - mouseY) * height, d = dx * dx + dy * dy;
                if (!best.HasValue || d < distance) { best = (i, p.Value); distance = d; }
            }
            return best;
        }
    }
}
