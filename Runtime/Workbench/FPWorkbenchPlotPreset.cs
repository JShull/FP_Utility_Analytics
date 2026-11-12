// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    using System.IO;
    using System.Text;
    using UnityEngine;

    /// <summary>Portable presentation only; excludes source identities, corrections and viewport limits.</summary>
    [Serializable] public sealed class FPWorkbenchPlotPreset
    {
        public string Format = "FuzzPhyte.DataWorkbench.PlotPreset";
        public int Version = 1;
        public string Title = "", XLabel = "", YLabel = "";
        public FPWorkbenchAxisScale XScale, YScale;
        public FPWorkbenchPlotStyle Style;
        public float LineWidth = 1, MarkerSize = 3;
        public bool Grid = true, InvertY;
        public double XTickSpacing, YTickSpacing;
        public const int MaximumBytes = 16384;
        public static FPWorkbenchPlotPreset Capture(FPWorkbenchPlotSettings s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            var p = new FPWorkbenchPlotPreset { XScale = s.XScale, YScale = s.YScale, Title = s.Title, XLabel = s.XLabel, YLabel = s.YLabel, Style = s.Style,
                LineWidth = s.LineWidth, MarkerSize = s.MarkerSize, Grid = s.Grid, InvertY = s.InvertY,
                XTickSpacing = s.XTickSpacing, YTickSpacing = s.YTickSpacing }; p.Validate(); return p;
        }
        public void Validate()
        {
            if (Format != "FuzzPhyte.DataWorkbench.PlotPreset" || Version != 1 || Title == null || XLabel == null || YLabel == null ||
                !Enum.IsDefined(typeof(FPWorkbenchAxisScale), XScale) || !Enum.IsDefined(typeof(FPWorkbenchAxisScale), YScale) || Title.Length > 512 || XLabel.Length > 512 || YLabel.Length > 512 || !Enum.IsDefined(typeof(FPWorkbenchPlotStyle), Style) ||
                !FPWorkbenchSeriesBinding.Finite(LineWidth) || LineWidth < 1 || LineWidth > 8 || !FPWorkbenchSeriesBinding.Finite(MarkerSize) || MarkerSize < 1 || MarkerSize > 16 ||
                !FPWorkbenchSeriesBinding.Finite(XTickSpacing) || XTickSpacing < 0 || !FPWorkbenchSeriesBinding.Finite(YTickSpacing) || YTickSpacing < 0)
                throw new InvalidDataException("Invalid or unsupported plot presentation preset.");
        }
        public void Apply(FPWorkbenchPlotSettings s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s)); Validate();
            if (XScale == FPWorkbenchAxisScale.Log10 && s.HasRange) FPWorkbenchAxis.Validate(s.StartX, s.EndX, XScale);
            if (YScale == FPWorkbenchAxisScale.Log10 && s.LockY) FPWorkbenchAxis.Validate(s.MinimumY, s.MaximumY, YScale);
            s.XScale = XScale; s.YScale = YScale; s.Title = Title; s.XLabel = XLabel; s.YLabel = YLabel; s.Style = Style; s.LineWidth = LineWidth;
            s.MarkerSize = MarkerSize; s.Grid = Grid; s.InvertY = InvertY; s.XTickSpacing = XTickSpacing; s.YTickSpacing = YTickSpacing;
        }
        public string ToJson() { Validate(); return JsonUtility.ToJson(this, true); }
        public static FPWorkbenchPlotPreset Parse(string json)
        {
            if (string.IsNullOrEmpty(json) || Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw new InvalidDataException("Plot preset is empty or exceeds 16 KiB.");
            try {
                // A struct marker has no initializer defaults: unrelated JSON must not acquire our format/version.
                var marker = JsonUtility.FromJson<Marker>(json);
                if (marker.Format != "FuzzPhyte.DataWorkbench.PlotPreset" || marker.Version != 1) throw new InvalidDataException("Not a supported plot preset.");
                var p = JsonUtility.FromJson<FPWorkbenchPlotPreset>(json); p.Validate(); return p;
            } catch (ArgumentException ex) { throw new InvalidDataException("Invalid plot preset JSON.", ex); }
        }
        [Serializable] private struct Marker { public string Format; public int Version; }
    }
}
