// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    public enum FPWorkbenchInvalidPolicy { Reject, Skip }

    /// <summary>One immutable full-range result; empty numeric aggregates are NaN, count and sum are zero.</summary>
    public sealed class FPWorkbenchReductionResult
    {
        public string DatasetId { get; internal set; }
        public string FormulaFingerprint { get; internal set; }
        public string CorrectionFingerprint { get; internal set; }
        public int Column { get; internal set; }
        public bool Derived { get; internal set; }
        public FPWorkbenchInvalidPolicy InvalidPolicy { get; internal set; }
        public long FirstRow { get; internal set; }
        public long EndRow { get; internal set; }
        public long Count { get; internal set; }
        public long InvalidCount { get; internal set; }
        public double Sum { get; internal set; }
        public double Minimum { get; internal set; } = double.NaN;
        public double Maximum { get; internal set; } = double.NaN;
        public double Mean => Count == 0 ? double.NaN : Sum / Count;
    }

    /// <summary>Sequential background reduction with bounded row memory and compensated double summation.</summary>
    public static class FPWorkbenchReduction
    {
        public static Task<FPWorkbenchReductionResult> CalculateAsync(FPWorkbenchCsvDataset dataset,
            FPWorkbenchFormulaProgram program, int column, bool derived, long firstRow, long endRow,
            FPWorkbenchInvalidPolicy invalidPolicy = FPWorkbenchInvalidPolicy.Reject,
            FPWorkbenchChangeSnapshot changes = null, IProgress<long> progress = null, CancellationToken cancellation = default)
        {
            if (dataset == null || program == null) throw new ArgumentNullException();
            if (program.DatasetId != dataset.DatasetId || firstRow < 0 || endRow < firstRow || endRow > dataset.RowCount ||
                column < 0 || column >= (derived ? program.ColumnCount : dataset.ColumnCount) ||
                (invalidPolicy != FPWorkbenchInvalidPolicy.Reject && invalidPolicy != FPWorkbenchInvalidPolicy.Skip)) throw new ArgumentException("Invalid reduction source, column, range or policy.");
            var evaluator = program.CreateEvaluator(changes);
            return dataset.RunCachedAsync(() =>
            {
                cancellation.ThrowIfCancellationRequested();
                var result = new FPWorkbenchReductionResult { DatasetId = dataset.DatasetId, FormulaFingerprint = program.Fingerprint,
                    CorrectionFingerprint = changes?.Fingerprint ?? "original", Column = column, Derived = derived,
                    InvalidPolicy = invalidPolicy, FirstRow = firstRow, EndRow = endRow };
                using var reader = dataset.OpenReaderAt(firstRow); var parser = dataset.Parser(reader, cancellation);
                double sum = 0, compensation = 0;
                for (long row = firstRow; row < endRow; row++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var values = parser.Read();
                    if (values == null || values.Length != dataset.ColumnCount) throw new InvalidDataException("Reduction source row differs from its index.");
                    evaluator.SetRow(values, row); var value = derived ? evaluator.Derived(column) : evaluator.Source(column);
                    if (!value.IsValid)
                    {
                        if (invalidPolicy == FPWorkbenchInvalidPolicy.Reject) throw new InvalidDataException("Reduction row " + row + ": " + value.Status);
                        result.InvalidCount++;
                    }
                    else
                    {
                        double next = sum + value.Number;
                        compensation += Math.Abs(sum) >= Math.Abs(value.Number) ? (sum - next) + value.Number : (value.Number - next) + sum;
                        sum = next;
                        if (!FPWorkbenchSeriesBinding.Finite(sum) || !FPWorkbenchSeriesBinding.Finite(compensation)) throw new OverflowException("Reduction exceeds finite double precision.");
                        if (result.Count == 0) result.Minimum = result.Maximum = value.Number;
                        else { result.Minimum = Math.Min(result.Minimum, value.Number); result.Maximum = Math.Max(result.Maximum, value.Number); }
                        result.Count++;
                    }
                    if (((row - firstRow + 1) & 4095) == 0) progress?.Report(row - firstRow + 1);
                }
                result.Sum = sum + compensation;
                if (!FPWorkbenchSeriesBinding.Finite(result.Sum)) throw new OverflowException("Reduction exceeds finite double precision.");
                cancellation.ThrowIfCancellationRequested(); dataset.ValidateUnchanged(); progress?.Report(endRow - firstRow);
                return result;
            }, cancellation);
        }
    }
}
