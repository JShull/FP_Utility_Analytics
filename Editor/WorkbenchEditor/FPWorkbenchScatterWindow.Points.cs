// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.Globalization;
    using UnityEditor;
    using UnityEngine;
    public sealed partial class FPWorkbenchScatterWindow
    {
        private string InterestAxisKey => "scatter:" + datasetId + ":" + appliedX + ":" + appliedY + ":" + program?.Fingerprint + ":" + JsonUtility.ToJson(appliedXBinding) + ":" + cache?.OriginUtcTicks;
        private FPWorkbenchFigurePoint CaptureInterest() => new FPWorkbenchFigurePoint { X = selection.X, Y = selection.Y, References = graphSelection.CaptureReferences(), Details = alignedOrigin == null ? "Source sample" : "Aligned pair " + (selection.Row + 1) };
        private void InterestUndoChanged() { pinnedReferencesReady = false; Repaint(); }
        private void DrawPointsOfInterest()
        {
            if (view == null) return;
            bool changed = pointsOfInterest.Draw(selected && revealing == null && pendingRevealing == null ? CaptureInterest : (Func<FPWorkbenchFigurePoint>)null, InterestAxisKey, false, onUndo: InterestUndoChanged);
            if (changed || !pinnedReferencesReady) { graphSelection.SetPinned(workbookId, pointsOfInterest.Values); pinnedReferencesReady = true; }
        }
        private Vector2? ProjectInterest(FPWorkbenchFigurePoint point, Rect plot, bool image)
        {
            if (view == null) return null; var bounds = view.Bounds;
            return FPWorkbenchFigurePoints.ProjectXY(point, plot, bounds.MinimumX, bounds.MaximumX, bounds.MinimumY, bounds.MaximumY, settings.XScale, settings.YScale, settings.InvertY, image);
        }
    }
}
