// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Single-owner-thread figure controller shared by views. Workers are bounded and latest-request wins.</summary>
    public sealed class FPWorkbenchFigureSession
    {
        private readonly FPWorkbenchCsvDataset dataset;
        private Task<FPWorkbenchSeriesCache> build;
        private Task<FPWorkbenchSeriesView> query;
        private CancellationTokenSource buildCancel, queryCancel;
        private FPWorkbenchSeriesBinding pending;
        private FPWorkbenchSeriesBinding requestedBinding;
        private FPWorkbenchChangeSnapshot changes;
        private FPWorkbenchFormulaProgram calculations;
        private long generation, buildGeneration, queryGeneration;
        private double requestedStart, requestedEnd;
        private bool disposed, failedQuery;
        private int leases;
        private readonly FPWorkbenchPlotSettings observed = new FPWorkbenchPlotSettings();
        public FPWorkbenchPlotSettings Settings { get; }
        public FPWorkbenchSeriesCache Series { get; private set; }
        public FPWorkbenchSeriesView View { get; private set; }
        public Exception Error { get; private set; }
        public string DatasetId => dataset.DatasetId;
        public FPWorkbenchChangeSnapshot ChangeSnapshot => changes;
        public string YName => Series?.YName ?? "";
        public FPWorkbenchFormulaProgram CalculationProgram => calculations;
        public FPWorkbenchSeriesBinding RequestedBinding => requestedBinding.Copy(dataset.ColumnCount);
        public bool IsBusy => build != null || query != null || pending != null;
        public bool IsBuilding => build != null || pending != null;
        public bool IsDisposed => disposed;
        public int LeaseCount => leases;
        public long Revision { get; private set; }
        public FPWorkbenchFigureSession(FPWorkbenchCsvDataset dataset, FPWorkbenchSeriesBinding binding, FPWorkbenchPlotSettings settings, FPWorkbenchChangeSnapshot changes = null, FPWorkbenchFormulaProgram calculations = null)
        {
            this.dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            if (changes != null && changes.DatasetId != dataset.DatasetId) throw new ArgumentException("Corrections belong to another source revision.");
            this.changes = changes;
            if (calculations != null && calculations.DatasetId != dataset.DatasetId) throw new ArgumentException("Calculations belong to another source revision.");
            this.calculations = calculations;
            ApplyBinding(binding, false);
        }
        public Lease Retain()
        {
            Check(); leases++; return new Lease(this);
        }
        public sealed class Lease : IDisposable
        {
            private FPWorkbenchFigureSession owner;
            public FPWorkbenchFigureSession Session => owner ?? throw new ObjectDisposedException(nameof(Lease));
            internal Lease(FPWorkbenchFigureSession owner) { this.owner = owner; }
            public void Dispose()
            {
                if (owner == null) return;
                var current = owner; owner = null;
                if (--current.leases == 0) current.Close();
            }
        }
        public void ApplyBinding(FPWorkbenchSeriesBinding binding, bool fit = true)
        {
            Check(); var candidate = (binding ?? throw new ArgumentNullException(nameof(binding))).Copy(dataset.ColumnCount);
            candidate.ResolveDerivedY(calculations); candidate.ResolveDerivedX(calculations); pending = candidate;
            requestedBinding = pending;
            generation++; buildCancel?.Cancel(); queryCancel?.Cancel(); Error = null; failedQuery = false;
            if (fit) Settings.HasRange = false;
        }
        public void Retry()
        { Check(); Error = null; failedQuery = false; if (Series != null) View = null; }
        public void ApplyChanges(FPWorkbenchChangeSnapshot snapshot)
        {
            Check(); if (snapshot != null && snapshot.DatasetId != dataset.DatasetId) throw new ArgumentException("Correction source revision differs.");
            if (changes?.Fingerprint == snapshot?.Fingerprint) return;
            changes = snapshot; RebuildRequested();
        }
        public void ApplyCalculations(FPWorkbenchFormulaProgram program)
        {
            Check(); if (program != null && program.DatasetId != dataset.DatasetId) throw new ArgumentException("Calculation source differs.");
            if (calculations?.Fingerprint == program?.Fingerprint) return;
            calculations = program;
            if (!requestedBinding.HasDerivedY && !requestedBinding.HasDerivedX) return;
            Series = null; RebuildRequested();
        }
        private void RebuildRequested()
        {
            View = null; Revision++;
            try { ApplyBinding(requestedBinding, false); }
            catch (ArgumentException ex)
            { generation++; buildCancel?.Cancel(); queryCancel?.Cancel(); pending = null; Error = ex; }
        }
        public bool Poll()
        {
            Check(); bool changed = false;
            if (!observed.SameAs(Settings)) { observed.CopyFrom(Settings); Revision++; changed = true; }
            if (build != null && build.IsCompleted)
            {
                if (build.Status == TaskStatus.RanToCompletion && buildGeneration == generation)
                {
                    Series = build.Result; View = null;
                    if (!Settings.HasRange) Settings.Fit(Series.FirstX, Series.LastX);
                    Error = null; Revision++;
                }
                else if (build.IsFaulted) { var error = build.Exception.GetBaseException(); if (buildGeneration == generation) Error = error; }
                build = null; changed = true;
            }
            if (query != null && query.IsCompleted)
            {
                if (query.Status == TaskStatus.RanToCompletion && queryGeneration == generation && Settings.StartX == requestedStart && Settings.EndX == requestedEnd)
                {
                    View = query.Result; Error = null; Revision++;
                    if (!Settings.LockY && !double.IsNaN(View.Minimum)) { Settings.MinimumY = View.Minimum; Settings.MaximumY = View.Maximum; }
                }
                else if (query.IsFaulted)
                {
                    var error = query.Exception.GetBaseException();
                    if (queryGeneration == generation && Settings.StartX == requestedStart && Settings.EndX == requestedEnd) { Error = error; failedQuery = true; }
                }
                query = null; changed = true;
            }
            if (pending != null && build == null && query == null)
            {
                DisposeToken(ref buildCancel); buildCancel = new CancellationTokenSource();
                buildGeneration = generation; var binding = pending; pending = null;
                build = FPWorkbenchSeriesCache.BuildAsync(dataset, binding, buildCancel.Token, changes, calculations);
            }
            if (failedQuery && (Settings.StartX != requestedStart || Settings.EndX != requestedEnd)) { Error = null; failedQuery = false; }
            if (build != null || pending != null || Series == null || Error != null) return changed;
            bool missing = View == null || View.StartX != Settings.StartX || View.EndX != Settings.EndX;
            if (query != null && (queryGeneration != generation || Settings.StartX != requestedStart || Settings.EndX != requestedEnd)) queryCancel?.Cancel();
            if (missing && query == null && (!failedQuery || Settings.StartX != requestedStart || Settings.EndX != requestedEnd))
            {
                failedQuery = false; DisposeToken(ref queryCancel); queryCancel = new CancellationTokenSource();
                requestedStart = Settings.StartX; requestedEnd = Settings.EndX; queryGeneration = generation;
                try { query = Series.QueryAsync(requestedStart, requestedEnd, 512, queryCancel.Token); }
                catch (ArgumentException ex) { Error = ex; failedQuery = true; }
            }
            return changed;
        }
        private void Close()
        {
            disposed = true; DisposeToken(ref buildCancel); DisposeToken(ref queryCancel);
            Observe(build); Observe(query); View = null; Series = null; pending = null;
        }
        private static void DisposeToken(ref CancellationTokenSource token)
        { if (token != null) { token.Cancel(); token.Dispose(); token = null; } }
        private static void Observe(Task task)
        { if (task != null) task.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted); }
        private void Check() { if (disposed) throw new ObjectDisposedException(nameof(FPWorkbenchFigureSession)); }
    }
}
