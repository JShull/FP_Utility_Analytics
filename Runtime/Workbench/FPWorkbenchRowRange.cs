// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    /// <summary>Source row identities, zero-based with an exclusive end. Disabled means all source rows.</summary>
    [Serializable] public sealed class FPWorkbenchRowRange
    {
        public bool Enabled;
        public long FirstRow, EndRow;
        public FPWorkbenchRowRange Copy() => (FPWorkbenchRowRange)MemberwiseClone();
        public void Validate(long rowCount = long.MaxValue)
        {
            if (rowCount < 0 || Enabled && (FirstRow < 0 || EndRow <= FirstRow || EndRow > rowCount))
                throw new ArgumentOutOfRangeException("Choose a nonempty row range inside the original source.");
        }
        public long First(long rowCount) { Validate(rowCount); return Enabled ? FirstRow : 0; }
        public long End(long rowCount) { Validate(rowCount); return Enabled ? EndRow : rowCount; }
        public long Count(long rowCount) => End(rowCount) - First(rowCount);
        public bool SameAs(FPWorkbenchRowRange other) => other != null && Enabled == other.Enabled && (!Enabled || FirstRow == other.FirstRow && EndRow == other.EndRow);
        public bool Contains(long row) => !Enabled || row >= FirstRow && row < EndRow;
    }
}
