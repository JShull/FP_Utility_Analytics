// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Tests
{
    using System;
    using System.IO;
    using System.Text;
    using System.Threading.Tasks;
    using NUnit.Framework;

    public sealed class FPWorkbenchDerivedSeriesTests
    {
        [Test] public async Task CalculatedXPreservesCorrectionsDuplicatesAndRejectsInvalidCoordinates()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPDerivedX-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try
            {
                string source = Path.Combine(root, "source.csv"); var csv = new StringBuilder("x,y\n");
                for (int i = 0; i < 1000; i++) csv.Append(i).Append(',').Append(i * 2).Append('\n');
                File.WriteAllText(source, csv.ToString());
                var data = await FPWorkbenchCsvDataset.ImportAsync(source, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile());
                var definitions = new FPWorkbenchFormulaSet { DatasetId = data.DatasetId };
                definitions.Columns.Add(new FPWorkbenchDerivedColumn { Name = "Scaled time", Expression = "C1 * .5" });
                definitions.Columns.Add(new FPWorkbenchDerivedColumn { Name = "Sum", Expression = "C1 + C2" });
                var program = FPWorkbenchFormulaProgram.Compile(data, definitions);
                var binding = new FPWorkbenchSeriesBinding { XMode = FPWorkbenchXMode.Numeric, XDerivedId = program.ColumnId(0), YDerivedId = program.ColumnId(1) };
                var changes = new FPWorkbenchChangeSet { DatasetId = data.DatasetId };
                changes.Set(data.Cell(500, 0), "500", "500.25", FPWorkbenchEditKind.Number, "time correction");
                changes.Set(data.Cell(500, 1), "1000", "4000", FPWorkbenchEditKind.Number, "spike");
                var cache = await FPWorkbenchSeriesCache.BuildAsync(data, binding, changes: changes.Snapshot(), calculations: program);
                Assert.That(cache.XName, Is.EqualTo("Scaled time"));
                Assert.That((await cache.QueryAsync(200, 300, 100)).Maximum, Is.EqualTo(4500.25));
                var overlay = await FPWorkbenchOverlay.BuildAsync(data, binding, new[] { 1 }, changes.Snapshot(), calculations: program);
                binding.XDerivedId = "";
                Assert.That((await overlay.QueryAsync(200, 300, 100)).Series[0].FormulaFingerprint, Is.EqualTo(program.Fingerprint));
                binding.XDerivedId = program.ColumnId(0);
                definitions.Columns[0].Expression = "C1 * 2";
                var next = FPWorkbenchFormulaProgram.Compile(data, definitions);
                var revised = await FPWorkbenchSeriesCache.BuildAsync(data, binding, changes: changes.Snapshot(), calculations: next);
                Assert.That(revised.DirectoryPath, Is.Not.EqualTo(cache.DirectoryPath));
                Assert.That((await revised.QueryAsync(200, 300, 100)).Maximum, Is.EqualTo(450));
                definitions.Columns[0].Expression = "0";
                var duplicates = await FPWorkbenchSeriesCache.BuildAsync(data, binding, calculations: FPWorkbenchFormulaProgram.Compile(data, definitions));
                long count = 0; foreach (var bin in (await duplicates.QueryAsync(0, 1, 100)).Bins) count += bin.Count;
                Assert.That(count, Is.EqualTo(1000), "Equal coordinates must retain every sample.");
                foreach (string expression in new[] { "-C1", "1 / (C1 - C1)", "C1 * 1e308" })
                {
                    definitions.Columns[0].Expression = expression;
                    try { await FPWorkbenchSeriesCache.BuildAsync(data, binding, calculations: FPWorkbenchFormulaProgram.Compile(data, definitions)); Assert.Fail("Expected invalid X rejection: " + expression); }
                    catch (InvalidDataException) { }
                }
                binding.XMode = FPWorkbenchXMode.Row;
                Assert.Throws<ArgumentException>(() => binding.Copy(2));
                Assert.That(File.ReadAllText(source), Is.EqualTo(csv.ToString()));
            }
            finally { Directory.Delete(root, true); }
        }

        [Test] public async Task DerivedCacheUsesStableIdentityOriginalInputsAndRevisionKeys()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPDerivedSeries-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try
            {
                string source = Path.Combine(root, "source.csv"); var csv = new StringBuilder("x,y\n"); for (int i = 0; i < 2000; i++) csv.Append(i).Append(',').Append(i * 2).Append('\n'); File.WriteAllText(source, csv.ToString());
                var data = await FPWorkbenchCsvDataset.ImportAsync(source, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile());
                var definitions = new FPWorkbenchFormulaSet { DatasetId = data.DatasetId }; definitions.Columns.Add(new FPWorkbenchDerivedColumn { Name = "Sum", Expression = "C1 + C2" });
                var program = FPWorkbenchFormulaProgram.Compile(data, definitions); var binding = new FPWorkbenchSeriesBinding { XMode = FPWorkbenchXMode.Numeric, XColumn = 0, YColumn = 0, YDerivedId = program.ColumnId(0) };
                var changes = new FPWorkbenchChangeSet { DatasetId = data.DatasetId };
                changes.Set(data.Cell(500, 0), "500", "500.5", FPWorkbenchEditKind.Number, "corrected X used by formula too");
                changes.Set(data.Cell(500, 1), "1000", "20000", FPWorkbenchEditKind.Number, "spike");
                var cache = await FPWorkbenchSeriesCache.BuildAsync(data, binding, changes: changes.Snapshot(), calculations: program);
                var view = await cache.QueryAsync(0, 1999, 100);
                Assert.That(view.Maximum, Is.EqualTo(20500.5)); Assert.That(cache.YName, Is.EqualTo("Sum"));
                Assert.That(view.FormulaFingerprint, Is.EqualTo(program.Fingerprint)); Assert.That(view.CorrectionFingerprint, Is.EqualTo(changes.Snapshot().Fingerprint));
                var reopened = await FPWorkbenchSeriesCache.BuildAsync(data, binding, changes: changes.Snapshot(), calculations: program);
                Assert.That(reopened.DirectoryPath, Is.EqualTo(cache.DirectoryPath)); Assert.That(reopened.YName, Is.EqualTo("Sum"));
                var edited = program.Definition; edited.Columns[0].Expression = "C2 - C1"; var next = FPWorkbenchFormulaProgram.Compile(data, edited);
                var nextCache = await FPWorkbenchSeriesCache.BuildAsync(data, binding, changes: changes.Snapshot(), calculations: next);
                Assert.That(nextCache.DirectoryPath, Is.Not.EqualTo(cache.DirectoryPath)); Assert.That((await nextCache.QueryAsync(0, 1999, 100)).Maximum, Is.EqualTo(19499.5));
                var physical = await FPWorkbenchSeriesCache.BuildAsync(data, new FPWorkbenchSeriesBinding { YColumn = 1 });
                var physicalWithDefinitions = await FPWorkbenchSeriesCache.BuildAsync(data, new FPWorkbenchSeriesBinding { YColumn = 1 }, calculations: next);
                Assert.That(physical.DirectoryPath, Is.EqualTo(physicalWithDefinitions.DirectoryPath), "Unrelated formulas do not invalidate a physical Y cache.");
                definitions.Columns[0].Expression = "C2 / C1"; var ratio = FPWorkbenchFormulaProgram.Compile(data, definitions);
                var ratioCache = await FPWorkbenchSeriesCache.BuildAsync(data, binding, calculations: ratio); var ratioView = await ratioCache.QueryAsync(0, 1999, 100);
                long invalid = 0; foreach (var bin in ratioView.Bins) invalid += bin.InvalidCount;
                Assert.That(invalid, Is.EqualTo(1)); Assert.That(ratioView.Minimum, Is.EqualTo(2));
                Assert.Throws<ArgumentException>(() => FPWorkbenchSeriesCache.BuildAsync(data, binding));
                Assert.That(File.ReadAllText(source), Is.EqualTo(csv.ToString()));
            }
            finally { Directory.Delete(root, true); }
        }
    }
}

