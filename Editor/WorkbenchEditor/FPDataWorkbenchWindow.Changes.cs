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
            foreach (var set in restored.Sources) set.Validate();
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
            string payload = JsonUtility.ToJson(new CorrectionPayload { Sources = next });
            if (Encoding.UTF8.GetByteCount(payload) > 2 * 1024 * 1024) throw new InvalidOperationException("The first correction slice supports at most 2 MiB of combined correction metadata; indexed larger overlays remain planned.");
            SyncCorrectionUndo(); Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            Undo.RegisterCompleteObjectUndo(editUndo, label); corrections = next;
            SyncCorrectionUndo(); Undo.CollapseUndoOperations(group); Undo.IncrementCurrentGroup();
            correctionSnapshots.Clear(); RefreshCorrectionViews(); status = "Correction metadata updated; original source unchanged."; Repaint();
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
                    { Cancel(changeCancel); changeCancel = new CancellationTokenSource(); changeSave = FPWorkbenchChangesFile.SaveAsync(corrections, path, OpenDatasets(), changeCancel.Token); }
                    catch (Exception ex) { error = ex.Message; }
                }
                if (GUILayout.Button("Import changes…"))
                {
                    string path = EditorUtility.OpenFilePanel("Import corrections", "", "fpchanges");
                    if (!string.IsNullOrEmpty(path)) try
                    { Cancel(changeCancel); changeCancel = new CancellationTokenSource(); importBase = JsonUtility.ToJson(new CorrectionPayload { Sources = corrections }); changeLoad = FPWorkbenchChangesFile.LoadAsync(path, OpenDatasets(), changeCancel.Token); }
                    catch (Exception ex) { error = ex.Message; }
                }
            }
            if (changeLoad != null || changeSave != null) if (GUILayout.Button("Cancel correction file operation")) changeCancel.Cancel();
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
