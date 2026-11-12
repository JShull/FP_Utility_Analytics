// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Tests
{
    using System;
    using System.IO;
    using System.Threading.Tasks;
    using NUnit.Framework;

    public sealed class FPWorkbenchMemoryBudgetTests
    {
        [Test] public async Task ScalarConstructionDefersBeforeDispatchAndRollsBackScanAdmissionOnMemoryPressure()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPScalarScan-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            var memory = new FPWorkbenchMemoryBudget(256L * 1024 * 1024, 2);
            var scans = new FPWorkbenchMemoryBudget(1, 1);
            FPWorkbenchFigureSession.Lease lease = null;
            FPWorkbenchMemoryBudget.Reservation scanBlocker = null, memoryBlocker = null;
            Task<FPWorkbenchSeriesCache> build = null;
            try {
                string source = Path.Combine(root, "input.csv"); File.WriteAllText(source, "x,y\n0,1\n1,2\n2,3\n");
                var data = await FPWorkbenchCsvDataset.ImportAsync(source, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile());
                var session = new FPWorkbenchFigureSession(data, new FPWorkbenchSeriesBinding { XMode = FPWorkbenchXMode.Numeric, XColumn = 0, YColumn = 1 }, new FPWorkbenchPlotSettings(), memoryBudget: memory, scanBudget: scans);
                lease = session.Retain(); Assert.That(scans.TryAcquire(1, out scanBlocker), Is.True);
                session.Poll(); Assert.That(session.WaitingForMemory, Is.True); Assert.That(session.Series, Is.Null);
                Assert.That(memory.LeaseCount, Is.Zero);
                var field = typeof(FPWorkbenchFigureSession).GetField("build", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.That(field.GetValue(session), Is.Null, "No source worker may queue before admission.");
                scanBlocker.Dispose();
                Assert.That(memory.TryAcquire(memory.LimitBytes, out memoryBlocker), Is.True); memoryBlocker.Complete(memory.LimitBytes);
                session.Poll(); Assert.That(session.WaitingForMemory, Is.True); Assert.That(scans.LeaseCount, Is.Zero, "Failed payload admission rolls back its scan ticket.");
                memoryBlocker.Dispose(); session.Poll();
                build = (Task<FPWorkbenchSeriesCache>)field.GetValue(session); Assert.That(build, Is.Not.Null);
                Assert.That(scans.ActiveWorkers, Is.EqualTo(1)); Assert.That(memory.UsedBytes, Is.GreaterThan(0));
                Assert.That(memory.ActiveWorkers, Is.Zero, "Construction must leave interactive worker admission available.");
                await build;
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (session.View == null && DateTime.UtcNow < deadline) { session.Poll(); await Task.Delay(1); }
                Assert.That(session.View, Is.Not.Null); Assert.That(session.WaitingForMemory, Is.False);
                Assert.That(scans.LeaseCount, Is.Zero); Assert.That(memory.LeaseCount, Is.EqualTo(1));
                lease.Dispose(); lease = null;
                Assert.That(memory.UsedBytes, Is.Zero); Assert.That(scans.UsedBytes, Is.Zero);
            } finally {
                lease?.Dispose(); scanBlocker?.Dispose(); memoryBlocker?.Dispose();
                if (build != null) try { await build; } catch { }
                Directory.Delete(root, true);
            }
        }
        [Test] public async Task ScalarSessionsDeferQueriesShareRetainedViewsAndReleaseOnLastOwner()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPScalarBudget-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            var budget = new FPWorkbenchMemoryBudget(2 * 1024 * 1024, 1);
            FPWorkbenchFigureSession.Lease first = null, second = null;
            budget.TryAcquire(budget.LimitBytes, out var blocker);
            try {
                string source = Path.Combine(root, "source.csv"); File.WriteAllText(source, "x,y\n0,1\n1,2\n2,3\n");
                var data = await FPWorkbenchCsvDataset.ImportAsync(source, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile());
                var settings = new FPWorkbenchPlotSettings();
                var session = new FPWorkbenchFigureSession(data, new FPWorkbenchSeriesBinding { XMode = FPWorkbenchXMode.Numeric, XColumn = 0, YColumn = 1 }, settings, memoryBudget: budget);
                first = session.Retain(); var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!session.WaitingForMemory && DateTime.UtcNow < deadline) { session.Poll(); await Task.Delay(1); }
                Assert.That(session.WaitingForMemory, Is.True); Assert.That(session.IsBusy, Is.True); Assert.That(session.View, Is.Null);
                Assert.That(budget.LeaseCount, Is.EqualTo(1), "Refused queries must not queue reservations.");
                blocker.Dispose();
                while (session.View == null && DateTime.UtcNow < deadline) { session.Poll(); await Task.Delay(1); }
                Assert.That(session.View, Is.Not.Null); Assert.That(session.WaitingForMemory, Is.False);
                Assert.That(budget.ActiveWorkers, Is.Zero); Assert.That(budget.UsedBytes, Is.GreaterThan(0));
                Assert.That(budget.LeaseCount, Is.EqualTo(1)); long retained = budget.UsedBytes;
                second = session.Retain(); first.Dispose(); first = null;
                Assert.That(session.IsDisposed, Is.False); Assert.That(budget.UsedBytes, Is.EqualTo(retained)); Assert.That(budget.LeaseCount, Is.EqualTo(1));
                settings.StartX = .25; settings.EndX = 1.75; session.Poll();
                Assert.That(budget.UsedBytes, Is.GreaterThan(retained));
                second.Dispose(); second = null;
                while (budget.LeaseCount != 0 && DateTime.UtcNow < deadline) await Task.Delay(1);
                Assert.That(session.IsDisposed, Is.True); Assert.That(budget.UsedBytes, Is.Zero); Assert.That(budget.ActiveWorkers, Is.Zero); Assert.That(budget.LeaseCount, Is.Zero);
            }
            finally { first?.Dispose(); second?.Dispose(); blocker.Dispose(); Directory.Delete(root, true); }
        }
        [Test] public void ReservationsBoundWorkersAndRetainedBytesUntilExplicitRelease()
        {
            var budget = new FPWorkbenchMemoryBudget(100, 2);
            Assert.That(budget.TryAcquire(60, out var first), Is.True);
            Assert.That(budget.TryAcquire(50, out _), Is.False);
            Assert.That(budget.TryAcquire(40, out var second), Is.True);
            Assert.That(budget.TryAcquire(1, out _), Is.False);
            Assert.That(budget.UsedBytes, Is.EqualTo(100)); Assert.That(budget.ActiveWorkers, Is.EqualTo(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => first.Complete(61));
            Assert.That(budget.UsedBytes, Is.EqualTo(100)); Assert.That(budget.ActiveWorkers, Is.EqualTo(2));
            first.Complete(20);
            Assert.That(budget.UsedBytes, Is.EqualTo(60)); Assert.That(budget.ActiveWorkers, Is.EqualTo(1));
            Assert.That(budget.TryAcquire(40, out var third), Is.True);
            Assert.Throws<InvalidOperationException>(() => first.Complete(0));
            second.Dispose(); second.Dispose(); third.Dispose();
            Assert.That(budget.UsedBytes, Is.EqualTo(20)); Assert.That(budget.LeaseCount, Is.EqualTo(1));
            first.Reduce(10); Assert.That(budget.UsedBytes, Is.EqualTo(10));
            Assert.Throws<ArgumentOutOfRangeException>(() => first.Reduce(11));
            first.Dispose(); Assert.Throws<ObjectDisposedException>(() => first.Complete(0));
            Assert.That(budget.UsedBytes, Is.Zero); Assert.That(budget.ActiveWorkers, Is.Zero); Assert.That(budget.LeaseCount, Is.Zero);
            Assert.That(budget.PeakBytes, Is.EqualTo(100));
            Assert.Throws<ArgumentOutOfRangeException>(() => budget.TryAcquire(101, out _));
        }
        [Test] public async Task ConcurrentReservationCompletionAndDisposalPreserveBounds()
        {
            var budget = new FPWorkbenchMemoryBudget(1024, 2); var tasks = new Task[8];
            for (int i = 0; i < tasks.Length; i++) tasks[i] = Task.Run(() => {
                for (int j = 0; j < 1000; j++) if (budget.TryAcquire(512, out var reservation)) {
                    using (reservation) {
                        Assert.That(budget.UsedBytes, Is.LessThanOrEqualTo(1024));
                        Assert.That(budget.ActiveWorkers, Is.LessThanOrEqualTo(2));
                        reservation.Complete(128);
                    }
                }
            });
            await Task.WhenAll(tasks);
            Assert.That(budget.UsedBytes, Is.Zero); Assert.That(budget.ActiveWorkers, Is.Zero); Assert.That(budget.LeaseCount, Is.Zero);
            Assert.That(budget.PeakBytes, Is.LessThanOrEqualTo(1024));
        }
    }
}
