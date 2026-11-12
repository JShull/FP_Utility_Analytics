// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using UnityEditor;
    using UnityEngine;

    public sealed partial class FPDataWorkbenchWindow
    {
        [Serializable] private sealed class CorrectionPayload { public List<FPWorkbenchChangeSet> Sources = new List<FPWorkbenchChangeSet>(); }
        private sealed class SnapshotEntry { public FPWorkbenchChangeSet Source; public long Revision; public FPWorkbenchChangeSnapshot Snapshot; }
        [SerializeField] private List<FPWorkbenchChangeSet> corrections = new List<FPWorkbenchChangeSet>();
        [SerializeField] private FPWorkbenchEditUndoState editUndo;
        [SerializeField] private string selectedDataset, selectedOriginal = "", editValue = "", editReason = "";
        [SerializeField] private long selectedRow;
        [SerializeField] private int selectedColumn;
        [SerializeField] private FPWorkbenchEditKind editKind;
        private readonly Dictionary<string, SnapshotEntry> correctionSnapshots = new Dictionary<string, SnapshotEntry>();
        private string undoApplied;
        private Task<List<FPWorkbenchChangeSet>> changeLoad;
        private Task changeSave;
        private FPWorkbenchEditorResources.ScanReservation changeMemory;
        private CancellationTokenSource changeCancel;
        private string importBase;
        private void InitializeCorrectionUndo()
        {
            if (editUndo == null) { editUndo = CreateInstance<FPWorkbenchEditUndoState>(); editUndo.hideFlags = HideFlags.HideAndDontSave; }
            SyncCorrectionUndo(); Undo.undoRedoPerformed += RestoreCorrectionUndo;
        }
        private void SyncCorrectionUndo()
        { editUndo.Json = JsonUtility.ToJson(new CorrectionPayload { Sources = corrections }); undoApplied = editUndo.Json; }
        private void RestoreCorrectionUndo()
        {
            if (editUndo == null || editUndo.Json == undoApplied) return;
            var restored = JsonUtility.FromJson<CorrectionPayload>(editUndo.Json);
            try { foreach (var set in restored.Sources) set.Validate(); ValidateTypedCorrections(restored.Sources); }
            catch (Exception ex) { error = "Correction Undo/Redo refused under the current schema: " + ex.Message; editUndo.Json = undoApplied; EditorUtility.SetDirty(editUndo); Repaint(); return; }
            corrections = restored.Sources; undoApplied = editUndo.Json; correctionSnapshots.Clear(); RefreshCorrectionViews(); Repaint();
        }
        private FPWorkbenchChangeSnapshot SnapshotFor(string dataset)
        {
            var source = corrections.Find(c => c.DatasetId == dataset);
            if (source == null || source.Cells.Count == 0) return null;
            if (!correctionSnapshots.TryGetValue(dataset, out var cached) || !ReferenceEquals(cached.Source, source) || cached.Revision != source.Revision)
            { cached = new SnapshotEntry { Source = source, Revision = source.Revision, Snapshot = source.Snapshot() }; correctionSnapshots[dataset] = cached; }
            return cached.Snapshot;
        }
        private void RefreshCorrectionViews()
        {
            summary = null;
            foreach (var sheet in sheets)
                if (sheet.Dataset != null && sheet.FigureLease != null)
                    sheet.FigureLease.Session.ApplyChanges(sheet.ShowOriginal ? null : SnapshotFor(sheet.Dataset.DatasetId));
        }
        private bool ApplyCorrection(Sheet sheet, long row, int column, string original, string value, FPWorkbenchEditKind kind, string reason, bool revert)
        {
            var address = sheet.Dataset.Cell(row, column);
            var old = corrections.Find(c => c.DatasetId == address.DatasetId);
            var candidate = old == null ? new FPWorkbenchChangeSet { DatasetId = address.DatasetId } : JsonUtility.FromJson<FPWorkbenchChangeSet>(JsonUtility.ToJson(old));
            bool changed = revert ? candidate.Revert(address) : candidate.Set(address, original, value, kind, reason);
            if (!changed) return false;
            var next = new List<FPWorkbenchChangeSet>(corrections);
            if (old == null) next.Add(candidate); else next[next.IndexOf(old)] = candidate;
            CommitCorrections(next, revert ? "Revert Workbench cell correction" : "Correct Workbench cell"); return true;
        }
        private void CommitCorrections(List<FPWorkbenchChangeSet> next, string label)
        {
            if (next.Count > 64) throw new InvalidOperationException("Too many correction source revisions.");
            foreach (var source in next) source.Validate();
            ValidateTypedCorrections(next);
            string payload = JsonUtility.ToJson(new CorrectionPayload { Sources = next });
            if (Encoding.UTF8.GetByteCount(payload) > 2 * 1024 * 1024) throw new InvalidOperationException("The first correction slice supports at most 2 MiB of combined correction metadata; indexed larger overlays remain planned.");
            SyncCorrectionUndo(); Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            Undo.RegisterCompleteObjectUndo(editUndo, label); corrections = next;
            SyncCorrectionUndo(); Undo.CollapseUndoOperations(group); Undo.IncrementCurrentGroup();
            correctionSnapshots.Clear(); RefreshCorrectionViews(); status = "Correction metadata updated; original source unchanged."; Repaint();
        }
        private void ValidateTypedCorrections(List<FPWorkbenchChangeSet> sets)
        {
            foreach (var set in sets) {
                var source = registeredSources.Find(s => s.DatasetId == set.DatasetId);
                if (source == null || !source.HasTypedSchema) continue;
                var data = FindSource(set.DatasetId) ?? throw new InvalidOperationException("Admit the typed source before changing its corrections.");
                source.TypedSchema.Compile(data).ValidateChanges(set);
            }
        }
        private List<FPWorkbenchCsvDataset> OpenDatasets()
        { var result = new List<FPWorkbenchCsvDataset>(); foreach (var sheet in sheets) if (sheet.Dataset != null) result.Add(sheet.Dataset); return result; }
        private bool TickChanges()
        {
            bool changed = false;
            if (changeSave != null && changeSave.IsCompleted)
            {
                if (changeSave.IsFaulted) error = changeSave.Exception.GetBaseException().Message;
                else status = changeSave.IsCanceled ? "Correction export cancelled." : "Correction file exported; original sources unchanged.";
                changeSave = null; changed = true;
                changeMemory?.Dispose(); changeMemory = null;
            }
            if (changeLoad != null && changeLoad.IsCompleted)
            {
                try
                {
                    if (changeLoad.Status == TaskStatus.RanToCompletion)
                    {
                        if (importBase != JsonUtility.ToJson(new CorrectionPayload { Sources = corrections })) throw new InvalidOperationException("Corrections changed during import; retry to avoid overwriting newer edits.");
                        var next = new List<FPWorkbenchChangeSet>(corrections);
                        foreach (var imported in changeLoad.Result)
                        {
                            if (!sheets.Exists(s => s.Dataset?.DatasetId == imported.DatasetId)) throw new InvalidOperationException("Correction source is no longer open; current edits retained.");
                            next.RemoveAll(c => c.DatasetId == imported.DatasetId); next.Add(imported);
                        }
                        CommitCorrections(next, "Import Workbench corrections");
                    }
                    else if (changeLoad.IsFaulted) error = changeLoad.Exception.GetBaseException().Message;
                }
                catch (Exception ex) { error = ex.Message; }
                changeLoad = null; changed = true;
                changeMemory?.Dispose(); changeMemory = null;
            }
            return changed;
        }
        private void DrawChangeFiles()
        {
            using (new EditorGUI.DisabledScope(changeLoad != null || changeSave != null))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Export changes…"))
                {
                    string path = EditorUtility.SaveFilePanel("Export corrections", "", workbookName, "fpchanges");
                    if (!string.IsNullOrEmpty(path)) try
                    { BeginChangeFile(path, false); }
                    catch (Exception ex) { error = ex.Message; }
                }
                if (GUILayout.Button("Import changes…"))
                {
                    string path = EditorUtility.OpenFilePanel("Import corrections", "", "fpchanges");
                    if (!string.IsNullOrEmpty(path)) try
                    { BeginChangeFile(path, true); }
                    catch (Exception ex) { error = ex.Message; }
                }
            }
            if (changeLoad != null || changeSave != null) if (GUILayout.Button("Cancel correction file operation")) changeCancel.Cancel();
        }
        private void BeginChangeFile(string path, bool importing)
        {
            if (changeLoad != null || changeSave != null) return;
            try {
                var data = OpenDatasets(); long bytes = 32L * 1024 * 1024;
                if (importing) foreach (var source in data) bytes = Math.Max(bytes, FPWorkbenchEditorResources.SourceBytes(source, 32L * 1024 * 1024));
                if (!FPWorkbenchEditorResources.TryAcquireScan(bytes, out changeMemory))
                    throw new InvalidOperationException("Shared correction-file memory/background capacity is busy; retry when current work finishes.");
                error = null;
                Cancel(changeCancel); changeCancel = new CancellationTokenSource();
                if (importing) {
                    importBase = JsonUtility.ToJson(new CorrectionPayload { Sources = corrections });
                    changeLoad = FPWorkbenchChangesFile.LoadAsync(path, data, changeCancel.Token);
                } else changeSave = FPWorkbenchChangesFile.SaveAsync(corrections, path, data, changeCancel.Token);
            } catch (Exception ex) { changeMemory?.Dispose(); changeMemory = null; error = ex.Message; }
        }
        private void CancelChangeFiles()
        {
            Cancel(changeCancel); changeCancel = null;
            if (changeMemory != null) changeMemory.ReleaseAfter((Task)changeLoad ?? changeSave);
            else { Observe(changeLoad); Observe(changeSave); }
            changeMemory = null; changeLoad = null; changeSave = null; importBase = null;
        }
        private void SelectCell(Sheet sheet, long row, int column, string original)
        {
            selectedDataset = sheet.Dataset.DatasetId; selectedRow = row; selectedColumn = column; selectedOriginal = original;
            editValue = original; editReason = ""; editKind = FPWorkbenchEditKind.Text;
            var existing = corrections.Find(c => c.DatasetId == selectedDataset)?.Cells.Find(c => c.Row == row && c.Column == column);
            if (existing != null) { editValue = existing.Value; editReason = existing.Reason; editKind = existing.Kind; }
        }
        private void DrawCorrectionControls(Sheet sheet)
        {
            bool original = EditorGUILayout.Toggle("Original values", sheet.ShowOriginal);
            if (original != sheet.ShowOriginal) { sheet.ShowOriginal = original; RefreshCorrectionViews(); }
            var source = corrections.Find(c => c.DatasetId == sheet.Dataset.DatasetId);
            EditorGUILayout.LabelField("Corrections", (source?.Cells.Count ?? 0).ToString());
            if (selectedDataset != sheet.Dataset.DatasetId) return;
            GUILayout.Label("Selected cell: row " + (selectedRow + 1) + ", column " + (selectedColumn + 1), EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Source", selectedOriginal.Length > 256 ? selectedOriginal.Substring(0, 256) + "…" : selectedOriginal);
            if (sheet.TableTypedSchema != null && sheet.TableTypedSchema.TryGetParser(selectedColumn, out var declared))
                EditorGUILayout.HelpBox("Declared " + declared.Type + ". Values must match its culture and tokens. Use Text input for custom numeric/boolean formats; Missing and Masked remain explicit quality actions.", MessageType.Info);
            editKind = (FPWorkbenchEditKind)EditorGUILayout.EnumPopup("Correction kind", editKind);
            using (new EditorGUI.DisabledScope(editKind == FPWorkbenchEditKind.Masked || editKind == FPWorkbenchEditKind.Missing)) editValue = EditorGUILayout.TextField("Effective value", editValue);
            editReason = EditorGUILayout.TextField("Reason", editReason);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Apply correction"))
                { try { ApplyCorrection(sheet, selectedRow, selectedColumn, selectedOriginal, editValue, editKind, editReason, false); error = null; } catch (Exception ex) { error = ex.Message; } }
                if (GUILayout.Button("Restore source"))
                { try { ApplyCorrection(sheet, selectedRow, selectedColumn, selectedOriginal, "", editKind, "", true); error = null; } catch (Exception ex) { error = ex.Message; } }
            }
        }
        private void OnDestroy()
        { ClearCalculationUndo(true); ClearPlotUndo(); if (editUndo != null) { Undo.ClearUndo(editUndo); DestroyImmediate(editUndo); editUndo = null; } }
    }
}
