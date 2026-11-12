// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Tests
{
    using System;
    using System.Collections;
    using System.IO;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using FuzzPhyte.Utility.Analytics.WorkBench.Editor;

    public sealed class FPWorkbenchEditorImageJobTests
    {
        private sealed class EncoderGate : IProgress<FPWorkbenchImageExportPhase>, IDisposable
        {
            internal readonly ManualResetEventSlim Entered = new ManualResetEventSlim(), Continue = new ManualResetEventSlim();
            public void Report(FPWorkbenchImageExportPhase phase) {
                if (phase != FPWorkbenchImageExportPhase.Encoding) return;
                Entered.Set(); if (!Continue.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Encoder gate was not released.");
            }
            public void Dispose() { Continue.Set(); Entered.Dispose(); Continue.Dispose(); }
        }
        [UnityTest] public IEnumerator ImageReservationsRefuseBeforeCaptureAndSurviveCancelledWorkerOwnership()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPImageMemory-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            var assembly = typeof(FPWorkbenchFigureWindow).Assembly;
            var type = assembly.GetType("FuzzPhyte.Utility.Analytics.WorkBench.Editor.FPWorkbenchEditorImageReservation");
            var resources = assembly.GetType("FuzzPhyte.Utility.Analytics.WorkBench.Editor.FPWorkbenchEditorResources");
            const BindingFlags staticFlags = BindingFlags.Static | BindingFlags.NonPublic;
            var memory = (FPWorkbenchMemoryBudget)resources.GetField("Memory", staticFlags).GetValue(null);
            var scans = (FPWorkbenchMemoryBudget)resources.GetField("Scans", staticFlags).GetValue(null);
            var acquire = type.GetMethod("Acquire", staticFlags);
            long baseline = memory.UsedBytes; int baselineLeases = memory.LeaseCount, baselineScans = scans.ActiveWorkers;
            object scope = null; FPWorkbenchMemoryBudget.Reservation blocker = null;
            using var gate = new EncoderGate(); using var cancel = new CancellationTokenSource(); Task<FPWorkbenchImageExportResult> worker = null;
            try {
                Assert.That(memory.TryAcquire(memory.LimitBytes - memory.UsedBytes, out blocker), Is.True);
                Assert.That(Assert.Throws<TargetInvocationException>(() => acquire.Invoke(null, new object[] { 64, 64, 0L })).InnerException, Is.TypeOf<InvalidOperationException>());
                Assert.That(scans.ActiveWorkers, Is.EqualTo(baselineScans)); Assert.That(FPWorkbenchImageExport.IsActive, Is.False);
                blocker.Dispose(); blocker = null;
                scope = acquire.Invoke(null, new object[] { 64, 64, 0L }); Assert.That(memory.UsedBytes, Is.GreaterThan(baseline));
                ((IDisposable)scope).Dispose(); scope = null; Assert.That(memory.UsedBytes, Is.EqualTo(baseline));
                Assert.That(Assert.Throws<TargetInvocationException>(() => acquire.Invoke(null, new object[] { 4096, 1024, 1048576L })).InnerException, Is.TypeOf<InvalidOperationException>());
                Assert.That(memory.UsedBytes, Is.EqualTo(baseline));
                scope = acquire.Invoke(null, new object[] { 64, 64, 0L });
                string output = Path.Combine(root, "cancelled.fpfigure");
                worker = (Task<FPWorkbenchImageExportResult>)type.GetMethod("Publish", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(scope,
                    new object[] { output, new Color32[64 * 64], 64, 64, new FPWorkbenchImageProvenance(), cancel.Token, gate });
                ((IDisposable)scope).Dispose(); scope = null;
                var deadline = DateTime.UtcNow.AddSeconds(10); while (!gate.Entered.IsSet && DateTime.UtcNow < deadline) yield return null;
                Assert.That(gate.Entered.IsSet, Is.True); cancel.Cancel();
                Assert.That(worker.IsCompleted, Is.False); Assert.That(memory.UsedBytes, Is.GreaterThan(baseline)); Assert.That(scans.ActiveWorkers, Is.EqualTo(baselineScans + 1));
                gate.Continue.Set(); while (!worker.IsCompleted && DateTime.UtcNow < deadline) yield return null;
                Assert.That(worker.IsCompleted, Is.True);
                bool cancelled = false; try { worker.GetAwaiter().GetResult(); } catch (OperationCanceledException) { cancelled = true; }
                Assert.That(cancelled, Is.True);
                while ((memory.UsedBytes != baseline || scans.ActiveWorkers != baselineScans) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(memory.UsedBytes, Is.EqualTo(baseline)); Assert.That(memory.LeaseCount, Is.EqualTo(baselineLeases)); Assert.That(scans.ActiveWorkers, Is.EqualTo(baselineScans));
                Assert.That(Directory.Exists(output), Is.False); Assert.That(FPWorkbenchImageExport.IsActive, Is.False);
            }
            finally { cancel.Cancel(); gate.Continue.Set(); ((IDisposable)scope)?.Dispose(); blocker?.Dispose(); Directory.Delete(root, true); }
        }
        [Test] public void ClosingFigureCancelsItsOwnedPendingImageJob()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var window = ScriptableObject.CreateInstance<FPWorkbenchFigureWindow>();
            var pending = new TaskCompletionSource<FPWorkbenchImageExportResult>();
            try {
                object job = window.GetType().GetProperty("ImageJob", flags).GetValue(window);
                job.GetType().GetMethod("Start", flags).Invoke(job, new object[] { (Func<Task<FPWorkbenchImageExportResult>>)(() => pending.Task) });
                var token = (CancellationToken)job.GetType().GetProperty("Token", flags).GetValue(job);
                UnityEngine.Object.DestroyImmediate(window); window = null;
                Assert.That(token.IsCancellationRequested, Is.True);
                Assert.That((bool)job.GetType().GetProperty("IsBusy", flags).GetValue(job), Is.False);
                pending.SetCanceled();
            }
            finally { if (window != null) UnityEngine.Object.DestroyImmediate(window); }
        }
        [Test] public void DisposingPendingJobCancelsTokenAndDetachesCallbacks()
        {
            Type type = typeof(FPWorkbenchFigureWindow).Assembly.GetType("FuzzPhyte.Utility.Analytics.WorkBench.Editor.FPWorkbenchEditorImageJob", true);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            int repaints = 0; var job = (IDisposable)Activator.CreateInstance(type, new object[] { (Action)(() => repaints++) });
            var pending = new TaskCompletionSource<FPWorkbenchImageExportResult>();
            try {
                type.GetMethod("Start", flags).Invoke(job, new object[] { (Func<Task<FPWorkbenchImageExportResult>>)(() => pending.Task) });
                var token = (CancellationToken)type.GetProperty("Token", flags).GetValue(job);
                Assert.That((bool)type.GetProperty("IsBusy", flags).GetValue(job), Is.True);
                job.Dispose(); Assert.That(token.IsCancellationRequested, Is.True); int before = repaints;
                pending.SetResult(new FPWorkbenchImageExportResult()); type.GetMethod("Tick", flags).Invoke(job, null);
                Assert.That(repaints, Is.EqualTo(before)); Assert.That((bool)type.GetProperty("IsBusy", flags).GetValue(job), Is.False);
            }
            finally { job.Dispose(); }
        }
        [UnityTest] public IEnumerator EditorJobPollsWorkerCompletionAndReportsTimings()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPImageAdapter-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            Type type = typeof(FPWorkbenchFigureWindow).Assembly.GetType("FuzzPhyte.Utility.Analytics.WorkBench.Editor.FPWorkbenchEditorImageJob", true);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var job = (IDisposable)Activator.CreateInstance(type, new object[] { (Action)(() => { }) });
            Task<FPWorkbenchImageExportResult> work = null;
            try {
                type.GetMethod("Start", flags).Invoke(job, new object[] { (Func<Task<FPWorkbenchImageExportResult>>)(() => {
                    var token = (CancellationToken)type.GetProperty("Token", flags).GetValue(job);
                    return work = FPWorkbenchImageExport.PublishAsync(Path.Combine(root, "image.fpfigure"), new Color32[4096], 64, 64, new FPWorkbenchImageProvenance(), token);
                }) });
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while ((bool)type.GetProperty("IsBusy", flags).GetValue(job) && DateTime.UtcNow < deadline) yield return null;
                Assert.That((bool)type.GetProperty("IsBusy", flags).GetValue(job), Is.False);
                Assert.That(work, Is.Not.Null); var result = work.GetAwaiter().GetResult();
                Assert.That(File.Exists(Path.Combine(result.Directory, "figure.png")), Is.True);
                Assert.That((string)type.GetField("status", flags).GetValue(job), Does.Contain("capture").And.Contain("encode").And.Contain("publish"));
            }
            finally { job.Dispose(); Directory.Delete(root, true); }
        }
    }
}
