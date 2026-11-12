// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using UnityEngine;

    /// <summary>Uniform local-origin frame. Source doubles remain authoritative; units are not independently distorted per axis.</summary>
    public readonly struct FPWorkbenchSpatialFrame
    {
        public readonly double OriginX, OriginY, OriginZ, SourceUnitsPerLocalUnit;
        public FPWorkbenchSpatialFrame(double originX, double originY, double originZ, double sourceUnitsPerLocalUnit)
        {
            if (!Finite(originX) || !Finite(originY) || !Finite(originZ) || !Finite(sourceUnitsPerLocalUnit) || sourceUnitsPerLocalUnit <= 0)
                throw new ArgumentException("A spatial frame needs finite origins and a positive finite source-unit scale.");
            OriginX = originX; OriginY = originY; OriginZ = originZ; SourceUnitsPerLocalUnit = sourceUnitsPerLocalUnit;
        }
        public static FPWorkbenchSpatialFrame Fit(FPWorkbenchSpatialBounds bounds)
        {
            // Half-sums avoid overflowing max-min when the source crosses the finite double range.
            double x = Midpoint(bounds.MinimumX, bounds.MaximumX), y = Midpoint(bounds.MinimumY, bounds.MaximumY), z = Midpoint(bounds.MinimumZ, bounds.MaximumZ);
            double extent = Math.Max(Math.Max(bounds.MaximumX - x, x - bounds.MinimumX),
                Math.Max(Math.Max(bounds.MaximumY - y, y - bounds.MinimumY), Math.Max(bounds.MaximumZ - z, z - bounds.MinimumZ)));
            return new FPWorkbenchSpatialFrame(x, y, z, extent > 0 ? extent : 1);
        }
        private static double Midpoint(double min, double max)
        { double span = max - min; return Finite(span) ? min + span * .5 : min * .5 + max * .5; }
        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        public FPWorkbenchSpatialPoint ToSource(Vector3 local, long row)
        {
            double x = OriginX + local.x * SourceUnitsPerLocalUnit, y = OriginY + local.y * SourceUnitsPerLocalUnit, z = OriginZ + local.z * SourceUnitsPerLocalUnit;
            if (!Finite(x) || !Finite(y) || !Finite(z)) throw new OverflowException("Local position cannot be reconstructed as finite source doubles.");
            return new FPWorkbenchSpatialPoint(x, y, z, row);
        }
    }
    public sealed class FPWorkbenchPreparedPoints
    {
        public string DatasetId { get; internal set; }
        public string FormulaFingerprint { get; internal set; }
        public string CorrectionFingerprint { get; internal set; }
        public FPWorkbenchSpatialFrame Frame { get; internal set; }
        public IReadOnlyList<Vector3> Positions { get; internal set; }
        public IReadOnlyList<long> SourceRows { get; internal set; }
        public long MatchingCount { get; internal set; }
        public double MaximumLocalError { get; internal set; }
        public double MaximumSourceReconstructionError { get; internal set; }
    }

    /// <summary>Bounded worker-side float presentation preparation; no scene, Editor, GPU or Entities dependencies.</summary>
    public static class FPWorkbenchPointPreparation
    {
        public static Task<FPWorkbenchPreparedPoints> PrepareAsync(FPWorkbenchPointView view, FPWorkbenchSpatialFrame frame,
            double maximumLocalError = 1e-6, CancellationToken cancellation = default)
        {
            if (view == null || view.Points == null) throw new ArgumentNullException(nameof(view));
            if (view.Points.Count > 65536 || !FPWorkbenchSpatialFrame.Finite(maximumLocalError) || maximumLocalError < 0)
                throw new ArgumentException("Preparation requires at most 65,536 points and a finite nonnegative local error bound.");
            // Validate default(struct) as well as explicitly constructed frames.
            frame = new FPWorkbenchSpatialFrame(frame.OriginX, frame.OriginY, frame.OriginZ, frame.SourceUnitsPerLocalUnit);
            return Task.Run(() =>
            {
                var positions = new Vector3[view.Points.Count]; var rows = new long[positions.Length]; double localError = 0, sourceError = 0;
                for (int i = 0; i < positions.Length; i++)
                {
                    cancellation.ThrowIfCancellationRequested(); var point = view.Points[i];
                    double x = (point.X - frame.OriginX) / frame.SourceUnitsPerLocalUnit,
                        y = (point.Y - frame.OriginY) / frame.SourceUnitsPerLocalUnit, z = (point.Z - frame.OriginZ) / frame.SourceUnitsPerLocalUnit;
                    var local = new Vector3((float)x, (float)y, (float)z);
                    double error = Math.Max(Math.Abs(x - local.x), Math.Max(Math.Abs(y - local.y), Math.Abs(z - local.z)));
                    if (!FPWorkbenchSpatialFrame.Finite(x) || !FPWorkbenchSpatialFrame.Finite(y) || !FPWorkbenchSpatialFrame.Finite(z) ||
                        !FPWorkbenchSpatialFrame.Finite(error) || error > maximumLocalError)
                        throw new InvalidOperationException("Source row " + point.Row + " exceeds the declared local float error budget; use a closer origin or a different scale.");
                    var recovered = frame.ToSource(local, point.Row);
                    double reconstruction = Math.Max(Math.Abs(point.X - recovered.X), Math.Max(Math.Abs(point.Y - recovered.Y), Math.Abs(point.Z - recovered.Z)));
                    if (!FPWorkbenchSpatialFrame.Finite(reconstruction)) throw new OverflowException("Source reconstruction error exceeds finite double range.");
                    localError = Math.Max(localError, error); sourceError = Math.Max(sourceError, reconstruction); positions[i] = local; rows[i] = point.Row;
                }
                cancellation.ThrowIfCancellationRequested();
                return new FPWorkbenchPreparedPoints { DatasetId = view.DatasetId, FormulaFingerprint = view.FormulaFingerprint,
                    CorrectionFingerprint = view.CorrectionFingerprint, Frame = frame, Positions = Array.AsReadOnly(positions), SourceRows = Array.AsReadOnly(rows),
                    MatchingCount = view.MatchingCount, MaximumLocalError = localError, MaximumSourceReconstructionError = sourceError };
            }, cancellation);
        }
    }
}
