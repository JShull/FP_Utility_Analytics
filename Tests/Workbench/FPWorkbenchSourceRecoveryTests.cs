// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Tests
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;
    public sealed class FPWorkbenchSourceRecoveryTests
    {
        [Test] public async Task MovedOriginalRebuildsIndexAndRetainsWorkbookIdentityEditsAndFigures()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPRecover-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try {
                string raw = Path.Combine(root, "original.csv"), moved = Path.Combine(root, "moved.csv"), path = Path.Combine(root, "original.fpworkbook");
                File.WriteAllText(raw, "x,y\n0,5\n1,8\n"); byte[] original = File.ReadAllBytes(raw);
                var data = await FPWorkbenchCsvDataset.ImportAsync(raw, Path.Combine(root, "old-cache"), new FPWorkbenchCsvProfile());
                var doc = new FPWorkbenchDocument(); doc.Sheets.Add(new FPWorkbenchSheetDocument { DatasetId = data.DatasetId, SourcePath = raw, CacheDirectory = data.CacheDirectory,
                    FirstRow = 1, Binding = new FPWorkbenchSeriesBinding { XMode = FPWorkbenchXMode.Numeric, YColumn = 1 } });
                doc.Sheets[0].Plot.StartX = .2; doc.Sheets[0].Plot.EndX = .7;
                var changes = new FPWorkbenchChangeSet { DatasetId = data.DatasetId }; changes.Set(data.Cell(0, 1), "5", "6", FPWorkbenchEditKind.Number, "test"); doc.Changes.Add(changes);
                doc.Figures.Add(new FPWorkbenchFigureDocument { DatasetId = data.DatasetId, YColumns = new[] { 1 }, Colors = new[] { Color.cyan },
                    HasCorrectionSnapshot = true, CorrectionSnapshot = JsonUtility.FromJson<FPWorkbenchChangeSet>(JsonUtility.ToJson(changes)) });
                await FPWorkbenchDocumentFile.SaveAsync(doc, path); byte[] saved = File.ReadAllBytes(path);
                File.Move(raw, moved); File.SetLastWriteTimeUtc(moved, DateTime.UtcNow.AddMinutes(-5)); Directory.Delete(data.CacheDirectory, true);
                try { await FPWorkbenchDocumentFile.LoadAsync(path); Assert.Fail("Ordinary admission must still reject missing caches."); } catch (DirectoryNotFoundException) { }
                var metadata = await FPWorkbenchDocumentFile.ReadMetadataAsync(path);
                var repaired = await FPWorkbenchSourceRecovery.RelinkAsync(metadata, data.DatasetId, moved, Path.Combine(root, "new-cache"));
                Assert.That(metadata.Sources[0].SourcePath, Is.EqualTo(raw)); Assert.That(repaired.Sources[0].SourcePath, Is.EqualTo(moved));
                Assert.That(repaired.Id, Is.EqualTo(doc.Id)); Assert.That(repaired.Sheets[0].Id, Is.EqualTo(doc.Sheets[0].Id));
                Assert.That(repaired.Sheets[0].Plot.StartX, Is.EqualTo(.2)); Assert.That(repaired.Sheets[0].FirstRow, Is.EqualTo(1));
                string output = Path.Combine(root, "recovered.fpworkbook"); await FPWorkbenchDocumentFile.SaveAsync(repaired, output);
                var loaded = await FPWorkbenchDocumentFile.LoadAsync(output);
                Assert.That(loaded.Datasets[0].DatasetId, Is.EqualTo(data.DatasetId)); Assert.That(loaded.Document.Figures[0].Id, Is.EqualTo(doc.Figures[0].Id));
                Assert.That(JsonUtility.ToJson(loaded.Document.Changes[0]), Is.EqualTo(JsonUtility.ToJson(changes)));
                Assert.That(File.ReadAllBytes(moved), Is.EqualTo(original)); Assert.That(File.ReadAllBytes(path), Is.EqualTo(saved));
            } finally { Directory.Delete(root, true); }
        }
        [Test] public async Task ChangedRecordingAndCancellationDoNotPublishRelinkOrLeaveFailedCaches()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPRejectRecover-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try {
                string raw = Path.Combine(root, "original.csv"), wrong = Path.Combine(root, "wrong.csv"), parent = Path.Combine(root, "new-cache");
                File.WriteAllText(raw, "y\n5\n"); File.WriteAllText(wrong, "y\n6\n");
                var data = await FPWorkbenchCsvDataset.ImportAsync(raw, Path.Combine(root, "old-cache"), new FPWorkbenchCsvProfile());
                var doc = new FPWorkbenchDocument(); doc.Sources.Add(new FPWorkbenchSourceDocument { DatasetId = data.DatasetId, SourcePath = raw, CacheDirectory = data.CacheDirectory });
                try { await FPWorkbenchSourceRecovery.RelinkAsync(doc, data.DatasetId, wrong, parent); Assert.Fail("Changed data must reject."); } catch (InvalidDataException) { }
                Assert.That(doc.Sources[0].SourcePath, Is.EqualTo(raw)); Assert.That(Directory.GetDirectories(parent), Is.Empty);
                using var cancel = new CancellationTokenSource(); cancel.Cancel();
                try { await FPWorkbenchSourceRecovery.RelinkAsync(doc, data.DatasetId, raw, parent, cancel.Token); Assert.Fail("Cancelled recovery must reject."); } catch (OperationCanceledException) { }
                Assert.That(Directory.GetDirectories(parent), Is.Empty);
            } finally { Directory.Delete(root, true); }
        }
    }
}
