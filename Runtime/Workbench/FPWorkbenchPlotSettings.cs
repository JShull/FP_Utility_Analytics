// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    using UnityEngine;

    public enum FPWorkbenchPlotStyle { Line, Step, Points }
    public enum FPWorkbenchLabelAlignment { Left, Center, Right }
    public enum FPWorkbenchYLabelDirection { BottomToTop, TopToBottom, StackedTopToBottom }

    [Serializable]
    public sealed class FPWorkbenchPlotSettings
    {
        public double StartX, EndX = 1, MinimumY, MaximumY = 1, XTickSpacing, YTickSpacing;
        public FPWorkbenchAxisScale XScale, YScale;
        public FPWorkbenchPlotStyle Style;
        public float LineWidth = 1, MarkerSize = 3;
        public bool InvertY, Grid = true, LockY, HasRange;
        public string Title = "", XLabel = "", YLabel = "";
        public int TitleFontSize = 12, XLabelFontSize = 11, YLabelFontSize = 11;
        public FPWorkbenchLabelAlignment TitleAlignment = FPWorkbenchLabelAlignment.Left, XLabelAlignment = FPWorkbenchLabelAlignment.Center, YLabelAlignment = FPWorkbenchLabelAlignment.Center;
        public FPWorkbenchYLabelDirection YLabelDirection;
        public float StackedYCharacterSpacing;
        public bool WhiteBackground;
        public bool CustomTitleColor, CustomXLabelColor, CustomYLabelColor;
        public Color TitleColor = Color.white, XLabelColor = Color.white, YLabelColor = Color.white;
        internal bool SameAs(FPWorkbenchPlotSettings other) => XScale == other.XScale && YScale == other.YScale && Style == other.Style && LineWidth == other.LineWidth && MarkerSize == other.MarkerSize && StartX == other.StartX && EndX == other.EndX && MinimumY == other.MinimumY && MaximumY == other.MaximumY && XTickSpacing == other.XTickSpacing && YTickSpacing == other.YTickSpacing && InvertY == other.InvertY && Grid == other.Grid && LockY == other.LockY && HasRange == other.HasRange && Title == other.Title && XLabel == other.XLabel && YLabel == other.YLabel && TitleFontSize == other.TitleFontSize && XLabelFontSize == other.XLabelFontSize && YLabelFontSize == other.YLabelFontSize && TitleAlignment == other.TitleAlignment && XLabelAlignment == other.XLabelAlignment && YLabelAlignment == other.YLabelAlignment && YLabelDirection == other.YLabelDirection && StackedYCharacterSpacing == other.StackedYCharacterSpacing && WhiteBackground == other.WhiteBackground && CustomTitleColor == other.CustomTitleColor && CustomXLabelColor == other.CustomXLabelColor && CustomYLabelColor == other.CustomYLabelColor && TitleColor == other.TitleColor && XLabelColor == other.XLabelColor && YLabelColor == other.YLabelColor;
        internal void CopyFrom(FPWorkbenchPlotSettings other)
        {
            XScale = other.XScale; YScale = other.YScale; Style = other.Style; LineWidth = other.LineWidth; MarkerSize = other.MarkerSize;
            StartX = other.StartX; EndX = other.EndX; MinimumY = other.MinimumY; MaximumY = other.MaximumY;
            XTickSpacing = other.XTickSpacing; YTickSpacing = other.YTickSpacing;
            InvertY = other.InvertY; Grid = other.Grid; LockY = other.LockY; HasRange = other.HasRange;
            Title = other.Title; XLabel = other.XLabel; YLabel = other.YLabel;
            TitleFontSize = other.TitleFontSize; XLabelFontSize = other.XLabelFontSize; YLabelFontSize = other.YLabelFontSize;
            TitleAlignment = other.TitleAlignment; XLabelAlignment = other.XLabelAlignment; YLabelAlignment = other.YLabelAlignment;
            YLabelDirection = other.YLabelDirection; StackedYCharacterSpacing = other.StackedYCharacterSpacing; WhiteBackground = other.WhiteBackground;
            CustomTitleColor = other.CustomTitleColor; CustomXLabelColor = other.CustomXLabelColor; CustomYLabelColor = other.CustomYLabelColor; TitleColor = other.TitleColor; XLabelColor = other.XLabelColor; YLabelColor = other.YLabelColor;
        }
        public void Fit(double first, double last)
        {
            if (!FPWorkbenchSeriesBinding.Finite(first) || !FPWorkbenchSeriesBinding.Finite(last) || last < first) throw new ArgumentException("Invalid source extent.");
            if (last == first && XScale == FPWorkbenchAxisScale.Log10 && first > 0) { FPWorkbenchAxis.ExpandConstant(first, XScale, out first, out last); }
            StartX = first; EndX = last > first ? last : first + Math.Max(1, Math.Abs(first) * 1e-12); HasRange = true;
        }
        public bool Navigate(double scale, double anchorFraction, double panFraction)
        {
            if (!FPWorkbenchAxis.TryNavigate(StartX, EndX, XScale, scale, anchorFraction, panFraction, out double first, out double last)) return false;
            StartX = first; EndX = last; HasRange = true; return true;
        }
    }
}
