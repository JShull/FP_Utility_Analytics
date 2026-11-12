// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using UnityEditor;
    using UnityEngine;

    public sealed partial class FPWorkbenchFigureWindow : EditorWindow
    {
        private FPWorkbenchPresentationUndo presentationUndo;
        [SerializeField] private bool showFigureSettings;
        [SerializeField] private string figureId, metadata, corrections;
        [SerializeField] private string calculations;
        private FPWorkbenchSheetDocument document;
        private FPWorkbenchFigureSession.Lease lease;
        private Task<FPWorkbenchCsvDataset> opening;
        private bool pendingOpening;
        private FPWorkbenchEditorResources.ScanReservation openingMemory;
        private CancellationTokenSource cancellation;
        private string error;
        private long lastRevision = -1;
        private bool windowActive;
        public static FPWorkbenchFigureWindow Open(string id, FPWorkbenchSheetDocument document, FPWorkbenchCsvDataset dataset, FPWorkbenchFormulaProgram program = null)
        {
            var window = CreateInstance<FPWorkbenchFigureWindow>(); window.figureId = id;
            window.document = document; window.metadata = JsonUtility.ToJson(document);
            window.calculations = program == null ? null : JsonUtility.ToJson(program.Definition);
            window.lease = FPWorkbenchFigureRegistry.Retain(id, dataset, document.Binding, document.Plot, calculations: program);
            window.titleContent = new GUIContent(document.Name + " — Figure"); window.Show(); return window;
        }
        private void OnEnable()
        {
            windowActive = true; FPWorkbenchEditorCache.Register(OnDisable);
            minSize = new Vector2(420, 260);
            EditorApplication.update += Tick; AssemblyReloadEvents.beforeAssemblyReload += Capture;
            if (string.IsNullOrEmpty(figureId) || string.IsNullOrEmpty(metadata)) return;
            try
            {
                document = JsonUtility.FromJson<FPWorkbenchSheetDocument>(metadata);
                lease = FPWorkbenchFigureRegistry.TryRetain(figureId);
                if (lease == null)
                { cancellation = new CancellationTokenSource(); pendingOpening = true; }
            }
            catch (Exception ex) { error = ex.Message; }
        }
        private void OnDisable()
        {
            if (!windowActive) return; windowActive = false; FPWorkbenchEditorCache.Unregister(OnDisable);
            imageJob?.Dispose(); imageJob = null;
            presentationUndo?.Dispose(); presentationUndo = null;
            Capture(); EditorApplication.update -= Tick; AssemblyReloadEvents.beforeAssemblyReload -= Capture;
            cancellation?.Cancel(); cancellation?.Dispose(); cancellation = null;
            if (openingMemory != null) openingMemory.ReleaseAfter(opening);
            else if (opening != null) opening.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            openingMemory = null; pendingOpening = false; FPWorkbenchEditorCache.Abandon(opening); opening = null;
            lease?.Dispose(); lease = null;
        }
        private void Capture()
        {
            if (document == null) return;
            if (lease != null && !lease.Session.IsDisposed)
            { document.Plot = lease.Session.Settings; document.Binding = lease.Session.RequestedBinding; calculations = lease.Session.CalculationProgram == null ? null : JsonUtility.ToJson(lease.Session.CalculationProgram.Definition); }
            if (lease != null) corrections = lease.Session.ChangeSnapshot?.Json;
            metadata = JsonUtility.ToJson(document);
        }
        private void Tick()
        {
            if (!windowActive) return;
            if (pendingOpening) {
                try { pendingOpening = !FPWorkbenchEditorResources.TryOpenCache(document.CacheDirectory, document.Import, cancellation.Token, ref opening, ref openingMemory); }
                catch (Exception ex) { pendingOpening = false; error = ex.Message; }
                Repaint();
            }
            if (opening != null && opening.IsCompleted)
            {
                openingMemory?.Dispose(); openingMemory = null;
                if (opening.Status == TaskStatus.RanToCompletion)
                {
                    if (opening.Result.DatasetId == document.DatasetId)
                    {
                        try
                        {
                            var program = string.IsNullOrEmpty(calculations) ? null : FPWorkbenchFormulaProgram.Compile(opening.Result, JsonUtility.FromJson<FPWorkbenchFormulaSet>(calculations));
                            lease = FPWorkbenchFigureRegistry.Retain(figureId, opening.Result, document.Binding, document.Plot,
                                string.IsNullOrEmpty(corrections) ? null : JsonUtility.FromJson<FPWorkbenchChangeSet>(corrections).Snapshot(), program);
                        }
                        catch (Exception ex) { error = ex.Message; }
                    }
                    else error = "Figure source revision changed; reopen/relink the workbook.";
                }
                else if (opening.IsFaulted) error = opening.Exception.GetBaseException().Message;
                FPWorkbenchEditorCache.Abandon(opening); opening = null; Repaint();
            }
            if (lease == null) return;
            var session = lease.Session; bool changed = session.Poll();
            if (session.Revision != lastRevision) { lastRevision = session.Revision; changed = true; }
            if (changed || session.IsBusy) Repaint();
        }
        private void OnGUI()
        {
            if (!string.IsNullOrEmpty(error)) { EditorGUILayout.HelpBox(error, MessageType.Error); return; }
            if (lease == null) { EditorGUILayout.HelpBox(pendingOpening ? "Waiting for shared memory/background capacity…" : "Verifying figure source…", MessageType.Info); return; }
            var session = lease.Session;
            var rows = session.RequestedBinding.Rows;
            if (rows.Enabled) GUILayout.Label("Inherited sheet rows: " + (rows.FirstRow + 1).ToString("N0") + " - " + rows.EndRow.ToString("N0") + " (table row numbers)", EditorStyles.miniLabel);
            presentationUndo ??= new FPWorkbenchPresentationUndo(Repaint);
            presentationUndo.Begin(session.Settings);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Fit X", EditorStyles.toolbarButton) && session.Series != null) session.Settings.Fit(session.Series.FirstX, session.Series.LastX);
                session.Settings.InvertY = GUILayout.Toggle(session.Settings.InvertY, "Invert Y", EditorStyles.toolbarButton);
                FPWorkbenchPlotView.StyleControls(session.Settings, true);
                session.Settings.Grid = GUILayout.Toggle(session.Settings.Grid, "Grid", EditorStyles.toolbarButton);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Close figure view", EditorStyles.toolbarButton)) { Close(); return; }
            }
            showFigureSettings = EditorGUILayout.Foldout(showFigureSettings, "Figure settings", true);
            if (showFigureSettings)
            {
                FPWorkbenchPlotView.LabelControls(session.Settings);
                FPWorkbenchPlotView.StyleControls(session.Settings);
                session.Settings.XTickSpacing = EditorGUILayout.DoubleField("X grid spacing", session.Settings.XTickSpacing);
                session.Settings.YTickSpacing = EditorGUILayout.DoubleField("Y grid spacing", session.Settings.YTickSpacing);
            }
            presentationUndo.End();
            DrawImageExport(session);
            if (session.Error != null) EditorGUILayout.HelpBox(session.Error.Message, MessageType.Error);
            Rect plot = GUILayoutUtility.GetRect(100, 10000, 100, 10000, GUILayout.ExpandHeight(true));
            FPWorkbenchPlotView.Draw(plot, session.View, session.Settings, (session.ChangeSnapshot == null ? "Original • " : "Effective • ") + session.YName);
            if (session.IsBusy) GUI.Label(new Rect(plot.x + 80, plot.y + 20, plot.width - 90, 20), session.WaitingForMemory ? "Waiting for shared memory/background capacity…" : "Updating cached view…", EditorStyles.miniLabel);
        }
    }
}
