// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Caller-owned accounting for reserved worker/retained payloads; not a measurement of CLR or GPU memory.</summary>
    public sealed class FPWorkbenchMemoryBudget
    {
        private readonly object gate = new object();
        public long LimitBytes { get; }
        public int MaximumWorkers { get; }
        private long used, peak;
        private int workers, leases;
        public long UsedBytes { get { lock (gate) return used; } }
        public long PeakBytes { get { lock (gate) return peak; } }
        public int ActiveWorkers { get { lock (gate) return workers; } }
        public int LeaseCount { get { lock (gate) return leases; } }
        public FPWorkbenchMemoryBudget(long limitBytes, int maximumWorkers)
        {
            if (limitBytes < 1 || maximumWorkers < 1) throw new ArgumentOutOfRangeException();
            LimitBytes = limitBytes; MaximumWorkers = maximumWorkers;
        }
        /// <summary>No queue or allocation-heavy work is started on refusal. Retry only while the request remains relevant.</summary>
        public bool TryAcquire(long maximumBytes, out Reservation reservation)
        {
            if (maximumBytes < 1 || maximumBytes > LimitBytes) throw new ArgumentOutOfRangeException(nameof(maximumBytes), "Request exceeds this memory budget.");
            lock (gate) {
                if (workers == MaximumWorkers || maximumBytes > LimitBytes - used) { reservation = null; return false; }
                used += maximumBytes; peak = Math.Max(peak, used); workers++; leases++;
                reservation = new Reservation(this, maximumBytes); return true;
            }
        }
        public sealed class Reservation : IDisposable
        {
            private readonly FPWorkbenchMemoryBudget owner;
            private long bytes;
            private bool working = true, disposed;
            internal Reservation(FPWorkbenchMemoryBudget owner, long bytes) { this.owner = owner; this.bytes = bytes; }
            /// <summary>Release scratch/work capacity but keep accounting for the retained result. Cannot grow beyond the admitted bound.</summary>
            public void Complete(long retainedBytes)
            {
                lock (owner.gate) {
                    if (disposed) throw new ObjectDisposedException(nameof(Reservation));
                    if (!working) throw new InvalidOperationException("Reservation already completed.");
                    if (retainedBytes < 0 || retainedBytes > bytes) throw new ArgumentOutOfRangeException(nameof(retainedBytes));
                    owner.used -= bytes - retainedBytes; bytes = retainedBytes; working = false; owner.workers--;
                }
            }
            public void Dispose()
            {
                lock (owner.gate) {
                    if (disposed) return;
                    disposed = true; owner.used -= bytes; owner.leases--; if (working) owner.workers--;
                }
            }
            /// <summary>Reduces an already retained reservation, for example after a separately scheduled scan finishes.</summary>
            public void Reduce(long retainedBytes)
            {
                lock (owner.gate) {
                    if (disposed) throw new ObjectDisposedException(nameof(Reservation));
                    if (working) throw new InvalidOperationException("Complete worker accounting before reducing retained bytes.");
                    if (retainedBytes < 0 || retainedBytes > bytes) throw new ArgumentOutOfRangeException(nameof(retainedBytes));
                    owner.used -= bytes - retainedBytes; bytes = retainedBytes;
                }
            }
            /// <summary>Transfers cleanup to a terminal worker without blocking or releasing its reservation early.</summary>
            public void ReleaseAfter(Task worker)
            {
                if (worker == null) { Dispose(); return; }
                worker.ContinueWith(t => { _ = t.Exception; Dispose(); }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
    }
}
