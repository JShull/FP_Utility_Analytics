// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Tests
{
    using System;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;

    public sealed class FPWorkbenchPointCacheTests
    {
        [Test] public async Task UnsortedCoordinatesRetainRowsSkipBlocksAndDecimateDeterministically()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPSpatial-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try
            {
                string path = Path.Combine(root, "source.csv"); var text = new StringBuilder("x,y\n");
                for (int row = 0; row < 9000; row++) text.Append(8999 - row).Append(',').Append(row % 17).Append('\n'); File.WriteAllText(path, text.ToString());
                var data = await FPWorkbenchCsvDataset.ImportAsync(path, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile());
                var columns = new[] { 0, 1 }; var building = FPWorkbenchPointCache.BuildAsync(data, columns); columns[0] = 1; var cache = await building;
                Assert.That(cache.ValidCount, Is.EqualTo(9000)); Assert.That(cache.InvalidCount, Is.Zero); Assert.That(cache.HasZ, Is.False);
                var view = await cache.QueryAsync(new FPWorkbenchSpatialBounds(0, 100, 0, 16), 200);
                Assert.That(view.MatchingCount, Is.EqualTo(101)); Assert.That(view.Points.Count, Is.EqualTo(101)); Assert.That(view.RecordsRead, Is.LessThan(4096));
                for (int i = 0; i < view.Points.Count; i++)
                { var point = view.Points[i]; Assert.That(point.X, Is.EqualTo(8999 - point.Row)); Assert.That(point.Y, Is.EqualTo(point.Row % 17)); if (i > 0) Assert.That(point.Row, Is.GreaterThan(view.Points[i - 1].Row)); }
                var small = await cache.QueryAsync(cache.Bounds, 10); var again = await cache.QueryAsync(cache.Bounds, 10); var larger = await cache.QueryAsync(cache.Bounds, 20);
                Assert.That(small.Decimated, Is.True); Assert.That(small.MatchingCount, Is.EqualTo(9000)); Assert.That(small.Points.Count, Is.EqualTo(10));
                for (int i = 0; i < small.Points.Count; i++)
                { Assert.That(small.Points[i].Row, Is.EqualTo(again.Points[i].Row)); bool found = false; foreach (var point in larger.Points) found |= point.Row == small.Points[i].Row; Assert.That(found, Is.True); }
                var none = await cache.QueryAsync(new FPWorkbenchSpatialBounds(-10, -1, 0, 16)); Assert.That(none.Points, Is.Empty); Assert.That(none.RecordsRead, Is.Zero);
                var reopened = await FPWorkbenchPointCache.BuildAsync(data, new[] { 0, 1 }); Assert.That(reopened.DirectoryPath, Is.EqualTo(cache.DirectoryPath));
                var definitions = new FPWorkbenchFormulaSet { DatasetId = data.DatasetId }; definitions.Columns.Add(new FPWorkbenchDerivedColumn { Name = "Z", Expression = "C1 + C2" });
                var program = FPWorkbenchFormulaProgram.Compile(data, definitions); var edits = new FPWorkbenchChangeSet { DatasetId = data.DatasetId };
                edits.Set(data.Cell(8999, 1), "6", "25", FPWorkbenchEditKind.Number, "fixture");
                // Last row modulo 17 is 6; all three coordinates share one immutable correction revision.
                var spatial = await FPWorkbenchPointCache.BuildAsync(data, new[] { 0, 1, 2 }, program, edits.Snapshot());
                Assert.That(spatial.HasZ, Is.True); Assert.That(spatial.FormulaFingerprint, Is.EqualTo(program.Fingerprint));
                var end = await spatial.QueryAsync(new FPWorkbenchSpatialBounds(0, 0, 25, 25, 25, 25));
                Assert.That(end.MatchingCount, Is.EqualTo(1)); Assert.That(end.Points[0].Row, Is.EqualTo(8999)); Assert.That(end.Points[0].Z, Is.EqualTo(25));
                using (var file = new FileStream(Path.Combine(cache.DirectoryPath, "points.bin"), FileMode.Open, FileAccess.Write)) file.WriteByte(255);
                try { await FPWorkbenchPointCache.BuildAsync(data, new[] { 0, 1 }); Assert.Fail("Expected corrupt-cache rejection."); } catch (InvalidDataException) { }
                using var cancel = new CancellationTokenSource(); cancel.Cancel();
                try { await spatial.QueryAsync(spatial.Bounds, cancellation: cancel.Token); Assert.Fail("Expected cancellation."); } catch (OperationCanceledException) { }
                Assert.That(File.ReadAllText(path), Is.EqualTo(text.ToString()));
            }
            finally { Directory.Delete(root, true); }
        }
        [Test] public async Task InvalidPolicyIsExplicitAndFailedBuildDoesNotPublish()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPSpatialInvalid-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try
            {
                string path = Path.Combine(root, "source.csv"); File.WriteAllText(path, "x,y\n2,1\n0,no\n1,1\n");
                var data = await FPWorkbenchCsvDataset.ImportAsync(path, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile());
                try { await FPWorkbenchPointCache.BuildAsync(data, new[] { 0, 1 }); Assert.Fail("Expected invalid-coordinate rejection."); } catch (InvalidDataException) { }
                Assert.That(Directory.GetDirectories(data.CacheDirectory, ".spatial-*"), Is.Empty); Assert.That(Directory.GetDirectories(data.CacheDirectory, "spatial-*"), Is.Empty);
                var cache = await FPWorkbenchPointCache.BuildAsync(data, new[] { 0, 1 }, invalidPolicy: FPWorkbenchInvalidPolicy.Skip);
                Assert.That(cache.ValidCount, Is.EqualTo(2)); Assert.That(cache.InvalidCount, Is.EqualTo(1));
                var points = await cache.QueryAsync(cache.Bounds); Assert.That(points.Points[0].Row, Is.Zero); Assert.That(points.Points[1].Row, Is.EqualTo(2));
                Assert.Throws<ArgumentOutOfRangeException>(() => cache.QueryAsync(cache.Bounds, 65537));
                Assert.Throws<ArgumentException>(() => new FPWorkbenchSpatialBounds(1, 0, 0, 1));
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
