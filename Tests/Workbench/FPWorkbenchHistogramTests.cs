// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Tests
{
    using System;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;

    public sealed class FPWorkbenchHistogramTests
    {
        private string root;
        [SetUp] public void Setup() { root = Path.Combine(Path.GetTempPath(), "FPHistogram-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); }
        [TearDown] public void Teardown() { Directory.Delete(root, true); }
        private Task<FPWorkbenchCsvDataset> Import(string csv)
        { string source = Path.Combine(root, "source.csv"); File.WriteAllText(source, csv, new UTF8Encoding(false)); return FPWorkbenchCsvDataset.ImportAsync(source, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile()); }
        [Test] public async Task FullRangeCountsIncludeLastEdgeAndExposeExcludedValues()
        {
            var csv = new StringBuilder("value\n"); for (int i = 0; i <= 1000; i++) csv.Append(i).Append('\n');
            csv.Append("-1\n1001\n\"\"\n"); var data = await Import(csv.ToString());
            var program = FPWorkbenchFormulaProgram.Compile(data, new FPWorkbenchFormulaSet { DatasetId = data.DatasetId });
            var result = await FPWorkbenchHistogram.CalculateAsync(data, program, 0, false, 0, data.RowCount, 0, 1000, 10, FPWorkbenchInvalidPolicy.Skip);
            Assert.That(result.IncludedCount, Is.EqualTo(1001)); Assert.That(result.InvalidCount, Is.EqualTo(1));
            Assert.That(result.UnderflowCount, Is.EqualTo(1)); Assert.That(result.OverflowCount, Is.EqualTo(1));
            for (int i = 0; i < 9; i++) Assert.That(result.Counts[i], Is.EqualTo(100));
            Assert.That(result.Counts[9], Is.EqualTo(101));
            double integral = 0; for (int i = 0; i < 10; i++) integral += result.Density(i) * (result.Edges[i + 1] - result.Edges[i]);
            Assert.That(integral, Is.EqualTo(1).Within(1e-14));
            try { await FPWorkbenchHistogram.CalculateAsync(data, program, 0, false, 0, data.RowCount, 0, 1000, 10); Assert.Fail("Expected explicit invalid-input rejection."); }
            catch (InvalidDataException) { }
        }
        [Test] public async Task DerivedCorrectionRangeAndEmptyResultsAreRevisionQualified()
        {
            var data = await Import("value\n0\n1\n2\n3\n"); var set = new FPWorkbenchFormulaSet { DatasetId = data.DatasetId };
            set.Columns.Add(new FPWorkbenchDerivedColumn { Expression = "C1 * 2" }); var program = FPWorkbenchFormulaProgram.Compile(data, set);
            var changes = new FPWorkbenchChangeSet { DatasetId = data.DatasetId }; changes.Set(data.Cell(2, 0), "2", "1", FPWorkbenchEditKind.Number, "fixture");
            var result = await FPWorkbenchHistogram.CalculateAsync(data, program, 0, true, 1, 3, 0, 4, 2, changes: changes.Snapshot());
            Assert.That(result.Counts[0], Is.Zero); Assert.That(result.Counts[1], Is.EqualTo(2));
            Assert.That(result.CorrectionFingerprint, Is.EqualTo(changes.Snapshot().Fingerprint)); Assert.That(result.FormulaFingerprint, Is.EqualTo(program.Fingerprint));
            var empty = await FPWorkbenchHistogram.CalculateAsync(data, program, 0, false, 4, 4, 0, 4, 2);
            Assert.That(empty.IncludedCount, Is.Zero); Assert.That(empty.Density(0), Is.Zero);
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            try { await FPWorkbenchHistogram.CalculateAsync(data, program, 0, false, 0, 4, 0, 4, 2, cancellation: cancellation.Token); Assert.Fail("Expected cancellation."); }
            catch (OperationCanceledException) { }
            Assert.Throws<ArgumentException>(() => FPWorkbenchHistogram.CalculateAsync(data, program, 0, false, 0, 4, 1e16, 1e16 + 2, 100));
        }
    }
}
