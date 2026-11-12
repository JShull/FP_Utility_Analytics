// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;

    [Serializable]
    public sealed class FPWorkbenchColumnLayout
    {
        public const int MaximumColumns = 4160; // up to 4096 physical plus 64 calculated
        public int Version = 2;
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
        public float[] Fit(float availableWidth, int firstColumn, float autoMinimum = 110, int maximumVisible = 32)
        {
            if (firstColumn < 0 || firstColumn >= ManualWidths.Length || availableWidth <= 0 || float.IsNaN(availableWidth) || float.IsInfinity(availableWidth) || autoMinimum < 40 || maximumVisible < 1 || maximumVisible > 32) throw new ArgumentOutOfRangeException();
            int count = 0, automatic = 0; float used = 0;
            while (count < maximumVisible && firstColumn + count < ManualWidths.Length)
            {
                float manual = ManualWidths[firstColumn + count]; float width = manual > 0 ? manual : autoMinimum;
                if (count > 0 && used + width > availableWidth) break;
                used += width; if (manual == 0) automatic++; count++;
            }
            var result = new float[count]; float extra = automatic == 0 ? 0 : Math.Max(0, availableWidth - used) / automatic;
            for (int i = 0; i < count; i++) result[i] = ManualWidths[firstColumn + i] > 0 ? ManualWidths[firstColumn + i] : autoMinimum + extra;
            return result;
        }
        public int PreviousFirst(int firstColumn, float availableWidth, float autoMinimum = 110)
        {
            if (firstColumn < 0 || firstColumn >= ManualWidths.Length || availableWidth <= 0 || float.IsNaN(availableWidth) || float.IsInfinity(availableWidth) || autoMinimum < 40) throw new ArgumentOutOfRangeException();
            int first = firstColumn, count = 0; float used = 0;
            while (first > 0 && count < 32)
            {
                float width = ManualWidths[first - 1] > 0 ? ManualWidths[first - 1] : autoMinimum;
                if (count > 0 && used + width > availableWidth) break;
                first--; count++; used += width;
            }
            return first;
        }
    }
}
