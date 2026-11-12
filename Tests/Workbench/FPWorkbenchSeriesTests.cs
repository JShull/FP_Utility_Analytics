// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Tests
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using NUnit.Framework;

    public sealed class FPWorkbenchSeriesTests
    {
        private string root;
        [SetUp] public void Setup() { root = Path.Combine(Path.GetTempPath(), "FPSeriesTest-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); }
        [TearDown] public void Teardown() { Directory.Delete(root, true); }
        private Task<FPWorkbenchCsvDataset> Import(string text)
        {
            string path = Path.Combine(root, "input.csv"); File.WriteAllText(path, text, new UTF8Encoding(false));
            return FPWorkbenchCsvDataset.ImportAsync(path, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile());
        }
        [Test] public async Task PyramidRangeQueryPreservesExtremaAndUsesBoundedRecords()
        {
            var text = new StringBuilder("x,y\n");
            for (int i = 0; i < 100000; i++) text.Append(i).Append(',').AppendLine(i == 50001 ? "999" : i == 70001 ? "---" : "2");
            var dataset = await Import(text.ToString());
            var binding = new FPWorkbenchSeriesBinding { XMode = FPWorkbenchXMode.Numeric, XColumn = 0, YColumn = 1 };
            var series = await FPWorkbenchSeriesCache.BuildAsync(dataset, binding);
            var view = await series.QueryAsync(0, 99999, 128);
            Assert.That(view.Maximum, Is.EqualTo(999)); Assert.That(view.Minimum, Is.EqualTo(2));
            Assert.That(view.Bins.Sum(b => b.Count + b.InvalidCount), Is.EqualTo(100000));
            Assert.That(view.Bins.Sum(b => b.InvalidCount), Is.EqualTo(1));
            Assert.That(view.Bins.Single(b => b.Maximum == 999).MaximumRow, Is.EqualTo(50001));
            Assert.That(view.Bins.Length, Is.LessThanOrEqualTo(514));
            Assert.That(view.RecordsRead, Is.LessThan(3000));
            var detail = await series.QueryAsync(50000, 50002);
            Assert.That(detail.Bins.Length, Is.EqualTo(3)); Assert.That(detail.Maximum, Is.EqualTo(999));
            var reopened = await FPWorkbenchSeriesCache.BuildAsync(dataset, binding);
            Assert.That(reopened.DirectoryPath, Is.EqualTo(series.DirectoryPath));
            Assert.That((await reopened.QueryAsync(50001, 50001.5)).Bins.Single().MinimumRow, Is.EqualTo(50001));
        }
        [Test] public async Task UtcMidnightMicrosecondsDuplicatesAndGapsRetainRowIdentity()
        {
            var dataset = await Import("date,time,depth\n31.12.2025,23:59:59.999900,1\n01.01.2026,00:00:00.000100,2\n01.01.2026,00:00:00.000100,3\n01.01.2026,00:00:02.000000,---\n");
            var series = await FPWorkbenchSeriesCache.BuildAsync(dataset, new FPWorkbenchSeriesBinding { XMode = FPWorkbenchXMode.UtcDateTime, DateColumn = 0, XColumn = 1, YColumn = 2, MaximumGap = 1 });
            Assert.That(series.OriginUtcTicks, Is.EqualTo(new DateTime(2025, 12, 31, 23, 59, 59, DateTimeKind.Utc).AddTicks(9999000).Ticks));
            var view = await series.QueryAsync(0, 3);
            Assert.That(view.Bins[1].FirstX, Is.EqualTo(.0002).Within(1e-12));
            Assert.That(view.Bins[2].FirstX, Is.EqualTo(.0002).Within(1e-12));
            Assert.That(view.Bins[3].GapCount, Is.EqualTo(1)); Assert.That(view.Bins[3].InvalidCount, Is.EqualTo(1));
            var duplicates = await series.QueryAsync(.0002, .00021);
            Assert.That(duplicates.Bins.Length, Is.EqualTo(2));
            Assert.That(duplicates.Bins.Select(b => b.MinimumRow), Is.EqualTo(new long[] { 1, 2 }));
        }
        [TestCase("x,y\n1,2\n0,3\n")]
        [TestCase("x,y\n1,2\n---,3\n")]
        public async Task InvalidOrDecreasingXDoesNotPublishSeries(string text)
        {
            var dataset = await Import(text);
            try { await FPWorkbenchSeriesCache.BuildAsync(dataset, new FPWorkbenchSeriesBinding { XMode = FPWorkbenchXMode.Numeric, XColumn = 0, YColumn = 1 }); Assert.Fail(); }
            catch (InvalidDataException) { }
            Assert.That(Directory.GetDirectories(dataset.CacheDirectory), Is.Empty);
        }
        [Test] public async Task PartialPyramidBoundaryExcludesOutsideSpike()
        {
            var text = new StringBuilder("y\n"); for (int i = 0; i < 10000; i++) text.AppendLine(i == 101 ? "900" : "1");
            var dataset = await Import(text.ToString());
            var series = await FPWorkbenchSeriesCache.BuildAsync(dataset, new FPWorkbenchSeriesBinding());
            var view = await series.QueryAsync(102, 9998, 10);
            Assert.That(view.Maximum, Is.EqualTo(1));
            Assert.That(view.Bins.Sum(b => b.Count), Is.EqualTo(9897));
            Assert.That(view.Bins[0].FirstIndex, Is.EqualTo(102));
            Assert.That(view.Bins.Last().EndIndex, Is.EqualTo(9999));
        }
        [Test] public async Task DamagedSeriesCacheIsRejectedOnReopen()
        {
            var dataset = await Import("x\n1\n2\n"); var binding = new FPWorkbenchSeriesBinding();
            var series = await FPWorkbenchSeriesCache.BuildAsync(dataset, binding);
            using (var file = File.OpenWrite(Path.Combine(series.DirectoryPath, "points.bin"))) { file.WriteByte(255); }
            try { await FPWorkbenchSeriesCache.BuildAsync(dataset, binding); Assert.Fail(); }
            catch (InvalidDataException) { }
        }
    }
}
