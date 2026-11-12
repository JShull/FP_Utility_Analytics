// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Tests
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;

    public sealed class FPWorkbenchImageExportTests
    {
        [Serializable] private sealed class Metadata : FPWorkbenchImageProvenance { public string Source = "original"; }
        private sealed class Phase : IProgress<FPWorkbenchImageExportPhase>
        {
            private readonly Action<FPWorkbenchImageExportPhase> report;
            public Phase(Action<FPWorkbenchImageExportPhase> report) { this.report = report; }
            public void Report(FPWorkbenchImageExportPhase value) => report(value);
        }
        [Test] public async Task WorkerPublishesSnapshotPixelsAndMetadataWithExclusiveAdmission()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPImageWorker-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            var pixels = new Color32[64 * 64]; for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
            pixels[0] = new Color32(255, 0, 0, 255); pixels[63] = new Color32(0, 255, 0, 255); pixels[63 * 64] = new Color32(0, 0, 255, 255);
            var metadata = new Metadata(); int caller = Thread.CurrentThread.ManagedThreadId;
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var release = new ManualResetEventSlim(); Task<FPWorkbenchImageExportResult> work = null; Texture2D image = null;
            try {
                string output = Path.Combine(root, "image.fpfigure");
                work = FPWorkbenchImageExport.PublishAsync(output, pixels, 64, 64, metadata, progress: new Phase(phase => {
                    if (phase != FPWorkbenchImageExportPhase.Encoding) return;
                    entered.TrySetResult(true); if (!release.Wait(5000)) throw new TimeoutException("Fixture worker gate timed out.");
                }));
                Assert.That(await Task.WhenAny(entered.Task, Task.Delay(5000)), Is.SameAs(entered.Task));
                Assert.That(FPWorkbenchImageExport.IsActive, Is.True);
                Assert.Throws<InvalidOperationException>(() => FPWorkbenchImageExport.PublishAsync(Path.Combine(root, "second.fpfigure"), pixels, 64, 64, metadata));
                pixels[0] = new Color32(0, 0, 0, 255); metadata.Source = "changed after request";
                release.Set(); var result = await work;
                Assert.That(result.WorkerThreadId, Is.Not.EqualTo(caller)); Assert.That(result.EncodeMilliseconds, Is.GreaterThanOrEqualTo(0));
                Assert.That(FPWorkbenchImageExport.IsActive, Is.False);
                var saved = JsonUtility.FromJson<Metadata>(File.ReadAllText(Path.Combine(output, "provenance.json")));
                Assert.That(saved.Source, Is.EqualTo("original")); Assert.That(saved.Width, Is.EqualTo(64)); Assert.That(saved.Height, Is.EqualTo(64));
                Assert.That(saved.PngSha256Base64, Is.EqualTo(result.PngSha256Base64));
                image = new Texture2D(2, 2); Assert.That(image.LoadImage(File.ReadAllBytes(Path.Combine(output, "figure.png"))), Is.True);
                Assert.That(image.width, Is.EqualTo(64)); Assert.That(image.height, Is.EqualTo(64));
                var decoded = image.GetPixels32(); Assert.That(decoded[0], Is.EqualTo(new Color32(255, 0, 0, 255)));
                Assert.That(decoded[63], Is.EqualTo(new Color32(0, 255, 0, 255))); Assert.That(decoded[63 * 64], Is.EqualTo(new Color32(0, 0, 255, 255)));
                Assert.Throws<IOException>(() => FPWorkbenchImageExport.PublishAsync(output, pixels, 64, 64, metadata));
            }
            finally {
                release.Set(); if (work != null) { try { await work; } catch { } }
                if (image != null) UnityEngine.Object.DestroyImmediate(image); Directory.Delete(root, true);
            }
        }
        [Test] public async Task CancellationBeforeCommitCleansStagingAndReleasesAdmission()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPImageCancel-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            using var cancellation = new CancellationTokenSource();
            try {
                string output = Path.Combine(root, "cancelled.fpfigure");
                var work = FPWorkbenchImageExport.PublishAsync(output, new Color32[4096], 64, 64, new Metadata(), cancellation.Token,
                    new Phase(phase => { if (phase == FPWorkbenchImageExportPhase.Publishing) cancellation.Cancel(); }));
                try { await work; Assert.Fail("Cancelled export must not publish."); } catch (OperationCanceledException) { }
                Assert.That(Directory.Exists(output), Is.False); Assert.That(Directory.GetDirectories(root), Is.Empty); Assert.That(FPWorkbenchImageExport.IsActive, Is.False);
                Assert.Throws<OperationCanceledException>(() => FPWorkbenchImageExport.PublishAsync(output, new Color32[4096], 64, 64, new Metadata(), cancellation.Token));
                var next = await FPWorkbenchImageExport.PublishAsync(Path.Combine(root, "next.fpfigure"), new Color32[4096], 64, 64, new Metadata());
                Assert.That(File.Exists(Path.Combine(next.Directory, "figure.png")), Is.True);
            }
            finally { Directory.Delete(root, true); }
        }
        [Test] public void InvalidBoundsAndOversizedMetadataDoNotHoldAdmission()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPImageBounds-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try {
                string output = Path.Combine(root, "image.fpfigure");
                Assert.Throws<ArgumentException>(() => FPWorkbenchImageExport.PublishAsync(output, new Color32[1], 4096, 4096, new Metadata()));
                Assert.Throws<InvalidDataException>(() => FPWorkbenchImageExport.PublishAsync(output, new Color32[1], 1, 1, new Metadata { Source = new string('x', FPWorkbenchImageExport.MaximumMetadataBytes + 1) }));
                Assert.That(FPWorkbenchImageExport.IsActive, Is.False); Assert.That(Directory.GetFileSystemEntries(root), Is.Empty);
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
