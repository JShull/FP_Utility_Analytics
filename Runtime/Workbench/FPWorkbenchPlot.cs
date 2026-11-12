// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    public struct FPWorkbenchPlotBin
    {
        public long Count, InvalidCount, FirstRow, MinimumRow, MaximumRow;
        public double Minimum, Maximum;
    }

    public sealed class FPWorkbenchPlotSummary
    {
        public string DatasetId { get; internal set; }
        public long FirstRow { get; internal set; }
        public long EndRow { get; internal set; }
        public int Column { get; internal set; }
        public FPWorkbenchPlotBin[] Bins { get; internal set; }
        public double Minimum { get; internal set; } = double.NaN;
        public double Maximum { get; internal set; } = double.NaN;
        public long InvalidCount { get; internal set; }
    }

    /// <summary>Bounded row-domain extrema. Exact values still come from source-addressed cells.</summary>
    public static class FPWorkbenchPlot
    {
        public static Task<FPWorkbenchPlotSummary> SummarizeAsync(FPWorkbenchCsvDataset dataset, int column,
            long firstRow, long endRow, int binCount = 512, CancellationToken cancellation = default)
        {
            if (dataset == null) throw new ArgumentNullException(nameof(dataset));
            if (column < 0 || column >= dataset.ColumnCount || firstRow < 0 || endRow <= firstRow || endRow > dataset.RowCount || binCount < 1 || binCount > 4096) throw new ArgumentOutOfRangeException();
            return Task.Run(() =>
            {
                var summary = new FPWorkbenchPlotSummary { DatasetId = dataset.DatasetId, Column = column, FirstRow = firstRow, EndRow = endRow, Bins = new FPWorkbenchPlotBin[binCount] };
                using var source = dataset.OpenReader(); dataset.SkipHeader(source, cancellation);
                var parser = dataset.Parser(source, cancellation);
                for (long row = 0; row < endRow; row++)
                {
                    var values = parser.Read();
                    if (values == null || values.Length != dataset.ColumnCount) throw new InvalidDataException("Source changed while summarizing.");
                    if (row < firstRow) continue;
                    int index = Math.Min(binCount - 1, (int)((row - firstRow) / (double)(endRow - firstRow) * binCount));
                    var bin = summary.Bins[index];
                    if (!double.TryParse(values[column], NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || double.IsNaN(value) || double.IsInfinity(value))
                    { bin.InvalidCount++; summary.InvalidCount++; }
                    else
                    {
                        if (bin.Count == 0) { bin.Minimum = bin.Maximum = value; bin.FirstRow = bin.MinimumRow = bin.MaximumRow = row; }
                        if (value < bin.Minimum) { bin.Minimum = value; bin.MinimumRow = row; }
                        if (value > bin.Maximum) { bin.Maximum = value; bin.MaximumRow = row; }
                        bin.Count++;
                        if (double.IsNaN(summary.Minimum) || value < summary.Minimum) summary.Minimum = value;
                        if (double.IsNaN(summary.Maximum) || value > summary.Maximum) summary.Maximum = value;
                    }
                    summary.Bins[index] = bin;
                }
                dataset.ValidateUnchanged();
                return summary;
            }, cancellation);
        }

        public static double[] Ticks(double minimum, double maximum, double spacing = 0, int maximumTicks = 128)
        {
            if (!Finite(minimum) || !Finite(maximum) || maximum <= minimum || maximumTicks < 2 || maximumTicks > 4096 || !Finite(spacing) || spacing < 0) throw new ArgumentException("Invalid axis/tick settings.");
            double span = maximum - minimum;
            if (!Finite(span)) throw new ArgumentException("Axis span must be finite.");
            if (spacing == 0)
            {
                double step = Math.Pow(10, Math.Floor(Math.Log10(span / 6)));
                double fraction = span / 6 / step;
                spacing = step * (fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10);
            }
            if (!Finite(spacing) || spacing <= 0) throw new ArgumentException("Tick spacing is too small.");
            double first = Math.Ceiling(minimum / spacing) * spacing;
            double count = Math.Floor((maximum - first) / spacing) + 1;
            if (!Finite(first) || !Finite(count) || count > maximumTicks) throw new ArgumentException("Tick spacing would exceed the rendered tick limit.");
            var result = new double[(int)Math.Max(0, count)];
            for (int i = 0; i < result.Length; i++) result[i] = first + i * spacing;
            return result;
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
