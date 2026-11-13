// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEngine;

    public enum FPWorkbenchFigureKind { Overlay, Histogram, Scatter, Point3D, AlignedScatter }

    [Serializable] public sealed class FPWorkbenchPoint3DSettings
    {
        public int ZColumn;
        public string ZDerivedId = "";
        public bool Clip, Orthographic, InvertOrbit;
        public bool ShowOrigin = true, ShowXAxis = true, ShowYAxis = true, ShowZAxis = true, ShowAxisValues = true;
        public double MinimumX, MaximumX = 1, MinimumY, MaximumY = 1, MinimumZ, MaximumZ = 1;
        public Quaternion Orbit = Quaternion.Euler(25, -35, 0);
        public Vector3 Pan;
        public float Zoom = 1.4f, PointSize = 4, NearClip = .001f;
        internal void Validate()
        {
            if (!Finite(NearClip) || NearClip < .00001f || NearClip > 1 || ZColumn < 0 || ZDerivedId != null && ZDerivedId.Length > 128 || !Finite(Zoom) || Zoom < .05 || Zoom > 40 || !Finite(PointSize) || PointSize < 1 || PointSize > 32 ||
                !Finite(Pan.x) || !Finite(Pan.y) || !Finite(Pan.z) || Math.Abs(Pan.x) > 1e6 || Math.Abs(Pan.y) > 1e6 || Math.Abs(Pan.z) > 1e6)
                throw new InvalidDataException("Invalid 3D axis, camera pan/zoom or point size.");
            double norm = (double)Orbit.x * Orbit.x + (double)Orbit.y * Orbit.y + (double)Orbit.z * Orbit.z + (double)Orbit.w * Orbit.w;
            if (!Finite(norm) || Math.Abs(norm - 1) > .001) throw new InvalidDataException("3D camera orientation must be a finite unit quaternion.");
            try { _ = new FPWorkbenchSpatialBounds(MinimumX, MaximumX, MinimumY, MaximumY, MinimumZ, MaximumZ); }
            catch (ArgumentException ex) { throw new InvalidDataException("Invalid 3D clip bounds.", ex); }
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    /// <summary>Durable figure metadata. Source data and transient cache/view objects are never embedded.</summary>
    [Serializable]
    public sealed class FPWorkbenchFigureDocument
    {
        public int Version = 1;
        public string Id = Guid.NewGuid().ToString("N"), Name = "Figure", DatasetId;
        public FPWorkbenchFigureKind Kind;
        public string SourceSheetId = "", SourceSheetName = "";
        public System.Collections.Generic.List<FPWorkbenchFigurePoint> PointsOfInterest = new System.Collections.Generic.List<FPWorkbenchFigurePoint>();
        public FPWorkbenchRowRange Rows = new FPWorkbenchRowRange();
        public bool HasCorrectionSnapshot;
        public FPWorkbenchChangeSet CorrectionSnapshot;
        public bool HasCalculationSnapshot;
        public FPWorkbenchFormulaSet CalculationSnapshot;
        public FPWorkbenchSeriesBinding Binding = new FPWorkbenchSeriesBinding();
        public FPWorkbenchPlotSettings Plot = new FPWorkbenchPlotSettings();
        public int[] YColumns = Array.Empty<int>();
        public string[] YDerivedIds = Array.Empty<string>();
        public Color[] Colors = Array.Empty<Color>();
        public FPWorkbenchAlignedFigureRecipe AlignedRecipe;
        public FPWorkbenchOverlayAxes Axes = new FPWorkbenchOverlayAxes();
        public FPWorkbenchPoint3DSettings Spatial = new FPWorkbenchPoint3DSettings();
        public int PointBudget = 10000;
        public int HistogramColumn, HistogramBins = 32;
        public string HistogramDerivedId = "";
        public long FirstRow, EndRow;
        public double Minimum, Maximum = 1;
        public bool Density, Grid = true, FitHistogramOnOpen;
        public FPWorkbenchInvalidPolicy InvalidPolicy = FPWorkbenchInvalidPolicy.Skip;

        public void Validate()
        {
            Rows ??= new FPWorkbenchRowRange(); Rows.Validate();
            PointsOfInterest ??= new System.Collections.Generic.List<FPWorkbenchFigurePoint>(); FPWorkbenchFigurePoint.Validate(PointsOfInterest);
            if (SourceSheetName != null && SourceSheetName.Length > 512) throw new InvalidDataException("Invalid figure sheet name.");
            if (SourceSheetId != null && SourceSheetId.Length > 128) throw new InvalidDataException("Invalid figure source sheet.");
            if (Version != 1 || string.IsNullOrEmpty(Id) || Id.Length > 128 || string.IsNullOrEmpty(Name) || Name.Length > 512 ||
                string.IsNullOrEmpty(DatasetId) || DatasetId.Length > 256 || !Enum.IsDefined(typeof(FPWorkbenchFigureKind), Kind))
                throw new InvalidDataException("Invalid figure version, kind or identity.");
            // Unity JSON may materialize a null nested class as an empty instance; the explicit marker owns presence.
            if (!HasCorrectionSnapshot)
            {
                if (CorrectionSnapshot != null && !string.IsNullOrEmpty(CorrectionSnapshot.DatasetId)) throw new InvalidDataException("Figure correction presence marker contradicts its snapshot.");
                CorrectionSnapshot = null;
            }
            else
            {
                if (CorrectionSnapshot == null) throw new InvalidDataException("Figure correction snapshot is missing.");
                CorrectionSnapshot.Validate(); if (CorrectionSnapshot.DatasetId != DatasetId) throw new InvalidDataException("Figure corrections belong to another source revision.");
            }
            if (!HasCalculationSnapshot)
            {
                if (CalculationSnapshot != null && !string.IsNullOrEmpty(CalculationSnapshot.DatasetId)) throw new InvalidDataException("Figure calculation presence marker contradicts its snapshot.");
                CalculationSnapshot = null;
            }
            else
            {
                if (CalculationSnapshot == null || CalculationSnapshot.DatasetId != DatasetId) throw new InvalidDataException("Figure calculation snapshot is missing or belongs to another source.");
                try { CalculationSnapshot = FPWorkbenchFormulaProgram.CompileSchema(DatasetId, 4096, CalculationSnapshot).Definition; }
                catch (ArgumentException ex) { throw new InvalidDataException("Invalid figure calculation snapshot.", ex); }
            }
            if (HistogramDerivedId != null && HistogramDerivedId.Length > 128 || !string.IsNullOrEmpty(HistogramDerivedId) && !HasCalculationSnapshot)
                throw new InvalidDataException("Calculated histogram needs a valid stable ID and formula snapshot.");
            if (Kind == FPWorkbenchFigureKind.Overlay)
            {
                if (Binding == null || Plot == null || YColumns == null || YColumns.Length < 1 || YColumns.Length > 8 || Colors == null || Colors.Length != YColumns.Length)
                    throw new InvalidDataException("An overlay requires 1–8 columns with corresponding colors and plot settings.");
                if (YDerivedIds == null || YDerivedIds.Length == 0) YDerivedIds = new string[YColumns.Length];
                if (YDerivedIds.Length != YColumns.Length) throw new InvalidDataException("Overlay derived identities differ from the series count.");
                var unique = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < YColumns.Length; i++)
                {
                    YDerivedIds[i] ??= ""; bool derived = YDerivedIds[i].Length > 0;
                    if (YColumns[i] < 0 || YDerivedIds[i].Length > 128 || derived && !HasCalculationSnapshot ||
                        !unique.Add(derived ? "D:" + YDerivedIds[i] : "C:" + YColumns[i])) throw new InvalidDataException("Overlay columns need distinct valid physical/derived identities.");
                }
                foreach (Color color in Colors) if (!Channel(color.r) || !Channel(color.g) || !Channel(color.b) || !Channel(color.a)) throw new InvalidDataException("Figure colors must be finite normalized values.");
                Binding.Copy(int.MaxValue); FPWorkbenchDocument.ValidatePlot(Plot);
                Axes ??= new FPWorkbenchOverlayAxes(); Axes.Validate(YColumns.Length);
                if (Binding.HasDerivedX && !HasCalculationSnapshot) throw new InvalidDataException("Overlay calculated X requires its formula snapshot.");
            }
            else if (Kind == FPWorkbenchFigureKind.Scatter || Kind == FPWorkbenchFigureKind.AlignedScatter)
            {
                if (Binding == null || Kind == FPWorkbenchFigureKind.AlignedScatter && Binding.XMode != FPWorkbenchXMode.Numeric || Plot == null || PointBudget < 1 || PointBudget > 65536 ||
                    Colors == null || Colors.Length != 1 || !Enum.IsDefined(typeof(FPWorkbenchInvalidPolicy), InvalidPolicy)) throw new InvalidDataException("Invalid scatter binding, budget, color or policy.");
                Binding.Copy(int.MaxValue); FPWorkbenchDocument.ValidatePlot(Plot);
                if (Kind == FPWorkbenchFigureKind.AlignedScatter)
                {
                    if (AlignedRecipe == null || HasCorrectionSnapshot || HasCalculationSnapshot) throw new InvalidDataException("Aligned figures need their two-source recipe, not outer single-source snapshots.");
                    AlignedRecipe.Validate(); if (AlignedRecipe.LeftDatasetId != DatasetId) throw new InvalidDataException("Aligned primary source differs from its recipe.");
                }
                if (Plot.XScale == FPWorkbenchAxisScale.Log10 && Plot.StartX <= 0 || Plot.YScale == FPWorkbenchAxisScale.Log10 && Plot.MinimumY <= 0)
                    throw new InvalidDataException("Logarithmic scatter bounds must be positive.");
                if (!Finite(Plot.EndX - Plot.StartX) || Plot.MaximumY <= Plot.MinimumY || !Finite(Plot.MaximumY - Plot.MinimumY)) throw new InvalidDataException("Scatter viewport requires finite positive X/Y spans.");
                if ((Binding.HasDerivedX || Binding.HasDerivedY) && !HasCalculationSnapshot) throw new InvalidDataException("Calculated scatter axes require their formula snapshot.");
                var color = Colors[0]; if (!Channel(color.r) || !Channel(color.g) || !Channel(color.b) || !Channel(color.a)) throw new InvalidDataException("Scatter color must be finite and normalized.");
            }
            else if (Kind == FPWorkbenchFigureKind.Point3D)
            {
                if (Binding == null || Binding.XMode != FPWorkbenchXMode.Numeric || Spatial == null || PointBudget < 1 || PointBudget > 65536 || Colors == null || Colors.Length != 1 ||
                    !Enum.IsDefined(typeof(FPWorkbenchInvalidPolicy), InvalidPolicy)) throw new InvalidDataException("Invalid 3D binding, point budget, color or policy.");
                Binding.Copy(int.MaxValue); Spatial.Validate();
                if ((Binding.HasDerivedX || Binding.HasDerivedY || !string.IsNullOrEmpty(Spatial.ZDerivedId)) && !HasCalculationSnapshot) throw new InvalidDataException("Calculated XYZ needs a frozen formula snapshot.");
                var color = Colors[0]; if (!Channel(color.r) || !Channel(color.g) || !Channel(color.b) || !Channel(color.a)) throw new InvalidDataException("3D point color must be normalized.");
            }
            else if (HistogramColumn < 0 || HistogramBins < 1 || HistogramBins > 4096 || FirstRow < 0 || EndRow < FirstRow ||
                !Finite(Minimum) || !Finite(Maximum) || Maximum <= Minimum || !Finite(Maximum - Minimum) ||
                !Enum.IsDefined(typeof(FPWorkbenchInvalidPolicy), InvalidPolicy)) throw new InvalidDataException("Invalid histogram column, row range, bins or limits.");
        }
        internal void ValidateSource(FPWorkbenchCsvDataset source, FPWorkbenchCsvDataset secondary = null)
        {
            Validate();
            Rows.Validate(source.RowCount);
            if (Binding != null) (Binding.Rows ?? new FPWorkbenchRowRange()).Validate(source.RowCount);
            if (source.DatasetId != DatasetId) throw new InvalidDataException("Figure source revision differs.");
            FPWorkbenchFormulaProgram calculations = null;
            if (HasCalculationSnapshot)
            {
                try { calculations = FPWorkbenchFormulaProgram.Compile(source, CalculationSnapshot); }
                catch (ArgumentException ex) { throw new InvalidDataException("Figure calculations differ from the source schema.", ex); }
            }
            if (Kind == FPWorkbenchFigureKind.AlignedScatter) AlignedRecipe.ValidateSources(source, secondary);
            else if (Kind == FPWorkbenchFigureKind.Overlay)
            {
                Binding.Copy(source.ColumnCount);
                try { Binding.ResolveDerivedX(calculations); } catch (ArgumentException ex) { throw new InvalidDataException("Overlay calculated X is unresolved.", ex); }
                for (int i = 0; i < YColumns.Length; i++)
                {
                    if (YColumns[i] >= source.ColumnCount) throw new InvalidDataException("Figure column exceeds its source schema.");
                    if (YDerivedIds[i].Length > 0)
                        try { new FPWorkbenchSeriesBinding { YDerivedId = YDerivedIds[i] }.ResolveDerivedY(calculations); }
                        catch (ArgumentException ex) { throw new InvalidDataException("Overlay calculated reference is unresolved.", ex); }
                }
            }
            else if (Kind == FPWorkbenchFigureKind.Scatter)
            {
                Binding.Copy(source.ColumnCount);
                try { Binding.ResolveDerivedX(calculations); Binding.ResolveDerivedY(calculations); }
                catch (ArgumentException ex) { throw new InvalidDataException("Scatter calculated axis is unresolved.", ex); }
            }
            else if (Kind == FPWorkbenchFigureKind.Point3D)
            {
                Binding.Copy(source.ColumnCount);
                if (Spatial.ZColumn >= source.ColumnCount) throw new InvalidDataException("3D Z axis exceeds its source schema.");
                try
                {
                    Binding.ResolveDerivedX(calculations); Binding.ResolveDerivedY(calculations);
                    if (!string.IsNullOrEmpty(Spatial.ZDerivedId)) new FPWorkbenchSeriesBinding { YDerivedId = Spatial.ZDerivedId }.ResolveDerivedY(calculations);
                }
                catch (ArgumentException ex) { throw new InvalidDataException("3D calculated axis is unresolved.", ex); }
            }
            else
            {
                if (EndRow > source.RowCount || string.IsNullOrEmpty(HistogramDerivedId) && HistogramColumn >= source.ColumnCount) throw new InvalidDataException("Histogram selection exceeds its source.");
                if (!string.IsNullOrEmpty(HistogramDerivedId))
                    try { new FPWorkbenchSeriesBinding { YDerivedId = HistogramDerivedId }.ResolveDerivedY(calculations); }
                    catch (ArgumentException ex) { throw new InvalidDataException("Histogram calculated reference is unresolved.", ex); }
            }
        }
        private static bool Channel(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0 && value <= 1;
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
