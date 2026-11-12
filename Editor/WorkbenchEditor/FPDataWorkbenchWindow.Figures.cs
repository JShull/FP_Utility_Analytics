// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;

    public sealed partial class FPDataWorkbenchWindow
    {
        [SerializeField] private List<FPWorkbenchFigureDocument> savedFigures = new List<FPWorkbenchFigureDocument>();
        [SerializeField] private bool showSavedFigures;
        internal static void RememberFigure(string owner, FPWorkbenchFigureDocument figure)
        {
            if (string.IsNullOrEmpty(owner)) return;
            foreach (var window in Resources.FindObjectsOfTypeAll<FPDataWorkbenchWindow>())
            {
                if (window.workbookId != owner) continue;
                try { window.StoreFigure(figure); }
                catch (Exception ex) { window.error = "Figure settings not retained: " + ex.Message; }
            }
        }
        private void StoreFigure(FPWorkbenchFigureDocument figure)
        {
            figure.Validate(); savedFigures ??= new List<FPWorkbenchFigureDocument>();
            int index = savedFigures.FindIndex(f => f.Id == figure.Id);
            if (index < 0 && savedFigures.Count >= 32) throw new InvalidOperationException("A workbook supports up to 32 figures.");
            var copy = JsonUtility.FromJson<FPWorkbenchFigureDocument>(JsonUtility.ToJson(figure));
            if (index < 0) savedFigures.Add(copy); else savedFigures[index] = copy;
            Repaint();
        }
        private void CaptureOpenFigures()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<FPWorkbenchHistogramWindow>())
                if (window.WorkbookId == workbookId) StoreFigure(window.DescribeFigure());
            foreach (var window in Resources.FindObjectsOfTypeAll<FPWorkbenchOverlayWindow>())
                if (window.WorkbookId == workbookId) StoreFigure(window.DescribeFigure());
            foreach (var window in Resources.FindObjectsOfTypeAll<FPWorkbenchScatterWindow>())
                if (window.WorkbookId == workbookId) StoreFigure(window.DescribeFigure());
            foreach (var window in Resources.FindObjectsOfTypeAll<FPWorkbenchPoint3DWindow>())
                if (window.WorkbookId == workbookId) StoreFigure(window.DescribeFigure());
        }
        private void OpenSavedFigure(FPWorkbenchFigureDocument figure)
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<FPWorkbenchHistogramWindow>())
                if (window.WorkbookId == workbookId && window.FigureId == figure.Id) { window.Focus(); return; }
            foreach (var window in Resources.FindObjectsOfTypeAll<FPWorkbenchOverlayWindow>())
                if (window.WorkbookId == workbookId && window.FigureId == figure.Id) { window.Focus(); return; }
            foreach (var window in Resources.FindObjectsOfTypeAll<FPWorkbenchScatterWindow>())
                if (window.WorkbookId == workbookId && window.FigureId == figure.Id) { window.Focus(); return; }
            foreach (var window in Resources.FindObjectsOfTypeAll<FPWorkbenchPoint3DWindow>())
                if (window.WorkbookId == workbookId && window.FigureId == figure.Id) { window.Focus(); return; }
            if (figure.Kind == FPWorkbenchFigureKind.AlignedScatter)
            {
                var a = FindSource(figure.AlignedRecipe.LeftDatasetId); if (a == null) { OpenFigureSource(figure, figure.AlignedRecipe.LeftDatasetId); return; }
                var b = FindSource(figure.AlignedRecipe.RightDatasetId); if (b == null) { OpenFigureSource(figure, figure.AlignedRecipe.RightDatasetId); return; }
                try { FPWorkbenchScatterWindow.OpenSavedAligned(workbookId, a, b, figure); } catch (Exception ex) { error = ex.Message; }
                return;
            }
            var data = FindSource(figure.DatasetId);
            if (data == null) { OpenFigureSource(figure); return; }
            try
            {
                if (figure.Kind == FPWorkbenchFigureKind.Histogram) FPWorkbenchHistogramWindow.OpenSaved(workbookId, data, figure);
                else if (figure.Kind == FPWorkbenchFigureKind.Scatter) FPWorkbenchScatterWindow.OpenSaved(workbookId, data, figure);
                else if (figure.Kind == FPWorkbenchFigureKind.Point3D) FPWorkbenchPoint3DWindow.OpenSaved(workbookId, data, figure);
                else FPWorkbenchOverlayWindow.OpenSaved(workbookId, data, figure);
            }
            catch (Exception ex) { error = ex.Message; }
        }
        private void CloseOwnedFigures(string id = null)
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<FPWorkbenchHistogramWindow>())
                if (window.WorkbookId == workbookId && (id == null || window.FigureId == id)) window.Close();
            foreach (var window in Resources.FindObjectsOfTypeAll<FPWorkbenchOverlayWindow>())
                if (window.WorkbookId == workbookId && (id == null || window.FigureId == id)) window.Close();
            foreach (var window in Resources.FindObjectsOfTypeAll<FPWorkbenchScatterWindow>())
                if (window.WorkbookId == workbookId && (id == null || window.FigureId == id)) window.Close();
            foreach (var window in Resources.FindObjectsOfTypeAll<FPWorkbenchPoint3DWindow>())
                if (window.WorkbookId == workbookId && (id == null || window.FigureId == id)) window.Close();
        }
        private void DrawSavedFigures()
        {
            savedFigures ??= new List<FPWorkbenchFigureDocument>();
            showSavedFigures = EditorGUILayout.Foldout(showSavedFigures, "Saved figures • " + savedFigures.Count, true);
            if ((sourceOpening != null || pendingSourceOpening != null) && GUILayout.Button("Cancel figure source verification")) CancelSourceOpening();
            if (!showSavedFigures) return;
            for (int i = 0; i < savedFigures.Count; i++)
            {
                var figure = savedFigures[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(figure.Name)) OpenSavedFigure(figure);
                    if (GUILayout.Button("×", GUILayout.Width(24)))
                    { CloseOwnedFigures(figure.Id); savedFigures.RemoveAll(f => f.Id == figure.Id); break; }
                }
            }
        }
    }
}
