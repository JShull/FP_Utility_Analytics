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
        private readonly FPWorkbenchMemoryBudget memoryBudget;
        private readonly FPWorkbenchMemoryBudget scanBudget;
        private FPWorkbenchMemoryBudget.Reservation buildMemory, buildSlot;
        private FPWorkbenchMemoryBudget.Reservation queryMemory, viewMemory;
        private const long QueryReservationBytes = 1024 * 1024;
        public bool WaitingForMemory { get; private set; }
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
        public bool IsBusy => build != null || query != null || pending != null || WaitingForMemory;
        public bool IsBuilding => build != null || pending != null;
        public bool IsDisposed => disposed;
        public int LeaseCount => leases;
        public long Revision { get; private set; }
        public FPWorkbenchFigureSession(FPWorkbenchCsvDataset dataset, FPWorkbenchSeriesBinding binding, FPWorkbenchPlotSettings settings, FPWorkbenchChangeSnapshot changes = null, FPWorkbenchFormulaProgram calculations = null, FPWorkbenchMemoryBudget memoryBudget = null, FPWorkbenchMemoryBudget scanBudget = null)
        {
            this.dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            if (changes != null && changes.DatasetId != dataset.DatasetId) throw new ArgumentException("Corrections belong to another source revision.");
            this.changes = changes;
            if (calculations != null && calculations.DatasetId != dataset.DatasetId) throw new ArgumentException("Calculations belong to another source revision.");
            this.calculations = calculations;
            if (memoryBudget != null && memoryBudget.LimitBytes < 2 * QueryReservationBytes) throw new ArgumentException("A scalar query budget needs at least 2 MiB for replacement and retained views.");
            this.memoryBudget = memoryBudget;
            if (scanBudget != null && memoryBudget == null) throw new ArgumentException("Background scan admission also requires payload accounting.");
            this.scanBudget = scanBudget;
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
            generation++; buildCancel?.Cancel(); queryCancel?.Cancel(); Error = null; failedQuery = false; WaitingForMemory = false;
            if (fit) Settings.HasRange = false;
        }
        public void Retry()
        { Check(); Error = null; failedQuery = false; if (Series != null) ClearView(); }
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
            bool typedChanged = (calculations?.TypedSchemaFingerprint ?? "none") != (program?.TypedSchemaFingerprint ?? "none");
            calculations = program;
            if (!requestedBinding.HasDerivedY && !requestedBinding.HasDerivedX && !typedChanged) return;
            Series = null; RebuildRequested();
        }
        private void RebuildRequested()
        {
            ClearView(); Revision++;
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
                    Series = build.Result; ClearView();
                    if (!Settings.HasRange) Settings.Fit(Series.FirstX, Series.LastX);
                    Error = null; Revision++;
                }
                else if (build.IsFaulted) { var error = build.Exception.GetBaseException(); if (buildGeneration == generation) Error = error; }
                buildMemory?.Dispose(); buildMemory = null; buildSlot?.Dispose(); buildSlot = null;
                build = null; changed = true;
            }
            if (query != null && query.IsCompleted)
            {
                if (query.Status == TaskStatus.RanToCompletion && queryGeneration == generation && Settings.StartX == requestedStart && Settings.EndX == requestedEnd)
                {
                    ClearView(); View = query.Result; viewMemory = queryMemory; queryMemory = null;
                    viewMemory?.Complete(checked(4096L + View.Bins.Length * 128L)); Error = null; Revision++;
                    if (!Settings.LockY && !double.IsNaN(View.Minimum)) { Settings.MinimumY = View.Minimum; Settings.MaximumY = View.Maximum; }
                }
                else if (query.IsFaulted)
                {
                    var error = query.Exception.GetBaseException();
                    if (queryGeneration == generation && Settings.StartX == requestedStart && Settings.EndX == requestedEnd) { Error = error; failedQuery = true; }
                }
                queryMemory?.Dispose(); queryMemory = null; query = null; changed = true;
            }
            if (pending != null && build == null && query == null)
            {
                try {
                    bool admitted = TryAdmitBuild();
                    if (WaitingForMemory != !admitted) { WaitingForMemory = !admitted; changed = true; }
                    if (admitted) {
                        DisposeToken(ref buildCancel); buildCancel = new CancellationTokenSource();
                        buildGeneration = generation;
                        build = FPWorkbenchSeriesCache.BuildAsync(dataset, pending, buildCancel.Token, changes, calculations);
                        pending = null;
                    }
                } catch (Exception ex) {
                    buildMemory?.Dispose(); buildMemory = null; buildSlot?.Dispose(); buildSlot = null;
                    pending = null; WaitingForMemory = false; Error = ex; changed = true;
                }
            }
            if (failedQuery && (Settings.StartX != requestedStart || Settings.EndX != requestedEnd)) { Error = null; failedQuery = false; }
            if (build != null || pending != null || Series == null || Error != null) return changed;
            bool missing = View == null || View.StartX != Settings.StartX || View.EndX != Settings.EndX;
            if (query != null && (queryGeneration != generation || Settings.StartX != requestedStart || Settings.EndX != requestedEnd)) queryCancel?.Cancel();
            if (missing && query == null && (!failedQuery || Settings.StartX != requestedStart || Settings.EndX != requestedEnd))
            {
                try {
                    bool admitted = memoryBudget == null || memoryBudget.TryAcquire(QueryReservationBytes, out queryMemory);
                    if (WaitingForMemory != !admitted) { WaitingForMemory = !admitted; changed = true; }
                    if (admitted) {
                        failedQuery = false; DisposeToken(ref queryCancel); queryCancel = new CancellationTokenSource();
                        requestedStart = Settings.StartX; requestedEnd = Settings.EndX; queryGeneration = generation;
                        query = Series.QueryAsync(requestedStart, requestedEnd, 512, queryCancel.Token);
                    }
                }
                catch (ArgumentException ex) { queryMemory?.Dispose(); queryMemory = null; WaitingForMemory = false; Error = ex; failedQuery = true; }
            }
            return changed;
        }
        private void Close()
        {
            disposed = true; DisposeToken(ref buildCancel); DisposeToken(ref queryCancel);
            if (buildMemory != null) buildMemory.ReleaseAfter(build); else Observe(build);
            buildSlot?.ReleaseAfter(build); buildMemory = null; buildSlot = null; build = null;
            if (queryMemory != null) queryMemory.ReleaseAfter(query); else Observe(query);
            queryMemory = null; query = null; ClearView(); Series = null; pending = null; WaitingForMemory = false;
        }
        private bool TryAdmitBuild()
        {
            if (scanBudget == null) return true;
            if (!scanBudget.TryAcquire(1, out buildSlot)) return false;
            try {
                long bytes = checked(8L * 1024 * 1024 + dataset.ImportProfile.MaximumRecordBytes * 8L + dataset.ColumnCount * 128L);
                if (!memoryBudget.TryAcquire(bytes, out buildMemory)) { buildSlot.Dispose(); buildSlot = null; return false; }
                // Retain scratch bytes while the separate scan ticket owns background concurrency.
                buildMemory.Complete(bytes); return true;
            } catch { buildSlot.Dispose(); buildSlot = null; throw; }
        }
        private void ClearView() { View = null; viewMemory?.Dispose(); viewMemory = null; }
        private static void DisposeToken(ref CancellationTokenSource token)
        { if (token != null) { token.Cancel(); token.Dispose(); token = null; } }
        private static void Observe(Task task)
        { if (task != null) task.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted); }
        private void Check() { if (disposed) throw new ObjectDisposedException(nameof(FPWorkbenchFigureSession)); }
    }
}
