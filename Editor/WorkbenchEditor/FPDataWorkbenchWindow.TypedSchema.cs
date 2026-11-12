// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using UnityEditor;
    using UnityEngine;

    public sealed partial class FPDataWorkbenchWindow
    {
        [SerializeField] private bool showColumnTypes;
        [SerializeField] private int typedColumn;
        [SerializeField] private FPWorkbenchColumnDeclaration typedDraft;
        private string typedDraftDataset;
        private int typedDraftColumn = -1;
        private FPWorkbenchTypedSchema typedDraftSource;

        private CalculationPayload NormalizeDefinitions(CalculationPayload payload)
        {
            if (payload?.Sets == null || payload.Sets.Count > 64 || payload.Schemas == null || payload.Schemas.Count > 64)
                throw new InvalidDataException("Invalid bounded source definitions.");
            var schemas = new Dictionary<string, FPWorkbenchTypedSchema>();
            var sets = new Dictionary<string, FPWorkbenchFormulaSet>();
            foreach (var schema in payload.Schemas) {
                if (schema == null || registeredSources.Find(s => s.DatasetId == schema.DatasetId) == null || !schemas.TryAdd(schema.DatasetId, schema.Copy()))
                    throw new InvalidDataException("Schema source is missing or duplicated.");
            }
            foreach (var set in payload.Sets) {
                if (set == null || registeredSources.Find(s => s.DatasetId == set.DatasetId) == null || !sets.TryAdd(set.DatasetId, set))
                    throw new InvalidDataException("Calculation source is missing or duplicated.");
            }
            var normalized = new CalculationPayload();
            foreach (var source in registeredSources) {
                schemas.TryGetValue(source.DatasetId, out var schema); sets.TryGetValue(source.DatasetId, out var set);
                if (schema == null && set == null) continue;
                var data = sheets.Find(s => s.Dataset?.DatasetId == source.DatasetId)?.Dataset;
                if (data == null) admittedSources.TryGetValue(source.DatasetId, out data);
                var definitions = set == null ? new FPWorkbenchFormulaSet { DatasetId = source.DatasetId }
                    : JsonUtility.FromJson<FPWorkbenchFormulaSet>(JsonUtility.ToJson(set));
                // Active source metadata owns interpretation; frozen figures keep their separate snapshots.
                definitions.HasTypedSchema = schema != null; definitions.TypedSchema = schema;
                var program = data == null ? FPWorkbenchFormulaProgram.CompileSchema(source.DatasetId, source.Import.MaximumColumns, definitions)
                    : FPWorkbenchFormulaProgram.Compile(data, definitions);
                if (schema != null) {
                    normalized.Schemas.Add(program.Definition.TypedSchema);
                    var changes = corrections.Find(c => c.DatasetId == source.DatasetId);
                    if (changes != null) {
                        if (data == null) throw new InvalidOperationException("Open the source before validating its typed corrections.");
                        schema.Compile(data).ValidateChanges(changes);
                    }
                }
                if (set != null) normalized.Sets.Add(program.Definition);
                foreach (var sheet in sheets) if (sheet.Dataset?.DatasetId == source.DatasetId && sheet.ShowPlot) {
                    var binding = sheet.FigureLease?.Session.RequestedBinding ?? sheet.Binding;
                    binding.ResolveDerivedX(program); binding.ResolveDerivedY(program);
                }
            }
            return normalized;
        }
        private void InstallDefinitions(CalculationPayload payload)
        {
            foreach (var source in registeredSources) {
                source.Calculations = payload.Sets.Find(s => s.DatasetId == source.DatasetId); source.HasCalculations = source.Calculations != null;
                source.TypedSchema = payload.Schemas.Find(s => s.DatasetId == source.DatasetId); source.HasTypedSchema = source.TypedSchema != null;
            }
            typedDraftDataset = null; calculationSource = null;
        }
        private bool CommitDefinitions(CalculationPayload candidate, string label)
        {
            var normalized = NormalizeDefinitions(candidate);
            string before = JsonUtility.ToJson(CaptureCalculations()), next = JsonUtility.ToJson(normalized);
            if (Encoding.UTF8.GetByteCount(next) > 2 * 1024 * 1024) throw new InvalidOperationException("Source definition Undo metadata exceeds 2 MiB.");
            if (before == next) return false;
            calculationUndo.Json = before; calculationUndoObserved = before;
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName(label);
            Undo.RegisterCompleteObjectUndo(calculationUndo, label);
            InstallDefinitions(normalized);
            calculationUndo.Json = next; calculationUndoObserved = next; EditorUtility.SetDirty(calculationUndo); Undo.CollapseUndoOperations(group);
            error = null; status = "Source definitions applied; original measurements retained."; Repaint(); return true;
        }
        private bool ApplyTypedDeclaration(Sheet sheet, int column, FPWorkbenchColumnDeclaration declaration)
        {
            try {
                if (column < 0 || column >= sheet.Dataset.ColumnCount) throw new ArgumentOutOfRangeException(nameof(column));
                var payload = CaptureCalculations();
                var current = payload.Schemas.Find(s => s.DatasetId == sheet.Dataset.DatasetId);
                var next = current?.Copy() ?? new FPWorkbenchTypedSchema { DatasetId = sheet.Dataset.DatasetId };
                var columns = new List<FPWorkbenchColumnDeclaration>(next.Columns); columns.RemoveAll(c => c.Column == column);
                if (declaration != null) {
                    var value = JsonUtility.FromJson<FPWorkbenchColumnDeclaration>(JsonUtility.ToJson(declaration)); value.Column = column; value.ColumnId = sheet.Dataset.Cell(0, column).ColumnId;
                    columns.Add(value);
                }
                next.Columns = columns.ToArray();
                payload.Schemas.RemoveAll(s => s.DatasetId == sheet.Dataset.DatasetId);
                if (columns.Count > 0) payload.Schemas.Add(next);
                return CommitDefinitions(payload, "Change Workbench column type");
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }
        private void DrawTypedSchemaControls(Sheet sheet)
        {
            showColumnTypes = EditorGUILayout.Foldout(showColumnTypes, "Column types and missing values", true); if (!showColumnTypes) return;
            typedColumn = EditorGUILayout.Popup("Source column", Mathf.Clamp(typedColumn, 0, sheet.Dataset.ColumnCount - 1), sheet.ColumnNames);
            var source = registeredSources.Find(s => s.DatasetId == sheet.Dataset.DatasetId);
            var schema = source != null && source.HasTypedSchema ? source.TypedSchema : null;
            if (typedDraft == null || typedDraftDataset != sheet.Dataset.DatasetId || typedDraftColumn != typedColumn || !ReferenceEquals(typedDraftSource, schema)) {
                typedDraftDataset = sheet.Dataset.DatasetId; typedDraftColumn = typedColumn; typedDraftSource = schema;
                var existing = Array.Find(schema?.Columns ?? Array.Empty<FPWorkbenchColumnDeclaration>(), c => c.Column == typedColumn);
                typedDraft = existing == null ? new FPWorkbenchColumnDeclaration { Column = typedColumn, ColumnId = sheet.Dataset.Cell(0, typedColumn).ColumnId }
                    : new FPWorkbenchColumnParser(existing).Declaration;
            }
            typedDraft.Type = (FPWorkbenchColumnType)EditorGUILayout.EnumPopup("Declared type", typedDraft.Type);
            typedDraft.Units = EditorGUILayout.TextField("Units", typedDraft.Units);
            if (typedDraft.Type == FPWorkbenchColumnType.Real || typedDraft.Type == FPWorkbenchColumnType.SignedInteger || typedDraft.Type == FPWorkbenchColumnType.UnsignedInteger)
                typedDraft.Culture = EditorGUILayout.TextField(new GUIContent("Numeric culture", "Blank uses invariant culture; e.g. de-DE for decimal comma. Grouping is not accepted."), typedDraft.Culture);
            if (typedDraft.Type == FPWorkbenchColumnType.UtcTimestamp) typedDraft.UtcFormat = EditorGUILayout.TextField("UTC format", typedDraft.UtcFormat);
            if (typedDraft.Type == FPWorkbenchColumnType.Boolean) {
                typedDraft.TrueToken = EditorGUILayout.TextField("True token", typedDraft.TrueToken); typedDraft.FalseToken = EditorGUILayout.TextField("False token", typedDraft.FalseToken);
            }
            typedDraft.TrimWhitespace = EditorGUILayout.Toggle("Trim whitespace", typedDraft.TrimWhitespace);
            typedDraft.IgnoreTokenCase = EditorGUILayout.Toggle("Ignore token case", typedDraft.IgnoreTokenCase);
            int count = EditorGUILayout.IntSlider(new GUIContent("Missing tokens", "Each field is an exact token. An empty field explicitly declares empty text missing."), typedDraft.MissingTokens.Length, 0, 16);
            if (count != typedDraft.MissingTokens.Length) {
                var tokens = new string[count]; for (int i = 0; i < count; i++) tokens[i] = i < typedDraft.MissingTokens.Length ? typedDraft.MissingTokens[i] : "";
                typedDraft.MissingTokens = tokens;
            }
            for (int i = 0; i < count; i++) typedDraft.MissingTokens[i] = EditorGUILayout.TextField("Token " + (i + 1), typedDraft.MissingTokens[i]);
            EditorGUILayout.HelpBox("Declarations preserve raw fields. Invalid measurements remain inspectable; incompatible corrections or calculation units prevent Apply. UTC plots and alignment keys retain their explicit bindings.", MessageType.Info);
            using (new EditorGUILayout.HorizontalScope()) {
                if (GUILayout.Button("Apply type")) ApplyTypedDeclaration(sheet, typedColumn, typedDraft);
                using (new EditorGUI.DisabledScope(schema == null || !Array.Exists(schema.Columns, c => c.Column == typedColumn)))
                    if (GUILayout.Button("Clear type")) ApplyTypedDeclaration(sheet, typedColumn, null);
            }
        }
    }
}
