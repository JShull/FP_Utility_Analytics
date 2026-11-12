// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Tests
{
    using System.IO;
    using NUnit.Framework;
    public sealed class FPWorkbenchPlotPresetTests
    {
        [Test] public void PointAppearancePresetRejectsInvalidColorSizeAndForeignDocuments()
        {
            var preset = FPWorkbenchPoint3DPreset.Parse(new FPWorkbenchPoint3DPreset { Color = new UnityEngine.Color(.2f, .4f, .6f, .8f), PointSize = 12 }.ToJson());
            Assert.That(preset.PointSize, Is.EqualTo(12)); Assert.That(preset.Color.a, Is.EqualTo(.8f));
            Assert.Throws<InvalidDataException>(() => FPWorkbenchPoint3DPreset.Parse("{}"));
            Assert.Throws<InvalidDataException>(() => FPWorkbenchPoint3DPreset.Parse(new FPWorkbenchHistogramPreset().ToJson()));
            Assert.Throws<InvalidDataException>(() => FPWorkbenchPoint3DPreset.Parse(new string('x', FPWorkbenchPoint3DPreset.MaximumBytes + 1)));
            preset.PointSize = float.NaN; Assert.Throws<InvalidDataException>(() => preset.ToJson());
            preset.PointSize = 4; preset.Color = new UnityEngine.Color(2, 0, 0); Assert.Throws<InvalidDataException>(() => preset.ToJson());
        }
        [Test] public void HistogramPresentationRoundTripRejectsForeignAndOversizedDocuments()
        {
            var preset = FPWorkbenchHistogramPreset.Parse(new FPWorkbenchHistogramPreset { Density = true, Grid = false }.ToJson());
            Assert.That(preset.Density, Is.True); Assert.That(preset.Grid, Is.False);
            Assert.Throws<InvalidDataException>(() => FPWorkbenchHistogramPreset.Parse("{}"));
            Assert.Throws<InvalidDataException>(() => FPWorkbenchHistogramPreset.Parse(FPWorkbenchPlotPreset.Capture(new FPWorkbenchPlotSettings()).ToJson()));
            Assert.Throws<InvalidDataException>(() => FPWorkbenchHistogramPreset.Parse(new string('x', FPWorkbenchHistogramPreset.MaximumBytes + 1)));
            preset.Version = 2; Assert.Throws<InvalidDataException>(() => preset.ToJson());
        }
        [Test] public void RightAxisPresentationPreservesSharedControlsAndRejectsInvalidDomainAtomically()
        {
            var source = new FPWorkbenchPlotSettings { YScale = FPWorkbenchAxisScale.Log10, YLabel = "Pressure", YTickSpacing = 2, Grid = false, InvertY = true };
            var preset = FPWorkbenchPlotPreset.Parse(FPWorkbenchPlotPreset.Capture(source).ToJson());
            var target = new FPWorkbenchPlotSettings { Title = "Keep", XLabel = "Seconds", XScale = FPWorkbenchAxisScale.Linear,
                HasRange = true, StartX = -10, EndX = 10, XTickSpacing = 3, Style = FPWorkbenchPlotStyle.Step,
                LineWidth = 4, MarkerSize = 7, LockY = true, MinimumY = 1, MaximumY = 100 };
            preset.ApplyY(target);
            Assert.That(target.YScale, Is.EqualTo(FPWorkbenchAxisScale.Log10)); Assert.That(target.YLabel, Is.EqualTo("Pressure"));
            Assert.That(target.YTickSpacing, Is.EqualTo(2)); Assert.That(target.Grid, Is.False); Assert.That(target.InvertY, Is.True);
            Assert.That(target.Title, Is.EqualTo("Keep")); Assert.That(target.XLabel, Is.EqualTo("Seconds"));
            Assert.That(target.XScale, Is.EqualTo(FPWorkbenchAxisScale.Linear)); Assert.That(target.XTickSpacing, Is.EqualTo(3));
            Assert.That(target.StartX, Is.EqualTo(-10)); Assert.That(target.EndX, Is.EqualTo(10)); Assert.That(target.HasRange, Is.True);
            Assert.That(target.Style, Is.EqualTo(FPWorkbenchPlotStyle.Step)); Assert.That(target.LineWidth, Is.EqualTo(4)); Assert.That(target.MarkerSize, Is.EqualTo(7));
            Assert.That(target.LockY, Is.True); Assert.That(target.MinimumY, Is.EqualTo(1)); Assert.That(target.MaximumY, Is.EqualTo(100));
            target.MinimumY = 0; target.YScale = FPWorkbenchAxisScale.Linear; target.YLabel = "Retain";
            Assert.Throws<System.ArgumentException>(() => preset.ApplyY(target));
            Assert.That(target.YLabel, Is.EqualTo("Retain")); Assert.That(target.YScale, Is.EqualTo(FPWorkbenchAxisScale.Linear));
        }
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
