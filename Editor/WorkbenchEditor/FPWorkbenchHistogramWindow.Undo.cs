// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.IO;
    using UnityEditor;
    using UnityEngine;
    public sealed partial class FPWorkbenchHistogramWindow
    {
        [Serializable] private sealed class HistogramSettings
        {
            public int Bins;
            public long FirstRow, EndRow;
            public double Minimum, Maximum;
            public FPWorkbenchInvalidPolicy InvalidPolicy;
            public bool Density, Grid, FitOnOpen;
        }
        [SerializeField] private string publishedSettings;
        private FPWorkbenchPresentationUndoState histogramUndo;
        private string histogramUndoObserved;
        private bool recordHistogramFit;
        private FPWorkbenchInvalidPolicy buildingHistogramPolicy;
        private HistogramSettings CaptureHistogramSettings() => new HistogramSettings { Bins = bins, FirstRow = firstRow, EndRow = endRow,
            Minimum = minimum, Maximum = maximum, InvalidPolicy = invalidPolicy, Density = density, Grid = grid, FitOnOpen = fitOnOpen };
        private string HistogramSettingsJson() => JsonUtility.ToJson(CaptureHistogramSettings());
        private void ApplyPresentationPreset(string json)
        {
            var preset = FPWorkbenchHistogramPreset.Parse(json);
            string before = HistogramSettingsJson();
            density = preset.Density; grid = preset.Grid;
            RecordHistogramSettings(before); Repaint();
        }
        private HistogramSettings PublishedHistogramSettings()
        {
            if (string.IsNullOrEmpty(publishedSettings)) return null;
            if (publishedSettings.Length > 4096) throw new InvalidDataException("Histogram published settings exceed their bound.");
            return JsonUtility.FromJson<HistogramSettings>(publishedSettings) ?? throw new InvalidDataException("Missing published histogram settings.");
        }
        private void ApplyHistogramSettings(HistogramSettings s)
        {
            bins = s.Bins; firstRow = s.FirstRow; endRow = s.EndRow; minimum = s.Minimum; maximum = s.Maximum;
            invalidPolicy = s.InvalidPolicy; density = s.Density; grid = s.Grid; fitOnOpen = s.FitOnOpen;
        }
        private void RestorePublishedQuery()
        {
            var published = PublishedHistogramSettings(); if (published == null) return;
            bool currentDensity = density, currentGrid = grid; ApplyHistogramSettings(published); density = currentDensity; grid = currentGrid;
        }
        private void PublishHistogramSettings()
        {
            var s = CaptureHistogramSettings(); s.Bins = result.Counts.Count; s.FirstRow = result.FirstRow; s.EndRow = result.EndRow;
            s.Minimum = result.Edges[0]; s.Maximum = result.Edges[result.Edges.Count - 1]; s.InvalidPolicy = buildingHistogramPolicy; s.FitOnOpen = false;
            publishedSettings = JsonUtility.ToJson(s);
        }
        private void RecordHistogramSettings(string before)
        {
            string after = HistogramSettingsJson(); if (after == before) return;
            if (histogramUndo == null) {
                histogramUndo = ScriptableObject.CreateInstance<FPWorkbenchPresentationUndoState>(); histogramUndo.hideFlags = HideFlags.HideAndDontSave;
                Undo.undoRedoPerformed += RestoreHistogramUndo;
            }
            histogramUndo.Json = histogramUndoObserved = before; Undo.RegisterCompleteObjectUndo(histogramUndo, "Change Workbench histogram settings");
            histogramUndo.Json = histogramUndoObserved = after; EditorUtility.SetDirty(histogramUndo);
        }
        private void RestoreHistogramUndo()
        {
            if (histogramUndo.Json == histogramUndoObserved) return;
            histogramUndoObserved = histogramUndo.Json;
            cancellation?.Cancel(); ReleaseHistogramOperation();
            ApplyHistogramSettings(JsonUtility.FromJson<HistogramSettings>(histogramUndo.Json));
            var shown = PublishedHistogramSettings();
            bool sameQuery = result != null && shown != null && !fitOnOpen && bins == shown.Bins && firstRow == shown.FirstRow && endRow == shown.EndRow &&
                minimum == shown.Minimum && maximum == shown.Maximum && invalidPolicy == shown.InvalidPolicy;
            error = null;
            if (!sameQuery && dataset != null) { if (fitOnOpen) { BeginFit(); recordHistogramFit = false; } else BeginBuild(); }
            Repaint();
        }
        private void DisposeHistogramUndo()
        {
            Undo.undoRedoPerformed -= RestoreHistogramUndo;
            if (histogramUndo != null) { Undo.ClearUndo(histogramUndo); UnityEngine.Object.DestroyImmediate(histogramUndo); histogramUndo = null; }
        }
    }
}
