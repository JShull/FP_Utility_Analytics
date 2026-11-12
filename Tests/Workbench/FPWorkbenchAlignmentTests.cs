// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Tests
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;

    public sealed class FPWorkbenchAlignmentTests
    {
        private string root;
        [SetUp] public void Setup() { root = Path.Combine(Path.GetTempPath(), "FPAlign-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); }
        [TearDown] public void Cleanup() { Directory.Delete(root, true); }
        private Task<FPWorkbenchCsvDataset> Import(string name, string values)
        {
            string path = Path.Combine(root, name + ".csv"); File.WriteAllText(path, values);
            return FPWorkbenchCsvDataset.ImportAsync(path, Path.Combine(root, name + "-cache"), new FPWorkbenchCsvProfile());
        }
        [Test] public async Task ExactDecimalMergePreservesLargeKeysAmbiguityCoverageAndRevisions()
        {
            var left = await Import("left", "key,value\n1,10\n2,20\n2,21\n4,40\n9007199254740993,50\n");
            var right = await Import("right", "key,value\n0,0\n2,20\n2,21\n3,30\n4.000,40\n9007199254740992,50\n9007199254740993,60\n");
            var options = new FPWorkbenchAlignmentOptions { KeyDomain = "declared observation ID", KeyKind = FPWorkbenchAlignmentKeyKind.Decimal, Duplicates = FPWorkbenchDuplicateKeyPolicy.ReportAmbiguous };
            var work = FPWorkbenchAlignment.BuildAsync(left, right, options); options.Mode = FPWorkbenchAlignmentMode.SharedSourceRows; var alignment = await work;
            var meta = alignment.Metadata; Assert.That(meta.MatchedPairs, Is.EqualTo(2)); Assert.That(meta.UnmatchedLeftRows, Is.EqualTo(1)); Assert.That(meta.UnmatchedRightRows, Is.EqualTo(3));
            Assert.That(meta.AmbiguousLeftRows, Is.EqualTo(2)); Assert.That(meta.AmbiguousRightRows, Is.EqualTo(2));
            var pairs = await alignment.ReadPageAsync(0); Assert.That(pairs.Length, Is.EqualTo(2));
            Assert.That(pairs[0].LeftRow, Is.EqualTo(3)); Assert.That(pairs[0].RightRow, Is.EqualTo(4));
            Assert.That(pairs[1].LeftRow, Is.EqualTo(4)); Assert.That(pairs[1].RightRow, Is.EqualTo(6), "Keys above 2^53 must not alias their adjacent integer.");
            meta.MatchedPairs = 999; Assert.That(alignment.Metadata.MatchedPairs, Is.EqualTo(2));
            options.Mode = FPWorkbenchAlignmentMode.ExactKey; var reopened = await FPWorkbenchAlignment.BuildAsync(left, right, options); Assert.That(reopened.DirectoryPath, Is.EqualTo(alignment.DirectoryPath));
            var changes = new FPWorkbenchChangeSet { DatasetId = left.DatasetId }; changes.Set(left.Cell(0, 0), "1", "0", FPWorkbenchEditKind.Number, "fixture key correction");
            var effective = await FPWorkbenchAlignment.BuildAsync(left, right, options, changes.Snapshot());
            Assert.That(effective.Metadata.MatchedPairs, Is.EqualTo(3)); Assert.That(effective.DirectoryPath, Is.Not.EqualTo(alignment.DirectoryPath));
            Assert.That(effective.Metadata.HasLeftCorrectionSnapshot, Is.True); Assert.That(effective.Metadata.LeftCorrections.Cells.Count, Is.EqualTo(1));
            Assert.That(effective.Metadata.LeftCorrectionFingerprint, Is.EqualTo(changes.Snapshot().Fingerprint));
            options.Duplicates = FPWorkbenchDuplicateKeyPolicy.Reject;
            try { await FPWorkbenchAlignment.BuildAsync(left, right, options); Assert.Fail("Expected duplicate rejection."); } catch (InvalidDataException) { }
            Assert.That(Directory.GetDirectories(left.CacheDirectory, ".alignment-*"), Is.Empty);
            options.Duplicates = FPWorkbenchDuplicateKeyPolicy.ReportAmbiguous;
            using (var file = new FileStream(Path.Combine(alignment.DirectoryPath, "matches.bin"), FileMode.Open, FileAccess.Write)) file.WriteByte(255);
            try { await FPWorkbenchAlignment.BuildAsync(left, right, options); Assert.Fail("Expected checksum rejection."); } catch (InvalidDataException) { }
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            try { await effective.ReadPageAsync(0, cancellation: cancel.Token); Assert.Fail("Expected cancellation."); } catch (OperationCanceledException) { }
            options.LeftUnits = "m"; options.RightUnits = "cm"; Assert.Throws<ArgumentException>(() => FPWorkbenchAlignment.BuildAsync(left, right, options));
        }
        [Test] public async Task SharedRowsRequireVerifiedIdentityAndNeverInferPositionalPairing()
        {
            var source = await Import("identity", "key,value\n3,1\n1,2\n2,3\n"); var other = await Import("other", "key,value\n3,4\n1,5\n2,6\n");
            var options = new FPWorkbenchAlignmentOptions { Mode = FPWorkbenchAlignmentMode.SharedSourceRows };
            var same = await FPWorkbenchAlignment.BuildAsync(source, source, options); var pairs = await same.ReadPageAsync(0);
            Assert.That(pairs.Length, Is.EqualTo(3)); for (int i = 0; i < pairs.Length; i++) { Assert.That(pairs[i].LeftRow, Is.EqualTo(i)); Assert.That(pairs[i].RightRow, Is.EqualTo(i)); }
            Assert.Throws<ArgumentException>(() => FPWorkbenchAlignment.BuildAsync(source, other, options));
            options.Mode = FPWorkbenchAlignmentMode.ExactKey; options.KeyKind = FPWorkbenchAlignmentKeyKind.Decimal; options.KeyDomain = "declared key";
            try { await FPWorkbenchAlignment.BuildAsync(source, other, options); Assert.Fail("Expected unsorted-key rejection."); } catch (InvalidDataException) { }
            Assert.That(Directory.GetDirectories(source.CacheDirectory, ".alignment-*"), Is.Empty);
        }
        [Test] public async Task ExactTextAndDecimalPrecisionPoliciesAreExplicit()
        {
            var left = await Import("textLeft", "key\n0001\n0010\n0100\n"); var right = await Import("textRight", "key\n0001\n0002\n0100\n");
            var options = new FPWorkbenchAlignmentOptions { KeyDomain = "literal fixed-width IDs" };
            var text = await FPWorkbenchAlignment.BuildAsync(left, right, options); Assert.That(text.Metadata.MatchedPairs, Is.EqualTo(2)); Assert.That(text.Metadata.UnmatchedLeftRows, Is.EqualTo(1));
            Assert.That((await text.ReadPageAsync(1, 1))[0].LeftRow, Is.EqualTo(2)); Assert.That(await text.ReadPageAsync(2, 0), Is.Empty);
            foreach (string key in new[] { "0.123456789012345678901234567891", "1e-100", "" })
            {
                var data = await Import("precision" + Guid.NewGuid().ToString("N"), "key\n" + (key.Length == 0 ? "\"\"" : key) + "\n");
                options.KeyKind = FPWorkbenchAlignmentKeyKind.Decimal;
                try { await FPWorkbenchAlignment.BuildAsync(data, data, options); Assert.Fail("Expected inexact/missing-key rejection: " + key); } catch (InvalidDataException) { }
            }
            options.KeyDomain = ""; Assert.Throws<ArgumentException>(() => FPWorkbenchAlignment.BuildAsync(left, right, options));
        }
    }
}
