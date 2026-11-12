// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Explicit runtime import/recovery policy. The caller owns the store and each returned source lease.</summary>
    public sealed class FPWorkbenchManagedCache
    {
        private readonly FPWorkbenchDiskCacheStore store;
        private readonly Dictionary<string, WeakReference<FPWorkbenchCacheBinding>> bindings = new Dictionary<string, WeakReference<FPWorkbenchCacheBinding>>();
        private void Attach(FPWorkbenchCsvDataset data, string id)
        {
            lock (bindings) {
                if (bindings.Count > 128) {
                    var expired = new List<string>();
                    foreach (var pair in bindings) if (!pair.Value.TryGetTarget(out _)) expired.Add(pair.Key);
                    foreach (string key in expired) bindings.Remove(key);
                }
                if (!bindings.TryGetValue(id, out var reference) || !reference.TryGetTarget(out var binding)) {
                    binding = new FPWorkbenchCacheBinding(store, id); bindings[id] = new WeakReference<FPWorkbenchCacheBinding>(binding);
                }
                data.CacheBinding = binding;
            }
        }
        public FPWorkbenchManagedCache(FPWorkbenchDiskCacheStore store) { this.store = store ?? throw new ArgumentNullException(nameof(store)); }
        public sealed class SourceLease : IDisposable
        {
            private FPWorkbenchDiskCacheStore.Lease lease;
            public FPWorkbenchCsvDataset Dataset { get; }
            internal SourceLease(FPWorkbenchCsvDataset dataset, FPWorkbenchDiskCacheStore.Lease lease) { Dataset = dataset; this.lease = lease; }
            public void Dispose() { Interlocked.Exchange(ref lease, null)?.Dispose(); }
        }
        public async Task<SourceLease> ImportAsync(string sourcePath, FPWorkbenchCsvProfile profile,
            CancellationToken cancellation = default, IProgress<double> progress = null, IProgress<FPWorkbenchCsvPreview> preview = null)
        {
            var settings = (profile ?? throw new ArgumentNullException(nameof(profile))).Copy();
            // At most one logical record per source byte, one 64-bit offset per record, and bounded metadata.
            long estimate = checked(new FileInfo(sourcePath).Length * 8 + 16L * 1024 * 1024);
            using var writer = await store.ReserveAsync(estimate, cancellation).ConfigureAwait(false);
            var data = await FPWorkbenchCsvDataset.ImportAsync(sourcePath, writer.DirectoryPath, settings, cancellation, progress, preview).ConfigureAwait(false);
            // Once atomic CSV publication succeeds, finish ownership publication even if cancellation arrives.
            // This leaves the cancelled product eligible for safe eviction instead of orphaning protected files.
            await writer.CommitAsync(new[] { "rows.fpwi", "dataset.fpwm" }).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            Attach(data, writer.Id);
            if (!store.TryPin(writer.Id, out var pin)) throw new IOException("The published cache could not be retained.");
            return new SourceLease(data, pin);
        }
        /// <summary>Rebuilds only absent caches. Changed or corrupt existing caches fail verification, preserving legacy behavior.</summary>
        public async Task<SourceLease> OpenExistingAsync(string directory, CancellationToken cancellation = default)
        {
            string path = Path.GetFullPath(directory);
            string id = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
            FPWorkbenchDiskCacheStore.Lease pin = null;
            bool managed = SamePath(Path.Combine(store.Root, "entries", id), path);
            if (managed && !store.TryPin(id, out pin)) throw new IOException("Managed cache is unavailable or protected; reopen its workbook to reconstruct it from the original source.");
            try {
                var data = await FPWorkbenchCsvDataset.OpenAsync(path, cancellation).ConfigureAwait(false);
                if (managed) Attach(data, id);
                return new SourceLease(data, pin);
            } catch { pin?.Dispose(); throw; }
        }
        public async Task<SourceLease> OpenAsync(FPWorkbenchSourceDocument source, CancellationToken cancellation = default)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            source.Validate(); SourceLease result;
            if (Directory.Exists(source.CacheDirectory)) result = await OpenExistingAsync(source.CacheDirectory, cancellation).ConfigureAwait(false);
            else result = await ImportAsync(source.SourcePath, source.Import, cancellation).ConfigureAwait(false);
            var data = result.Dataset;
            if (data.DatasetId != source.DatasetId || !SamePath(data.SourcePath, source.SourcePath) ||
                data.ImportProfile.Delimiter != source.Import.Delimiter || data.ImportProfile.EncodingCodePage != source.Import.EncodingCodePage) {
                result.Dispose(); throw new InvalidDataException("Original source/import identity changed; annotations were not retargeted.");
            }
            return result;
        }
        private static bool SamePath(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b),
            Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
