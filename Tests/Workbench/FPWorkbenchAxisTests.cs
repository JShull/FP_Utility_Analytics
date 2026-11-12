// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Tests
{
    using System;
    using NUnit.Framework;
    public sealed class FPWorkbenchAxisTests
    {
        [Test] public void LinearAndLogTransformsRetainEndpointsAndInvertAcrossExtremeDomains()
        {
            Assert.That(FPWorkbenchAxis.Fraction(10, 1, 100, FPWorkbenchAxisScale.Log10), Is.EqualTo(.5).Within(1e-14));
            Assert.That(FPWorkbenchAxis.Value(.5, 1, 100, FPWorkbenchAxisScale.Log10), Is.EqualTo(10).Within(1e-12));
            Assert.That(FPWorkbenchAxis.Fraction(0, -double.MaxValue, double.MaxValue, FPWorkbenchAxisScale.Linear), Is.EqualTo(.5));
            Assert.That(FPWorkbenchAxis.Value(.5, -double.MaxValue, double.MaxValue, FPWorkbenchAxisScale.Linear), Is.EqualTo(0));
            foreach (double f in new[] { 0d, .1, .5, .9, 1d }) {
                double x = FPWorkbenchAxis.Value(f, double.Epsilon, double.MaxValue, FPWorkbenchAxisScale.Log10);
                Assert.That(double.IsNaN(x) || double.IsInfinity(x) || x <= 0, Is.False);
                Assert.That(FPWorkbenchAxis.Fraction(x, double.Epsilon, double.MaxValue, FPWorkbenchAxisScale.Log10), Is.EqualTo(f).Within(1e-12));
            }
            double minimum = 1e300;
            long bits = BitConverter.DoubleToInt64Bits(minimum);
            double maximum = BitConverter.Int64BitsToDouble(bits + 4), middle = BitConverter.Int64BitsToDouble(bits + 2);
            Assert.That(FPWorkbenchAxis.Fraction(middle, minimum, maximum, FPWorkbenchAxisScale.Log10), Is.EqualTo(.5).Within(1e-14));
            Assert.That(FPWorkbenchAxis.Value(.5, minimum, maximum, FPWorkbenchAxisScale.Log10), Is.EqualTo(middle));
            Assert.That(FPWorkbenchAxis.Value(0, minimum, maximum, FPWorkbenchAxisScale.Log10), Is.EqualTo(minimum));
            Assert.That(FPWorkbenchAxis.Value(1, minimum, maximum, FPWorkbenchAxisScale.Log10), Is.EqualTo(maximum));
        }
        [Test] public void LogNavigationPreservesAnchorsAndPresetsValidateWithoutRetargetingViewport()
        {
            var settings = new FPWorkbenchPlotSettings { XScale = FPWorkbenchAxisScale.Log10, YScale = FPWorkbenchAxisScale.Log10, StartX = 1, EndX = 100,
                MinimumY = 1, MaximumY = 100, LockY = true, HasRange = true, Title = "Log view" };
            Assert.That(settings.Navigate(.5, .5, 0), Is.True);
            Assert.That(Math.Sqrt(settings.StartX * settings.EndX), Is.EqualTo(10).Within(1e-12));
            Assert.That(settings.EndX / settings.StartX, Is.EqualTo(10).Within(1e-12));
            Assert.That(FPWorkbenchAxis.TryNavigate(1, 100, FPWorkbenchAxisScale.Log10, 1, .5, .5, out double left, out double right), Is.True);
            Assert.That(left, Is.EqualTo(10).Within(1e-12)); Assert.That(right, Is.EqualTo(1000).Within(1e-9));
            Assert.That(FPWorkbenchAxis.TryNavigate(double.Epsilon, double.Epsilon * 2, FPWorkbenchAxisScale.Log10, 1, .5, -10000, out _, out _), Is.False);
            var map = new FPWorkbenchAxisMapping(1, 100, FPWorkbenchAxisScale.Log10); Assert.That(map.Fraction(10), Is.EqualTo(.5).Within(1e-14));
            FPWorkbenchAxis.ExpandConstant(double.Epsilon, FPWorkbenchAxisScale.Log10, out left, out right); Assert.That(left, Is.EqualTo(double.Epsilon)); Assert.That(right, Is.EqualTo(double.Epsilon * 2));
            var preset = FPWorkbenchPlotPreset.Parse(FPWorkbenchPlotPreset.Capture(settings).ToJson());
            var target = new FPWorkbenchPlotSettings { HasRange = true, StartX = 2, EndX = 20, LockY = true, MinimumY = 2, MaximumY = 20 };
            preset.Apply(target); Assert.That(target.XScale, Is.EqualTo(FPWorkbenchAxisScale.Log10)); Assert.That(target.StartX, Is.EqualTo(2));
            var invalid = new FPWorkbenchPlotSettings { HasRange = true, StartX = 0, EndX = 10, Title = "retained" };
            Assert.Throws<ArgumentException>(() => preset.Apply(invalid)); Assert.That(invalid.Title, Is.EqualTo("retained")); Assert.That(invalid.XScale, Is.EqualTo(FPWorkbenchAxisScale.Linear));
        }
        [Test] public void LogTicksRemainBoundedPositiveOrderedAndInOriginalUnits()
        {
            Assert.That(FPWorkbenchAxis.Ticks(1, 1000, FPWorkbenchAxisScale.Log10), Is.EqualTo(new[] { 1d, 10, 100, 1000 }));
            Assert.That(FPWorkbenchAxis.Ticks(1, 10000, FPWorkbenchAxisScale.Log10, 2), Is.EqualTo(new[] { 1d, 100, 10000 }));
            var broad = FPWorkbenchAxis.Ticks(double.Epsilon, double.MaxValue, FPWorkbenchAxisScale.Log10, maximumTicks: 128);
            Assert.That(broad.Length, Is.LessThanOrEqualTo(128)); Assert.That(broad.Length, Is.GreaterThan(1));
            for (int i = 0; i < broad.Length; i++) { Assert.That(broad[i], Is.GreaterThan(0)); if (i > 0) Assert.That(broad[i], Is.GreaterThan(broad[i - 1])); }
            double small = 1e300, next = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(small) + 1);
            Assert.That(FPWorkbenchAxis.Ticks(small, next, FPWorkbenchAxisScale.Log10).Length, Is.LessThanOrEqualTo(128));
            Assert.Throws<ArgumentException>(() => FPWorkbenchAxis.Ticks(1, 1000, FPWorkbenchAxisScale.Log10, .001, 128));
            Assert.Throws<ArgumentException>(() => FPWorkbenchAxis.Validate(0, 1, FPWorkbenchAxisScale.Log10));
            Assert.Throws<ArgumentOutOfRangeException>(() => FPWorkbenchAxis.Fraction(0, 1, 10, FPWorkbenchAxisScale.Log10));
            Assert.Throws<ArgumentOutOfRangeException>(() => FPWorkbenchAxis.Value(1.1, 1, 10, FPWorkbenchAxisScale.Log10));
            Assert.Throws<ArgumentException>(() => FPWorkbenchAxis.Validate(1, 10, (FPWorkbenchAxisScale)999));
        }
    }
}
