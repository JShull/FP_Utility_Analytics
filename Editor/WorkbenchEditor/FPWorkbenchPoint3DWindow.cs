// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using UnityEditor;
    using UnityEngine;
    using FuzzPhyte.Utility.Editor;

    /// <summary>Bounded XYZ snapshot preview. One mesh per view; camera controls reuse the shared preview utility.</summary>
    public sealed class FPWorkbenchPoint3DWindow : EditorWindow
    {
        [SerializeField] private string figureId = Guid.NewGuid().ToString("N"), lastFigureMetadata;
        internal string WorkbookId => workbook;
        internal string FigureId => figureId;
        [SerializeField] private string workbook, cacheDirectory, datasetId, calculations, corrections;
        [SerializeField] private int xColumn, yColumn = 1, zColumn = 2, pointBudget = 10000;
        [SerializeField] private FPWorkbenchInvalidPolicy invalidPolicy = FPWorkbenchInvalidPolicy.Reject;
        [SerializeField] private Color pointColor = new Color(0, .7f, 1);
        [SerializeField] private float pointSize = 4, zoom = FPMeshPreviewEditorUtility.DefaultPreviewZoom;
        [SerializeField] private Quaternion orbit = Quaternion.Euler(25, -35, 0);
        [SerializeField] private Vector3 pan;
        [SerializeField] private FPMeshPreviewProjection projection;
        [SerializeField] private bool invertOrbit, clip, showClip;
        [SerializeField] private double minX, maxX = 1, minY, maxY = 1, minZ, maxZ = 1;
        private FPWorkbenchCsvDataset dataset;
        private FPWorkbenchFormulaProgram program;
        private FPWorkbenchChangeSnapshot edits;
        private string[] names;
        private Task<FPWorkbenchCsvDataset> opening;
        [SerializeField] private FPWorkbenchCsvProfile sourceProfile;
        private bool pendingOpening;
        private FPWorkbenchEditorResources.ScanReservation openingMemory;
        private Task<Batch> building;
        private Func<Task<Batch>> pendingBuild;
        private FPWorkbenchEditorResources.ScanReservation buildMemory;
        private Task<Batch> preparing;
        private Batch pendingBatch;
        private FPWorkbenchMemoryBudget.Reservation preparationMemory, batchMemory;
        private bool windowActive, waitingForMemory;
        private const long PreparationReservationBytes = 64L * 1024 * 1024;
        private CancellationTokenSource cancellation;
        private PreviewRenderUtility preview;
        private FPWorkbenchPointRenderer renderer;
        private Batch batch;
        private Texture previewTexture;
        private Rect previewRect;
        private double uploadMilliseconds;
        private string error;
        private int activeAxis = -1;
        private int[] appliedColumns;
        private bool selected;
        private FPWorkbenchSpatialPoint selection;
        [SerializeField] private int exportWidth = 1600, exportHeight = 1200;
        private FPWorkbenchEditorImageJob imageJob;
        private FPWorkbenchEditorImageJob ImageJob => imageJob ??= new FPWorkbenchEditorImageJob(Repaint);
        [Serializable] private sealed class ImageProvenance : FPWorkbenchImageProvenance
        {
            public string Format = "FuzzPhyte.DataWorkbench.Point3DImage", SourcePath;
            public int Version = 1, RenderedPoints;
            public long MatchingPoints;
            public bool Sampled;
            public double OriginX, OriginY, OriginZ, SourceUnitsPerLocalUnit, MaximumLocalError;
            public FPWorkbenchFigureDocument Figure;
        }
        private Task<FPWorkbenchImageExportResult> ExportPng(string directory, int width, int height)
        {
            if (width < 64 || height < 64 || width > 4096 || height > 4096 || (long)width * height > 4194304)
                throw new ArgumentOutOfRangeException("Image dimensions must be 64–4096 pixels, at most 4,194,304 pixels total.");
            if (Busy || batch == null || renderer == null) throw new InvalidOperationException("Wait for a completed point view before exporting.");
            string output = Path.GetFullPath(directory);
            if (!output.EndsWith(".fpfigure", StringComparison.OrdinalIgnoreCase) || Directory.Exists(output) || File.Exists(output))
                throw new IOException("Choose a new .fpfigure output directory; existing outputs are never overwritten.");
            string parent = Path.GetDirectoryName(output);
            if (!Directory.Exists(parent)) throw new DirectoryNotFoundException(parent);
            using var reservation = FPWorkbenchEditorImageReservation.Acquire(width, height, 0);
            var figure = DescribeFigure(); figure.Validate();
            var frame = batch.Prepared.Frame;
            var metadata = new ImageProvenance { SourcePath = dataset.SourcePath, Width = width, Height = height,
                RenderedPoints = batch.View.Points.Count, MatchingPoints = batch.View.MatchingCount, Sampled = batch.View.Decimated,
                OriginX = frame.OriginX, OriginY = frame.OriginY, OriginZ = frame.OriginZ, SourceUnitsPerLocalUnit = frame.SourceUnitsPerLocalUnit,
                MaximumLocalError = batch.Prepared.MaximumLocalError, Figure = figure };
            PreviewRenderUtility export = null; Texture2D image = null;
            try {
                export = new PreviewRenderUtility(); var rect = new Rect(0, 0, width, height);
                export.BeginStaticPreview(rect); RenderPoints(export, rect); image = export.EndStaticPreview();
                return reservation.Publish(output, image.GetPixels32(), width, height, metadata, imageJob?.Token ?? default);
            }
            finally { if (image != null) DestroyImmediate(image); export?.Cleanup(); }
        }
        [SerializeField] private bool showAppearancePresets;
        private FPWorkbenchPresentationUndoState appearanceUndo;
        private string observedAppearance;
        private string AppearanceJson() => new FPWorkbenchPoint3DPreset { Color = pointColor, PointSize = pointSize }.ToJson();
        private void ApplyAppearancePreset(string json)
        {
            var preset = FPWorkbenchPoint3DPreset.Parse(json); string before = AppearanceJson();
            pointColor = preset.Color; pointSize = preset.PointSize; RecordAppearance(before); Repaint();
        }
        private void RecordAppearance(string before)
        {
            string after = AppearanceJson(); if (before == after) return;
            if (appearanceUndo == null) {
                appearanceUndo = CreateInstance<FPWorkbenchPresentationUndoState>(); appearanceUndo.hideFlags = HideFlags.HideAndDontSave;
                Undo.undoRedoPerformed += RestoreAppearance;
            }
            appearanceUndo.Json = observedAppearance = before; Undo.RegisterCompleteObjectUndo(appearanceUndo, "Change Workbench 3D appearance");
            appearanceUndo.Json = observedAppearance = after; EditorUtility.SetDirty(appearanceUndo);
        }
        private void RestoreAppearance()
        {
            if (appearanceUndo == null || appearanceUndo.Json == observedAppearance) return;
            var preset = FPWorkbenchPoint3DPreset.Parse(appearanceUndo.Json); observedAppearance = appearanceUndo.Json;
            pointColor = preset.Color; pointSize = preset.PointSize; Repaint();
        }
        private sealed class Batch { public FPWorkbenchPointCache Cache; public FPWorkbenchPointView View; public FPWorkbenchPreparedPoints Prepared; public int[] Columns; public double Milliseconds; public bool Clip; public FPWorkbenchSpatialBounds Limits; public int Budget; public FPWorkbenchInvalidPolicy Policy; }
        private bool Busy => opening != null || pendingOpening || building != null || pendingBuild != null || preparing != null || pendingBatch != null;
        public static FPWorkbenchPoint3DWindow Open(string owner, FPWorkbenchCsvDataset source, int x, int y, int z,
            FPWorkbenchFormulaProgram formulas = null, FPWorkbenchChangeSnapshot corrections = null)
        {
            var schema = new FPWorkbenchTableView(source, formulas); schema.ColumnId(x); schema.ColumnId(y); schema.ColumnId(z);
            if (corrections != null && corrections.DatasetId != source.DatasetId) throw new ArgumentException("3D correction source differs.");
            var window = CreateInstance<FPWorkbenchPoint3DWindow>(); window.workbook = owner; window.cacheDirectory = source.CacheDirectory; window.datasetId = source.DatasetId;
            window.xColumn = x; window.yColumn = y; window.zColumn = z; window.calculations = formulas == null ? null : JsonUtility.ToJson(formulas.Definition); window.corrections = corrections?.Json;
            window.Attach(source); window.BeginBuild(); FPDataWorkbenchWindow.RememberFigure(owner, window.DescribeFigure()); window.titleContent = new GUIContent("3D Point Figure"); window.Show(); return window;
        }
        internal FPWorkbenchFigureDocument DescribeFigure()
        {
            if (dataset == null)
            {
                if (string.IsNullOrEmpty(lastFigureMetadata)) throw new InvalidOperationException("3D source is not admitted yet.");
                return JsonUtility.FromJson<FPWorkbenchFigureDocument>(lastFigureMetadata);
            }
            var axes = batch?.Columns ?? new[] { xColumn, yColumn, zColumn };
            string Derived(int c) => c < dataset.ColumnCount ? "" : program.ColumnId(c - dataset.ColumnCount);
            int Physical(int c) => c < dataset.ColumnCount ? c : 0;
            var bounds = batch == null ? (clip ? new FPWorkbenchSpatialBounds(minX, maxX, minY, maxY, minZ, maxZ) : default) : batch.Limits;
            var figure = new FPWorkbenchFigureDocument { Id = figureId, Name = "3D point figure", DatasetId = datasetId, Kind = FPWorkbenchFigureKind.Point3D,
                Binding = new FPWorkbenchSeriesBinding { XMode = FPWorkbenchXMode.Numeric, XColumn = Physical(axes[0]), YColumn = Physical(axes[1]), XDerivedId = Derived(axes[0]), YDerivedId = Derived(axes[1]) },
                PointBudget = batch?.Budget ?? pointBudget, InvalidPolicy = batch?.Policy ?? invalidPolicy, Colors = new[] { pointColor },
                Spatial = new FPWorkbenchPoint3DSettings { ZColumn = Physical(axes[2]), ZDerivedId = Derived(axes[2]), Clip = batch?.Clip ?? clip,
                    MinimumX = bounds.MinimumX, MaximumX = bounds.MaximumX, MinimumY = bounds.MinimumY, MaximumY = bounds.MaximumY, MinimumZ = bounds.MinimumZ, MaximumZ = bounds.MaximumZ,
                    Orbit = orbit, Pan = pan, Zoom = zoom, PointSize = pointSize, Orthographic = projection == FPMeshPreviewProjection.Orthographic, InvertOrbit = invertOrbit },
                HasCalculationSnapshot = program != null, CalculationSnapshot = program?.Definition,
                HasCorrectionSnapshot = edits != null, CorrectionSnapshot = edits == null ? null : JsonUtility.FromJson<FPWorkbenchChangeSet>(edits.Json) };
            lastFigureMetadata = JsonUtility.ToJson(figure); return figure;
        }
        internal static FPWorkbenchPoint3DWindow OpenSaved(string owner, FPWorkbenchCsvDataset source, FPWorkbenchFigureDocument figure)
        {
            figure.Validate(); if (figure.Kind != FPWorkbenchFigureKind.Point3D || figure.DatasetId != source.DatasetId) throw new ArgumentException("3D source/kind differs.");
            var formulas = figure.HasCalculationSnapshot ? FPWorkbenchFormulaProgram.Compile(source, figure.CalculationSnapshot) : null;
            var binding = figure.Binding.Copy(source.ColumnCount); var spatial = figure.Spatial;
            int x = binding.HasDerivedX ? source.ColumnCount + binding.ResolveDerivedX(formulas) : binding.XColumn;
            int y = binding.HasDerivedY ? source.ColumnCount + binding.ResolveDerivedY(formulas) : binding.YColumn;
            int z = !string.IsNullOrEmpty(spatial.ZDerivedId) ? source.ColumnCount + new FPWorkbenchSeriesBinding { YDerivedId = spatial.ZDerivedId }.ResolveDerivedY(formulas) : spatial.ZColumn;
            var schema = new FPWorkbenchTableView(source, formulas); schema.ColumnId(x); schema.ColumnId(y); schema.ColumnId(z);
            var window = CreateInstance<FPWorkbenchPoint3DWindow>(); window.workbook = owner; window.figureId = figure.Id; window.cacheDirectory = source.CacheDirectory; window.datasetId = source.DatasetId;
            window.xColumn = x; window.yColumn = y; window.zColumn = z; window.pointBudget = figure.PointBudget; window.invalidPolicy = figure.InvalidPolicy;
            window.calculations = formulas == null ? null : JsonUtility.ToJson(formulas.Definition); window.corrections = figure.HasCorrectionSnapshot ? JsonUtility.ToJson(figure.CorrectionSnapshot) : null;
            window.clip = spatial.Clip; window.minX = spatial.MinimumX; window.maxX = spatial.MaximumX; window.minY = spatial.MinimumY; window.maxY = spatial.MaximumY; window.minZ = spatial.MinimumZ; window.maxZ = spatial.MaximumZ;
            window.orbit = spatial.Orbit; window.pan = spatial.Pan; window.zoom = spatial.Zoom; window.pointSize = spatial.PointSize; window.pointColor = figure.Colors[0];
            window.projection = spatial.Orthographic ? FPMeshPreviewProjection.Orthographic : FPMeshPreviewProjection.Perspective; window.invertOrbit = spatial.InvertOrbit;
            window.Attach(source); window.DescribeFigure(); window.BeginBuild(); window.titleContent = new GUIContent(figure.Name); window.Show(); return window;
        }
        private void OnEnable()
        {
            windowActive = true;
            minSize = new Vector2(760, 520); cancellation = new CancellationTokenSource(); preview = new PreviewRenderUtility();
            EditorApplication.update += Tick;
            pendingOpening = !string.IsNullOrEmpty(cacheDirectory);
        }
        private void OnDisable()
        {
            windowActive = false;
            imageJob?.Dispose(); imageJob = null;
            Undo.undoRedoPerformed -= RestoreAppearance;
            if (appearanceUndo != null) { Undo.ClearUndo(appearanceUndo); DestroyImmediate(appearanceUndo); appearanceUndo = null; }
            try { if (dataset != null) FPDataWorkbenchWindow.RememberFigure(workbook, DescribeFigure()); }
            catch (Exception ex) { error = "3D settings not retained: " + ex.Message; }
            EditorApplication.update -= Tick; cancellation?.Cancel(); cancellation?.Dispose(); cancellation = null;
            if (openingMemory != null) openingMemory.ReleaseAfter(opening); else Observe(opening);
            openingMemory = null; pendingOpening = false;
            if (buildMemory != null) buildMemory.ReleaseAfter(building); else Observe(building);
            buildMemory = null; pendingBuild = null; opening = null; building = null;
            if (preparationMemory != null) preparationMemory.ReleaseAfter(preparing); else Observe(preparing);
            preparationMemory = null; preparing = null; pendingBatch = null; waitingForMemory = false;
            batchMemory?.Dispose(); batchMemory = null;
            renderer?.Dispose(); renderer = null; preview?.Cleanup(); preview = null; batch = null; dataset = null; previewTexture = null;
        }
        private static void Observe(Task task) { if (task != null) task.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted); }
        private void Attach(FPWorkbenchCsvDataset source)
        {
            sourceProfile = source.ImportProfile;
            if (source.DatasetId != datasetId) throw new InvalidOperationException("3D source revision changed.");
            program = string.IsNullOrEmpty(calculations) ? null : FPWorkbenchFormulaProgram.Compile(source, JsonUtility.FromJson<FPWorkbenchFormulaSet>(calculations));
            edits = string.IsNullOrEmpty(corrections) ? null : JsonUtility.FromJson<FPWorkbenchChangeSet>(corrections).Snapshot();
            if (edits != null && edits.DatasetId != datasetId) throw new InvalidOperationException("3D correction source differs.");
            var schema = new FPWorkbenchTableView(source, program); schema.ColumnId(xColumn); schema.ColumnId(yColumn); schema.ColumnId(zColumn);
            names = new string[schema.ColumnCount]; for (int c = 0; c < names.Length; c++) names[c] = (c >= source.ColumnCount ? "ƒ " : "") + schema.ColumnName(c);
            dataset = source;
        }
        private void BeginBuild()
        {
            if (dataset == null || Busy) return;
            try
            {
                var columns = new[] { xColumn, yColumn, zColumn }; var bounds = clip ? new FPWorkbenchSpatialBounds(minX, maxX, minY, maxY, minZ, maxZ) : default;
                var source = dataset; var formulas = program; var changes = edits; var policy = invalidPolicy;
                var budget = pointBudget; var clipping = clip; var token = cancellation.Token;
                pendingBuild = () => Build(source, columns, formulas, changes, policy, budget, clipping, bounds, token); error = null;
                waitingForMemory = !FPWorkbenchEditorResources.TryStartSource(dataset, ref pendingBuild, ref building, ref buildMemory);
            }
            catch (Exception ex) { error = ex.Message; }
        }
        private static async Task<Batch> Build(FPWorkbenchCsvDataset source, int[] columns, FPWorkbenchFormulaProgram formulas, FPWorkbenchChangeSnapshot changes,
            FPWorkbenchInvalidPolicy policy, int budget, bool clip, FPWorkbenchSpatialBounds limits, CancellationToken token)
        {
            var clock = Stopwatch.StartNew(); var cache = await FPWorkbenchPointCache.BuildAsync(source, columns, formulas, changes, policy, token).ConfigureAwait(false);
            return new Batch { Cache = cache, Columns = columns, Clip = clip, Limits = clip ? limits : cache.Bounds, Budget = budget, Policy = policy, Milliseconds = clock.Elapsed.TotalMilliseconds };
        }
        private static async Task<Batch> Prepare(Batch next, CancellationToken token)
        {
            var clock = Stopwatch.StartNew();
            next.View = await next.Cache.QueryAsync(next.Limits, next.Budget, token).ConfigureAwait(false);
            next.Prepared = await FPWorkbenchPointPreparation.PrepareAsync(next.View, FPWorkbenchSpatialFrame.Fit(next.View.Bounds), cancellation: token).ConfigureAwait(false);
            next.Milliseconds += clock.Elapsed.TotalMilliseconds; return next;
        }
        private void Tick()
        {
            if (!windowActive) return;
            if (pendingOpening) {
                try { pendingOpening = !FPWorkbenchEditorResources.TryOpenCache(cacheDirectory, sourceProfile, cancellation.Token, ref opening, ref openingMemory); waitingForMemory = pendingOpening; }
                catch (Exception ex) { pendingOpening = false; waitingForMemory = false; error = ex.Message; }
                Repaint();
            }
            if (pendingBuild != null) {
                try { waitingForMemory = !FPWorkbenchEditorResources.TryStartSource(dataset, ref pendingBuild, ref building, ref buildMemory); }
                catch (Exception ex) { waitingForMemory = false; error = ex.Message; }
                Repaint();
            }
            if (opening != null && opening.IsCompleted)
            {
                var work = opening; opening = null;
                openingMemory?.Dispose(); openingMemory = null;
                try { Attach(work.GetAwaiter().GetResult()); BeginBuild(); } catch (Exception ex) { error = ex.Message; } Repaint();
            }
            if (building != null && building.IsCompleted)
            {
                var work = building; building = null;
                buildMemory?.Dispose(); buildMemory = null;
                try { pendingBatch = work.GetAwaiter().GetResult(); }
                catch (Exception ex) { error = ex.Message; } Repaint();
            }
            if (preparing != null && preparing.IsCompleted)
            {
                var work = preparing; preparing = null;
                try
                {
                    var next = work.GetAwaiter().GetResult(); renderer ??= new FPWorkbenchPointRenderer(Shader.Find("Hidden/FuzzPhyte/Workbench/Points"));
                    var clock = Stopwatch.StartNew(); renderer.SetPoints(next.Prepared); uploadMilliseconds = clock.Elapsed.TotalMilliseconds;
                    batchMemory?.Dispose(); batchMemory = preparationMemory; preparationMemory = null;
                    // Covers retained point/prepared arrays and estimated readable mesh plus GPU payload, not actual driver allocation.
                    batchMemory.Complete(checked(4096L + next.View.Points.Count * 64L + next.Prepared.Positions.Count * 320L));
                    batch = next; appliedColumns = next.Columns; selected = false;
                }
                catch (Exception ex) { error = ex.Message; }
                preparationMemory?.Dispose(); preparationMemory = null; Repaint();
            }
            if (pendingBatch != null && preparing == null) {
                bool admitted = FPWorkbenchEditorResources.Memory.TryAcquire(PreparationReservationBytes, out preparationMemory);
                if (waitingForMemory != !admitted) { waitingForMemory = !admitted; Repaint(); }
                if (admitted) {
                    var next = pendingBatch; pendingBatch = null;
                    try { preparing = Prepare(next, cancellation.Token); }
                    catch (Exception ex) { preparationMemory.Dispose(); preparationMemory = null; error = ex.Message; }
                }
            }
            if (Busy) Repaint();
        }
        private void CancelPointUpdate()
        {
            if (building == null && pendingBuild == null && preparing == null && pendingBatch == null) return;
            cancellation.Cancel(); cancellation.Dispose(); cancellation = new CancellationTokenSource();
            if (buildMemory != null) buildMemory.ReleaseAfter(building); else Observe(building);
            buildMemory = null; pendingBuild = null; building = null;
            if (preparationMemory != null) preparationMemory.ReleaseAfter(preparing); else Observe(preparing);
            preparationMemory = null; preparing = null; pendingBatch = null; waitingForMemory = false;
            error = "Point update cancelled; previous view retained."; Repaint();
        }
        private void OnGUI()
        {
            if (dataset == null) { EditorGUILayout.HelpBox(error ?? (pendingOpening ? "Waiting for shared memory/background capacity…" : "Verifying XYZ source…"), MessageType.Info); return; }
            using (new EditorGUI.DisabledScope(Busy))
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("X", GUILayout.Width(15)); xColumn = EditorGUILayout.Popup(xColumn, names);
                GUILayout.Label("Y", GUILayout.Width(15)); yColumn = EditorGUILayout.Popup(yColumn, names);
                GUILayout.Label("Z", GUILayout.Width(15)); zColumn = EditorGUILayout.Popup(zColumn, names);
                if (GUILayout.Button("Apply", GUILayout.Width(60))) BeginBuild();
            }
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Frame", EditorStyles.toolbarButton)) { pan = Vector3.zero; zoom = FPMeshPreviewEditorUtility.DefaultPreviewZoom; }
                projection = FPMeshPreviewEditorUtility.DrawProjectionPopup(projection); invertOrbit = FPMeshPreviewEditorUtility.DrawInvertCameraOrbitToggle(invertOrbit);
                GUILayout.Label("Budget", GUILayout.Width(45)); pointBudget = Mathf.Clamp(EditorGUILayout.IntField(pointBudget, GUILayout.Width(65)), 1, 65536);
                invalidPolicy = (FPWorkbenchInvalidPolicy)EditorGUILayout.EnumPopup(invalidPolicy, GUILayout.Width(70));
            }
            string beforeAppearance = AppearanceJson();
            using (new EditorGUILayout.HorizontalScope())
            { pointColor = EditorGUILayout.ColorField("Point color", pointColor); pointSize = EditorGUILayout.Slider("Pixels", pointSize, 1, 32); }
            RecordAppearance(beforeAppearance);
            imageJob?.Draw();
            showAppearancePresets = EditorGUILayout.Foldout(showAppearancePresets, "Appearance presets", true);
            if (showAppearancePresets) FPWorkbenchPlotPresetGUI.DrawFileActions(AppearanceJson, ApplyAppearancePreset, "3D appearance preset", "fp3dplot", FPWorkbenchPoint3DPreset.MaximumBytes);
            if (showAppearancePresets) {
                using (new EditorGUILayout.HorizontalScope()) {
                    exportWidth = EditorGUILayout.IntField("PNG width", exportWidth); exportHeight = EditorGUILayout.IntField("Height", exportHeight);
                    using (new EditorGUI.DisabledScope(Busy || batch == null || FPWorkbenchImageExport.IsActive))
                        if (GUILayout.Button("Export PNG…")) {
                            string path = EditorUtility.SaveFilePanel("Export 3D image and provenance", "", "PointFigure", "fpfigure");
                            if (!string.IsNullOrEmpty(path)) { int w = exportWidth, h = exportHeight; EditorApplication.delayCall += () => {
                                if (this == null) return;
                                ImageJob.Start(() => ExportPng(path, w, h));
                            }; }
                        }
                }
            }
            showClip = EditorGUILayout.Foldout(showClip, "Source-coordinate clipping (Apply to query)", true);
            if (showClip)
            {
                clip = EditorGUILayout.Toggle("Enable clip bounds", clip);
                using (new EditorGUILayout.HorizontalScope()) { minX = EditorGUILayout.DoubleField("X min", minX); maxX = EditorGUILayout.DoubleField("X max", maxX); }
                using (new EditorGUILayout.HorizontalScope()) { minY = EditorGUILayout.DoubleField("Y min", minY); maxY = EditorGUILayout.DoubleField("Y max", maxY); }
                using (new EditorGUILayout.HorizontalScope()) { minZ = EditorGUILayout.DoubleField("Z min", minZ); maxZ = EditorGUILayout.DoubleField("Z max", maxZ); }
                if (batch != null && GUILayout.Button("Copy full source extents")) { var b = batch.Cache.Bounds; minX = b.MinimumX; maxX = b.MaximumX; minY = b.MinimumY; maxY = b.MaximumY; minZ = b.MinimumZ; maxZ = b.MaximumZ; }
            }
            GUILayout.Label((edits == null ? "Original" : "Effective") + " frozen snapshot • right-drag orbit • middle-drag pan • wheel zoom • click inspect", EditorStyles.miniLabel);
            if (error != null) EditorGUILayout.HelpBox(error, MessageType.Warning);
            if (Busy)
            {
                GUILayout.Label(waitingForMemory ? "Waiting for shared view memory/read capacity…" : "Building/verifying/querying XYZ cache and preparing local points…", EditorStyles.miniLabel);
                if (GUILayout.Button("Cancel point update")) CancelPointUpdate();
            }
            if (batch != null)
            {
                GUILayout.Label(batch.View.Points.Count + " / " + batch.View.MatchingCount + " matching points" + (batch.View.Decimated ? " • sampled" : " • all shown") + "; invalid rows " + batch.Cache.InvalidCount + "; worker " + batch.Milliseconds.ToString("F1") + " ms; mesh upload " + uploadMilliseconds.ToString("F1") + " ms", EditorStyles.miniLabel);
                GUILayout.Label("X " + names[appliedColumns[0]] + " • Y " + names[appliedColumns[1]] + " • Z " + names[appliedColumns[2]] + "; max local float error " + batch.Prepared.MaximumLocalError.ToString("G3"), EditorStyles.miniLabel);
            }
            Rect rect = GUILayoutUtility.GetRect(100, 10000, 150, 10000, GUILayout.ExpandHeight(true)); DrawPreview(rect);
            if (selected)
            {
                GUILayout.Label("Source row " + selection.Row + ": " + selection.X.ToString("R", CultureInfo.InvariantCulture) + ", " + selection.Y.ToString("R", CultureInfo.InvariantCulture) + ", " + selection.Z.ToString("R", CultureInfo.InvariantCulture), EditorStyles.miniLabel);
                if (GUILayout.Button("Reveal source row in workbook"))
                {
                    var columns = new System.Collections.Generic.SortedSet<int>();
                    foreach (int axis in appliedColumns) if (axis < dataset.ColumnCount) columns.Add(axis); else foreach (int c in program.SourceDependencies(axis - dataset.ColumnCount)) columns.Add(c);
                    var physical = new int[columns.Count]; columns.CopyTo(physical);
                    if (!FPDataWorkbenchWindow.RevealFigureRow(workbook, datasetId, selection.Row, physical)) error = "Open the owning workbook and free a sheet tab to reveal this row.";
                }
            }
        }
        private void DrawPreview(Rect rect)
        {
            previewRect = rect;
            if (renderer?.Mesh == null || batch == null) { GUI.Label(rect, "Choose numeric XYZ columns and Apply."); return; }
            int control = GUIUtility.GetControlID(78310, FocusType.Passive); var e = Event.current;
            if (rect.Contains(e.mousePosition))
            {
                if (e.type == EventType.ScrollWheel) { zoom = FPMeshPreviewEditorUtility.ApplyScrollZoom(zoom, e.delta.y); e.Use(); Repaint(); }
                if (e.type == EventType.MouseDown && e.button == 0 && FPMeshPreviewEditorUtility.TryGetSceneOrientationClick(rect, preview.camera, e.mousePosition, out var snapped)) { orbit = snapped; e.Use(); Repaint(); }
                if (e.type == EventType.MouseDown && (e.button == 1 || e.button == 2)) { GUIUtility.hotControl = control; e.Use(); }
                if (e.type == EventType.MouseDown && e.button == 0)
                {
                    activeAxis = FPMeshPreviewEditorUtility.GetOrbitAxisAtPosition(rect, e.mousePosition);
                    if (activeAxis >= 0) { GUIUtility.hotControl = control; e.Use(); }
                    else if (!FPMeshPreviewEditorUtility.IsOrbitGizmoPosition(rect, e.mousePosition)) { Inspect(rect, e.mousePosition); e.Use(); Repaint(); }
                }
            }
            if (GUIUtility.hotControl == control)
            {
                if (e.type == EventType.MouseDrag)
                {
                    if (activeAxis >= 0) orbit = FPMeshPreviewEditorUtility.ApplyOrbitAxisDrag(orbit, activeAxis, e.delta);
                    else if (e.button == 2) pan += FPMeshPreviewEditorUtility.CalculatePanDelta(preview.camera, rect, renderer.Mesh.bounds.center + pan, e.delta);
                    else orbit = FPMeshPreviewEditorUtility.ApplyUnityStyleOrbit(orbit, e.delta, invertOrbit);
                    e.Use(); Repaint();
                }
                else if (e.type == EventType.MouseUp) { GUIUtility.hotControl = 0; activeAxis = -1; e.Use(); }
            }
            if (e.type == EventType.Repaint)
            {
                preview.BeginPreview(rect, GUIStyle.none); RenderPoints(preview, rect); Camera camera = preview.camera;
                previewTexture = preview.EndPreview(); GUI.DrawTexture(rect, previewTexture, ScaleMode.StretchToFill, false);
                FPMeshPreviewEditorUtility.DrawSceneOrientationGizmo(rect, camera, projection);
            }
            FPMeshPreviewEditorUtility.DrawOrbitGizmo(rect, (direction, up) => { orbit = Quaternion.LookRotation(direction, up); Repaint(); });
        }
        private void RenderPoints(PreviewRenderUtility target, Rect rect)
        {
            Camera camera = target.camera; var bounds = renderer.Mesh.bounds;
            camera.backgroundColor = new Color(.1f, .1f, .1f); camera.clearFlags = CameraClearFlags.Color; camera.fieldOfView = FPMeshPreviewEditorUtility.DefaultFieldOfView;
            float distance = FPMeshPreviewEditorUtility.CalculateFitDistance(bounds, rect) * zoom;
            camera.transform.SetPositionAndRotation(bounds.center + pan - orbit * Vector3.forward * distance, orbit);
            camera.orthographic = projection == FPMeshPreviewProjection.Orthographic;
            camera.orthographicSize = FPMeshPreviewEditorUtility.CalculateOrthographicSize(bounds, rect, orbit) * zoom;
            float radius = Math.Max(.1f, bounds.extents.magnitude); camera.nearClipPlane = Math.Max(.001f, distance - radius * 2.4f); camera.farClipPlane = distance + radius * 3.4f;
            renderer.SetAppearance(pointColor, pointSize); target.DrawMesh(renderer.Mesh, Matrix4x4.identity, renderer.Material, 0); target.Render(true);
        }
        private void Inspect(Rect rect, Vector2 mouse)
        {
            selected = false; double best = 100; float depth = float.PositiveInfinity;
            for (int i = 0; i < batch.Prepared.Positions.Count; i++)
            {
                var p = preview.camera.WorldToViewportPoint(batch.Prepared.Positions[i]); if (p.z <= preview.camera.nearClipPlane) continue;
                double dx = rect.x + p.x * rect.width - mouse.x, dy = rect.y + (1 - p.y) * rect.height - mouse.y, distance = dx * dx + dy * dy;
                if (distance < best || distance == best && p.z < depth) { best = distance; depth = p.z; selection = batch.View.Points[i]; selected = true; }
            }
        }
    }
}
