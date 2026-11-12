// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.Threading.Tasks;
    using System.Threading;
    /// <summary>Explicit domain-scoped accounting shared by Workbench views; contains no source contents.</summary>
    internal static class FPWorkbenchEditorResources
    {
        internal static readonly FPWorkbenchMemoryBudget Memory = new FPWorkbenchMemoryBudget(256L * 1024 * 1024, 2);
        // One unit is an admission token; scan payload bytes belong to Memory.
        internal static readonly FPWorkbenchMemoryBudget Scans = new FPWorkbenchMemoryBudget(2, 2);
        internal static long SourceBytes(FPWorkbenchCsvDataset source, long payloadBytes)
        {
            if (source == null || payloadBytes < 0) throw new ArgumentException("Source reservation needs a verified dataset and nonnegative payload.");
            return checked(payloadBytes + source.ImportProfile.MaximumRecordBytes * 8L + source.ColumnCount * 128L);
        }
        internal static long ProfileBytes(FPWorkbenchCsvProfile profile, long payloadBytes)
        {
            if (profile == null || payloadBytes < 0) throw new ArgumentException("A scan reservation needs a valid import profile and payload.");
            var verified = profile.Copy();
            return checked(payloadBytes + verified.MaximumRecordBytes * 8L + verified.MaximumColumns * 128L);
        }
        internal static bool TryOpenCache(string cache, FPWorkbenchCsvProfile profile, CancellationToken token,
            ref Task<FPWorkbenchCsvDataset> worker, ref ScanReservation reservation)
        {
            if (worker != null) return true;
            var bounds = profile ?? new FPWorkbenchCsvProfile { MaximumRecordBytes = 16 * 1024 * 1024, MaximumColumns = 4096 };
            if (!TryAcquireScan(ProfileBytes(bounds, 8L * 1024 * 1024), out reservation)) return false;
            try { worker = FPWorkbenchCsvDataset.OpenAsync(cache, token); return true; }
            catch { reservation.Dispose(); reservation = null; throw; }
        }
        internal static bool TryAcquireScan(long bytes, out ScanReservation reservation)
        {
            reservation = null;
            if (!Scans.TryAcquire(1, out var scan)) return false;
            FPWorkbenchMemoryBudget.Reservation memory = null;
            try {
                if (!Memory.TryAcquire(bytes, out memory)) { scan.Dispose(); return false; }
                // Long scans share byte accounting without occupying interactive-read slots.
                memory.Complete(bytes); reservation = new ScanReservation(memory, scan); return true;
            }
            catch { memory?.Dispose(); scan.Dispose(); throw; }
        }
        internal static bool TryStartSource<T>(FPWorkbenchCsvDataset source, ref Func<Task<T>> pending,
            ref Task<T> worker, ref ScanReservation reservation)
            => TryStartScan(SourceBytes(source, 8L * 1024 * 1024), ref pending, ref worker, ref reservation);
        internal static bool TryStartRead<T>(long bytes, ref Func<Task<T>> pending,
            ref Task<T> worker, ref FPWorkbenchMemoryBudget.Reservation reservation)
        {
            if (pending == null || worker != null) return true;
            try {
                if (!Memory.TryAcquire(bytes, out reservation)) return false;
                worker = pending(); pending = null; return true;
            } catch { reservation?.Dispose(); reservation = null; pending = null; throw; }
        }
        internal static bool TryStartScan<T>(long bytes, ref Func<Task<T>> pending,
            ref Task<T> worker, ref ScanReservation reservation)
        {
            if (pending == null || worker != null) return true;
            try {
                if (!TryAcquireScan(bytes, out reservation)) return false;
                worker = pending(); pending = null; return true;
            } catch { reservation?.Dispose(); reservation = null; pending = null; throw; }
        }
        internal sealed class ScanReservation : IDisposable
        {
            private readonly FPWorkbenchMemoryBudget.Reservation memory, scan;
            internal ScanReservation(FPWorkbenchMemoryBudget.Reservation memory, FPWorkbenchMemoryBudget.Reservation scan) { this.memory = memory; this.scan = scan; }
            internal void Complete(long retainedBytes) { memory.Reduce(retainedBytes); scan.Dispose(); }
            internal void ReleaseAfter(Task task) { memory.ReleaseAfter(task); scan.ReleaseAfter(task); }
            public void Dispose() { memory.Dispose(); scan.Dispose(); }
        }
    }
}
