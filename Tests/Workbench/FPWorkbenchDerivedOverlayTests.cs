// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Tests
{
    using System;
    using System.IO;
    using System.Threading.Tasks;
    using NUnit.Framework;

    public sealed class FPWorkbenchDerivedOverlayTests
    {
        [Test] public async Task MixedSeriesPublishMatchingFormulaAndCorrectionRevisions()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPDerivedOverlay-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try
            {
                string source = Path.Combine(root, "source.csv"); File.WriteAllText(source, "x,y\n0,0\n1,2\n2,4\n");
                var data = await FPWorkbenchCsvDataset.ImportAsync(source, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile());
                var definitions = new FPWorkbenchFormulaSet { DatasetId = data.DatasetId };
                definitions.Columns.Add(new FPWorkbenchDerivedColumn { Name = "Sum", Expression = "C1 + C2" }); definitions.Columns.Add(new FPWorkbenchDerivedColumn { Name = "Ratio", Expression = "C2 / C1" });
                var program = FPWorkbenchFormulaProgram.Compile(data, definitions); var common = new FPWorkbenchSeriesBinding { XMode = FPWorkbenchXMode.Numeric, XColumn = 0 };
                string sum = program.ColumnId(0), ratio = program.ColumnId(1); string[] ids = { "", sum, ratio };
                var changes = new FPWorkbenchChangeSet { DatasetId = data.DatasetId }; changes.Set(data.Cell(2, 1), "4", "20", FPWorkbenchEditKind.Number, "fixture");
                var overlay = await FPWorkbenchOverlay.BuildAsync(data, common, new[] { 1, 0, 0 }, changes.Snapshot(), calculations: program, yDerivedIds: ids);
                ids[1] = "mutated caller input"; var view = await overlay.QueryAsync(0, 2, 100);
                Assert.That(view.Series.Count, Is.EqualTo(3)); Assert.That(view.Maximum, Is.EqualTo(22)); Assert.That(view.YDerivedIds[1], Is.EqualTo(sum));
                Assert.That(view.FormulaFingerprint, Is.EqualTo(program.Fingerprint)); Assert.That(view.Series[0].FormulaFingerprint, Is.EqualTo("none"));
                Assert.That(view.Series[1].FormulaFingerprint, Is.EqualTo(program.Fingerprint)); Assert.That(view.Series[2].Minimum, Is.EqualTo(2));
                long invalid = 0; foreach (var bin in view.Series[2].Bins) invalid += bin.InvalidCount; Assert.That(invalid, Is.EqualTo(1));
                var reordered = program.Definition; reordered.Columns.Reverse(); var other = FPWorkbenchFormulaProgram.Compile(data, reordered);
                var second = await FPWorkbenchOverlay.BuildAsync(data, common, new[] { 1, 0, 0 }, changes.Snapshot(), calculations: other, yDerivedIds: new[] { "", sum, ratio });
                Assert.That((await second.QueryAsync(0, 2, 100)).Series[1].Maximum, Is.EqualTo(22));
                try { await FPWorkbenchOverlay.BuildAsync(data, common, new[] { 0, 0 }, calculations: program, yDerivedIds: new[] { sum, sum }); Assert.Fail("Expected duplicate derived-ID rejection."); } catch (ArgumentException) { }
                try { await FPWorkbenchOverlay.BuildAsync(data, common, new[] { 0 }, calculations: program, yDerivedIds: new[] { "missing" }); Assert.Fail("Expected missing calculated reference rejection."); } catch (ArgumentException) { }
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
