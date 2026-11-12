// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using UnityEngine;

    public enum FPWorkbenchAlignmentMode { SharedSourceRows, ExactKey }
    public enum FPWorkbenchAlignmentKeyKind { OrdinalText, Decimal }
    public enum FPWorkbenchDuplicateKeyPolicy { Reject, ReportAmbiguous }
    [Serializable] public sealed class FPWorkbenchAlignmentOptions
    {
        public FPWorkbenchAlignmentMode Mode = FPWorkbenchAlignmentMode.ExactKey;
        public FPWorkbenchAlignmentKeyKind KeyKind;
        public FPWorkbenchDuplicateKeyPolicy Duplicates = FPWorkbenchDuplicateKeyPolicy.Reject;
        public int LeftKeyColumn, RightKeyColumn;
        public string KeyDomain = "", LeftUnits = "1", RightUnits = "1";
        internal FPWorkbenchAlignmentOptions Copy()
        {
            if (!Enum.IsDefined(typeof(FPWorkbenchAlignmentMode), Mode) || !Enum.IsDefined(typeof(FPWorkbenchAlignmentKeyKind), KeyKind) ||
                !Enum.IsDefined(typeof(FPWorkbenchDuplicateKeyPolicy), Duplicates) || KeyDomain == null || KeyDomain.Length > 256 || LeftUnits == null || RightUnits == null || LeftUnits.Length > 128 || RightUnits.Length > 128)
                throw new ArgumentException("Invalid alignment policy/domain.");
            if (Mode == FPWorkbenchAlignmentMode.ExactKey)
            {
                if (string.IsNullOrWhiteSpace(KeyDomain)) throw new ArgumentException("Declare the common key domain; it is never inferred from headers.");
                if (KeyKind == FPWorkbenchAlignmentKeyKind.Decimal && !FPWorkbenchFormulaUnits.Parse(LeftUnits).Equivalent(FPWorkbenchFormulaUnits.Parse(RightUnits)))
                    throw new ArgumentException("Decimal keys require identical declared symbolic units; no implicit conversion.");
            }
            return (FPWorkbenchAlignmentOptions)MemberwiseClone();
        }
    }
    public readonly struct FPWorkbenchAlignedRows
    {
        public readonly long LeftRow, RightRow;
        internal FPWorkbenchAlignedRows(long left, long right) { LeftRow = left; RightRow = right; }
    }
    [Serializable] public sealed class FPWorkbenchAlignmentMetadata
    {
        public int Version = 2;
        public bool HasLeftCorrectionSnapshot, HasRightCorrectionSnapshot;
        public FPWorkbenchChangeSet LeftCorrections, RightCorrections;
        public string Key, LeftDatasetId, RightDatasetId, LeftSourcePath, RightSourcePath, LeftCorrectionFingerprint, RightCorrectionFingerprint, MatchesSha256;
        public FPWorkbenchAlignmentOptions Options;
        public long MatchedPairs, UnmatchedLeftRows, UnmatchedRightRows, AmbiguousLeftRows, AmbiguousRightRows;
    }

    /// <summary>Bounded original/effective key merge. Unsorted keys and implicit positional/time mappings are rejected.</summary>
    public sealed class FPWorkbenchAlignment
    {
        private readonly FPWorkbenchCsvDataset left, right;
        private readonly FPWorkbenchAlignmentMetadata metadata;
        internal FPWorkbenchCsvDataset LeftSource => left;
        internal FPWorkbenchCsvDataset RightSource => right;
        internal FPWorkbenchAlignmentMetadata Provenance => metadata;
        public string DirectoryPath { get; }
        public FPWorkbenchAlignmentMetadata Metadata => JsonUtility.FromJson<FPWorkbenchAlignmentMetadata>(JsonUtility.ToJson(metadata));
        private FPWorkbenchAlignment(FPWorkbenchCsvDataset left, FPWorkbenchCsvDataset right, string directory, FPWorkbenchAlignmentMetadata metadata)
        { this.left = left; this.right = right; DirectoryPath = directory; this.metadata = metadata; }
        public static Task<FPWorkbenchAlignment> BuildAsync(FPWorkbenchCsvDataset left, FPWorkbenchCsvDataset right, FPWorkbenchAlignmentOptions options,
            FPWorkbenchChangeSnapshot leftChanges = null, FPWorkbenchChangeSnapshot rightChanges = null, CancellationToken cancellation = default)
        {
            if (left == null || right == null || options == null) throw new ArgumentNullException();
            var policy = options.Copy();
            if (leftChanges != null && leftChanges.DatasetId != left.DatasetId || rightChanges != null && rightChanges.DatasetId != right.DatasetId) throw new ArgumentException("Alignment correction source differs.");
            if (policy.Mode == FPWorkbenchAlignmentMode.SharedSourceRows)
            {
                if (left.DatasetId != right.DatasetId || left.RowCount != right.RowCount || left.ColumnCount != right.ColumnCount || JsonUtility.ToJson(left.ImportProfile) != JsonUtility.ToJson(right.ImportProfile))
                    throw new ArgumentException("Shared source rows require the same verified source revision, profile and row schema.");
            }
            else if (policy.LeftKeyColumn < 0 || policy.LeftKeyColumn >= left.ColumnCount || policy.RightKeyColumn < 0 || policy.RightKeyColumn >= right.ColumnCount)
                throw new ArgumentOutOfRangeException("Choose physical key columns from both sources.");
            string key = MakeKey(left, right, policy, leftChanges?.Fingerprint ?? "original", rightChanges?.Fingerprint ?? "original");
            return Task.Run(() => Build(left, right, policy, leftChanges, rightChanges, key, cancellation), cancellation);
        }
        private static string MakeKey(FPWorkbenchCsvDataset left, FPWorkbenchCsvDataset right, FPWorkbenchAlignmentOptions policy, string leftFingerprint, string rightFingerprint)
        {
            using var buffer = new MemoryStream();
            using (var writer = new BinaryWriter(buffer, Encoding.UTF8, true))
            {
                writer.Write("FPAlignment2"); writer.Write(left.DatasetId); writer.Write(right.DatasetId); writer.Write(left.SourcePath); writer.Write(right.SourcePath);
                writer.Write(JsonUtility.ToJson(left.ImportProfile)); writer.Write(JsonUtility.ToJson(right.ImportProfile));
                writer.Write(leftFingerprint); writer.Write(rightFingerprint); writer.Write(JsonUtility.ToJson(policy));
            }
            using var hash = SHA256.Create(); return Hex(hash.ComputeHash(buffer.ToArray()));
        }
        private static FPWorkbenchAlignment Build(FPWorkbenchCsvDataset left, FPWorkbenchCsvDataset right, FPWorkbenchAlignmentOptions options,
            FPWorkbenchChangeSnapshot leftChanges, FPWorkbenchChangeSnapshot rightChanges, string key, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); left.ValidateUnchanged(); right.ValidateUnchanged();
            string target = Path.Combine(left.CacheDirectory, "alignment-" + key);
            if (Directory.Exists(target)) return Open(left, right, target, key, token);
            string staging = Path.Combine(left.CacheDirectory, ".alignment-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(staging);
            var metadata = new FPWorkbenchAlignmentMetadata { Key = key, Options = options, LeftDatasetId = left.DatasetId, RightDatasetId = right.DatasetId,
                LeftSourcePath = left.SourcePath, RightSourcePath = right.SourcePath, LeftCorrectionFingerprint = leftChanges?.Fingerprint ?? "original", RightCorrectionFingerprint = rightChanges?.Fingerprint ?? "original",
                HasLeftCorrectionSnapshot = leftChanges != null, HasRightCorrectionSnapshot = rightChanges != null,
                LeftCorrections = leftChanges == null ? null : JsonUtility.FromJson<FPWorkbenchChangeSet>(leftChanges.Json),
                RightCorrections = rightChanges == null ? null : JsonUtility.FromJson<FPWorkbenchChangeSet>(rightChanges.Json) };
            try
            {
                string matches = Path.Combine(staging, "matches.bin");
                using (var output = new BinaryWriter(File.Create(matches)))
                {
                    if (options.Mode == FPWorkbenchAlignmentMode.SharedSourceRows)
                        for (long row = 0; row < left.RowCount; row++) { token.ThrowIfCancellationRequested(); output.Write(row); output.Write(row); metadata.MatchedPairs++; }
                    else
                    {
                        using var a = new Cursor(left, options.LeftKeyColumn, options.KeyKind, leftChanges, token);
                        using var b = new Cursor(right, options.RightKeyColumn, options.KeyKind, rightChanges, token);
                        while (a.HasValue || b.HasValue)
                        {
                            token.ThrowIfCancellationRequested(); int compare = !a.HasValue ? 1 : !b.HasValue ? -1 : a.Current.Compare(b.Current);
                            var ga = compare <= 0 ? a.TakeGroup() : default; var gb = compare >= 0 ? b.TakeGroup() : default;
                            bool ambiguous = ga.Count > 1 || gb.Count > 1;
                            if (ambiguous && options.Duplicates == FPWorkbenchDuplicateKeyPolicy.Reject) throw new InvalidDataException("Duplicate alignment key at left row " + ga.First + " / right row " + gb.First + ".");
                            if (ambiguous) { metadata.AmbiguousLeftRows += ga.Count; metadata.AmbiguousRightRows += gb.Count; }
                            else if (compare == 0) { output.Write(ga.First); output.Write(gb.First); metadata.MatchedPairs++; }
                            else if (compare < 0) metadata.UnmatchedLeftRows += ga.Count;
                            else metadata.UnmatchedRightRows += gb.Count;
                        }
                    }
                }
                left.ValidateUnchanged(); right.ValidateUnchanged(); metadata.MatchesSha256 = Hash(matches, token);
                string json = JsonUtility.ToJson(metadata, true);
                if (Encoding.UTF8.GetByteCount(json) > FPWorkbenchDocumentFile.MaximumDocumentBytes) throw new InvalidDataException("Alignment provenance exceeds 4 MiB.");
                File.WriteAllText(Path.Combine(staging, "metadata.json"), json, new UTF8Encoding(false, true));
                token.ThrowIfCancellationRequested(); left.ValidateUnchanged(); right.ValidateUnchanged();
                try { Directory.Move(staging, target); } catch (IOException) when (Directory.Exists(target)) { }
                return Open(left, right, target, key, token);
            }
            finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
        }
        private static FPWorkbenchAlignment Open(FPWorkbenchCsvDataset left, FPWorkbenchCsvDataset right, string directory, string key, CancellationToken token)
        {
            string path = Path.Combine(directory, "metadata.json"); if (new FileInfo(path).Length > FPWorkbenchDocumentFile.MaximumDocumentBytes) throw new InvalidDataException("Alignment metadata exceeds its bound.");
            var metadata = JsonUtility.FromJson<FPWorkbenchAlignmentMetadata>(File.ReadAllText(path, new UTF8Encoding(false, true)));
            if (metadata == null || metadata.Version != 2 || metadata.Key != key || metadata.LeftDatasetId != left.DatasetId || metadata.RightDatasetId != right.DatasetId || metadata.Options == null ||
                !Conserves(left.RowCount, metadata.MatchedPairs, metadata.UnmatchedLeftRows, metadata.AmbiguousLeftRows) || !Conserves(right.RowCount, metadata.MatchedPairs, metadata.UnmatchedRightRows, metadata.AmbiguousRightRows) ||
                metadata.MatchedPairs > long.MaxValue / 16 || new FileInfo(Path.Combine(directory, "matches.bin")).Length != metadata.MatchedPairs * 16 ||
                Hash(Path.Combine(directory, "matches.bin"), token) != metadata.MatchesSha256) throw new InvalidDataException("Alignment identity, coverage or checksum failed.");
            VerifyCorrections(metadata.HasLeftCorrectionSnapshot, metadata.LeftCorrections, left.DatasetId, metadata.LeftCorrectionFingerprint);
            VerifyCorrections(metadata.HasRightCorrectionSnapshot, metadata.RightCorrections, right.DatasetId, metadata.RightCorrectionFingerprint);
            if (!metadata.HasLeftCorrectionSnapshot) metadata.LeftCorrections = null;
            if (!metadata.HasRightCorrectionSnapshot) metadata.RightCorrections = null;
            metadata.Options.Copy();
            if (string.IsNullOrEmpty(metadata.LeftCorrectionFingerprint) || string.IsNullOrEmpty(metadata.RightCorrectionFingerprint) ||
                metadata.LeftSourcePath != left.SourcePath || metadata.RightSourcePath != right.SourcePath ||
                MakeKey(left, right, metadata.Options, metadata.LeftCorrectionFingerprint, metadata.RightCorrectionFingerprint) != key) throw new InvalidDataException("Alignment policy/provenance differs from its cache identity.");
            left.ValidateUnchanged(); right.ValidateUnchanged(); return new FPWorkbenchAlignment(left, right, directory, metadata);
        }
        private static void VerifyCorrections(bool present, FPWorkbenchChangeSet changes, string dataset, string fingerprint)
        {
            if (!present)
            {
                if (fingerprint != "original" || changes != null && !string.IsNullOrEmpty(changes.DatasetId)) throw new InvalidDataException("Alignment correction presence contradicts its revision.");
                return;
            }
            if (changes == null || changes.DatasetId != dataset) throw new InvalidDataException("Alignment correction snapshot is missing or foreign.");
            changes.Validate(); if (changes.Snapshot().Fingerprint != fingerprint) throw new InvalidDataException("Alignment correction fingerprint differs from its snapshot.");
        }
        internal void ValidatePayload(CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); left.ValidateUnchanged(); right.ValidateUnchanged();
            string path = Path.Combine(DirectoryPath, "matches.bin");
            if (new FileInfo(path).Length != metadata.MatchedPairs * 16 || Hash(path, token) != metadata.MatchesSha256) throw new InvalidDataException("Alignment mapping changed after admission.");
        }
        private static bool Conserves(long rows, long matched, long unmatched, long ambiguous) => matched >= 0 && matched <= rows && unmatched >= 0 && unmatched <= rows - matched && ambiguous == rows - matched - unmatched;
        public Task<FPWorkbenchAlignedRows[]> ReadPageAsync(long first, int count = 256, CancellationToken cancellation = default)
        {
            if (first < 0 || first > metadata.MatchedPairs || count < 0 || count > 4096) throw new ArgumentOutOfRangeException("Alignment pages require 0–4096 pairs within the product.");
            int rows = (int)Math.Min(count, metadata.MatchedPairs - first);
            return Task.Run(() =>
            {
                cancellation.ThrowIfCancellationRequested(); left.ValidateUnchanged(); right.ValidateUnchanged();
                using var reader = new BinaryReader(File.OpenRead(Path.Combine(DirectoryPath, "matches.bin"))); reader.BaseStream.Position = first * 16;
                var result = new FPWorkbenchAlignedRows[rows];
                for (int i = 0; i < rows; i++)
                {
                    cancellation.ThrowIfCancellationRequested(); long a = reader.ReadInt64(), b = reader.ReadInt64();
                    if (a < 0 || a >= left.RowCount || b < 0 || b >= right.RowCount) throw new InvalidDataException("Aligned source-row mapping is outside its source.");
                    result[i] = new FPWorkbenchAlignedRows(a, b);
                }
                left.ValidateUnchanged(); right.ValidateUnchanged(); return result;
            }, cancellation);
        }
        private readonly struct Group
        { public readonly long First, Count; public Group(long first, long count) { First = first; Count = count; } }
        private readonly struct Key
        {
            private static readonly char[] ExponentMarkers = { 'e', 'E' };
            private readonly string text; private readonly decimal number; private readonly bool numeric;
            public Key(string text, FPWorkbenchAlignmentKeyKind kind)
            {
                this.text = text; numeric = kind == FPWorkbenchAlignmentKeyKind.Decimal; number = 0;
                if (string.IsNullOrEmpty(text) || numeric && !decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) throw new InvalidDataException("Missing, invalid or out-of-range alignment key.");
                if (numeric && Normalize(text) != Normalize(number.ToString("G29", CultureInfo.InvariantCulture))) throw new InvalidDataException("Decimal alignment key would require rounding; use an exact text key or an explicitly transformed key.");
            }
            private static string Normalize(string value)
            {
                value = value.Trim(); bool negative = value.StartsWith("-", StringComparison.Ordinal);
                if (value.StartsWith("+", StringComparison.Ordinal) || negative) value = value.Substring(1);
                int e = value.IndexOfAny(ExponentMarkers); long exponent = 0;
                if (e >= 0)
                {
                    if (!long.TryParse(value.Substring(e + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out exponent) || exponent < -1000000 || exponent > 1000000) throw new InvalidDataException("Decimal key exponent exceeds its exact-key bound.");
                    value = value.Substring(0, e);
                }
                int dot = value.IndexOf('.'); if (dot >= 0) { exponent -= value.Length - dot - 1; value = value.Remove(dot, 1); }
                value = value.TrimStart('0'); if (value.Length == 0) return "0";
                int length = value.Length; value = value.TrimEnd('0'); exponent += length - value.Length;
                return (negative ? "-" : "") + value + "e" + exponent.ToString(CultureInfo.InvariantCulture);
            }
            public int Compare(Key other) => numeric ? number.CompareTo(other.number) : string.CompareOrdinal(text, other.text);
        }
        private sealed class Cursor : IDisposable
        {
            private readonly FPWorkbenchCsvDataset data; private readonly int column; private readonly FPWorkbenchAlignmentKeyKind kind;
            private readonly FPWorkbenchChangeSnapshot changes; private readonly CancellationToken token; private readonly Stream source; private readonly FPWorkbenchCsvRecords parser;
            private long next, row; private bool prior; private Key previous;
            public bool HasValue { get; private set; }
            public Key Current { get; private set; }
            public Cursor(FPWorkbenchCsvDataset data, int column, FPWorkbenchAlignmentKeyKind kind, FPWorkbenchChangeSnapshot changes, CancellationToken token)
            {
                this.data = data; this.column = column; this.kind = kind; this.changes = changes; this.token = token;
                source = data.OpenReaderAt(0); parser = data.Parser(source, token);
                try { Advance(); } catch { source.Dispose(); throw; }
            }
            private void Advance()
            {
                token.ThrowIfCancellationRequested(); HasValue = next < data.RowCount; if (!HasValue) return;
                var record = parser.Read(); if (record == null || record.Length != data.ColumnCount) throw new InvalidDataException("Alignment source differs from its index.");
                row = next++; string text = record[column];
                if (changes != null && changes.TryGet(data.Cell(row, column), text, out string effective, out bool masked))
                { if (masked) throw new InvalidDataException("Alignment key is masked at source row " + row + "."); text = effective; }
                Current = new Key(text, kind);
                if (prior && previous.Compare(Current) > 0) throw new InvalidDataException("Alignment keys decrease at source row " + row + "; build an explicit sorted key index first.");
                previous = Current; prior = true;
            }
            public Group TakeGroup()
            {
                Key key = Current; long first = row, count = 0;
                do { count++; Advance(); } while (HasValue && key.Compare(Current) == 0);
                return new Group(first, count);
            }
            public void Dispose() => source.Dispose();
        }
        private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        private static string Hash(string path, CancellationToken token)
        {
            using var hash = SHA256.Create(); using var input = File.OpenRead(path); var buffer = new byte[65536]; int count;
            while ((count = input.Read(buffer, 0, buffer.Length)) != 0) { token.ThrowIfCancellationRequested(); hash.TransformBlock(buffer, 0, count, buffer, 0); }
            hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0); return Hex(hash.Hash);
        }
    }
}
