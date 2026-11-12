// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    /// <summary>Bounded quoted TSV generation; does not access an OS clipboard or modify source values.</summary>
    public static class FPWorkbenchTableClipboard
    {
        private static readonly char[] QuotedCharacters = { '\t', '\r', '\n', '"' };
        public const int MaximumRows = 256, MaximumCharacters = 512 * 1024;
        public static async Task<string> BuildAsync(FPWorkbenchTableView table, FPWorkbenchTableSelection selection,
            bool headers = true, FPWorkbenchChangeSnapshot changes = null, CancellationToken cancellation = default)
        {
            if (selection == null) throw new ArgumentNullException(nameof(selection));
            int[] columns = selection.Resolve(table); long first = selection.FirstRow, count = selection.EndRow - first;
            if (count > MaximumRows) throw new ArgumentException("Clipboard copies are limited to 256 rows; use materialized export for larger ranges.");
            string[] names = new string[columns.Length]; for (int c = 0; c < columns.Length; c++) names[c] = table.ColumnName(columns[c]);
            var page = await table.ReadProjectedPageAsync(first, (int)count, columns, changes, cancellation).ConfigureAwait(false);
            return await Task.Run(() => {
                var text = new StringBuilder();
                void Field(string value) {
                    // Quoted TSV retains tabs, embedded newlines and literal quotes without rewriting measurements.
                    bool quote = value.IndexOfAny(QuotedCharacters) >= 0;
                    if (quote) text.Append('"');
                    foreach (char ch in value) { text.Append(ch); if (quote && ch == '"') text.Append('"'); if (text.Length > MaximumCharacters) throw new InvalidDataException("Clipboard text exceeds 1 MiB of UTF-16 payload."); }
                    if (quote) text.Append('"');
                }
                if (headers) { for (int c = 0; c < names.Length; c++) { if (c > 0) text.Append('\t'); Field(names[c]); } text.Append('\n'); }
                foreach (var row in page.Values) {
                    cancellation.ThrowIfCancellationRequested();
                    for (int c = 0; c < row.Length; c++) { if (c > 0) text.Append('\t'); Field(row[c].Masked ? "[masked]" : row[c].Text); }
                    text.Append('\n');
                    if (text.Length > MaximumCharacters) throw new InvalidDataException("Clipboard text exceeds 1 MiB of UTF-16 payload.");
                }
                cancellation.ThrowIfCancellationRequested(); return text.ToString();
            }, cancellation).ConfigureAwait(false);
        }
    }
}
