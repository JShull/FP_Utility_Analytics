// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using UnityEditor;
    using UnityEngine;
    using FuzzPhyte.Utility.Editor;

    /// <summary>Initial virtualized CSV workbench. All source reads and indexing run in runtime services.</summary>
    public sealed partial class FPDataWorkbenchWindow : EditorWindow
    {
        [Serializable] private sealed class Sheet
        {
            public string Id = Guid.NewGuid().ToString("N"), Name, Source, Cache;
            public long FirstRow;
            [NonSerialized] public long RowRangeDraftFirst, RowRangeDraftEnd;
            public int FirstColumn, PlotColumn;
            public FPWorkbenchSeriesBinding Binding = new FPWorkbenchSeriesBinding();
            public FPWorkbenchPlotSettings Plot = new FPWorkbenchPlotSettings();
            public FPWorkbenchTableSelection Selection;
            public long RangeAnchorRow;
            public string RangeAnchorColumn;
            public bool ShowPlot;
            public bool ShowOriginal;
            public bool CellGrid;
            public float CellGridOpacity = .35f;
            [NonSerialized] public FPWorkbenchGraphSelection GraphSelection;
            [NonSerialized] public FPWorkbenchSeriesPoint? PlotSelectedPoint;
            [NonSerialized] public FPWorkbenchSeriesCache PlotSelectedSeries;
            [NonSerialized] public FPWorkbenchPresentationUndo PresentationUndo;
            [NonSerialized] public FPWorkbenchColumnProjectionUndo ProjectionUndo;
            [NonSerialized] public int[] ColumnOrder;
            [NonSerialized] public int[] FrozenViewport, FrozenViewportOrder;
            [NonSerialized] public string[] FrozenViewportIds;
            [NonSerialized] public int FrozenViewportFirst;
            [NonSerialized] public string[] SchemaIds;
            [NonSerialized] public FPWorkbenchTableView PageTable;
            [NonSerialized] public FPWorkbenchSeriesCache Series;
            [NonSerialized] public FPWorkbenchFigureSession.Lease FigureLease;
            [NonSerialized] public long FigureRevision = -1;
            [NonSerialized] public string[] ColumnNames;
            public FPWorkbenchColumnLayout Columns;
            public FPWorkbenchCsvProfile Import;
            [NonSerialized] public FPWorkbenchCsvDataset Dataset;
            [NonSerialized] public IDisposable DiskLease;
            [NonSerialized] public FPWorkbenchTableView Table;
            [NonSerialized] public FPWorkbenchFormulaSet TableDefinitions;
            [NonSerialized] public FPWorkbenchTypedSchema TableTypedDefinitions;
            [NonSerialized] public FPWorkbenchCompiledSchema TableTypedSchema;
            [NonSerialized] public FPWorkbenchFormulaProgram TableProgram;
            [NonSerialized] public string[] TableNames;
            [NonSerialized] public Task<FPWorkbenchCsvDataset> Loading;
            [NonSerialized] public Func<Task<FPWorkbenchCsvDataset>> PendingLoading;
            [NonSerialized] public FPWorkbenchEditorResources.ScanReservation LoadMemory;
            [NonSerialized] public long LoadBytes;
            [NonSerialized] public bool WaitingForLoad;
            [NonSerialized] public ImportPreviewReports PreviewReports;
            [NonSerialized] public FPWorkbenchCsvPreview Preview;
            [NonSerialized] public CancellationTokenSource Lifetime;
        }
        [SerializeField] private List<Sheet> sheets = new List<Sheet>();
        [SerializeField] private int active;
        [SerializeField] private string importPath = "";
        [SerializeField] private int encoding;
        [SerializeField] private float sidebar = 260, plotHeight = 220;
        private Task<FPWorkbenchPage> pageWork;
        private bool windowActive;
        private CancellationTokenSource pageCancel;
        private FPWorkbenchPage page;
        private FPWorkbenchSeriesView summary;
        private string pageSheet, summarySheet, error, status = "Open a CSV to create a sheet.";
        private long requestedRow;
        private int requestedColumn, requestedColumnCount;
        private FPWorkbenchSeriesCache requestedSeries;
        private Vector2 sidebarScroll;
        private string failedPage;
        private double progress;
        private Rect tableRect;
        private int resizeControl;
        private bool resizeSidebar;
        private Vector2 resizeStartMouse;
        private float resizeStartSize;
        private Rect plotDividerRect;
        private double repaintUntil, lastResizeRepaint;
        private Vector2 lastWindowPixels;
        private float lastSidebarWidth;
        private readonly Rect[] columnButtonRects = new Rect[4];
        private Rect rowHeaderRestoreRect;
        private Sheet Current => active >= 0 && active < sheets.Count ? sheets[active] : null;
        private const int RowsPerPage = 128;
        private int visibleColumns = 1;
        private const float MinimumColumnWidth = 110, RowHeaderWidth = 76;
        internal static int ColumnCapacity(float width, int remainingColumns) => Math.Min(Math.Max(1, remainingColumns), Math.Max(1, Math.Min(32, (int)Math.Floor(Math.Max(0, width - RowHeaderWidth) / MinimumColumnWidth))));

        [MenuItem(FP_UtilityData.MENU_COMPANY + "Utility/Analytics/Data Workbench", priority = FP_UtilityData.MENU_UTILITY_EDITOR + 70)]
        public static void Open() => GetWindow<FPDataWorkbenchWindow>("Data Workbench");
        private void OnEnable()
        {
            windowActive = true; FPWorkbenchEditorCache.Register(OnDisable);
            minSize = new Vector2(720, 480);
            InitializeCorrectionUndo();
            InitializePlotUndo();
            InitializeCalculationUndo();
            AssemblyReloadEvents.beforeAssemblyReload += PreserveWorkingMetadata;
            foreach (var sheet in sheets)
            {
                sheet.Lifetime = new CancellationTokenSource();
                if (!string.IsNullOrEmpty(sheet.Cache)) {
                    string cache = sheet.Cache; var token = sheet.Lifetime.Token;
                    // Older serialized windows have no saved bound: admit the largest supported parser before opening.
                    var reference = registeredSources?.Find(s => s.DatasetId == sheet.Dataset?.DatasetId || s.CacheDirectory == cache);
                    QueueSheetLoad(sheet, () => reference == null ? FPWorkbenchEditorCache.OpenAsync(cache, token) : FPWorkbenchEditorCache.OpenAsync(reference, token), sheet.Import ?? new FPWorkbenchCsvProfile { MaximumRecordBytes = 16 * 1024 * 1024, MaximumColumns = 4096 });
                }
            }
            EditorApplication.update += Tick;
        }
        private void OnDisable()
        {
            if (!windowActive) return; windowActive = false; FPWorkbenchEditorCache.Unregister(OnDisable);
            CancelPlotPick(true);
            PreserveWorkingMetadata();
            pendingLinkedDataset = null; pendingLinkedColumns = null;
            CancelSelectionFrame();
            CancelClipboard();
            CancelExport();
            CancelSourceOpening();
            CancelChangeFiles();
            Undo.undoRedoPerformed -= RestoreCorrectionUndo;
            Undo.undoRedoPerformed -= RestorePlotUndo;
            Undo.undoRedoPerformed -= RestoreCalculationUndo;
            AssemblyReloadEvents.beforeAssemblyReload -= PreserveWorkingMetadata;
            Cancel(workbookCancel); AbandonWorkbookLoad();
            if (workbookSaveMemory != null) workbookSaveMemory.ReleaseAfter(workbookSave); else Observe(workbookSave);
            workbookSaveMemory = null; workbookSave = null;
            if (sizingSheet != null) { SaveColumns(sizingSheet); sizingSheet = null; sizingColumn = -1; if (GUIUtility.hotControl == columnControl) GUIUtility.hotControl = 0; }
            EditorApplication.update -= Tick;
            AbandonRawPage();
            CancelVirtualPage();
            foreach (var sheet in sheets) { sheet.ProjectionUndo?.Dispose(); sheet.ProjectionUndo = null; sheet.GraphSelection?.Dispose(); sheet.GraphSelection = null; sheet.PresentationUndo?.Dispose(); sheet.PresentationUndo = null; sheet.FigureLease?.Dispose(); sheet.FigureLease = null; CancelSheetLoad(sheet); }
        }
        private static void Observe(Task task)
        {
            if (task != null) task.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        }
        private static void Cancel(CancellationTokenSource source) { if (source != null) { source.Cancel(); source.Dispose(); } }
        private void Tick()
        {
            // Editor update may already have captured its invocation list when this window closes.
            if (!windowActive) return;
            bool changed = TickPlotPick() | TickDiskCache() | TickClipboard() | TickWorkbook() | TickChanges() | TickSourceOpening() | TickExport() | TickLinkedSelection();
            double now = EditorApplication.timeSinceStartup;
            Vector2 pixels = position.size;
            if (pixels != lastWindowPixels || sidebar != lastSidebarWidth)
            {
                if (Current?.Dataset != null && Current.ColumnOrder != null && tableRect.width > 1)
                    visibleColumns = FitTableColumns(Current, Math.Max(1, tableRect.width + pixels.x - lastWindowPixels.x - sidebar + lastSidebarWidth - IndexWidth(Current) - RowScrollWidth)).Length;
                repaintUntil = now + .25;
                lastWindowPixels = pixels; lastSidebarWidth = sidebar;
                Repaint();
            }
            if (now < repaintUntil && now - lastResizeRepaint >= 1.0 / 30)
            { lastResizeRepaint = now; Repaint(); }
            foreach (var sheet in sheets)
            {
                var preview = sheet.PreviewReports?.Take();
                if (preview != null) { sheet.Preview = preview; changed = true; }
                if (sheet.PendingLoading != null) { TryStartSheetLoad(sheet); changed = true; }
                if (sheet.Loading == null || !sheet.Loading.IsCompleted) continue;
                if (sheet.Loading.Status == TaskStatus.RanToCompletion)
                { sheet.Dataset = sheet.Loading.Result; sheet.DiskLease?.Dispose(); sheet.DiskLease = FPWorkbenchEditorCache.Retain(sheet.Dataset); sheet.Cache = sheet.Dataset.CacheDirectory; if (sheet.Columns == null) RestoreColumns(sheet); else sheet.Columns.EnsureColumns(TableColumnCount(sheet.Dataset)); sheet.ColumnNames = new string[sheet.Dataset.ColumnCount]; for (int c = 0; c < sheet.ColumnNames.Length; c++) sheet.ColumnNames[c] = (c + 1) + ": " + sheet.Dataset.GetColumnName(c); status = "Indexed " + sheet.Dataset.RowCount.ToString("N0") + " logical data rows."; }
                else if (sheet.Loading.IsFaulted) error = sheet.Loading.Exception.GetBaseException().Message;
                ClearImportPreview(sheet); sheet.LoadMemory?.Dispose(); sheet.LoadMemory = null;
                sheet.Loading = null; sheet.WaitingForLoad = false; changed = true;
                if (sheet.Dataset != null) sheet.Import = sheet.Dataset.ImportProfile;
                if (sheet.Dataset != null) { try { RememberSource(sheet.Dataset, sheet.Name); } catch (Exception ex) { error = ex.Message; } }
            }
            var current = Current;
            foreach (var sheet in sheets) if (sheet.Dataset != null) EnsureTableSchema(sheet);
            changed |= TickVirtualPage(current);
            if (current?.Dataset == null) AbandonRawPage();
            if (pageWork != null && pageWork.IsCompleted)
            {
                if (pageWork.Status == TaskStatus.RanToCompletion && current != null && current.Id == pageSheet && current.FirstRow == requestedRow && current.FirstColumn == requestedColumn && visibleColumns == requestedColumnCount) {
                    rawMemory?.Dispose(); rawMemory = pendingRawMemory; pendingRawMemory = null; page = pageWork.Result;
                    rawMemory.Complete(RetainedPageBytes(page.CharacterCount, page.RowCount, requestedColumnCount, false));
                }
                else if (pageWork.IsFaulted) { error = pageWork.Exception.GetBaseException().Message; failedPage = pageSheet + ":" + requestedRow + ":" + requestedColumn + ":" + requestedColumnCount; }
                pendingRawMemory?.Dispose(); pendingRawMemory = null;
                pageWork = null; changed = true;
            }
            changed |= TickPlot(current);
            if (current?.Dataset != null && current.Table == null && !UsesProjection(current))
            {
                bool pageMissing = page == null || page.DatasetId != current.Dataset.DatasetId || page.FirstRow != current.FirstRow || page.FirstColumn != current.FirstColumn || (page.RowCount > 0 && page.Values[0].Length != visibleColumns);
                if (pageMissing && pageWork == null && failedPage != current.Id + ":" + current.FirstRow + ":" + current.FirstColumn + ":" + visibleColumns)
                {
                    Cancel(pageCancel); pageCancel = new CancellationTokenSource();
                    pageSheet = current.Id; requestedRow = current.FirstRow; requestedColumn = current.FirstColumn;
                    requestedColumnCount = Math.Min(visibleColumns, current.Dataset.ColumnCount - current.FirstColumn);
                    rawMemory?.Dispose(); rawMemory = null; page = null;
                    if (AdmitPage(current.Dataset, out pendingRawMemory)) {
                        try { pageWork = current.Dataset.ReadPageAsync(current.FirstRow, RowsPerPage, current.FirstColumn, requestedColumnCount, pageCancel.Token); }
                        catch { pendingRawMemory.Dispose(); pendingRawMemory = null; throw; }
                    }
                }
                else if (pageWork != null && (pageSheet != current.Id || requestedRow != current.FirstRow || requestedColumn != current.FirstColumn || requestedColumnCount != visibleColumns)) pageCancel?.Cancel();
            }
            if (changed || virtualPageWork != null || changeLoad != null || changeSave != null || workbookLoad != null || workbookSave != null || pageWork != null || (current?.FigureLease != null && current.FigureLease.Session.IsBusy) || current?.Loading != null) Repaint();
        }
        private void AddCsv(string path)
        {
            if (registeredSources != null && registeredSources.Count >= 64) { error = "This workbook has 64 registered sources; reopen an existing source from the source list or use another workbook."; return; }
            if (sheets.Count >= 8) { error = "This first slice supports eight open sheets."; return; }
            foreach (var existing in sheets) if (IsLoading(existing)) { error = "Wait for or cancel the active import before adding another sheet."; return; }
            try
            {
                string full = Path.GetFullPath(path);
                var profile = new FPWorkbenchCsvProfile { EncodingCodePage = encoding == 0 ? 65001 : 1252 };
                var reference = registeredSources?.Find(s => string.Equals(Path.GetFullPath(s.SourcePath), full, StringComparison.OrdinalIgnoreCase) && s.Import.EncodingCodePage == profile.EncodingCodePage && s.Import.Delimiter == profile.Delimiter);
                var sheet = new Sheet { Name = Path.GetFileNameWithoutExtension(full), Source = full, Cache = reference?.CacheDirectory, Lifetime = new CancellationTokenSource() };
                var token = sheet.Lifetime.Token; var reporter = new ImportProgress(this);
                var previews = new ImportPreviewReports(); sheet.PreviewReports = previews;
                QueueSheetLoad(sheet, () => reference == null ? FPWorkbenchEditorCache.ImportAsync(full, profile, token, reporter, previews) : FPWorkbenchEditorCache.OpenAsync(reference, token), profile);
                sheets.Add(sheet); active = sheets.Count - 1; error = null; progress = 0; status = "Hashing/indexing in background; source remains unchanged.";
            }
            catch (Exception ex) { error = ex.Message; }
        }
        private sealed class ImportProgress : IProgress<double>
        {
            private readonly FPDataWorkbenchWindow owner;
            public ImportProgress(FPDataWorkbenchWindow owner) { this.owner = owner; }
            public void Report(double value) { Interlocked.Exchange(ref owner.progress, value); }
        }
        private void OnGUI()
        {
            // Allocate before variable table/sidebar controls so capture survives layout changes.
            int sidebarControl = GUIUtility.GetControlID(7381, FocusType.Passive);
            int plotControl = GUIUtility.GetControlID(7382, FocusType.Passive);
            ContinueResize();
            ColumnInput();
            ColumnDragInput();
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(sidebar)))
                { sidebarScroll = EditorGUILayout.BeginScrollView(sidebarScroll); DrawSidebar(); EditorGUILayout.EndScrollView(); }
                using (new EditorGUILayout.VerticalScope())
                {
                    DrawTabs();
                    var sheet = Current;
                    if (sheet?.Dataset != null)
                    {
                        DrawNavigation(sheet);
                        DrawRowRangeTableControls(sheet);
                        Rect currentTable = GUILayoutUtility.GetRect(100, 10000, 100, 10000, GUILayout.ExpandHeight(true));
                        if (Event.current.type == EventType.Repaint) { tableRect = currentTable; lastWindowPixels = position.size; lastSidebarWidth = sidebar; }
                        DrawTable(sheet, currentTable);
                        Rect divider = GUILayoutUtility.GetRect(10, 6, GUILayout.ExpandWidth(true));
                        if (Event.current.type == EventType.Repaint) plotDividerRect = divider;
                        Resize(divider, false, plotControl);
                        DrawPlotPickControls(sheet);
                        Rect plot = GUILayoutUtility.GetRect(100, plotHeight, GUILayout.ExpandWidth(true));
                        bool matches = summary != null && sheet.Id == summarySheet && sheet.Series == requestedSeries;
                        FPWorkbenchPlotView.Draw(plot, matches ? summary : null, sheet.Plot,
                            sheet.Series == null ? "" : (sheet.ShowOriginal ? "Original • " : "Effective • ") + sheet.Series.YName);
                        DrawPlotPointSelection(sheet, plot, matches);
                        DrawColumnDropTargets(sheet, plot);
                        if (matches && (summary.StartX != sheet.Plot.StartX || summary.EndX != sheet.Plot.EndX))
                            GUI.Label(new Rect(plot.x + 80, plot.y + 23, plot.width - 90, 20), "Updating view… (previous range shown)", EditorStyles.miniLabel);
                    }
                    else if (sheet?.Preview != null) DrawImportPreview(sheet);
                    else EditorGUILayout.HelpBox(sheet?.WaitingForLoad == true ? "Waiting for shared memory/background capacity…" : IsLoading(sheet) ? "Indexing/verifying source in the background…" : "Open Workbook Settings to import a CSV or open a workbook.", MessageType.Info);
                }
            }
            Resize(new Rect(sidebar + 2, 0, 6, position.height), true, sidebarControl);
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
            EditorGUILayout.LabelField(status, EditorStyles.miniLabel);
        }
        private void DrawSidebar()
        {
            GUILayout.Label("Data Workbench", EditorStyles.boldLabel);
            if (GUILayout.Button("Workbook Settings…")) FPWorkbenchWorkbookSettingsWindow.Open(this);
            EditorGUILayout.LabelField(workbookName, EditorStyles.miniLabel);
            var sheet = Current;
            if (sheet == null) return;
            FPMeshPreviewEditorUtility.DrawSectionDivider();
            showSheetDetails = EditorGUILayout.Foldout(showSheetDetails, "Active sheet", true);
            if (showSheetDetails) {
                sheet.Name = EditorGUILayout.TextField("Sheet name", sheet.Name);
                if (IsLoading(sheet)) {
                    EditorGUILayout.LabelField("Index progress", progress.ToString("P0"));
                    if (GUILayout.Button("Cancel import")) { CancelSheetLoad(sheet); status = "Import cancelled; original source retained."; }
                }
                if (sheet.Dataset != null) { DrawCorrectionControls(sheet); DrawCellGridControls(sheet); DrawPageBudget(); }
            }
            if (sheet.Dataset != null)
            {
                DrawRowRangeControls(sheet);
                FPMeshPreviewEditorUtility.DrawSectionDivider();
                DrawTypedSchemaControls(sheet);
                FPMeshPreviewEditorUtility.DrawSectionDivider();
                DrawSourceUnitControls(sheet);
                FPMeshPreviewEditorUtility.DrawSectionDivider();
                DrawCalculationControls(sheet);
                FPMeshPreviewEditorUtility.DrawSectionDivider();
                DrawExportControls(sheet);
                DrawPlotControls(sheet);
            }
            FPMeshPreviewEditorUtility.DrawSectionDivider();
            if (GUILayout.Button("Close sheet"))
                CloseCurrentSheet();
        }
        internal void DrawWorkbookSettings()
        {
            DrawWorkbookControls();
            SidebarSection("Shared Workbench cache");
            EditorGUILayout.HelpBox("Cache root and quota apply across all workbooks. Active datasets and figures keep their caches protected.", MessageType.Info);
            DrawDiskCacheControls();
            SidebarSection("Corrections files");
            DrawChangeFiles();
            SidebarSection("CSV import");
            encoding = EditorGUILayout.Popup("CSV encoding", encoding, new[] { "UTF-8 (strict)", "Windows-1252" });
            importPath = EditorGUILayout.TextField("CSV path", importPath);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Browse…")) { string p = EditorUtility.OpenFilePanel("Open CSV", "", "csv"); if (!string.IsNullOrEmpty(p)) importPath = p; }
                if (GUILayout.Button("Add sheet")) AddCsv(importPath);
            }
            if (IsLoading(Current)) EditorGUILayout.LabelField("Index progress", progress.ToString("P0"));
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
            if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, MessageType.None);
        }
        [SerializeField] private bool showSheetDetails = true, showRowRange = true;
        [SerializeField] private string settingsOwnerId = Guid.NewGuid().ToString("N");
        internal string SettingsOwnerId => settingsOwnerId;
        private static void SidebarSection(string title)
        {
            FPMeshPreviewEditorUtility.DrawSectionDivider();
            GUILayout.Label(title, EditorStyles.boldLabel);
        }
        private void CloseCurrentSheet()
        {
            CancelSelectionFrame();
            CancelClipboard();
            var sheet = Current; if (sheet == null) return;
            try { RememberSource(sheet.Dataset, sheet.Name); } catch (Exception ex) { error = ex.Message; return; }
            sheet.ProjectionUndo?.Dispose(); sheet.ProjectionUndo = null; sheet.GraphSelection?.Dispose(); sheet.GraphSelection = null; sheet.PresentationUndo?.Dispose(); sheet.PresentationUndo = null; sheet.FigureLease?.Dispose(); sheet.FigureLease = null; CancelSheetLoad(sheet); ClearPlotUndo(sheet.Id);
            sheets.RemoveAt(active); active = Math.Max(0, active - 1); page = null; summary = null;
        }
        private void DrawTabs()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
                for (int i = 0; i < sheets.Count; i++)
                    if (GUILayout.Toggle(i == active, sheets[i].Name, EditorStyles.toolbarButton) && active != i)
                { active = i; page = null; summary = null; }
        }
        private void DrawNavigation(Sheet sheet)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Rows", GUILayout.Width(70));
                using (new EditorGUI.DisabledScope(sheet.FirstRow == 0))
                    if (GUILayout.Button("◀", EditorStyles.toolbarButton, GUILayout.Width(28))) Move(sheet, -RowsPerPage);
                long row = EditorGUILayout.LongField(sheet.FirstRow + 1, GUILayout.Width(120));
                sheet.FirstRow = Math.Max(0, Math.Min(sheet.Dataset.RowCount - 1, row <= 0 ? 0 : row - 1));
                using (new EditorGUI.DisabledScope(sheet.FirstRow >= sheet.Dataset.RowCount - 1))
                    if (GUILayout.Button("▶", EditorStyles.toolbarButton, GUILayout.Width(28))) Move(sheet, RowsPerPage);
                GUILayout.Label("/ " + sheet.Dataset.RowCount.ToString("N0") + " rows");
                GUILayout.FlexibleSpace();
            }
            int shown = FitTableColumns(sheet, Math.Max(1, tableRect.width - IndexWidth(sheet) - RowScrollWidth)).Length;
            int scrollingShown = Math.Max(0, shown - FrozenCount(sheet));
            int left = sheet.FirstColumn, right = Math.Max(0, ScrollingCount(sheet) - sheet.FirstColumn - scrollingShown);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Columns", GUILayout.Width(70));
                using (new EditorGUI.DisabledScope(left == 0))
                {
                    if (ColumnButton(0, "◀◀", "Move left by one visible window")) sheet.FirstColumn = FrozenCount(sheet) > 0 ? Math.Max(0, sheet.FirstColumn - Math.Max(1, scrollingShown)) : sheet.Columns.PreviousFirst(sheet.FirstColumn, Math.Max(1, tableRect.width - IndexWidth(sheet) - RowScrollWidth), order: sheet.ColumnOrder);
                    if (ColumnButton(1, "◀", "Move left by one column")) sheet.FirstColumn = Math.Max(0, sheet.FirstColumn - 1);
                }
                using (new EditorGUI.DisabledScope(right == 0))
                {
                    if (ColumnButton(2, "▶", "Move right by one column")) sheet.FirstColumn++;
                    if (ColumnButton(3, "▶▶", "Move right by one visible window")) sheet.FirstColumn = Math.Min(Math.Max(0, ScrollingCount(sheet) - 1), sheet.FirstColumn + Math.Max(1, scrollingShown));
                }
                if (GUILayout.Button("Visibility…", EditorStyles.toolbarButton, GUILayout.Width(75))) ShowColumnVisibility(sheet);
                GUILayout.Label("Showing " + (ScrollingCount(sheet) == 0 ? 0 : left + 1) + "–" + (left + scrollingShown) + " / " + ScrollingCount(sheet) + (FrozenCount(sheet) == 0 ? "" : " + " + FrozenCount(sheet) + " frozen"));
                GUILayout.FlexibleSpace();
            }
            Rect remaining = GUILayoutUtility.GetRect(1, 16, GUILayout.ExpandWidth(true));
            float restoreWidth = sheet.Columns.ShowRowHeader ? 0 : 84;
            Rect restore = new Rect(remaining.x, remaining.y, restoreWidth, remaining.height);
            if (Event.current.type == EventType.Repaint) rowHeaderRestoreRect = restore;
            if (!sheet.Columns.ShowRowHeader && GUI.Button(restore, "Show Row #", EditorStyles.miniButton))
            { sheet.Columns.ShowRowHeader = true; SaveColumns(sheet); }
            GUI.Label(new Rect(remaining.x + restoreWidth + 3, remaining.y, remaining.width * .5f, remaining.height), left.ToString("N0") + " columns left", EditorStyles.miniLabel);
            string rightText = right.ToString("N0") + " columns right";
            float rightWidth = EditorStyles.miniLabel.CalcSize(new GUIContent(rightText)).x;
            GUI.Label(new Rect(remaining.xMax - rightWidth - 3, remaining.y, rightWidth, remaining.height), rightText, EditorStyles.miniLabel);
            DrawRangeToolbar(sheet);
        }
        private bool ColumnButton(int index, string text, string tooltip)
        {
            bool clicked = GUILayout.Button(new GUIContent(text, tooltip), EditorStyles.toolbarButton, GUILayout.Width(28));
            if (Event.current.type == EventType.Repaint) columnButtonRects[index] = GUILayoutUtility.GetLastRect();
            return clicked;
        }
        private void Move(Sheet sheet, long delta) => sheet.FirstRow = Math.Max(0, Math.Min(Math.Max(0, sheet.Dataset.RowCount - 1), sheet.FirstRow + delta));
        private void DrawTable(Sheet sheet, Rect rect)
        {
            DrawRowScroll(sheet, new Rect(rect.xMax - RowScrollWidth, rect.y + 20, RowScrollWidth, Math.Max(50, rect.height - 20)));
            rect.width = Math.Max(1, rect.width - RowScrollWidth);
            float indexWidth = IndexWidth(sheet);
            EditorGUI.DrawRect(rect, new Color(.1f, .1f, .1f));
            if (indexWidth > 0) EditorGUI.DrawRect(new Rect(rect.x, rect.y, Math.Min(indexWidth, rect.width), rect.height), EditorGUIUtility.isProSkin ? new Color(.15f, .18f, .21f) : new Color(.72f, .77f, .82f));
            if (indexWidth > 0) EditorGUI.DrawRect(new Rect(rect.x + indexWidth - 1, rect.y, 1, rect.height), new Color(.45f, .53f, .61f, .6f));
            if (indexWidth > 0) RowHeaderGrip(sheet, new Rect(rect.x, rect.y, indexWidth, 20));
            if (indexWidth > 0 && !GUI.Toggle(new Rect(rect.x + 2, rect.y, Math.Max(1, indexWidth - 6), 20), true, new GUIContent("Row #", "Hide the workbench's 1-based record index. Restore it using Show Row # above the table."), EditorStyles.toolbarButton))
            { sheet.Columns.ShowRowHeader = false; SaveColumns(sheet); Repaint(); }
            if (Event.current.type == EventType.ScrollWheel && rect.Contains(Event.current.mousePosition))
            { Move(sheet, (long)Math.Sign(Event.current.delta.y) * 8); Event.current.Use(); }
            if (rect.width > 1)
            {
                columnPixels = FitTableColumns(sheet, Math.Max(1, rect.width - indexWidth));
                if (Event.current.type == EventType.Repaint) visibleColumns = columnPixels.Length;
            }
            int columns = WindowCount(sheet, columnPixels.Length);
            float edge = rect.x + indexWidth;
            for (int c = 0; c < columns; c++)
            {
                int schemaColumn = SchemaColumn(sheet, sheet.FirstColumn + c);
                float width = columnPixels[c];
                float visibleWidth = Math.Max(1, Math.Min(width, rect.xMax - edge));
                bool calculated = schemaColumn >= sheet.Dataset.ColumnCount;
                if (c < FrozenCount(sheet)) EditorGUI.DrawRect(new Rect(edge, rect.y, visibleWidth, rect.height), new Color(.2f, .45f, .55f, .13f));
                if (calculated) EditorGUI.DrawRect(new Rect(edge, rect.y, visibleWidth, 20), new Color(.4f, .3f, .65f, .25f));
                string token = calculated ? "D" + (schemaColumn - sheet.Dataset.ColumnCount + 1) : "C" + (schemaColumn + 1);
                string name = calculated ? "ƒ " + sheet.Table.ColumnName(schemaColumn) : sheet.Dataset.GetColumnName(schemaColumn);
                var header = new Rect(edge + 3, rect.y, Math.Max(1, visibleWidth - 8), 20);
                float tokenWidth = Math.Min(header.width, EditorStyles.miniLabel.CalcSize(new GUIContent(token)).x + 8);
                string tooltip = token + ": " + name + ". Formula reference uses the source/definition order, not the displayed position. Units: " + ColumnUnitLabel(sheet, schemaColumn) + ". No implicit conversion. Drag the right edge to resize; double-click or right-click the edge to reset to Auto.";
                GUI.Label(new Rect(header.x, header.y, Math.Max(0, header.width - tokenWidth), 20), new GUIContent(name, tooltip), EditorStyles.boldLabel);
                GUI.Label(new Rect(header.xMax - tokenWidth, header.y, tokenWidth, 20), new GUIContent(token, tooltip), EditorStyles.centeredGreyMiniLabel);
                ColumnGrip(sheet, schemaColumn, new Rect(edge + visibleWidth - 4, rect.y, 8, 20), width);
                ColumnHeaderMenu(sheet, schemaColumn, new Rect(edge + 3, rect.y, Math.Max(0, visibleWidth - 9), 20));
                ColumnDragHeader(sheet, schemaColumn, new Rect(edge + 3, rect.y, Math.Max(0, visibleWidth - 9), 20));
                edge += width;
            }
            DrawCellGrid(sheet, rect, indexWidth, columns);
            if (sheet.Table != null || UsesProjection(sheet)) { DrawVirtualRows(sheet, rect, indexWidth, columns); return; }
            if (page == null || page.DatasetId != sheet.Dataset.DatasetId || page.FirstRow != sheet.FirstRow || page.FirstColumn != sheet.FirstColumn)
            { GUI.Label(new Rect(rect.x + 5, rect.y + 24, rect.width, 20), "Loading page…"); return; }
            int rows = Math.Min(page.RowCount, Math.Max(0, (int)(rect.height / 20) - 1));
            var effective = sheet.ShowOriginal ? null : SnapshotFor(sheet.Dataset.DatasetId);
            for (int r = 0; r < rows; r++)
            {
                float y = rect.y + (r + 1) * 20;
                DrawLinkedRow(sheet, page.FirstRow + r, new Rect(rect.x, y, rect.width, 20));
                if (indexWidth > 0) GUI.Label(new Rect(rect.x + 3, y, indexWidth - 6, 20), (page.FirstRow + r + 1).ToString(CultureInfo.InvariantCulture), EditorStyles.miniLabel);
                if (indexWidth > 0) DrawLinkedCell(sheet, page.FirstRow + r, -1, new Rect(rect.x + 1, y, indexWidth - 2, 20));
                edge = rect.x + indexWidth;
                for (int c = 0; c < columns; c++)
                {
                    var cellRect = new Rect(edge + 3, y, Math.Max(1, Math.Min(columnPixels[c] - 6, rect.xMax - edge - 3)), 20);
                    edge += columnPixels[c];
                    if (c >= page.Values[r].Length) { GUI.Label(cellRect, "…", EditorStyles.miniLabel); continue; }
                    string text = page.Values[r][c];
                    if (effective != null && effective.TryGet(sheet.Dataset.Cell(page.FirstRow + r, sheet.FirstColumn + c), text, out string corrected, out bool masked))
                    { text = masked ? "[masked]" : corrected; EditorGUI.DrawRect(cellRect, new Color(.65f, .4f, .1f, .2f)); }
                    DrawTableSelection(sheet, page.FirstRow + r, sheet.FirstColumn + c, cellRect);
                    DrawLinkedCell(sheet, page.FirstRow + r, sheet.FirstColumn + c, cellRect);
                    if (GUI.Button(cellRect, new GUIContent(text, "Click to inspect/edit a source-addressed cell; original file is never modified"), EditorStyles.label))
                    {
                        SelectTableRange(sheet, page.FirstRow + r, sheet.FirstColumn + c, Event.current.shift);
                        var cell = sheet.Dataset.Cell(page.FirstRow + r, sheet.FirstColumn + c);
                        status = "Row " + (cell.Row + 1) + ", column " + (cell.Column + 1) + " • source: " + page.Values[r][c];
                        sheet.PlotColumn = cell.Column;
                        SelectCell(sheet, cell.Row, cell.Column, page.Values[r][c]);
                    }
                }
                DrawRowRangeOverlay(sheet, page.FirstRow + r, new Rect(rect.x, y, rect.width, 20));
            }
        }
        private void Resize(Rect rect, bool horizontal, int control)
        {
            EditorGUIUtility.AddCursorRect(rect, horizontal ? MouseCursor.ResizeHorizontal : MouseCursor.ResizeVertical);
            var e = Event.current;
            if (GUIUtility.hotControl != 0 || e.type != EventType.MouseDown || e.button != 0 || !rect.Contains(e.mousePosition)) return;
            resizeControl = control; resizeSidebar = horizontal;
            resizeStartMouse = e.mousePosition; resizeStartSize = horizontal ? sidebar : plotHeight;
            GUIUtility.hotControl = control; e.Use();
        }
        private void ContinueResize()
        {
            if (resizeControl == 0) return;
            if (GUIUtility.hotControl != resizeControl) { resizeControl = 0; return; }
            var e = Event.current;
            if (e.type == EventType.MouseDrag)
            {
                if (resizeSidebar) sidebar = Mathf.Clamp(resizeStartSize + e.mousePosition.x - resizeStartMouse.x, 220, Math.Max(220, position.width - 400));
                else plotHeight = Mathf.Clamp(resizeStartSize + resizeStartMouse.y - e.mousePosition.y, 100, Math.Max(100, position.height - 240));
                e.Use(); Repaint();
            }
            else if (e.type == EventType.MouseUp && e.button == 0)
            { GUIUtility.hotControl = 0; resizeControl = 0; e.Use(); Repaint(); }
        }
    }
}
