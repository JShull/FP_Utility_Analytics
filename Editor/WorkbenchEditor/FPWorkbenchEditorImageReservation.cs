// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using UnityEngine;

    /// <summary>Main-thread export admission before capture. Estimates are owned-payload accounting, not driver/encoder heap measurements.</summary>
    internal sealed class FPWorkbenchEditorImageReservation : IDisposable
    {
        private FPWorkbenchEditorResources.ScanReservation reservation;
        private FPWorkbenchEditorImageReservation(FPWorkbenchEditorResources.ScanReservation reservation) { this.reservation = reservation; }
        internal static FPWorkbenchEditorImageReservation Acquire(int width, int height, long maximumVertices)
        {
            if (width < 1 || height < 1 || width > 4096 || height > 4096 || (long)width * height > FPWorkbenchImageExport.MaximumPixels || maximumVertices < 0 || maximumVertices > 1048576)
                throw new ArgumentOutOfRangeException("Image dimensions/geometry exceed the export account bounds.");
            if (FPWorkbenchImageExport.IsActive) throw new InvalidOperationException("Another Workbench image export is active.");
            // Pixel/render/readback/clone allowance, owned geometry copies and bounded PNG/provenance allowance.
            long bytes = checked(64L * 1024 * 1024 + (long)width * height * 40 + maximumVertices * 128);
            if (bytes > FPWorkbenchEditorResources.Memory.LimitBytes)
                throw new InvalidOperationException("Export needs " + (bytes / 1048576d).ToString("F1") + " MiB of accounted capacity; reduce image dimensions or point budget.");
            if (!FPWorkbenchEditorResources.TryAcquireScan(bytes, out var reservation))
                throw new InvalidOperationException("Shared export memory/scan capacity is busy. Close unused figures or retry after background work finishes.");
            return new FPWorkbenchEditorImageReservation(reservation);
        }
        internal Task<FPWorkbenchImageExportResult> Publish(string output, Color32[] pixels, int width, int height,
            FPWorkbenchImageProvenance metadata, CancellationToken cancellation, IProgress<FPWorkbenchImageExportPhase> progress = null)
        {
            if (reservation == null) throw new ObjectDisposedException(nameof(FPWorkbenchEditorImageReservation));
            var task = FPWorkbenchImageExport.PublishAsync(output, pixels, width, height, metadata, cancellation, progress);
            var transferred = reservation; reservation = null; transferred.ReleaseAfter(task); return task;
        }
        public void Dispose() { reservation?.Dispose(); reservation = null; }
    }
}
