// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using UnityEditor;
    using UnityEngine;

    public sealed partial class FPWorkbenchFigureWindow
    {
        [SerializeField] private FPWorkbenchPlotUndoState bindingUndo;
        private string bindingObserved, settingsBinding;
        internal string PlotSettingsId => figureId;
        internal string PlotSettingsName => document?.Name ?? titleContent.text;
        internal FPWorkbenchFigureSession PlotSettingsSession => lease?.Session;
        private void SetSourceNames(FPWorkbenchCsvDataset dataset)
        {
            sourceColumnNames = new string[dataset.ColumnCount];
            for (int i = 0; i < sourceColumnNames.Length; i++) sourceColumnNames[i] = (i + 1) + ": " + dataset.GetColumnName(i);
        }
        private void InitializeBindingUndo()
        {
            if (bindingUndo != null) bindingObserved = bindingUndo.Json;
            Undo.undoRedoPerformed += RestoreBindingUndo;
        }
        private void DisposeBindingUndo()
        {
            Undo.undoRedoPerformed -= RestoreBindingUndo;
            if (bindingUndo == null) return;
            Undo.ClearUndo(bindingUndo); DestroyImmediate(bindingUndo); bindingUndo = null;
        }
        private void RestoreBindingUndo()
        {
            if (bindingUndo == null || bindingObserved == bindingUndo.Json || lease == null) return;
            bindingObserved = bindingUndo.Json;
            try {
                lease.Session.ApplyBinding(JsonUtility.FromJson<FPWorkbenchSeriesBinding>(bindingObserved));
                document.Binding = lease.Session.RequestedBinding;
                document.PlotColumn = document.Binding.HasDerivedY ? sourceColumnNames.Length + document.Binding.ResolveDerivedY(lease.Session.CalculationProgram) : document.Binding.YColumn;
                Repaint();
            } catch (Exception ex) { error = ex.Message; }
        }
        private void ApplyStandaloneBinding(FPWorkbenchSeriesBinding candidate)
        {
            var session = lease.Session;
            var value = candidate.Copy(sourceColumnNames.Length);
            value.ResolveDerivedX(session.CalculationProgram); value.ResolveDerivedY(session.CalculationProgram);
            if (bindingUndo == null) {
                bindingUndo = CreateInstance<FPWorkbenchPlotUndoState>(); bindingUndo.hideFlags = HideFlags.HideAndDontSave;
                bindingUndo.Json = JsonUtility.ToJson(session.RequestedBinding);
            }
            string next = JsonUtility.ToJson(value);
            if (bindingUndo.Json != next) {
                Undo.RegisterCompleteObjectUndo(bindingUndo, "Change figure plot binding");
                bindingUndo.Json = next; EditorUtility.SetDirty(bindingUndo);
            }
            bindingObserved = bindingUndo.Json; session.ApplyBinding(value); document.Binding = session.RequestedBinding; error = null;
        }
        internal void DrawPlotSettings(FPWorkbenchPlotSettingsWindow panel)
        {
            if (lease == null) { EditorGUILayout.HelpBox("Waiting for the figure source to finish loading.", MessageType.Info); return; }
            var session = lease.Session;
            string applied = JsonUtility.ToJson(session.RequestedBinding);
            if (sourceColumnNames != null && applied != settingsBinding) {
                settingsBinding = applied; document.Binding = session.RequestedBinding;
                document.PlotColumn = document.Binding.HasDerivedY ? sourceColumnNames.Length + document.Binding.ResolveDerivedY(session.CalculationProgram) : document.Binding.YColumn;
            }
            if (panel.BindingSection()) {
                if (sourceColumnNames == null || sourceColumnNames.Length == 0) EditorGUILayout.HelpBox("Reopen this figure to edit its source binding.", MessageType.Info);
                else {
                    var program = session.CalculationProgram;
                    var names = new string[sourceColumnNames.Length + (program?.ColumnCount ?? 0)];
                    Array.Copy(sourceColumnNames, names, sourceColumnNames.Length);
                    for (int i = sourceColumnNames.Length; i < names.Length; i++) names[i] = "D" + (i - sourceColumnNames.Length + 1) + ": " + program.Name(i - sourceColumnNames.Length);
                    if (FPWorkbenchPlotSettingsWindow.BindingControls(document.Binding, sourceColumnNames.Length, program, sourceColumnNames, names, ref document.PlotColumn)) {
                        try {
                            var candidate = document.Binding.Copy(sourceColumnNames.Length);
                            candidate.YDerivedId = document.PlotColumn < sourceColumnNames.Length ? "" : program.ColumnId(document.PlotColumn - sourceColumnNames.Length);
                            candidate.YColumn = candidate.HasDerivedY ? 0 : document.PlotColumn; ApplyStandaloneBinding(candidate);
                        } catch (Exception ex) { error = ex.Message; }
                    }
                }
            }
            presentationUndo ??= new FPWorkbenchPresentationUndo(Repaint);
            presentationUndo.Begin(session.Settings);
            try { panel.PresentationControls(session.Settings, session.Series, session.View); }
            finally { presentationUndo.End(); }
            if (session.Error != null) EditorGUILayout.HelpBox(session.Error.Message, MessageType.Error);
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
        }
        internal void DrawExportSettings()
        {
            if (lease != null) DrawImageExport(lease.Session);
        }
    }
}
