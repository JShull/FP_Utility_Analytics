// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using UnityEngine;
    internal sealed class FPWorkbenchPlotUndoState : ScriptableObject
    {
        public string SheetId, DatasetId, Json;
        [System.NonSerialized] public string Observed;
    }
}
