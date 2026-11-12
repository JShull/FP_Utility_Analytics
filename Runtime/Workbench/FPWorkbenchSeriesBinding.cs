// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    using System.Globalization;
    using System.IO;

    public enum FPWorkbenchXMode { Row, Numeric, UtcDateTime }

    [Serializable]
    public sealed class FPWorkbenchSeriesBinding
    {
        public FPWorkbenchXMode XMode;
        public int XColumn, YColumn, DateColumn;
        public string YDerivedId = "";
        public string XDerivedId = "";
        public bool HasDerivedY => !string.IsNullOrEmpty(YDerivedId);
        public bool HasDerivedX => !string.IsNullOrEmpty(XDerivedId);
        public string UtcFormat = "dd.MM.yyyy HH:mm:ss.ffffff";
        public double MaximumGap; // 0 disables gap detection; units follow the selected X axis.
        public FPWorkbenchSeriesBinding Copy(int columns)
        {
            if (!Enum.IsDefined(typeof(FPWorkbenchXMode), XMode) || YColumn < 0 || YColumn >= columns || (YDerivedId != null && YDerivedId.Length > 128) ||
                (XDerivedId != null && XDerivedId.Length > 128) || HasDerivedX && XMode != FPWorkbenchXMode.Numeric ||
                (XMode != FPWorkbenchXMode.Row && (XColumn < 0 || XColumn >= columns)) ||
                (XMode == FPWorkbenchXMode.UtcDateTime && (DateColumn < 0 || DateColumn >= columns || string.IsNullOrEmpty(UtcFormat) || UtcFormat.Length > 256)) ||
                double.IsNaN(MaximumGap) || double.IsInfinity(MaximumGap) || MaximumGap < 0)
                throw new ArgumentException("Invalid series columns, UTC format or gap bound.");
            if (XMode == FPWorkbenchXMode.UtcDateTime) FPWorkbenchUtcTimestamp.ValidateFormat(UtcFormat);
            return (FPWorkbenchSeriesBinding)MemberwiseClone();
        }
        public int ResolveDerivedY(FPWorkbenchFormulaProgram calculations)
        {
            if (!HasDerivedY) return -1;
            if (calculations == null) throw new ArgumentException("Calculated Y requires its formula program.");
            for (int i = 0; i < calculations.ColumnCount; i++) if (calculations.ColumnId(i) == YDerivedId) return i;
            throw new ArgumentException("Calculated Y reference was removed or belongs to another definition set.");
        }
        public int ResolveDerivedX(FPWorkbenchFormulaProgram calculations)
        {
            if (!HasDerivedX) return -1;
            if (calculations == null) throw new ArgumentException("Calculated X requires its formula program.");
            for (int i = 0; i < calculations.ColumnCount; i++) if (calculations.ColumnId(i) == XDerivedId) return i;
            throw new ArgumentException("Calculated X reference was removed or belongs to another definition set.");
        }
        internal double ReadX(string[] row, long index, ref long originTicks)
        {
            if (XMode == FPWorkbenchXMode.Row) return index;
            if (XMode == FPWorkbenchXMode.Numeric)
            {
                if (!double.TryParse(row[XColumn], NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !Finite(value))
                    throw new InvalidDataException("Invalid numeric X at logical row " + index + ". Choose a different binding or correct it explicitly.");
                return value;
            }
            if (!FPWorkbenchUtcTimestamp.TryRead(row[DateColumn], row[XColumn], UtcFormat, out long ticks))
                throw new InvalidDataException("UTC date/time does not match the declared format at logical row " + index + ".");
            if (index == 0) originTicks = ticks;
            return (ticks - originTicks) / (double)TimeSpan.TicksPerSecond;
        }
        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public readonly struct FPWorkbenchSeriesPoint
    {
        public double X { get; }
        public double Y { get; }
        public long Row { get; }
        public bool IsValid => FPWorkbenchSeriesBinding.Finite(Y);
        public bool GapBefore { get; }
        public FPWorkbenchSeriesPoint(double x, double y, long row, bool gapBefore)
        { X = x; Y = y; Row = row; GapBefore = gapBefore; }
    }

    public struct FPWorkbenchSeriesBin
    {
        public long FirstIndex, EndIndex, MinimumRow, MaximumRow, Count, InvalidCount, GapCount;
        public double FirstX, LastX, Minimum, Maximum, FirstY, LastY;
        internal static FPWorkbenchSeriesBin Point(long index, FPWorkbenchSeriesPoint point) => new FPWorkbenchSeriesBin
        {
            FirstIndex = index, EndIndex = index + 1, FirstX = point.X, LastX = point.X,
            FirstY = point.Y, LastY = point.Y, Minimum = point.Y, Maximum = point.Y,
            MinimumRow = point.Row, MaximumRow = point.Row,
            Count = point.IsValid ? 1 : 0, InvalidCount = point.IsValid ? 0 : 1, GapCount = point.GapBefore ? 1 : 0
        };
        internal void Merge(FPWorkbenchSeriesBin other)
        {
            if (EndIndex == FirstIndex) { this = other; return; }
            if (other.Count > 0)
            {
                if (Count == 0 || other.Minimum < Minimum) { Minimum = other.Minimum; MinimumRow = other.MinimumRow; }
                if (Count == 0 || other.Maximum > Maximum) { Maximum = other.Maximum; MaximumRow = other.MaximumRow; }
            }
            EndIndex = other.EndIndex; LastX = other.LastX; LastY = other.LastY;
            Count += other.Count; InvalidCount += other.InvalidCount; GapCount += other.GapCount;
        }
    }

    /// <summary>Inclusive X endpoints for a half-open original-source row range. Equal X neighbors may lie outside that row range.</summary>
    public readonly struct FPWorkbenchSeriesRowExtent
    {
        public string DatasetId { get; }
        public long FirstRow { get; }
        public long EndRow { get; }
        public double FirstX { get; }
        public double LastX { get; }
        public int RecordsRead { get; }
        internal FPWorkbenchSeriesRowExtent(string id, long first, long end, double firstX, double lastX, int read)
        { DatasetId = id; FirstRow = first; EndRow = end; FirstX = firstX; LastX = lastX; RecordsRead = read; }
    }

    public sealed class FPWorkbenchSeriesView
    {
        public string DatasetId { get; internal set; }
        public string FormulaFingerprint { get; internal set; }
        public string CorrectionFingerprint { get; internal set; }
        public FPWorkbenchSeriesBin[] Bins { get; internal set; }
        public double StartX { get; internal set; }
        public double EndX { get; internal set; }
        public long OriginUtcTicks { get; internal set; }
        public long RecordsRead { get; internal set; }
        public double Minimum { get; internal set; } = double.NaN;
        public double Maximum { get; internal set; } = double.NaN;
    }
}
