// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;

    public enum FPWorkbenchPlotStyle { Line, Step, Points }

    [Serializable]
    public sealed class FPWorkbenchPlotSettings
    {
        public double StartX, EndX = 1, MinimumY, MaximumY = 1, XTickSpacing, YTickSpacing;
        public FPWorkbenchPlotStyle Style;
        public float LineWidth = 1, MarkerSize = 3;
        public bool InvertY, Grid = true, LockY, HasRange;
        public string Title = "", XLabel = "", YLabel = "";
        internal bool SameAs(FPWorkbenchPlotSettings other) => Style == other.Style && LineWidth == other.LineWidth && MarkerSize == other.MarkerSize && StartX == other.StartX && EndX == other.EndX && MinimumY == other.MinimumY && MaximumY == other.MaximumY && XTickSpacing == other.XTickSpacing && YTickSpacing == other.YTickSpacing && InvertY == other.InvertY && Grid == other.Grid && LockY == other.LockY && HasRange == other.HasRange && Title == other.Title && XLabel == other.XLabel && YLabel == other.YLabel;
        internal void CopyFrom(FPWorkbenchPlotSettings other)
        {
            Style = other.Style; LineWidth = other.LineWidth; MarkerSize = other.MarkerSize;
            StartX = other.StartX; EndX = other.EndX; MinimumY = other.MinimumY; MaximumY = other.MaximumY;
            XTickSpacing = other.XTickSpacing; YTickSpacing = other.YTickSpacing;
            InvertY = other.InvertY; Grid = other.Grid; LockY = other.LockY; HasRange = other.HasRange;
            Title = other.Title; XLabel = other.XLabel; YLabel = other.YLabel;
        }
        public void Fit(double first, double last)
        {
            if (!FPWorkbenchSeriesBinding.Finite(first) || !FPWorkbenchSeriesBinding.Finite(last) || last < first) throw new ArgumentException("Invalid source extent.");
            StartX = first; EndX = last > first ? last : first + Math.Max(1, Math.Abs(first) * 1e-12); HasRange = true;
        }
        public bool Navigate(double scale, double anchorFraction, double panFraction)
        {
            if (!FPWorkbenchSeriesBinding.Finite(scale) || scale <= 0 || !FPWorkbenchSeriesBinding.Finite(anchorFraction) || !FPWorkbenchSeriesBinding.Finite(panFraction)) return false;
            double span = EndX - StartX, nextSpan = span * scale;
            if (!FPWorkbenchSeriesBinding.Finite(nextSpan) || nextSpan < 1e-9) return false;
            double start = StartX + span * anchorFraction - nextSpan * anchorFraction + span * panFraction;
            double end = start + nextSpan;
            if (!FPWorkbenchSeriesBinding.Finite(start) || !FPWorkbenchSeriesBinding.Finite(end) || end <= start) return false;
            StartX = start; EndX = end; HasRange = true; return true;
        }
    }
}
