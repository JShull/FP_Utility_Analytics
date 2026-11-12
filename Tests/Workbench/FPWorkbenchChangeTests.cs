// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Tests
{
    using System;
    using System.IO;
    using System.Text;
    using System.Threading.Tasks;
    using NUnit.Framework;

    public sealed class FPWorkbenchChangeTests
    {
        private string root, source;
        [SetUp] public void Setup() { root = Path.Combine(Path.GetTempPath(), "FPChangeTest-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); source = Path.Combine(root, "source.csv"); }
        [TearDown] public void Teardown() { Directory.Delete(root, true); }
        private Task<FPWorkbenchCsvDataset> Import()
        {
            File.WriteAllText(source, "x,y\n0,1\n1,2\n2,3\n", new UTF8Encoding(false));
            return FPWorkbenchCsvDataset.ImportAsync(source, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile());
        }
        [Test] public async Task TypedCorrectionsRetainOriginalsAndSnapshotsAreImmutable()
        {
            var data = await Import(); var changes = new FPWorkbenchChangeSet { DatasetId = data.DatasetId }; var cell = data.Cell(1, 1);
            Assert.That(changes.Set(cell, "2", "9007199254740993", FPWorkbenchEditKind.Integer, "exact integer"), Is.True);
            var originalSnapshot = changes.Snapshot();
            changes.Set(cell, "2", "9", FPWorkbenchEditKind.Number, "corrected");
            Assert.That(originalSnapshot.TryGet(cell, "2", out string first, out _), Is.True); Assert.That(first, Is.EqualTo("9007199254740993"));
            var next = changes.Snapshot(); Assert.That(next.Fingerprint, Is.Not.EqualTo(originalSnapshot.Fingerprint));
            Assert.That(next.TryGet(cell, "2", out string value, out _), Is.True); Assert.That(value, Is.EqualTo("9"));
            Assert.Throws<InvalidDataException>(() => next.TryGet(cell, "changed", out _, out _));
            Assert.Throws<ArgumentException>(() => changes.Set(cell, "2", "NaN", FPWorkbenchEditKind.Number, "bad"));
            Assert.Throws<ArgumentException>(() => changes.Set(cell, "2", "9", (FPWorkbenchEditKind)999, "bad kind"));
            Assert.That(changes.Cells[0].Value, Is.EqualTo("9"));
            Assert.That((await data.ReadPageAsync(1, 1, 1, 1)).Values[0][0], Is.EqualTo("2"));
            Assert.That(changes.Revert(cell), Is.True); Assert.That(changes.Cells, Is.Empty);
        }
        [Test] public async Task EffectivePlotUsesDistinctCacheAndMaskCounts()
        {
            var data = await Import(); byte[] bytes = File.ReadAllBytes(source);
            var changes = new FPWorkbenchChangeSet { DatasetId = data.DatasetId };
            changes.Set(data.Cell(1, 1), "2", "99", FPWorkbenchEditKind.Number, "fixture");
            changes.Set(data.Cell(2, 1), "3", "", FPWorkbenchEditKind.Masked, "bad reading");
            var binding = new FPWorkbenchSeriesBinding { XMode = FPWorkbenchXMode.Numeric, XColumn = 0, YColumn = 1 };
            var raw = await FPWorkbenchSeriesCache.BuildAsync(data, binding);
            var effective = await FPWorkbenchSeriesCache.BuildAsync(data, binding, changes: changes.Snapshot());
            Assert.That(raw.DirectoryPath, Is.Not.EqualTo(effective.DirectoryPath));
            Assert.That((await raw.QueryAsync(0, 2)).Maximum, Is.EqualTo(3));
            var view = await effective.QueryAsync(0, 2);
            Assert.That(view.Maximum, Is.EqualTo(99)); Assert.That(view.Bins[2].InvalidCount, Is.EqualTo(1));
            Assert.That(File.ReadAllBytes(source), Is.EqualTo(bytes));
        }
        [Test] public async Task ChangesFileRoundTripRequiresMatchingSourceAndPreconditions()
        {
            var data = await Import(); var changes = new FPWorkbenchChangeSet { DatasetId = data.DatasetId };
            changes.Set(data.Cell(0, 1), "1", "4", FPWorkbenchEditKind.Number, "test reason");
            string path = Path.Combine(root, "delta.fpchanges");
            await FPWorkbenchChangesFile.SaveAsync(new[] { changes }, path, new[] { data });
            var loaded = await FPWorkbenchChangesFile.LoadAsync(path, new[] { data });
            Assert.That(loaded[0].Cells[0].Original, Is.EqualTo("1")); Assert.That(loaded[0].Cells[0].Value, Is.EqualTo("4"));
            try { await FPWorkbenchChangesFile.LoadAsync(path, Array.Empty<FPWorkbenchCsvDataset>()); Assert.Fail(); }
            catch (InvalidDataException) { }
            changes.Cells[0].Original = "wrong precondition";
            await FPWorkbenchChangesFile.SaveAsync(new[] { changes }, path, new[] { data });
            try { await FPWorkbenchChangesFile.LoadAsync(path, new[] { data }); Assert.Fail(); }
            catch (InvalidDataException) { }
            Assert.Throws<IOException>(() => FPWorkbenchChangesFile.SaveAsync(new[] { changes }, Path.Combine(data.CacheDirectory, "unsafe.fpchanges"), new[] { data }));
        }
        [Test] public async Task WorkbookPersistsSourceBoundCorrections()
        {
            var data = await Import(); var changes = new FPWorkbenchChangeSet { DatasetId = data.DatasetId };
            changes.Set(data.Cell(1, 1), "2", "20", FPWorkbenchEditKind.Number, "verified correction");
            var document = new FPWorkbenchDocument(); document.Changes.Add(changes);
            document.Sheets.Add(new FPWorkbenchSheetDocument { SourcePath = source, DatasetId = data.DatasetId, CacheDirectory = data.CacheDirectory });
            string path = Path.Combine(root, "review.fpworkbook"); await FPWorkbenchDocumentFile.SaveAsync(document, path);
            var loaded = await FPWorkbenchDocumentFile.LoadAsync(path);
            Assert.That(loaded.Document.Changes[0].DatasetId, Is.EqualTo(data.DatasetId));
            Assert.That(loaded.Document.Changes[0].Cells[0].Reason, Is.EqualTo("verified correction"));
        }
    }
}
