// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using UnityEditor;
    using UnityEngine;

    /// <summary>Verifies source revisions and previews bounded exact-key/shared-row mappings before value binding.</summary>
    public sealed class FPWorkbenchAlignmentWindow : EditorWindow
    {
        [Serializable] private sealed class Request { public string Left, Right; public FPWorkbenchAlignmentOptions Options; public bool Effective; }
        [SerializeField] private string workbook, lastRequest;
        [SerializeField] private FPWorkbenchSourceDocument[] sources = Array.Empty<FPWorkbenchSourceDocument>();
        [SerializeField] private FPWorkbenchChangeSet[] corrections = Array.Empty<FPWorkbenchChangeSet>();
        [SerializeField] private int leftSelection, rightSelection;
        [SerializeField] private FPWorkbenchAlignmentOptions options = new FPWorkbenchAlignmentOptions();
        [SerializeField] private bool effective;
        [SerializeField] private int leftValueColumn, rightValueColumn;
        [SerializeField] private FPWorkbenchExportInvalidPolicy valueInvalidPolicy;
        private Task<FPWorkbenchAlignedValues> materializing;
        private FPWorkbenchScatterWindow generatedPlot;
        [SerializeField] private long firstPair;
        private string[] sourceNames, leftNames, rightNames, leftValueNames, rightValueNames;
        private FPWorkbenchFormulaProgram leftProgram, rightProgram;
        private FPWorkbenchCsvDataset left, right;
        private Task<FPWorkbenchCsvDataset[]> admitting;
        private Task<FPWorkbenchAlignment> building;
        private Task<FPWorkbenchAlignedRows[]> paging;
        private FPWorkbenchAlignment alignment;
        private FPWorkbenchAlignmentMetadata metadata;
        private FPWorkbenchAlignedRows[] pairs;
        private CancellationTokenSource operation, pageCancellation;
        private bool restoreAfterAdmission;
        private long requestedPage, displayedPage = -1, failedPage = -1;
        private string buildRequest, error;
        private Vector2 scroll;
        private bool Busy => admitting != null || building != null || materializing != null;
        public static FPWorkbenchAlignmentWindow Open(string owner, FPWorkbenchSourceDocument[] registered, FPWorkbenchChangeSet[] edits)
        {
            if (edits != null && edits.Length > 64) throw new ArgumentException("At most 64 correction snapshots are supported.");
            if (edits != null) foreach (var edit in edits) { if (edit == null) throw new ArgumentException("Missing correction snapshot."); edit.Validate(); }
            if (registered == null || registered.Length < 1 || registered.Length > 64) throw new ArgumentException("Choose 1–64 registered sources.");
            var copy = new FPWorkbenchSourceDocument[registered.Length];
            for (int i = 0; i < copy.Length; i++)
            {
                var source = registered[i];
                copy[i] = new FPWorkbenchSourceDocument { DatasetId = source.DatasetId, SourcePath = source.SourcePath, CacheDirectory = source.CacheDirectory, Name = source.Name, Import = source.Import.Copy(), HasCalculations = source.HasCalculations,
                    Calculations = source.HasCalculations ? JsonUtility.FromJson<FPWorkbenchFormulaSet>(JsonUtility.ToJson(source.Calculations)) : null }; copy[i].Validate();
            }
            var window = CreateInstance<FPWorkbenchAlignmentWindow>(); window.workbook = owner; window.sources = copy;
            window.corrections = edits == null ? Array.Empty<FPWorkbenchChangeSet>() : Array.ConvertAll(edits, set => JsonUtility.FromJson<FPWorkbenchChangeSet>(JsonUtility.ToJson(set)));
            window.rightSelection = Math.Min(1, copy.Length - 1); window.InitializeNames(); window.BeginAdmission(false);
            window.titleContent = new GUIContent("Cross-sheet Alignment"); window.Show(); return window;
        }
        private void OnEnable()
        {
            minSize = new Vector2(760, 520); EditorApplication.update += Tick; InitializeNames();
            if (sources.Length == 0) return;
            try
            {
                bool restore = !string.IsNullOrEmpty(lastRequest);
                if (restore)
                {
                    var request = JsonUtility.FromJson<Request>(lastRequest);
                    int a = Array.FindIndex(sources, s => s.DatasetId == request.Left), b = Array.FindIndex(sources, s => s.DatasetId == request.Right);
                    if (a < 0 || b < 0) throw new InvalidDataException("Saved alignment source is no longer registered.");
                    if (request.Options == null) throw new InvalidDataException("Saved alignment options are missing.");
                    leftSelection = a; rightSelection = b; options = request.Options; effective = request.Effective;
                }
                BeginAdmission(restore);
            }
            catch (Exception ex) { error = ex.Message; }
        }
        private void OnDisable()
        {
            EditorApplication.update -= Tick; CancelOperations(); alignment = null; metadata = null; pairs = null; left = null; right = null;
        }
        private void InitializeNames()
        {
            options ??= new FPWorkbenchAlignmentOptions();
            sources ??= Array.Empty<FPWorkbenchSourceDocument>(); corrections ??= Array.Empty<FPWorkbenchChangeSet>();
            sourceNames = new string[sources.Length]; for (int i = 0; i < sources.Length; i++) sourceNames[i] = (i + 1) + ": " + sources[i].Name;
        }
        private static void Observe(Task work) { if (work != null) work.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted); }
        private void CancelOperations()
        {
            operation?.Cancel(); operation?.Dispose(); operation = null; pageCancellation?.Cancel(); pageCancellation?.Dispose(); pageCancellation = null;
            Observe(materializing); materializing = null;
            Observe(admitting); Observe(building); Observe(paging); admitting = null; building = null; paging = null;
        }
        private void BeginAdmission(bool restore)
        {
            CancelOperations(); left = null; right = null; leftNames = null; rightNames = null;
            leftSelection = Mathf.Clamp(leftSelection, 0, sources.Length - 1); rightSelection = Mathf.Clamp(rightSelection, 0, sources.Length - 1);
            operation = new CancellationTokenSource(); restoreAfterAdmission = restore; error = null;
            admitting = Admit(sources[leftSelection], sources[rightSelection], operation.Token);
        }
        private static async Task<FPWorkbenchCsvDataset[]> Admit(FPWorkbenchSourceDocument a, FPWorkbenchSourceDocument b, CancellationToken token)
        {
            async Task<FPWorkbenchCsvDataset> One(FPWorkbenchSourceDocument reference)
            {
                reference.Validate(); var data = await FPWorkbenchCsvDataset.OpenAsync(reference.CacheDirectory, token).ConfigureAwait(false);
                var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (data.DatasetId != reference.DatasetId || !string.Equals(Path.GetFullPath(data.SourcePath), Path.GetFullPath(reference.SourcePath), comparison) || JsonUtility.ToJson(data.ImportProfile) != JsonUtility.ToJson(reference.Import))
                    throw new InvalidDataException("Registered alignment source identity/path/profile differs.");
                return data;
            }
            var left = await One(a).ConfigureAwait(false); var right = a.DatasetId == b.DatasetId && a.CacheDirectory == b.CacheDirectory ? left : await One(b).ConfigureAwait(false);
            return new[] { left, right };
        }
        private void BeginBuild()
        {
            if (Busy || left == null || right == null) return;
            try
            {
                var request = new Request { Left = left.DatasetId, Right = right.DatasetId, Effective = effective, Options = JsonUtility.FromJson<FPWorkbenchAlignmentOptions>(JsonUtility.ToJson(options)) };
                FPWorkbenchChangeSnapshot Changes(string id) => effective ? Array.Find(corrections, set => set.DatasetId == id)?.Snapshot() : null;
                building = FPWorkbenchAlignment.BuildAsync(left, right, request.Options, Changes(left.DatasetId), Changes(right.DatasetId), operation.Token);
                buildRequest = JsonUtility.ToJson(request); error = null;
            }
            catch (Exception ex) { error = ex.Message; }
        }
        private void Tick()
        {
            bool repaint = false;
            if (materializing != null && materializing.IsCompleted)
            {
                var task = materializing; materializing = null;
                try { var values = task.GetAwaiter().GetResult(); generatedPlot = FPWorkbenchScatterWindow.OpenAligned(workbook, values); }
                catch (OperationCanceledException) { }
                catch (Exception ex) { error = ex.Message; } repaint = true;
            }
            if (admitting != null && admitting.IsCompleted)
            {
                var task = admitting; admitting = null;
                try
                {
                    var data = task.GetAwaiter().GetResult(); left = data[0]; right = data[1];
                    string[] Names(FPWorkbenchCsvDataset source) { var names = new string[source.ColumnCount]; for (int c = 0; c < names.Length; c++) names[c] = (c + 1) + ": " + source.GetColumnName(c); return names; }
                    leftNames = Names(left); rightNames = Names(right);
                    FPWorkbenchFormulaProgram Program(FPWorkbenchCsvDataset data, int selection) => sources[selection].HasCalculations ? FPWorkbenchFormulaProgram.Compile(data, sources[selection].Calculations) : null;
                    leftProgram = Program(left, leftSelection); rightProgram = Program(right, rightSelection);
                    string[] ValueNames(string[] physical, FPWorkbenchFormulaProgram program) {
                        var names = new string[physical.Length + (program?.ColumnCount ?? 0)]; Array.Copy(physical, names, physical.Length);
                        for (int i = physical.Length; i < names.Length; i++) names[i] = "Calculated: " + program.Definition.Columns[i - physical.Length].Name;
                        return names;
                    }
                    leftValueNames = ValueNames(leftNames, leftProgram); rightValueNames = ValueNames(rightNames, rightProgram);
                    if (restoreAfterAdmission && options.Mode == FPWorkbenchAlignmentMode.ExactKey && (options.LeftKeyColumn < 0 || options.LeftKeyColumn >= left.ColumnCount || options.RightKeyColumn < 0 || options.RightKeyColumn >= right.ColumnCount)) throw new InvalidDataException("Saved alignment keys are outside the verified source schema.");
                    options.LeftKeyColumn = Mathf.Clamp(options.LeftKeyColumn, 0, left.ColumnCount - 1); options.RightKeyColumn = Mathf.Clamp(options.RightKeyColumn, 0, right.ColumnCount - 1);
                    if (restoreAfterAdmission) BeginBuild();
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { error = ex.Message; } repaint = true;
            }
            if (building != null && building.IsCompleted)
            {
                var task = building; building = null;
                try
                {
                    var result = task.GetAwaiter().GetResult(); pageCancellation?.Cancel(); Observe(paging); paging = null;
                    alignment = result; metadata = result.Metadata; lastRequest = buildRequest; pairs = null; displayedPage = -1; failedPage = -1;
                    firstPair = Math.Max(0, Math.Min(firstPair, Math.Max(0, metadata.MatchedPairs - 1)));
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { error = ex.Message; } repaint = true;
            }
            if (paging != null && firstPair != requestedPage) pageCancellation.Cancel();
            if (paging != null && paging.IsCompleted)
            {
                var task = paging; paging = null;
                try { var result = task.GetAwaiter().GetResult(); if (firstPair == requestedPage) { pairs = result; displayedPage = requestedPage; } }
                catch (OperationCanceledException) { }
                catch (Exception ex) { if (firstPair == requestedPage) { error = ex.Message; failedPage = requestedPage; } } repaint = true;
            }
            if (alignment != null && paging == null && displayedPage != firstPair && failedPage != firstPair)
            {
                pageCancellation?.Dispose(); pageCancellation = new CancellationTokenSource(); requestedPage = firstPair;
                paging = alignment.ReadPageAsync(firstPair, 128, pageCancellation.Token); repaint = true;
            }
            if (repaint || Busy) Repaint();
        }
        private void OnGUI()
        {
            if (sources.Length == 0) { EditorGUILayout.HelpBox("Open this window from a workbook with registered sources.", MessageType.Info); return; }
            using (new EditorGUI.DisabledScope(Busy))
            {
                int a = EditorGUILayout.Popup("Left source", leftSelection, sourceNames), b = EditorGUILayout.Popup("Right source", rightSelection, sourceNames);
                if (a != leftSelection || b != rightSelection) { leftSelection = a; rightSelection = b; BeginAdmission(false); }
                if ((left == null || right == null) && GUILayout.Button("Verify selected sources")) BeginAdmission(false);
                if (left != null && right != null)
                {
                    options.Mode = (FPWorkbenchAlignmentMode)EditorGUILayout.EnumPopup("Pairing", options.Mode);
                    if (options.Mode == FPWorkbenchAlignmentMode.ExactKey)
                    {
                        options.LeftKeyColumn = EditorGUILayout.Popup("Left key", options.LeftKeyColumn, leftNames);
                        options.RightKeyColumn = EditorGUILayout.Popup("Right key", options.RightKeyColumn, rightNames);
                        options.KeyKind = (FPWorkbenchAlignmentKeyKind)EditorGUILayout.EnumPopup("Key comparison", options.KeyKind);
                        options.KeyDomain = EditorGUILayout.TextField(new GUIContent("Common key domain", "Explicit common identity/clock domain. No timezone, clock or unit mapping is inferred."), options.KeyDomain);
                        options.Duplicates = (FPWorkbenchDuplicateKeyPolicy)EditorGUILayout.EnumPopup("Duplicate keys", options.Duplicates);
                        if (options.KeyKind == FPWorkbenchAlignmentKeyKind.Decimal)
                        { options.LeftUnits = EditorGUILayout.TextField("Left key units", options.LeftUnits); options.RightUnits = EditorGUILayout.TextField("Right key units", options.RightUnits); }
                    }
                    effective = EditorGUILayout.Toggle("Use frozen corrections", effective);
                    if (GUILayout.Button("Build / verify alignment")) BeginBuild();
                }
            }
            if (Busy)
            {
                GUILayout.Label(admitting != null ? "Verifying source revisions in background…" : materializing != null ? "Preparing matched X/Y values and plot…" : "Building exact mapping in background…", EditorStyles.miniLabel);
                if (GUILayout.Button("Cancel operation")) { CancelOperations(); operation = new CancellationTokenSource(); error = "Operation cancelled; previous result retained."; }
            }
            if (error != null) EditorGUILayout.HelpBox(error, MessageType.Warning);
            if (metadata == null) { EditorGUILayout.HelpBox("Choose verified sources and an explicit pairing policy. Exact keys must already be sorted; no positional matching is inferred. This window inspects row mappings before value/plot binding.", MessageType.Info); return; }
            if (left != null && right != null && left.DatasetId == metadata.LeftDatasetId && right.DatasetId == metadata.RightDatasetId)
            {
                using (new EditorGUI.DisabledScope(Busy || metadata.MatchedPairs == 0))
                {
                    GUILayout.Label("Generate plot from this mapping", EditorStyles.boldLabel);
                    leftValueColumn = EditorGUILayout.Popup("X from left source", Mathf.Clamp(leftValueColumn, 0, leftValueNames.Length - 1), leftValueNames);
                    rightValueColumn = EditorGUILayout.Popup("Y from right source", Mathf.Clamp(rightValueColumn, 0, rightValueNames.Length - 1), rightValueNames);
                    valueInvalidPolicy = (FPWorkbenchExportInvalidPolicy)EditorGUILayout.EnumPopup("Invalid numeric values", valueInvalidPolicy);
                    if (GUILayout.Button("Open aligned scatter plot")) BeginPlot();
                }
            }
            GUILayout.Label("Result: " + Name(metadata.LeftDatasetId) + " ↔ " + Name(metadata.RightDatasetId) + " • " + metadata.Options.Mode, EditorStyles.boldLabel);
            GUILayout.Label("Last successful result • L " + (metadata.HasLeftCorrectionSnapshot ? "Effective" : "Original") + " / R " + (metadata.HasRightCorrectionSnapshot ? "Effective" : "Original") +
                (metadata.Options.Mode == FPWorkbenchAlignmentMode.ExactKey ? " • " + metadata.Options.KeyDomain + " • " + metadata.Options.KeyKind : " • verified shared row identity"), EditorStyles.miniLabel);
            GUILayout.Label("Matched " + metadata.MatchedPairs + " • unmatched L/R " + metadata.UnmatchedLeftRows + "/" + metadata.UnmatchedRightRows + " • ambiguous L/R " + metadata.AmbiguousLeftRows + "/" + metadata.AmbiguousRightRows, EditorStyles.miniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(firstPair == 0)) if (GUILayout.Button("Previous 128")) firstPair = Math.Max(0, firstPair - 128);
                long input = EditorGUILayout.LongField("First matched pair", firstPair + 1); firstPair = Math.Max(0, Math.Min(input <= 1 ? 0 : input - 1, Math.Max(0, metadata.MatchedPairs - 1)));
                using (new EditorGUI.DisabledScope(firstPair + 128 >= metadata.MatchedPairs)) if (GUILayout.Button("Next 128")) firstPair += 128;
            }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            if (pairs != null && displayedPage == firstPair)
                for (int i = 0; i < pairs.Length; i++)
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label((firstPair + i + 1).ToString(), GUILayout.Width(65)); var pair = pairs[i];
                        if (GUILayout.Button("Reveal left row " + (pair.LeftRow + 1))) Reveal(metadata.LeftDatasetId, pair.LeftRow, metadata.Options.LeftKeyColumn);
                        if (GUILayout.Button("Reveal right row " + (pair.RightRow + 1))) Reveal(metadata.RightDatasetId, pair.RightRow, metadata.Options.RightKeyColumn);
                    }
            else GUILayout.Label(paging != null ? "Reading matched pair page…" : "No matched pairs.");
            EditorGUILayout.EndScrollView();
        }
        private void BeginPlot()
        {
            if (Busy || alignment == null || metadata.MatchedPairs == 0) return;
            try { materializing = FPWorkbenchAlignedValues.BuildAsync(alignment, leftValueColumn, rightValueColumn, leftProgram, rightProgram, invalidPolicy: valueInvalidPolicy, cancellation: operation.Token); error = null; }
            catch (Exception ex) { error = ex.Message; }
        }
        private string Name(string id) => Array.Find(sources, source => source.DatasetId == id)?.Name ?? id;
        private void Reveal(string id, long row, int keyColumn)
        {
            int[] columns = metadata.Options.Mode == FPWorkbenchAlignmentMode.SharedSourceRows ? Array.Empty<int>() : new[] { keyColumn };
            if (!FPDataWorkbenchWindow.RevealFigureRow(workbook, id, row, columns)) error = "Open the owning workbook and free a sheet tab to reveal this row.";
        }
    }
}
