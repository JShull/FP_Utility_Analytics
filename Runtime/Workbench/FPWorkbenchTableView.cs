// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    public readonly struct FPWorkbenchTableValue
    {
        public string Text { get; }
        public string Original { get; }
        public bool Calculated { get; }
        public bool Masked { get; }
        public FPWorkbenchTypedCell Typed { get; }
        /// <summary>Only meaningful for calculated cells; physical source text is not implicitly typed.</summary>
        public FPWorkbenchFormulaStatus CalculationStatus { get; }
        internal FPWorkbenchTableValue(string text, string original, bool calculated, bool masked, FPWorkbenchFormulaStatus status, FPWorkbenchTypedCell typed = null)
        { Text = text; Original = original; Calculated = calculated; Masked = masked; CalculationStatus = status; Typed = typed; }
    }
    public sealed class FPWorkbenchTypedCell
    {
        public FPWorkbenchTypedValue Original { get; }
        public FPWorkbenchTypedValue Effective { get; }
        internal FPWorkbenchTypedCell(FPWorkbenchTypedValue original, FPWorkbenchTypedValue effective) { Original = original; Effective = effective; }
    }
    public sealed class FPWorkbenchTablePage
    {
        public string DatasetId { get; internal set; }
        public string FormulaFingerprint { get; internal set; }
        public string CorrectionFingerprint { get; internal set; }
        public string TypedSchemaFingerprint { get; internal set; }
        public long FirstRow { get; internal set; }
        public int FirstColumn { get; internal set; }
        /// <summary>Virtual schema ordinals in displayed order. Each entry retains its original source/calculated identity.</summary>
        public int[] Columns { get; internal set; }
        public FPWorkbenchTableValue[][] Values { get; internal set; }
        public long CharacterCount { get; internal set; }
    }
    /// <summary>Virtual source-plus-calculated schema. Physical cells retain exact original text and source identity.</summary>
    public sealed class FPWorkbenchTableView
    {
        private readonly FPWorkbenchCsvDataset dataset;
        private readonly FPWorkbenchFormulaProgram program;
        private readonly FPWorkbenchCompiledSchema typedSchema;
        public string TypedSchemaFingerprint => typedSchema?.Fingerprint ?? "none";
        public string DatasetId => dataset.DatasetId;
        public long RowCount => dataset.RowCount;
        public int SourceColumnCount => dataset.ColumnCount;
        public int ColumnCount => SourceColumnCount + (program?.ColumnCount ?? 0);
        public FPWorkbenchTableView(FPWorkbenchCsvDataset source, FPWorkbenchFormulaProgram calculations = null, FPWorkbenchCompiledSchema typedSchema = null)
        {
            dataset = source ?? throw new ArgumentNullException(nameof(source)); program = calculations;
            if (program != null && program.DatasetId != source.DatasetId) throw new ArgumentException("Table calculations belong to another source revision.");
            if (typedSchema != null && typedSchema.DatasetId != source.DatasetId) throw new ArgumentException("Typed table schema belongs to another source revision.");
            this.typedSchema = typedSchema;
        }
        public bool IsCalculated(int column) { ValidateColumn(column); return column >= SourceColumnCount; }
        public string ColumnId(int column) => IsCalculated(column) ? DatasetId + ":derived:" + program.ColumnId(column - SourceColumnCount) : new FPWorkbenchCell(DatasetId, 0, column).ColumnId;
        public string ColumnName(int column) => IsCalculated(column) ? program.Name(column - SourceColumnCount) : dataset.GetColumnName(column);
        public FPWorkbenchCell SourceCell(long row, int column)
        {
            if (IsCalculated(column)) throw new InvalidOperationException("Calculated cells are definitions, not source-cell edit targets.");
            if (row < 0 || row >= RowCount) throw new ArgumentOutOfRangeException(nameof(row));
            return dataset.Cell(row, column);
        }
        private void ValidateColumn(int column) { if (column < 0 || column >= ColumnCount) throw new ArgumentOutOfRangeException(nameof(column)); }
        public Task<FPWorkbenchTablePage> ReadPageAsync(long firstRow, int rowCount, int firstColumn, int columnCount,
            FPWorkbenchChangeSnapshot changes = null, CancellationToken cancellation = default)
        {
            if (firstRow < 0 || firstRow > RowCount || rowCount < 0 || rowCount > 256 || firstColumn < 0 || columnCount < 1 || columnCount > 32 || firstColumn > ColumnCount - columnCount)
                throw new ArgumentOutOfRangeException("Table tile must fit its virtual schema and 256-row/32-column limits.");
            var columns = new int[columnCount]; for (int c = 0; c < columnCount; c++) columns[c] = firstColumn + c;
            return ReadProjectedPageAsync(firstRow, rowCount, columns, changes, cancellation);
        }
        /// <summary>Reads up to 32 distinct arbitrary virtual columns in one bounded source pass, preserving supplied order.</summary>
        public Task<FPWorkbenchTablePage> ReadProjectedPageAsync(long firstRow, int rowCount, int[] selectedColumns,
            FPWorkbenchChangeSnapshot changes = null, CancellationToken cancellation = default)
        {
            if (firstRow < 0 || firstRow > RowCount || rowCount < 0 || rowCount > 256 || selectedColumns == null || selectedColumns.Length < 1 || selectedColumns.Length > 32)
                throw new ArgumentOutOfRangeException("Projected table tile must fit 256-row/32-column bounds.");
            var columns = (int[])selectedColumns.Clone(); var unique = new System.Collections.Generic.HashSet<int>();
            foreach (int c in columns) { ValidateColumn(c); if (!unique.Add(c)) throw new ArgumentException("Projected columns must be distinct."); }
            int columnCount = columns.Length;
            if (changes != null && changes.DatasetId != DatasetId) throw new ArgumentException("Table correction source differs.");
            var evaluator = program?.CreateEvaluator(changes); int rows = (int)Math.Min(rowCount, RowCount - firstRow);
            return dataset.RunCachedAsync(() =>
            {
                cancellation.ThrowIfCancellationRequested(); using var reader = dataset.OpenReaderAt(firstRow); var parser = dataset.Parser(reader, cancellation);
                var values = new FPWorkbenchTableValue[rows][]; long characters = 0;
                for (int r = 0; r < rows; r++)
                {
                    cancellation.ThrowIfCancellationRequested(); var record = parser.Read();
                    if (record == null || record.Length != SourceColumnCount) throw new InvalidDataException("Virtual table source/index mismatch.");
                    evaluator?.SetRow(record, firstRow + r); values[r] = new FPWorkbenchTableValue[columnCount];
                    for (int c = 0; c < columnCount; c++)
                    {
                        int column = columns[c]; FPWorkbenchTableValue cell;
                        if (column >= SourceColumnCount)
                        { var number = evaluator.Derived(column - SourceColumnCount); cell = new FPWorkbenchTableValue(number.ToString(), null, true, false, number.Status); }
                        else
                        {
                            string original = record[column], text = original; bool masked = false, explicitMissing = false;
                            if (changes != null && changes.TryGet(dataset.Cell(firstRow + r, column), original, out string effective, out masked, out var kind))
                            { text = effective; explicitMissing = kind == FPWorkbenchEditKind.Missing; }
                            FPWorkbenchTypedCell typed = null;
                            if (typedSchema != null && typedSchema.TryGetParser(column, out var columnParser))
                                typed = new FPWorkbenchTypedCell(columnParser.Parse(original), explicitMissing
                                    ? new FPWorkbenchTypedValue(text, columnParser.Type, FPWorkbenchValueState.Missing, false)
                                    : columnParser.Parse(masked ? original : text, masked));
                            cell = new FPWorkbenchTableValue(text, original, false, masked, FPWorkbenchFormulaStatus.Valid, typed);
                        }
                        characters += cell.Text.Length;
                        if (cell.Original != null && !ReferenceEquals(cell.Text, cell.Original)) characters += cell.Original.Length;
                        if (characters > 2 * 1024 * 1024) throw new InvalidDataException("Virtual table page exceeds 4 MiB of retained text; request fewer cells.");
                        values[r][c] = cell;
                    }
                }
                cancellation.ThrowIfCancellationRequested(); dataset.ValidateUnchanged();
                return new FPWorkbenchTablePage { DatasetId = DatasetId, FormulaFingerprint = program?.Fingerprint ?? "none", TypedSchemaFingerprint = TypedSchemaFingerprint,
                    CorrectionFingerprint = changes?.Fingerprint ?? "original", FirstRow = firstRow, FirstColumn = columns[0], Columns = (int[])columns.Clone(), Values = values, CharacterCount = characters };
            }, cancellation);
        }
    }
}
