// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using UnityEngine;
    using UnityEditor;

    public sealed partial class FPDataWorkbenchWindow
    {
        [SerializeField] private List<FPWorkbenchSourceDocument> registeredSources = new List<FPWorkbenchSourceDocument>();
        [SerializeField] private bool showRegisteredSources;
        private readonly Dictionary<string, FPWorkbenchCsvDataset> admittedSources = new Dictionary<string, FPWorkbenchCsvDataset>();
        private Task<FPWorkbenchCsvDataset> sourceOpening;
        private CancellationTokenSource sourceCancellation;
        private FPWorkbenchFigureDocument sourceFigure;
        private FPWorkbenchSourceDocument openingSource;
        private void RememberSource(FPWorkbenchCsvDataset data, string name)
        {
            if (data == null) return;
            registeredSources ??= new List<FPWorkbenchSourceDocument>();
            int index = registeredSources.FindIndex(s => s.DatasetId == data.DatasetId);
            if (index < 0 && registeredSources.Count >= 64) throw new InvalidOperationException("A workbook supports up to 64 registered sources.");
            var source = new FPWorkbenchSourceDocument { DatasetId = data.DatasetId, SourcePath = data.SourcePath, CacheDirectory = data.CacheDirectory, Name = name, Import = data.ImportProfile };
            if (index >= 0)
            { source.HasCalculations = registeredSources[index].HasCalculations; source.Calculations = registeredSources[index].Calculations; }
            if (index < 0) registeredSources.Add(source); else registeredSources[index] = source;
            admittedSources[data.DatasetId] = data;
        }
        private FPWorkbenchCsvDataset FindSource(string id)
        {
            var sheet = sheets.Find(s => s.Dataset?.DatasetId == id);
            if (sheet != null) { RememberSource(sheet.Dataset, sheet.Name); return sheet.Dataset; }
            return admittedSources.TryGetValue(id, out var data) ? data : null;
        }
        private void OpenFigureSource(FPWorkbenchFigureDocument figure, string datasetId = null)
        {
            var source = registeredSources.Find(s => s.DatasetId == (datasetId ?? figure.DatasetId));
            if (source == null) { error = "The figure's source is not registered in this workbook."; return; }
            CancelSourceOpening(); sourceCancellation = new CancellationTokenSource(); sourceFigure = figure; openingSource = source;
            sourceOpening = FPWorkbenchCsvDataset.OpenAsync(source.CacheDirectory, sourceCancellation.Token);
            status = "Verifying registered figure source in background…";
        }
        private bool TickSourceOpening()
        {
            if (sourceOpening == null || !sourceOpening.IsCompleted) return false;
            var task = sourceOpening; var figure = sourceFigure; var source = openingSource;
            sourceOpening = null; sourceFigure = null; openingSource = null;
            try
            {
                var data = task.GetAwaiter().GetResult();
                if (data.DatasetId != source.DatasetId || !string.Equals(Path.GetFullPath(data.SourcePath), Path.GetFullPath(source.SourcePath), StringComparison.OrdinalIgnoreCase) ||
                    data.ImportProfile.Delimiter != source.Import.Delimiter || data.ImportProfile.EncodingCodePage != source.Import.EncodingCodePage)
                    throw new InvalidDataException("Registered source identity/profile differs; current workbook retained.");
                RememberSource(data, source.Name);
                if (source.HasCalculations) FPWorkbenchFormulaProgram.Compile(data, source.Calculations);
                if (figure == null) AddRegisteredSheet(data, source.Name);
                else if (savedFigures.Exists(f => f.Id == figure.Id)) OpenSavedFigure(figure);
                status = "Registered figure source verified.";
            }
            catch (OperationCanceledException) { status = "Source verification cancelled."; }
            catch (Exception ex) { error = ex.Message; }
            return true;
        }
        private void CancelSourceOpening()
        {
            Cancel(sourceCancellation); sourceCancellation = null; Observe(sourceOpening); sourceOpening = null; sourceFigure = null; openingSource = null;
        }
        private void OpenRegisteredSheet(FPWorkbenchSourceDocument source)
        {
            int existing = sheets.FindIndex(s => s.Dataset?.DatasetId == source.DatasetId);
            if (existing >= 0) { active = existing; Repaint(); return; }
            if (sheets.Count >= 8) { error = "Close a sheet before reopening another source (eight open tabs maximum)."; return; }
            var data = FindSource(source.DatasetId);
            if (data != null) { AddRegisteredSheet(data, source.Name); return; }
            CancelSourceOpening(); sourceCancellation = new CancellationTokenSource(); openingSource = source;
            sourceOpening = FPWorkbenchCsvDataset.OpenAsync(source.CacheDirectory, sourceCancellation.Token);
            status = "Verifying registered table source in background…";
        }
        private void AddRegisteredSheet(FPWorkbenchCsvDataset data, string name)
        {
            if (sheets.Count >= 8) { error = "Eight open sheet tabs maximum; verified source retained."; return; }
            var sheet = new Sheet { Dataset = data, Name = name, Source = data.SourcePath, Cache = data.CacheDirectory,
                Lifetime = new CancellationTokenSource(), ColumnNames = new string[data.ColumnCount] };
            RestoreColumns(sheet);
            for (int c = 0; c < data.ColumnCount; c++) sheet.ColumnNames[c] = (c + 1) + ": " + data.GetColumnName(c);
            sheets.Add(sheet); active = sheets.Count - 1; page = null; summary = null; failedPage = null; error = null;
            status = "Registered source reopened without reimporting its CSV."; Repaint();
        }
        private void DrawRegisteredSources()
        {
            registeredSources ??= new List<FPWorkbenchSourceDocument>();
            showRegisteredSources = EditorGUILayout.Foldout(showRegisteredSources, "Registered sources • " + registeredSources.Count, true);
            using (new EditorGUI.DisabledScope(registeredSources.Count == 0))
                if (GUILayout.Button("Open cross-sheet alignment…"))
                {
                    try { FPWorkbenchAlignmentWindow.Open(workbookId, registeredSources.ToArray(), corrections.ToArray()); }
                    catch (Exception ex) { error = ex.Message; }
                }
            if (!showRegisteredSources) return;
            foreach (var source in registeredSources)
                if (GUILayout.Button(new GUIContent(source.Name, source.SourcePath))) OpenRegisteredSheet(source);
        }
    }
}
