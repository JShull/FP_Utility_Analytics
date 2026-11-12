// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Tests
{
    using System.IO;
    using NUnit.Framework;
    public sealed class FPWorkbenchPlotPresetTests
    {
        [Test] public void PresentationRoundTripPreservesTargetNavigationAndRejectsInvalidAtomically()
        {
            var source = new FPWorkbenchPlotSettings { Title = "Depth µ", XLabel = "UTC seconds", YLabel = "Depth (m)", Style = FPWorkbenchPlotStyle.Step,
                InvertY = true, Grid = false, LineWidth = 4, MarkerSize = 8, XTickSpacing = 2, YTickSpacing = .5 };
            var preset = FPWorkbenchPlotPreset.Parse(FPWorkbenchPlotPreset.Capture(source).ToJson());
            var target = new FPWorkbenchPlotSettings { StartX = 20, EndX = 30, HasRange = true, LockY = true, MinimumY = 10, MaximumY = 90 };
            preset.Apply(target);
            Assert.That(target.Title, Is.EqualTo(source.Title)); Assert.That(target.Style, Is.EqualTo(source.Style)); Assert.That(target.InvertY, Is.True);
            Assert.That(target.XTickSpacing, Is.EqualTo(2)); Assert.That(target.Grid, Is.False); Assert.That(target.LineWidth, Is.EqualTo(4));
            Assert.That(target.StartX, Is.EqualTo(20)); Assert.That(target.EndX, Is.EqualTo(30)); Assert.That(target.HasRange && target.LockY, Is.True);
            Assert.That(target.MinimumY, Is.EqualTo(10)); Assert.That(target.MaximumY, Is.EqualTo(90));
            preset.Title = "must not apply"; preset.LineWidth = float.NaN;
            Assert.Throws<InvalidDataException>(() => preset.Apply(target)); Assert.That(target.Title, Is.EqualTo(source.Title));
            Assert.Throws<InvalidDataException>(() => FPWorkbenchPlotPreset.Parse("{}"));
            Assert.Throws<InvalidDataException>(() => FPWorkbenchPlotPreset.Parse(new string('x', FPWorkbenchPlotPreset.MaximumBytes + 1)));
        }
    }
}
