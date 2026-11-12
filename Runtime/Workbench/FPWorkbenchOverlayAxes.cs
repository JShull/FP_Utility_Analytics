// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    using System.IO;

    public readonly struct FPWorkbenchAxisRange
    {
        public readonly bool HasValues;
        public readonly double Minimum, Maximum;
        internal FPWorkbenchAxisRange(bool hasValues, double min, double max) { HasValues = hasValues; Minimum = min; Maximum = max; }
    }
    [Serializable] public sealed class FPWorkbenchOverlayAxes
    {
        public bool[] Right = Array.Empty<bool>();
        public FPWorkbenchPlotSettings RightSettings = new FPWorkbenchPlotSettings { Grid = false };
        public void EnsureCount(int count)
        {
            if (count < 1 || count > 8) throw new ArgumentOutOfRangeException(nameof(count));
            Right ??= Array.Empty<bool>(); if (Right.Length != count) Array.Resize(ref Right, count);
        }
        public void Validate(int count)
        {
            if (count < 1 || count > 8) throw new InvalidDataException("An overlay supports 1–8 axis assignments.");
            if (Right == null || Right.Length == 0) Right = new bool[count];
            if (Right.Length != count || RightSettings == null) throw new InvalidDataException("Overlay axis assignment must match its series.");
            FPWorkbenchDocument.ValidatePlot(RightSettings);
        }
        public FPWorkbenchAxisRange Resolve(FPWorkbenchOverlayView view, FPWorkbenchPlotSettings left, bool right, bool useLocks = true)
        {
            if (view == null || view.Series == null || left == null) throw new ArgumentNullException();
            if (Right == null || Right.Length != view.Series.Count || RightSettings == null) throw new ArgumentException("Axis assignments do not match this view.");
            var settings = right ? RightSettings : left; bool any = false;
            double min = 0, max = 0;
            for (int i = 0; i < view.Series.Count; i++)
            {
                if (Right[i] != right || double.IsNaN(view.Series[i].Minimum)) continue;
                if (!any) { min = view.Series[i].Minimum; max = view.Series[i].Maximum; any = true; }
                else { min = Math.Min(min, view.Series[i].Minimum); max = Math.Max(max, view.Series[i].Maximum); }
            }
            if (!any) return new FPWorkbenchAxisRange(false, 0, 1);
            if (useLocks && settings.LockY) { min = settings.MinimumY; max = settings.MaximumY; }
            if (min == max) { double pad = Math.Max(1, Math.Abs(min) * .01); min -= pad; max += pad; }
            if (!Finite(min) || !Finite(max) || max <= min || !Finite(max - min)) throw new ArgumentException("Axis needs finite positive Y span; choose explicit limits for this range.");
            return new FPWorkbenchAxisRange(true, min, max);
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
