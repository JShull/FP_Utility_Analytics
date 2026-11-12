// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using UnityEditor;
    using UnityEngine;

    internal sealed class FPWorkbenchPresentationUndoState : ScriptableObject
    {
        public string Json;
    }

    /// <summary>Records only authoring controls, never viewport movement or source data.</summary>
    internal sealed class FPWorkbenchPresentationUndo : IDisposable
    {
        [Serializable] private sealed class Payload
        {
            public string Title, XLabel, YLabel;
            public FPWorkbenchAxisScale XScale, YScale;
            public FPWorkbenchPlotStyle Style;
            public float LineWidth, MarkerSize;
            public bool Grid, InvertY, LockY;
            public double XTickSpacing, YTickSpacing, MinimumY, MaximumY;
            public Payload(FPWorkbenchPlotSettings s)
            {
                XScale = s.XScale; YScale = s.YScale; Title = s.Title; XLabel = s.XLabel; YLabel = s.YLabel; Style = s.Style;
                LineWidth = s.LineWidth; MarkerSize = s.MarkerSize; Grid = s.Grid; InvertY = s.InvertY; LockY = s.LockY;
                XTickSpacing = s.XTickSpacing; YTickSpacing = s.YTickSpacing; MinimumY = s.MinimumY; MaximumY = s.MaximumY;
            }
            public void Apply(FPWorkbenchPlotSettings s)
            {
                s.XScale = XScale; s.YScale = YScale; s.Title = Title; s.XLabel = XLabel; s.YLabel = YLabel; s.Style = Style;
                s.LineWidth = LineWidth; s.MarkerSize = MarkerSize; s.Grid = Grid; s.InvertY = InvertY; s.LockY = LockY;
                s.XTickSpacing = XTickSpacing; s.YTickSpacing = YTickSpacing;
                // Automatic limits track the current view; only authored locked limits are restored.
                if (LockY) { s.MinimumY = MinimumY; s.MaximumY = MaximumY; }
            }
        }
        private readonly FPWorkbenchPresentationUndoState state;
        private readonly Action repaint;
        private FPWorkbenchPlotSettings target;
        private string observed, before;
        public FPWorkbenchPresentationUndo(Action repaint)
        {
            this.repaint = repaint;
            state = ScriptableObject.CreateInstance<FPWorkbenchPresentationUndoState>(); state.hideFlags = HideFlags.HideAndDontSave;
            Undo.undoRedoPerformed += Restore;
        }
        private static string Capture(FPWorkbenchPlotSettings s)
        {
            var p = new Payload(s);
            if (!s.LockY) { p.MinimumY = 0; p.MaximumY = 0; }
            return JsonUtility.ToJson(p);
        }
        public void Begin(FPWorkbenchPlotSettings settings)
        { target = settings; before = Capture(settings); }
        public void End()
        {
            string after = Capture(target);
            if (after == before) return;
            state.Json = observed = before;
            Undo.RegisterCompleteObjectUndo(state, "Change Workbench figure settings");
            state.Json = observed = after; EditorUtility.SetDirty(state);
        }
        private void Restore()
        {
            if (target == null || state.Json == observed) return;
            observed = state.Json; JsonUtility.FromJson<Payload>(state.Json).Apply(target); repaint?.Invoke();
        }
        public void Dispose()
        {
            Undo.undoRedoPerformed -= Restore;
            Undo.ClearUndo(state); UnityEngine.Object.DestroyImmediate(state);
        }
    }
}
