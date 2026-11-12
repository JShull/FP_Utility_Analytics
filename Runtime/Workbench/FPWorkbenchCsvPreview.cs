// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;

    /// <summary>A bounded unverified prefix, never an indexed dataset or proof of complete coverage.</summary>
    public sealed class FPWorkbenchCsvPreview
    {
        public const int MaximumRows = 32, MaximumColumns = 32, MaximumCharacters = 512 * 1024;
        public string SourcePath { get; }
        public int SourceColumnCount { get; }
        public IReadOnlyList<string> Headers { get; }
        public IReadOnlyList<IReadOnlyList<string>> Rows { get; }
        public bool TextLimitReached { get; }
        private FPWorkbenchCsvPreview(string path, int columns, string[] headers, List<IReadOnlyList<string>> rows, bool limited)
        {
            SourcePath = path; SourceColumnCount = columns; Headers = Array.AsReadOnly(headers);
            Rows = rows.AsReadOnly(); TextLimitReached = limited;
        }
        internal static FPWorkbenchCsvPreview Read(string path, FPWorkbenchCsvRecords parser, CancellationToken cancellation)
        {
            var names = parser.Read() ?? throw new InvalidDataException("CSV header is missing.");
            int columns = Math.Min(MaximumColumns, names.Length), characters = 0;
            var rows = new List<IReadOnlyList<string>>(); var headers = new string[columns];
            for (int c = 0; c < columns; c++) {
                if (names[c].Length > MaximumCharacters - characters)
                    return new FPWorkbenchCsvPreview(path, names.Length, Array.Empty<string>(), rows, true);
                headers[c] = names[c]; characters += names[c].Length;
            }
            for (int r = 0; r < MaximumRows; r++) {
                cancellation.ThrowIfCancellationRequested(); var record = parser.Read(); if (record == null) break;
                if (record.Length != names.Length) throw new InvalidDataException("Column count mismatch at logical data row " + r + ".");
                var row = new string[columns];
                for (int c = 0; c < columns; c++) {
                    if (record[c].Length > MaximumCharacters - characters)
                        return new FPWorkbenchCsvPreview(path, names.Length, headers, rows, true);
                    row[c] = record[c]; characters += record[c].Length;
                }
                rows.Add(Array.AsReadOnly(row));
            }
            cancellation.ThrowIfCancellationRequested();
            return new FPWorkbenchCsvPreview(path, names.Length, headers, rows, false);
        }
    }
}
