// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using UnityEditor;
    using UnityEngine;

    public sealed class FPWorkbenchRecoveryWindow : EditorWindow
    {
        [SerializeField] private string metadata, originalPath;
        [SerializeField] private int selection;
        private FPWorkbenchDocument candidate;
        private Task<FPWorkbenchDocument> reading, relinking;
        private Task<bool> saving;
        private Func<Task<FPWorkbenchDocument>> pendingReading, pendingRelinking;
        private Func<Task<bool>> pendingSaving;
        private FPWorkbenchEditorResources.ScanReservation operationMemory;
        private long relinkBytes;
        private bool windowActive;
        private CancellationTokenSource cancellation;
        private string error, status, savePath, readPath;
        private bool Busy => reading != null || relinking != null || saving != null || pendingReading != null || pendingRelinking != null || pendingSaving != null;
        public static FPWorkbenchRecoveryWindow Open()
        {
            var window = GetWindow<FPWorkbenchRecoveryWindow>(); window.titleContent = new GUIContent("Workbook Recovery"); window.Show(); return window;
        }
        private void OnEnable()
        {
            windowActive = true;
            minSize = new Vector2(520, 260); EditorApplication.update += Tick;
            if (!string.IsNullOrEmpty(metadata)) try { candidate = JsonUtility.FromJson<FPWorkbenchDocument>(metadata); candidate.Validate(); }
                catch (Exception ex) { candidate = null; error = ex.Message; }
        }
        private void OnDisable()
        {
            windowActive = false;
            EditorApplication.update -= Tick; cancellation?.Cancel(); cancellation?.Dispose(); cancellation = null;
            if (operationMemory != null) operationMemory.ReleaseAfter((Task)reading ?? relinking ?? (Task)saving);
            else { Observe(reading); Observe(relinking); Observe(saving); }
            operationMemory = null; reading = null; relinking = null; saving = null;
            pendingReading = null; pendingRelinking = null; pendingSaving = null;
        }
        private static void Observe(Task task) { task?.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted); }
        private void StartOperation() { cancellation?.Dispose(); cancellation = new CancellationTokenSource(); error = null; }
        private void BeginRead(string path)
        {
            if (Busy) return; StartOperation(); readPath = Path.GetFullPath(path);
            var token = cancellation.Token;
            pendingReading = () => FPWorkbenchDocumentFile.ReadMetadataAsync(path, token); status = "Reading metadata only; sources are not yet verified."; TryStartPending();
        }
        private void BeginRelink(string path)
        {
            if (Busy || candidate == null || candidate.Sources.Count == 0) return; StartOperation();
            string parent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FuzzPhyte", "Analytics", "Workbench");
            var document = candidate; var source = candidate.Sources[selection]; var token = cancellation.Token;
            relinkBytes = FPWorkbenchEditorResources.ProfileBytes(source.Import, 32L * 1024 * 1024);
            pendingRelinking = () => FPWorkbenchSourceRecovery.RelinkAsync(document, source.DatasetId, path, parent, token);
            status = "Verifying replacement content and building a fresh index…";
            TryStartPending();
        }
        private void BeginSave(string path)
        {
            if (Busy || candidate == null) return;
            if (string.Equals(Path.GetFullPath(path), originalPath, Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) {
                error = "Choose a new workbook copy; the original recovery input is preserved."; return;
            }
            StartOperation(); savePath = path; var document = candidate; var token = cancellation.Token;
            async Task<bool> Save() { await FPWorkbenchDocumentFile.SaveAsync(document, path, token).ConfigureAwait(false); return true; }
            pendingSaving = Save;
            status = "Saving recovered metadata copy…";
            TryStartPending();
        }
        private void Tick()
        {
            if (!windowActive) return;
            if (pendingReading != null || pendingRelinking != null || pendingSaving != null) { TryStartPending(); Repaint(); }
            if (reading?.IsCompleted == true) {
                var task = reading; reading = null;
                operationMemory?.Dispose(); operationMemory = null;
                try { candidate = task.GetAwaiter().GetResult(); originalPath = readPath; metadata = JsonUtility.ToJson(candidate); selection = 0; status = "Metadata loaded. Choose each moved/missing source to relink; unchanged sources retain their references."; }
                catch (Exception ex) { error = ex.Message; }
                Repaint();
            }
            if (relinking?.IsCompleted == true) {
                var task = relinking; relinking = null;
                operationMemory?.Dispose(); operationMemory = null;
                try { candidate = task.GetAwaiter().GetResult(); metadata = JsonUtility.ToJson(candidate); status = "Replacement matches the original recording. Save a workbook copy, then open it normally to verify all sources."; }
                catch (Exception ex) { error = ex.Message; }
                Repaint();
            }
            if (saving?.IsCompleted == true) {
                var task = saving; saving = null;
                operationMemory?.Dispose(); operationMemory = null;
                try { task.GetAwaiter().GetResult(); status = "Recovered copy saved: " + savePath + ". Open it with Open workbook to verify the complete document."; }
                catch (Exception ex) { error = ex.Message; }
                Repaint();
            }
        }
        private void TryStartPending()
        {
            try {
                bool admitted = FPWorkbenchEditorResources.TryStartScan(32L * 1024 * 1024, ref pendingReading, ref reading, ref operationMemory);
                if (!FPWorkbenchEditorResources.TryStartScan(relinkBytes, ref pendingRelinking, ref relinking, ref operationMemory)) admitted = false;
                if (!FPWorkbenchEditorResources.TryStartScan(32L * 1024 * 1024, ref pendingSaving, ref saving, ref operationMemory)) admitted = false;
                if (!admitted) status = "Waiting for shared memory/background capacity; current recovery document retained.";
            } catch (Exception ex) { error = ex.Message; }
        }
        private void OnGUI()
        {
            using (new EditorGUI.DisabledScope(Busy)) {
                if (GUILayout.Button("Choose workbook to recover…")) {
                    string path = EditorUtility.OpenFilePanel("Workbook recovery", "", "fpworkbook"); if (!string.IsNullOrEmpty(path)) BeginRead(path);
                }
                if (candidate != null && candidate.Sources.Count > 0) {
                    GUILayout.Label(candidate.Name, EditorStyles.boldLabel);
                    string[] names = candidate.Sources.ConvertAll(s => s.Name).ToArray(); selection = EditorGUILayout.Popup("Original source", Mathf.Clamp(selection, 0, names.Length - 1), names);
                    EditorGUILayout.SelectableLabel(candidate.Sources[selection].SourcePath, EditorStyles.wordWrappedMiniLabel, GUILayout.Height(40));
                    if (GUILayout.Button("Choose matching CSV / rebuild its index…")) {
                        string path = EditorUtility.OpenFilePanel("Locate the identical original CSV", "", "csv"); if (!string.IsNullOrEmpty(path)) BeginRelink(path);
                    }
                    if (GUILayout.Button("Save recovered workbook copy…")) {
                        string path = EditorUtility.SaveFilePanel("Save recovered workbook", "", candidate.Name + "-recovered", "fpworkbook"); if (!string.IsNullOrEmpty(path)) BeginSave(path);
                    }
                }
            }
            if (Busy && GUILayout.Button("Cancel recovery operation")) { cancellation.Cancel(); pendingReading = null; pendingRelinking = null; pendingSaving = null; status = "Recovery operation cancelled; current document retained."; }
            if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, MessageType.Info);
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Warning);
            EditorGUILayout.HelpBox("Recovery changes detached references only. Changed data is rejected; your open session and original workbook remain intact. Complete source/figure admission happens when the saved copy is opened normally.", MessageType.None);
        }
    }
}
