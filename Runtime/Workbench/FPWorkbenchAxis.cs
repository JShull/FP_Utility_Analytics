// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    using System.Collections.Generic;
    public enum FPWorkbenchAxisScale { Linear, Log10 }
    /// <summary>Validated reusable axis map; avoids policy validation and range-log recomputation per rendered point.</summary>
    public sealed class FPWorkbenchAxisMapping
    {
        private readonly double minimum, maximum, span;
        private readonly bool logarithmic, halfSpan;
        public FPWorkbenchAxisMapping(double minimum, double maximum, FPWorkbenchAxisScale scale)
        {
            FPWorkbenchAxis.Validate(minimum, maximum, scale); this.minimum = minimum; this.maximum = maximum;
            logarithmic = scale == FPWorkbenchAxisScale.Log10;
            span = logarithmic ? FPWorkbenchAxis.LogRatio(maximum, minimum) : maximum - minimum;
            halfSpan = double.IsInfinity(span); if (halfSpan) span = maximum * .5 - minimum * .5;
        }
        public double Fraction(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < minimum || value > maximum) throw new ArgumentOutOfRangeException(nameof(value));
            if (value == minimum) return 0; if (value == maximum) return 1;
            return logarithmic ? FPWorkbenchAxis.LogRatio(value, minimum) / span : halfSpan ? (value * .5 - minimum * .5) / span : (value - minimum) / span;
        }
    }
    /// <summary>Double-precision presentation transforms. Never changes stored measurements or timestamp identities.</summary>
    public static class FPWorkbenchAxis
    {
        public static void Validate(double minimum, double maximum, FPWorkbenchAxisScale scale)
        {
            if (!Enum.IsDefined(typeof(FPWorkbenchAxisScale), scale) || !Finite(minimum) || !Finite(maximum) || maximum <= minimum || scale == FPWorkbenchAxisScale.Log10 && minimum <= 0)
                throw new ArgumentException("Axes require finite increasing limits; logarithmic limits must be positive.");
        }
        public static double Fraction(double value, double minimum, double maximum, FPWorkbenchAxisScale scale)
        {
            Validate(minimum, maximum, scale);
            if (!Finite(value) || value < minimum || value > maximum) throw new ArgumentOutOfRangeException(nameof(value), "Transform only values inside the declared axis; clip/exclude other values explicitly.");
            if (value == minimum) return 0; if (value == maximum) return 1;
            if (scale == FPWorkbenchAxisScale.Log10) return LogRatio(value, minimum) / LogRatio(maximum, minimum);
            double span = maximum - minimum;
            return Finite(span) ? (value - minimum) / span : (value * .5 - minimum * .5) / (maximum * .5 - minimum * .5);
        }
        public static double Value(double fraction, double minimum, double maximum, FPWorkbenchAxisScale scale)
        {
            Validate(minimum, maximum, scale);
            if (!Finite(fraction) || fraction < 0 || fraction > 1) throw new ArgumentOutOfRangeException(nameof(fraction));
            if (fraction == 0) return minimum; if (fraction == 1) return maximum;
            double value;
            if (scale == FPWorkbenchAxisScale.Linear) {
                double span = maximum - minimum;
                value = Finite(span) ? minimum + span * fraction : minimum * (1 - fraction) + maximum * fraction;
            } else {
                double distance = LogRatio(maximum, minimum) * fraction;
                // Preserve small deltas at large origins instead of subtracting rounded absolute logarithms.
                if (distance < .0001) { double term = distance, delta = term; for (int n = 2; n <= 8; n++) { term *= distance / n; delta += term; } value = minimum + minimum * delta; }
                else value = Math.Exp(Math.Log(minimum) + distance);
            }
            // Roundoff near representable endpoints must not produce an out-of-domain viewport coordinate.
            return Math.Max(minimum, Math.Min(maximum, value));
        }
        /// <summary>For Log10, spacing is in decades; returned tick values remain in original physical units.</summary>
        public static double[] Ticks(double minimum, double maximum, FPWorkbenchAxisScale scale, double spacing = 0, int maximumTicks = 128)
        {
            Validate(minimum, maximum, scale);
            if (scale == FPWorkbenchAxisScale.Linear) return FPWorkbenchPlot.Ticks(minimum, maximum, spacing, maximumTicks);
            if (!Finite(spacing) || spacing < 0 || maximumTicks < 2 || maximumTicks > 4096) throw new ArgumentException("Invalid logarithmic tick spacing/bound.");
            double lo = Math.Log10(minimum), hi = Math.Log10(maximum);
            if (hi <= lo) return spacing == 0 ? new[] { minimum, maximum } : Array.Empty<double>();
            if (spacing == 0 && hi - lo >= 1) spacing = Math.Max(1, Math.Ceiling((hi - lo) / (maximumTicks - 1)));
            var powers = FPWorkbenchPlot.Ticks(lo, hi, spacing, maximumTicks); var ticks = new List<double>(powers.Length);
            foreach (double power in powers) {
                double tick = Math.Pow(10, power);
                if (Finite(tick) && tick >= minimum && tick <= maximum && (ticks.Count == 0 || tick > ticks[ticks.Count - 1])) ticks.Add(tick);
            }
            return ticks.ToArray();
        }
        public static bool TryNavigate(double minimum, double maximum, FPWorkbenchAxisScale scale, double factor, double anchor, double pan, out double first, out double last)
        {
            first = minimum; last = maximum;
            try { Validate(minimum, maximum, scale); } catch (ArgumentException) { return false; }
            if (!Finite(factor) || factor <= 0 || !Finite(anchor) || !Finite(pan)) return false;
            double lo, hi;
            if (scale == FPWorkbenchAxisScale.Log10) {
                double span = LogRatio(maximum, minimum), next = span * factor;
                if (!Finite(next) || next <= 0) return false;
                double shift = span * (anchor + pan) - next * anchor;
                lo = Shift(minimum, shift); hi = Shift(minimum, shift + next);
            } else {
                double span = maximum - minimum, next = span * factor;
                if (!Finite(next) || next < 1e-9) return false;
                lo = minimum + span * anchor - next * anchor + span * pan; hi = lo + next;
            }
            if (!Finite(lo) || !Finite(hi) || hi <= lo || scale == FPWorkbenchAxisScale.Log10 && lo <= 0) return false;
            first = lo; last = hi; return true;
        }
        public static void ExpandConstant(double value, FPWorkbenchAxisScale scale, out double minimum, out double maximum)
        {
            if (!Finite(value) || !Enum.IsDefined(typeof(FPWorkbenchAxisScale), scale) || scale == FPWorkbenchAxisScale.Log10 && value <= 0) throw new ArgumentException("Cannot expand an invalid axis value.");
            if (scale == FPWorkbenchAxisScale.Log10) { minimum = value / 2; maximum = value * 2; if (minimum == 0) minimum = value; if (!Finite(maximum)) maximum = value; }
            else { double pad = Math.Max(1, Math.Abs(value) * .01); minimum = value - pad; maximum = value + pad; if (!Finite(minimum)) minimum = value; if (!Finite(maximum)) maximum = value; }
            Validate(minimum, maximum, scale);
        }
        private static double Shift(double origin, double distance)
        {
            if (Math.Abs(distance) < .0001) { double term = distance, delta = term; for (int n = 2; n <= 8; n++) { term *= distance / n; delta += term; } return origin + origin * delta; }
            return Math.Exp(Math.Log(origin) + distance);
        }
        internal static double LogRatio(double upper, double lower)
        {
            double delta = upper - lower;
            if (delta <= lower * .0001) {
                double x = delta / lower, term = x, sum = term;
                for (int n = 2; n <= 8; n++) { term *= -x; sum += term / n; }
                return sum;
            }
            double ratio = upper / lower;
            return Finite(ratio) ? Math.Log(ratio) : Math.Log(upper) - Math.Log(lower);
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
