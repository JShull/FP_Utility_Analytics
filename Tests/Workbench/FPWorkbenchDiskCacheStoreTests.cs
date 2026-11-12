// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Tests
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;

    public sealed class FPWorkbenchDiskCacheStoreTests
    {
        private const long MiB = 1024 * 1024;
        private sealed class Item { public string Id, Directory; }
        private static async Task<Item> Add(FPWorkbenchDiskCacheStore store, int bytes)
        {
            using var writer = await store.ReserveAsync(bytes);
            Directory.CreateDirectory(Path.Combine(writer.DirectoryPath, "nested"));
            File.WriteAllBytes(Path.Combine(writer.DirectoryPath, "nested", "payload.bin"), new byte[bytes]);
            await writer.CommitAsync(new[] { "nested/payload.bin" });
            return new Item { Id = writer.Id, Directory = writer.DirectoryPath };
        }
        [Test] public async Task ReservationsEvictLeastRecentUnpinnedEntriesAndReleaseEstimatedOverhead()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPDiskLru-" + Guid.NewGuid().ToString("N"));
            FPWorkbenchDiskCacheStore store = null; FPWorkbenchDiskCacheStore.Lease pinned = null;
            try {
                store = await FPWorkbenchDiskCacheStore.OpenAsync(root, 4 * MiB);
                var first = await Add(store, (int)MiB); var second = await Add(store, (int)MiB);
                Assert.That(store.ReservedBytes, Is.Zero); Assert.That(store.TryPin(first.Id, out pinned), Is.True);
                Assert.Throws<InvalidOperationException>(() => store.Dispose(), "A live reader prevents closing its store.");
                var third = await Add(store, (int)MiB);
                Assert.That(Directory.Exists(first.Directory), Is.True); Assert.That(Directory.Exists(second.Directory), Is.False);
                Assert.That(Directory.Exists(third.Directory), Is.True); Assert.That(store.TryPin(second.Id, out _), Is.False);
                var held = await store.TrimAsync(0); Assert.That(held.WithinTarget, Is.False); Assert.That(Directory.Exists(first.Directory), Is.True);
                pinned.Dispose(); pinned = null;
                var empty = await store.TrimAsync(0); Assert.That(empty.WithinTarget, Is.True); Assert.That(empty.RemainingBytes, Is.Zero);
                Assert.That(store.ReservedBytes, Is.Zero);
            } finally { pinned?.Dispose(); store?.Dispose(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
        [Test] public async Task FlushAndReopenPreserveRecentUseAndExcludeConcurrentStoreOwners()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPDiskRestore-" + Guid.NewGuid().ToString("N"));
            FPWorkbenchDiskCacheStore store = null;
            try {
                store = await FPWorkbenchDiskCacheStore.OpenAsync(root, 4 * MiB);
                var first = await Add(store, 256 * 1024); var second = await Add(store, 256 * 1024);
                Assert.That(store.TryPin(first.Id, out var lease), Is.True); lease.Dispose(); await store.FlushAsync();
                bool refused = false;
                try { using var other = await FPWorkbenchDiskCacheStore.OpenAsync(root, 4 * MiB); }
                catch (IOException) { refused = true; }
                Assert.That(refused, Is.True);
                store.Dispose(); store = await FPWorkbenchDiskCacheStore.OpenAsync(root, 4 * MiB);
                var trim = await store.TrimAsync(300 * 1024);
                Assert.That(trim.WithinTarget, Is.True); Assert.That(trim.RemovedEntries, Is.EqualTo(1));
                Assert.That(Directory.Exists(first.Directory), Is.True); Assert.That(Directory.Exists(second.Directory), Is.False);
            } finally { store?.Dispose(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
        [Test] public async Task UnexpectedAndModifiedContentIsProtectedAndPathTraversalCannotClaimSourceFiles()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPDiskProtect-" + Guid.NewGuid().ToString("N"));
            FPWorkbenchDiskCacheStore store = null;
            try {
                store = await FPWorkbenchDiskCacheStore.OpenAsync(Path.Combine(root, "store"), 4 * MiB);
                var unknown = await Add(store, 128); string note = Path.Combine(unknown.Directory, "notes.txt"); File.WriteAllText(note, "Preserve user content");
                var modified = await Add(store, 128); string payload = Path.Combine(modified.Directory, "nested", "payload.bin");
                var changed = new byte[128]; changed[0] = 42; File.WriteAllBytes(payload, changed);
                var trim = await store.TrimAsync(0); Assert.That(trim.WithinTarget, Is.False); Assert.That(trim.RemovedEntries, Is.Zero);
                Assert.That(File.ReadAllText(note), Is.EqualTo("Preserve user content")); Assert.That(File.ReadAllBytes(payload), Is.EqualTo(changed));
                string source = Path.Combine(root, "source.csv"); File.WriteAllText(source, "x\n3\n");
                using (var writer = await store.ReserveAsync(256)) {
                    Directory.CreateDirectory(writer.DirectoryPath); bool refused = false;
                    try { await writer.CommitAsync(new[] { "../../source.csv" }); } catch (InvalidDataException) { refused = true; }
                    Assert.That(refused, Is.True); Assert.That(File.ReadAllText(source), Is.EqualTo("x\n3\n"));
                }
                Assert.That(store.ReservedBytes, Is.Zero);
                using (var writer = await store.ReserveAsync(128)) {
                    Directory.CreateDirectory(writer.DirectoryPath);
                    File.WriteAllText(Path.Combine(writer.DirectoryPath, "payload.bin"), "cache");
                    string unexpected = Path.Combine(writer.DirectoryPath, "original.csv"); File.WriteAllText(unexpected, "Preserve original");
                    bool refused = false;
                    try { await writer.CommitAsync(new[] { "payload.bin" }); } catch (InvalidDataException) { refused = true; }
                    Assert.That(refused, Is.True, "Unlisted content cannot be implicitly adopted into cache ownership.");
                    Assert.That(File.ReadAllText(unexpected), Is.EqualTo("Preserve original"));
                }
            } finally { store?.Dispose(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
        [Test] public async Task PendingWritesConsumeAdmissionAndOversizedUncommittedPayloadsRemainUnowned()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPDiskPending-" + Guid.NewGuid().ToString("N"));
            FPWorkbenchDiskCacheStore store = null; FPWorkbenchDiskCacheStore.WriteLease writer = null;
            try {
                store = await FPWorkbenchDiskCacheStore.OpenAsync(root, 3 * MiB);
                writer = await store.ReserveAsync(MiB); Assert.That(store.ReservedBytes, Is.EqualTo(2 * MiB));
                bool refused = false;
                try { using var excess = await store.ReserveAsync(MiB); } catch (IOException) { refused = true; }
                Assert.That(refused, Is.True); Assert.That(store.ReservedBytes, Is.EqualTo(2 * MiB));
                Directory.CreateDirectory(writer.DirectoryPath); string payload = Path.Combine(writer.DirectoryPath, "payload.bin");
                File.WriteAllBytes(payload, new byte[(int)MiB + 1]); refused = false;
                try { await writer.CommitAsync(new[] { "payload.bin" }); } catch (IOException) { refused = true; }
                Assert.That(refused, Is.True); Assert.That(File.Exists(Path.Combine(writer.DirectoryPath, "entry.fpcache")), Is.False);
                writer.Dispose(); writer = null; Assert.That(store.ReservedBytes, Is.Zero);
                var trim = await store.TrimAsync(0); Assert.That(trim.WithinTarget, Is.False); Assert.That(File.Exists(payload), Is.True);
            } finally { writer?.Dispose(); store?.Dispose(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
        [Test] public async Task CancellationAndCorruptManifestsNeverDeleteProtectedPayloads()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPDiskCorrupt-" + Guid.NewGuid().ToString("N"));
            FPWorkbenchDiskCacheStore store = null;
            try {
                store = await FPWorkbenchDiskCacheStore.OpenAsync(root, 4 * MiB); var item = await Add(store, 128);
                using var cancel = new CancellationTokenSource(); cancel.Cancel(); bool cancelled = false;
                try { await store.TrimAsync(0, cancel.Token); } catch (OperationCanceledException) { cancelled = true; }
                Assert.That(cancelled, Is.True); Assert.That(Directory.Exists(item.Directory), Is.True);
                File.WriteAllBytes(Path.Combine(item.Directory, "entry.fpcache"), new byte[] { 1, 2, 3 });
                store.Dispose(); store = await FPWorkbenchDiskCacheStore.OpenAsync(root, 4 * MiB);
                Assert.That(store.TryPin(item.Id, out _), Is.False, "Corrupt entries cannot be adopted.");
                var trim = await store.TrimAsync(0); Assert.That(trim.WithinTarget, Is.False);
                Assert.That(File.Exists(Path.Combine(item.Directory, "nested", "payload.bin")), Is.True);
            } finally { store?.Dispose(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
        [Test] public async Task DisposingAQueuedCommitRetainsItsReservationUntilTerminalCancellation()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPDiskCommitLifetime-" + Guid.NewGuid().ToString("N"));
            FPWorkbenchDiskCacheStore store = null; FPWorkbenchDiskCacheStore.WriteLease writer = null;
            SemaphoreSlim serialization = null; bool held = false;
            using var cancel = new CancellationTokenSource();
            try {
                store = await FPWorkbenchDiskCacheStore.OpenAsync(root, 4 * MiB); writer = await store.ReserveAsync(128);
                Directory.CreateDirectory(writer.DirectoryPath); File.WriteAllText(Path.Combine(writer.DirectoryPath, "payload.bin"), "cache");
                serialization = (SemaphoreSlim)typeof(FPWorkbenchDiskCacheStore).GetField("mutation", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(store);
                held = serialization.Wait(0); Assert.That(held, Is.True);
                var commit = writer.CommitAsync(new[] { "payload.bin" }, cancel.Token);
                writer.Dispose(); Assert.That(store.ReservedBytes, Is.GreaterThan(0), "Queued commit still owns its write admission.");
                Assert.Throws<InvalidOperationException>(() => store.Dispose());
                cancel.Cancel(); bool cancelled = false;
                try { await commit; } catch (OperationCanceledException) { cancelled = true; }
                Assert.That(cancelled, Is.True); Assert.That(store.ReservedBytes, Is.Zero);
                Assert.That(File.Exists(Path.Combine(writer.DirectoryPath, "entry.fpcache")), Is.False);
                serialization.Release(); held = false; writer = null;
            } finally { if (held) serialization.Release(); writer?.Dispose(); store?.Dispose(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
        [Test] public async Task RecentPinsRemainNewestWhenPersistedRecencyIsAheadOfTheWallClock()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPDiskRecency-" + Guid.NewGuid().ToString("N")); FPWorkbenchDiskCacheStore store = null;
            try {
                store = await FPWorkbenchDiskCacheStore.OpenAsync(root, 4 * MiB);
                var first = await Add(store, 256 * 1024); var second = await Add(store, 256 * 1024); store.Dispose(); store = null;
                // Simulate a store written before a wall-clock rollback, without changing the host clock.
                using (var output = new BinaryWriter(File.OpenWrite(Path.Combine(first.Directory, "entry.fpcache")))) {
                    output.BaseStream.Position = 36; output.Write(DateTime.UtcNow.AddYears(1).Ticks);
                }
                store = await FPWorkbenchDiskCacheStore.OpenAsync(root, 4 * MiB);
                Assert.That(store.TryPin(second.Id, out var recent), Is.True); recent.Dispose();
                var trim = await store.TrimAsync(300 * 1024);
                Assert.That(trim.WithinTarget, Is.True); Assert.That(Directory.Exists(first.Directory), Is.False);
                Assert.That(Directory.Exists(second.Directory), Is.True, "The newest access must win even after wall-clock rollback.");
            } finally { store?.Dispose(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
    }
}
