// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    using System.Globalization;

    /// <summary>Source-addressed cell identity, independent of table sort order and labels.</summary>
    public readonly struct FPWorkbenchCell : IEquatable<FPWorkbenchCell>
    {
        public string DatasetId { get; }
        public long Row { get; }
        public int Column { get; }
        public FPWorkbenchCell(string datasetId, long row, int column)
        {
            if (string.IsNullOrEmpty(datasetId) || row < 0 || column < 0) throw new ArgumentException("A source revision and nonnegative cell coordinates are required.");
            DatasetId = datasetId; Row = row; Column = column;
        }
        public string ColumnId => DatasetId + ":column:" + Column.ToString(CultureInfo.InvariantCulture);
        public bool Equals(FPWorkbenchCell other) => DatasetId == other.DatasetId && Row == other.Row && Column == other.Column;
        public override bool Equals(object obj) => obj is FPWorkbenchCell cell && Equals(cell);
        public override int GetHashCode() => (DatasetId ?? "").GetHashCode() ^ Row.GetHashCode() ^ Column;
    }

    public readonly struct FPWorkbenchRange
    {
        public long FirstRow { get; }
        public long EndRow { get; }
        public int FirstColumn { get; }
        public int EndColumn { get; }
        public FPWorkbenchRange(long firstRow, long endRow, int firstColumn, int endColumn)
        {
            if (firstRow < 0 || endRow < firstRow || firstColumn < 0 || endColumn < firstColumn) throw new ArgumentException("Ranges are nonnegative and half-open.");
            FirstRow = firstRow; EndRow = endRow; FirstColumn = firstColumn; EndColumn = endColumn;
        }
    }

    [Serializable]
    public sealed class FPWorkbenchCsvProfile
    {
        public char Delimiter = ',';
        public int EncodingCodePage = 65001;
        public int MaximumRecordBytes = 1024 * 1024;
        public int MaximumColumns = 256;
        public FPWorkbenchCsvProfile Copy()
        {
            if (Delimiter == '"' || Delimiter == '\r' || Delimiter == '\n' || Delimiter > 127 || Delimiter == '\0') throw new ArgumentException("Choose an ASCII delimiter other than quote/newline.");
            if (MaximumRecordBytes < 64 || MaximumRecordBytes > 16 * 1024 * 1024 || MaximumColumns < 1 || MaximumColumns > 4096) throw new ArgumentException("CSV bounds are invalid.");
            return (FPWorkbenchCsvProfile)MemberwiseClone();
        }
    }

    /// <summary>A bounded row/column tile. Values retain source text, including integer precision.</summary>
    public sealed class FPWorkbenchPage
    {
        public string DatasetId { get; }
        public long FirstRow { get; }
        public int FirstColumn { get; }
        public string[][] Values { get; }
        public int RowCount => Values.Length;
        public long CharacterCount { get; }
        internal FPWorkbenchPage(string id, long first, int column, string[][] values)
        {
            DatasetId = id; FirstRow = first; FirstColumn = column; Values = values;
            foreach (var row in values) foreach (string value in row) CharacterCount += value.Length;
        }
    }
}
