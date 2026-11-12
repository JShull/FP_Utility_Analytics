// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using UnityEngine;
    using UnityEngine.Experimental.Rendering;

    public enum FPWorkbenchImageExportPhase { Encoding, Writing, Publishing }
    [Serializable] public class FPWorkbenchImageProvenance
    {
        public int Width, Height;
        public string PngSha256Base64;
    }
    public sealed class FPWorkbenchImageExportResult
    {
        public string Directory { get; internal set; }
        public int PngBytes { get; internal set; }
        public string PngSha256Base64 { get; internal set; }
        public int WorkerThreadId { get; internal set; }
        public double EncodeMilliseconds { get; internal set; }
        public double PublishMilliseconds { get; internal set; }
    }
    /// <summary>One worker export at a time. Caller captures pixels; this service snapshots, encodes and publishes without Editor APIs.</summary>
    public static class FPWorkbenchImageExport
    {
        public const int MaximumPixels = 4194304, MaximumPngBytes = 33554432, MaximumMetadataBytes = 4194304;
        private static int active;
        public static bool IsActive => Volatile.Read(ref active) != 0;
        public static Task<FPWorkbenchImageExportResult> PublishAsync(string directory, Color32[] rgba, int width, int height,
            FPWorkbenchImageProvenance provenance, CancellationToken cancellation = default, IProgress<FPWorkbenchImageExportPhase> progress = null)
        {
            cancellation.ThrowIfCancellationRequested();
            if (width < 1 || height < 1 || width > 4096 || height > 4096 || (long)width * height > MaximumPixels || rgba == null || rgba.LongLength != (long)width * height)
                throw new ArgumentException("Image dimensions/pixel count exceed the RGBA8 export contract.");
            if (provenance == null) throw new ArgumentNullException(nameof(provenance));
            string output = Path.GetFullPath(directory), parent = Path.GetDirectoryName(output);
            if (!output.EndsWith(".fpfigure", StringComparison.OrdinalIgnoreCase) || File.Exists(output) || System.IO.Directory.Exists(output))
                throw new IOException("Choose a new .fpfigure directory; output is never overwritten.");
            if (!System.IO.Directory.Exists(parent)) throw new DirectoryNotFoundException(parent);
            if (Interlocked.CompareExchange(ref active, 1, 0) != 0) throw new InvalidOperationException("Another Workbench image export is active.");
            try {
                string json = JsonUtility.ToJson(provenance, true);
                if (Encoding.UTF8.GetByteCount(json) > MaximumMetadataBytes) throw new InvalidDataException("Image metadata exceeds 4 MiB.");
                Type metadataType = provenance.GetType();
                var pixels = (Color32[])rgba.Clone(); // The caller may immediately reuse its readback array.
                return Task.Run(() => Publish(output, pixels, width, height, json, metadataType, cancellation, progress));
            }
            catch { Interlocked.Exchange(ref active, 0); throw; }
        }
        private static FPWorkbenchImageExportResult Publish(string output, Color32[] pixels, int width, int height, string json, Type metadataType,
            CancellationToken cancellation, IProgress<FPWorkbenchImageExportPhase> progress)
        {
            string stage = output + "." + Guid.NewGuid().ToString("N") + ".tmp"; bool created = false;
            try {
                progress?.Report(FPWorkbenchImageExportPhase.Encoding); cancellation.ThrowIfCancellationRequested();
                var clock = Stopwatch.StartNew();
                // Unity documents this array encoder as thread-safe; no Texture2D or Editor object enters the worker.
                byte[] png = ImageConversion.EncodeArrayToPNG(pixels, GraphicsFormat.R8G8B8A8_UNorm, (uint)width, (uint)height);
                double encode = clock.Elapsed.TotalMilliseconds; cancellation.ThrowIfCancellationRequested();
                if (png == null || png.Length == 0 || png.Length > MaximumPngBytes) throw new IOException("Encoded PNG is empty or exceeds 32 MiB.");
                string hash; using (var sha = SHA256.Create()) hash = Convert.ToBase64String(sha.ComputeHash(png));
                var metadata = (FPWorkbenchImageProvenance)JsonUtility.FromJson(json, metadataType);
                metadata.Width = width; metadata.Height = height; metadata.PngSha256Base64 = hash;
                byte[] metadataBytes = new UTF8Encoding(false).GetBytes(JsonUtility.ToJson(metadata, true));
                if (metadataBytes.Length > MaximumMetadataBytes) throw new InvalidDataException("Image metadata exceeds 4 MiB.");
                clock.Restart(); progress?.Report(FPWorkbenchImageExportPhase.Writing); cancellation.ThrowIfCancellationRequested();
                if (System.IO.Directory.Exists(stage) || File.Exists(stage)) throw new IOException("Image staging path already exists.");
                System.IO.Directory.CreateDirectory(stage); created = true;
                Write(Path.Combine(stage, "figure.png"), png, cancellation); Write(Path.Combine(stage, "provenance.json"), metadataBytes, cancellation);
                progress?.Report(FPWorkbenchImageExportPhase.Publishing); cancellation.ThrowIfCancellationRequested();
                System.IO.Directory.Move(stage, output); created = false; // Commit: cancellation after this point cannot retract a published pair.
                return new FPWorkbenchImageExportResult { Directory = output, PngBytes = png.Length, PngSha256Base64 = hash,
                    WorkerThreadId = Thread.CurrentThread.ManagedThreadId, EncodeMilliseconds = encode, PublishMilliseconds = clock.Elapsed.TotalMilliseconds };
            }
            finally {
                try { if (created && System.IO.Directory.Exists(stage)) System.IO.Directory.Delete(stage, true); }
                finally { Interlocked.Exchange(ref active, 0); }
            }
        }
        private static void Write(string path, byte[] bytes, CancellationToken cancellation)
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            for (int offset = 0; offset < bytes.Length;) {
                cancellation.ThrowIfCancellationRequested(); int count = Math.Min(65536, bytes.Length - offset);
                stream.Write(bytes, offset, count); offset += count;
            }
            stream.Flush();
        }
    }
}
