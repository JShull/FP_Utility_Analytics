// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Immutable source revision and disk-backed 64-bit logical-record index. No Editor dependencies.</summary>
    public sealed class FPWorkbenchCsvDataset
    {
        internal FPWorkbenchCacheBinding CacheBinding;
        /// <summary>Retains managed disk products for a view. Legacy, unmanaged datasets return no lease.</summary>
        public IDisposable RetainCache() => CacheBinding?.Retain();
        internal async Task<T> RunCachedAsync<T>(Func<T> work, CancellationToken cancellation)
        {
            using var pin = RetainCache();
            return await Task.Run(work, cancellation).ConfigureAwait(false);
        }
        private const int Magic = 0x31574246;
        private readonly FPWorkbenchCsvProfile profile;
        private readonly string[] columns;
        private readonly long sourceBytes, modifiedTicks;
        public string SourcePath { get; }
        public string CacheDirectory { get; }
        public string DatasetId { get; }
        public long RowCount { get; }
        public int ColumnCount => columns.Length;
        public FPWorkbenchCsvProfile ImportProfile => profile.Copy();
        public string GetColumnName(int column) => columns[column];
        public FPWorkbenchCell Cell(long row, int column)
        {
            if (row < 0 || row >= RowCount || column < 0 || column >= ColumnCount) throw new ArgumentOutOfRangeException();
            return new FPWorkbenchCell(DatasetId, row, column);
        }
        private FPWorkbenchCsvDataset(string path, string directory, string id, long rows, string[] columns,
            FPWorkbenchCsvProfile profile, long bytes, long ticks)
        {
            SourcePath = path; CacheDirectory = directory; DatasetId = id; RowCount = rows;
            this.columns = columns; this.profile = profile; sourceBytes = bytes; modifiedTicks = ticks;
        }

        public static Task<FPWorkbenchCsvDataset> ImportAsync(string sourcePath, string destination,
            FPWorkbenchCsvProfile profile, CancellationToken cancellation = default, IProgress<double> progress = null, IProgress<FPWorkbenchCsvPreview> preview = null)
        {
            var settings = (profile ?? throw new ArgumentNullException(nameof(profile))).Copy();
            string source = Path.GetFullPath(sourcePath), target = Path.GetFullPath(destination);
            return Task.Run(() => Import(source, target, settings, cancellation, progress, preview), cancellation);
        }
        private static FPWorkbenchCsvDataset Import(string path, string target, FPWorkbenchCsvProfile profile,
            CancellationToken cancellation, IProgress<double> progress, IProgress<FPWorkbenchCsvPreview> preview)
        {
            cancellation.ThrowIfCancellationRequested();
            if (Directory.Exists(target) || File.Exists(target)) throw new IOException("Choose a new cache directory. Existing files are not overwritten.");
            string parent = Path.GetDirectoryName(target);
            if (string.IsNullOrEmpty(parent)) throw new IOException("A non-root cache directory is required.");
            Directory.CreateDirectory(parent);
            string staging = Path.Combine(parent, ".fp-workbench-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            bool published = false;
            try
            {
                var info = new FileInfo(path); long bytes = info.Length, ticks = info.LastWriteTimeUtc.Ticks;
                string hash;
                string[] headers; long count = 0;
                using (var source = OpenSource(path))
                {
                    if (preview != null) {
                        SkipBom(source, profile);
                        var prefix = FPWorkbenchCsvPreview.Read(path, new FPWorkbenchCsvRecords(source, profile, cancellation), cancellation);
                        ValidateSource(path, bytes, ticks); preview.Report(prefix);
                        cancellation.ThrowIfCancellationRequested(); source.Position = 0;
                    }
                    hash = Hash(source, cancellation); source.Position = 0;
                    SkipBom(source, profile);
                    var parser = new FPWorkbenchCsvRecords(source, profile, cancellation);
                    headers = parser.Read() ?? throw new InvalidDataException("CSV header is missing.");
                    using var offsets = new BinaryWriter(File.Create(Path.Combine(staging, "rows.fpwi")));
                    offsets.Write(Magic); offsets.Write(1); offsets.Write(0L);
                    long lastProgress = 0;
                    while (true)
                    {
                        long offset = source.Position;
                        var record = parser.Read();
                        if (record == null) break;
                        if (record.Length != headers.Length) throw new InvalidDataException("Column count mismatch at logical data row " + count + ".");
                        offsets.Write(offset); count = checked(count + 1);
                        if (source.Position - lastProgress >= 4 * 1024 * 1024)
                        { progress?.Report(bytes == 0 ? 1 : source.Position / (double)bytes); lastProgress = source.Position; }
                    }
                    offsets.Seek(8, SeekOrigin.Begin); offsets.Write(count);
                }
                ValidateSource(path, bytes, ticks);
                string id;
                using (var sha = SHA256.Create())
                    id = hash + ":" + Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(profile.Delimiter + ":" + profile.EncodingCodePage)));
                string indexHash;
                using (var input = File.OpenRead(Path.Combine(staging, "rows.fpwi"))) indexHash = Hash(input, cancellation);
                using (var metadata = new BinaryWriter(File.Create(Path.Combine(staging, "dataset.fpwm"))))
                {
                    metadata.Write(Magic); metadata.Write(2); metadata.Write(path); metadata.Write(id); metadata.Write(bytes); metadata.Write(ticks); metadata.Write(count);
                    metadata.Write((int)profile.Delimiter); metadata.Write(profile.EncodingCodePage); metadata.Write(profile.MaximumRecordBytes); metadata.Write(profile.MaximumColumns);
                    metadata.Write(headers.Length); foreach (string name in headers) metadata.Write(name);
                    metadata.Write(indexHash);
                }
                cancellation.ThrowIfCancellationRequested();
                Directory.Move(staging, target); published = true; progress?.Report(1);
                return new FPWorkbenchCsvDataset(path, target, id, count, headers, profile, bytes, ticks);
            }
            finally { if (!published && Directory.Exists(staging)) Directory.Delete(staging, true); }
        }

        /// <summary>Reopens an owned cache only after background source-content verification.</summary>
        public static Task<FPWorkbenchCsvDataset> OpenAsync(string directory, CancellationToken cancellation = default)
        {
            string path = Path.GetFullPath(directory);
            return Task.Run(() =>
            {
                using var metadata = new BinaryReader(File.OpenRead(Path.Combine(path, "dataset.fpwm")));
                if (metadata.BaseStream.Length > 16 * 1024 * 1024) throw new InvalidDataException("Metadata exceeds its bound.");
                if (metadata.ReadInt32() != Magic || metadata.ReadInt32() != 2) throw new InvalidDataException("Unsupported workbench cache; rebuild it from the original CSV.");
                string source = metadata.ReadString(), id = metadata.ReadString();
                long bytes = metadata.ReadInt64(), ticks = metadata.ReadInt64(), rows = metadata.ReadInt64();
                var profile = new FPWorkbenchCsvProfile { Delimiter = (char)metadata.ReadInt32(), EncodingCodePage = metadata.ReadInt32(), MaximumRecordBytes = metadata.ReadInt32(), MaximumColumns = metadata.ReadInt32() }.Copy();
                int count = metadata.ReadInt32();
                if (count < 1 || count > profile.MaximumColumns || rows < 0) throw new InvalidDataException("Invalid cache dimensions.");
                var headers = new string[count]; for (int i = 0; i < count; i++) headers[i] = metadata.ReadString();
                string indexHash = metadata.ReadString();
                if (metadata.BaseStream.Position != metadata.BaseStream.Length) throw new InvalidDataException("Unexpected metadata contents.");
                ValidateSource(source, bytes, ticks);
                using (var input = OpenSource(source)) if (!id.StartsWith(Hash(input, cancellation) + ":", StringComparison.Ordinal)) throw new InvalidDataException("Source content changed; rebuild/relink the dataset.");
                using (var index = new BinaryReader(File.OpenRead(Path.Combine(path, "rows.fpwi")))) ValidateIndex(index, rows);
                using (var input = File.OpenRead(Path.Combine(path, "rows.fpwi")))
                    if (Hash(input, cancellation) != indexHash) throw new InvalidDataException("Row index checksum changed; rebuild the cache.");
                return new FPWorkbenchCsvDataset(source, path, id, rows, headers, profile, bytes, ticks);
            }, cancellation);
        }

        public Task<FPWorkbenchPage> ReadPageAsync(long firstRow, int rowCount, int firstColumn, int columnCount, CancellationToken cancellation = default)
        {
            if (firstRow < 0 || firstRow > RowCount || rowCount < 0 || rowCount > 256 || firstColumn < 0 || columnCount < 1 || columnCount > 32 || firstColumn > ColumnCount - columnCount) throw new ArgumentOutOfRangeException("Page must fit source bounds, 256 rows and 32 columns.");
            int count = (int)Math.Min(rowCount, RowCount - firstRow);
            return RunCachedAsync(() =>
            {
                cancellation.ThrowIfCancellationRequested(); ValidateSource(SourcePath, sourceBytes, modifiedTicks);
                using var index = new BinaryReader(File.OpenRead(Path.Combine(CacheDirectory, "rows.fpwi")));
                ValidateIndex(index, RowCount);
                using var source = OpenSource(SourcePath);
                var values = new string[count][]; long characters = 0;
                var parser = new FPWorkbenchCsvRecords(source, profile, cancellation);
                for (int r = 0; r < count; r++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    index.BaseStream.Position = checked(16 + (firstRow + r) * 8); long offset = index.ReadInt64();
                    if (offset < 0 || offset >= source.Length) throw new InvalidDataException("Invalid row offset.");
                    source.Position = offset;
                    var record = parser.Read();
                    if (record == null || record.Length != ColumnCount) throw new InvalidDataException("Source/index row mismatch.");
                    values[r] = new string[columnCount];
                    for (int c = 0; c < columnCount; c++)
                    {
                        string text = record[firstColumn + c]; characters += text.Length;
                        if (characters > 2 * 1024 * 1024) throw new InvalidDataException("Page text exceeds 4 MiB; request fewer rows/columns.");
                        values[r][c] = text;
                    }
                }
                ValidateSource(SourcePath, sourceBytes, modifiedTicks);
                return new FPWorkbenchPage(DatasetId, firstRow, firstColumn, values);
            }, cancellation);
        }
        internal FileStream OpenReader()
        {
            ValidateSource(SourcePath, sourceBytes, modifiedTicks); return OpenSource(SourcePath);
        }
        internal FileStream OpenReaderAt(long row)
        {
            if (row < 0 || row > RowCount) throw new ArgumentOutOfRangeException(nameof(row));
            using var index = new BinaryReader(File.OpenRead(Path.Combine(CacheDirectory, "rows.fpwi")));
            ValidateIndex(index, RowCount);
            var source = OpenReader();
            try
            {
                if (row == RowCount) source.Position = source.Length;
                else
                {
                    index.BaseStream.Position = checked(16 + row * 8); long offset = index.ReadInt64();
                    if (offset < 0 || offset >= source.Length) throw new InvalidDataException("Invalid row offset.");
                    source.Position = offset;
                }
                return source;
            }
            catch { source.Dispose(); throw; }
        }
        internal void ValidateUnchanged() => ValidateSource(SourcePath, sourceBytes, modifiedTicks);
        internal FPWorkbenchCsvRecords Parser(Stream source, CancellationToken cancellation) => new FPWorkbenchCsvRecords(source, profile, cancellation);
        internal void SkipHeader(Stream source, CancellationToken cancellation) { SkipBom(source, profile); Parser(source, cancellation).Read(); }
        private static FileStream OpenSource(string path) => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
        private static void SkipBom(Stream source, FPWorkbenchCsvProfile profile)
        {
            int a = source.ReadByte(), b = source.ReadByte(), c = source.ReadByte();
            source.Position = a == 239 && b == 187 && c == 191 && profile.EncodingCodePage == 65001 ? 3 : 0;
        }
        private static void ValidateIndex(BinaryReader index, long rows)
        {
            if (index.ReadInt32() != Magic || index.ReadInt32() != 1 || index.ReadInt64() != rows || index.BaseStream.Length != checked(16 + rows * 8)) throw new InvalidDataException("Invalid row index.");
        }
        private static void ValidateSource(string path, long bytes, long ticks)
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != bytes || info.LastWriteTimeUtc.Ticks != ticks) throw new InvalidDataException("Source changed or is missing; rebuild/relink the dataset.");
        }
        private static string Hash(Stream source, CancellationToken cancellation)
        {
            using var sha = SHA256.Create(); var buffer = new byte[64 * 1024]; int count;
            while ((count = source.Read(buffer, 0, buffer.Length)) > 0) { cancellation.ThrowIfCancellationRequested(); sha.TransformBlock(buffer, 0, count, buffer, 0); }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0); return Hex(sha.Hash);
        }
        private static string Hex(byte[] value) => BitConverter.ToString(value).Replace("-", "").ToLowerInvariant();
    }
}

