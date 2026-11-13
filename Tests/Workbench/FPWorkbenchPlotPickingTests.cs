// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Tests
{
    using System;
    using System.IO;
    using System.Collections;
    using UnityEngine.TestTools;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;
    public sealed class FPWorkbenchPlotPickingTests
    {
        [UnityTest] public IEnumerator ExactPickingPreservesSourceRowsAtLargeOriginsWithLogAndInvertedAxes()
        {
            var work = ExactPickingPreservesSourceRowsAtLargeOriginsWithLogAndInvertedAxesCore();
            while (!work.IsCompleted) yield return null;
            work.GetAwaiter().GetResult();
        }
        private async Task ExactPickingPreservesSourceRowsAtLargeOriginsWithLogAndInvertedAxesCore()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPPick-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try {
                string path = Path.Combine(root, "source.csv"); File.WriteAllText(path, "x,y\n1000000000000,1\n1000000000000.25,10\n1000000000000.5,1\n1000000000000.75,10\n1000000000001,100\n1000000000001.25,1000\n1000000000001.5,1\n");
                var data = await FPWorkbenchCsvDataset.ImportAsync(path, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile());
                var binding = new FPWorkbenchSeriesBinding { XMode = FPWorkbenchXMode.Numeric, YColumn = 1, Rows = new FPWorkbenchRowRange { Enabled = true, FirstRow = 2, EndRow = 6 } };
                var series = await FPWorkbenchSeriesCache.BuildAsync(data, binding); var view = await series.QueryAsync(1000000000000.5, 1000000000001.25, 64);
                var settings = new FPWorkbenchPlotSettings { LockY = true, MinimumY = 1, MaximumY = 1000, YScale = FPWorkbenchAxisScale.Log10, InvertY = true, StartX = view.StartX, EndX = view.EndX };
                double fx = new FPWorkbenchAxisMapping(view.StartX, view.EndX, settings.XScale).Fraction(1000000000001);
                double fy = new FPWorkbenchAxisMapping(1, 1000, settings.YScale).Fraction(100);
                var picked = await series.PickDisplayedPointAsync(view, settings, fx, fy, 1200, 300);
                Assert.That(picked.HasValue, Is.True); Assert.That(picked.Value.Row, Is.EqualTo(4)); Assert.That(picked.Value.X, Is.EqualTo(1000000000001d)); Assert.That(picked.Value.Y, Is.EqualTo(100));
                var miss = await series.PickDisplayedPointAsync(view, settings, .5, 0, 1200, 300); Assert.That(miss.HasValue, Is.False);
                using var cancel = new CancellationTokenSource(); cancel.Cancel();
                bool cancelled = false;
                try { await series.PickDisplayedPointAsync(view, settings, fx, fy, 1200, 300, cancellation: cancel.Token); }
                catch (OperationCanceledException) { cancelled = true; }
                Assert.That(cancelled, Is.True);
            } finally { Directory.Delete(root, true); }
        }
        [UnityTest] public IEnumerator EnvelopePickingReturnsActualExtremumAndDeterministicDuplicateRowWithoutInterpolation()
        {
            var work = EnvelopePickingReturnsActualExtremumAndDeterministicDuplicateRowWithoutInterpolationCore();
            while (!work.IsCompleted) yield return null;
            work.GetAwaiter().GetResult();
        }
        private async Task EnvelopePickingReturnsActualExtremumAndDeterministicDuplicateRowWithoutInterpolationCore()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPPickBins-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try {
                string path = Path.Combine(root, "source.csv"); File.WriteAllLines(path, new[] { "x,y" }.Concat(Enumerable.Range(0, 2048).Select(i => "5," + (i == 700 ? 100 : 1))));
                var data = await FPWorkbenchCsvDataset.ImportAsync(path, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile());
                var series = await FPWorkbenchSeriesCache.BuildAsync(data, new FPWorkbenchSeriesBinding { XMode = FPWorkbenchXMode.Numeric, YColumn = 1 });
                var view = await series.QueryAsync(4, 6, 1); var settings = new FPWorkbenchPlotSettings { StartX = 4, EndX = 6, LockY = true, MinimumY = 0, MaximumY = 100 };
                var peak = await series.PickDisplayedPointAsync(view, settings, .5, 0, 800, 300); Assert.That(peak.Value.Row, Is.EqualTo(700)); Assert.That(peak.Value.Y, Is.EqualTo(100));
                var same = await series.PickDisplayedPointAsync(view, settings, .5, .99, 800, 300); Assert.That(same.Value.Row, Is.Zero);
                var middle = await series.PickDisplayedPointAsync(view, settings, .5, .5, 800, 300); Assert.That(middle.HasValue, Is.False, "The middle of an extrema envelope is not necessarily a real sample.");
            } finally { Directory.Delete(root, true); }
        }
    }
}
