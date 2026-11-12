// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;

    public sealed partial class FPDataWorkbenchWindow
    {
        [Serializable] private sealed class CalculationPayload { public List<FPWorkbenchFormulaSet> Sets = new List<FPWorkbenchFormulaSet>(); }
        [SerializeField] private FPWorkbenchEditUndoState calculationUndo;
        [SerializeField] private bool showCalculations;
        [SerializeField] private string calculationSource, calculationName = "Calculated", calculationExpression = "C1", calculationUnits = "";
        [SerializeField] private int calculationSelection = -1;
        private string calculationUndoObserved;
        private CalculationPayload CaptureCalculations()
        {
            var payload = new CalculationPayload();
            registeredSources ??= new List<FPWorkbenchSourceDocument>();
            foreach (var source in registeredSources) if (source.HasCalculations) payload.Sets.Add(source.Calculations);
            return payload;
        }
        private void InitializeCalculationUndo()
        {
            if (calculationUndo == null) { calculationUndo = CreateInstance<FPWorkbenchEditUndoState>(); calculationUndo.hideFlags = HideFlags.HideAndDontSave; }
            calculationUndo.Json = JsonUtility.ToJson(CaptureCalculations()); calculationUndoObserved = calculationUndo.Json;
            Undo.undoRedoPerformed += RestoreCalculationUndo;
        }
        private void ClearCalculationUndo(bool destroy = false)
        {
            if (calculationUndo == null) return;
            Undo.ClearUndo(calculationUndo);
            if (destroy) { DestroyImmediate(calculationUndo); calculationUndo = null; }
            else { calculationUndo.Json = JsonUtility.ToJson(CaptureCalculations()); calculationUndoObserved = calculationUndo.Json; }
        }
        private bool ApplyCalculations(Sheet sheet, FPWorkbenchFormulaSet definitions)
        {
            try
            {
                var program = FPWorkbenchFormulaProgram.Compile(sheet.Dataset, definitions); var normalized = program.Definition;
                foreach (var other in sheets)
                    if (other.Dataset?.DatasetId == sheet.Dataset.DatasetId && other.ShowPlot)
                    {
                        var active = other.FigureLease?.Session.RequestedBinding ?? other.Binding;
                        active.ResolveDerivedY(program); active.ResolveDerivedX(program);
                    }
                RememberSource(sheet.Dataset, sheet.Name); var source = registeredSources.Find(s => s.DatasetId == sheet.Dataset.DatasetId);
                string before = JsonUtility.ToJson(CaptureCalculations()); var after = CaptureCalculations();
                int existing = after.Sets.FindIndex(s => s.DatasetId == source.DatasetId);
                bool present = normalized.Columns.Count > 0 || normalized.SourceUnits.Count > 0;
                if (present) { if (existing < 0) after.Sets.Add(normalized); else after.Sets[existing] = normalized; }
                else if (existing >= 0) after.Sets.RemoveAt(existing);
                string next = JsonUtility.ToJson(after);
                if (next.Length > 1024 * 1024) throw new InvalidOperationException("Calculation Undo metadata exceeds 2 MiB; use fewer/shorter definitions.");
                if (before == next) return false;
                calculationUndo.Json = before; calculationUndoObserved = before;
                Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Change Workbench calculations");
                Undo.RegisterCompleteObjectUndo(calculationUndo, "Change Workbench calculations");
                source.HasCalculations = present; source.Calculations = present ? normalized : null;
                calculationUndo.Json = next; calculationUndoObserved = next; EditorUtility.SetDirty(calculationUndo); Undo.CollapseUndoOperations(group);
                error = null; status = "Calculation definitions applied; original source unchanged."; Repaint(); return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }
        private void RestoreCalculationUndo()
        {
            if (calculationUndo == null || calculationUndo.Json == calculationUndoObserved) return;
            try
            {
                var payload = JsonUtility.FromJson<CalculationPayload>(calculationUndo.Json);
                var normalized = new Dictionary<string, FPWorkbenchFormulaSet>();
                foreach (var set in payload.Sets)
                {
                    var source = registeredSources.Find(s => s.DatasetId == set.DatasetId);
                    if (source == null || normalized.ContainsKey(set.DatasetId)) throw new InvalidOperationException("Calculation Undo source is no longer registered.");
                    var data = sheets.Find(s => s.Dataset?.DatasetId == set.DatasetId)?.Dataset;
                    if (data == null) admittedSources.TryGetValue(set.DatasetId, out data);
                    normalized[set.DatasetId] = (data == null ? FPWorkbenchFormulaProgram.CompileSchema(set.DatasetId, source.Import.MaximumColumns, set) : FPWorkbenchFormulaProgram.Compile(data, set)).Definition;
                }
                foreach (var source in registeredSources)
                { source.HasCalculations = normalized.TryGetValue(source.DatasetId, out var definitions); source.Calculations = definitions; }
                calculationUndoObserved = calculationUndo.Json; calculationSource = null; Repaint();
            }
            catch (Exception ex) { error = ex.Message; }
        }
        private void LoadCalculationDraft(Sheet sheet, FPWorkbenchFormulaSet definitions)
        {
            if (calculationSelection >= 0 && calculationSelection < (definitions?.Columns.Count ?? 0))
            {
                var column = definitions.Columns[calculationSelection]; calculationName = column.Name; calculationExpression = column.Expression; calculationUnits = column.ExpectedUnits;
            }
            else { calculationSelection = -1; calculationName = "Calculated"; calculationExpression = sheet.Dataset.ColumnCount > 1 ? "C1 + C2" : "C1"; calculationUnits = ""; }
        }
        private void DrawCalculationControls(Sheet sheet)
        {
            showCalculations = EditorGUILayout.Foldout(showCalculations, "Calculated columns", true); if (!showCalculations) return;
            var source = registeredSources.Find(s => s.DatasetId == sheet.Dataset.DatasetId);
            var definitions = source != null && source.HasCalculations ? source.Calculations : null;
            if (calculationSource != sheet.Dataset.DatasetId) { calculationSource = sheet.Dataset.DatasetId; calculationSelection = -1; LoadCalculationDraft(sheet, definitions); }
            var choices = new string[(definitions?.Columns.Count ?? 0) + 1]; choices[0] = "+ New calculated column";
            for (int c = 1; c < choices.Length; c++) choices[c] = "D" + c + ": " + definitions.Columns[c - 1].Name;
            int selection = EditorGUILayout.Popup("Definition", Math.Min(calculationSelection + 1, choices.Length - 1), choices) - 1;
            if (selection != calculationSelection) { calculationSelection = selection; LoadCalculationDraft(sheet, definitions); }
            calculationName = EditorGUILayout.TextField("Name", calculationName);
            calculationExpression = EditorGUILayout.TextField("Expression", calculationExpression);
            calculationUnits = EditorGUILayout.TextField(new GUIContent("Expected units", "Optional check against explicit source unit declarations. No implicit unit conversion."), calculationUnits);
            EditorGUILayout.HelpBox("C1/C2: source columns. D1/D2: calculated columns. Arithmetic only; originals stay unchanged. Select a calculated Y column to plot its values.", MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Apply calculation"))
                {
                    var next = definitions == null ? new FPWorkbenchFormulaSet { DatasetId = sheet.Dataset.DatasetId } : JsonUtility.FromJson<FPWorkbenchFormulaSet>(JsonUtility.ToJson(definitions));
                    FPWorkbenchDerivedColumn column;
                    if (calculationSelection >= 0) column = next.Columns[calculationSelection];
                    else { column = new FPWorkbenchDerivedColumn(); next.Columns.Add(column); }
                    if (column.Expression != calculationExpression) column.References.Clear();
                    column.Name = calculationName; column.Expression = calculationExpression; column.ExpectedUnits = calculationUnits;
                    int applied = calculationSelection < 0 ? next.Columns.Count - 1 : calculationSelection;
                    if (ApplyCalculations(sheet, next))
                    { calculationSelection = applied; LoadCalculationDraft(sheet, registeredSources.Find(s => s.DatasetId == sheet.Dataset.DatasetId).Calculations); }
                }
                using (new EditorGUI.DisabledScope(calculationSelection < 0))
                    if (GUILayout.Button("Remove"))
                    {
                        var next = JsonUtility.FromJson<FPWorkbenchFormulaSet>(JsonUtility.ToJson(definitions)); next.Columns.RemoveAt(calculationSelection);
                        if (ApplyCalculations(sheet, next)) { calculationSelection = -1; LoadCalculationDraft(sheet, null); }
                    }
            }
        }
    }
}
