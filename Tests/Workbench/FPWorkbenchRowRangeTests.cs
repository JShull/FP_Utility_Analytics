// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Tests
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;

    public sealed class FPWorkbenchRowRangeTests
    {
        [Test] public async Task ScopedProductsExcludeEqualXNeighborsAndInvalidOutsideRowsBeforeCountingAndSampling()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPRowRange-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try {
                string path = Path.Combine(root, "source.csv");
                const string text = "x,y,z\nbad,bad,bad\n10,999,999\n10,2,20\n10,3,30\n11,4,40\n11,999,999\nbad,bad,bad\n";
                File.WriteAllText(path, text);
                var data = await FPWorkbenchCsvDataset.ImportAsync(path, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile());
                var rows = new FPWorkbenchRowRange { Enabled = true, FirstRow = 2, EndRow = 5 };
                var binding = new FPWorkbenchSeriesBinding { XMode = FPWorkbenchXMode.Numeric, XColumn = 0, YColumn = 1, Rows = rows };
                var series = await FPWorkbenchSeriesCache.BuildAsync(data, binding);
                Assert.That(series.RowCount, Is.EqualTo(3)); Assert.That(series.FirstSourceRow, Is.EqualTo(2));
                var view = await series.QueryAsync(9, 12, 64);
                Assert.That(view.Bins.Sum(b => b.Count), Is.EqualTo(3)); Assert.That(view.Minimum, Is.EqualTo(2)); Assert.That(view.Maximum, Is.EqualTo(4));
                Assert.That(view.Bins.Select(b => b.MinimumRow), Is.EqualTo(new long[] { 2, 3, 4 }));
                var extent = await series.ReadRowExtentAsync(2, 5); Assert.That(extent.FirstX, Is.EqualTo(10)); Assert.That(extent.LastX, Is.EqualTo(11));
                Assert.Throws<ArgumentOutOfRangeException>(() => series.ReadRowExtentAsync(1, 5));
                var overlay = await FPWorkbenchOverlay.BuildAsync(data, binding, new[] { 1, 2 });
                var multi = await overlay.QueryAsync(9, 12, 64); Assert.That(multi.Maximum, Is.EqualTo(40));
                foreach (var columns in new[] { new[] { 0, 1 }, new[] { 0, 1, 2 } }) {
                    var cache = await FPWorkbenchPointCache.BuildAsync(data, columns, rowRange: rows);
                    Assert.That(cache.ValidCount, Is.EqualTo(3)); Assert.That(cache.InvalidCount, Is.Zero);
                    var points = await cache.QueryAsync(cache.Bounds, 2); Assert.That(points.MatchingCount, Is.EqualTo(3));
                    Assert.That(points.Points.All(p => p.Row >= 2 && p.Row < 5), Is.True); Assert.That(points.Points.Count, Is.EqualTo(2));
                    var again = await FPWorkbenchPointCache.BuildAsync(data, columns, rowRange: rows); Assert.That(again.DirectoryPath, Is.EqualTo(cache.DirectoryPath));
                }
                var program = FPWorkbenchFormulaProgram.Compile(data, new FPWorkbenchFormulaSet { DatasetId = data.DatasetId });
                var histogram = await FPWorkbenchHistogram.CalculateAsync(data, program, 1, false, rows.First(data.RowCount), rows.End(data.RowCount), 0, 10, 5);
                Assert.That(histogram.IncludedCount, Is.EqualTo(3)); Assert.That(histogram.InvalidCount, Is.Zero); Assert.That(histogram.OverflowCount, Is.Zero);
                Assert.That(File.ReadAllText(path), Is.EqualTo(text));
                var copy = binding.Copy(data.ColumnCount); copy.Rows.FirstRow = 3; Assert.That(binding.Rows.FirstRow, Is.EqualTo(2));
            } finally { Directory.Delete(root, true); }
        }
        [Test] public async Task ScopedPyramidsAndSingleRowsRetainOriginalIdentitiesAndDistinctCacheKeys()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPRowPyramid-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try {
                string path = Path.Combine(root, "source.csv"); File.WriteAllLines(path, new[] { "x,y" }.Concat(Enumerable.Range(0, 1000).Select(i => i + "," + i)));
                var data = await FPWorkbenchCsvDataset.ImportAsync(path, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile());
                var binding = new FPWorkbenchSeriesBinding { XMode = FPWorkbenchXMode.Numeric, YColumn = 1, Rows = new FPWorkbenchRowRange { Enabled = true, FirstRow = 37, EndRow = 601 } };
                var series = await FPWorkbenchSeriesCache.BuildAsync(data, binding); var view = await series.QueryAsync(0, 999, 1);
                Assert.That(view.Bins.Sum(b => b.Count), Is.EqualTo(564)); Assert.That(view.Minimum, Is.EqualTo(37)); Assert.That(view.Maximum, Is.EqualTo(600));
                var reopened = await FPWorkbenchSeriesCache.BuildAsync(data, binding); Assert.That(reopened.DirectoryPath, Is.EqualTo(series.DirectoryPath));
                binding.Rows = new FPWorkbenchRowRange { Enabled = true, FirstRow = 400, EndRow = 401 };
                var one = await FPWorkbenchSeriesCache.BuildAsync(data, binding); Assert.That(one.DirectoryPath, Is.Not.EqualTo(series.DirectoryPath));
                var single = await one.QueryAsync(399, 401); Assert.That(single.Bins.Single().MinimumRow, Is.EqualTo(400));
                Assert.That(one.FirstX, Is.EqualTo(400)); Assert.That(one.LastX, Is.EqualTo(400));
                binding.Rows = new FPWorkbenchRowRange(); var all = await FPWorkbenchSeriesCache.BuildAsync(data, binding);
                Assert.That(all.RowCount, Is.EqualTo(1000)); Assert.That(all.DirectoryPath, Is.Not.EqualTo(series.DirectoryPath));
            } finally { Directory.Delete(root, true); }
        }
        [Test] public async Task ScopedUtcOriginStartsAtTheFirstIncludedSourceRow()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPRowUtc-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try {
                string path = Path.Combine(root, "source.csv"); File.WriteAllText(path, "date,time,y\nbad,bad,999\n17.01.2025,23:59:59.999999,999\n18.01.2025,00:00:00.000001,2\n18.01.2025,00:00:00.000002,3\n");
                var data = await FPWorkbenchCsvDataset.ImportAsync(path, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile());
                var binding = new FPWorkbenchSeriesBinding { XMode = FPWorkbenchXMode.UtcDateTime, DateColumn = 0, XColumn = 1, YColumn = 2, Rows = new FPWorkbenchRowRange { Enabled = true, FirstRow = 2, EndRow = 4 } };
                var series = await FPWorkbenchSeriesCache.BuildAsync(data, binding);
                Assert.That(series.OriginUtcTicks, Is.EqualTo(new DateTime(2025, 1, 18, 0, 0, 0, DateTimeKind.Utc).Ticks + 10));
                Assert.That(series.FirstX, Is.Zero); Assert.That(series.LastX, Is.EqualTo(.000001).Within(1e-15));
                var view = await series.QueryAsync(-1, 1); Assert.That(view.Bins.Sum(b => b.Count), Is.EqualTo(2)); Assert.That(view.Maximum, Is.EqualTo(3));
            } finally { Directory.Delete(root, true); }
        }
        [Test] public void LegacyDefaultsAndInvalidRangesRemainExplicit()
        {
            var legacy = JsonUtility.FromJson<FPWorkbenchSeriesBinding>("{\"XMode\":0,\"YColumn\":0}").Copy(1);
            Assert.That(legacy.Rows.Enabled, Is.False); Assert.That(legacy.Rows.Count(20), Is.EqualTo(20));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FPWorkbenchRowRange { Enabled = true, FirstRow = 2, EndRow = 2 }.Validate(10));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FPWorkbenchRowRange { Enabled = true, FirstRow = 2, EndRow = 11 }.Validate(10));
        }
    }
}
