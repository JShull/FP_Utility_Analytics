// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    public sealed partial class FPWorkbenchSeriesCache
    {
        /// <summary>Picks an actual cached endpoint/extremum represented by the displayed bins, never an interpolated envelope value.</summary>
        public Task<FPWorkbenchSeriesPoint?> PickDisplayedPointAsync(FPWorkbenchSeriesView view, FPWorkbenchPlotSettings settings,
            double mouseX, double mouseY, double width, double height, double radius = 10, CancellationToken cancellation = default)
        {
            if (view == null || settings == null || view.DatasetId != dataset.DatasetId || view.FormulaFingerprint != FormulaFingerprint || view.CorrectionFingerprint != CorrectionFingerprint ||
                view.Bins == null || view.Bins.Length > 16388 || !FPWorkbenchSeriesBinding.Finite(mouseX) || !FPWorkbenchSeriesBinding.Finite(mouseY) || mouseX < 0 || mouseX > 1 || mouseY < 0 || mouseY > 1 ||
                !FPWorkbenchSeriesBinding.Finite(width) || !FPWorkbenchSeriesBinding.Finite(height) || width <= 0 || height <= 0 || width > 32768 || height > 32768 || !FPWorkbenchSeriesBinding.Finite(radius) || radius <= 0 || radius > 64)
                throw new ArgumentException("Pick a current bounded plot view with finite screen coordinates.");
            var snapshot = new FPWorkbenchPlotSettings(); snapshot.CopyFrom(settings);
            return dataset.RunCachedAsync(() => {
                cancellation.ThrowIfCancellationRequested(); dataset.ValidateUnchanged();
                double min = snapshot.LockY ? snapshot.MinimumY : view.Minimum, max = snapshot.LockY ? snapshot.MaximumY : view.Maximum;
                if (double.IsNaN(min) || double.IsNaN(max)) return (FPWorkbenchSeriesPoint?)null;
                if (max == min) FPWorkbenchAxis.ExpandConstant(min, snapshot.YScale, out min, out max);
                var xMap = new FPWorkbenchAxisMapping(view.StartX, view.EndX, snapshot.XScale);
                var yMap = new FPWorkbenchAxisMapping(min, max, snapshot.YScale);
                var seen = new HashSet<long>(); double best = radius * radius; FPWorkbenchSeriesPoint? selected = null;
                using var points = new BinaryReader(File.OpenRead(Path.Combine(DirectoryPath, "points.bin")));
                if (points.BaseStream.Length != checked(RowCount * PointBytes)) throw new InvalidDataException("Series point payload length changed.");
                void Consider(long row)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (row < FirstSourceRow || row >= FirstSourceRow + RowCount) throw new InvalidDataException("Displayed row lies outside its cached series.");
                    if (!seen.Add(row)) return;
                    var point = ReadAt(points, row - FirstSourceRow);
                    if (point.Row != row) throw new InvalidDataException("Cached point source-row identity differs.");
                    if (!point.IsValid || point.X < view.StartX || point.X > view.EndX || point.Y < min || point.Y > max) return;
                    double dx = (xMap.Fraction(point.X) - mouseX) * width;
                    double fractionY = yMap.Fraction(point.Y); double dy = ((snapshot.InvertY ? fractionY : 1 - fractionY) - mouseY) * height;
                    double distance = dx * dx + dy * dy;
                    if (distance < best || distance == best && (!selected.HasValue || row < selected.Value.Row)) { best = distance; selected = point; }
                }
                foreach (var bin in view.Bins) {
                    cancellation.ThrowIfCancellationRequested();
                    if (bin.Count == 0 || bin.Maximum < min || bin.Minimum > max || snapshot.XScale == FPWorkbenchAxisScale.Log10 && bin.FirstX <= 0 || snapshot.YScale == FPWorkbenchAxisScale.Log10 && bin.Minimum <= 0) continue;
                    Consider(bin.MinimumRow); Consider(bin.MaximumRow);
                    Consider(FirstSourceRow + bin.FirstIndex); Consider(FirstSourceRow + bin.EndIndex - 1);
                }
                cancellation.ThrowIfCancellationRequested(); dataset.ValidateUnchanged(); return selected;
            }, cancellation);
        }
    }
}
