// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using UnityEditor;
    using UnityEngine;

    /// <summary>Snapshot histogram viewer. Serialized metadata rebuilds bounded products after reload.</summary>
    public sealed partial class FPWorkbenchHistogramWindow : EditorWindow
    {
        [SerializeField] private string cache, datasetId, corrections, columnName;
        [SerializeField] private string workbook, figureId = Guid.NewGuid().ToString("N");
        [SerializeField] private string calculations, derivedId;
        internal string WorkbookId => workbook;
        internal string FigureId => figureId;
        [SerializeField] private int column, bins = 32;
        [SerializeField] private long firstRow, endRow;
        [SerializeField] private double minimum, maximum = 1;
        [SerializeField] private bool density, grid = true, fitOnOpen = true;
        [SerializeField] private FPWorkbenchInvalidPolicy invalidPolicy = FPWorkbenchInvalidPolicy.Skip;
        private FPWorkbenchCsvDataset dataset;
        private IDisposable diskLease;
        private FPWorkbenchFormulaProgram program;
        private FPWorkbenchChangeSnapshot snapshot;
        private Task<FPWorkbenchCsvDataset> opening;
        [SerializeField] private FPWorkbenchCsvProfile sourceProfile;
        private bool pendingOpening;
        private FPWorkbenchEditorResources.ScanReservation openingMemory;
        private Task<FPWorkbenchReductionResult> fitting;
        private Task<FPWorkbenchHistogramResult> building;
        private FPWorkbenchEditorResources.ScanReservation operationMemory, resultMemory;
        private bool pendingFit, pendingBuild, waitingForMemory, windowActive;
        private FPWorkbenchHistogramResult result;
        private CancellationTokenSource cancellation;
        private string error;
        private GUIContent equationLabel;
        [SerializeField] private string sourceSheetId = "", sourceSheetName = "";
        [SerializeField] private Vector2 figureSettingsScroll;
        [SerializeField] private FPWorkbenchFigurePoints pointsOfInterest = new FPWorkbenchFigurePoints();
        private bool pinnedReferencesReady;
        [SerializeField] private FPWorkbenchRowRange rowRange = new FPWorkbenchRowRange();
        private bool Busy => opening != null || pendingOpening || fitting != null || building != null || pendingFit || pendingBuild;
        public static FPWorkbenchHistogramWindow Open(FPWorkbenchCsvDataset source, int sourceColumn, FPWorkbenchChangeSnapshot changes = null, string workbookId = null, FPWorkbenchFormulaProgram calculationProgram = null, string derivedColumnId = null, FPWorkbenchRowRange rows = null, string sheetId = "")
        {
            bool derived = !string.IsNullOrEmpty(derivedColumnId);
            if (source == null || !derived && (sourceColumn < 0 || sourceColumn >= source.ColumnCount) || changes != null && changes.DatasetId != source.DatasetId ||
                derived && calculationProgram == null || calculationProgram != null && calculationProgram.DatasetId != source.DatasetId) throw new ArgumentException("Histogram source/column differs.");
            int selected = derived ? new FPWorkbenchSeriesBinding { YDerivedId = derivedColumnId }.ResolveDerivedY(calculationProgram) : sourceColumn;
            var window = CreateInstance<FPWorkbenchHistogramWindow>(); window.cache = source.CacheDirectory; window.datasetId = source.DatasetId;
            window.column = selected; window.columnName = derived ? calculationProgram.Name(selected) : source.GetColumnName(selected); window.endRow = source.RowCount;
            window.derivedId = derivedColumnId; window.calculations = calculationProgram == null ? null : JsonUtility.ToJson(calculationProgram.Definition);
            window.workbook = workbookId; window.sourceSheetId = sheetId; window.sourceSheetName = FPDataWorkbenchWindow.FigureSheetName(window.WorkbookId, sheetId, ""); window.rowRange = rows?.Copy() ?? new FPWorkbenchRowRange();
            window.firstRow = window.rowRange.First(source.RowCount); window.endRow = window.rowRange.End(source.RowCount);
            window.corrections = changes?.Json; window.Attach(source); window.BeginFit();
            FPDataWorkbenchWindow.RememberFigure(workbookId, window.DescribeFigure());
            window.titleContent = new GUIContent(window.columnName + " — Histogram"); window.Show(); return window;
        }
        internal void ApplySheetRows(FPWorkbenchFigureDocument figure)
        {
            rowRange = figure.Rows.Copy(); sourceSheetId = figure.SourceSheetId; sourceSheetName = figure.SourceSheetName;
            if (dataset != null) { cancellation.Cancel(); ReleaseHistogramOperation(); }
            resultMemory?.Dispose(); resultMemory = null; result = null; publishedSettings = null; DisposeHistogramUndo();
            firstRow = figure.FirstRow; endRow = figure.EndRow; fitOnOpen = true; error = null;
            if (dataset != null) BeginFit(); Repaint();
        }
        private void OnEnable()
        {
            windowActive = true; FPWorkbenchEditorCache.Register(OnDisable);
            minSize = new Vector2(920, 520); wantsMouseMove = true; cancellation = new CancellationTokenSource(); EditorApplication.update += Tick;
            try { RestorePublishedQuery(); } catch (Exception ex) { error = ex.Message; return; }
            pendingOpening = !string.IsNullOrEmpty(cache);
        }
        private void OnDisable()
        {
            if (!windowActive) return; windowActive = false; FPWorkbenchEditorCache.Unregister(OnDisable); diskLease?.Dispose(); diskLease = null;
            pointsOfInterest.Dispose();
            imageJob?.Dispose(); imageJob = null;
            DisposeHistogramUndo();
            try { if (!string.IsNullOrEmpty(datasetId)) FPDataWorkbenchWindow.RememberFigure(workbook, DescribeFigure()); }
            catch (Exception ex) { error = ex.Message; }
            EditorApplication.update -= Tick; cancellation?.Cancel(); cancellation?.Dispose(); cancellation = null;
            if (openingMemory != null) openingMemory.ReleaseAfter(opening); else Observe(opening);
            openingMemory = null; pendingOpening = false; ReleaseHistogramOperation();
            resultMemory?.Dispose(); resultMemory = null; FPWorkbenchEditorCache.Abandon(opening); opening = null;
            dataset = null; program = null; snapshot = null; result = null;
        }
        internal FPWorkbenchFigureDocument DescribeFigure()
        {
            sourceSheetName = FPDataWorkbenchWindow.FigureSheetName(workbook, sourceSheetId, sourceSheetName);
            var shown = PublishedHistogramSettings() ?? CaptureHistogramSettings();
            return new FPWorkbenchFigureDocument
        {
            Id = figureId, Name = columnName + " histogram", DatasetId = datasetId, Kind = FPWorkbenchFigureKind.Histogram, SourceSheetId = sourceSheetId, SourceSheetName = sourceSheetName, PointsOfInterest = pointsOfInterest.Snapshot(), Rows = rowRange.Copy(),
            HistogramColumn = string.IsNullOrEmpty(derivedId) ? column : 0, HistogramDerivedId = derivedId, HistogramBins = shown.Bins, FirstRow = shown.FirstRow, EndRow = shown.EndRow, Minimum = shown.Minimum, Maximum = shown.Maximum,
            Density = density, Grid = grid, FitHistogramOnOpen = shown.FitOnOpen, InvalidPolicy = shown.InvalidPolicy,
            HasCorrectionSnapshot = !string.IsNullOrEmpty(corrections),
            HasCalculationSnapshot = !string.IsNullOrEmpty(calculations),
            CalculationSnapshot = string.IsNullOrEmpty(calculations) ? null : JsonUtility.FromJson<FPWorkbenchFormulaSet>(calculations),
            CorrectionSnapshot = string.IsNullOrEmpty(corrections) ? null : JsonUtility.FromJson<FPWorkbenchChangeSet>(corrections)
        };
        }
        internal static FPWorkbenchHistogramWindow OpenSaved(string workbookId, FPWorkbenchCsvDataset source, FPWorkbenchFigureDocument saved)
        {
            saved.Validate(); var window = CreateInstance<FPWorkbenchHistogramWindow>();
            window.workbook = workbookId; window.figureId = saved.Id; window.cache = source.CacheDirectory; window.datasetId = saved.DatasetId;
            window.rowRange = saved.Rows.Copy(); window.sourceSheetId = saved.SourceSheetId; window.sourceSheetName = saved.SourceSheetName; window.pointsOfInterest.Restore(saved.PointsOfInterest);
            window.column = saved.HistogramColumn; window.bins = saved.HistogramBins; window.derivedId = saved.HistogramDerivedId;
            window.calculations = saved.HasCalculationSnapshot ? JsonUtility.ToJson(saved.CalculationSnapshot) : null;
            window.firstRow = saved.FirstRow; window.endRow = saved.EndRow; window.minimum = saved.Minimum; window.maximum = saved.Maximum;
            window.density = saved.Density; window.grid = saved.Grid; window.fitOnOpen = saved.FitHistogramOnOpen; window.invalidPolicy = saved.InvalidPolicy;
            window.corrections = saved.CorrectionSnapshot == null ? null : JsonUtility.ToJson(saved.CorrectionSnapshot); window.Attach(source);
            window.columnName = string.IsNullOrEmpty(window.derivedId) ? source.GetColumnName(window.column) : window.program.Name(window.column);
            if (window.fitOnOpen) window.BeginFit(); else window.BeginBuild(); window.titleContent = new GUIContent(saved.Name); window.Show(); return window;
        }
        private static void Observe(Task task) { if (task != null) task.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted); }
        private void Attach(FPWorkbenchCsvDataset source)
        {
            if (source.DatasetId != datasetId) throw new InvalidOperationException("Histogram source revision changed.");
            sourceProfile = source.ImportProfile;
            var pin = FPWorkbenchEditorCache.Retain(source); diskLease?.Dispose(); diskLease = pin; dataset = source; program = FPWorkbenchFormulaProgram.Compile(source, string.IsNullOrEmpty(calculations) ? new FPWorkbenchFormulaSet { DatasetId = datasetId } : JsonUtility.FromJson<FPWorkbenchFormulaSet>(calculations));
            if (!string.IsNullOrEmpty(derivedId)) column = new FPWorkbenchSeriesBinding { YDerivedId = derivedId }.ResolveDerivedY(program);
            else if (column < 0 || column >= source.ColumnCount) throw new InvalidOperationException("Histogram source column changed.");
            equationLabel = string.IsNullOrEmpty(derivedId) ? null : new GUIContent("Formula snapshot: " + program.Expression(column), program.Expression(column));
            snapshot = string.IsNullOrEmpty(corrections) ? null : JsonUtility.FromJson<FPWorkbenchChangeSet>(corrections).Snapshot();
        }
        private void Prepare()
        {
            cancellation?.Dispose(); cancellation = new CancellationTokenSource(); error = null;
        }
        private void BeginFit()
        {
            if (Busy || dataset == null) return;
            try { Prepare(); recordHistogramFit = result != null; pendingFit = true; TryStartScan(); }
            catch (Exception ex) { error = ex.Message; }
        }
        private void BeginBuild()
        {
            if (Busy || dataset == null) return;
            try { Prepare(); buildingHistogramPolicy = invalidPolicy; pendingBuild = true; TryStartScan(); }
            catch (Exception ex) { error = ex.Message; }
        }
        private void Tick()
        {
            if (!windowActive) return;
            bool changed = false;
            if (pendingOpening) {
                try { pendingOpening = !FPWorkbenchEditorResources.TryOpenCache(cache, sourceProfile, cancellation.Token, ref opening, ref openingMemory); waitingForMemory = pendingOpening; }
                catch (Exception ex) { pendingOpening = false; waitingForMemory = false; error = ex.Message; }
                changed = true;
            }
            if (opening != null && opening.IsCompleted)
            {
                var task = opening; opening = null;
                openingMemory?.Dispose(); openingMemory = null;
                try { Attach(task.GetAwaiter().GetResult()); if (fitOnOpen) BeginFit(); else BeginBuild(); }
                catch (Exception ex) { error = ex.Message; } finally { FPWorkbenchEditorCache.Abandon(task); } changed = true;
            }
            if (fitting != null && fitting.IsCompleted)
            {
                var task = fitting; fitting = null;
                operationMemory?.Dispose(); operationMemory = null;
                try
                {
                    var extent = task.GetAwaiter().GetResult();
                    if (extent.Count == 0) throw new InvalidOperationException("No valid numeric samples in this range. Choose explicit limits or another column.");
                    string beforeFit = HistogramSettingsJson();
                    minimum = extent.Minimum; maximum = extent.Maximum;
                    if (maximum == minimum) { double pad = Math.Max(1, Math.Abs(minimum) * 1e-6); minimum -= pad; maximum += pad; }
                    fitOnOpen = false; if (recordHistogramFit) { Undo.IncrementCurrentGroup(); RecordHistogramSettings(beforeFit); } BeginBuild();
                }
                catch (Exception ex) { error = ex.Message; } changed = true;
            }
            if (building != null && building.IsCompleted)
            {
                var task = building; building = null;
                try {
                    var candidate = task.GetAwaiter().GetResult();
                    resultMemory?.Dispose(); resultMemory = operationMemory; operationMemory = null;
                    resultMemory.Complete(checked(4096L + candidate.Counts.Count * 32L + candidate.Edges.Count * 16L));
                    result = candidate; fitOnOpen = false; PublishHistogramSettings();
                }
                catch (Exception ex) { error = ex.Message; }
                operationMemory?.Dispose(); operationMemory = null; changed = true;
            }
            if (pendingFit || pendingBuild) changed |= TryStartScan();
            if (changed) Repaint();
        }
        private bool TryStartScan()
        {
            if ((!pendingFit && !pendingBuild) || fitting != null || building != null) return false;
            long bytes = FPWorkbenchEditorResources.SourceBytes(dataset, 1024L * 1024);
            bool admitted = FPWorkbenchEditorResources.TryAcquireScan(bytes, out operationMemory);
            bool changed = waitingForMemory != !admitted; waitingForMemory = !admitted;
            if (!admitted) return changed;
            try {
                bool fit = pendingFit; pendingFit = pendingBuild = false;
                if (fit) fitting = FPWorkbenchReduction.CalculateAsync(dataset, program, column, !string.IsNullOrEmpty(derivedId), firstRow, endRow, invalidPolicy, snapshot, cancellation: cancellation.Token);
                else building = FPWorkbenchHistogram.CalculateAsync(dataset, program, column, !string.IsNullOrEmpty(derivedId), firstRow, endRow, minimum, maximum, bins, invalidPolicy, snapshot, cancellation: cancellation.Token);
            }
            catch (Exception ex) { operationMemory.Dispose(); operationMemory = null; pendingFit = pendingBuild = false; error = ex.Message; }
            return true;
        }
        private void CancelHistogram()
        {
            cancellation?.Cancel();
            if (openingMemory != null) openingMemory.ReleaseAfter(opening); else Observe(opening);
            openingMemory = null; FPWorkbenchEditorCache.Abandon(opening); opening = null; pendingOpening = false;
            ReleaseHistogramOperation(); Repaint();
        }
        private void ReleaseHistogramOperation()
        {
            if (operationMemory != null) operationMemory.ReleaseAfter((Task)fitting ?? building);
            else { Observe(fitting); Observe(building); }
            operationMemory = null; fitting = null; building = null; pendingFit = pendingBuild = waitingForMemory = false;
        }
        private void OnGUI()
        {
            sourceSheetName = FPDataWorkbenchWindow.FigureSheetName(workbook, sourceSheetId, sourceSheetName);
            GUILayout.Label(string.IsNullOrEmpty(sourceSheetName) ? "Workbook-level figure" : "Sheet: " + sourceSheetName, EditorStyles.boldLabel);
            if (dataset != null) FPDataWorkbenchWindow.DrawFigureRowScope(sourceSheetId, rowRange, dataset.RowCount);
            using (new EditorGUILayout.HorizontalScope()) {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(360))) {
                    figureSettingsScroll = EditorGUILayout.BeginScrollView(figureSettingsScroll);
                    try { DrawFigureSettings(); DrawPointsOfInterest(); } finally { EditorGUILayout.EndScrollView(); }
                }
                using (new EditorGUILayout.VerticalScope()) DrawFigureGraph();
            }
        }
        private void DrawFigureSettings()
        {

            string beforeSettings = HistogramSettingsJson();
            GUILayout.Label(columnName + (string.IsNullOrEmpty(derivedId) ? "" : " • Calculated") + (string.IsNullOrEmpty(corrections) ? " • Original snapshot" : " • Effective snapshot"), EditorStyles.boldLabel);
            if (equationLabel != null) GUILayout.Label(equationLabel, EditorStyles.miniLabel);
            using (new EditorGUI.DisabledScope(Busy || dataset == null))
            {
                using (new EditorGUI.DisabledScope(!string.IsNullOrEmpty(sourceSheetId)))
                using (new EditorGUILayout.VerticalScope())
                { firstRow = EditorGUILayout.LongField("First row (0-based)", firstRow); endRow = EditorGUILayout.LongField("End row (exclusive)", endRow); }
                using (new EditorGUILayout.VerticalScope())
                { minimum = EditorGUILayout.DoubleField("Minimum", minimum); maximum = EditorGUILayout.DoubleField("Maximum", maximum); }
                using (new EditorGUILayout.VerticalScope())
                {
                    bins = EditorGUILayout.IntField("Bins (1–4096)", bins);
                    invalidPolicy = (FPWorkbenchInvalidPolicy)EditorGUILayout.EnumPopup("Invalid values", invalidPolicy);
                }
                using (new EditorGUILayout.VerticalScope())
                { if (GUILayout.Button("Fit limits / build")) BeginFit(); if (GUILayout.Button("Apply / build")) BeginBuild(); }
            }
            using (new EditorGUILayout.VerticalScope())
            {
                density = GUILayout.Toggle(density, "Density", EditorStyles.toolbarButton); grid = GUILayout.Toggle(grid, "Grid", EditorStyles.toolbarButton);
                if (Busy) { GUILayout.Label(waitingForMemory ? "Waiting for shared memory / scan capacity…" : "Reading source in background…", EditorStyles.miniLabel); if (GUILayout.Button("Cancel")) CancelHistogram(); }
            }
            RecordHistogramSettings(beforeSettings);
            DrawImageExport();
            FPWorkbenchPlotPresetGUI.DrawFileActions(() => new FPWorkbenchHistogramPreset { Density = density, Grid = grid }.ToJson(),
                ApplyPresentationPreset, "histogram preset", "fphistplot", FPWorkbenchHistogramPreset.MaximumBytes);
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Warning);
        }
        private void DrawFigureGraph()
        {
            if (result == null) { EditorGUILayout.HelpBox("Build a histogram to display counts. Source files remain unchanged.", MessageType.Info); return; }
            GUILayout.Label($"Displayed rows [{result.FirstRow:N0}, {result.EndRow:N0}) • {result.Counts.Count} bins • included {result.IncludedCount:N0} • invalid {result.InvalidCount:N0} • below {result.UnderflowCount:N0} • above {result.OverflowCount:N0}", EditorStyles.miniLabel);
            Rect area = GUILayoutUtility.GetRect(100, 10000, 100, 10000, GUILayout.ExpandHeight(true));
            try { DrawBars(area); } catch (OverflowException ex) { GUI.Label(area, ex.Message); }
                }
        private void DrawBars(Rect area)
        {
            EditorGUI.DrawRect(area, new Color(.1f, .1f, .1f));
            Rect plot = new Rect(area.x + 62, area.y + 20, Math.Max(1, area.width - 76), Math.Max(1, area.height - 48));
            double peak = 0;
            for (int i = 0; i < result.Counts.Count; i++) peak = Math.Max(peak, density ? result.Density(i) : result.Counts[i]);
            if (peak == 0) peak = 1;
            for (int i = 0; i <= 4; i++)
            {
                float y = plot.yMax - plot.height * i / 4;
                if (grid) EditorGUI.DrawRect(new Rect(plot.x, y, plot.width, 1), new Color(.4f, .4f, .4f, .3f));
                GUI.Label(new Rect(area.x + 3, y - 8, 58, 18), (peak * i / 4).ToString("G4", CultureInfo.InvariantCulture), EditorStyles.miniLabel);
            }
            float width = plot.width / result.Counts.Count;
            for (int i = 0; i < result.Counts.Count; i++)
            {
                double value = density ? result.Density(i) : result.Counts[i]; float height = (float)(value / peak * plot.height);
                EditorGUI.DrawRect(new Rect(plot.x + width * i, plot.yMax - height, Math.Max(.5f, width - 1), height), new Color(.15f, .65f, .9f));
            }
            GUI.Label(new Rect(plot.x, plot.yMax + 4, plot.width / 2, 20), result.Edges[0].ToString("G6", CultureInfo.InvariantCulture), EditorStyles.miniLabel);
            GUI.Label(new Rect(plot.xMax - 100, plot.yMax + 4, 100, 20), result.Edges[result.Edges.Count - 1].ToString("G6", CultureInfo.InvariantCulture), EditorStyles.miniLabel);
            pointsOfInterest.DrawMarkers(InterestAxisKey, p => ProjectInterest(p, plot, false), Color.yellow);
            if (plot.Contains(Event.current.mousePosition))
            {
                int bin = Mathf.Clamp((int)((Event.current.mousePosition.x - plot.x) / width), 0, result.Counts.Count - 1);
                GUI.Label(new Rect(plot.x + 4, area.y, plot.width - 8, 20), $"Bin {bin + 1}: [{result.Edges[bin]:G6}, {result.Edges[bin + 1]:G6}{(bin == result.Counts.Count - 1 ? "]" : ")")} • {result.Counts[bin]:N0} samples", EditorStyles.miniLabel);
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0) {
                    selectedInterest = new FPWorkbenchFigurePoint { X = (result.Edges[bin] + result.Edges[bin + 1]) * .5, Y = density ? result.Density(bin) : result.Counts[bin], AxisKey = InterestAxisKey,
                        Details = "Bin " + (bin + 1) + ": [" + result.Edges[bin].ToString("G9", CultureInfo.InvariantCulture) + ", " + result.Edges[bin + 1].ToString("G9", CultureInfo.InvariantCulture) + (bin == result.Counts.Count - 1 ? "]" : ")") + "; " + result.Counts[bin].ToString("N0") + " samples (aggregate)" }; Event.current.Use(); Repaint();
                }
                if (Event.current.type == EventType.MouseMove) Repaint();
            }
        }
    }
}
