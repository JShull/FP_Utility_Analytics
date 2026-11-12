// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;

    /// <summary>Per-entry coordination shared by explicitly managed datasets. Filesystem work runs only on workers.</summary>
    internal sealed class FPWorkbenchCacheBinding
    {
        private readonly FPWorkbenchDiskCacheStore store;
        private readonly string id;
        private readonly SemaphoreSlim writes = new SemaphoreSlim(1, 1);
        internal FPWorkbenchCacheBinding(FPWorkbenchDiskCacheStore store, string id) { this.store = store; this.id = id; }
        internal IDisposable Retain()
        {
            if (!store.TryPin(id, out var pin)) throw new IOException("Managed cache is no longer available; reopen it from the workbook's original source.");
            return pin;
        }
        internal Publication Begin(string directory, long estimatedBytes, CancellationToken token)
        {
            var pin = Retain(); bool entered = false;
            try {
                writes.Wait(token); entered = true;
                FPWorkbenchDiskCacheStore.ExtensionLease write = null;
                if (!Directory.Exists(directory)) write = store.ReserveExtensionAsync(id, estimatedBytes, token).GetAwaiter().GetResult();
                return new Publication(this, pin, write, directory, token);
            } catch { if (entered) writes.Release(); pin.Dispose(); throw; }
        }
        internal sealed class Publication : IDisposable
        {
            private FPWorkbenchCacheBinding owner;
            private readonly IDisposable pin;
            private readonly FPWorkbenchDiskCacheStore.ExtensionLease writer;
            private readonly string directory;
            private readonly CancellationToken token;
            internal Publication(FPWorkbenchCacheBinding owner, IDisposable pin, FPWorkbenchDiskCacheStore.ExtensionLease writer, string directory, CancellationToken token)
            { this.owner = owner; this.pin = pin; this.writer = writer; this.directory = directory; this.token = token; }
            internal void Complete(IEnumerable<string> files)
            {
                if (writer == null) return;
                var paths = files.Select(f => Path.GetRelativePath(writer.DirectoryPath, Path.Combine(directory, f)).Replace(Path.DirectorySeparatorChar, '/')).ToArray();
                writer.CommitAsync(paths).GetAwaiter().GetResult();
                token.ThrowIfCancellationRequested();
            }
            public void Dispose()
            {
                var current = Interlocked.Exchange(ref owner, null); if (current == null) return;
                writer?.Dispose(); current.writes.Release(); pin.Dispose();
            }
        }
    }
}
