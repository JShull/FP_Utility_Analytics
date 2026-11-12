// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using UnityEditor;
    using UnityEngine;
    internal sealed class FPWorkbenchColumnProjectionUndoState : ScriptableObject { public string Json; }
    internal sealed class FPWorkbenchColumnProjectionUndo : IDisposable
    {
        private readonly FPWorkbenchColumnProjectionUndoState state;
        private readonly Action<FPWorkbenchColumnProjection> restore;
        private string observed;
        public FPWorkbenchColumnProjectionUndo(Action<FPWorkbenchColumnProjection> restore)
        {
            this.restore = restore; state = ScriptableObject.CreateInstance<FPWorkbenchColumnProjectionUndoState>(); state.hideFlags = HideFlags.HideAndDontSave;
            Undo.undoRedoPerformed += Restore;
        }
        public void Record(string before, string after)
        {
            if (before == after) return;
            state.Json = observed = before; Undo.RegisterCompleteObjectUndo(state, "Change Workbench column layout");
            state.Json = observed = after; EditorUtility.SetDirty(state);
        }
        private void Restore()
        {
            if (state.Json == observed) return; observed = state.Json;
            var projection = JsonUtility.FromJson<FPWorkbenchColumnProjection>(state.Json); projection.Validate(); restore(projection);
        }
        public void Dispose() { Undo.undoRedoPerformed -= Restore; Undo.ClearUndo(state); UnityEngine.Object.DestroyImmediate(state); }
    }
}
