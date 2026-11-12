// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;

    public sealed partial class FPDataWorkbenchWindow
    {
        [Serializable] private sealed class PlotBindingPayload
        {
            public FPWorkbenchSeriesBinding Binding;
            public int Column;
            public bool Visible;
        }
        [SerializeField] private List<FPWorkbenchPlotUndoState> plotUndo = new List<FPWorkbenchPlotUndoState>();
        private bool restoringPlotUndo;
        private void InitializePlotUndo()
        {
            foreach (var state in plotUndo) if (state != null) state.Observed = state.Json;
            Undo.undoRedoPerformed += RestorePlotUndo;
        }
        private FPWorkbenchPlotUndoState EnsurePlotUndo(Sheet sheet)
        {
            var state = plotUndo.Find(p => p != null && p.SheetId == sheet.Id && p.DatasetId == sheet.Dataset.DatasetId);
            if (state != null) return state;
            state = CreateInstance<FPWorkbenchPlotUndoState>(); state.hideFlags = HideFlags.HideAndDontSave;
            state.SheetId = sheet.Id; state.DatasetId = sheet.Dataset.DatasetId;
            var binding = sheet.Series?.Binding ?? sheet.Binding;
            state.Json = JsonUtility.ToJson(new PlotBindingPayload { Binding = binding.Copy(sheet.Dataset.ColumnCount),
                Column = sheet.Series == null ? sheet.PlotColumn : PlotColumnForBinding(sheet, binding), Visible = sheet.ShowPlot });
            state.Observed = state.Json; plotUndo.Add(state); return state;
        }
        private void RecordPlotBinding(Sheet sheet)
        {
            if (restoringPlotUndo) return;
            var state = EnsurePlotUndo(sheet);
            string next = JsonUtility.ToJson(new PlotBindingPayload { Binding = sheet.Binding.Copy(sheet.Dataset.ColumnCount), Column = sheet.PlotColumn, Visible = sheet.ShowPlot });
            if (state.Json == next) return;
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Change Workbench plot binding"); Undo.RegisterCompleteObjectUndo(state, "Change Workbench plot binding");
            state.Json = next; state.Observed = next; EditorUtility.SetDirty(state); Undo.CollapseUndoOperations(group);
        }
        private void RestorePlotUndo()
        {
            restoringPlotUndo = true;
            try
            {
                foreach (var state in plotUndo)
                {
                    if (state == null || state.Json == state.Observed) continue;
                    state.Observed = state.Json;
                    var sheet = sheets.Find(s => s.Id == state.SheetId && s.Dataset?.DatasetId == state.DatasetId);
                    if (sheet == null) continue;
                    var payload = JsonUtility.FromJson<PlotBindingPayload>(state.Json);
                    sheet.Binding = payload.Binding.Copy(sheet.Dataset.ColumnCount); sheet.PlotColumn = payload.Column;
                    EnsureTableSchema(sheet);
                    if (sheet.Binding.HasDerivedY) sheet.PlotColumn = PlotColumnForBinding(sheet, sheet.Binding);
                    summary = null;
                    if (payload.Visible) StartSeries(sheet, false);
                    else
                    {
                        sheet.FigureLease?.Dispose(); sheet.FigureLease = null; sheet.Series = null; sheet.ShowPlot = false;
                    }
                }
                Repaint();
            }
            catch (Exception ex) { error = ex.Message; }
            finally { restoringPlotUndo = false; }
        }
        private void ClearPlotUndo(string sheetId = null)
        {
            for (int i = plotUndo.Count - 1; i >= 0; i--)
            {
                var state = plotUndo[i];
                if (state != null && sheetId != null && state.SheetId != sheetId) continue;
                if (state != null) { Undo.ClearUndo(state); DestroyImmediate(state); }
                plotUndo.RemoveAt(i);
            }
        }
    }
}
