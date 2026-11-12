// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.Diagnostics;
    using System.Globalization;
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
        private Task<Batch> building;
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
        private sealed class Batch { public FPWorkbenchPointCache Cache; public FPWorkbenchPointView View; public FPWorkbenchPreparedPoints Prepared; public int[] Columns; public double Milliseconds; public bool Clip; public FPWorkbenchSpatialBounds Limits; public int Budget; public FPWorkbenchInvalidPolicy Policy; }
        private bool Busy => opening != null || building != null;
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
            minSize = new Vector2(760, 520); cancellation = new CancellationTokenSource(); preview = new PreviewRenderUtility();
            EditorApplication.update += Tick;
            if (!string.IsNullOrEmpty(cacheDirectory)) opening = FPWorkbenchCsvDataset.OpenAsync(cacheDirectory, cancellation.Token);
        }
        private void OnDisable()
        {
            try { if (dataset != null) FPDataWorkbenchWindow.RememberFigure(workbook, DescribeFigure()); }
            catch (Exception ex) { error = "3D settings not retained: " + ex.Message; }
            EditorApplication.update -= Tick; cancellation?.Cancel(); cancellation?.Dispose(); cancellation = null;
            Observe(opening); Observe(building); opening = null; building = null;
            renderer?.Dispose(); renderer = null; preview?.Cleanup(); preview = null; batch = null; dataset = null; previewTexture = null;
        }
        private static void Observe(Task task) { if (task != null) task.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted); }
        private void Attach(FPWorkbenchCsvDataset source)
        {
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
                building = Build(dataset, columns, program, edits, invalidPolicy, pointBudget, clip, bounds, cancellation.Token); error = null;
            }
            catch (Exception ex) { error = ex.Message; }
        }
        private static async Task<Batch> Build(FPWorkbenchCsvDataset source, int[] columns, FPWorkbenchFormulaProgram formulas, FPWorkbenchChangeSnapshot changes,
            FPWorkbenchInvalidPolicy policy, int budget, bool clip, FPWorkbenchSpatialBounds limits, CancellationToken token)
        {
            var clock = Stopwatch.StartNew(); var cache = await FPWorkbenchPointCache.BuildAsync(source, columns, formulas, changes, policy, token).ConfigureAwait(false);
            var view = await cache.QueryAsync(clip ? limits : cache.Bounds, budget, token).ConfigureAwait(false);
            var prepared = await FPWorkbenchPointPreparation.PrepareAsync(view, FPWorkbenchSpatialFrame.Fit(view.Bounds), cancellation: token).ConfigureAwait(false);
            return new Batch { Cache = cache, View = view, Prepared = prepared, Columns = columns, Clip = clip, Limits = clip ? limits : cache.Bounds, Budget = budget, Policy = policy, Milliseconds = clock.Elapsed.TotalMilliseconds };
        }
        private void Tick()
        {
            if (opening != null && opening.IsCompleted)
            {
                var work = opening; opening = null;
                try { Attach(work.GetAwaiter().GetResult()); BeginBuild(); } catch (Exception ex) { error = ex.Message; } Repaint();
            }
            if (building != null && building.IsCompleted)
            {
                var work = building; building = null;
                try
                {
                    var next = work.GetAwaiter().GetResult(); renderer ??= new FPWorkbenchPointRenderer(Shader.Find("Hidden/FuzzPhyte/Workbench/Points"));
                    var clock = Stopwatch.StartNew(); renderer.SetPoints(next.Prepared); uploadMilliseconds = clock.Elapsed.TotalMilliseconds;
                    batch = next; appliedColumns = next.Columns; selected = false;
                }
                catch (Exception ex) { error = ex.Message; } Repaint();
            }
            if (Busy) Repaint();
        }
        private void OnGUI()
        {
            if (dataset == null) { EditorGUILayout.HelpBox(error ?? "Verifying XYZ source…", MessageType.Info); return; }
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
            using (new EditorGUILayout.HorizontalScope())
            { pointColor = EditorGUILayout.ColorField("Point color", pointColor); pointSize = EditorGUILayout.Slider("Pixels", pointSize, 1, 32); }
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
                GUILayout.Label("Building/verifying/querying XYZ cache and preparing local points…", EditorStyles.miniLabel);
                if (building != null && GUILayout.Button("Cancel point update"))
                {
                    cancellation.Cancel(); cancellation.Dispose(); cancellation = new CancellationTokenSource();
                    Observe(building); building = null; error = "Point update cancelled; previous view retained.";
                }
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
                preview.BeginPreview(rect, GUIStyle.none); Camera camera = preview.camera; var bounds = renderer.Mesh.bounds;
                camera.backgroundColor = new Color(.1f, .1f, .1f); camera.clearFlags = CameraClearFlags.Color; camera.fieldOfView = FPMeshPreviewEditorUtility.DefaultFieldOfView;
                float distance = FPMeshPreviewEditorUtility.CalculateFitDistance(bounds, rect) * zoom;
                camera.transform.SetPositionAndRotation(bounds.center + pan - orbit * Vector3.forward * distance, orbit);
                camera.orthographic = projection == FPMeshPreviewProjection.Orthographic;
                camera.orthographicSize = FPMeshPreviewEditorUtility.CalculateOrthographicSize(bounds, rect, orbit) * zoom;
                float radius = Math.Max(.1f, bounds.extents.magnitude); camera.nearClipPlane = Math.Max(.001f, distance - radius * 2.4f); camera.farClipPlane = distance + radius * 3.4f;
                renderer.SetAppearance(pointColor, pointSize); preview.DrawMesh(renderer.Mesh, Matrix4x4.identity, renderer.Material, 0); preview.Render(true);
                previewTexture = preview.EndPreview(); GUI.DrawTexture(rect, previewTexture, ScaleMode.StretchToFill, false);
                FPMeshPreviewEditorUtility.DrawSceneOrientationGizmo(rect, camera, projection);
            }
            FPMeshPreviewEditorUtility.DrawOrbitGizmo(rect, (direction, up) => { orbit = Quaternion.LookRotation(direction, up); Repaint(); });
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
