// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Explicit equal-width bins: [left,right), with the final right edge included.</summary>
    public sealed class FPWorkbenchHistogramResult
    {
        public string DatasetId { get; internal set; }
        public string FormulaFingerprint { get; internal set; }
        public string CorrectionFingerprint { get; internal set; }
        public long FirstRow { get; internal set; }
        public long EndRow { get; internal set; }
        public int Column { get; internal set; }
        public bool Derived { get; internal set; }
        public FPWorkbenchInvalidPolicy InvalidPolicy { get; internal set; }
        public IReadOnlyList<double> Edges { get; internal set; }
        public IReadOnlyList<long> Counts { get; internal set; }
        public long IncludedCount { get; internal set; }
        public long InvalidCount { get; internal set; }
        public long UnderflowCount { get; internal set; }
        public long OverflowCount { get; internal set; }
        /// <summary>Density conditional on included samples. Empty histograms have density zero.</summary>
        public double Density(int bin)
        {
            if (bin < 0 || bin >= Counts.Count) throw new ArgumentOutOfRangeException(nameof(bin));
            if (IncludedCount == 0) return 0;
            double density = (Counts[bin] / (double)IncludedCount) / (Edges[bin + 1] - Edges[bin]);
            if (!FPWorkbenchSeriesBinding.Finite(density)) throw new OverflowException("Histogram density exceeds finite double range.");
            return density;
        }
    }

    public static class FPWorkbenchHistogram
    {
        public static Task<FPWorkbenchHistogramResult> CalculateAsync(FPWorkbenchCsvDataset dataset,
            FPWorkbenchFormulaProgram program, int column, bool derived, long firstRow, long endRow,
            double minimum, double maximum, int bins,
            FPWorkbenchInvalidPolicy invalidPolicy = FPWorkbenchInvalidPolicy.Reject,
            FPWorkbenchChangeSnapshot changes = null, IProgress<long> progress = null, CancellationToken cancellation = default)
        {
            if (dataset == null || program == null) throw new ArgumentNullException();
            if (dataset.DatasetId != program.DatasetId || column < 0 || column >= (derived ? program.ColumnCount : dataset.ColumnCount) ||
                firstRow < 0 || endRow < firstRow || endRow > dataset.RowCount || bins < 1 || bins > 4096 ||
                !FPWorkbenchSeriesBinding.Finite(minimum) || !FPWorkbenchSeriesBinding.Finite(maximum) || maximum <= minimum ||
                !FPWorkbenchSeriesBinding.Finite(maximum - minimum) ||
                (invalidPolicy != FPWorkbenchInvalidPolicy.Reject && invalidPolicy != FPWorkbenchInvalidPolicy.Skip))
                throw new ArgumentException("Invalid histogram source, column, range, bounds, bin count or policy.");
            var edges = new double[bins + 1]; edges[0] = minimum; edges[bins] = maximum;
            for (int i = 1; i < bins; i++) edges[i] = minimum + (i / (double)bins) * (maximum - minimum);
            for (int i = 1; i <= bins; i++) if (edges[i] <= edges[i - 1]) throw new ArgumentException("Histogram bin edges collapse at double precision; reduce bin count or change limits.");
            var evaluator = program.CreateEvaluator(changes);
            return dataset.RunCachedAsync(() =>
            {
                cancellation.ThrowIfCancellationRequested(); var counts = new long[bins];
                var result = new FPWorkbenchHistogramResult { DatasetId = dataset.DatasetId, FormulaFingerprint = program.Fingerprint,
                    CorrectionFingerprint = changes?.Fingerprint ?? "original", Column = column, Derived = derived,
                    InvalidPolicy = invalidPolicy, FirstRow = firstRow, EndRow = endRow,
                    Edges = Array.AsReadOnly(edges), Counts = Array.AsReadOnly(counts) };
                using var reader = dataset.OpenReaderAt(firstRow); var parser = dataset.Parser(reader, cancellation);
                for (long row = firstRow; row < endRow; row++)
                {
                    cancellation.ThrowIfCancellationRequested(); var values = parser.Read();
                    if (values == null || values.Length != dataset.ColumnCount) throw new InvalidDataException("Histogram source/index mismatch.");
                    evaluator.SetRow(values, row); var value = derived ? evaluator.Derived(column) : evaluator.Source(column);
                    if (!value.IsValid)
                    {
                        if (invalidPolicy == FPWorkbenchInvalidPolicy.Reject) throw new InvalidDataException("Histogram row " + row + ": " + value.Status);
                        result.InvalidCount++;
                    }
                    else if (value.Number < minimum) result.UnderflowCount++;
                    else if (value.Number > maximum) result.OverflowCount++;
                    else
                    {
                        // Upper-bound search gives exact edge semantics without quotient-rounding errors.
                        int low = 0, high = bins + 1;
                        while (low < high) { int mid = low + (high - low) / 2; if (edges[mid] <= value.Number) low = mid + 1; else high = mid; }
                        counts[Math.Min(bins - 1, low - 1)]++; result.IncludedCount++;
                    }
                    if (((row - firstRow + 1) & 4095) == 0) progress?.Report(row - firstRow + 1);
                }
                cancellation.ThrowIfCancellationRequested(); dataset.ValidateUnchanged(); progress?.Report(endRow - firstRow);
                return result;
            }, cancellation);
        }
    }
}
