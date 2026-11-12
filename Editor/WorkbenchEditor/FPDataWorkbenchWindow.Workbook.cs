// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using UnityEditor;
    using UnityEngine;

    public sealed partial class FPDataWorkbenchWindow
    {
        [SerializeField] private string workbookId = Guid.NewGuid().ToString("N"), workbookName = "Workbook", workbookPath = "";
        private Task<FPWorkbenchLoadedDocument> workbookLoad;
        private Func<Task<FPWorkbenchLoadedDocument>> pendingWorkbookLoad;
        private FPWorkbenchEditorResources.ScanReservation workbookLoadMemory;
        // Sources are verified sequentially. Reserve the supported maximum parser plus bounded metadata/definitions.
        private const long WorkbookLoadBytes = 168L * 1024 * 1024;
        private Task workbookSave;
        private FPWorkbenchEditorResources.ScanReservation workbookSaveMemory;
        private CancellationTokenSource workbookCancel;
        private string operationPath;
        private void PreserveWorkingMetadata()
        {
            try { CaptureOpenFigures(); } catch (Exception ex) { error = "Figure metadata needs correction before saving: " + ex.Message; }
            if (sizingSheet != null) SaveColumns(sizingSheet);
            foreach (var sheet in sheets)
            {
                if (sheet.Dataset != null) sheet.Import = sheet.Dataset.ImportProfile;
                if (sheet.FigureLease != null) { sheet.Binding = sheet.FigureLease.Session.RequestedBinding; sheet.PlotColumn = PlotColumnForBinding(sheet, sheet.Binding); }
            }
            // EditorWindow serializes these metadata fields during domain reload; readers/tasks stay transient.
        }
        private FPWorkbenchDocument CaptureWorkbook()
        {
            CaptureOpenFigures();
            var document = new FPWorkbenchDocument { Id = workbookId, Name = workbookName, ActiveSheet = active };
            document.Changes = corrections;
            document.Figures = savedFigures;
            foreach (var sheet in sheets)
            {
                if (sheet.Dataset == null || IsLoading(sheet)) throw new InvalidOperationException("Wait for all sheets to finish indexing/verifying before saving.");
                if (sheet.FigureLease != null && sheet.FigureLease.Session.IsBuilding) throw new InvalidOperationException("Finish the series build before saving its binding.");
                document.Sheets.Add(DescribeSheet(sheet));
                RememberSource(sheet.Dataset, sheet.Name);
            }
            document.Sources = registeredSources;
            document.Validate(); return document;
        }
        private static FPWorkbenchSheetDocument DescribeSheet(Sheet sheet)
        {
            var series = sheet.FigureLease?.Session.Series ?? sheet.Series;
            var binding = series?.Binding ?? sheet.Binding;
            return new FPWorkbenchSheetDocument
            {
                Id = sheet.Id, Name = sheet.Name, SourcePath = sheet.Dataset.SourcePath, CacheDirectory = sheet.Cache, DatasetId = sheet.Dataset.DatasetId,
                FirstRow = sheet.FirstRow, FirstColumn = sheet.FirstColumn, PlotColumn = series == null ? sheet.PlotColumn : PlotColumnForBinding(sheet, binding),
                Selection = sheet.Selection, Import = sheet.Dataset.ImportProfile, Binding = binding, Plot = sheet.Plot, Columns = sheet.Columns, ShowPlot = sheet.ShowPlot, ShowOriginal = sheet.ShowOriginal
            };
        }
        private void SaveWorkbook(string path)
        {
            if (workbookLoad != null || pendingWorkbookLoad != null || workbookSave != null) { error = "A workbook operation is already running."; return; }
            try
            {
                if (!FPWorkbenchEditorResources.TryAcquireScan(32L * 1024 * 1024, out workbookSaveMemory))
                    throw new InvalidOperationException("Shared workbook-save memory/background capacity is busy; retry when current work finishes.");
                var snapshot = CaptureWorkbook();
                Cancel(workbookCancel); workbookCancel = new CancellationTokenSource(); operationPath = path;
                workbookSave = FPWorkbenchDocumentFile.SaveAsync(snapshot, path, workbookCancel.Token);
                error = null; status = "Saving workbook metadata; original files are unchanged.";
            }
            catch (Exception ex) { workbookSaveMemory?.Dispose(); workbookSaveMemory = null; error = ex.Message; }
        }
        private void OpenWorkbook(string path)
        {
            if (workbookLoad != null || pendingWorkbookLoad != null || workbookSave != null) { error = "A workbook operation is already running."; return; }
            try
            {
                Cancel(workbookCancel); workbookCancel = new CancellationTokenSource(); operationPath = path;
                var token = workbookCancel.Token;
                pendingWorkbookLoad = () => FPWorkbenchDocumentFile.LoadAsync(path, token);
                error = null; status = "Verifying workbook sources in background; current workbook retained until admission succeeds.";
                TryStartWorkbookLoad();
            }
            catch (Exception ex) { error = ex.Message; }
        }
        private bool TickWorkbook()
        {
            bool changed = false;
            if (pendingWorkbookLoad != null) { TryStartWorkbookLoad(); changed = true; }
            if (workbookSave != null && workbookSave.IsCompleted)
            {
                if (workbookSave.Status == TaskStatus.RanToCompletion) { workbookPath = operationPath; status = "Workbook metadata and corrections saved; original files unchanged."; }
                else if (workbookSave.IsFaulted) error = workbookSave.Exception.GetBaseException().Message;
                else status = "Workbook save cancelled.";
                workbookSave = null; changed = true;
                workbookSaveMemory?.Dispose(); workbookSaveMemory = null;
            }
            if (workbookLoad != null && workbookLoad.IsCompleted)
            {
                workbookLoadMemory?.Dispose(); workbookLoadMemory = null;
                if (workbookLoad.Status == TaskStatus.RanToCompletion) { ApplyWorkbook(workbookLoad.Result); workbookPath = operationPath; }
                else if (workbookLoad.IsFaulted) { error = workbookLoad.Exception.GetBaseException().Message; status = "Workbook open failed; current workbook retained."; }
                else status = "Workbook open cancelled; current workbook retained.";
                workbookLoad = null; changed = true;
            }
            return changed;
        }
        private void TryStartWorkbookLoad()
        {
            try {
                if (!FPWorkbenchEditorResources.TryStartScan(WorkbookLoadBytes, ref pendingWorkbookLoad, ref workbookLoad, ref workbookLoadMemory))
                    status = "Workbook verification is waiting for shared memory/background capacity; current workbook retained.";
            } catch (Exception ex) { error = ex.Message; }
        }
        private void AbandonWorkbookLoad()
        {
            if (workbookLoadMemory != null) workbookLoadMemory.ReleaseAfter(workbookLoad); else Observe(workbookLoad);
            workbookLoadMemory = null; workbookLoad = null; pendingWorkbookLoad = null;
        }
        private void ApplyWorkbook(FPWorkbenchLoadedDocument loaded)
        {
            var candidate = new List<Sheet>();
            for (int i = 0; i < loaded.Document.Sheets.Count; i++)
            {
                var saved = loaded.Document.Sheets[i]; var data = loaded.Datasets[i];
                var sheet = new Sheet
                {
                    Id = saved.Id, Name = saved.Name, Source = data.SourcePath, Cache = data.CacheDirectory, Dataset = data, Import = data.ImportProfile,
                    FirstRow = saved.FirstRow, FirstColumn = saved.FirstColumn, PlotColumn = saved.PlotColumn, Binding = saved.Binding,
                    Selection = saved.Selection, RangeAnchorRow = saved.Selection?.FirstRow ?? 0,
                    RangeAnchorColumn = saved.Selection?.ColumnIds[0], Plot = saved.Plot, ShowPlot = saved.ShowPlot, ShowOriginal = saved.ShowOriginal, Columns = saved.Columns, Lifetime = new CancellationTokenSource(),
                    ColumnNames = new string[data.ColumnCount]
                };
                for (int c = 0; c < sheet.ColumnNames.Length; c++) sheet.ColumnNames[c] = (c + 1) + ": " + data.GetColumnName(c);
                candidate.Add(sheet);
            }
            CancelSelectionFrame();
            CancelClipboard();
            CancelChangeFiles();
            Cancel(pageCancel); pageCancel = null;
            CancelVirtualPage();
            AbandonRawPage();
            foreach (var sheet in sheets) { sheet.ProjectionUndo?.Dispose(); sheet.ProjectionUndo = null; sheet.PresentationUndo?.Dispose(); sheet.PresentationUndo = null; sheet.FigureLease?.Dispose(); sheet.FigureLease = null; CancelSheetLoad(sheet); }
            if (sizingSheet != null) { SaveColumns(sizingSheet); sizingSheet = null; sizingColumn = -1; GUIUtility.hotControl = 0; }
            pendingLinkedDataset = null; pendingLinkedColumns = null; linkedDataset = null; linkedColumns = Array.Empty<int>(); linkedRow = -1;
            CloseOwnedFigures();
            CancelSourceOpening(); admittedSources.Clear(); registeredSources = loaded.Document.Sources;
            foreach (var source in loaded.SourceDatasets) admittedSources[source.DatasetId] = source;
            sheets = candidate; active = loaded.Document.ActiveSheet;
            ClearPlotUndo();
            workbookId = loaded.Document.Id; workbookName = loaded.Document.Name;
            savedFigures = loaded.Document.Figures;
            ClearCalculationUndo(); calculationSource = null;
            corrections = loaded.Document.Changes; correctionSnapshots.Clear(); Undo.ClearUndo(editUndo); SyncCorrectionUndo();
            page = null; summary = null; requestedSeries = null; failedPage = null;
            status = "Workbook restored; sources verified. Plot caches will reopen as needed.";
        }
        private void DrawWorkbookControls()
        {
            workbookName = EditorGUILayout.TextField("Workbook", workbookName);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open workbook…"))
                { string path = EditorUtility.OpenFilePanel("Open workbook", "", "fpworkbook"); if (!string.IsNullOrEmpty(path)) OpenWorkbook(path); }
                if (GUILayout.Button("Save workbook…"))
                {
                    string path = EditorUtility.SaveFilePanel("Save workbook", string.IsNullOrEmpty(workbookPath) ? "" : System.IO.Path.GetDirectoryName(workbookPath), workbookName, "fpworkbook");
                    if (!string.IsNullOrEmpty(path)) SaveWorkbook(path);
                }
            }
            if (workbookLoad != null || pendingWorkbookLoad != null || workbookSave != null)
            { if (GUILayout.Button("Cancel workbook operation")) { workbookCancel.Cancel(); if (pendingWorkbookLoad != null) { pendingWorkbookLoad = null; status = "Workbook open cancelled; current workbook retained."; } } }
            if (GUILayout.Button("Recover moved/missing workbook sources…")) FPWorkbenchRecoveryWindow.Open();
            DrawSavedFigures();
            DrawRegisteredSources();
        }
    }
}
