// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Tests
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;
    public sealed class FPWorkbenchTableClipboardTests
    {
        [Test] public async Task CopyRetainsProjectedTextCorrectionsAndCalculationsWithoutMutatingSource()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPCopy-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try {
                string path = Path.Combine(root, "source.csv"); File.WriteAllText(path, "a,b,c\n\"tab\tvalue\",\"quote\"\"value\",1\n\"line\nnext\",5,2\n");
                byte[] original = File.ReadAllBytes(path);
                var data = await FPWorkbenchCsvDataset.ImportAsync(path, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile());
                var definitions = new FPWorkbenchFormulaSet { DatasetId = data.DatasetId };
                definitions.Columns.Add(new FPWorkbenchDerivedColumn { Name = "Double", Expression = "C3 * 2" });
                var table = new FPWorkbenchTableView(data, FPWorkbenchFormulaProgram.Compile(data, definitions));
                var edits = new FPWorkbenchChangeSet { DatasetId = data.DatasetId }; edits.Set(data.Cell(0, 2), "1", "9", FPWorkbenchEditKind.Number, "copy fixture");
                var selection = new FPWorkbenchTableSelection { DatasetId = data.DatasetId, EndRow = 2, ColumnIds = new[] { table.ColumnId(3), table.ColumnId(0), table.ColumnId(1) } };
                string copy = await FPWorkbenchTableClipboard.BuildAsync(table, selection, changes: edits.Snapshot());
                Assert.That(copy, Is.EqualTo("Double\ta\tb\n18\t\"tab\tvalue\"\t\"quote\"\"value\"\n4\t\"line\nnext\"\t5\n"));
                edits.Set(data.Cell(1, 0), "line\nnext", "", FPWorkbenchEditKind.Masked, "mask fixture");
                Assert.That(await FPWorkbenchTableClipboard.BuildAsync(table, selection, false, edits.Snapshot()), Does.Contain("[masked]"));
                Assert.That(await FPWorkbenchTableClipboard.BuildAsync(table, selection, false), Does.StartWith("2\t"));
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(original));
                selection.ColumnIds[0] = "foreign"; Assert.Throws<InvalidDataException>(() => selection.Resolve(table));
            } finally { Directory.Delete(root, true); }
        }
        [Test] public async Task RowTextAndCancellationBoundsRejectWithoutReturningPartialText()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPCopyBounds-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try {
                string path = Path.Combine(root, "source.csv"); File.WriteAllText(path, "a\n" + new string('z', 600000) + "\n" + string.Concat(System.Linq.Enumerable.Repeat("1\n", 299)));
                var data = await FPWorkbenchCsvDataset.ImportAsync(path, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile()); var table = new FPWorkbenchTableView(data);
                var selection = new FPWorkbenchTableSelection { DatasetId = data.DatasetId, EndRow = 257, ColumnIds = new[] { table.ColumnId(0) } };
                try { await FPWorkbenchTableClipboard.BuildAsync(table, selection); Assert.Fail("Oversized row selection must reject."); } catch (ArgumentException) { }
                selection.EndRow = 1;
                try { await FPWorkbenchTableClipboard.BuildAsync(table, selection); Assert.Fail("Oversized text must reject."); } catch (InvalidDataException) { }
                using var cancel = new CancellationTokenSource(); cancel.Cancel();
                try { await FPWorkbenchTableClipboard.BuildAsync(table, selection, cancellation: cancel.Token); Assert.Fail("Cancelled copy must reject."); } catch (OperationCanceledException) { }
            } finally { Directory.Delete(root, true); }
        }
    }
}
