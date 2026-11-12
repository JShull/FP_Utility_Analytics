// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Explicitly owned cache entries, estimated write admission and pinned LRU eviction. Never adopts legacy directories.</summary>
    public sealed class FPWorkbenchDiskCacheStore : IDisposable
    {
        private const int Magic = 0x31434446, MaximumFiles = 1024, ManifestAllowance = 1024 * 1024;
        public const int MaximumEntries = 1024, MaximumCatalogFiles = 16384;
        private const string ManifestName = "entry.fpcache";
        private readonly object gate = new object();
        private readonly SemaphoreSlim mutation = new SemaphoreSlim(1, 1);
        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly FileStream ownershipLock;
        private readonly string ownerId, payloadRoot;
        private long reserved, recencyClock;
        private int operations;
        private bool disposed;
        public string Root { get; }
        public long QuotaBytes { get; }
        public long ReservedBytes { get { lock (gate) return reserved; } }
        public bool IsIdle { get { lock (gate) return operations == 0 && entries.Values.All(e => e.Pins == 0 && !e.Pending); } }
        internal sealed class OwnedFile { public string Path; public long Bytes; public byte[] Hash; }
        internal sealed class Entry
        {
            public string Id, Directory;
            public long LastUse, Reserved;
            public int Pins;
            public bool Pending, Evicting, Committing, ReleaseRequested, Updating;
            public OwnedFile[] Files;
        }
        public sealed class TrimResult
        {
            public long RemainingBytes { get; internal set; }
            public long ReclaimedBytes { get; internal set; }
            public int RemovedEntries { get; internal set; }
            public bool WithinTarget { get; internal set; }
        }
        private FPWorkbenchDiskCacheStore(string root, string owner, long quota, FileStream ownership)
        { Root = root; ownerId = owner; QuotaBytes = quota; ownershipLock = ownership; payloadRoot = Path.Combine(root, "entries"); }

        public static Task<FPWorkbenchDiskCacheStore> OpenAsync(string root, long quotaBytes, CancellationToken cancellation = default)
        {
            if (quotaBytes < ManifestAllowance) throw new ArgumentOutOfRangeException(nameof(quotaBytes));
            string path = Path.GetFullPath(root);
            return Task.Run(() => {
                cancellation.ThrowIfCancellationRequested(); CheckLinks(path); Directory.CreateDirectory(path); CheckLinks(path);
                string lockPath = Path.Combine(path, "store.lock"); CheckLinks(lockPath);
                var ownership = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                try {
                    string marker = Path.Combine(path, "store.fpcache"), owner;
                    if (File.Exists(marker)) {
                        CheckLinks(marker); using var input = new BinaryReader(File.OpenRead(marker));
                        if (input.BaseStream.Length != 20 || input.ReadInt32() != Magic) throw new InvalidDataException("Not an owned Workbench cache store.");
                        owner = new Guid(input.ReadBytes(16)).ToString("N");
                    } else {
                        owner = Guid.NewGuid().ToString("N");
                        using var output = new BinaryWriter(new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None));
                        output.Write(Magic); output.Write(new Guid(owner).ToByteArray());
                    }
                    var store = new FPWorkbenchDiskCacheStore(path, owner, quotaBytes, ownership);
                    Directory.CreateDirectory(store.payloadRoot); CheckLinks(store.payloadRoot);
                    int catalogFiles = 0;
                    foreach (string directory in Directory.EnumerateDirectories(store.payloadRoot)) {
                        cancellation.ThrowIfCancellationRequested();
                        Entry entry = null;
                        try { entry = store.ReadManifest(directory); }
                        catch (InvalidDataException) { } // Unrecognized/corrupt directories are protected, never adopted.
                        catch (IOException) { }
                        catch (ArgumentException) { }
                        if (entry == null) continue;
                        if (store.entries.Count >= MaximumEntries || catalogFiles + entry.Files.Length > MaximumCatalogFiles)
                            throw new InvalidDataException("Cache catalog exceeds its bounded entry/file capacity.");
                        store.entries.Add(entry.Id, entry); catalogFiles += entry.Files.Length; store.recencyClock = Math.Max(store.recencyClock, entry.LastUse);
                    }
                    return store;
                } catch { ownership.Dispose(); throw; }
            }, cancellation);
        }

        /// <summary>Reserves estimated payload plus bounded manifest overhead. Existing unpinned entries may be evicted.</summary>
        public Task<WriteLease> ReserveAsync(long estimatedPayloadBytes, CancellationToken cancellation = default)
        {
            if (estimatedPayloadBytes < 0) throw new ArgumentOutOfRangeException(nameof(estimatedPayloadBytes));
            if (estimatedPayloadBytes > QuotaBytes - ManifestAllowance) throw new IOException("The estimated cache write exceeds this store's quota. Increase the quota or choose another dedicated root.");
            long bytes = checked(estimatedPayloadBytes + ManifestAllowance);
            return MutateAsync(() => {
                var trim = TrimCore(QuotaBytes - bytes, cancellation);
                lock (gate) {
                    if (!trim.WithinTarget) throw new IOException("Cache quota is occupied by pinned, pending or protected entries. Close unused views, finish/cancel workers, increase the quota, or choose another dedicated root. Unexpected/modified files remain protected.");
                    if (entries.Count >= MaximumEntries) throw new IOException("Cache entry capacity is full; trim unused entries before reserving another.");
                    string id = Guid.NewGuid().ToString("N");
                    var entry = new Entry { Id = id, Directory = Path.Combine(payloadRoot, id), LastUse = NextUse(), Reserved = bytes, Pending = true, Pins = 1 };
                    if (Directory.Exists(entry.Directory) || File.Exists(entry.Directory)) throw new IOException("Cache identity collision.");
                    entries.Add(id, entry); reserved = checked(reserved + bytes); return new WriteLease(this, entry);
                }
            }, cancellation);
        }
        public sealed class WriteLease : IDisposable
        {
            private readonly FPWorkbenchDiskCacheStore owner;
            private readonly Entry entry;
            public string Id => entry.Id;
            /// <summary>Destination does not exist yet; use it for atomic cache publication, never for original data.</summary>
            public string DirectoryPath => entry.Directory;
            internal WriteLease(FPWorkbenchDiskCacheStore owner, Entry entry) { this.owner = owner; this.entry = entry; }
            public Task CommitAsync(IReadOnlyList<string> ownedRelativeFiles, CancellationToken cancellation = default)
            {
                if (ownedRelativeFiles == null || ownedRelativeFiles.Count > MaximumFiles) throw new ArgumentException("Declare at most 1024 owned cache files.");
                var files = ownedRelativeFiles.ToArray();
                lock (owner.gate) {
                    owner.Check();
                    if (!entry.Pending || entry.ReleaseRequested || entry.Committing) throw new InvalidOperationException("Write lease is not available for commit.");
                    entry.Committing = true;
                }
                return Commit(files, cancellation);
            }
            private async Task Commit(string[] files, CancellationToken token)
            {
                try { await owner.MutateAsync(() => { owner.CommitCore(entry, files, token); return true; }, token).ConfigureAwait(false); }
                finally { lock (owner.gate) { entry.Committing = false; if (entry.ReleaseRequested) owner.ReleaseWrite(entry); } }
            }
            public void Dispose()
            {
                lock (owner.gate) {
                    if (entry.ReleaseRequested) return;
                    entry.ReleaseRequested = true;
                    if (!entry.Committing) owner.ReleaseWrite(entry);
                }
            }
        }
        public sealed class Lease : IDisposable
        {
            private FPWorkbenchDiskCacheStore owner;
            private readonly Entry entry;
            public string DirectoryPath => entry.Directory;
            internal Lease(FPWorkbenchDiskCacheStore owner, Entry entry) { this.owner = owner; this.entry = entry; }
            public void Dispose() { var current = Interlocked.Exchange(ref owner, null); if (current != null) lock (current.gate) entry.Pins--; }
        }
        /// <summary>No filesystem work. A failed pin means the entry is absent, pending, or already being evicted.</summary>
        public bool TryPin(string id, out Lease lease)
        {
            lock (gate) {
                Check(); lease = null;
                if (id == null || !entries.TryGetValue(id, out var entry) || entry.Pending || entry.Evicting) return false;
                long recent = NextUse(); entry.Pins++; entry.LastUse = recent; lease = new Lease(this, entry); return true;
            }
        }
        public Task<TrimResult> TrimAsync(long targetBytes, CancellationToken cancellation = default)
        {
            if (targetBytes < 0 || targetBytes > QuotaBytes) throw new ArgumentOutOfRangeException(nameof(targetBytes));
            return MutateAsync(() => TrimCore(targetBytes, cancellation), cancellation);
        }
        /// <summary>Reserves additional generated files in an existing entry. Existing and unexpected files cannot be adopted or overwritten.</summary>
        public Task<ExtensionLease> ReserveExtensionAsync(string id, long estimatedPayloadBytes, CancellationToken cancellation = default)
        {
            if (estimatedPayloadBytes < 0) throw new ArgumentOutOfRangeException(nameof(estimatedPayloadBytes));
            if (estimatedPayloadBytes > QuotaBytes - ManifestAllowance) throw new IOException("The estimated additional cache write exceeds this store's quota. Increase the quota or choose another dedicated root.");
            long bytes = checked(estimatedPayloadBytes + ManifestAllowance);
            return MutateAsync(() => {
                Entry entry;
                lock (gate) {
                    if (id == null || !entries.TryGetValue(id, out entry) || entry.Pending || entry.Evicting || entry.Updating)
                        throw new IOException("Cache entry is unavailable for additional writes.");
                    entry.Pins++; entry.Updating = true;
                }
                try {
                    if (!Matches(entry, cancellation)) throw new InvalidDataException("Modified or unexpected cache files prevent additional writes.");
                    var trim = TrimCore(QuotaBytes - bytes, cancellation);
                    if (!trim.WithinTarget) throw new IOException("Cache quota is occupied by pinned, pending or protected entries. Close unused views, finish/cancel workers, increase the quota, or choose another dedicated root. Unexpected/modified files remain protected.");
                    lock (gate) reserved = checked(reserved + bytes);
                    return new ExtensionLease(this, entry, bytes);
                } catch { lock (gate) { entry.Pins--; entry.Updating = false; } throw; }
            }, cancellation);
        }
        public sealed class ExtensionLease : IDisposable
        {
            private readonly FPWorkbenchDiskCacheStore owner;
            private readonly Entry entry;
            private readonly long bytes;
            private bool committing, released, finished;
            public string DirectoryPath => entry.Directory;
            internal ExtensionLease(FPWorkbenchDiskCacheStore owner, Entry entry, long bytes) { this.owner = owner; this.entry = entry; this.bytes = bytes; }
            public async Task CommitAsync(IReadOnlyList<string> addedRelativeFiles, CancellationToken cancellation = default)
            {
                if (addedRelativeFiles == null) throw new ArgumentNullException(nameof(addedRelativeFiles));
                var paths = addedRelativeFiles.ToArray();
                lock (owner.gate) {
                    if (released || committing || finished) throw new InvalidOperationException("Additional write lease is no longer available.");
                    committing = true;
                }
                try {
                    await owner.MutateAsync(() => { owner.ExtendCore(entry, paths, bytes, cancellation); return true; }, cancellation).ConfigureAwait(false);
                } finally {
                    lock (owner.gate) { committing = false; finished = true; Release(); }
                }
            }
            private void Release()
            {
                if (released) return;
                released = true; owner.reserved -= bytes; entry.Pins--; entry.Updating = false;
            }
            public void Dispose() { lock (owner.gate) { if (!committing) Release(); } }
        }
        /// <summary>Measured bytes on disk, excluding active initial writes whose full estimate is reserved.</summary>
        public Task<long> MeasureAsync(CancellationToken cancellation = default) => MutateAsync(() => MeasureStore(cancellation), cancellation);

        private void ExtendCore(Entry entry, string[] added, long reservation, CancellationToken token)
        {
            if (entry.Files.Length + added.Length > MaximumFiles) throw new InvalidDataException("Cache entry file capacity exceeded.");
            var files = new List<OwnedFile>(entry.Files);
            var expected = new HashSet<string>(entry.Files.Select(f => f.Path), Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            long additional = ManifestAllowance;
            foreach (var file in entry.Files) {
                string path = FilePath(entry.Directory, file.Path); CheckLinks(path);
                if (new FileInfo(path).Length != file.Bytes || !Hash(path, token).SequenceEqual(file.Hash)) throw new InvalidDataException("Existing cache content changed during additional write.");
            }
            // Re-read the old manifest separately: new files deliberately make Matches fail until publication.
            var manifest = ReadManifest(entry.Directory);
            if (manifest.Files.Length != entry.Files.Length || manifest.Files.Where((f, i) => f.Path != entry.Files[i].Path || f.Bytes != entry.Files[i].Bytes || !f.Hash.SequenceEqual(entry.Files[i].Hash)).Any())
                throw new InvalidDataException("Cache ownership changed during additional write.");
            foreach (string relative in added) {
                token.ThrowIfCancellationRequested(); string path = FilePath(entry.Directory, relative);
                if (relative == ManifestName || !expected.Add(relative)) throw new InvalidDataException("Additional files must have new, unique paths.");
                CheckLinks(path); long length = new FileInfo(path).Length; additional = checked(additional + length);
                files.Add(new OwnedFile { Path = relative, Bytes = length, Hash = Hash(path, token) });
            }
            if (additional > reservation) throw new IOException("Additional cache payload exceeded its admitted estimate.");
            expected.Add(ManifestName);
            foreach (string path in Walk(entry.Directory, token))
                if (!expected.Remove(Path.GetRelativePath(entry.Directory, path).Replace(Path.DirectorySeparatorChar, '/'))) throw new InvalidDataException("Unexpected files prevent cache ownership publication.");
            if (expected.Count != 0) throw new InvalidDataException("Declared cache files are missing.");
            lock (gate) if (entries.Values.Where(e => !e.Pending).Sum(e => e.Files.Length) + added.Length > MaximumCatalogFiles) throw new IOException("Cache catalog file capacity is full.");
            var replacement = new Entry { Id = entry.Id, Directory = entry.Directory, LastUse = entry.LastUse, Files = files.ToArray() };
            WriteManifest(replacement, token);
            lock (gate) entry.Files = replacement.Files;
        }
        /// <summary>Persists recent pin order. Call from the owning application's background lifecycle workflow.</summary>
        public Task FlushAsync(CancellationToken cancellation = default) => MutateAsync(() => {
            Entry[] snapshot; lock (gate) snapshot = entries.Values.Where(e => !e.Pending).ToArray();
            foreach (var entry in snapshot) { cancellation.ThrowIfCancellationRequested(); if (Matches(entry, cancellation)) WriteManifest(entry, cancellation); }
            return true;
        }, cancellation);

        private async Task<T> MutateAsync<T>(Func<T> work, CancellationToken cancellation)
        {
            lock (gate) { Check(); operations++; }
            bool entered = false;
            try {
                await mutation.WaitAsync(cancellation).ConfigureAwait(false); entered = true;
                return await Task.Run(work, cancellation).ConfigureAwait(false);
            } finally { if (entered) mutation.Release(); lock (gate) operations--; }
        }
        private void CommitCore(Entry entry, string[] paths, CancellationToken token)
        {
            CheckLinks(entry.Directory); if (!Directory.Exists(entry.Directory)) throw new DirectoryNotFoundException(entry.Directory);
            var unique = new HashSet<string>(Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            var files = new List<OwnedFile>(); long bytes = ManifestAllowance;
            foreach (string relative in paths) {
                token.ThrowIfCancellationRequested(); string path = FilePath(entry.Directory, relative);
                if (!unique.Add(relative) || relative == ManifestName) throw new InvalidDataException("Duplicate/reserved cache file path.");
                CheckLinks(path); var info = new FileInfo(path); bytes = checked(bytes + info.Length);
                files.Add(new OwnedFile { Path = relative, Bytes = info.Length, Hash = Hash(path, token) });
            }
            var declared = new HashSet<string>(paths, Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (string path in Walk(entry.Directory, token)) {
                string relative = Path.GetRelativePath(entry.Directory, path).Replace(Path.DirectorySeparatorChar, '/');
                if (!declared.Remove(relative)) throw new InvalidDataException("Unlisted files prevent cache ownership publication.");
            }
            if (declared.Count != 0) throw new InvalidDataException("Declared cache files are missing.");
            if (bytes > entry.Reserved) throw new IOException("Cache payload exceeded its admitted write estimate; it was not registered for eviction.");
            lock (gate) if (entries.Values.Where(e => !e.Pending).Sum(e => e.Files.Length) + files.Count > MaximumCatalogFiles)
                throw new IOException("Cache catalog file capacity is full; trim unused entries before committing another.");
            if (File.Exists(Path.Combine(entry.Directory, ManifestName))) throw new IOException("An entry ownership manifest already exists.");
            entry.Files = files.ToArray(); WriteManifest(entry, token);
            lock (gate) { reserved -= entry.Reserved; entry.Reserved = 0; entry.Pending = false; }
        }
        private void ReleaseWrite(Entry entry)
        {
            entry.Pins--;
            if (entry.Pending) { reserved -= entry.Reserved; entry.Reserved = 0; entries.Remove(entry.Id); }
        }
        private TrimResult TrimCore(long target, CancellationToken token)
        {
            long before = MeasureStore(token), total = before;
            Entry[] candidates; lock (gate) candidates = entries.Values.Where(e => !e.Pending).OrderBy(e => e.LastUse).ThenBy(e => e.Id, StringComparer.Ordinal).ToArray();
            int removed = 0;
            foreach (var entry in candidates) {
                token.ThrowIfCancellationRequested();
                lock (gate) {
                    if (total + reserved <= target) break;
                    if (entry.Pins != 0 || entry.Pending) continue;
                    entry.Evicting = true;
                }
                bool deleted = false;
                try {
                    if (Matches(entry, token)) {
                        token.ThrowIfCancellationRequested();
                        // Cancellation is observed between entries, not halfway through an admitted eviction.
                        foreach (var file in entry.Files) { string path = FilePath(entry.Directory, file.Path); CheckLinks(path); File.Delete(path); }
                        File.Delete(Path.Combine(entry.Directory, ManifestName)); RemoveOwnedEmptyDirectories(entry);
                        deleted = true;
                    }
                } finally { lock (gate) { entry.Evicting = false; if (deleted) entries.Remove(entry.Id); } }
                if (deleted) removed++;
                total = MeasureStore(token);
            }
            bool within; lock (gate) within = total + reserved <= target;
            return new TrimResult { RemainingBytes = total, ReclaimedBytes = before - total, RemovedEntries = removed, WithinTarget = within };
        }
        private bool Matches(Entry entry, CancellationToken token)
        {
            try {
                CheckLinks(entry.Directory); var manifest = ReadManifest(entry.Directory);
                if (manifest.Id != entry.Id || manifest.Files.Length != entry.Files.Length) return false;
                for (int i = 0; i < entry.Files.Length; i++)
                    if (manifest.Files[i].Path != entry.Files[i].Path || manifest.Files[i].Bytes != entry.Files[i].Bytes || !manifest.Files[i].Hash.SequenceEqual(entry.Files[i].Hash)) return false;
                var expected = new HashSet<string>(entry.Files.Select(f => f.Path), StringComparer.Ordinal); expected.Add(ManifestName);
                foreach (string path in Walk(entry.Directory, token)) {
                    string relative = Path.GetRelativePath(entry.Directory, path).Replace(Path.DirectorySeparatorChar, '/');
                    if (!expected.Remove(relative)) return false;
                }
                if (expected.Count != 0) return false;
                foreach (var file in entry.Files) {
                    string path = FilePath(entry.Directory, file.Path);
                    if (new FileInfo(path).Length != file.Bytes || !Hash(path, token).SequenceEqual(file.Hash)) return false;
                }
                return true;
            } catch (IOException) { return false; } catch (InvalidDataException) { return false; } catch (ArgumentException) { return false; }
        }
        private Entry ReadManifest(string directory)
        {
            string id = Path.GetFileName(directory); if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("Unowned cache directory.");
            CheckLinks(directory); string path = Path.Combine(directory, ManifestName); CheckLinks(path);
            using var input = new BinaryReader(File.OpenRead(path), Encoding.UTF8);
            if (input.BaseStream.Length > ManifestAllowance || input.ReadInt32() != Magic || new Guid(input.ReadBytes(16)).ToString("N") != ownerId || new Guid(input.ReadBytes(16)).ToString("N") != id) throw new InvalidDataException("Cache ownership differs.");
            var entry = new Entry { Id = id, Directory = directory, LastUse = input.ReadInt64() }; int count = input.ReadInt32();
            if (count < 0 || count > MaximumFiles || entry.LastUse < DateTime.MinValue.Ticks || entry.LastUse > DateTime.MaxValue.Ticks) throw new InvalidDataException("Invalid cache manifest bounds.");
            var files = new List<OwnedFile>(); var unique = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < count; i++) {
                int length = input.ReadUInt16(); if (length < 1 || length > 240) throw new InvalidDataException("Invalid relative-path length.");
                byte[] encoded = input.ReadBytes(length); if (encoded.Length != length) throw new EndOfStreamException();
                string relative = Encoding.ASCII.GetString(encoded); FilePath(directory, relative); long bytes = input.ReadInt64(); byte[] hash = input.ReadBytes(32);
                if (relative == ManifestName || !unique.Add(relative) || bytes < 0 || hash.Length != 32) throw new InvalidDataException("Invalid owned file.");
                files.Add(new OwnedFile { Path = relative, Bytes = bytes, Hash = hash });
            }
            if (input.BaseStream.Position != input.BaseStream.Length) throw new InvalidDataException("Unexpected cache manifest data.");
            entry.Files = files.ToArray(); return entry;
        }
        private void WriteManifest(Entry entry, CancellationToken token)
        {
            CheckLinks(entry.Directory); string path = Path.Combine(entry.Directory, ManifestName), temporary = path + ".tmp";
            if (File.Exists(temporary)) throw new IOException("Cache manifest staging file already exists.");
            try {
                using (var output = new BinaryWriter(new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None), Encoding.UTF8)) {
                    output.Write(Magic); output.Write(new Guid(ownerId).ToByteArray()); output.Write(new Guid(entry.Id).ToByteArray());
                    long last; lock (gate) last = entry.LastUse; output.Write(last); output.Write(entry.Files.Length);
                    foreach (var file in entry.Files) { output.Write((ushort)file.Path.Length); output.Write(Encoding.ASCII.GetBytes(file.Path)); output.Write(file.Bytes); output.Write(file.Hash); }
                    if (output.BaseStream.Length > ManifestAllowance) throw new InvalidDataException("Cache manifest exceeds its bound.");
                }
                token.ThrowIfCancellationRequested(); CheckLinks(path);
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            } finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        private static string FilePath(string root, string relative)
        {
            if (string.IsNullOrEmpty(relative) || relative.Length > 240) throw new InvalidDataException("Invalid cache-relative path.");
            var parts = relative.Split('/'); if (parts.Length > 32) throw new InvalidDataException("Cache path exceeds its depth bound.");
            foreach (string part in parts) {
                if (part.Length == 0 || part == "." || part == ".." || part.EndsWith(".", StringComparison.Ordinal) || part.Any(c => !(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '.' || c == '_' || c == '-'))) throw new InvalidDataException("Cache-relative path escaped its entry.");
            }
            string path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(prefix, Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new InvalidDataException("Cache-relative path escaped its entry.");
            return path;
        }
        private static void CheckLinks(string path)
        {
            string current = Path.GetFullPath(path);
            while (!string.IsNullOrEmpty(current)) {
                try { if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked cache paths are not managed."); }
                catch (FileNotFoundException) { } catch (DirectoryNotFoundException) { }
                string parent = Path.GetDirectoryName(current); if (parent == current) break; current = parent;
            }
        }
        private static IEnumerable<string> Walk(string root, CancellationToken token, int depth = 0)
        {
            if (depth > 64) throw new InvalidDataException("Cache inventory exceeds its depth bound.");
            CheckLinks(root); if (!Directory.Exists(root)) yield break;
            foreach (string path in Directory.EnumerateFileSystemEntries(root)) {
                token.ThrowIfCancellationRequested(); CheckLinks(path);
                if (Directory.Exists(path)) { foreach (string child in Walk(path, token, depth + 1)) yield return child; }
                else yield return path;
            }
        }
        private static long Measure(string root, CancellationToken token) { long bytes = 0; foreach (string file in Walk(root, token)) bytes = checked(bytes + new FileInfo(file).Length); return bytes; }
        private long MeasureStore(CancellationToken token)
        {
            HashSet<string> pending; lock (gate) pending = new HashSet<string>(entries.Values.Where(e => e.Pending).Select(e => e.Directory), StringComparer.Ordinal);
            long bytes = 0;
            foreach (string path in Directory.EnumerateFileSystemEntries(payloadRoot)) {
                token.ThrowIfCancellationRequested(); CheckLinks(path);
                if (pending.Contains(path)) continue; // Pending payloads already own their full write reservation.
                bytes = checked(bytes + (Directory.Exists(path) ? Measure(path, token) : new FileInfo(path).Length));
            }
            return bytes;
        }
        private static byte[] Hash(string path, CancellationToken token)
        {
            CheckLinks(path); using var input = File.OpenRead(path); using var hash = SHA256.Create(); var buffer = new byte[65536]; int count;
            while ((count = input.Read(buffer, 0, buffer.Length)) > 0) { token.ThrowIfCancellationRequested(); hash.TransformBlock(buffer, 0, count, buffer, 0); }
            hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0); return hash.Hash;
        }
        private static void RemoveOwnedEmptyDirectories(Entry entry)
        {
            var directories = new HashSet<string>(StringComparer.Ordinal) { entry.Directory };
            foreach (var file in entry.Files) {
                string parent = Path.GetDirectoryName(FilePath(entry.Directory, file.Path));
                while (parent != entry.Directory) { directories.Add(parent); parent = Path.GetDirectoryName(parent); }
            }
            foreach (string directory in directories.OrderByDescending(p => p.Length)) {
                CheckLinks(directory);
                if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
            }
        }
        private void Check() { if (disposed) throw new ObjectDisposedException(nameof(FPWorkbenchDiskCacheStore)); }
        private long NextUse()
        {
            if (recencyClock >= DateTime.MaxValue.Ticks) throw new InvalidDataException("Cache recency order exceeds its supported range.");
            recencyClock = Math.Max(DateTime.UtcNow.Ticks, recencyClock + 1); return recencyClock;
        }
        public void Dispose()
        {
            lock (gate) {
                if (disposed) return;
                if (operations != 0 || entries.Values.Any(e => e.Pins != 0 || e.Pending)) throw new InvalidOperationException("Release cache leases and finish operations before closing the store.");
                disposed = true; ownershipLock.Dispose(); mutation.Dispose();
            }
        }
    }
}
