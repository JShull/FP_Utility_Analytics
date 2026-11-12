// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>A bounded calculated tile, independent of the visible source-column page.</summary>
    public sealed class FPWorkbenchCalculatedPage
    {
        public string DatasetId { get; }
        public string FormulaFingerprint { get; }
        public string CorrectionFingerprint { get; }
        public long FirstRow { get; }
        public int FirstColumn { get; }
        public FPWorkbenchFormulaValue[][] Values { get; }
        private FPWorkbenchCalculatedPage(string dataset, string formula, string corrections, long row, int column, FPWorkbenchFormulaValue[][] values)
        { DatasetId = dataset; FormulaFingerprint = formula; CorrectionFingerprint = corrections; FirstRow = row; FirstColumn = column; Values = values; }

        public static Task<FPWorkbenchCalculatedPage> ReadAsync(FPWorkbenchCsvDataset dataset, FPWorkbenchFormulaProgram program,
            long firstRow, int rowCount, int firstColumn, int columnCount,
            FPWorkbenchChangeSnapshot changes = null, CancellationToken cancellation = default)
        {
            if (dataset == null || program == null) throw new ArgumentNullException();
            if (program.DatasetId != dataset.DatasetId || firstRow < 0 || firstRow > dataset.RowCount || rowCount < 0 || rowCount > 256 ||
                firstColumn < 0 || columnCount < 1 || columnCount > 32 || firstColumn > program.ColumnCount - columnCount)
                throw new ArgumentOutOfRangeException("Calculated page must fit its source and 256-row/32-column limits.");
            var evaluator = program.CreateEvaluator(changes);
            int count = (int)Math.Min(rowCount, dataset.RowCount - firstRow);
            return dataset.RunCachedAsync(() =>
            {
                cancellation.ThrowIfCancellationRequested();
                using var reader = dataset.OpenReaderAt(firstRow); var parser = dataset.Parser(reader, cancellation);
                var values = new FPWorkbenchFormulaValue[count][];
                for (int r = 0; r < count; r++)
                {
                    cancellation.ThrowIfCancellationRequested(); var record = parser.Read();
                    if (record == null || record.Length != dataset.ColumnCount) throw new InvalidDataException("Calculated page source/index mismatch.");
                    evaluator.SetRow(record, firstRow + r); values[r] = new FPWorkbenchFormulaValue[columnCount];
                    for (int c = 0; c < columnCount; c++) values[r][c] = evaluator.Derived(firstColumn + c);
                }
                cancellation.ThrowIfCancellationRequested(); dataset.ValidateUnchanged();
                return new FPWorkbenchCalculatedPage(dataset.DatasetId, program.Fingerprint, changes?.Fingerprint ?? "original", firstRow, firstColumn, values);
            }, cancellation);
        }
    }
}
