// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    [Serializable] public sealed class FPWorkbenchFigurePointReference
    {
        public string DatasetId = "", SheetId = "", PrimaryColumn = "";
        public long Row;
        public string[] Columns = Array.Empty<string>();
    }
    /// <summary>A named annotation in frozen figure coordinates, optionally linked to exact original source cells.</summary>
    [Serializable] public sealed class FPWorkbenchFigurePoint
    {
        public string Label = "Point", AxisKey = "", Details = "";
        public double X, Y, Z;
        public bool HasZ;
        // Inverted flags preserve the behavior of figures saved before these options existed.
        public bool ExcludeFromExport, HideExportValues, HideExportRow;
        // Zero retains legacy automatic precision; 1..10 mean 0..9 decimal places.
        public int ExportDecimalPlacesPlusOne;
        public bool UseExportColor, ExportLabelAtPoint;
        public UnityEngine.Color ExportColor = UnityEngine.Color.yellow;
        public int Series;
        public FPWorkbenchFigurePointReference[] References = Array.Empty<FPWorkbenchFigurePointReference>();
        public FPWorkbenchFigurePoint Copy() => UnityEngine.JsonUtility.FromJson<FPWorkbenchFigurePoint>(UnityEngine.JsonUtility.ToJson(this));
        public static void Validate(List<FPWorkbenchFigurePoint> points)
        {
            if (points == null || points.Count > 16) throw new InvalidDataException("A figure supports up to 16 points of interest.");
            foreach (var point in points) {
                if (point == null || point.Label == null || point.Label.Length > 128 || point.AxisKey == null || point.AxisKey.Length > 4096 ||
                    point.Details == null || point.Details.Length > 512 || !Finite(point.X) || !Finite(point.Y) || !Finite(point.Z) || point.Series < 0 || point.Series > 7 ||
                    point.ExportDecimalPlacesPlusOne < 0 || point.ExportDecimalPlacesPlusOne > 10 ||
                    !Finite(point.ExportColor.r) || !Finite(point.ExportColor.g) || !Finite(point.ExportColor.b) || !Finite(point.ExportColor.a) ||
                    point.References == null || point.References.Length > 2) throw new InvalidDataException("Invalid figure point of interest.");
                foreach (var r in point.References) if (r == null || string.IsNullOrEmpty(r.DatasetId) || r.DatasetId.Length > 256 || r.SheetId == null || r.SheetId.Length > 128 ||
                    r.Row < 0 || r.PrimaryColumn == null || r.PrimaryColumn.Length > 512 || r.Columns == null || r.Columns.Length > 4096 ||
                    Array.Exists(r.Columns, c => string.IsNullOrEmpty(c) || c.Length > 512)) throw new InvalidDataException("Invalid point source reference.");
            }
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
