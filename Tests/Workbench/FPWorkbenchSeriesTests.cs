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
        [Test] public async Task TypedPhysicalSeriesUseDeclaredCultureAndSeparateCacheRevisions()
        {
            var data = await Import("x,y\n\"1,25\",\"2,5\"\n\"2,25\",---\n");
            var schema = new FPWorkbenchTypedSchema { DatasetId = data.DatasetId, Columns = new[] {
                new FPWorkbenchColumnDeclaration { Column = 0, ColumnId = data.Cell(0, 0).ColumnId, Type = FPWorkbenchColumnType.Real, Culture = "de-DE" },
                new FPWorkbenchColumnDeclaration { Column = 1, ColumnId = data.Cell(0, 1).ColumnId, Type = FPWorkbenchColumnType.Real, Culture = "de-DE", MissingTokens = new[] { "---" } } } };
            var program = FPWorkbenchFormulaProgram.Compile(data, new FPWorkbenchFormulaSet { DatasetId = data.DatasetId }, schema.Compile(data));
            var binding = new FPWorkbenchSeriesBinding { XMode = FPWorkbenchXMode.Numeric, XColumn = 0, YColumn = 1 };
            var series = await FPWorkbenchSeriesCache.BuildAsync(data, binding, calculations: program);
            Assert.That(series.FirstX, Is.EqualTo(1.25)); Assert.That(series.LastX, Is.EqualTo(2.25));
            var view = await series.QueryAsync(1, 3);
            Assert.That(view.Maximum, Is.EqualTo(2.5)); Assert.That(view.Bins.Sum(b => b.InvalidCount), Is.EqualTo(1));
            Assert.That(series.FormulaFingerprint, Is.EqualTo(program.Fingerprint));
            var edits = new FPWorkbenchChangeSet { DatasetId = data.DatasetId };
            edits.Set(data.Cell(0, 1), "2,5", "7,5", FPWorkbenchEditKind.Text, "fixture");
            var corrected = await FPWorkbenchSeriesCache.BuildAsync(data, binding, changes: edits.Snapshot(), calculations: program);
            Assert.That((await corrected.QueryAsync(1, 3)).Maximum, Is.EqualTo(7.5));
            Assert.That((await series.QueryAsync(1, 3)).Maximum, Is.EqualTo(2.5));
            schema.Columns[1].Type = FPWorkbenchColumnType.Text;
            var other = FPWorkbenchFormulaProgram.Compile(data, new FPWorkbenchFormulaSet { DatasetId = data.DatasetId }, schema.Compile(data));
            var text = await FPWorkbenchSeriesCache.BuildAsync(data, binding, calculations: other);
            Assert.That(text.DirectoryPath, Is.Not.EqualTo(series.DirectoryPath));
            Assert.That((await text.QueryAsync(1, 3)).Bins.Sum(b => b.InvalidCount), Is.EqualTo(2));
            var session = new FPWorkbenchFigureSession(data, binding, new FPWorkbenchPlotSettings(), calculations: program);
            using (var lease = session.Retain()) {
                long revision = session.Revision;
                session.ApplyCalculations(other);
                Assert.That(session.Revision, Is.GreaterThan(revision), "Physical plots must invalidate when typed interpretation changes.");
                revision = session.Revision; session.ApplyCalculations(other);
                Assert.That(session.Revision, Is.EqualTo(revision));
                session.ApplyCalculations(null);
                Assert.That(session.Revision, Is.GreaterThan(revision), "Removing a declaration restores the legacy interpretation.");
            }
            schema.Columns[0].Type = FPWorkbenchColumnType.Text;
            var invalidX = FPWorkbenchFormulaProgram.Compile(data, new FPWorkbenchFormulaSet { DatasetId = data.DatasetId }, schema.Compile(data));
            try { await FPWorkbenchSeriesCache.BuildAsync(data, binding, calculations: invalidX); Assert.Fail("Expected invalid typed X rejection."); }
            catch (InvalidDataException) { }
            Assert.That(Directory.GetDirectories(data.CacheDirectory, ".series-*"), Is.Empty);
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
        [Test] public async Task UtcBindingsRequireCompleteDatesAndRetainHundredNanosecondOffsets()
        {
            var data = await Import("date,time,y\n2026-01-17,00:00:00.0000000,1\n2026-01-17,00:00:00.0000001,2\n");
            var binding = new FPWorkbenchSeriesBinding { XMode = FPWorkbenchXMode.UtcDateTime, DateColumn = 0, XColumn = 1, YColumn = 2, UtcFormat = "yyyy-MM-dd HH:mm:ss.fffffff" };
            var series = await FPWorkbenchSeriesCache.BuildAsync(data, binding);
            Assert.That(series.OriginUtcTicks, Is.EqualTo(new DateTime(2026, 1, 17, 0, 0, 0, DateTimeKind.Utc).Ticks));
            var view = await series.QueryAsync(0, .000001); Assert.That(view.Bins.Length, Is.EqualTo(2));
            Assert.That(view.Bins[1].FirstX, Is.EqualTo(.0000001).Within(1e-20));
            int cacheCount = Directory.GetDirectories(data.CacheDirectory).Length;
            foreach (string format in new[] { "dd.MM HH:mm:ss.fffffff", "HH:mm:ss.fffffff", "yyyy-MM-dd HH:mm:ssK", "yyyy-MM-dd HH:mm:ss.ffffffff" }) {
                binding.UtcFormat = format; Assert.Throws<ArgumentException>(() => FPWorkbenchSeriesCache.BuildAsync(data, binding));
            }
            Assert.That(Directory.GetDirectories(data.CacheDirectory).Length, Is.EqualTo(cacheCount));
            var document = new FPWorkbenchDocument(); document.Sheets.Add(new FPWorkbenchSheetDocument { DatasetId = data.DatasetId,
                SourcePath = data.SourcePath, CacheDirectory = data.CacheDirectory, Import = data.ImportProfile, Binding = binding });
            Assert.Throws<InvalidDataException>(() => document.Validate(), "Unsafe/incomplete UTC declarations cannot be saved as valid workbook metadata.");
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
        [Test] public async Task SourceRowExtentReadsOnlyEndpointsAndRetainsDuplicateXIdentity()
        {
            var data = await Import("x,y\n10,1\n20,2\n20,3\n30,4\n");
            var series = await FPWorkbenchSeriesCache.BuildAsync(data, new FPWorkbenchSeriesBinding { XMode = FPWorkbenchXMode.Numeric, YColumn = 1 });
            var extent = await series.ReadRowExtentAsync(1, 3);
            Assert.That(extent.DatasetId, Is.EqualTo(data.DatasetId)); Assert.That(extent.FirstRow, Is.EqualTo(1)); Assert.That(extent.EndRow, Is.EqualTo(3));
            Assert.That(extent.FirstX, Is.EqualTo(20)); Assert.That(extent.LastX, Is.EqualTo(20)); Assert.That(extent.RecordsRead, Is.EqualTo(2));
            Assert.That((await series.ReadRowExtentAsync(2, 3)).RecordsRead, Is.EqualTo(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => series.ReadRowExtentAsync(3, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => series.ReadRowExtentAsync(0, 5));
            using (var writer = new BinaryWriter(File.OpenWrite(Path.Combine(series.DirectoryPath, "points.bin")))) { writer.BaseStream.Position = 32 + 16; writer.Write(999L); }
            try { await series.ReadRowExtentAsync(1, 3); Assert.Fail("Endpoint source-row tampering must reject."); } catch (InvalidDataException) { }
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
