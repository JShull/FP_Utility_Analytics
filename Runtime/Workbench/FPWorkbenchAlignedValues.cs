// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    using System.Globalization;
    using System.Collections.Generic;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using UnityEngine;

    [Serializable] public sealed class FPWorkbenchAlignedValuesMetadata
    {
        public int Version = 2;
        public string Key, LeftColumnId, RightColumnId, LeftColumnName, RightColumnName, LeftFormulaFingerprint, RightFormulaFingerprint;
        public FPWorkbenchAlignmentMetadata Alignment;
        public FPWorkbenchExportInvalidPolicy InvalidPolicy;
        public long InvalidX, InvalidY;
        public int[] LeftSourceColumns, RightSourceColumns;
        public bool HasLeftCalculations, HasRightCalculations;
        public FPWorkbenchFormulaSet LeftCalculations, RightCalculations;
    }
    /// <summary>Derived numeric XY plus exact paired source-row columns. Source measurements and mappings stay immutable.</summary>
    public sealed class FPWorkbenchAlignedValues
    {
        private readonly FPWorkbenchCsvDataset leftSource, rightSource;
        public FPWorkbenchSourceDocument LeftReference => Reference(leftSource);
        public FPWorkbenchSourceDocument RightReference => Reference(rightSource);
        private static FPWorkbenchSourceDocument Reference(FPWorkbenchCsvDataset source) => new FPWorkbenchSourceDocument { DatasetId = source.DatasetId, SourcePath = source.SourcePath, CacheDirectory = source.CacheDirectory, Import = source.ImportProfile, Name = Path.GetFileNameWithoutExtension(source.SourcePath) };
        public FPWorkbenchCsvDataset Dataset { get; }
        private readonly FPWorkbenchAlignedValuesMetadata metadata;
        public FPWorkbenchAlignedValuesMetadata Metadata => JsonUtility.FromJson<FPWorkbenchAlignedValuesMetadata>(JsonUtility.ToJson(metadata));
        private FPWorkbenchAlignedValues(FPWorkbenchCsvDataset dataset, FPWorkbenchAlignedValuesMetadata metadata, FPWorkbenchCsvDataset left, FPWorkbenchCsvDataset right) { Dataset = dataset; this.metadata = metadata; leftSource = left; rightSource = right; }
        public static Task<FPWorkbenchAlignedValues> BuildAsync(FPWorkbenchAlignment alignment, int leftColumn, int rightColumn,
            FPWorkbenchFormulaProgram leftCalculations = null, FPWorkbenchFormulaProgram rightCalculations = null,
            FPWorkbenchExportInvalidPolicy invalidPolicy = FPWorkbenchExportInvalidPolicy.Reject, CancellationToken cancellation = default)
        {
            if (alignment == null) throw new ArgumentNullException(nameof(alignment));
            if (!Enum.IsDefined(typeof(FPWorkbenchExportInvalidPolicy), invalidPolicy)) throw new ArgumentException("Choose an invalid-value policy.");
            var left = alignment.LeftSource; var right = alignment.RightSource;
            var a = new FPWorkbenchTableView(left, leftCalculations); var b = new FPWorkbenchTableView(right, rightCalculations);
            var metadata = new FPWorkbenchAlignedValuesMetadata { Alignment = alignment.Metadata, LeftColumnId = a.ColumnId(leftColumn), RightColumnId = b.ColumnId(rightColumn),
                LeftColumnName = a.ColumnName(leftColumn), RightColumnName = b.ColumnName(rightColumn), InvalidPolicy = invalidPolicy,
                LeftFormulaFingerprint = leftCalculations?.Fingerprint ?? "none", RightFormulaFingerprint = rightCalculations?.Fingerprint ?? "none",
                HasLeftCalculations = leftCalculations != null, HasRightCalculations = rightCalculations != null,
                LeftCalculations = leftCalculations?.Definition, RightCalculations = rightCalculations?.Definition,
                LeftSourceColumns = leftColumn < left.ColumnCount ? new[] { leftColumn } : leftCalculations.SourceDependencies(leftColumn - left.ColumnCount),
                RightSourceColumns = rightColumn < right.ColumnCount ? new[] { rightColumn } : rightCalculations.SourceDependencies(rightColumn - right.ColumnCount) };
            if (metadata.Alignment.Options.Mode == FPWorkbenchAlignmentMode.ExactKey)
            {
                int[] Include(int[] columns, int key) { var set = new SortedSet<int>(columns); set.Add(key); var result = new int[set.Count]; set.CopyTo(result); return result; }
                metadata.LeftSourceColumns = Include(metadata.LeftSourceColumns, metadata.Alignment.Options.LeftKeyColumn);
                metadata.RightSourceColumns = Include(metadata.RightSourceColumns, metadata.Alignment.Options.RightKeyColumn);
                if (metadata.Alignment.Options.KeyKind == FPWorkbenchAlignmentKeyKind.UtcDateTime) {
                    metadata.LeftSourceColumns = Include(metadata.LeftSourceColumns, metadata.Alignment.Options.LeftDateColumn);
                    metadata.RightSourceColumns = Include(metadata.RightSourceColumns, metadata.Alignment.Options.RightDateColumn);
                }
            }
            string identity = "FPAlignedValues2\n" + metadata.Alignment.Key + "\n" + metadata.LeftColumnId + "\n" + metadata.RightColumnId + "\n" + metadata.LeftFormulaFingerprint + "\n" + metadata.RightFormulaFingerprint + "\n" + (int)invalidPolicy;
            using (var hash = SHA256.Create()) metadata.Key = Hex(hash.ComputeHash(Encoding.UTF8.GetBytes(identity)));
            return BuildProduct(alignment, leftColumn, rightColumn, leftCalculations, rightCalculations, metadata, cancellation);
        }
        private static async Task<FPWorkbenchAlignedValues> BuildProduct(FPWorkbenchAlignment alignment, int a, int b, FPWorkbenchFormulaProgram leftProgram,
            FPWorkbenchFormulaProgram rightProgram, FPWorkbenchAlignedValuesMetadata metadata, CancellationToken token)
        {
            string directory = await Task.Run(() => Materialize(alignment, a, b, leftProgram, rightProgram, metadata, token), token).ConfigureAwait(false);
            string csv = Path.Combine(directory, "data.csv"), index = Path.Combine(alignment.LeftSource.CacheDirectory, "ai-" + metadata.Key.Substring(0, 32));
            FPWorkbenchCsvDataset data;
            if (Directory.Exists(index)) data = await FPWorkbenchCsvDataset.OpenAsync(index, token).ConfigureAwait(false);
            else
            {
                try { data = await FPWorkbenchCsvDataset.ImportAsync(csv, index, new FPWorkbenchCsvProfile(), token).ConfigureAwait(false); }
                catch (IOException) when (Directory.Exists(index)) { data = await FPWorkbenchCsvDataset.OpenAsync(index, token).ConfigureAwait(false); }
            }
            var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!string.Equals(Path.GetFullPath(data.SourcePath), Path.GetFullPath(csv), comparison) || JsonUtility.ToJson(data.ImportProfile) != JsonUtility.ToJson(new FPWorkbenchCsvProfile().Copy()) ||
                data.RowCount != metadata.Alignment.MatchedPairs || data.ColumnCount != 4) throw new InvalidDataException("Aligned value index differs from its mapping.");
            return new FPWorkbenchAlignedValues(data, metadata, alignment.LeftSource, alignment.RightSource);
        }
        private static string Materialize(FPWorkbenchAlignment alignment, int a, int b, FPWorkbenchFormulaProgram leftProgram, FPWorkbenchFormulaProgram rightProgram,
            FPWorkbenchAlignedValuesMetadata expected, CancellationToken token)
        {
            var left = alignment.LeftSource; var right = alignment.RightSource;
            token.ThrowIfCancellationRequested(); left.ValidateUnchanged(); right.ValidateUnchanged(); alignment.ValidatePayload(token);
            string target = Path.Combine(left.CacheDirectory, "av-" + expected.Key.Substring(0, 32));
            if (Directory.Exists(target)) { ReadMetadata(target, expected, token); return target; }
            string staging = Path.Combine(left.CacheDirectory, ".av-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(staging);
            try
            {
                var changes = alignment.Provenance;
                using (var x = new Values(left, a, leftProgram, changes.HasLeftCorrectionSnapshot ? changes.LeftCorrections.Snapshot() : null, token))
                using (var y = new Values(right, b, rightProgram, changes.HasRightCorrectionSnapshot ? changes.RightCorrections.Snapshot() : null, token))
                using (var pairs = new BinaryReader(File.OpenRead(Path.Combine(alignment.DirectoryPath, "matches.bin"))))
                using (var writer = new StreamWriter(Path.Combine(staging, "data.csv"), false, new UTF8Encoding(false, true), 65536))
                {
                    writer.NewLine = "\r\n"; writer.WriteLine("LeftSourceRow,RightSourceRow,X,Y"); long previousA = -1, previousB = -1;
                    for (long i = 0; i < changes.MatchedPairs; i++)
                    {
                        token.ThrowIfCancellationRequested(); long rowA = pairs.ReadInt64(), rowB = pairs.ReadInt64();
                        if (rowA <= previousA || rowB <= previousB || rowA >= left.RowCount || rowB >= right.RowCount) throw new InvalidDataException("Paired rows must be valid and strictly source-ordered.");
                        previousA = rowA; previousB = rowB;
                        var vx = x.At(rowA); var vy = y.At(rowB);
                        if (!vx.IsValid) { expected.InvalidX++; if (expected.InvalidPolicy == FPWorkbenchExportInvalidPolicy.Reject) throw new InvalidDataException("Invalid aligned X at left source row " + rowA + ": " + vx.Status); }
                        if (!vy.IsValid) { expected.InvalidY++; if (expected.InvalidPolicy == FPWorkbenchExportInvalidPolicy.Reject) throw new InvalidDataException("Invalid aligned Y at right source row " + rowB + ": " + vy.Status); }
                        writer.Write(rowA.ToString(CultureInfo.InvariantCulture)); writer.Write(','); writer.Write(rowB.ToString(CultureInfo.InvariantCulture)); writer.Write(',');
                        FPWorkbenchCsvExport.Field(writer, vx.IsValid ? vx.ToString() : ""); writer.Write(','); FPWorkbenchCsvExport.Field(writer, vy.IsValid ? vy.ToString() : ""); writer.WriteLine();
                    }
                    if (pairs.BaseStream.Position != pairs.BaseStream.Length) throw new InvalidDataException("Alignment mapping size changed.");
                }
                left.ValidateUnchanged(); right.ValidateUnchanged();
                string json = JsonUtility.ToJson(expected, true);
                if (Encoding.UTF8.GetByteCount(json) > FPWorkbenchDocumentFile.MaximumDocumentBytes) throw new InvalidDataException("Aligned value provenance exceeds 4 MiB.");
                File.WriteAllText(Path.Combine(staging, "metadata.json"), json, new UTF8Encoding(false, true));
                string seal = Hash(Path.Combine(staging, "data.csv"), token) + "\n" + Hash(Path.Combine(staging, "metadata.json"), token);
                File.WriteAllText(Path.Combine(staging, "seal.txt"), seal, new UTF8Encoding(false, true));
                token.ThrowIfCancellationRequested(); left.ValidateUnchanged(); right.ValidateUnchanged();
                try { Directory.Move(staging, target); } catch (IOException) when (Directory.Exists(target)) { ReadMetadata(target, expected, token); }
                return target;
            }
            finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
        }
        private static void ReadMetadata(string directory, FPWorkbenchAlignedValuesMetadata expected, CancellationToken token)
        {
            string sealPath = Path.Combine(directory, "seal.txt"), jsonPath = Path.Combine(directory, "metadata.json");
            if (new FileInfo(sealPath).Length > 256 || new FileInfo(jsonPath).Length > FPWorkbenchDocumentFile.MaximumDocumentBytes) throw new InvalidDataException("Invalid aligned value provenance size.");
            string[] seal = File.ReadAllLines(sealPath);
            if (seal.Length != 2 || Hash(Path.Combine(directory, "data.csv"), token) != seal[0] || Hash(jsonPath, token) != seal[1]) throw new InvalidDataException("Aligned value payload/provenance checksum failed.");
            var saved = JsonUtility.FromJson<FPWorkbenchAlignedValuesMetadata>(File.ReadAllText(jsonPath, new UTF8Encoding(false, true)));
            if (saved == null || saved.Version != 2 || saved.Key != expected.Key || saved.Alignment?.Key != expected.Alignment.Key || saved.LeftColumnId != expected.LeftColumnId || saved.RightColumnId != expected.RightColumnId ||
                saved.LeftFormulaFingerprint != expected.LeftFormulaFingerprint || saved.RightFormulaFingerprint != expected.RightFormulaFingerprint || saved.InvalidPolicy != expected.InvalidPolicy || saved.InvalidX < 0 || saved.InvalidY < 0 ||
                saved.InvalidX > expected.Alignment.MatchedPairs || saved.InvalidY > expected.Alignment.MatchedPairs) throw new InvalidDataException("Aligned value identity differs.");
            expected.InvalidX = saved.InvalidX; expected.InvalidY = saved.InvalidY;
        }
        private sealed class Values : IDisposable
        {
            private readonly FPWorkbenchCsvDataset data; private readonly int column; private readonly Stream input; private readonly FPWorkbenchCsvRecords parser;
            private readonly FPWorkbenchFormulaProgram.Evaluator evaluator; private readonly CancellationToken token;
            private long row = -1;
            public Values(FPWorkbenchCsvDataset data, int column, FPWorkbenchFormulaProgram program, FPWorkbenchChangeSnapshot changes, CancellationToken token)
            {
                this.data = data; this.column = column; this.token = token;
                program ??= FPWorkbenchFormulaProgram.Compile(data, new FPWorkbenchFormulaSet { DatasetId = data.DatasetId });
                evaluator = program.CreateEvaluator(changes); input = data.OpenReaderAt(0); parser = data.Parser(input, token);
            }
            public FPWorkbenchFormulaValue At(long requested)
            {
                while (row < requested)
                {
                    token.ThrowIfCancellationRequested(); var values = parser.Read(); row++;
                    if (values == null || values.Length != data.ColumnCount) throw new InvalidDataException("Aligned value source differs from its row index.");
                    if (row == requested) evaluator.SetRow(values, row);
                }
                return column < data.ColumnCount ? evaluator.Source(column) : evaluator.Derived(column - data.ColumnCount);
            }
            public void Dispose() => input.Dispose();
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
