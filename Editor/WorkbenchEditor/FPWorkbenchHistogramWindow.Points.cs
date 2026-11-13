// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.Globalization;
    using UnityEditor;
    using UnityEngine;
    public sealed partial class FPWorkbenchHistogramWindow
    {
        private FPWorkbenchFigurePoint selectedInterest;
        private string InterestAxisKey => "histogram:" + datasetId + ":" + column + ":" + derivedId + ":" + density + ":" + (result == null ? "pending" : result.FirstRow + ":" + result.EndRow + ":" + result.Counts.Count + ":" + result.Edges[0].ToString("R", CultureInfo.InvariantCulture) + ":" + result.Edges[result.Edges.Count - 1].ToString("R", CultureInfo.InvariantCulture));
        private FPWorkbenchFigurePoint CaptureInterest() => selectedInterest;
        private void DrawPointsOfInterest()
        {
            if (result != null) pointsOfInterest.Draw(selectedInterest != null && selectedInterest.AxisKey == InterestAxisKey ? CaptureInterest : (Func<FPWorkbenchFigurePoint>)null, InterestAxisKey, false, onUndo: Repaint);
        }
        private Vector2? ProjectInterest(FPWorkbenchFigurePoint point, Rect area, bool image)
        {
            if (result == null) return null; double peak = 0;
            for (int i = 0; i < result.Counts.Count; i++) peak = Math.Max(peak, density ? result.Density(i) : result.Counts[i]); if (peak == 0) peak = 1;
            return FPWorkbenchFigurePoints.ProjectXY(point, area, result.Edges[0], result.Edges[result.Edges.Count - 1], 0, peak, FPWorkbenchAxisScale.Linear, FPWorkbenchAxisScale.Linear, false, image);
        }
    }
}
