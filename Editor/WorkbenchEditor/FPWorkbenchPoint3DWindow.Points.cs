// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.Globalization;
    using UnityEditor;
    using UnityEngine;
    public sealed partial class FPWorkbenchPoint3DWindow
    {
        private string InterestAxisKey => "3d:" + datasetId + ":" + string.Join(",", appliedColumns ?? Array.Empty<int>()) + ":" + program?.Fingerprint;
        private FPWorkbenchFigurePoint CaptureInterest() => new FPWorkbenchFigurePoint { X = selection.X, Y = selection.Y, Z = selection.Z, HasZ = true, References = graphSelection.CaptureReferences(), Details = "Source sample" };
        private void InterestUndoChanged() { pinnedReferencesReady = false; Repaint(); }
        private void DrawPointsOfInterest()
        {
            if (batch == null) return;
            bool changed = pointsOfInterest.Draw(selected ? CaptureInterest : (Func<FPWorkbenchFigurePoint>)null, InterestAxisKey, true, onUndo: InterestUndoChanged);
            if (changed || !pinnedReferencesReady) { graphSelection.SetPinned(workbook, pointsOfInterest.Values); pinnedReferencesReady = true; }
        }
        private Vector2? ProjectInterest(FPWorkbenchFigurePoint point, Camera camera, Rect area, bool image)
        {
            if (batch == null) return null; var frame = batch.Prepared.Frame;
            var local = new Vector3((float)((point.X - frame.OriginX) / frame.SourceUnitsPerLocalUnit), (float)((point.Y - frame.OriginY) / frame.SourceUnitsPerLocalUnit), (float)((point.Z - frame.OriginZ) / frame.SourceUnitsPerLocalUnit));
            if (float.IsNaN(local.x) || float.IsNaN(local.y) || float.IsNaN(local.z) || float.IsInfinity(local.x) || float.IsInfinity(local.y) || float.IsInfinity(local.z)) return null;
            var p = camera.WorldToViewportPoint(local); if (p.z <= camera.nearClipPlane || p.x < 0 || p.x > 1 || p.y < 0 || p.y > 1) return null;
            return new Vector2(area.x + p.x * area.width, area.y + (image ? p.y : 1 - p.y) * area.height);
        }
    }
}
