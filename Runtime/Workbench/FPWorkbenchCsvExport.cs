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
    using UnityEngine;

    public enum FPWorkbenchExportInvalidPolicy { Reject, EmptyField }

    [Serializable] public sealed class FPWorkbenchCsvExportResult
    {
        public string Format = "FuzzPhyte.DataWorkbench.MaterializedCsv";
        public int Version = 1;
        public string OutputEncoding = "utf-8", OutputDelimiter = ",", OutputLineEnding = "CRLF";
        public string DatasetId, SourcePath, FormulaFingerprint, CorrectionFingerprint, CsvSha256;
        public long FirstRow, EndRow, InvalidCells, MaskedCells;
        public string[] ColumnIds, ColumnNames;
        public FPWorkbenchCsvProfile Import;
        public FPWorkbenchExportInvalidPolicy InvalidPolicy;
        public FPWorkbenchFormulaSet Calculations;
        public FPWorkbenchChangeSet Corrections;
    }

    /// <summary>Publishes a new directory containing data.csv and provenance.json atomically. Never overwrites a product.</summary>
    public static class FPWorkbenchCsvExport
    {
        public static Task<FPWorkbenchCsvExportResult> WriteAsync(FPWorkbenchCsvDataset dataset, string outputDirectory,
            int[] columns, long firstRow, long endRow, FPWorkbenchFormulaProgram calculations = null,
            FPWorkbenchChangeSnapshot changes = null, FPWorkbenchExportInvalidPolicy invalidPolicy = FPWorkbenchExportInvalidPolicy.Reject,
            IProgress<long> progress = null, CancellationToken cancellation = default)
        {
            if (dataset == null || columns == null) throw new ArgumentNullException();
            if (columns.Length < 1 || columns.Length > 256 || firstRow < 0 || endRow < firstRow || endRow > dataset.RowCount ||
                !Enum.IsDefined(typeof(FPWorkbenchExportInvalidPolicy), invalidPolicy)) throw new ArgumentException("Export requires 1–256 columns and a valid half-open row range.");
            if (changes != null && changes.DatasetId != dataset.DatasetId) throw new ArgumentException("Export corrections belong to another source.");
            var table = new FPWorkbenchTableView(dataset, calculations); var selected = (int[])columns.Clone();
            var unique = new HashSet<int>();
            var result = new FPWorkbenchCsvExportResult { DatasetId = dataset.DatasetId, SourcePath = dataset.SourcePath,
                FirstRow = firstRow, EndRow = endRow, ColumnIds = new string[selected.Length], ColumnNames = new string[selected.Length],
                FormulaFingerprint = calculations?.Fingerprint ?? "none", CorrectionFingerprint = changes?.Fingerprint ?? "original",
                Import = dataset.ImportProfile, InvalidPolicy = invalidPolicy, Calculations = calculations?.Definition,
                Corrections = changes == null ? null : JsonUtility.FromJson<FPWorkbenchChangeSet>(changes.Json) };
            for (int c = 0; c < selected.Length; c++)
            {
                if (!unique.Add(selected[c])) throw new ArgumentException("Export columns must be distinct.");
                result.ColumnIds[c] = table.ColumnId(selected[c]); result.ColumnNames[c] = table.ColumnName(selected[c]);
            }
            string target = Path.GetFullPath(outputDirectory);
            var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            string cache = Path.GetFullPath(dataset.CacheDirectory).TrimEnd(Path.DirectorySeparatorChar);
            if (target.Equals(cache, comparison) || target.StartsWith(cache + Path.DirectorySeparatorChar, comparison) ||
                target.Equals(Path.GetFullPath(dataset.SourcePath), comparison)) throw new IOException("Export outside the source and its cache.");
            if (Directory.Exists(target) || File.Exists(target)) throw new IOException("Choose a new export directory; existing files are never replaced.");
            string parent = Path.GetDirectoryName(target);
            if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent)) throw new DirectoryNotFoundException("The export parent directory must exist.");
            return dataset.RunCachedAsync(() =>
            {
                string staging = Path.Combine(parent, ".fp-export-" + Guid.NewGuid().ToString("N"));
                try
                {
                    cancellation.ThrowIfCancellationRequested(); dataset.ValidateUnchanged(); Directory.CreateDirectory(staging);
                    string csv = Path.Combine(staging, "data.csv");
                    using (var writer = new StreamWriter(csv, false, new UTF8Encoding(false, true), 65536))
                    using (var reader = dataset.OpenReaderAt(firstRow))
                    {
                        writer.NewLine = "\r\n";
                        for (int c = 0; c < selected.Length; c++) { if (c != 0) writer.Write(','); Field(writer, result.ColumnNames[c]); } writer.WriteLine();
                        var parser = dataset.Parser(reader, cancellation); var evaluator = calculations?.CreateEvaluator(changes);
                        for (long row = firstRow; row < endRow; row++)
                        {
                            cancellation.ThrowIfCancellationRequested(); var record = parser.Read();
                            if (record == null || record.Length != dataset.ColumnCount) throw new InvalidDataException("Export source differs from its row index.");
                            evaluator?.SetRow(record, row);
                            for (int c = 0; c < selected.Length; c++)
                            {
                                if (c != 0) writer.Write(','); int column = selected[c]; string text;
                                if (column >= dataset.ColumnCount)
                                {
                                    var number = evaluator.Derived(column - dataset.ColumnCount);
                                    if (!number.IsValid)
                                    {
                                        if (invalidPolicy == FPWorkbenchExportInvalidPolicy.Reject) throw new InvalidDataException("Export row " + row + ", column " + column + ": " + number.Status);
                                        result.InvalidCells++; text = "";
                                    }
                                    else text = number.ToString();
                                }
                                else
                                {
                                    text = record[column];
                                    if (changes != null && changes.TryGet(dataset.Cell(row, column), text, out string effective, out bool masked))
                                    { text = effective; if (masked) { text = ""; result.MaskedCells++; } }
                                }
                                Field(writer, text);
                            }
                            writer.WriteLine(); if (((row - firstRow + 1) & 4095) == 0) progress?.Report(row - firstRow + 1);
                        }
                    }
                    cancellation.ThrowIfCancellationRequested(); dataset.ValidateUnchanged();
                    using (var hash = SHA256.Create()) using (var input = File.OpenRead(csv))
                    {
                        var buffer = new byte[65536]; int count;
                        while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
                        { cancellation.ThrowIfCancellationRequested(); hash.TransformBlock(buffer, 0, count, buffer, 0); }
                        hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                        result.CsvSha256 = BitConverter.ToString(hash.Hash).Replace("-", "").ToLowerInvariant();
                    }
                    string metadata = JsonUtility.ToJson(result, true);
                    if (Encoding.UTF8.GetByteCount(metadata) > FPWorkbenchDocumentFile.MaximumDocumentBytes) throw new InvalidDataException("Export provenance exceeds 4 MiB.");
                    File.WriteAllText(Path.Combine(staging, "provenance.json"), metadata, new UTF8Encoding(false, true));
                    progress?.Report(endRow - firstRow); cancellation.ThrowIfCancellationRequested();
                    dataset.ValidateUnchanged(); Directory.Move(staging, target); return result;
                }
                finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
            }, cancellation);
        }
        internal static void Field(TextWriter writer, string text)
        {
            // Always quoting preserves delimiters, newlines, leading zeroes and exact source text on reimport.
            writer.Write('"');
            foreach (char value in text) { if (value == '"') writer.Write('"'); writer.Write(value); }
            writer.Write('"');
        }
    }
}
