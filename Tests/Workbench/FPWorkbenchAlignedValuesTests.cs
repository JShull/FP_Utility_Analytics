// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Tests
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;

    public sealed class FPWorkbenchAlignedValuesTests
    {
        private string root;
        [SetUp] public void Setup() { root = Path.Combine(Path.GetTempPath(), "FPAlignedValues-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); }
        [TearDown] public void Cleanup() { Directory.Delete(root, true); }
        private Task<FPWorkbenchCsvDataset> Import(string name, string csv)
        { string path = Path.Combine(root, name + ".csv"); File.WriteAllText(path, csv); return FPWorkbenchCsvDataset.ImportAsync(path, Path.Combine(root, name + "-cache"), new FPWorkbenchCsvProfile()); }
        private static FPWorkbenchAlignmentOptions Options() => new FPWorkbenchAlignmentOptions { KeyDomain = "Observation ID", KeyKind = FPWorkbenchAlignmentKeyKind.Decimal };
        [Test] public async Task AlignedValuesRetainBothRowsFrozenCorrectionsAndCalculatedCoordinates()
        {
            string leftText = "key,x\n1,10\n3,30\n5,50\n", rightText = "key,y\n0,100\n3,300\n5,500\n";
            var left = await Import("left", leftText); var right = await Import("right", rightText);
            var edits = new FPWorkbenchChangeSet { DatasetId = right.DatasetId }; edits.Set(right.Cell(1, 1), "300", "900", FPWorkbenchEditKind.Number, "value fixture");
            var alignment = await FPWorkbenchAlignment.BuildAsync(left, right, Options(), rightChanges: edits.Snapshot());
            var definitions = new FPWorkbenchFormulaSet { DatasetId = left.DatasetId }; definitions.Columns.Add(new FPWorkbenchDerivedColumn { Name = "Double X", Expression = "C2 * 2" });
            var program = FPWorkbenchFormulaProgram.Compile(left, definitions);
            var values = await FPWorkbenchAlignedValues.BuildAsync(alignment, 2, 1, program);
            var page = await values.Dataset.ReadPageAsync(0, 2, 0, 4);
            Assert.That(page.Values[0], Is.EqualTo(new[] { "1", "1", "60", "900" })); Assert.That(page.Values[1], Is.EqualTo(new[] { "2", "2", "100", "500" }));
            Assert.That(values.Metadata.LeftSourceColumns, Is.EqualTo(new[] { 0, 1 })); Assert.That(values.Metadata.RightSourceColumns, Is.EqualTo(new[] { 0, 1 }));
            Assert.That(values.Metadata.LeftFormulaFingerprint, Is.EqualTo(program.Fingerprint)); Assert.That(values.Metadata.Alignment.RightCorrectionFingerprint, Is.EqualTo(edits.Snapshot().Fingerprint));
            Assert.That(values.Dataset.SourcePath, Does.Not.Contain(".values-"));
            var reopened = await FPWorkbenchAlignedValues.BuildAsync(alignment, 2, 1, program); Assert.That(reopened.Dataset.CacheDirectory, Is.EqualTo(values.Dataset.CacheDirectory));
            Assert.That(File.ReadAllText(left.SourcePath), Is.EqualTo(leftText)); Assert.That(File.ReadAllText(right.SourcePath), Is.EqualTo(rightText));
            string product = Path.GetDirectoryName(values.Dataset.SourcePath); File.AppendAllText(Path.Combine(product, "metadata.json"), " ");
            try { await FPWorkbenchAlignedValues.BuildAsync(alignment, 2, 1, program); Assert.Fail("Expected provenance checksum rejection."); } catch (InvalidDataException) { }
            using (var file = new FileStream(Path.Combine(alignment.DirectoryPath, "matches.bin"), FileMode.Open, FileAccess.Write)) file.WriteByte(0);
            try { await FPWorkbenchAlignedValues.BuildAsync(alignment, 1, 1); Assert.Fail("A valid-looking row remap must fail its mapping checksum."); } catch (InvalidDataException) { }
        }
        [Test] public async Task InvalidAndEmptyMappingsRemainExplicitAndCancellable()
        {
            var left = await Import("left", "key,x\n0,bad\n1,2\n"); var right = await Import("right", "key,y\n0,4\n1,5\n");
            var mapping = await FPWorkbenchAlignment.BuildAsync(left, right, Options());
            try { await FPWorkbenchAlignedValues.BuildAsync(mapping, 1, 1); Assert.Fail("Expected invalid-value rejection."); } catch (InvalidDataException) { }
            Assert.That(Directory.GetDirectories(left.CacheDirectory, ".av-*"), Is.Empty);
            var values = await FPWorkbenchAlignedValues.BuildAsync(mapping, 1, 1, invalidPolicy: FPWorkbenchExportInvalidPolicy.EmptyField);
            Assert.That(values.Metadata.InvalidX, Is.EqualTo(1)); Assert.That(values.Dataset.RowCount, Is.EqualTo(2));
            var points = await FPWorkbenchPointCache.BuildAsync(values.Dataset, new[] { 2, 3 }, invalidPolicy: FPWorkbenchInvalidPolicy.Skip);
            Assert.That(points.ValidCount, Is.EqualTo(1)); Assert.That(points.InvalidCount, Is.EqualTo(1)); Assert.That((await points.QueryAsync(points.Bounds)).Points[0].Row, Is.EqualTo(1));
            var reopened = await FPWorkbenchAlignedValues.BuildAsync(mapping, 1, 1, invalidPolicy: FPWorkbenchExportInvalidPolicy.EmptyField); Assert.That(reopened.Metadata.InvalidX, Is.EqualTo(1));
            var absent = await Import("absent", "key,y\n2,5\n"); var noMatches = await FPWorkbenchAlignment.BuildAsync(left, absent, Options());
            var empty = await FPWorkbenchAlignedValues.BuildAsync(noMatches, 1, 1); Assert.That(empty.Dataset.RowCount, Is.Zero);
            var schema = new FPWorkbenchTableView(empty.Dataset); Assert.That(schema.ColumnId(2), Is.Not.Empty);
            Assert.That((await FPWorkbenchPointCache.BuildAsync(empty.Dataset, new[] { 2, 3 })).HasPoints, Is.False);
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            try { await FPWorkbenchAlignedValues.BuildAsync(mapping, 1, 1, cancellation: cancel.Token); Assert.Fail("Expected cancellation."); } catch (OperationCanceledException) { }
        }
    }
}
