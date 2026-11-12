// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Tests
{
    using System;
    using System.IO;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;
    public sealed class FPWorkbenchColumnProjectionTests
    {
        [Test] public void ProjectionRetainsStableIdentitiesAcrossDefinitionReorderAndRejectsInvalidMetadata()
        {
            var widths = new FPWorkbenchColumnLayout(); widths.EnsureColumns(6); widths.ManualWidths[5] = 300;
            Assert.That(widths.Fit(500, 0, order: new[] { 5, 0 }), Is.EqualTo(new float[] { 300, 200 }));
            Assert.That(widths.PreviousFirst(1, 350, order: new[] { 5, 0 }), Is.EqualTo(0));
            Assert.Throws<ArgumentException>(() => widths.Fit(500, 0, order: new[] { 6 }));
            var p = new FPWorkbenchColumnProjection(); p.BindSchema(new[] { "physical0", "physical1", "derivedA", "derivedB" });
            p.Move("derivedB", 0); p.SetHidden("physical1", true);
            Assert.That(p.Resolve(new[] { "physical0", "physical1", "derivedA", "derivedB" }), Is.EqualTo(new[] { 3, 0, 2 }));
            var restored = JsonUtility.FromJson<FPWorkbenchColumnProjection>(JsonUtility.ToJson(p));
            restored.BindSchema(new[] { "physical0", "physical1", "derivedB", "derivedA", "derivedC" });
            Assert.That(restored.Resolve(new[] { "physical0", "physical1", "derivedB", "derivedA", "derivedC" }), Is.EqualTo(new[] { 2, 0, 3, 4 }));
            restored.BindSchema(new[] { "physical0", "derivedA" });
            Assert.That(restored.Resolve(new[] { "physical0", "derivedA" }), Is.EqualTo(new[] { 0, 1 }));
            restored.SetHidden("physical0", true); Assert.Throws<InvalidOperationException>(() => restored.SetHidden("derivedA", true));
            Assert.That(restored.Hidden, Is.EqualTo(new[] { "physical0" }));
            Assert.Throws<InvalidDataException>(() => new FPWorkbenchColumnProjection { Order = new[] { "a", "a" } }.Validate());
            Assert.Throws<InvalidDataException>(() => new FPWorkbenchColumnProjection { Order = new[] { "a" }, Hidden = new[] { "foreign" } }.Validate());
            Assert.Throws<InvalidDataException>(() => restored.Resolve(new[] { "physical0", "foreign" }));
        }
        [Test] public async Task ProjectedPageUsesOneSourceSchemaAndRetainsCalculatedCorrectionAndCellIdentity()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPProjection-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try {
                string path = Path.Combine(root, "input.csv"); File.WriteAllText(path, "a,b,c\n1,2,3\n4,5,6\n"); byte[] original = File.ReadAllBytes(path);
                var data = await FPWorkbenchCsvDataset.ImportAsync(path, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile());
                var definitions = new FPWorkbenchFormulaSet { DatasetId = data.DatasetId };
                definitions.Columns.Add(new FPWorkbenchDerivedColumn { Name = "Sum", Expression = "C1 + C3" });
                var program = FPWorkbenchFormulaProgram.Compile(data, definitions); var table = new FPWorkbenchTableView(data, program);
                var changes = new FPWorkbenchChangeSet { DatasetId = data.DatasetId }; changes.Set(data.Cell(0, 2), "3", "9", FPWorkbenchEditKind.Number, "fixture");
                var selected = new[] { 3, 2, 0 }; var task = table.ReadProjectedPageAsync(0, 2, selected, changes.Snapshot()); selected[0] = 1;
                var page = await task;
                Assert.That(page.Columns, Is.EqualTo(new[] { 3, 2, 0 })); Assert.That(page.Values[0][0].Calculated, Is.True);
                Assert.That(page.Values[0][0].Text, Is.EqualTo("10")); Assert.That(page.Values[0][1].Text, Is.EqualTo("9"));
                Assert.That(page.Values[0][1].Original, Is.EqualTo("3")); Assert.That(page.Values[1][0].Text, Is.EqualTo("10"));
                Assert.That(table.SourceCell(page.FirstRow, page.Columns[1]), Is.EqualTo(data.Cell(0, 2)));
                Assert.Throws<ArgumentException>(() => table.ReadProjectedPageAsync(0, 1, new[] { 1, 1 }));
                Assert.Throws<ArgumentOutOfRangeException>(() => table.ReadProjectedPageAsync(0, 1, new[] { 4 }));
                Assert.Throws<ArgumentOutOfRangeException>(() => table.ReadProjectedPageAsync(0, 1, new int[33]));
                var contiguous = await table.ReadPageAsync(0, 1, 1, 2);
                Assert.That(contiguous.Columns, Is.EqualTo(new[] { 1, 2 })); Assert.That(contiguous.Values[0][0].Text, Is.EqualTo("2"));
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(original));
            } finally { Directory.Delete(root, true); }
        }
    }
}
