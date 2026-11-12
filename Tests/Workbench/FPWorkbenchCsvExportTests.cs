// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Tests
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;

    public sealed class FPWorkbenchCsvExportTests
    {
        private sealed class CancelProgress : IProgress<long>
        {
            private readonly CancellationTokenSource cancellation;
            public CancelProgress(CancellationTokenSource cancellation) { this.cancellation = cancellation; }
            public void Report(long value) { cancellation.Cancel(); }
        }
        [Test] public async Task MaterializedExportPreservesTextLayerOrderAndProvenance()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPExport-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try
            {
                string path = Path.Combine(root, "source.csv");
                string original = "id,x,y,note\n0001,0,2,\"a,b\"\n0002,1,4,\"a\"\"b\nc\"\n"; File.WriteAllText(path, original);
                var data = await FPWorkbenchCsvDataset.ImportAsync(path, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile());
                var definitions = new FPWorkbenchFormulaSet { DatasetId = data.DatasetId };
                definitions.Columns.Add(new FPWorkbenchDerivedColumn { Name = "Ratio", Expression = "C3 / C2" });
                var program = FPWorkbenchFormulaProgram.Compile(data, definitions);
                var corrections = new FPWorkbenchChangeSet { DatasetId = data.DatasetId };
                corrections.Set(data.Cell(1, 2), "4", "8", FPWorkbenchEditKind.Number, "fixture");
                string product = Path.Combine(root, "effective");
                var result = await FPWorkbenchCsvExport.WriteAsync(data, product, new[] { 3, 0, 4, 2 }, 0, 2, program, corrections.Snapshot(), FPWorkbenchExportInvalidPolicy.EmptyField);
                Assert.That(result.InvalidCells, Is.EqualTo(1)); Assert.That(result.CsvSha256.Length, Is.EqualTo(64));
                Assert.That(result.FormulaFingerprint, Is.EqualTo(program.Fingerprint)); Assert.That(result.CorrectionFingerprint, Is.EqualTo(corrections.Snapshot().Fingerprint));
                var exported = await FPWorkbenchCsvDataset.ImportAsync(Path.Combine(product, "data.csv"), Path.Combine(root, "export-index"), new FPWorkbenchCsvProfile());
                var page = await exported.ReadPageAsync(0, 2, 0, 4);
                Assert.That(page.Values[0], Is.EqualTo(new[] { "a,b", "0001", "", "2" }));
                Assert.That(page.Values[1], Is.EqualTo(new[] { "a\"b\nc", "0002", "8", "8" }));
                var metadata = JsonUtility.FromJson<FPWorkbenchCsvExportResult>(File.ReadAllText(Path.Combine(product, "provenance.json")));
                Assert.That(metadata.ColumnIds, Is.EqualTo(result.ColumnIds)); Assert.That(metadata.Corrections.Cells.Count, Is.EqualTo(1));
                Assert.That(metadata.Calculations.Columns[0].Expression, Is.EqualTo("C3 / C2")); Assert.That(File.ReadAllText(path), Is.EqualTo(original));
                Assert.Throws<IOException>(() => FPWorkbenchCsvExport.WriteAsync(data, product, new[] { 0 }, 0, 2));
                Assert.Throws<IOException>(() => FPWorkbenchCsvExport.WriteAsync(data, Path.Combine(data.CacheDirectory, "export"), new[] { 0 }, 0, 2));
                string rejected = Path.Combine(root, "rejected");
                try { await FPWorkbenchCsvExport.WriteAsync(data, rejected, new[] { 4 }, 0, 2, program); Assert.Fail("Invalid formula should reject export."); } catch (InvalidDataException) { }
                Assert.That(Directory.Exists(rejected), Is.False); Assert.That(Directory.GetDirectories(root, ".fp-export-*"), Is.Empty);
                string originalProduct = Path.Combine(root, "original");
                await FPWorkbenchCsvExport.WriteAsync(data, originalProduct, new[] { 2, 0 }, 1, 2);
                Assert.That(File.ReadAllText(Path.Combine(originalProduct, "data.csv")), Does.Contain("\"4\",\"0002\""));
                corrections.Set(data.Cell(0, 2), "2", "", FPWorkbenchEditKind.Masked, "masked fixture");
                var masked = await FPWorkbenchCsvExport.WriteAsync(data, Path.Combine(root, "masked"), new[] { 2 }, 0, 1, changes: corrections.Snapshot());
                Assert.That(masked.MaskedCells, Is.EqualTo(1));
                var empty = await FPWorkbenchCsvExport.WriteAsync(data, Path.Combine(root, "empty"), new[] { 0 }, 2, 2);
                Assert.That(empty.FirstRow, Is.EqualTo(empty.EndRow));
                using var midCancel = new CancellationTokenSource();
                try { await FPWorkbenchCsvExport.WriteAsync(data, Path.Combine(root, "mid-cancelled"), new[] { 0 }, 0, 2, progress: new CancelProgress(midCancel), cancellation: midCancel.Token); Assert.Fail("Expected staged cancellation."); } catch (OperationCanceledException) { }
                Assert.That(Directory.Exists(Path.Combine(root, "mid-cancelled")), Is.False);
                Assert.That(Directory.GetDirectories(root, ".fp-export-*"), Is.Empty);
                using var cancel = new CancellationTokenSource(); cancel.Cancel();
                try { await FPWorkbenchCsvExport.WriteAsync(data, Path.Combine(root, "cancelled"), new[] { 0 }, 0, 2, cancellation: cancel.Token); Assert.Fail("Expected cancellation."); } catch (OperationCanceledException) { }
                Assert.That(Directory.Exists(Path.Combine(root, "cancelled")), Is.False);
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
