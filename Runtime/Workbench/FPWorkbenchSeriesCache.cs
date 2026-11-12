// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Persisted sorted-X points plus a 256x4 summary pyramid. Queries never reparse CSV.</summary>
    public sealed class FPWorkbenchSeriesCache
    {
        private const int Magic = 0x31535046, PointBytes = 32, BinBytes = 104;
        private readonly FPWorkbenchCsvDataset dataset;
        private readonly FPWorkbenchSeriesBinding binding;
        private readonly int levels;
        public string DirectoryPath { get; }
        public double FirstX { get; }
        public double LastX { get; }
        public long OriginUtcTicks { get; }
        public long RowCount => dataset.RowCount;
        public string YName { get; private set; }
        public string XName { get; private set; }
        public string FormulaFingerprint { get; private set; }
        public string CorrectionFingerprint { get; private set; }
        public FPWorkbenchSeriesBinding Binding => binding.Copy(dataset.ColumnCount);
        private FPWorkbenchSeriesCache(FPWorkbenchCsvDataset dataset, string directory, FPWorkbenchSeriesBinding binding,
            int levels, double first, double last, long origin)
        { this.dataset = dataset; DirectoryPath = directory; this.binding = binding; this.levels = levels; FirstX = first; LastX = last; OriginUtcTicks = origin; }

        public static Task<FPWorkbenchSeriesCache> BuildAsync(FPWorkbenchCsvDataset dataset, FPWorkbenchSeriesBinding binding,
            CancellationToken cancellation = default, FPWorkbenchChangeSnapshot changes = null, FPWorkbenchFormulaProgram calculations = null)
        {
            if (dataset == null || binding == null) throw new ArgumentNullException();
            var settings = binding.Copy(dataset.ColumnCount);
            if (changes != null && changes.DatasetId != dataset.DatasetId) throw new ArgumentException("Series corrections belong to another source revision.");
            if ((settings.HasDerivedY || settings.HasDerivedX) && (calculations == null || calculations.DatasetId != dataset.DatasetId)) throw new ArgumentException("Series calculations belong to another source revision or are missing.");
            int derived = settings.ResolveDerivedY(calculations);
            int derivedX = settings.ResolveDerivedX(calculations);
            return Task.Run(() => Build(dataset, settings, cancellation, changes, calculations, derived, derivedX), cancellation);
        }
        private static FPWorkbenchSeriesCache Build(FPWorkbenchCsvDataset dataset, FPWorkbenchSeriesBinding settings, CancellationToken token, FPWorkbenchChangeSnapshot changes, FPWorkbenchFormulaProgram calculations, int derived, int derivedX)
        {
            if (dataset.RowCount == 0) throw new InvalidDataException("A plot requires at least one source row.");
            string formula = derived < 0 && derivedX < 0 ? "none" : calculations.Fingerprint;
            string correction = changes?.Fingerprint ?? "original";
            FPWorkbenchSeriesCache Qualify(FPWorkbenchSeriesCache value)
            {
                value.YName = derived < 0 ? dataset.GetColumnName(settings.YColumn) : calculations.Name(derived);
                value.XName = derivedX >= 0 ? calculations.Name(derivedX) : settings.XMode == FPWorkbenchXMode.Row ? "Row index" : dataset.GetColumnName(settings.XColumn);
                value.FormulaFingerprint = formula; value.CorrectionFingerprint = correction; return value;
            }
            string key;
            using (var buffer = new MemoryStream())
            {
                using (var writer = new BinaryWriter(buffer, Encoding.UTF8, true))
                {
                    writer.Write(dataset.DatasetId); writer.Write((int)settings.XMode); writer.Write(settings.XColumn); writer.Write(settings.YColumn);
                    writer.Write(settings.DateColumn); writer.Write(settings.UtcFormat ?? ""); writer.Write(settings.MaximumGap);
                    writer.Write(changes?.Fingerprint ?? "original");
                    writer.Write(settings.YDerivedId ?? ""); writer.Write(formula);
                    writer.Write(settings.XDerivedId ?? "");
                }
                using var sha = SHA256.Create(); key = Hex(sha.ComputeHash(buffer.ToArray()));
            }
            string target = Path.Combine(dataset.CacheDirectory, "series-" + key);
            if (Directory.Exists(target)) return Qualify(Open(dataset, target, settings, token));
            string staging = Path.Combine(dataset.CacheDirectory, ".series-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging); bool published = false;
            try
            {
                long origin = 0; double first = 0, previous = double.NaN;
                using (var source = dataset.OpenReader())
                using (var points = new BinaryWriter(File.Create(Path.Combine(staging, "points.bin"))))
                using (var level = new BinaryWriter(File.Create(LevelPath(staging, 0))))
                {
                    dataset.SkipHeader(source, token); var parser = dataset.Parser(source, token);
                    var evaluator = derived < 0 && derivedX < 0 ? null : calculations.CreateEvaluator(changes);
                    FPWorkbenchSeriesBin aggregate = default;
                    for (long row = 0; row < dataset.RowCount; row++)
                    {
                        var values = parser.Read();
                        if (values == null || values.Length != dataset.ColumnCount) throw new InvalidDataException("Source row changed during series construction.");
                        double y = double.NaN, computedX = double.NaN;
                        if (evaluator != null)
                        {
                            evaluator.SetRow(values, row);
                            if (derived >= 0) { var computed = evaluator.Derived(derived); if (computed.IsValid) y = computed.Number; }
                            if (derivedX >= 0)
                            {
                                var coordinate = evaluator.Derived(derivedX);
                                if (!coordinate.IsValid) throw new InvalidDataException("Invalid calculated X at logical row " + row + ": " + coordinate.Status);
                                computedX = coordinate.Number;
                            }
                        }
                        if (changes != null)
                        {
                            void Apply(int column)
                            {
                                if (changes.TryGet(new FPWorkbenchCell(dataset.DatasetId, row, column), values[column], out string effective, out _)) values[column] = effective;
                            }
                            int physicalY = derived < 0 ? settings.YColumn : -1;
                            if (physicalY >= 0) Apply(physicalY);
                            if (derivedX < 0 && settings.XMode != FPWorkbenchXMode.Row && settings.XColumn != physicalY) Apply(settings.XColumn);
                            if (settings.XMode == FPWorkbenchXMode.UtcDateTime && settings.DateColumn != physicalY && settings.DateColumn != settings.XColumn) Apply(settings.DateColumn);
                        }
                        double x = derivedX >= 0 ? computedX : settings.ReadX(values, row, ref origin);
                        if (row > 0 && x < previous) throw new InvalidDataException("X decreases at logical row " + row + ". A sorted/segmented mapping is required; samples were not reordered.");
                        if (row == 0) first = x;
                        if (!FPWorkbenchSeriesBinding.Finite(x - first)) throw new InvalidDataException("X extent exceeds finite double range at logical row " + row + ".");
                        if (derived < 0 && (!double.TryParse(values[settings.YColumn], NumberStyles.Float, CultureInfo.InvariantCulture, out y) || !FPWorkbenchSeriesBinding.Finite(y))) y = double.NaN;
                        bool gap = row > 0 && settings.MaximumGap > 0 && x - previous > settings.MaximumGap;
                        var point = new FPWorkbenchSeriesPoint(x, y, row, gap);
                        WritePoint(points, point); aggregate.Merge(FPWorkbenchSeriesBin.Point(row, point));
                        if ((row + 1) % 256 == 0 || row + 1 == dataset.RowCount) { WriteBin(level, aggregate); aggregate = default; }
                        previous = x;
                    }
                }
                int count = 1; long bins = Ceiling(dataset.RowCount, 256);
                while (bins > 1)
                {
                    token.ThrowIfCancellationRequested();
                    using var input = new BinaryReader(File.OpenRead(LevelPath(staging, count - 1)));
                    using var output = new BinaryWriter(File.Create(LevelPath(staging, count)));
                    FPWorkbenchSeriesBin merged = default;
                    for (long i = 0; i < bins; i++)
                    {
                        token.ThrowIfCancellationRequested(); merged.Merge(ReadBin(input));
                        if ((i + 1) % 4 == 0 || i + 1 == bins) { WriteBin(output, merged); merged = default; }
                    }
                    bins = Ceiling(bins, 4); count++;
                }
                dataset.ValidateUnchanged();
                using (var metadata = new BinaryWriter(File.Create(Path.Combine(staging, "series.bin"))))
                {
                    metadata.Write(Magic); metadata.Write(1); metadata.Write(dataset.DatasetId); metadata.Write(dataset.RowCount); metadata.Write(count);
                    metadata.Write(first); metadata.Write(previous); metadata.Write(origin);
                    metadata.Write(Hash(Path.Combine(staging, "points.bin"), token));
                    for (int i = 0; i < count; i++) metadata.Write(Hash(LevelPath(staging, i), token));
                }
                token.ThrowIfCancellationRequested();
                try { Directory.Move(staging, target); published = true; }
                catch (IOException) when (Directory.Exists(target)) { return Qualify(Open(dataset, target, settings, token)); }
                return Qualify(new FPWorkbenchSeriesCache(dataset, target, settings, count, first, previous, origin));
            }
            finally { if (!published && Directory.Exists(staging)) Directory.Delete(staging, true); }
        }
        private static FPWorkbenchSeriesCache Open(FPWorkbenchCsvDataset dataset, string target, FPWorkbenchSeriesBinding settings, CancellationToken token)
        {
            dataset.ValidateUnchanged();
            using var metadata = new BinaryReader(File.OpenRead(Path.Combine(target, "series.bin")));
            if (metadata.BaseStream.Length > 16384 || metadata.ReadInt32() != Magic || metadata.ReadInt32() != 1 || metadata.ReadString() != dataset.DatasetId || metadata.ReadInt64() != dataset.RowCount) throw new InvalidDataException("Invalid series cache identity/version.");
            int levels = metadata.ReadInt32();
            if (levels < 1 || levels > 32) throw new InvalidDataException("Invalid pyramid levels.");
            double first = metadata.ReadDouble(), last = metadata.ReadDouble(); long origin = metadata.ReadInt64();
            if (!FPWorkbenchSeriesBinding.Finite(first) || !FPWorkbenchSeriesBinding.Finite(last) || last < first) throw new InvalidDataException("Invalid series extent.");
            string points = Path.Combine(target, "points.bin");
            if (new FileInfo(points).Length != checked(dataset.RowCount * PointBytes) || Hash(points, token) != metadata.ReadString()) throw new InvalidDataException("Series point checksum/size mismatch.");
            long count = Ceiling(dataset.RowCount, 256);
            for (int i = 0; i < levels; i++)
            {
                string path = LevelPath(target, i);
                if (new FileInfo(path).Length != checked(count * BinBytes) || Hash(path, token) != metadata.ReadString()) throw new InvalidDataException("Series summary checksum/size mismatch.");
                count = Ceiling(count, 4);
            }
            if (metadata.BaseStream.Position != metadata.BaseStream.Length) throw new InvalidDataException("Unexpected series metadata.");
            return new FPWorkbenchSeriesCache(dataset, target, settings, levels, first, last, origin);
        }

        public Task<FPWorkbenchSeriesView> QueryAsync(double startX, double endX, int desiredBins = 512, CancellationToken cancellation = default)
        {
            if (!FPWorkbenchSeriesBinding.Finite(startX) || !FPWorkbenchSeriesBinding.Finite(endX) || endX <= startX || desiredBins < 1 || desiredBins > 4096) throw new ArgumentException("A finite increasing range and 1–4096 bins are required.");
            return Task.Run(() => Query(startX, endX, desiredBins, cancellation), cancellation);
        }
        private FPWorkbenchSeriesView Query(double start, double end, int bins, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); dataset.ValidateUnchanged();
            using var points = new BinaryReader(File.OpenRead(Path.Combine(DirectoryPath, "points.bin")));
            long reads = 0;
            long first = Bound(points, start, false, ref reads), stop = Bound(points, end, true, ref reads);
            var result = new List<FPWorkbenchSeriesBin>(bins + 2);
            var inputs = new BinaryReader[levels];
            try
            {
                if (stop - first <= bins * 2L)
                    for (long i = first; i < stop; i++) { token.ThrowIfCancellationRequested(); result.Add(FPWorkbenchSeriesBin.Point(i, ReadAt(points, i))); reads++; }
                else
                {
                    long span = 256; int level = 0;
                    while (level + 1 < levels && span <= (stop - first) / bins / 4) { span *= 4; level++; }
                    for (long i = first; i < stop;)
                    {
                        token.ThrowIfCancellationRequested();
                        long block = i / span, blockStart = block * span, blockEnd = Math.Min(RowCount, checked(blockStart + span));
                        long partEnd = Math.Min(stop, blockEnd);
                        FPWorkbenchSeriesBin value;
                        if (i == blockStart && partEnd == blockEnd)
                        { value = ReadLevel(inputs, level, block, token); reads++; }
                        else value = ReadRange(points, inputs, i, partEnd, token, ref reads);
                        result.Add(value); i = partEnd;
                    }
                }
            }
            finally { foreach (var reader in inputs) reader?.Dispose(); }
            var view = new FPWorkbenchSeriesView { DatasetId = dataset.DatasetId, FormulaFingerprint = FormulaFingerprint, CorrectionFingerprint = CorrectionFingerprint, Bins = result.ToArray(), StartX = start, EndX = end, OriginUtcTicks = OriginUtcTicks, RecordsRead = reads };
            foreach (var bin in view.Bins) if (bin.Count > 0)
            {
                if (double.IsNaN(view.Minimum) || bin.Minimum < view.Minimum) view.Minimum = bin.Minimum;
                if (double.IsNaN(view.Maximum) || bin.Maximum > view.Maximum) view.Maximum = bin.Maximum;
            }
            dataset.ValidateUnchanged(); return view;
        }
        private FPWorkbenchSeriesBin ReadRange(BinaryReader points, BinaryReader[] inputs, long first, long end, CancellationToken token, ref long reads)
        {
            FPWorkbenchSeriesBin value = default;
            while (first < end)
            {
                token.ThrowIfCancellationRequested(); long span = 256; int level = -1;
                for (int i = 0; i < levels && span <= end - first; i++, span = checked(span * 4)) if (first % span == 0) level = i;
                if (level < 0) { value.Merge(FPWorkbenchSeriesBin.Point(first, ReadAt(points, first))); first++; }
                else
                {
                    long width = 256; for (int i = 0; i < level; i++) width *= 4;
                    var bin = ReadLevel(inputs, level, first / width, token); value.Merge(bin); first = bin.EndIndex;
                }
                reads++;
            }
            return value;
        }
        private FPWorkbenchSeriesBin ReadLevel(BinaryReader[] inputs, int level, long index, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var reader = inputs[level] ?? (inputs[level] = new BinaryReader(File.OpenRead(LevelPath(DirectoryPath, level))));
            reader.BaseStream.Position = checked(index * BinBytes); return ReadBin(reader);
        }
        private long Bound(BinaryReader points, double x, bool upper, ref long reads)
        {
            long low = 0, high = RowCount;
            while (low < high)
            { long middle = low + (high - low) / 2; double value = ReadAt(points, middle).X; reads++; if (value < x || (upper && value == x)) low = middle + 1; else high = middle; }
            return low;
        }
        private static FPWorkbenchSeriesPoint ReadAt(BinaryReader reader, long index)
        {
            reader.BaseStream.Position = checked(index * PointBytes);
            double x = reader.ReadDouble(), y = reader.ReadDouble(); long row = reader.ReadInt64(); bool gap = reader.ReadInt32() != 0; reader.ReadInt32();
            return new FPWorkbenchSeriesPoint(x, y, row, gap);
        }
        private static void WritePoint(BinaryWriter writer, FPWorkbenchSeriesPoint point)
        { writer.Write(point.X); writer.Write(point.Y); writer.Write(point.Row); writer.Write(point.GapBefore ? 1 : 0); writer.Write(0); }
        private static void WriteBin(BinaryWriter writer, FPWorkbenchSeriesBin b)
        {
            writer.Write(b.FirstIndex); writer.Write(b.EndIndex); writer.Write(b.MinimumRow); writer.Write(b.MaximumRow); writer.Write(b.Count); writer.Write(b.InvalidCount); writer.Write(b.GapCount);
            writer.Write(b.FirstX); writer.Write(b.LastX); writer.Write(b.Minimum); writer.Write(b.Maximum); writer.Write(b.FirstY); writer.Write(b.LastY);
        }
        private static FPWorkbenchSeriesBin ReadBin(BinaryReader reader) => new FPWorkbenchSeriesBin
        {
            FirstIndex = reader.ReadInt64(), EndIndex = reader.ReadInt64(), MinimumRow = reader.ReadInt64(), MaximumRow = reader.ReadInt64(), Count = reader.ReadInt64(), InvalidCount = reader.ReadInt64(), GapCount = reader.ReadInt64(),
            FirstX = reader.ReadDouble(), LastX = reader.ReadDouble(), Minimum = reader.ReadDouble(), Maximum = reader.ReadDouble(), FirstY = reader.ReadDouble(), LastY = reader.ReadDouble()
        };
        private static string LevelPath(string directory, int level) => Path.Combine(directory, "level-" + level + ".bin");
        private static long Ceiling(long count, long divisor) => count / divisor + (count % divisor == 0 ? 0 : 1);
        private static string Hash(string path, CancellationToken token)
        {
            using var source = File.OpenRead(path); using var sha = SHA256.Create(); var buffer = new byte[64 * 1024]; int count;
            while ((count = source.Read(buffer, 0, buffer.Length)) > 0) { token.ThrowIfCancellationRequested(); sha.TransformBlock(buffer, 0, count, buffer, 0); }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0); return Hex(sha.Hash);
        }
        private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }
}
