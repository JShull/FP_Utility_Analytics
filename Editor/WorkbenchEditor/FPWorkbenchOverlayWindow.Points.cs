// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.Globalization;
    using UnityEditor;
    using UnityEngine;
    public sealed partial class FPWorkbenchOverlayWindow
    {
        private string InterestAxisKey => "overlay:" + datasetId + ":" + JsonUtility.ToJson(appliedBinding) + ":" + string.Join(",", columns) + ":" + string.Join(",", derivedColumns);
        private FPWorkbenchFigurePoint CaptureInterest() => new FPWorkbenchFigurePoint { X = selection.Value.Point.X, Y = selection.Value.Point.Y, Series = selection.Value.Series, References = graphSelection.CaptureReferences(), Details = "Series " + (selection.Value.Series + 1) };
        private void InterestUndoChanged() { pinnedReferencesReady = false; Repaint(); }
        private void DrawPointsOfInterest()
        {
            if (view == null) return;
            bool changed = pointsOfInterest.Draw(selection.HasValue ? CaptureInterest : (Func<FPWorkbenchFigurePoint>)null, InterestAxisKey, false, Math.Max(1, columns.Count), InterestUndoChanged);
            if (changed || !pinnedReferencesReady) { graphSelection.SetPinned(workbook, pointsOfInterest.Values); pinnedReferencesReady = true; }
        }
        private Vector2? ProjectInterest(FPWorkbenchFigurePoint point, Rect plot, bool image)
        {
            if (view == null || point.Series >= view.Series.Count) return null; var mapping = PickMappings()[point.Series];
            return FPWorkbenchFigurePoints.ProjectXY(point, plot, view.StartX, view.EndX, mapping.MinimumY, mapping.MaximumY, mapping.XScale, mapping.YScale, mapping.InvertY, image);
        }
    }
}
