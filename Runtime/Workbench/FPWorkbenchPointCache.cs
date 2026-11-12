// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    public readonly struct FPWorkbenchSpatialPoint
    {
        public readonly double X, Y, Z;
        public readonly long Row;
        public FPWorkbenchSpatialPoint(double x, double y, double z, long row) { X = x; Y = y; Z = z; Row = row; }
    }
    public readonly struct FPWorkbenchSpatialBounds
    {
        public readonly double MinimumX, MaximumX, MinimumY, MaximumY, MinimumZ, MaximumZ;
        public FPWorkbenchSpatialBounds(double minX, double maxX, double minY, double maxY, double minZ = 0, double maxZ = 0)
        {
            if (!Finite(minX) || !Finite(maxX) || !Finite(minY) || !Finite(maxY) || !Finite(minZ) || !Finite(maxZ) || minX > maxX || minY > maxY || minZ > maxZ)
                throw new ArgumentException("Spatial bounds must be finite and ordered.");
            MinimumX = minX; MaximumX = maxX; MinimumY = minY; MaximumY = maxY; MinimumZ = minZ; MaximumZ = maxZ;
        }
        private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
        internal bool Contains(FPWorkbenchSpatialPoint p) => p.X >= MinimumX && p.X <= MaximumX && p.Y >= MinimumY && p.Y <= MaximumY && p.Z >= MinimumZ && p.Z <= MaximumZ;
        internal bool Intersects(FPWorkbenchSpatialBounds b) => MinimumX <= b.MaximumX && MaximumX >= b.MinimumX && MinimumY <= b.MaximumY && MaximumY >= b.MinimumY && MinimumZ <= b.MaximumZ && MaximumZ >= b.MinimumZ;
    }
    public sealed class FPWorkbenchPointView
    {
        public string DatasetId { get; internal set; }
        public string FormulaFingerprint { get; internal set; }
        public string CorrectionFingerprint { get; internal set; }
        public FPWorkbenchSpatialBounds Bounds { get; internal set; }
        public IReadOnlyList<FPWorkbenchSpatialPoint> Points { get; internal set; }
        public long MatchingCount { get; internal set; }
        public long RecordsRead { get; internal set; }
        public long BlocksRead { get; internal set; }
        public bool Decimated => MatchingCount > Points.Count;
    }

    /// <summary>Unsorted 2D/3D double coordinates with source-row identities and disk-resident block bounds.</summary>
    public sealed class FPWorkbenchPointCache
    {
        private const int Magic = 0x31504346, BlockPoints = 4096, PointBytes = 32, BlockBytes = 64;
        private readonly FPWorkbenchCsvDataset dataset;
        public string DirectoryPath { get; private set; }
        public string FormulaFingerprint { get; private set; }
        public string CorrectionFingerprint { get; private set; }
        public long ValidCount { get; private set; }
        public long InvalidCount => dataset.RowCount - ValidCount;
        public FPWorkbenchSpatialBounds Bounds { get; private set; }
        public bool HasPoints => ValidCount != 0;
        public bool HasZ { get; private set; }
        private FPWorkbenchPointCache(FPWorkbenchCsvDataset dataset) { this.dataset = dataset; }

        public static Task<FPWorkbenchPointCache> BuildAsync(FPWorkbenchCsvDataset dataset, int[] columns,
            FPWorkbenchFormulaProgram calculations = null, FPWorkbenchChangeSnapshot changes = null,
            FPWorkbenchInvalidPolicy invalidPolicy = FPWorkbenchInvalidPolicy.Reject, CancellationToken cancellation = default)
        {
            if (dataset == null || columns == null) throw new ArgumentNullException();
            if (columns.Length != 2 && columns.Length != 3 || !Enum.IsDefined(typeof(FPWorkbenchInvalidPolicy), invalidPolicy)) throw new ArgumentException("Choose X/Y or X/Y/Z columns and an invalid-value policy.");
            if (changes != null && changes.DatasetId != dataset.DatasetId) throw new ArgumentException("Point corrections belong to another source.");
            var schema = new FPWorkbenchTableView(dataset, calculations); var selected = (int[])columns.Clone(); var ids = new string[selected.Length]; bool derived = false;
            for (int c = 0; c < selected.Length; c++) { ids[c] = schema.ColumnId(selected[c]); derived |= schema.IsCalculated(selected[c]); }
            string formula = derived || calculations?.TypedSchemaFingerprint != null && calculations.TypedSchemaFingerprint != "none" ? calculations.Fingerprint : "none", correction = changes?.Fingerprint ?? "original";
            var program = calculations ?? FPWorkbenchFormulaProgram.Compile(dataset, new FPWorkbenchFormulaSet { DatasetId = dataset.DatasetId });
            string key;
            using (var buffer = new MemoryStream())
            {
                using (var writer = new BinaryWriter(buffer, Encoding.UTF8, true))
                { writer.Write(Magic); writer.Write(dataset.DatasetId); writer.Write(formula); writer.Write(correction); writer.Write((int)invalidPolicy); writer.Write(selected.Length); foreach (string id in ids) writer.Write(id); }
                using var hash = SHA256.Create(); key = Hex(hash.ComputeHash(buffer.ToArray()));
            }
            return Task.Run(() => Build(dataset, selected, program, changes, invalidPolicy, formula, correction, key, cancellation), cancellation);
        }
        private static FPWorkbenchPointCache Build(FPWorkbenchCsvDataset data, int[] columns, FPWorkbenchFormulaProgram program,
            FPWorkbenchChangeSnapshot changes, FPWorkbenchInvalidPolicy policy, string formula, string correction, string key, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); data.ValidateUnchanged();
            string target = Path.Combine(data.CacheDirectory, "spatial-" + key);
            if (Directory.Exists(target)) return Open(data, target, key, formula, correction, columns.Length == 3, token);
            string staging = Path.Combine(data.CacheDirectory, ".spatial-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(staging);
            try
            {
                long count = 0, blockStart = 0; var block = new Extent(); var total = new Extent();
                using (var source = data.OpenReaderAt(0))
                using (var points = new BinaryWriter(File.Create(Path.Combine(staging, "points.bin"))))
                using (var blocks = new BinaryWriter(File.Create(Path.Combine(staging, "blocks.bin"))))
                {
                    var parser = data.Parser(source, token); var evaluator = program.CreateEvaluator(changes);
                    for (long row = 0; row < data.RowCount; row++)
                    {
                        token.ThrowIfCancellationRequested(); var values = parser.Read();
                        if (values == null || values.Length != data.ColumnCount) throw new InvalidDataException("Point source differs from its index.");
                        evaluator.SetRow(values, row);
                        FPWorkbenchFormulaValue Value(int c) => c < data.ColumnCount ? evaluator.Source(c) : evaluator.Derived(c - data.ColumnCount);
                        var x = Value(columns[0]); var y = Value(columns[1]); var z = columns.Length == 3 ? Value(columns[2]) : new FPWorkbenchFormulaValue(0);
                        if (!x.IsValid || !y.IsValid || !z.IsValid)
                        { if (policy == FPWorkbenchInvalidPolicy.Reject) throw new InvalidDataException("Invalid spatial coordinate at source row " + row + ": " + x.Status + "/" + y.Status + "/" + z.Status); continue; }
                        var point = new FPWorkbenchSpatialPoint(x.Number, y.Number, z.Number, row);
                        points.Write(point.X); points.Write(point.Y); points.Write(point.Z); points.Write(point.Row); count++;
                        block.Add(point); total.Add(point);
                        if (count - blockStart == BlockPoints) { WriteBlock(blocks, block.Bounds, blockStart, count - blockStart); blockStart = count; block = new Extent(); }
                    }
                    if (count != blockStart) WriteBlock(blocks, block.Bounds, blockStart, count - blockStart);
                }
                data.ValidateUnchanged();
                string pointHash = Hash(Path.Combine(staging, "points.bin"), token), blockHash = Hash(Path.Combine(staging, "blocks.bin"), token);
                using (var metadata = new BinaryWriter(File.Create(Path.Combine(staging, "metadata.bin"))))
                {
                    metadata.Write(Magic); metadata.Write(key); metadata.Write(data.DatasetId); metadata.Write(data.RowCount); metadata.Write(count);
                    WriteBounds(metadata, total.Bounds); metadata.Write(pointHash); metadata.Write(blockHash);
                }
                token.ThrowIfCancellationRequested(); data.ValidateUnchanged();
                try { Directory.Move(staging, target); } catch (IOException) when (Directory.Exists(target)) { }
                return Open(data, target, key, formula, correction, columns.Length == 3, token);
            }
            finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
        }
        private static FPWorkbenchPointCache Open(FPWorkbenchCsvDataset data, string directory, string key, string formula, string correction, bool z, CancellationToken token)
        {
            using var reader = new BinaryReader(File.OpenRead(Path.Combine(directory, "metadata.bin")));
            if (reader.BaseStream.Length > 1024 || reader.ReadInt32() != Magic || reader.ReadString() != key || reader.ReadString() != data.DatasetId || reader.ReadInt64() != data.RowCount)
                throw new InvalidDataException("Invalid spatial cache metadata.");
            long count = reader.ReadInt64(); var bounds = ReadBounds(reader); string pointsHash = reader.ReadString(), blocksHash = reader.ReadString();
            if (count < 0 || count > data.RowCount || count > long.MaxValue / PointBytes || reader.BaseStream.Position != reader.BaseStream.Length ||
                new FileInfo(Path.Combine(directory, "points.bin")).Length != count * PointBytes ||
                new FileInfo(Path.Combine(directory, "blocks.bin")).Length != (count / BlockPoints + (count % BlockPoints == 0 ? 0 : 1)) * BlockBytes ||
                Hash(Path.Combine(directory, "points.bin"), token) != pointsHash || Hash(Path.Combine(directory, "blocks.bin"), token) != blocksHash)
                throw new InvalidDataException("Spatial cache payload failed verification.");
            data.ValidateUnchanged();
            return new FPWorkbenchPointCache(data) { DirectoryPath = directory, FormulaFingerprint = formula, CorrectionFingerprint = correction, HasZ = z, ValidCount = count, Bounds = bounds };
        }
        public Task<FPWorkbenchPointView> QueryAsync(FPWorkbenchSpatialBounds bounds, int maximumPoints = 10000, CancellationToken cancellation = default)
        {
            if (maximumPoints < 1 || maximumPoints > 65536) throw new ArgumentOutOfRangeException(nameof(maximumPoints));
            return Task.Run(() =>
            {
                dataset.ValidateUnchanged(); var heap = new Samples(maximumPoints); long matches = 0, records = 0, blockCount = 0;
                using var blocks = new BinaryReader(File.OpenRead(Path.Combine(DirectoryPath, "blocks.bin")));
                using var points = new BinaryReader(File.OpenRead(Path.Combine(DirectoryPath, "points.bin")));
                long expected = 0;
                while (blocks.BaseStream.Position < blocks.BaseStream.Length)
                {
                    cancellation.ThrowIfCancellationRequested(); var region = ReadBounds(blocks); long start = blocks.ReadInt64(), count = blocks.ReadInt64(); blockCount++;
                    if (start != expected || count < 1 || count > BlockPoints || start > ValidCount - count) throw new InvalidDataException("Invalid spatial block mapping.");
                    expected += count; if (!bounds.Intersects(region)) continue;
                    points.BaseStream.Position = start * PointBytes;
                    for (long i = 0; i < count; i++)
                    {
                        cancellation.ThrowIfCancellationRequested(); var point = new FPWorkbenchSpatialPoint(points.ReadDouble(), points.ReadDouble(), points.ReadDouble(), points.ReadInt64()); records++;
                        if (bounds.Contains(point)) { matches++; heap.Add(point); }
                    }
                }
                if (expected != ValidCount) throw new InvalidDataException("Incomplete spatial block mapping.");
                cancellation.ThrowIfCancellationRequested(); dataset.ValidateUnchanged();
                return new FPWorkbenchPointView { DatasetId = dataset.DatasetId, FormulaFingerprint = FormulaFingerprint, CorrectionFingerprint = CorrectionFingerprint,
                    Bounds = bounds, Points = Array.AsReadOnly(heap.Finish()), MatchingCount = matches, RecordsRead = records, BlocksRead = blockCount };
            }, cancellation);
        }
        private struct Extent
        {
            private bool any; private double minX, maxX, minY, maxY, minZ, maxZ;
            public void Add(FPWorkbenchSpatialPoint p)
            {
                if (!any) { minX = maxX = p.X; minY = maxY = p.Y; minZ = maxZ = p.Z; any = true; return; }
                minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X); minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y); minZ = Math.Min(minZ, p.Z); maxZ = Math.Max(maxZ, p.Z);
            }
            public FPWorkbenchSpatialBounds Bounds => new FPWorkbenchSpatialBounds(minX, maxX, minY, maxY, minZ, maxZ);
        }
        // Lowest deterministic source-row hashes win, independent of view size or block traversal; no unbounded reservoir.
        private sealed class Samples
        {
            private readonly FPWorkbenchSpatialPoint[] points; private readonly ulong[] ranks; private int count;
            public Samples(int capacity) { points = new FPWorkbenchSpatialPoint[capacity]; ranks = new ulong[capacity]; }
            private static ulong Rank(long row)
            { unchecked { ulong v = (ulong)row + 0x9e3779b97f4a7c15UL; v = (v ^ (v >> 30)) * 0xbf58476d1ce4e5b9UL; v = (v ^ (v >> 27)) * 0x94d049bb133111ebUL; return v ^ (v >> 31); } }
            private bool Greater(int a, int b) => ranks[a] > ranks[b] || ranks[a] == ranks[b] && points[a].Row > points[b].Row;
            private void Swap(int a, int b) { var p = points[a]; points[a] = points[b]; points[b] = p; ulong r = ranks[a]; ranks[a] = ranks[b]; ranks[b] = r; }
            public void Add(FPWorkbenchSpatialPoint point)
            {
                ulong rank = Rank(point.Row);
                if (count < points.Length)
                {
                    int index = count++; points[index] = point; ranks[index] = rank;
                    while (index > 0) { int parent = (index - 1) / 2; if (!Greater(index, parent)) break; Swap(index, parent); index = parent; }
                }
                else
                {
                    if (rank > ranks[0] || rank == ranks[0] && point.Row >= points[0].Row) return;
                    points[0] = point; ranks[0] = rank; int index = 0;
                    while (index * 2 + 1 < count)
                    { int next = index * 2 + 1; if (next + 1 < count && Greater(next + 1, next)) next++; if (!Greater(next, index)) break; Swap(next, index); index = next; }
                }
            }
            public FPWorkbenchSpatialPoint[] Finish()
            { var result = new FPWorkbenchSpatialPoint[count]; Array.Copy(points, result, count); Array.Sort(result, (a, b) => a.Row.CompareTo(b.Row)); return result; }
        }
        private static void WriteBounds(BinaryWriter writer, FPWorkbenchSpatialBounds b)
        { writer.Write(b.MinimumX); writer.Write(b.MaximumX); writer.Write(b.MinimumY); writer.Write(b.MaximumY); writer.Write(b.MinimumZ); writer.Write(b.MaximumZ); }
        private static FPWorkbenchSpatialBounds ReadBounds(BinaryReader reader) => new FPWorkbenchSpatialBounds(reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble());
        private static void WriteBlock(BinaryWriter writer, FPWorkbenchSpatialBounds bounds, long start, long count) { WriteBounds(writer, bounds); writer.Write(start); writer.Write(count); }
        private static string Hex(byte[] hash) => BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        private static string Hash(string path, CancellationToken token)
        {
            using var hash = SHA256.Create(); using var input = File.OpenRead(path); var buffer = new byte[65536]; int count;
            while ((count = input.Read(buffer, 0, buffer.Length)) != 0) { token.ThrowIfCancellationRequested(); hash.TransformBlock(buffer, 0, count, buffer, 0); }
            hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0); return Hex(hash.Hash);
        }
    }
}
