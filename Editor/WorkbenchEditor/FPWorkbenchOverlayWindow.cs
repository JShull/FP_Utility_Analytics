// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using UnityEditor;
    using UnityEngine;

    /// <summary>Shared-source multi-series snapshot viewer; all visible series publish together.</summary>
    public sealed class FPWorkbenchOverlayWindow : EditorWindow
    {
        private FPWorkbenchPresentationUndo presentationUndo;
        private static readonly string[] AxisChoices = { "Left Y", "Right Y" };
        [SerializeField] private FPWorkbenchOverlayAxes axes = new FPWorkbenchOverlayAxes();
        private FPWorkbenchPresentationUndo rightPresentationUndo;
        private FPWorkbenchAxisAssignmentUndo axisAssignmentUndo;
        [SerializeField] private bool showFigureSettings;
        [SerializeField] private string workbook, cache, datasetId, corrections;
        [SerializeField] private string figureId = Guid.NewGuid().ToString("N");
        internal string WorkbookId => workbook;
        internal string FigureId => figureId;
        [SerializeField] private FPWorkbenchSeriesBinding appliedBinding;
        [SerializeField] private FPWorkbenchSeriesBinding binding = new FPWorkbenchSeriesBinding();
        [SerializeField] private FPWorkbenchPlotSettings settings = new FPWorkbenchPlotSettings();
        [SerializeField] private List<int> columns = new List<int>();
        [SerializeField] private List<string> derivedColumns = new List<string>();
        [SerializeField] private string calculations;
        [SerializeField] private Color[] colors = Array.Empty<Color>();
        [SerializeField] private int selectedColumn;
        private FPWorkbenchCsvDataset dataset;
        private FPWorkbenchChangeSnapshot snapshot;
        private string[] names, yNames;
        private FPWorkbenchFormulaProgram program;
        private Task<FPWorkbenchCsvDataset> opening;
        private Task<FPWorkbenchOverlay> building;
        private Task<FPWorkbenchOverlayView> querying;
        private FPWorkbenchOverlay overlay;
        private FPWorkbenchOverlayView view;
        private CancellationTokenSource lifetime, queryCancellation;
        private double requestedStart, requestedEnd;
        private string error;
        private bool Busy => opening != null || building != null || querying != null;
        public static FPWorkbenchOverlayWindow Open(string workbookId, FPWorkbenchCsvDataset source, FPWorkbenchSeriesBinding commonX, int yColumn, FPWorkbenchChangeSnapshot changes = null, FPWorkbenchFormulaProgram calculationProgram = null, string derivedId = null)
        {
            if (source == null || commonX == null || yColumn < 0 || yColumn >= source.ColumnCount || changes != null && changes.DatasetId != source.DatasetId) throw new ArgumentException("Invalid overlay source/binding.");
            var validated = commonX.Copy(source.ColumnCount);
            validated.YDerivedId = "";
            if (calculationProgram != null && calculationProgram.DatasetId != source.DatasetId) throw new ArgumentException("Overlay formula source differs.");
            validated.ResolveDerivedX(calculationProgram);
            int selected = string.IsNullOrEmpty(derivedId) ? yColumn : source.ColumnCount + new FPWorkbenchSeriesBinding { YDerivedId = derivedId }.ResolveDerivedY(calculationProgram);
            var window = CreateInstance<FPWorkbenchOverlayWindow>(); window.workbook = workbookId; window.cache = source.CacheDirectory;
            window.datasetId = source.DatasetId; window.binding = validated; window.columns.Add(selected); window.derivedColumns.Add(derivedId ?? "");
            window.calculations = calculationProgram == null ? null : JsonUtility.ToJson(calculationProgram.Definition);
            window.colors = new[] { SeriesColor(selected) }; window.corrections = changes?.Json;
            window.Attach(source); window.BeginBuild(); FPDataWorkbenchWindow.RememberFigure(workbookId, window.DescribeFigure());
            window.titleContent = new GUIContent("Multi-series Figure"); window.Show(); return window;
        }
        private static Color SeriesColor(int column) => Color.HSVToRGB((column * .618034f) % 1, .65f, .95f);
        private void OnEnable()
        {
            minSize = new Vector2(600, 420); lifetime = new CancellationTokenSource(); EditorApplication.update += Tick;
            if (!string.IsNullOrEmpty(cache)) opening = FPWorkbenchCsvDataset.OpenAsync(cache, lifetime.Token);
        }
        private void OnDisable()
        {
            axisAssignmentUndo?.Dispose(); axisAssignmentUndo = null;
            rightPresentationUndo?.Dispose(); rightPresentationUndo = null;
            presentationUndo?.Dispose(); presentationUndo = null;
            if (!string.IsNullOrEmpty(datasetId)) FPDataWorkbenchWindow.RememberFigure(workbook, DescribeFigure());
            EditorApplication.update -= Tick; lifetime?.Cancel(); lifetime?.Dispose(); lifetime = null;
            queryCancellation?.Cancel(); queryCancellation?.Dispose(); queryCancellation = null;
            Observe(opening); Observe(building); Observe(querying); opening = null; building = null; querying = null; overlay = null; view = null;
        }
        internal FPWorkbenchFigureDocument DescribeFigure() => new FPWorkbenchFigureDocument
        {
            Id = figureId, Name = string.IsNullOrEmpty(settings.Title) ? "Multi-series figure" : settings.Title,
            DatasetId = datasetId, Kind = FPWorkbenchFigureKind.Overlay, Binding = appliedBinding ?? binding, Plot = settings, Axes = axes,
            YColumns = PhysicalColumns(), YDerivedIds = derivedColumns.ToArray(), Colors = (Color[])colors.Clone(),
            HasCalculationSnapshot = !string.IsNullOrEmpty(calculations),
            CalculationSnapshot = string.IsNullOrEmpty(calculations) ? null : JsonUtility.FromJson<FPWorkbenchFormulaSet>(calculations),
            HasCorrectionSnapshot = !string.IsNullOrEmpty(corrections),
            CorrectionSnapshot = string.IsNullOrEmpty(corrections) ? null : JsonUtility.FromJson<FPWorkbenchChangeSet>(corrections)
        };
        internal static FPWorkbenchOverlayWindow OpenSaved(string workbookId, FPWorkbenchCsvDataset source, FPWorkbenchFigureDocument saved)
        {
            saved.Validate(); var window = CreateInstance<FPWorkbenchOverlayWindow>(); window.workbook = workbookId; window.figureId = saved.Id;
            window.cache = source.CacheDirectory; window.datasetId = saved.DatasetId; window.binding = saved.Binding.Copy(source.ColumnCount);
            window.axes = JsonUtility.FromJson<FPWorkbenchOverlayAxes>(JsonUtility.ToJson(saved.Axes));
            window.settings = JsonUtility.FromJson<FPWorkbenchPlotSettings>(JsonUtility.ToJson(saved.Plot)); window.columns = new List<int>(saved.YColumns);
            window.derivedColumns = new List<string>(saved.YDerivedIds); window.calculations = saved.HasCalculationSnapshot ? JsonUtility.ToJson(saved.CalculationSnapshot) : null;
            window.colors = (Color[])saved.Colors.Clone(); window.corrections = saved.CorrectionSnapshot == null ? null : JsonUtility.ToJson(saved.CorrectionSnapshot);
            window.Attach(source); window.BeginBuild(); window.titleContent = new GUIContent(saved.Name); window.Show(); return window;
        }
        private static void Observe(Task task) { if (task != null) task.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted); }
        private int[] PhysicalColumns()
        {
            derivedColumns ??= new List<string>();
            var result = columns.ToArray();
            for (int i = 0; i < result.Length; i++) if (i < derivedColumns.Count && !string.IsNullOrEmpty(derivedColumns[i])) result[i] = 0;
            return result;
        }
        private void Attach(FPWorkbenchCsvDataset source)
        {
            if (source.DatasetId != datasetId || columns == null || columns.Count < 1 || columns.Count > 8 || colors == null || colors.Length != columns.Count) throw new InvalidOperationException("Overlay source or saved series metadata changed.");
            program = string.IsNullOrEmpty(calculations) ? null : FPWorkbenchFormulaProgram.Compile(source, JsonUtility.FromJson<FPWorkbenchFormulaSet>(calculations));
            binding.ResolveDerivedX(program);
            derivedColumns ??= new List<string>();
            if (derivedColumns.Count == 0) for (int i = 0; i < columns.Count; i++) derivedColumns.Add("");
            if (derivedColumns.Count != columns.Count) throw new InvalidOperationException("Overlay derived identities differ from its series count.");
            for (int i = 0; i < columns.Count; i++)
            {
                derivedColumns[i] ??= "";
                if (derivedColumns[i].Length > 0) columns[i] = source.ColumnCount + new FPWorkbenchSeriesBinding { YDerivedId = derivedColumns[i] }.ResolveDerivedY(program);
                else if (columns[i] < 0 || columns[i] >= source.ColumnCount) throw new InvalidOperationException("Overlay column is outside the source.");
            }
            axes ??= new FPWorkbenchOverlayAxes(); axes.EnsureCount(columns.Count);
            dataset = source; snapshot = string.IsNullOrEmpty(corrections) ? null : JsonUtility.FromJson<FPWorkbenchChangeSet>(corrections).Snapshot();
            names = new string[source.ColumnCount]; for (int c = 0; c < names.Length; c++) names[c] = (c + 1) + ": " + source.GetColumnName(c);
            yNames = new string[source.ColumnCount + (program?.ColumnCount ?? 0)]; Array.Copy(names, yNames, names.Length);
            if (program != null) for (int c = 0; c < program.ColumnCount; c++) yNames[source.ColumnCount + c] = "ƒ " + program.Name(c);
        }
        private void BeginBuild()
        {
            if (Busy || dataset == null) return;
            try { var next = binding.Copy(dataset.ColumnCount); next.YDerivedId = ""; error = null; view = null; overlay = null; building = FPWorkbenchOverlay.BuildAsync(dataset, next, PhysicalColumns(), snapshot, lifetime.Token, program, derivedColumns.ToArray()); appliedBinding = next; }
            catch (Exception ex) { error = ex.Message; }
        }
        private bool AddColumn(string sourceWorkbook, string sourceDataset, string columnId, int column)
        {
            if (Busy || dataset == null || sourceWorkbook != workbook || sourceDataset != datasetId || column < 0 || column >= dataset.ColumnCount ||
                columnId != dataset.Cell(0, column).ColumnId || columns.Contains(column) || columns.Count >= 8) return false;
            AppendColumn(column, ""); return true;
        }
        private void AppendColumn(int column, string derived)
        { columns.Add(column); derivedColumns.Add(derived); axes.EnsureCount(columns.Count); Array.Resize(ref colors, columns.Count); colors[colors.Length - 1] = SeriesColor(column); BeginBuild(); }
        private int ResolveDerivedColumn(string columnId, string derived)
        {
            if (program == null || string.IsNullOrEmpty(derived) || columnId != datasetId + ":derived:" + derived) return -1;
            for (int i = 0; i < program.ColumnCount; i++) if (program.ColumnId(i) == derived) return dataset.ColumnCount + i;
            return -1;
        }
        private bool AddDerivedColumn(string sourceWorkbook, string sourceDataset, string columnId, string derived)
        {
            int column = ResolveDerivedColumn(columnId, derived);
            if (Busy || dataset == null || sourceWorkbook != workbook || sourceDataset != datasetId || column < 0 || derivedColumns.Contains(derived) || columns.Count >= 8) return false;
            AppendColumn(column, derived); return true;
        }
        private bool CanAcceptDerivedDrop(string owner, string source, string columnId, string derived, string fingerprint)
            => !Busy && dataset != null && owner == workbook && source == datasetId && fingerprint == program?.Fingerprint &&
                columns.Count < 8 && ResolveDerivedColumn(columnId, derived) >= 0 && !derivedColumns.Contains(derived);
        private void Tick()
        {
            bool repaint = false;
            if (opening != null && opening.IsCompleted)
            {
                var task = opening; opening = null;
                try { Attach(task.GetAwaiter().GetResult()); BeginBuild(); } catch (Exception ex) { error = ex.Message; } repaint = true;
            }
            if (building != null && building.IsCompleted)
            {
                var task = building; building = null;
                try { overlay = task.GetAwaiter().GetResult(); if (!settings.HasRange) settings.Fit(overlay.FirstX, overlay.LastX); }
                catch (Exception ex) { error = ex.Message; } repaint = true;
            }
            if (querying != null && querying.IsCompleted)
            {
                var task = querying; querying = null;
                try
                {
                    var candidate = task.GetAwaiter().GetResult();
                    if (requestedStart == settings.StartX && requestedEnd == settings.EndX) { view = candidate; error = null; }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { error = ex.Message; } repaint = true;
            }
            if (querying != null && (settings.StartX != requestedStart || settings.EndX != requestedEnd)) queryCancellation.Cancel();
            if (overlay != null && querying == null && (view == null || view.StartX != settings.StartX || view.EndX != settings.EndX) &&
                (error == null || settings.StartX != requestedStart || settings.EndX != requestedEnd))
            {
                queryCancellation?.Dispose(); queryCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                requestedStart = settings.StartX; requestedEnd = settings.EndX;
                try { querying = overlay.QueryAsync(requestedStart, requestedEnd, 512, queryCancellation.Token); }
                catch (Exception ex) { error = ex.Message; }
                repaint = true;
            }
            if (repaint) Repaint();
        }
        private void OnGUI()
        {
            GUILayout.Label("Shared-source overlay • " + (snapshot == null ? "Original snapshot" : "Effective snapshot"), EditorStyles.boldLabel);
            if (program != null && program.ColumnCount > 0) GUILayout.Label("Calculated series use the frozen formula snapshot.", EditorStyles.miniLabel);
            if (dataset == null) { EditorGUILayout.HelpBox(error ?? "Verifying source…", MessageType.Info); return; }
            using (new EditorGUI.DisabledScope(Busy))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var mode = (FPWorkbenchXMode)EditorGUILayout.EnumPopup("X axis", binding.XMode);
                    if (mode != binding.XMode) { binding.XMode = mode; binding.XDerivedId = ""; }
                    if (binding.XMode == FPWorkbenchXMode.Numeric)
                    {
                        int shown = binding.HasDerivedX ? dataset.ColumnCount + binding.ResolveDerivedX(program) : binding.XColumn;
                        int selected = EditorGUILayout.Popup(shown, yNames);
                        if (selected >= dataset.ColumnCount) { binding.XDerivedId = program.ColumnId(selected - dataset.ColumnCount); binding.XColumn = 0; }
                        else { binding.XDerivedId = ""; binding.XColumn = selected; }
                    }
                    else if (binding.XMode == FPWorkbenchXMode.UtcDateTime) binding.XColumn = EditorGUILayout.Popup(binding.XColumn, names);
                    if (GUILayout.Button("Apply X")) BeginBuild();
                }
                if (binding.XMode == FPWorkbenchXMode.UtcDateTime)
                { binding.DateColumn = EditorGUILayout.Popup("UTC date", binding.DateColumn, names); binding.UtcFormat = EditorGUILayout.TextField("UTC format", binding.UtcFormat); }
                using (new EditorGUILayout.HorizontalScope())
                {
                    selectedColumn = EditorGUILayout.Popup("Add Y", selectedColumn, yNames);
                    if (GUILayout.Button("Add series"))
                    {
                        if (selectedColumn < dataset.ColumnCount) AddColumn(workbook, datasetId, dataset.Cell(0, selectedColumn).ColumnId, selectedColumn);
                        else { string id = program.ColumnId(selectedColumn - dataset.ColumnCount); AddDerivedColumn(workbook, datasetId, datasetId + ":derived:" + id, id); }
                    }
                }
                for (int i = 0; i < columns.Count; i++)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        SetAxisSide(i, EditorGUILayout.Popup(axes.Right[i] ? 1 : 0, AxisChoices, GUILayout.Width(75)) == 1);
                        colors[i] = EditorGUILayout.ColorField(colors[i], GUILayout.Width(55));
                        GUILayout.Label(new GUIContent(yNames[columns[i]], derivedColumns[i].Length == 0 ? "Physical source column" : "Frozen formula: " + program.Expression(columns[i] - dataset.ColumnCount)));
                        using (new EditorGUI.DisabledScope(columns.Count <= 1))
                            if (GUILayout.Button("Remove", GUILayout.Width(70)))
                            { columns.RemoveAt(i); derivedColumns.RemoveAt(i); var sides = new List<bool>(axes.Right); sides.RemoveAt(i); axes.Right = sides.ToArray(); var next = new List<Color>(colors); next.RemoveAt(i); colors = next.ToArray(); BeginBuild(); break; }
                    }
                }
            }
            presentationUndo ??= new FPWorkbenchPresentationUndo(Repaint);
            presentationUndo.Begin(settings);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Fit X", EditorStyles.toolbarButton) && overlay != null) settings.Fit(overlay.FirstX, overlay.LastX);
                FPWorkbenchPlotView.StyleControls(settings, true);
                settings.Grid = GUILayout.Toggle(settings.Grid, "Grid", EditorStyles.toolbarButton);
                settings.InvertY = GUILayout.Toggle(settings.InvertY, "Invert Y", EditorStyles.toolbarButton);
                bool wasLocked = settings.LockY;
                settings.LockY = GUILayout.Toggle(settings.LockY, "Lock Y", EditorStyles.toolbarButton);
                if (!wasLocked && settings.LockY && view != null && !double.IsNaN(view.Minimum))
                { var range = axes.Resolve(view, settings, false, false); if (range.HasValues) { settings.MinimumY = range.Minimum; settings.MaximumY = range.Maximum; } }
                if (GUILayout.Button("Fit Y", EditorStyles.toolbarButton) && view != null && !double.IsNaN(view.Minimum))
                { var range = axes.Resolve(view, settings, false, false); if (range.HasValues) { settings.MinimumY = range.Minimum; settings.MaximumY = range.Maximum; } }
            }
            showFigureSettings = EditorGUILayout.Foldout(showFigureSettings, "Figure settings", true);
            if (showFigureSettings)
            {
                FPWorkbenchPlotView.LabelControls(settings);
                FPWorkbenchPlotView.StyleControls(settings);
                settings.XTickSpacing = EditorGUILayout.DoubleField("X grid spacing", settings.XTickSpacing);
                settings.YTickSpacing = EditorGUILayout.DoubleField("Y grid spacing", settings.YTickSpacing);
            }
            if (settings.LockY)
            { using (new EditorGUILayout.HorizontalScope()) { settings.MinimumY = EditorGUILayout.DoubleField("Y minimum", settings.MinimumY); settings.MaximumY = EditorGUILayout.DoubleField("Y maximum", settings.MaximumY); } }
            presentationUndo.End();
            if (Array.Exists(axes.Right, side => side))
            {
                rightPresentationUndo ??= new FPWorkbenchPresentationUndo(Repaint); rightPresentationUndo.Begin(axes.RightSettings);
                var right = axes.RightSettings;
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("Right Y", GUILayout.Width(55)); bool locked = right.LockY;
                    right.LockY = GUILayout.Toggle(right.LockY, "Lock limits"); right.InvertY = GUILayout.Toggle(right.InvertY, "Invert"); right.Grid = GUILayout.Toggle(right.Grid, "Grid");
                    bool fit = GUILayout.Button("Fit right Y");
                    if (view != null && (fit || !locked && right.LockY)) { var range = axes.Resolve(view, settings, true, false); if (range.HasValues) { right.MinimumY = range.Minimum; right.MaximumY = range.Maximum; } }
                }
                right.YLabel = EditorGUILayout.TextField("Right Y label", right.YLabel);
                right.YTickSpacing = EditorGUILayout.DoubleField("Right grid spacing", right.YTickSpacing);
                if (right.LockY) { right.MinimumY = EditorGUILayout.DoubleField("Right minimum", right.MinimumY); right.MaximumY = EditorGUILayout.DoubleField("Right maximum", right.MaximumY); }
                rightPresentationUndo.End();
            }
            if (Busy) GUILayout.Label("Updating all series in background…", EditorStyles.miniLabel);
            if (error != null) EditorGUILayout.HelpBox(error, MessageType.Warning);
            Rect plot = GUILayoutUtility.GetRect(100, 10000, 100, 10000, GUILayout.ExpandHeight(true));
            FPWorkbenchPlotView.Draw(plot, view?.Series[0], settings, "Shared X • source units unchanged", view, colors, axes);
            if (view != null && (view.StartX != settings.StartX || view.EndX != settings.EndX)) GUI.Label(new Rect(plot.x + 80, plot.y + 23, plot.width - 90, 20), "Updating viewport (previous range shown)", EditorStyles.miniLabel);
            HandleDrop(plot);
        }
        private void SetAxisSide(int index, bool right)
        {
            if (axes.Right[index] == right) return;
            axisAssignmentUndo ??= new FPWorkbenchAxisAssignmentUndo(() =>
            {
                var ids = new string[columns.Count];
                for (int i = 0; i < ids.Length; i++) ids[i] = derivedColumns[i].Length == 0 ? dataset.Cell(0, columns[i]).ColumnId : datasetId + ":derived:" + derivedColumns[i];
                return ids;
            }, () => axes, Repaint);
            axisAssignmentUndo.SetSide(index, right);
        }
        private void HandleDrop(Rect plot)
        {
            if (!(DragAndDrop.GetGenericData(FPWorkbenchColumnDragPayload.Key) is FPWorkbenchColumnDragPayload payload) || !plot.Contains(Event.current.mousePosition)) return;
            var e = Event.current;
            bool derived = !string.IsNullOrEmpty(payload.DerivedId);
            bool compatibleFormula = !derived || payload.FormulaFingerprint == program?.Fingerprint;
            bool accepted = !Busy && compatibleFormula && payload.Workbook == workbook && payload.Dataset == datasetId && columns.Count < 8 && (derived
                ? CanAcceptDerivedDrop(payload.Workbook, payload.Dataset, payload.ColumnId, payload.DerivedId, payload.FormulaFingerprint)
                : payload.Column >= 0 && payload.Column < dataset.ColumnCount && payload.ColumnId == dataset.Cell(0, payload.Column).ColumnId && !columns.Contains(payload.Column));
            GUI.Box(new Rect(plot.x + 80, plot.y + 45, Math.Max(1, plot.width - 160), 30), accepted ? "Drop to append Y series" : !compatibleFormula ? "Formula snapshot differs; reopen figure for current definitions." : "Same-source, distinct Y required (maximum 8); wait for current work");
            if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform) return;
            DragAndDrop.visualMode = accepted ? DragAndDropVisualMode.Link : DragAndDropVisualMode.Rejected;
            if (e.type == EventType.DragPerform)
            {
                if (accepted)
                {
                    DragAndDrop.AcceptDrag();
                    if (derived) AddDerivedColumn(payload.Workbook, payload.Dataset, payload.ColumnId, payload.DerivedId);
                    else AddColumn(payload.Workbook, payload.Dataset, payload.ColumnId, payload.Column);
                }
                DragAndDrop.SetGenericData(FPWorkbenchColumnDragPayload.Key, null);
            }
            e.Use(); Repaint();
        }
    }
}
