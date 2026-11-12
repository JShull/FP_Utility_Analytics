// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Tests
{
    using System;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;

    public sealed class FPWorkbenchDataTests
    {
        private string root, source;
        [SetUp] public void Setup()
        {
            root = Path.Combine(Path.GetTempPath(), "FPWorkbenchTest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root); source = Path.Combine(root, "source.csv");
        }
        [TearDown] public void Teardown() { if (Directory.Exists(root)) Directory.Delete(root, true); }
        private Task<FPWorkbenchCsvDataset> Import(string text)
        {
            File.WriteAllText(source, text, new UTF8Encoding(false));
            return FPWorkbenchCsvDataset.ImportAsync(source, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile());
        }
        [Test] public async Task MultilineQuotedRecordsHaveStableExactOffsetsAndRetainLargeIntegers()
        {
            string text = "name,value\r\n\"a,b\r\nc\",9007199254740993\r\n\"say \"\"hello\"\"\",2\r\n";
            var dataset = await Import(text);
            Assert.That(dataset.RowCount, Is.EqualTo(2));
            var page = await dataset.ReadPageAsync(0, 2, 0, 2);
            Assert.That(page.Values[0][0], Is.EqualTo("a,b\r\nc"));
            Assert.That(page.Values[0][1], Is.EqualTo("9007199254740993"));
            Assert.That(page.Values[1][0], Is.EqualTo("say \"hello\""));
            var last = await dataset.ReadPageAsync(1, 1, 1, 1);
            Assert.That(last.Values[0][0], Is.EqualTo("2"));
            Assert.That(File.ReadAllText(source), Is.EqualTo(text));
        }
        [Test] public async Task ReopenVerifiesSourceContentAndPreservesIdentity()
        {
            var dataset = await Import("a,b\n1,2\n3,4\n");
            var reopened = await FPWorkbenchCsvDataset.OpenAsync(dataset.CacheDirectory);
            Assert.That(reopened.Cell(1, 1), Is.EqualTo(dataset.Cell(1, 1)));
            Assert.That((await reopened.ReadPageAsync(1, 1, 0, 2)).Values[0][1], Is.EqualTo("4"));
            var time = File.GetLastWriteTimeUtc(source);
            File.WriteAllText(source, "a,b\n1,9\n3,4\n", new UTF8Encoding(false)); File.SetLastWriteTimeUtc(source, time);
            try { await FPWorkbenchCsvDataset.OpenAsync(dataset.CacheDirectory); Assert.Fail("Changed bytes must be rejected even if size/mtime match."); }
            catch (InvalidDataException) { }
        }
        [TestCase("a,b\n1\n")]
        [TestCase("a,b\n\"unterminated,2")]
        [TestCase("a,b\nfoo\"bar,2\n")]
        [TestCase("a,b\n\"foo\"bar,2\n")]
        public async Task MalformedImportDoesNotPublishPartialCache(string text)
        {
            try { await Import(text); Assert.Fail("Malformed CSV must fail."); }
            catch (InvalidDataException) { }
            Assert.That(Directory.Exists(Path.Combine(root, "cache")), Is.False);
            Assert.That(Directory.GetDirectories(root), Is.Empty);
        }
        [Test] public async Task PageBoundsAndSourceModificationAreExplicitFailures()
        {
            var dataset = await Import("a\n1\n");
            Assert.Throws<ArgumentOutOfRangeException>(() => dataset.ReadPageAsync(0, 257, 0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => dataset.Cell(1, 0));
            File.AppendAllText(source, "2\n");
            try { await dataset.ReadPageAsync(0, 1, 0, 1); Assert.Fail("Stale source must fail."); }
            catch (InvalidDataException) { }
        }
        [Test] public async Task CancellationAndNoOverwritePreserveExistingFiles()
        {
            var dataset = await Import("a\n1\n");
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            try { await FPWorkbenchCsvDataset.ImportAsync(source, Path.Combine(root, "cancelled"), new FPWorkbenchCsvProfile(), cancel.Token); Assert.Fail(); }
            catch (OperationCanceledException) { }
            try { await FPWorkbenchCsvDataset.ImportAsync(source, dataset.CacheDirectory, new FPWorkbenchCsvProfile()); Assert.Fail(); }
            catch (IOException) { }
            Assert.That(Directory.Exists(Path.Combine(root, "cancelled")), Is.False);
            Assert.That((await dataset.ReadPageAsync(0, 1, 0, 1)).Values[0][0], Is.EqualTo("1"));
        }
        [Test] public async Task ExplicitLegacyEncodingAndUtf8Bom()
        {
            File.WriteAllBytes(source, new byte[] { (byte)'x', 0xb5, 10, (byte)'1', 10 });
            var legacy = await FPWorkbenchCsvDataset.ImportAsync(source, Path.Combine(root, "legacy"), new FPWorkbenchCsvProfile { EncodingCodePage = 1252 });
            Assert.That(legacy.GetColumnName(0), Is.EqualTo("xµ"));
            File.WriteAllText(source, "depth\n5\n", new UTF8Encoding(true));
            var utf8 = await FPWorkbenchCsvDataset.ImportAsync(source, Path.Combine(root, "utf8"), new FPWorkbenchCsvProfile());
            Assert.That(utf8.GetColumnName(0), Is.EqualTo("depth"));
        }
        [Test] public async Task FullColumnEnvelopePreservesSpikeInvalidCountsAndSourceRow()
        {
            var text = new StringBuilder("depth\n");
            for (int i = 0; i < 10000; i++) text.AppendLine(i == 5301 ? "1000" : i == 7 ? "---" : "2");
            var dataset = await Import(text.ToString());
            var plot = await FPWorkbenchPlot.SummarizeAsync(dataset, 0, 0, dataset.RowCount, 10);
            Assert.That(plot.Maximum, Is.EqualTo(1000)); Assert.That(plot.Minimum, Is.EqualTo(2));
            Assert.That(plot.InvalidCount, Is.EqualTo(1)); Assert.That(plot.Bins[5].MaximumRow, Is.EqualTo(5301));
            Assert.That(plot.Bins[5].Count, Is.EqualTo(1000));
            Assert.That((await dataset.ReadPageAsync(5301, 1, 0, 1)).Values[0][0], Is.EqualTo("1000"));
        }
        [Test] public void TicksAreBoundedAndCellRowsAre64Bit()
        {
            Assert.That(FPWorkbenchPlot.Ticks(0, 10, 2), Is.EqualTo(new double[] { 0, 2, 4, 6, 8, 10 }));
            Assert.Throws<ArgumentException>(() => FPWorkbenchPlot.Ticks(0, 10, 1e-12));
            long row = (long)int.MaxValue + 100;
            Assert.That(new FPWorkbenchCell("revision", row, 2).Row, Is.EqualTo(row));
            Assert.That(new FPWorkbenchRange(row, row + 1, 2, 3).EndRow, Is.EqualTo(row + 1));
        }
        [Test] public void ManualColumnWidthAndAutoResetPreserveOtherOverrides()
        {
            var layout = new FPWorkbenchColumnLayout(); layout.EnsureColumns(10);
            layout.ManualWidths[0] = 240; layout.ManualWidths[1] = 80;
            var widths = layout.Fit(700, 0);
            Assert.That(widths[0], Is.EqualTo(240)); Assert.That(widths[1], Is.EqualTo(80));
            Assert.That(widths[2], Is.GreaterThanOrEqualTo(110));
            string saved = UnityEngine.JsonUtility.ToJson(layout);
            var restored = UnityEngine.JsonUtility.FromJson<FPWorkbenchColumnLayout>(saved); restored.EnsureColumns(10);
            Assert.That(restored.ManualWidths[0], Is.EqualTo(240));
            restored.ManualWidths[0] = 0;
            Assert.That(restored.ManualWidths[1], Is.EqualTo(80));
            Assert.That(restored.Fit(700, 0)[0], Is.Not.EqualTo(240));
            restored.ShowRowHeader = false; restored.RowHeaderWidth = 140;
            var rowLayout = UnityEngine.JsonUtility.FromJson<FPWorkbenchColumnLayout>(UnityEngine.JsonUtility.ToJson(restored)); rowLayout.EnsureColumns(10);
            Assert.That(rowLayout.ShowRowHeader, Is.False); Assert.That(rowLayout.RowHeaderWidth, Is.EqualTo(140));
            var legacy = new FPWorkbenchColumnLayout { Version = 1, ShowRowHeader = false, RowHeaderWidth = 0 };
            legacy.EnsureColumns(10); Assert.That(legacy.ShowRowHeader, Is.True); Assert.That(legacy.RowHeaderWidth, Is.EqualTo(76));
        }
        [Test] public async Task ChangedIndexIsRejectedBeforeCellsCanBeRetargeted()
        {
            var dataset = await Import("x\n11\n22\n");
            using (var writer = new BinaryWriter(File.OpenWrite(Path.Combine(dataset.CacheDirectory, "rows.fpwi"))))
            { writer.BaseStream.Position = 16; writer.Write(5L); }
            try { await FPWorkbenchCsvDataset.OpenAsync(dataset.CacheDirectory); Assert.Fail("Index changes must fail admission."); }
            catch (InvalidDataException) { }
        }
        [Test, Explicit("Local recovered deployment qualification; no private measurements are stored in the repository.")]
        public async Task RecoveredCsvQualifiesBackgroundIndexAndDistantPage()
        {
            string raw = @"C:\GItHub\FPLibraries\BioLoggingData\RAW mn250117-P46_recovered\mn250117-P46\raw\csvs\mn250117-P46_001.csv";
            Assert.That(File.Exists(raw), Is.True, "Recovered fixture is required for this explicit test.");
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var dataset = await FPWorkbenchCsvDataset.ImportAsync(raw, Path.Combine(root, "real"), new FPWorkbenchCsvProfile { EncodingCodePage = 1252 });
            long importMs = timer.ElapsedMilliseconds; timer.Restart();
            var page = await dataset.ReadPageAsync(dataset.RowCount - 1, 1, 0, 8);
            Assert.That(page.RowCount, Is.EqualTo(1));
            Assert.That(dataset.GetColumnName(18), Does.Contain("Depth"));
            var first = await dataset.ReadPageAsync(0, 1, 0, 4);
            Assert.That(first.Values[0][0], Is.EqualTo("17.01.2025"));
            Assert.That(first.Values[0][1], Is.EqualTo("11:49:52.000000"));
            timer.Restart();
            var series = await FPWorkbenchSeriesCache.BuildAsync(dataset, new FPWorkbenchSeriesBinding {
                XMode = FPWorkbenchXMode.UtcDateTime, DateColumn = 0, XColumn = 1, YColumn = 18 });
            long buildMs = timer.ElapsedMilliseconds; timer.Restart();
            var overview = await series.QueryAsync(series.FirstX, series.LastX, 512);
            Assert.That(series.FirstX, Is.EqualTo(0)); Assert.That(series.LastX, Is.GreaterThan(0));
            Assert.That(series.OriginUtcTicks, Is.EqualTo(new DateTime(2025, 1, 17, 11, 49, 52, DateTimeKind.Utc).Ticks));
            long covered = 0; foreach (var bin in overview.Bins) covered += bin.Count + bin.InvalidCount;
            Assert.That(covered, Is.EqualTo(dataset.RowCount), "TDR overview must cover every source row.");
            Assert.That(overview.Bins.Length, Is.LessThanOrEqualTo(2050));
            Assert.That(overview.RecordsRead, Is.LessThan(dataset.RowCount));
            var detail = await series.QueryAsync(0, .005);
            Assert.That(detail.Bins.Length, Is.EqualTo(3));
            var depth = await dataset.ReadPageAsync(0, 3, 18, 1);
            for (int i = 0; i < 3; i++) {
                Assert.That(detail.Bins[i].MinimumRow, Is.EqualTo(i));
                Assert.That(detail.Bins[i].FirstX, Is.EqualTo(i * .0025).Within(1e-9));
                Assert.That(detail.Bins[i].Minimum, Is.EqualTo(double.Parse(depth.Values[i][0], System.Globalization.CultureInfo.InvariantCulture)));
            }
            UnityEngine.Debug.Log("TDR_build_ms=" + buildMs + "; overview+detail_ms=" + timer.ElapsedMilliseconds +
                "; overview_records=" + overview.RecordsRead + "; bins=" + overview.Bins.Length);
            UnityEngine.Debug.Log("Rows=" + dataset.RowCount + "; import_ms=" + importMs + "; post_build_queries_ms=" + timer.ElapsedMilliseconds + "; source_bytes=" + new FileInfo(raw).Length);
        }
    }
}
