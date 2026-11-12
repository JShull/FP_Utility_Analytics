// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using UnityEditor;
    using UnityEngine;

    /// <summary>Restores axis sides by stable series identity; unrelated navigation and later series are untouched.</summary>
    internal sealed class FPWorkbenchAxisAssignmentUndo : IDisposable
    {
        [Serializable] private sealed class Payload { public string[] Ids; public bool[] Right; }
        private readonly Func<string[]> identities;
        private readonly Func<FPWorkbenchOverlayAxes> axes;
        private readonly Action repaint;
        private readonly FPWorkbenchPresentationUndoState state;
        private string observed;
        public FPWorkbenchAxisAssignmentUndo(Func<string[]> identities, Func<FPWorkbenchOverlayAxes> axes, Action repaint)
        {
            this.identities = identities; this.axes = axes; this.repaint = repaint;
            state = ScriptableObject.CreateInstance<FPWorkbenchPresentationUndoState>(); state.hideFlags = HideFlags.HideAndDontSave;
            Undo.undoRedoPerformed += Restore;
        }
        private string Capture() => JsonUtility.ToJson(new Payload { Ids = identities(), Right = (bool[])axes().Right.Clone() });
        public void SetSide(int index, bool right)
        {
            var sides = axes().Right;
            if (index < 0 || index >= sides.Length) throw new ArgumentOutOfRangeException(nameof(index));
            if (sides[index] == right) return;
            state.Json = observed = Capture(); Undo.RegisterCompleteObjectUndo(state, "Change Workbench Y-axis assignment");
            sides[index] = right; state.Json = observed = Capture(); EditorUtility.SetDirty(state); repaint?.Invoke();
        }
        private void Restore()
        {
            if (state.Json == observed) return; observed = state.Json;
            var payload = JsonUtility.FromJson<Payload>(state.Json); var ids = identities(); var target = axes(); target.EnsureCount(ids.Length);
            for (int i = 0; i < ids.Length; i++) { int prior = Array.IndexOf(payload.Ids, ids[i]); if (prior >= 0) target.Right[i] = payload.Right[prior]; }
            repaint?.Invoke();
        }
        public void Dispose() { Undo.undoRedoPerformed -= Restore; Undo.ClearUndo(state); UnityEngine.Object.DestroyImmediate(state); }
    }
}
