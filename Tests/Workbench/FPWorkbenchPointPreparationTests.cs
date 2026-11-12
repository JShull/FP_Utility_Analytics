// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Tests
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;

    public sealed class FPWorkbenchPointPreparationTests
    {
        [Test] public async Task LocalOriginRetainsSmallDeltasAndRejectsAnInadequateFrame()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPPointPrepare-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try
            {
                string path = Path.Combine(root, "source.csv"); File.WriteAllText(path, "x,y,z\n1000000000000,0,0\n1000000000000.125,1,0\n1000000000003,0,1\n");
                var data = await FPWorkbenchCsvDataset.ImportAsync(path, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile());
                var cache = await FPWorkbenchPointCache.BuildAsync(data, new[] { 0, 1, 2 }); var view = await cache.QueryAsync(cache.Bounds);
                var frame = FPWorkbenchSpatialFrame.Fit(cache.Bounds); var prepared = await FPWorkbenchPointPreparation.PrepareAsync(view, frame);
                Assert.That((float)view.Points[0].X, Is.EqualTo((float)view.Points[1].X), "Direct float coordinates lose the source delta.");
                Assert.That(prepared.Positions[0].x, Is.Not.EqualTo(prepared.Positions[1].x));
                Assert.That(prepared.Positions[0].x, Is.EqualTo(-1)); Assert.That(prepared.Positions[2].x, Is.EqualTo(1));
                Assert.That(prepared.Positions[1].y - prepared.Positions[0].y, Is.EqualTo(1f / 1.5f).Within(1e-6), "One scale preserves the relation between axes.");
                Assert.That(prepared.DatasetId, Is.EqualTo(data.DatasetId)); Assert.That(prepared.MatchingCount, Is.EqualTo(3));
                Assert.That(prepared.MaximumLocalError, Is.LessThanOrEqualTo(1e-6));
                double measured = 0;
                for (int i = 0; i < prepared.Positions.Count; i++)
                {
                    Assert.That(prepared.SourceRows[i], Is.EqualTo(view.Points[i].Row));
                    var original = view.Points[i]; var restored = frame.ToSource(prepared.Positions[i], prepared.SourceRows[i]);
                    measured = Math.Max(measured, Math.Max(Math.Abs(original.X - restored.X), Math.Max(Math.Abs(original.Y - restored.Y), Math.Abs(original.Z - restored.Z))));
                }
                Assert.That(prepared.MaximumSourceReconstructionError, Is.EqualTo(measured));
                try { await FPWorkbenchPointPreparation.PrepareAsync(view, new FPWorkbenchSpatialFrame(0, 0, 0, 1)); Assert.Fail("Expected inadequate-origin rejection."); } catch (InvalidOperationException) { }
                try { await FPWorkbenchPointPreparation.PrepareAsync(view, frame, 0); Assert.Fail("Expected exact-float budget rejection."); } catch (InvalidOperationException) { }
                using var cancel = new CancellationTokenSource(); cancel.Cancel();
                try { await FPWorkbenchPointPreparation.PrepareAsync(view, frame, cancellation: cancel.Token); Assert.Fail("Expected cancellation."); } catch (OperationCanceledException) { }
                Assert.Throws<ArgumentException>(() => FPWorkbenchPointPreparation.PrepareAsync(view, default));
            }
            finally { Directory.Delete(root, true); }
        }
        [Test] public async Task ExtremeFiniteExtentNormalizesWithoutOverflow()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPPointExtreme-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try
            {
                string path = Path.Combine(root, "source.csv"); File.WriteAllText(path, "x,y\n-1e308,0\n1e308,0\n");
                var data = await FPWorkbenchCsvDataset.ImportAsync(path, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile());
                var cache = await FPWorkbenchPointCache.BuildAsync(data, new[] { 0, 1 }); var view = await cache.QueryAsync(cache.Bounds);
                var frame = FPWorkbenchSpatialFrame.Fit(cache.Bounds); var prepared = await FPWorkbenchPointPreparation.PrepareAsync(view, frame, 0);
                Assert.That(frame.OriginX, Is.Zero); Assert.That(frame.SourceUnitsPerLocalUnit, Is.EqualTo(1e308));
                Assert.That(prepared.Positions[0].x, Is.EqualTo(-1)); Assert.That(prepared.Positions[1].x, Is.EqualTo(1)); Assert.That(prepared.MaximumSourceReconstructionError, Is.Zero);
                Assert.Throws<OverflowException>(() => frame.ToSource(new Vector3(3, 0, 0), 0));
                var constant = FPWorkbenchSpatialFrame.Fit(new FPWorkbenchSpatialBounds(3, 3, 4, 4, 5, 5));
                Assert.That(constant.SourceUnitsPerLocalUnit, Is.EqualTo(1)); Assert.That(constant.ToSource(Vector3.zero, 7).Z, Is.EqualTo(5));
                Assert.Throws<ArgumentException>(() => new FPWorkbenchSpatialFrame(0, 0, 0, 0));
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
