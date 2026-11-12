// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;

    [Serializable]
    public sealed class FPWorkbenchColumnLayout
    {
        public const int MaximumColumns = 4160; // up to 4096 physical plus 64 calculated
        public int Version = 2;
        public FPWorkbenchColumnProjection Projection = new FPWorkbenchColumnProjection();
        public bool ShowRowHeader = true;
        public float RowHeaderWidth = 76;
        public float[] ManualWidths = Array.Empty<float>(); // zero means automatic
        public void EnsureColumns(int count)
        {
            if (Version < 2) { ShowRowHeader = true; RowHeaderWidth = 76; Version = 2; }
            if (float.IsNaN(RowHeaderWidth) || float.IsInfinity(RowHeaderWidth) || RowHeaderWidth < 40 || RowHeaderWidth > 320) RowHeaderWidth = 76;
            if (count < 1 || count > MaximumColumns) throw new ArgumentOutOfRangeException(nameof(count));
            if (ManualWidths == null) ManualWidths = new float[count];
            else if (ManualWidths.Length != count) Array.Resize(ref ManualWidths, count);
            for (int i = 0; i < count; i++)
                if (float.IsNaN(ManualWidths[i]) || float.IsInfinity(ManualWidths[i]) || ManualWidths[i] < 40 || ManualWidths[i] > 1600) ManualWidths[i] = 0;
        }
        public float[] Fit(float availableWidth, int firstColumn, float autoMinimum = 110, int maximumVisible = 32, int[] order = null)
        {
            if (firstColumn < 0 || firstColumn >= ManualWidths.Length || availableWidth <= 0 || float.IsNaN(availableWidth) || float.IsInfinity(availableWidth) || autoMinimum < 40 || maximumVisible < 1 || maximumVisible > 32) throw new ArgumentOutOfRangeException();
            ValidateOrder(order);
            int total = order?.Length ?? ManualWidths.Length;
            if (firstColumn >= total) throw new ArgumentOutOfRangeException(nameof(firstColumn));
            int count = 0, automatic = 0; float used = 0;
            while (count < maximumVisible && firstColumn + count < total)
            {
                float manual = ManualWidths[order == null ? firstColumn + count : order[firstColumn + count]]; float width = manual > 0 ? manual : autoMinimum;
                if (count > 0 && used + width > availableWidth) break;
                used += width; if (manual == 0) automatic++; count++;
            }
            var result = new float[count]; float extra = automatic == 0 ? 0 : Math.Max(0, availableWidth - used) / automatic;
            for (int i = 0; i < count; i++) result[i] = ManualWidths[order == null ? firstColumn + i : order[firstColumn + i]] > 0 ? ManualWidths[order == null ? firstColumn + i : order[firstColumn + i]] : autoMinimum + extra;
            return result;
        }
        public int PreviousFirst(int firstColumn, float availableWidth, float autoMinimum = 110, int[] order = null)
        {
            if (firstColumn < 0 || firstColumn >= ManualWidths.Length || availableWidth <= 0 || float.IsNaN(availableWidth) || float.IsInfinity(availableWidth) || autoMinimum < 40) throw new ArgumentOutOfRangeException();
            ValidateOrder(order);
            if (order != null && firstColumn >= order.Length) throw new ArgumentOutOfRangeException(nameof(firstColumn));
            int first = firstColumn, count = 0; float used = 0;
            while (first > 0 && count < 32)
            {
                float width = ManualWidths[order == null ? first - 1 : order[first - 1]] > 0 ? ManualWidths[order == null ? first - 1 : order[first - 1]] : autoMinimum;
                if (count > 0 && used + width > availableWidth) break;
                first--; count++; used += width;
            }
            return first;
        }
        private void ValidateOrder(int[] order)
        {
            if (order == null) return;
            if (order.Length < 1 || order.Length > ManualWidths.Length) throw new ArgumentException("Invalid projected width order.");
            foreach (int column in order) if (column < 0 || column >= ManualWidths.Length) throw new ArgumentException("Projected width column is outside the schema.");
        }
    }
}
