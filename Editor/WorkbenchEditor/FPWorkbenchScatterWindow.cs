// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using UnityEditor;
    using UnityEngine;

    /// <summary>Bounded unsorted XY snapshot viewer; exact source identities survive display decimation.</summary>
    public sealed partial class FPWorkbenchScatterWindow : EditorWindow
    {
        [Serializable] private sealed class AlignedOrigin
        {
            public string Owner, Left, Right;
            public int[] LeftColumns, RightColumns;
            public bool LeftEffective, RightEffective;
            public long Matched, UnmatchedLeft, UnmatchedRight, AmbiguousLeft, AmbiguousRight;
        }
        [SerializeField] private bool hasAlignedRecipe;
        [SerializeField] private FPWorkbenchAlignedFigureRecipe alignedRecipe;
        [SerializeField] private FPWorkbenchSourceDocument alignedLeftReference, alignedRightReference;
        private Task<FPWorkbenchAlignedValues> regenerating;
        private Func<Task<FPWorkbenchAlignedValues>> pendingRegenerating;
        private FPWorkbenchEditorResources.ScanReservation regenerationMemory;
        private long regenerationBytes;
        [SerializeField] private bool hasAlignedOrigin;
        [SerializeField] private AlignedOrigin alignedOrigin;
        private Task<FPWorkbenchPage> revealing;
        private Func<Task<FPWorkbenchPage>> pendingRevealing;
        private FPWorkbenchMemoryBudget.Reservation revealMemory;
        private long revealBytes;
        private bool revealLeft;
        [SerializeField] private string lastFigureMetadata;
        [SerializeField] private string figureId = Guid.NewGuid().ToString("N");
        internal string WorkbookId => workbookId;
        internal string FigureId => figureId;
        [SerializeField] private string cacheDirectory, datasetId, workbookId, calculations, corrections;
        [SerializeField] private int xColumn, yColumn, maximumPoints = 10000;
        [SerializeField] private FPWorkbenchInvalidPolicy invalidPolicy = FPWorkbenchInvalidPolicy.Reject;
        [SerializeField] private FPWorkbenchPlotSettings settings = new FPWorkbenchPlotSettings { Style = FPWorkbenchPlotStyle.Points, MarkerSize = 3 };
        [SerializeField] private Color pointColor = new Color(0, .7f, 1);
        [SerializeField] private bool showSettings;
        private FPWorkbenchCsvDataset dataset;
        private FPWorkbenchFormulaProgram program;
        private FPWorkbenchChangeSnapshot changes;
        private string[] names;
        private Task<FPWorkbenchCsvDataset> opening;
        [SerializeField] private FPWorkbenchCsvProfile sourceProfile;
        private bool pendingOpening;
        private FPWorkbenchEditorResources.ScanReservation openingMemory;
        private Task<FPWorkbenchPointCache> building;
        private Func<Task<FPWorkbenchPointCache>> pendingBuild;
        private FPWorkbenchEditorResources.ScanReservation buildMemory;
        private Task<FPWorkbenchPointView> querying;
        private FPWorkbenchMemoryBudget.Reservation queryMemory, viewMemory;
        private bool windowActive, waitingForMemory;
        private const long QueryReservationBytes = 16L * 1024 * 1024;
        private FPWorkbenchPointCache cache;
        private FPWorkbenchPointView view;
        private CancellationTokenSource lifetime, queryCancellation;
        private FPWorkbenchPresentationUndo presentationUndo;
        private FPWorkbenchSpatialBounds requested;
        private int requestedBudget, completedBudget, buildX, buildY, appliedX, appliedY;
        private FPWorkbenchInvalidPolicy buildPolicy, appliedPolicy;
        private bool fitAfterBuild, failedQuery, selected;
        private FPWorkbenchSpatialPoint selection;
        private string error;
        private bool Busy => opening != null || pendingOpening || building != null || pendingBuild != null || querying != null || regenerating != null || pendingRegenerating != null || waitingForMemory;
        public static FPWorkbenchScatterWindow Open(string workbook, FPWorkbenchCsvDataset source, int x, int y,
            FPWorkbenchFormulaProgram formulas = null, FPWorkbenchChangeSnapshot edits = null, FPWorkbenchInvalidPolicy invalidPolicy = FPWorkbenchInvalidPolicy.Reject)
        {
            var schema = new FPWorkbenchTableView(source, formulas); schema.ColumnId(x); schema.ColumnId(y);
            if (edits != null && edits.DatasetId != source.DatasetId) throw new ArgumentException("Scatter corrections belong to another source.");
            var window = CreateInstance<FPWorkbenchScatterWindow>(); window.workbookId = workbook; window.cacheDirectory = source.CacheDirectory;
            window.datasetId = source.DatasetId; window.invalidPolicy = invalidPolicy; window.xColumn = x; window.yColumn = y;
            window.calculations = formulas == null ? null : JsonUtility.ToJson(formulas.Definition); window.corrections = edits?.Json;
            window.Attach(source); window.BeginBuild(true); FPDataWorkbenchWindow.RememberFigure(workbook, window.DescribeFigure()); window.titleContent = new GUIContent("Scatter Figure"); window.Show(); return window;
        }
        internal static FPWorkbenchScatterWindow OpenAligned(string owner, FPWorkbenchAlignedValues values)
        {
            var metadata = values.Metadata;
            var window = Open(null, values.Dataset, 2, 3, invalidPolicy: metadata.InvalidPolicy == FPWorkbenchExportInvalidPolicy.EmptyField ? FPWorkbenchInvalidPolicy.Skip : FPWorkbenchInvalidPolicy.Reject);
            window.workbookId = owner; window.hasAlignedRecipe = true; window.alignedRecipe = FPWorkbenchAlignedFigureRecipe.FromValues(metadata);
            window.alignedLeftReference = values.LeftReference; window.alignedRightReference = values.RightReference;
            window.hasAlignedOrigin = true;
            window.alignedOrigin = new AlignedOrigin { Owner = owner, Left = metadata.Alignment.LeftDatasetId, Right = metadata.Alignment.RightDatasetId,
                LeftColumns = metadata.LeftSourceColumns, RightColumns = metadata.RightSourceColumns, LeftEffective = metadata.Alignment.HasLeftCorrectionSnapshot, RightEffective = metadata.Alignment.HasRightCorrectionSnapshot,
                Matched = metadata.Alignment.MatchedPairs, UnmatchedLeft = metadata.Alignment.UnmatchedLeftRows, UnmatchedRight = metadata.Alignment.UnmatchedRightRows,
                AmbiguousLeft = metadata.Alignment.AmbiguousLeftRows, AmbiguousRight = metadata.Alignment.AmbiguousRightRows };
            window.settings.XLabel = metadata.LeftColumnName; window.settings.YLabel = metadata.RightColumnName;
            window.settings.Title = metadata.RightColumnName + " vs " + metadata.LeftColumnName + " (aligned)"; window.titleContent = new GUIContent("Aligned Scatter"); FPDataWorkbenchWindow.RememberFigure(owner, window.DescribeFigure()); return window;
        }
        internal FPWorkbenchFigureDocument DescribeFigure()
        {
            if (hasAlignedRecipe)
            {
                var figure = new FPWorkbenchFigureDocument { Id = figureId, Name = string.IsNullOrEmpty(settings.Title) ? "Aligned scatter" : settings.Title,
                    DatasetId = alignedRecipe.LeftDatasetId, Kind = FPWorkbenchFigureKind.AlignedScatter, AlignedRecipe = alignedRecipe,
                    Plot = settings, PointBudget = maximumPoints, Colors = new[] { pointColor }, InvalidPolicy = cache == null ? invalidPolicy : appliedPolicy,
                    Binding = new FPWorkbenchSeriesBinding { XMode = FPWorkbenchXMode.Numeric } };
                lastFigureMetadata = JsonUtility.ToJson(figure); return figure;
            }
            if (dataset == null)
            {
                if (string.IsNullOrEmpty(lastFigureMetadata)) throw new InvalidOperationException("Scatter source has not been admitted yet.");
                return JsonUtility.FromJson<FPWorkbenchFigureDocument>(lastFigureMetadata);
            }
            int x = cache == null ? xColumn : appliedX, y = cache == null ? yColumn : appliedY;
            var binding = new FPWorkbenchSeriesBinding { XMode = FPWorkbenchXMode.Numeric,
                XColumn = x < dataset.ColumnCount ? x : 0, YColumn = y < dataset.ColumnCount ? y : 0,
                XDerivedId = x < dataset.ColumnCount ? "" : program.ColumnId(x - dataset.ColumnCount),
                YDerivedId = y < dataset.ColumnCount ? "" : program.ColumnId(y - dataset.ColumnCount) };
            var result = new FPWorkbenchFigureDocument { Id = figureId, Name = string.IsNullOrEmpty(settings.Title) ? "Scatter figure" : settings.Title,
                DatasetId = datasetId, Kind = FPWorkbenchFigureKind.Scatter, Binding = binding, Plot = settings, PointBudget = maximumPoints,
                Colors = new[] { pointColor }, InvalidPolicy = cache == null ? invalidPolicy : appliedPolicy,
                HasCalculationSnapshot = !string.IsNullOrEmpty(calculations), CalculationSnapshot = program?.Definition,
                HasCorrectionSnapshot = changes != null, CorrectionSnapshot = changes == null ? null : JsonUtility.FromJson<FPWorkbenchChangeSet>(changes.Json) };
            lastFigureMetadata = JsonUtility.ToJson(result); return result;
        }
        internal static FPWorkbenchScatterWindow OpenSaved(string owner, FPWorkbenchCsvDataset source, FPWorkbenchFigureDocument figure)
        {
            figure.Validate();
            if (figure.Kind != FPWorkbenchFigureKind.Scatter || source.DatasetId != figure.DatasetId) throw new ArgumentException("Scatter source/kind differs.");
            var formulas = figure.HasCalculationSnapshot ? FPWorkbenchFormulaProgram.Compile(source, figure.CalculationSnapshot) : null;
            var binding = figure.Binding.Copy(source.ColumnCount);
            int x = binding.HasDerivedX ? source.ColumnCount + binding.ResolveDerivedX(formulas) : binding.XColumn;
            int y = binding.HasDerivedY ? source.ColumnCount + binding.ResolveDerivedY(formulas) : binding.YColumn;
            var window = CreateInstance<FPWorkbenchScatterWindow>(); window.workbookId = owner; window.figureId = figure.Id;
            window.cacheDirectory = source.CacheDirectory; window.datasetId = source.DatasetId; window.xColumn = x; window.yColumn = y;
            window.calculations = formulas == null ? null : JsonUtility.ToJson(formulas.Definition);
            window.corrections = figure.HasCorrectionSnapshot ? JsonUtility.ToJson(figure.CorrectionSnapshot) : null;
            window.settings = JsonUtility.FromJson<FPWorkbenchPlotSettings>(JsonUtility.ToJson(figure.Plot));
            window.maximumPoints = figure.PointBudget; window.pointColor = figure.Colors[0]; window.invalidPolicy = figure.InvalidPolicy;
            window.Attach(source); window.DescribeFigure(); window.BeginBuild(!window.settings.HasRange); window.titleContent = new GUIContent(figure.Name); window.Show(); return window;
        }
        internal static FPWorkbenchScatterWindow OpenSavedAligned(string owner, FPWorkbenchCsvDataset left, FPWorkbenchCsvDataset right, FPWorkbenchFigureDocument figure)
        {
            figure.Validate(); if (figure.Kind != FPWorkbenchFigureKind.AlignedScatter) throw new ArgumentException("Expected an aligned scatter recipe.");
            figure.AlignedRecipe.ValidateSources(left, right);
            var window = CreateInstance<FPWorkbenchScatterWindow>(); window.workbookId = owner; window.figureId = figure.Id;
            window.hasAlignedRecipe = true; window.alignedRecipe = JsonUtility.FromJson<FPWorkbenchAlignedFigureRecipe>(JsonUtility.ToJson(figure.AlignedRecipe));
            window.alignedLeftReference = Describe(left); window.alignedRightReference = Describe(right);
            window.settings = JsonUtility.FromJson<FPWorkbenchPlotSettings>(JsonUtility.ToJson(figure.Plot)); window.maximumPoints = figure.PointBudget; window.pointColor = figure.Colors[0]; window.invalidPolicy = figure.InvalidPolicy;
            window.DescribeFigure();
            var recipe = window.alignedRecipe; var token = window.lifetime.Token;
            window.BeginRegeneration(() => recipe.BuildAsync(left, right, token), left.ImportProfile, right.ImportProfile);
            window.titleContent = new GUIContent(figure.Name); window.Show(); return window;
        }
        private static FPWorkbenchSourceDocument Describe(FPWorkbenchCsvDataset source) => new FPWorkbenchSourceDocument { DatasetId = source.DatasetId, SourcePath = source.SourcePath, CacheDirectory = source.CacheDirectory, Import = source.ImportProfile, Name = System.IO.Path.GetFileNameWithoutExtension(source.SourcePath) };
        private static async Task<FPWorkbenchAlignedValues> Regenerate(FPWorkbenchAlignedFigureRecipe recipe, FPWorkbenchSourceDocument left, FPWorkbenchSourceDocument right, CancellationToken token)
        {
            async Task<FPWorkbenchCsvDataset> Admit(FPWorkbenchSourceDocument reference)
            {
                reference.Validate(); var data = await FPWorkbenchCsvDataset.OpenAsync(reference.CacheDirectory, token).ConfigureAwait(false);
                var comparison = System.IO.Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (data.DatasetId != reference.DatasetId || !string.Equals(System.IO.Path.GetFullPath(data.SourcePath), System.IO.Path.GetFullPath(reference.SourcePath), comparison) || JsonUtility.ToJson(data.ImportProfile) != JsonUtility.ToJson(reference.Import)) throw new InvalidOperationException("Aligned original source identity/path/profile differs.");
                return data;
            }
            var a = await Admit(left).ConfigureAwait(false); var b = await Admit(right).ConfigureAwait(false);
            return await recipe.BuildAsync(a, b, token).ConfigureAwait(false);
        }
        private void BeginRegeneration(Func<Task<FPWorkbenchAlignedValues>> request, FPWorkbenchCsvProfile a, FPWorkbenchCsvProfile b)
        {
            regenerationBytes = FPWorkbenchEditorResources.ProfileBytes(a, FPWorkbenchEditorResources.ProfileBytes(b, 32L * 1024 * 1024));
            pendingRegenerating = request; TryStartRegeneration();
        }
        private void TryStartRegeneration()
        {
            try { waitingForMemory = !FPWorkbenchEditorResources.TryStartScan(regenerationBytes, ref pendingRegenerating, ref regenerating, ref regenerationMemory); }
            catch (Exception ex) { waitingForMemory = false; error = ex.Message; }
        }
        private void AttachRegenerated(FPWorkbenchAlignedValues values)
        {
            var m = values.Metadata; cacheDirectory = values.Dataset.CacheDirectory; datasetId = values.Dataset.DatasetId;
            xColumn = 2; yColumn = 3; calculations = null; corrections = null;
            alignedLeftReference = values.LeftReference; alignedRightReference = values.RightReference; hasAlignedOrigin = true;
            alignedOrigin = new AlignedOrigin { Owner = workbookId, Left = m.Alignment.LeftDatasetId, Right = m.Alignment.RightDatasetId,
                LeftColumns = m.LeftSourceColumns, RightColumns = m.RightSourceColumns, LeftEffective = m.Alignment.HasLeftCorrectionSnapshot, RightEffective = m.Alignment.HasRightCorrectionSnapshot,
                Matched = m.Alignment.MatchedPairs, UnmatchedLeft = m.Alignment.UnmatchedLeftRows, UnmatchedRight = m.Alignment.UnmatchedRightRows, AmbiguousLeft = m.Alignment.AmbiguousLeftRows, AmbiguousRight = m.Alignment.AmbiguousRightRows };
            Attach(values.Dataset); BeginBuild(!settings.HasRange);
        }
        private void OnEnable()
        {
            windowActive = true;
            minSize = new Vector2(560, 420); wantsMouseMove = true; lifetime = new CancellationTokenSource(); EditorApplication.update += Tick;
            if (!hasAlignedRecipe) { alignedRecipe = null; alignedLeftReference = null; alignedRightReference = null; }
            else
            {
                try
                {
                    if (alignedRecipe == null || alignedLeftReference == null || alignedRightReference == null) throw new InvalidOperationException("Aligned recipe/original source references are missing.");
                    alignedRecipe.Validate(); var recipe = alignedRecipe; var a = alignedLeftReference; var b = alignedRightReference; var token = lifetime.Token;
                    BeginRegeneration(() => Regenerate(recipe, a, b, token), a.Import, b.Import);
                }
                catch (Exception ex) { error = ex.Message; }
                return;
            }
            if (!hasAlignedOrigin) alignedOrigin = null;
            else if (alignedOrigin == null || string.IsNullOrEmpty(alignedOrigin.Left) || string.IsNullOrEmpty(alignedOrigin.Right) || alignedOrigin.LeftColumns == null || alignedOrigin.RightColumns == null)
            { error = "Aligned source-row provenance is missing; regenerate this plot from its alignment."; return; }
            pendingOpening = !string.IsNullOrEmpty(cacheDirectory);
        }
        private void OnDisable()
        {
            windowActive = false;
            imageJob?.Dispose(); imageJob = null;
            try { if (dataset != null || hasAlignedRecipe) FPDataWorkbenchWindow.RememberFigure(workbookId, DescribeFigure()); }
            catch (Exception ex) { error = "Figure settings not retained: " + ex.Message; }
            EditorApplication.update -= Tick; presentationUndo?.Dispose(); presentationUndo = null;
            lifetime?.Cancel(); lifetime?.Dispose(); lifetime = null;
            queryCancellation?.Cancel(); queryCancellation?.Dispose(); queryCancellation = null;
            if (regenerationMemory != null) regenerationMemory.ReleaseAfter(regenerating); else Observe(regenerating);
            regenerationMemory = null; pendingRegenerating = null; regenerating = null;
            if (revealMemory != null) revealMemory.ReleaseAfter(revealing); else Observe(revealing);
            revealMemory = null; pendingRevealing = null; revealing = null;
            if (openingMemory != null) openingMemory.ReleaseAfter(opening); else Observe(opening);
            openingMemory = null; pendingOpening = false;
            if (buildMemory != null) buildMemory.ReleaseAfter(building); else Observe(building);
            buildMemory = null; pendingBuild = null;
            if (queryMemory != null) queryMemory.ReleaseAfter(querying); else Observe(querying);
            queryMemory = null; opening = null; building = null; querying = null;
            dataset = null; cache = null; ClearView(); waitingForMemory = false;
        }
        private static void Observe(Task task) { if (task != null) task.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted); }
        private void Attach(FPWorkbenchCsvDataset source)
        {
            sourceProfile = source.ImportProfile;
            if (source.DatasetId != datasetId) throw new InvalidOperationException("Scatter source revision changed.");
            program = string.IsNullOrEmpty(calculations) ? null : FPWorkbenchFormulaProgram.Compile(source, JsonUtility.FromJson<FPWorkbenchFormulaSet>(calculations));
            changes = string.IsNullOrEmpty(corrections) ? null : JsonUtility.FromJson<FPWorkbenchChangeSet>(corrections).Snapshot();
            if (changes != null && changes.DatasetId != datasetId) throw new InvalidOperationException("Scatter correction source changed.");
            var schema = new FPWorkbenchTableView(source, program); schema.ColumnId(xColumn); schema.ColumnId(yColumn);
            names = new string[schema.ColumnCount]; for (int c = 0; c < names.Length; c++) names[c] = (c >= source.ColumnCount ? "ƒ " : "") + schema.ColumnName(c);
            dataset = source;
        }
        private void BeginBuild(bool fit)
        {
            if (dataset == null || building != null || pendingBuild != null) return;
            try
            {
                queryCancellation?.Cancel();
                if (queryMemory != null) queryMemory.ReleaseAfter(querying); else Observe(querying);
                queryMemory = null; querying = null; waitingForMemory = false;
                buildX = xColumn; buildY = yColumn; buildPolicy = invalidPolicy; fitAfterBuild = fit; error = null;
                var source = dataset; var columns = new[] { buildX, buildY }; var formulas = program; var edits = changes;
                var policy = invalidPolicy; var token = lifetime.Token;
                pendingBuild = () => FPWorkbenchPointCache.BuildAsync(source, columns, formulas, edits, policy, token);
                waitingForMemory = !FPWorkbenchEditorResources.TryStartSource(dataset, ref pendingBuild, ref building, ref buildMemory);
            }
            catch (Exception ex) { error = ex.Message; }
        }
        private void Fit()
        {
            if (cache == null || !cache.HasPoints) return;
            double Pad(double value) => Math.Max(1, Math.Abs(value) * 1e-9);
            var b = cache.Bounds;
            double left = b.MinimumX, right = b.MaximumX, bottom = b.MinimumY, top = b.MaximumY;
            if (left == right) { left -= Pad(left); right += Pad(right); }
            if (bottom == top) { bottom -= Pad(bottom); top += Pad(top); }
            ValidateViewport(left, right, bottom, top);
            settings.StartX = left; settings.EndX = right; settings.MinimumY = bottom; settings.MaximumY = top; settings.HasRange = true;
        }
        private static FPWorkbenchSpatialBounds ValidateViewport(double left, double right, double bottom, double top)
        {
            var b = new FPWorkbenchSpatialBounds(left, right, bottom, top);
            if (right <= left || top <= bottom || double.IsInfinity(right - left) || double.IsInfinity(top - bottom)) throw new ArgumentException("Choose finite, nonzero X/Y display spans.");
            return b;
        }
        private FPWorkbenchSpatialBounds RequestedBounds() => ValidateViewport(settings.StartX, settings.EndX, settings.MinimumY, settings.MaximumY);
        private void Tick()
        {
            if (!windowActive) return;
            bool repaint = false;
            if (pendingRevealing != null) { TryStartReveal(); repaint = true; }
            if (pendingOpening) {
                try { pendingOpening = !FPWorkbenchEditorResources.TryOpenCache(cacheDirectory, sourceProfile, lifetime.Token, ref opening, ref openingMemory); waitingForMemory = pendingOpening; }
                catch (Exception ex) { pendingOpening = false; waitingForMemory = false; error = ex.Message; }
                repaint = true;
            }
            if (pendingRegenerating != null) { TryStartRegeneration(); repaint = true; }
            if (pendingBuild != null) {
                try { waitingForMemory = !FPWorkbenchEditorResources.TryStartSource(dataset, ref pendingBuild, ref building, ref buildMemory); }
                catch (Exception ex) { waitingForMemory = false; error = ex.Message; }
                repaint = true;
            }
            if (regenerating != null && regenerating.IsCompleted)
            {
                var work = regenerating; regenerating = null;
                regenerationMemory?.Dispose(); regenerationMemory = null;
                try { AttachRegenerated(work.GetAwaiter().GetResult()); } catch (Exception ex) { error = ex.Message; } repaint = true;
            }
            if (revealing != null && revealing.IsCompleted)
            {
                var work = revealing; revealing = null;
                try
                {
                    var page = work.GetAwaiter().GetResult(); long row = long.Parse(page.Values[0][revealLeft ? 0 : 1], NumberStyles.Integer, CultureInfo.InvariantCulture);
                    if (!FPDataWorkbenchWindow.RevealFigureRow(alignedOrigin.Owner, revealLeft ? alignedOrigin.Left : alignedOrigin.Right, row, revealLeft ? alignedOrigin.LeftColumns : alignedOrigin.RightColumns))
                        error = "Open the owning workbook and free a sheet tab to reveal this original row.";
                }
                catch (Exception ex) { error = ex.Message; } repaint = true;
                revealMemory?.Dispose(); revealMemory = null;
            }
            if (opening != null && opening.IsCompleted)
            {
                var work = opening; opening = null;
                openingMemory?.Dispose(); openingMemory = null;
                try { Attach(work.GetAwaiter().GetResult()); BeginBuild(!settings.HasRange); } catch (Exception ex) { error = ex.Message; } repaint = true;
            }
            if (building != null && building.IsCompleted)
            {
                var work = building; building = null;
                buildMemory?.Dispose(); buildMemory = null;
                try
                {
                    cache = work.GetAwaiter().GetResult(); appliedX = buildX; appliedY = buildY; appliedPolicy = buildPolicy; ClearView(); selected = false; failedQuery = false;
                    if (fitAfterBuild || !settings.HasRange) Fit();
                    if (!cache.HasPoints) error = "No valid XY coordinates; inspect bindings or invalid-value policy.";
                }
                catch (Exception ex) { error = ex.Message; } repaint = true;
            }
            FPWorkbenchSpatialBounds bounds = default; bool valid = false;
            if (cache != null && cache.HasPoints)
            { try { bounds = RequestedBounds(); valid = true; } catch (ArgumentException ex) { error = ex.Message; } }
            if (!valid && waitingForMemory && !pendingOpening && pendingBuild == null && pendingRegenerating == null) { waitingForMemory = false; repaint = true; }
            if (querying != null && (!valid || !bounds.Equals(requested) || requestedBudget != maximumPoints)) queryCancellation.Cancel();
            if (querying != null && querying.IsCompleted)
            {
                var work = querying; querying = null;
                try { var candidate = work.GetAwaiter().GetResult(); if (valid && bounds.Equals(requested) && requestedBudget == maximumPoints) {
                    ClearView(); view = candidate; viewMemory = queryMemory; queryMemory = null;
                    viewMemory.Complete(checked(4096L + view.Points.Count * 64L)); completedBudget = requestedBudget; failedQuery = false; error = null;
                } }
                catch (OperationCanceledException) { }
                catch (Exception ex) { failedQuery = true; error = ex.Message; }
                queryMemory?.Dispose(); queryMemory = null; repaint = true;
            }
            if (valid && building == null && pendingBuild == null && querying == null && (view == null || !view.Bounds.Equals(bounds) || completedBudget != maximumPoints) &&
                (!failedQuery || !requested.Equals(bounds) || requestedBudget != maximumPoints))
            {
                bool admitted = FPWorkbenchEditorResources.Memory.TryAcquire(QueryReservationBytes, out queryMemory);
                if (waitingForMemory != !admitted) { waitingForMemory = !admitted; repaint = true; }
                if (admitted) {
                    queryCancellation?.Dispose(); queryCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                    requested = bounds; requestedBudget = maximumPoints;
                    try { querying = cache.QueryAsync(bounds, maximumPoints, queryCancellation.Token); }
                    catch (Exception ex) { queryMemory.Dispose(); queryMemory = null; failedQuery = true; error = ex.Message; }
                    repaint = true;
                }
            }
            if (repaint || Busy) Repaint();
        }
        private void ClearView() { view = null; viewMemory?.Dispose(); viewMemory = null; }
        private void OnGUI()
        {
            if (dataset == null) { EditorGUILayout.HelpBox(error ?? (waitingForMemory ? "Waiting for shared memory/background capacity…" : "Verifying scatter source…"), MessageType.Info); return; }
            using (new EditorGUI.DisabledScope(building != null || pendingBuild != null || opening != null || hasAlignedRecipe))
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("X", GUILayout.Width(15)); xColumn = EditorGUILayout.Popup(xColumn, names);
                Rect xDrop = GUILayoutUtility.GetLastRect();
                GUILayout.Label("Y", GUILayout.Width(15)); yColumn = EditorGUILayout.Popup(yColumn, names);
                Rect yDrop = GUILayoutUtility.GetLastRect();
                HandleDrop(xDrop, true); HandleDrop(yDrop, false);
                if (GUILayout.Button("Apply / fit", GUILayout.Width(85))) BeginBuild(true);
            }
            presentationUndo ??= new FPWorkbenchPresentationUndo(Repaint); presentationUndo.Begin(settings);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Fit", EditorStyles.toolbarButton)) { try { Fit(); } catch (ArgumentException ex) { error = ex.Message; } }
                settings.InvertY = GUILayout.Toggle(settings.InvertY, "Invert Y", EditorStyles.toolbarButton);
                settings.Grid = GUILayout.Toggle(settings.Grid, "Grid", EditorStyles.toolbarButton);
                GUILayout.Label("Point budget", GUILayout.Width(85)); maximumPoints = Mathf.Clamp(EditorGUILayout.IntField(maximumPoints, GUILayout.Width(65)), 1, 65536);
                invalidPolicy = (FPWorkbenchInvalidPolicy)EditorGUILayout.EnumPopup(invalidPolicy, GUILayout.Width(70));
            }
            showSettings = EditorGUILayout.Foldout(showSettings, "Figure settings", true);
            if (showSettings)
            {
                FPWorkbenchPlotView.LabelControls(settings); settings.MarkerSize = EditorGUILayout.Slider("Point size", settings.MarkerSize, 1, 16);
                settings.XTickSpacing = EditorGUILayout.DoubleField("X grid spacing", settings.XTickSpacing);
                settings.YTickSpacing = EditorGUILayout.DoubleField("Y grid spacing", settings.YTickSpacing);
                settings.StartX = EditorGUILayout.DoubleField("X minimum", settings.StartX); settings.EndX = EditorGUILayout.DoubleField("X maximum", settings.EndX);
                settings.MinimumY = EditorGUILayout.DoubleField("Y minimum", settings.MinimumY); settings.MaximumY = EditorGUILayout.DoubleField("Y maximum", settings.MaximumY);
                pointColor = EditorGUILayout.ColorField("Point color", pointColor);
            }
            presentationUndo.End();
            DrawImageExport();
            GUILayout.Label(alignedOrigin == null ? (changes == null ? "Original" : "Effective") + " snapshot • formulas/corrections frozen at open" : "Aligned snapshot • L " + (alignedOrigin.LeftEffective ? "Effective" : "Original") + " / R " + (alignedOrigin.RightEffective ? "Effective" : "Original"), EditorStyles.miniLabel);
            if (alignedOrigin != null) GUILayout.Label("Alignment: " + alignedOrigin.Matched + " pairs; unmatched L/R " + alignedOrigin.UnmatchedLeft + "/" + alignedOrigin.UnmatchedRight + "; ambiguous L/R " + alignedOrigin.AmbiguousLeft + "/" + alignedOrigin.AmbiguousRight, EditorStyles.miniLabel);
            if (error != null) EditorGUILayout.HelpBox(error, MessageType.Warning);
            if (waitingForMemory) GUILayout.Label("Waiting for shared view memory/read capacity…", EditorStyles.miniLabel);
            else if (Busy) GUILayout.Label(building != null ? "Building/verifying unsorted point cache…" : "Reading point blocks…", EditorStyles.miniLabel);
            if (view != null) GUILayout.Label(view.Points.Count + " / " + view.MatchingCount + " matching points" + (view.Decimated ? " • sampled" : " • all shown") + "; invalid source rows: " + cache.InvalidCount + "; records read: " + view.RecordsRead, EditorStyles.miniLabel);
            Rect rect = GUILayoutUtility.GetRect(100, 10000, 100, 10000, GUILayout.ExpandHeight(true)); Draw(rect);
            if (selected && alignedOrigin == null && GUILayout.Button("Reveal source row in workbook"))
            {
                if (!RevealSelection()) error = "Open the owning workbook (and free a sheet tab if necessary) to reveal this source row.";
            }
            if (selected && alignedOrigin != null)
                using (new EditorGUI.DisabledScope(revealing != null || pendingRevealing != null))
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Reveal left source row")) BeginAlignedReveal(true);
                    if (GUILayout.Button("Reveal right source row")) BeginAlignedReveal(false);
                }
            if (pendingRevealing != null) GUILayout.Label("Linked-row inspection is waiting for shared memory/read capacity…", EditorStyles.miniLabel);
            if (selected) GUILayout.Label((alignedOrigin == null ? "Source row " + selection.Row : "Aligned pair " + (selection.Row + 1)) + " • X " + selection.X.ToString("R", CultureInfo.InvariantCulture) + " • Y " + selection.Y.ToString("R", CultureInfo.InvariantCulture), EditorStyles.miniLabel);
        }
        private void Draw(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(.1f, .1f, .1f));
            if (view == null) { GUI.Label(rect, "Choose numeric X/Y columns and Apply / fit."); return; }
            Rect plot = new Rect(rect.x + 80, rect.y + 25, Math.Max(1, rect.width - 95), Math.Max(1, rect.height - 85)); var b = view.Bounds;
            FPWorkbenchAxisMapping xMap, yMap;
            try { xMap = new FPWorkbenchAxisMapping(b.MinimumX, b.MaximumX, settings.XScale); yMap = new FPWorkbenchAxisMapping(b.MinimumY, b.MaximumY, settings.YScale); }
            catch (ArgumentException ex) { GUI.Label(plot, ex.Message + " Set positive limits in Figure settings."); return; }
            float X(double x) => plot.x + (float)xMap.Fraction(x) * plot.width;
            float Y(double y) { double t = yMap.Fraction(y); return plot.y + (float)(settings.InvertY ? t : 1 - t) * plot.height; }
            GUI.Label(new Rect(rect.x + 5, rect.y, rect.width - 10, 20), string.IsNullOrEmpty(settings.Title) ? names[appliedY] + " vs " + names[appliedX] : settings.Title, EditorStyles.boldLabel);
            try
            {
                foreach (double tick in FPWorkbenchAxis.Ticks(b.MinimumX, b.MaximumX, settings.XScale, settings.XTickSpacing))
                { float x = X(tick); if (settings.Grid) EditorGUI.DrawRect(new Rect(x, plot.y, 1, plot.height), new Color(1, 1, 1, .1f)); GUI.Label(new Rect(x - 20, plot.yMax + 2, 70, 18), tick.ToString("G5", CultureInfo.InvariantCulture), EditorStyles.miniLabel); }
                foreach (double tick in FPWorkbenchAxis.Ticks(b.MinimumY, b.MaximumY, settings.YScale, settings.YTickSpacing))
                { float y = Y(tick); if (settings.Grid) EditorGUI.DrawRect(new Rect(plot.x, y, plot.width, 1), new Color(1, 1, 1, .1f)); GUI.Label(new Rect(rect.x + 3, y - 8, 72, 18), tick.ToString("G5", CultureInfo.InvariantCulture), EditorStyles.miniLabel); }
            }
            catch (ArgumentException ex) { GUI.Label(plot, ex.Message); }
            GUI.Label(new Rect(plot.x, plot.yMax + 22, plot.width, 18), string.IsNullOrEmpty(settings.XLabel) ? names[appliedX] : settings.XLabel, EditorStyles.centeredGreyMiniLabel);
            GUI.Label(new Rect(plot.x, plot.yMax + 40, plot.width, 18), string.IsNullOrEmpty(settings.YLabel) ? "Y: " + names[appliedY] : "Y: " + settings.YLabel, EditorStyles.miniLabel);
            if (Event.current.type == EventType.Repaint)
            {
                GUI.BeginClip(plot);
                foreach (var p in view.Points)
                {
                    float size = settings.MarkerSize;
                    EditorGUI.DrawRect(new Rect(X(p.X) - plot.x - size * .5f, Y(p.Y) - plot.y - size * .5f, size, size), pointColor);
                }
                GUI.EndClip();
            }
            int control = GUIUtility.GetControlID(83714, FocusType.Passive); var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 2 && plot.Contains(e.mousePosition)) { GUIUtility.hotControl = control; e.Use(); }
            if (GUIUtility.hotControl == control)
            {
                if (e.type == EventType.MouseDrag) { Navigate(1, .5, .5, -e.delta.x / plot.width, (settings.InvertY ? -1 : 1) * e.delta.y / plot.height); e.Use(); }
                else if (e.type == EventType.MouseUp) { GUIUtility.hotControl = 0; e.Use(); }
            }
            if (e.type == EventType.ScrollWheel && plot.Contains(e.mousePosition))
            { Navigate(Math.Exp(e.delta.y * .1), (e.mousePosition.x - plot.x) / plot.width, settings.InvertY ? (e.mousePosition.y - plot.y) / plot.height : 1 - (e.mousePosition.y - plot.y) / plot.height, 0, 0); e.Use(); }
            if (e.type == EventType.MouseDown && e.button == 0 && plot.Contains(e.mousePosition))
            {
                double distance = 100; selected = false;
                foreach (var p in view.Points) { double dx = X(p.X) - e.mousePosition.x, dy = Y(p.Y) - e.mousePosition.y, d = dx * dx + dy * dy; if (d <= distance) { distance = d; selection = p; selected = true; } }
                e.Use(); Repaint();
            }
            if (!view.Bounds.Equals(new FPWorkbenchSpatialBounds(settings.StartX, settings.EndX, settings.MinimumY, settings.MaximumY))) GUI.Label(new Rect(plot.x + 5, plot.y + 5, plot.width - 10, 18), "Updating viewport • previous range shown", EditorStyles.miniLabel);
        }
        private void BeginAlignedReveal(bool left)
        {
            if (!selected || alignedOrigin == null || revealing != null || pendingRevealing != null) return;
            var source = dataset; var row = selection.Row; var token = lifetime.Token;
            revealBytes = FPWorkbenchEditorResources.SourceBytes(source, 8L * 1024 * 1024);
            revealLeft = left; pendingRevealing = () => source.ReadPageAsync(row, 1, 0, 2, token); TryStartReveal();
        }
        private void TryStartReveal()
        {
            try {
                FPWorkbenchEditorResources.TryStartRead(revealBytes, ref pendingRevealing, ref revealing, ref revealMemory);
            } catch (Exception ex) { error = ex.Message; }
        }
        private bool RevealSelection()
        {
            if (!selected || dataset == null || cache == null) return false;
            var columns = new System.Collections.Generic.SortedSet<int>();
            foreach (int axis in new[] { appliedX, appliedY })
                if (axis < dataset.ColumnCount) columns.Add(axis);
                else foreach (int c in program.SourceDependencies(axis - dataset.ColumnCount)) columns.Add(c);
            var sourceColumns = new int[columns.Count]; columns.CopyTo(sourceColumns);
            return FPDataWorkbenchWindow.RevealFigureRow(workbookId, datasetId, selection.Row, sourceColumns);
        }
        private void Navigate(double scale, double ax, double ay, double px, double py)
        {
            if (!FPWorkbenchAxis.TryNavigate(settings.StartX, settings.EndX, settings.XScale, scale, ax, px, out double left, out double right) ||
                !FPWorkbenchAxis.TryNavigate(settings.MinimumY, settings.MaximumY, settings.YScale, scale, ay, py, out double bottom, out double top)) return;
            try { ValidateViewport(left, right, bottom, top); settings.StartX = left; settings.EndX = right; settings.MinimumY = bottom; settings.MaximumY = top; } catch (ArgumentException) { }
        }
        private bool StageColumn(FPWorkbenchColumnDragPayload payload, bool x)
        {
            if (hasAlignedRecipe || payload == null || payload.Workbook != workbookId || payload.Dataset != datasetId || dataset == null) return false;
            var schema = new FPWorkbenchTableView(dataset, program); int column = payload.Column;
            if (!string.IsNullOrEmpty(payload.DerivedId))
            {
                if (program == null || payload.FormulaFingerprint != program.Fingerprint) return false;
                column = -1; for (int c = 0; c < program.ColumnCount; c++) if (program.ColumnId(c) == payload.DerivedId) column = dataset.ColumnCount + c;
            }
            else if (column >= dataset.ColumnCount) return false;
            if (column < 0 || column >= schema.ColumnCount || schema.ColumnId(column) != payload.ColumnId) return false;
            if (x) xColumn = column; else yColumn = column; return true;
        }
        private void HandleDrop(Rect rect, bool x)
        {
            var e = Event.current; if (!rect.Contains(e.mousePosition) || !(DragAndDrop.GetGenericData(FPWorkbenchColumnDragPayload.Key) is FPWorkbenchColumnDragPayload payload)) return;
            if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform) return;
            int oldX = xColumn, oldY = yColumn; bool accepted = StageColumn(payload, x); xColumn = oldX; yColumn = oldY;
            DragAndDrop.visualMode = accepted ? DragAndDropVisualMode.Link : DragAndDropVisualMode.Rejected;
            if (accepted && e.type == EventType.DragPerform) { DragAndDrop.AcceptDrag(); StageColumn(payload, x); DragAndDrop.SetGenericData(FPWorkbenchColumnDragPayload.Key, null); }
            e.Use(); Repaint();
        }
    }
}
