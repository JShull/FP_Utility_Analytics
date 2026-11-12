// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>One coherent viewport response, never a mixture of old/new per-series requests.</summary>
    public sealed class FPWorkbenchOverlayView
    {
        public string DatasetId { get; internal set; }
        public string CorrectionFingerprint { get; internal set; }
        public string FormulaFingerprint { get; internal set; }
        public double StartX { get; internal set; }
        public double EndX { get; internal set; }
        public double Minimum { get; internal set; } = double.NaN;
        public double Maximum { get; internal set; } = double.NaN;
        public IReadOnlyList<FPWorkbenchSeriesView> Series { get; internal set; }
        public IReadOnlyList<int> YColumns { get; internal set; }
        public IReadOnlyList<string> YDerivedIds { get; internal set; }
    }

    /// <summary>Up to eight independent Y series sharing one verified dataset and X binding.</summary>
    public sealed class FPWorkbenchOverlay
    {
        private readonly FPWorkbenchSeriesCache[] series;
        private readonly int[] columns;
        private readonly string[] derivedIds;
        public string DatasetId { get; }
        public string CorrectionFingerprint { get; }
        public string FormulaFingerprint { get; }
        public IReadOnlyList<int> YColumns { get; }
        public IReadOnlyList<string> YDerivedIds { get; }
        public double FirstX => series[0].FirstX;
        public double LastX => series[0].LastX;
        private FPWorkbenchOverlay(string dataset, string correction, string formula, int[] columns, string[] derived, FPWorkbenchSeriesCache[] series)
        { DatasetId = dataset; CorrectionFingerprint = correction; FormulaFingerprint = formula; this.columns = columns; derivedIds = derived; this.series = series; YColumns = Array.AsReadOnly(columns); YDerivedIds = Array.AsReadOnly(derived); }

        public static async Task<FPWorkbenchOverlay> BuildAsync(FPWorkbenchCsvDataset dataset, FPWorkbenchSeriesBinding commonX,
            int[] yColumns, FPWorkbenchChangeSnapshot changes = null, CancellationToken cancellation = default, FPWorkbenchFormulaProgram calculations = null, string[] yDerivedIds = null)
        {
            if (dataset == null || commonX == null || yColumns == null) throw new ArgumentNullException();
            if (yColumns.Length < 1 || yColumns.Length > 8 || changes != null && changes.DatasetId != dataset.DatasetId) throw new ArgumentException("An overlay needs 1–8 series from one source revision.");
            if (yDerivedIds != null && yDerivedIds.Length != yColumns.Length || calculations != null && calculations.DatasetId != dataset.DatasetId) throw new ArgumentException("Overlay formula source or derived-ID count differs.");
            int[] columns = (int[])yColumns.Clone(); string[] derived = yDerivedIds == null ? new string[columns.Length] : (string[])yDerivedIds.Clone(); var unique = new HashSet<string>(StringComparer.Ordinal);
            var binding = commonX.Copy(dataset.ColumnCount); binding.YDerivedId = "";
            bool usesProgram = binding.HasDerivedX || calculations != null && calculations.TypedSchemaFingerprint != "none";
            binding.ResolveDerivedX(calculations);
            for (int i = 0; i < columns.Length; i++)
            {
                derived[i] ??= "";
                if (derived[i].Length > 128) throw new ArgumentException("Invalid overlay derived ID.");
                bool calculated = derived[i].Length > 0; usesProgram |= calculated;
                if (columns[i] < 0 || columns[i] >= dataset.ColumnCount || !unique.Add(calculated ? "D:" + derived[i] : "C:" + columns[i])) throw new ArgumentException("Overlay columns must be distinct physical/derived identities.");
                if (calculated) new FPWorkbenchSeriesBinding { YDerivedId = derived[i] }.ResolveDerivedY(calculations);
            }
            var caches = new FPWorkbenchSeriesCache[columns.Length];
            // Admission is sequential to avoid a separate simultaneous whole-CSV scan per series.
            for (int i = 0; i < columns.Length; i++)
            {
                cancellation.ThrowIfCancellationRequested(); binding.YColumn = columns[i]; binding.YDerivedId = derived[i];
                caches[i] = await FPWorkbenchSeriesCache.BuildAsync(dataset, binding.Copy(dataset.ColumnCount), cancellation, changes, calculations).ConfigureAwait(false);
                if (caches[i].FirstX != caches[0].FirstX || caches[i].LastX != caches[0].LastX) throw new InvalidOperationException("Overlay X extents differ despite a common source binding.");
            }
            cancellation.ThrowIfCancellationRequested();
            return new FPWorkbenchOverlay(dataset.DatasetId, changes?.Fingerprint ?? "original", usesProgram ? calculations.Fingerprint : "none", columns, derived, caches);
        }

        public async Task<FPWorkbenchOverlayView> QueryAsync(double start, double end, int pixelWidth, CancellationToken cancellation = default)
        {
            if (!FPWorkbenchSeriesBinding.Finite(start) || !FPWorkbenchSeriesBinding.Finite(end) || end <= start || pixelWidth < 1 || pixelWidth > 4096)
                throw new ArgumentOutOfRangeException("Overlay viewport must be finite and 1–4096 pixels wide.");
            cancellation.ThrowIfCancellationRequested();
            var views = new FPWorkbenchSeriesView[series.Length];
            // One admitted overlay job must not fan out into eight independent disk workers.
            for (int i = 0; i < series.Length; i++) {
                cancellation.ThrowIfCancellationRequested();
                views[i] = await series[i].QueryAsync(start, end, pixelWidth, cancellation).ConfigureAwait(false);
            }
            cancellation.ThrowIfCancellationRequested();
            var result = new FPWorkbenchOverlayView { DatasetId = DatasetId, CorrectionFingerprint = CorrectionFingerprint, FormulaFingerprint = FormulaFingerprint,
                StartX = start, EndX = end, Series = Array.AsReadOnly(views), YColumns = Array.AsReadOnly((int[])columns.Clone()), YDerivedIds = Array.AsReadOnly((string[])derivedIds.Clone()) };
            for (int i = 0; i < views.Length; i++)
            {
                var view = views[i];
                if (view.DatasetId != DatasetId || view.StartX != start || view.EndX != end || view.OriginUtcTicks != views[0].OriginUtcTicks ||
                    view.CorrectionFingerprint != CorrectionFingerprint || view.FormulaFingerprint != series[i].FormulaFingerprint)
                    throw new InvalidOperationException("Overlay response identity, viewport or UTC origin differs.");
                if (double.IsNaN(view.Minimum)) continue;
                if (double.IsNaN(result.Minimum)) { result.Minimum = view.Minimum; result.Maximum = view.Maximum; }
                else { result.Minimum = Math.Min(result.Minimum, view.Minimum); result.Maximum = Math.Max(result.Maximum, view.Maximum); }
            }
            return result;
        }
    }
}
