// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    using System.IO;
    using System.Text;
    using UnityEngine;

    /// <summary>Reusable billboard appearance; excludes camera, source, clipping and point budgets.</summary>
    [Serializable] public sealed class FPWorkbenchPoint3DPreset
    {
        public string Format = "FuzzPhyte.DataWorkbench.Point3DPreset";
        public int Version = 1;
        public Color Color = new Color(0, .7f, 1);
        public float PointSize = 4;
        public const int MaximumBytes = 2048;
        public void Validate()
        {
            bool Unit(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0 && value <= 1;
            if (Format != "FuzzPhyte.DataWorkbench.Point3DPreset" || Version != 1 || !Unit(Color.r) || !Unit(Color.g) || !Unit(Color.b) || !Unit(Color.a) ||
                float.IsNaN(PointSize) || float.IsInfinity(PointSize) || PointSize < 1 || PointSize > 32)
                throw new InvalidDataException("Invalid 3D point appearance preset: use finite RGBA [0,1] and size [1,32].");
        }
        public string ToJson() { Validate(); return JsonUtility.ToJson(this, true); }
        public static FPWorkbenchPoint3DPreset Parse(string json)
        {
            if (string.IsNullOrEmpty(json) || Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw new InvalidDataException("3D point preset is empty or exceeds 2 KiB.");
            try {
                var marker = JsonUtility.FromJson<PointMarker>(json);
                if (marker.Format != "FuzzPhyte.DataWorkbench.Point3DPreset" || marker.Version != 1) throw new InvalidDataException("Not a supported 3D point appearance preset.");
                var preset = JsonUtility.FromJson<FPWorkbenchPoint3DPreset>(json); preset.Validate(); return preset;
            } catch (ArgumentException ex) { throw new InvalidDataException("Invalid 3D point preset JSON.", ex); }
        }
        [Serializable] private struct PointMarker { public string Format; public int Version; }
    }

    /// <summary>Histogram appearance only; never carries bins, bounds, rows or source policy.</summary>
    [Serializable] public sealed class FPWorkbenchHistogramPreset
    {
        public string Format = "FuzzPhyte.DataWorkbench.HistogramPreset";
        public int Version = 1;
        public bool Density, Grid = true;
        public const int MaximumBytes = 1024;
        public string ToJson()
        {
            Validate(); return JsonUtility.ToJson(this, true);
        }
        public void Validate()
        {
            if (Format != "FuzzPhyte.DataWorkbench.HistogramPreset" || Version != 1)
                throw new InvalidDataException("Not a supported histogram presentation preset.");
        }
        public static FPWorkbenchHistogramPreset Parse(string json)
        {
            if (string.IsNullOrEmpty(json) || Encoding.UTF8.GetByteCount(json) > MaximumBytes)
                throw new InvalidDataException("Histogram preset is empty or exceeds 1 KiB.");
            try
            {
                var marker = JsonUtility.FromJson<HistogramMarker>(json);
                if (marker.Format != "FuzzPhyte.DataWorkbench.HistogramPreset" || marker.Version != 1)
                    throw new InvalidDataException("Not a supported histogram presentation preset.");
                var preset = JsonUtility.FromJson<FPWorkbenchHistogramPreset>(json); preset.Validate(); return preset;
            }
            catch (ArgumentException ex) { throw new InvalidDataException("Invalid histogram preset JSON.", ex); }
        }
        [Serializable] private struct HistogramMarker { public string Format; public int Version; }
    }

    /// <summary>Portable presentation only; excludes source identities, corrections and viewport limits.</summary>
    [Serializable] public sealed class FPWorkbenchPlotPreset
    {
        public string Format = "FuzzPhyte.DataWorkbench.PlotPreset";
        public int Version = 1;
        public string Title = "", XLabel = "", YLabel = "";
        public int TitleFontSize = 12, XLabelFontSize = 11, YLabelFontSize = 11;
        public FPWorkbenchLabelAlignment TitleAlignment = FPWorkbenchLabelAlignment.Left, XLabelAlignment = FPWorkbenchLabelAlignment.Center, YLabelAlignment = FPWorkbenchLabelAlignment.Center;
        public FPWorkbenchYLabelDirection YLabelDirection;
        public float StackedYCharacterSpacing;
        public bool WhiteBackground;
        public bool CustomTitleColor, CustomXLabelColor, CustomYLabelColor;
        public UnityEngine.Color TitleColor = UnityEngine.Color.white, XLabelColor = UnityEngine.Color.white, YLabelColor = UnityEngine.Color.white;
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
                XTickSpacing = s.XTickSpacing, YTickSpacing = s.YTickSpacing, TitleFontSize = s.TitleFontSize, XLabelFontSize = s.XLabelFontSize, YLabelFontSize = s.YLabelFontSize, TitleAlignment = s.TitleAlignment, XLabelAlignment = s.XLabelAlignment, YLabelAlignment = s.YLabelAlignment, YLabelDirection = s.YLabelDirection, StackedYCharacterSpacing = s.StackedYCharacterSpacing, WhiteBackground = s.WhiteBackground, CustomTitleColor = s.CustomTitleColor, CustomXLabelColor = s.CustomXLabelColor, CustomYLabelColor = s.CustomYLabelColor, TitleColor = s.TitleColor, XLabelColor = s.XLabelColor, YLabelColor = s.YLabelColor }; p.Validate(); return p;
        }
        public void Validate()
        {
            if (Format != "FuzzPhyte.DataWorkbench.PlotPreset" || Version != 1 || Title == null || XLabel == null || YLabel == null ||
                !Enum.IsDefined(typeof(FPWorkbenchAxisScale), XScale) || !Enum.IsDefined(typeof(FPWorkbenchAxisScale), YScale) || Title.Length > 512 || XLabel.Length > 512 || YLabel.Length > 512 || !Enum.IsDefined(typeof(FPWorkbenchPlotStyle), Style) ||
                !FPWorkbenchSeriesBinding.Finite(StackedYCharacterSpacing) || StackedYCharacterSpacing < 0 || StackedYCharacterSpacing > 20 ||
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
            s.TitleFontSize = TitleFontSize; s.XLabelFontSize = XLabelFontSize; s.YLabelFontSize = YLabelFontSize;
            s.TitleAlignment = TitleAlignment; s.XLabelAlignment = XLabelAlignment; s.YLabelAlignment = YLabelAlignment; s.YLabelDirection = YLabelDirection; s.StackedYCharacterSpacing = StackedYCharacterSpacing; s.WhiteBackground = WhiteBackground;
            s.CustomTitleColor = CustomTitleColor; s.CustomXLabelColor = CustomXLabelColor; s.CustomYLabelColor = CustomYLabelColor; s.TitleColor = TitleColor; s.XLabelColor = XLabelColor; s.YLabelColor = YLabelColor;
            s.MarkerSize = MarkerSize; s.Grid = Grid; s.InvertY = InvertY; s.XTickSpacing = XTickSpacing; s.YTickSpacing = YTickSpacing;
        }
        /// <summary>Apply only Y-axis presentation, retaining limits, locks and all shared-X/series controls.</summary>
        public void ApplyY(FPWorkbenchPlotSettings s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s)); Validate();
            if (YScale == FPWorkbenchAxisScale.Log10 && s.LockY) FPWorkbenchAxis.Validate(s.MinimumY, s.MaximumY, YScale);
            s.YScale = YScale; s.YLabel = YLabel; s.YTickSpacing = YTickSpacing;
            s.Grid = Grid; s.InvertY = InvertY;
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
