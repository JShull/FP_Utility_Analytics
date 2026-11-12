// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Tests
{
    using System;
    using System.IO;
    using System.Threading.Tasks;
    using NUnit.Framework;

    public sealed class FPWorkbenchManagedCacheTests
    {
        [Test] public async Task LocalCacheSettingsRoundTripAndRejectRootsInsideAssets()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPManagedSettings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try {
                string path = Path.Combine(root, "settings.json");
                Assert.That((await FPWorkbenchCacheSettings.LoadAsync(path)).QuotaBytes, Is.EqualTo(4L * 1024 * 1024 * 1024));
                var settings = new FPWorkbenchCacheSettings { Root = Path.Combine(root, "store"), QuotaBytes = 128L * 1024 * 1024 };
                await settings.SaveAsync(path); var restored = await FPWorkbenchCacheSettings.LoadAsync(path);
                Assert.That(restored.Root, Is.EqualTo(settings.Root)); Assert.That(restored.QuotaBytes, Is.EqualTo(settings.QuotaBytes));
                restored.Root = Path.Combine(root, "Assets", "Cache"); Assert.Throws<InvalidDataException>(() => restored.Validate());
                restored.Root = Path.GetPathRoot(root); Assert.Throws<InvalidDataException>(() => restored.Validate());
                restored.Root = settings.Root; restored.QuotaBytes = 0; Assert.Throws<InvalidDataException>(() => restored.Validate());
            } finally { Directory.Delete(root, true); }
        }
        [Test] public async Task FailedMultiSourceWorkbookAdmissionReleasesEarlierRecoveryPins()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPManagedRejectWorkbook-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try {
                using var store = await FPWorkbenchDiskCacheStore.OpenAsync(Path.Combine(root, "store"), 64L * 1024 * 1024);
                var cache = new FPWorkbenchManagedCache(store); var document = new FPWorkbenchDocument(); string lastCsv = null;
                for (int i = 0; i < 2; i++) {
                    lastCsv = Path.Combine(root, "original-" + i + ".csv"); File.WriteAllText(lastCsv, "x\n" + i + "\n");
                    using var source = await cache.ImportAsync(lastCsv, new FPWorkbenchCsvProfile());
                    var data = source.Dataset;
                    document.Sources.Add(new FPWorkbenchSourceDocument { DatasetId = data.DatasetId, SourcePath = lastCsv, CacheDirectory = data.CacheDirectory, Import = data.ImportProfile });
                }
                string path = Path.Combine(root, "saved.fpworkbook"); await FPWorkbenchDocumentFile.SaveAsync(document, path);
                byte[] saved = File.ReadAllBytes(path); await store.TrimAsync(0); File.Delete(lastCsv);
                bool refused = false;
                try { using var loaded = await FPWorkbenchDocumentFile.LoadAsync(path, managedCache: cache); } catch (FileNotFoundException) { refused = true; }
                Assert.That(refused, Is.True); Assert.That(File.ReadAllBytes(path), Is.EqualTo(saved));
                Assert.That((await store.TrimAsync(0)).WithinTarget, Is.True, "A failed later source cannot leak earlier source pins.");
                Assert.That(store.ReservedBytes, Is.Zero);
            } finally { Directory.Delete(root, true); }
        }
        [Test] public async Task WorkbookRecoveryPreservesSheetsCorrectionsCalculationsFiguresAndSavedFile()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPManagedWorkbook-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try {
                string csv = Path.Combine(root, "original.csv"); File.WriteAllText(csv, "x,y,z\n1,2,3\n4,5,6\n");
                using var store = await FPWorkbenchDiskCacheStore.OpenAsync(Path.Combine(root, "store"), 64L * 1024 * 1024);
                var cache = new FPWorkbenchManagedCache(store); var document = new FPWorkbenchDocument();
                using (var source = await cache.ImportAsync(csv, new FPWorkbenchCsvProfile())) {
                    var data = source.Dataset;
                    var formulas = new FPWorkbenchFormulaSet { DatasetId = data.DatasetId };
                    formulas.Columns.Add(new FPWorkbenchDerivedColumn { Name = "Double X", Expression = "C1 * 2" });
                    document.Sources.Add(new FPWorkbenchSourceDocument { DatasetId = data.DatasetId, SourcePath = csv, CacheDirectory = data.CacheDirectory, Import = data.ImportProfile, HasCalculations = true, Calculations = formulas });
                    for (int i = 0; i < 2; i++) document.Sheets.Add(new FPWorkbenchSheetDocument {
                        Name = "View " + i, DatasetId = data.DatasetId, SourcePath = csv, CacheDirectory = data.CacheDirectory, Import = data.ImportProfile,
                        FirstRow = i, Columns = new FPWorkbenchColumnLayout { ManualWidths = new[] { 140f, 180f, 160f, 120f } } });
                    var changes = new FPWorkbenchChangeSet { DatasetId = data.DatasetId };
                    changes.Set(data.Cell(0, 1), "2", "7", FPWorkbenchEditKind.Number, "demonstration"); document.Changes.Add(changes);
                    var mapping = await FPWorkbenchAlignment.BuildAsync(data, data, new FPWorkbenchAlignmentOptions { Mode = FPWorkbenchAlignmentMode.SharedSourceRows });
                    var values = await FPWorkbenchAlignedValues.BuildAsync(mapping, 0, 1);
                    document.Figures.Add(new FPWorkbenchFigureDocument { DatasetId = data.DatasetId, Name = "Aligned recipe", Kind = FPWorkbenchFigureKind.AlignedScatter,
                        AlignedRecipe = FPWorkbenchAlignedFigureRecipe.FromValues(values.Metadata), Binding = new FPWorkbenchSeriesBinding { XMode = FPWorkbenchXMode.Numeric }, Colors = new[] { UnityEngine.Color.cyan } });
                }
                string path = Path.Combine(root, "saved.fpworkbook"); await FPWorkbenchDocumentFile.SaveAsync(document, path);
                byte[] saved = File.ReadAllBytes(path); string oldCache = document.Sources[0].CacheDirectory;
                Assert.That((await store.TrimAsync(0)).WithinTarget, Is.True);
                using (var loaded = await FPWorkbenchDocumentFile.LoadAsync(path, managedCache: cache)) {
                    Assert.That(loaded.Document.Sheets.Count, Is.EqualTo(2)); Assert.That(loaded.Datasets[0], Is.SameAs(loaded.Datasets[1]));
                    Assert.That(loaded.Document.Sheets[1].FirstRow, Is.EqualTo(1));
                    Assert.That(loaded.Document.Sheets[1].Columns.ManualWidths[1], Is.EqualTo(180));
                    Assert.That(loaded.Document.Changes[0].Cells.Count, Is.EqualTo(1));
                    Assert.That(loaded.Document.Sources[0].CompileProgram(loaded.SourceDatasets[0]).ColumnCount, Is.EqualTo(1));
                    Assert.That(loaded.Document.Sources[0].CacheDirectory, Is.Not.EqualTo(oldCache));
                    var rebuilt = await loaded.Document.Figures[0].AlignedRecipe.BuildAsync(loaded.SourceDatasets[0], loaded.SourceDatasets[0]);
                    Assert.That(rebuilt.Dataset.RowCount, Is.EqualTo(2));
                    Assert.That((await store.TrimAsync(0)).RemovedEntries, Is.Zero, "Admission pins survive until views take ownership.");
                }
                Assert.That(document.Sources[0].CacheDirectory, Is.EqualTo(oldCache));
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(saved), "Recovery does not implicitly save the workbook.");
                Assert.That((await store.TrimAsync(0)).WithinTarget, Is.True);
            } finally { Directory.Delete(root, true); }
        }
        [Test] public async Task SharedFigureRetainsCacheUntilItsFinalViewCloses()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPManagedFigure-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try {
                string csv = Path.Combine(root, "original.csv"); File.WriteAllText(csv, "x\n1\n2\n");
                using var store = await FPWorkbenchDiskCacheStore.OpenAsync(Path.Combine(root, "store"), 64L * 1024 * 1024);
                var cache = new FPWorkbenchManagedCache(store);
                using var source = await cache.ImportAsync(csv, new FPWorkbenchCsvProfile());
                var session = new FPWorkbenchFigureSession(source.Dataset, new FPWorkbenchSeriesBinding(), new FPWorkbenchPlotSettings());
                using var tableFigure = session.Retain(); using var detachedFigure = session.Retain(); source.Dispose();
                Assert.That((await store.TrimAsync(0)).RemovedEntries, Is.Zero);
                tableFigure.Dispose(); Assert.That((await store.TrimAsync(0)).RemovedEntries, Is.Zero);
                detachedFigure.Dispose(); Assert.That(session.IsDisposed, Is.True);
                Assert.That((await store.TrimAsync(0)).WithinTarget, Is.True);
            } finally { Directory.Delete(root, true); }
        }
        [Test] public async Task GeneratedSeriesSpatialAndAlignedProductsRemainOwnedAndEvictTogether()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPManagedProducts-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try {
                string csv = Path.Combine(root, "original.csv"); File.WriteAllText(csv, "x,y,z\n1,2,3\n4,5,6\n");
                using var store = await FPWorkbenchDiskCacheStore.OpenAsync(Path.Combine(root, "store"), 64L * 1024 * 1024);
                var cache = new FPWorkbenchManagedCache(store); string directory;
                using (var source = await cache.ImportAsync(csv, new FPWorkbenchCsvProfile())) {
                    var data = source.Dataset; directory = data.CacheDirectory;
                    var series = await FPWorkbenchSeriesCache.BuildAsync(data, new FPWorkbenchSeriesBinding { YColumn = 1 });
                    var points = await FPWorkbenchPointCache.BuildAsync(data, new[] { 0, 1, 2 });
                    var alignment = await FPWorkbenchAlignment.BuildAsync(data, data, new FPWorkbenchAlignmentOptions { Mode = FPWorkbenchAlignmentMode.SharedSourceRows });
                    var values = await FPWorkbenchAlignedValues.BuildAsync(alignment, 0, 1);
                    await FPWorkbenchSeriesCache.BuildAsync(values.Dataset, new FPWorkbenchSeriesBinding { YColumn = 3 });
                    Assert.That(store.ReservedBytes, Is.Zero);
                    using var detached = values.Dataset.RetainCache(); source.Dispose();
                    Assert.That((await store.TrimAsync(0)).RemovedEntries, Is.Zero, "A derived view retains its original owned entry.");
                    Assert.That((await series.QueryAsync(0, 2)).Bins, Is.Not.Empty);
                }
                var trim = await store.TrimAsync(0);
                Assert.That(trim.WithinTarget, Is.True, "Every subsequently generated file was declared; none became accidental protected content.");
                Assert.That(trim.RemovedEntries, Is.EqualTo(1)); Assert.That(Directory.Exists(directory), Is.False);
                Assert.That(File.ReadAllText(csv), Is.EqualTo("x,y,z\n1,2,3\n4,5,6\n"));
            } finally { Directory.Delete(root, true); }
        }
        [Test] public async Task EvictedSourceRegeneratesWithExactIdentityAndAWorkingRowIndex()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPManagedRecover-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try {
                string csv = Path.Combine(root, "original.csv"); File.WriteAllText(csv, "x,y\n1,2\n3,4\n");
                using var store = await FPWorkbenchDiskCacheStore.OpenAsync(Path.Combine(root, "store"), 64L * 1024 * 1024);
                var cache = new FPWorkbenchManagedCache(store);
                FPWorkbenchSourceDocument saved;
                using (var source = await cache.ImportAsync(csv, new FPWorkbenchCsvProfile())) {
                    var data = source.Dataset;
                    saved = new FPWorkbenchSourceDocument { DatasetId = data.DatasetId, SourcePath = csv, CacheDirectory = data.CacheDirectory, Import = data.ImportProfile, Name = "Sample" };
                    Assert.That((await store.TrimAsync(0)).RemovedEntries, Is.Zero, "An active source is pinned.");
                    using var second = await cache.OpenAsync(saved);
                    source.Dispose();
                    Assert.That((await store.TrimAsync(0)).RemovedEntries, Is.Zero, "A shared reader still pins the cache.");
                }
                Assert.That((await store.TrimAsync(0)).RemovedEntries, Is.EqualTo(1));
                using var restored = await cache.OpenAsync(saved);
                Assert.That(restored.Dataset.DatasetId, Is.EqualTo(saved.DatasetId));
                Assert.That(restored.Dataset.CacheDirectory, Is.Not.EqualTo(saved.CacheDirectory));
                Assert.That(restored.Dataset.RowCount, Is.EqualTo(2));
                var page = await restored.Dataset.ReadPageAsync(1, 1, 0, 2);
                Assert.That(page, Is.Not.Null);
                Assert.That(saved.CacheDirectory, Is.Not.EqualTo(restored.Dataset.CacheDirectory), "Opening never mutates the saved document.");
                Assert.That(File.ReadAllText(csv), Is.EqualTo("x,y\n1,2\n3,4\n"));
            } finally { Directory.Delete(root, true); }
        }
        [Test] public async Task ChangedOriginalIsRefusedAfterEvictionWithoutRetargetingMetadata()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPManagedChanged-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try {
                string csv = Path.Combine(root, "original.csv"); File.WriteAllText(csv, "x\n1\n");
                using var store = await FPWorkbenchDiskCacheStore.OpenAsync(Path.Combine(root, "store"), 64L * 1024 * 1024);
                var cache = new FPWorkbenchManagedCache(store); FPWorkbenchSourceDocument saved;
                using (var source = await cache.ImportAsync(csv, new FPWorkbenchCsvProfile()))
                    saved = new FPWorkbenchSourceDocument { DatasetId = source.Dataset.DatasetId, SourcePath = csv, CacheDirectory = source.Dataset.CacheDirectory, Import = source.Dataset.ImportProfile, Name = "Sample" };
                await store.TrimAsync(0); string id = saved.DatasetId, directory = saved.CacheDirectory;
                File.WriteAllText(csv, "x\n9\n"); bool refused = false;
                try { using var changed = await cache.OpenAsync(saved); } catch (InvalidDataException) { refused = true; }
                Assert.That(refused, Is.True); Assert.That(saved.DatasetId, Is.EqualTo(id)); Assert.That(saved.CacheDirectory, Is.EqualTo(directory));
                Assert.That(store.ReservedBytes, Is.Zero);
                Assert.That((await store.TrimAsync(0)).WithinTarget, Is.True, "Failed identity verification released its source pin.");
            } finally { Directory.Delete(root, true); }
        }
    }
}
