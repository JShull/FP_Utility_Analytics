// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Tests
{
    using System;
    using System.IO;
    using System.Threading.Tasks;
    using NUnit.Framework;
    public sealed class FPWorkbenchUtcAlignmentTests
    {
        private string root;
        [SetUp] public void Setup() { root = Path.Combine(Path.GetTempPath(), "FPUtcAlign-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); }
        [TearDown] public void Cleanup() { Directory.Delete(root, true); }
        private Task<FPWorkbenchCsvDataset> Import(string name, string rows)
        {
            string path = Path.Combine(root, name + ".csv"); File.WriteAllText(path, "date,time,value\n" + rows);
            return FPWorkbenchCsvDataset.ImportAsync(path, Path.Combine(root, name + "-cache"), new FPWorkbenchCsvProfile());
        }
        private static FPWorkbenchAlignmentOptions Options() => new FPWorkbenchAlignmentOptions {
            KeyKind = FPWorkbenchAlignmentKeyKind.UtcDateTime, KeyDomain = "Declared UTC fixture clock",
            LeftKeyColumn = 1, RightKeyColumn = 1, LeftDateColumn = 0, RightDateColumn = 0,
            LeftUtcFormat = "dd.MM.yyyy HH:mm:ss.fffffff", RightUtcFormat = "yyyy-MM-dd HH:mm:ss.fffffff"
        };
        [Test] public async Task ExactUtcTicksCrossMidnightAndFormatsWithoutAliasingAdjacentInstants()
        {
            var left = await Import("left", "31.12.2025,23:59:59.9999999,10\n01.01.2026,00:00:00.0000000,20\n01.01.2026,00:00:00.0000001,30\n");
            var right = await Import("right", "2025-12-31,23:59:59.9999998,100\n2025-12-31,23:59:59.9999999,110\n2026-01-01,00:00:00.0000001,130\n");
            var options = Options(); var alignment = await FPWorkbenchAlignment.BuildAsync(left, right, options);
            var pairs = await alignment.ReadPageAsync(0); Assert.That(pairs.Length, Is.EqualTo(2));
            Assert.That(pairs[0].LeftRow, Is.EqualTo(0)); Assert.That(pairs[0].RightRow, Is.EqualTo(1));
            Assert.That(pairs[1].LeftRow, Is.EqualTo(2)); Assert.That(pairs[1].RightRow, Is.EqualTo(2));
            Assert.That(alignment.Metadata.UnmatchedLeftRows, Is.EqualTo(1)); Assert.That(alignment.Metadata.UnmatchedRightRows, Is.EqualTo(1));
            Assert.That((await FPWorkbenchAlignment.BuildAsync(left, right, options)).DirectoryPath, Is.EqualTo(alignment.DirectoryPath));
            var values = await FPWorkbenchAlignedValues.BuildAsync(alignment, 2, 2);
            Assert.That(values.Metadata.LeftSourceColumns, Is.EqualTo(new[] { 0, 1, 2 })); Assert.That(values.Metadata.RightSourceColumns, Is.EqualTo(new[] { 0, 1, 2 }));
            var recipe = FPWorkbenchAlignedFigureRecipe.FromValues(values.Metadata);
            var regenerated = await recipe.BuildAsync(left, right);
            Assert.That((await regenerated.Dataset.ReadPageAsync(0, 2, 0, 4)).Values[1], Is.EqualTo(new[] { "2", "2", "30", "130" }));
            recipe.Alignment.RightDateColumn = 3; Assert.Throws<InvalidDataException>(() => recipe.ValidateSources(left, right));
            var dates = new FPWorkbenchChangeSet { DatasetId = left.DatasetId }; dates.Set(left.Cell(0, 0), "31.12.2025", "30.12.2025", FPWorkbenchEditKind.Text, "earlier date fixture");
            var corrected = await FPWorkbenchAlignment.BuildAsync(left, right, options, dates.Snapshot());
            Assert.That(corrected.Metadata.MatchedPairs, Is.EqualTo(1)); Assert.That(corrected.DirectoryPath, Is.Not.EqualTo(alignment.DirectoryPath));
        }
        [Test] public async Task DuplicateCompositeKeysAndMaskedDatesUseExplicitExistingPolicies()
        {
            var left = await Import("left", "31.12.2025,23:59:59.9999999,10\n01.01.2026,00:00:00.0000000,20\n01.01.2026,00:00:00.0000001,30\n");
            var right = await Import("right", "2025-12-31,23:59:59.9999999,110\n2026-01-01,00:00:00.0000001,130\n");
            var options = Options(); var edits = new FPWorkbenchChangeSet { DatasetId = left.DatasetId };
            edits.Set(left.Cell(1, 1), "00:00:00.0000000", "00:00:00.0000001", FPWorkbenchEditKind.Text, "duplicate fixture");
            try { await FPWorkbenchAlignment.BuildAsync(left, right, options, edits.Snapshot()); Assert.Fail("Duplicate UTC keys must reject."); } catch (InvalidDataException) { }
            options.Duplicates = FPWorkbenchDuplicateKeyPolicy.ReportAmbiguous;
            var ambiguous = await FPWorkbenchAlignment.BuildAsync(left, right, options, edits.Snapshot());
            Assert.That(ambiguous.Metadata.MatchedPairs, Is.EqualTo(1)); Assert.That(ambiguous.Metadata.AmbiguousLeftRows, Is.EqualTo(2)); Assert.That(ambiguous.Metadata.AmbiguousRightRows, Is.EqualTo(1));
            var mask = new FPWorkbenchChangeSet { DatasetId = left.DatasetId }; mask.Set(left.Cell(1, 0), "01.01.2026", "", FPWorkbenchEditKind.Masked, "masked date");
            try { await FPWorkbenchAlignment.BuildAsync(left, right, options, mask.Snapshot()); Assert.Fail("Masked UTC dates must reject."); } catch (InvalidDataException) { }
        }
        [Test] public async Task IncompleteFormatsExcessPrecisionInvalidDatesAndDecreasingTimeReject()
        {
            var right = await Import("right", "2026-01-01,00:00:00.0000000,1\n");
            var options = Options();
            foreach (string rows in new[] { "01.01.2026,00:00:00.00000001,1\n", "31.02.2026,00:00:00.0000000,1\n",
                "02.01.2026,00:00:00.0000000,1\n01.01.2026,00:00:00.0000000,2\n" }) {
                var left = await Import(Guid.NewGuid().ToString("N"), rows);
                try { await FPWorkbenchAlignment.BuildAsync(left, right, options); Assert.Fail("Invalid or decreasing UTC timestamps must reject."); } catch (InvalidDataException) { }
                Assert.That(Directory.GetDirectories(left.CacheDirectory), Is.Empty);
            }
            var source = await Import("valid", "01.01.2026,00:00:00.0000000,1\n");
            foreach (string format in new[] { "HH:mm:ss.fffffff", "dd.MM.yy HH:mm:ss.fffffff", "dd.MM.yyyy HH:mm:ss.ffffffff", "dd.MM.yyyy HH:mm:ssK" }) {
                options.LeftUtcFormat = format;
                Assert.Throws<ArgumentException>(() => FPWorkbenchAlignment.BuildAsync(source, right, options));
            }
        }
    }
}
