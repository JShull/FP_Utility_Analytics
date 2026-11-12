// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Tests
{
    using System;
    using System.IO;
    using System.Threading.Tasks;
    using NUnit.Framework;

    public sealed class FPWorkbenchTableViewTests
    {
        [Test] public async Task MixedTilePreservesExactSourceTextAndSeparatesCalculatedCells()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPVirtualTable-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try
            {
                string source = Path.Combine(root, "source.csv"); File.WriteAllText(source, "a,b\n9007199254740993,2\n3,4\n");
                var data = await FPWorkbenchCsvDataset.ImportAsync(source, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile());
                var definitions = new FPWorkbenchFormulaSet { DatasetId = data.DatasetId }; definitions.Columns.Add(new FPWorkbenchDerivedColumn { Name = "Total", Expression = "C1 + C2" });
                var program = FPWorkbenchFormulaProgram.Compile(data, definitions); var table = new FPWorkbenchTableView(data, program);
                Assert.That(table.ColumnCount, Is.EqualTo(3)); Assert.That(table.ColumnName(2), Is.EqualTo("Total")); Assert.That(table.ColumnId(2), Does.EndWith(program.ColumnId(0)));
                var tile = await table.ReadPageAsync(0, 2, 0, 3);
                Assert.That(tile.Values[0][0].Text, Is.EqualTo("9007199254740993")); Assert.That(tile.Values[0][0].Original, Is.EqualTo("9007199254740993"));
                Assert.That(tile.Values[0][2].CalculationStatus, Is.EqualTo(FPWorkbenchFormulaStatus.IntegerPrecision));
                Assert.That(tile.Values[1][2].Text, Is.EqualTo("7")); Assert.That(tile.Values[1][2].Original, Is.Null);
                Assert.Throws<InvalidOperationException>(() => table.SourceCell(1, 2)); Assert.That(table.SourceCell(1, 1), Is.EqualTo(data.Cell(1, 1)));
                var changes = new FPWorkbenchChangeSet { DatasetId = data.DatasetId }; changes.Set(data.Cell(1, 1), "4", "10", FPWorkbenchEditKind.Number, "fixture");
                var corrected = await table.ReadPageAsync(1, 1, 1, 2, changes.Snapshot());
                Assert.That(corrected.Values[0][0].Original, Is.EqualTo("4")); Assert.That(corrected.Values[0][0].Text, Is.EqualTo("10")); Assert.That(corrected.Values[0][1].Text, Is.EqualTo("13"));
                Assert.That(tile.Values[1][2].Text, Is.EqualTo("7"), "Previously published tiles retain their revision.");
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
