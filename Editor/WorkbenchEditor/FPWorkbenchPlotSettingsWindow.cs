// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using UnityEditor;
    using UnityEngine;
    using FuzzPhyte.Utility.Editor;

    /// <summary>One dockable inspector for the focused scalar plot. The source window owns all sessions and workers.</summary>
    public sealed class FPWorkbenchPlotSettingsWindow : EditorWindow
    {
        [SerializeField] private FPDataWorkbenchWindow owner;
        [SerializeField] private string ownerId, sheetId;
        [SerializeField] private FPWorkbenchFigureWindow figure;
        [SerializeField] private string figureId;
        [SerializeField] private Vector2 scroll;
        private bool scrollToExport;
        internal string WorkspaceError { get; set; }
        [SerializeField] private bool binding = true, labels = true, range = true, style = true, grid = true, fit = true;

        internal static FPWorkbenchPlotSettingsWindow Open(FPDataWorkbenchWindow workbench)
        {
            var view = workbench.GetOrCreatePlotFigure();
            if (view != null) return Open(view);
            var window = GetWindow<FPWorkbenchPlotSettingsWindow>("Plot Settings");
            window.Target(workbench); window.Show(); window.Focus(); return window;
        }
        internal static FPWorkbenchPlotSettingsWindow Open(FPWorkbenchFigureWindow view)
        {
            var window = GetWindow<FPWorkbenchPlotSettingsWindow>("Plot Settings");
            window.Target(view); window.Show(); window.Focus(); FPWorkbenchPlotWorkspace.Open(window, view); return window;
        }
        internal static void OpenExport(FPWorkbenchFigureWindow view)
        {
            var window = Open(view); window.scrollToExport = true; window.Repaint();
        }
        private void OnEnable() { titleContent = new GUIContent("Plot Settings"); minSize = new Vector2(330, 280); }
        private void Target(FPDataWorkbenchWindow workbench)
        {
            owner = workbench; ownerId = workbench.SettingsOwnerId; sheetId = workbench.ActivePlotSheetId;
            figure = null; figureId = null;
        }
        private void Target(FPWorkbenchFigureWindow view)
        {
            figure = view; figureId = view.PlotSettingsId;
            FPDataWorkbenchWindow.FindPlotOwner(figureId, out owner, out sheetId); ownerId = owner == null ? null : owner.SettingsOwnerId;
        }
        private void FollowFocusedPlot()
        {
            if (FPWorkbenchPlotWorkspace.IsPaired(this, figure)) return;
            if (focusedWindow is FPDataWorkbenchWindow workbench) Target(workbench);
            else if (focusedWindow is FPWorkbenchFigureWindow view) Target(view);
        }
        private void ResolveTarget()
        {
            if (figure == null && !string.IsNullOrEmpty(figureId))
                foreach (var view in Resources.FindObjectsOfTypeAll<FPWorkbenchFigureWindow>())
                    if (view.PlotSettingsId == figureId) { figure = view; break; }
            if (owner == null && !string.IsNullOrEmpty(ownerId))
                foreach (var workbench in Resources.FindObjectsOfTypeAll<FPDataWorkbenchWindow>())
                    if (workbench.SettingsOwnerId == ownerId) { owner = workbench; break; }
            // A detached figure may outlive the sheet; it remains editable through its own session.
            if (owner != null && !owner.HasPlotSheet(sheetId)) owner = null;
            if (owner == null && figure != null && FPDataWorkbenchWindow.FindPlotOwner(figureId, out var found, out var id))
            { owner = found; ownerId = found.SettingsOwnerId; sheetId = id; }
        }
        private void OnInspectorUpdate() { FollowFocusedPlot(); ResolveTarget(); Repaint(); }
        private void OnGUI()
        {
            ResolveTarget();
            if (owner == null && figure == null) {
                EditorGUILayout.HelpBox("Open Plot Settings from a sheet or Figure Window. This panel follows the focused scalar plot and stays on that target while you edit.", MessageType.Info); return;
            }
            GUILayout.Label(owner != null ? owner.PlotSheetName(sheetId) : figure.PlotSettingsName, EditorStyles.boldLabel);
            GUILayout.Label(FPWorkbenchPlotWorkspace.IsPaired(this, figure) ? "Shared with this figure • drag either tab out to undock" : "Shared plot settings • focus a sheet or Figure Window to switch", EditorStyles.wordWrappedMiniLabel);
            if (!string.IsNullOrEmpty(WorkspaceError)) EditorGUILayout.HelpBox(WorkspaceError, MessageType.Warning);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            try {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
                    EditorGUI.BeginChangeCheck();
                    if (owner != null) owner.DrawPlotSettings(this, sheetId); else figure.DrawPlotSettings(this);
                    if (figure != null) figure.DrawExportSettings();
                    if (EditorGUI.EndChangeCheck()) { if (owner != null) owner.Repaint(); if (figure != null) figure.Repaint(); }
                }
            } finally { EditorGUILayout.EndScrollView(); }
            if (scrollToExport && Event.current.type == EventType.Repaint) { scroll.y = 100000; scrollToExport = false; Repaint(); }
        }
        private static bool Section(ref bool open, string title)
        {
            FPMeshPreviewEditorUtility.DrawSectionDivider(); open = EditorGUILayout.Foldout(open, title, true); return open;
        }
        internal bool BindingSection() => Section(ref binding, "Series binding");
        internal static bool BindingControls(FPWorkbenchSeriesBinding value, int sourceCount, FPWorkbenchFormulaProgram program,
            string[] sourceNames, string[] allNames, ref int yColumn)
        {
            yColumn = EditorGUILayout.Popup("Y column", Mathf.Clamp(yColumn, 0, allNames.Length - 1), allNames);
            var mode = (FPWorkbenchXMode)EditorGUILayout.EnumPopup("X axis", value.XMode);
            if (mode != value.XMode) { value.XMode = mode; value.XDerivedId = ""; }
            if (value.XMode == FPWorkbenchXMode.Numeric) {
                int shown = value.HasDerivedX ? sourceCount + value.ResolveDerivedX(program) : value.XColumn;
                int selected = EditorGUILayout.Popup("X column", shown, allNames);
                if (selected >= sourceCount) { value.XDerivedId = program.ColumnId(selected - sourceCount); value.XColumn = 0; }
                else { value.XDerivedId = ""; value.XColumn = selected; }
            } else if (value.XMode == FPWorkbenchXMode.UtcDateTime) {
                value.XColumn = EditorGUILayout.Popup("UTC time column", value.XColumn, sourceNames);
                value.DateColumn = EditorGUILayout.Popup("UTC date column", value.DateColumn, sourceNames);
                value.UtcFormat = EditorGUILayout.TextField(new GUIContent("UTC format", FPWorkbenchPlotView.UtcFormatTooltip), value.UtcFormat);
            }
            value.MaximumGap = EditorGUILayout.DoubleField(new GUIContent("Gap threshold", "0 disables gap detection. Units follow X; UTC uses seconds."), value.MaximumGap);
            return GUILayout.Button("Apply binding / build plot");
        }
        internal void PresentationControls(FPWorkbenchPlotSettings settings, FPWorkbenchSeriesCache series, FPWorkbenchSeriesView view, Action frameRows = null, bool canFrame = false)
        {
            if (Section(ref labels, "Labels & axes")) FPWorkbenchPlotView.LabelControls(settings);
            if (Section(ref range, "View range")) {
                settings.StartX = EditorGUILayout.DoubleField("X start", settings.StartX);
                settings.EndX = EditorGUILayout.DoubleField("X end", settings.EndX);
                settings.InvertY = EditorGUILayout.Toggle("Invert Y (depth)", settings.InvertY);
            }
            if (Section(ref style, "Plot style")) FPWorkbenchPlotView.StyleControls(settings);
            if (Section(ref grid, "Grid & Y limits")) {
                settings.Grid = EditorGUILayout.Toggle("Grid", settings.Grid);
                settings.XTickSpacing = EditorGUILayout.DoubleField("X grid spacing", settings.XTickSpacing);
                settings.YTickSpacing = EditorGUILayout.DoubleField("Y grid spacing", settings.YTickSpacing);
                settings.LockY = EditorGUILayout.Toggle("Lock Y", settings.LockY);
                if (settings.LockY) {
                    settings.MinimumY = EditorGUILayout.DoubleField("Y minimum", settings.MinimumY);
                    settings.MaximumY = EditorGUILayout.DoubleField("Y maximum", settings.MaximumY);
                }
            }
            if (Section(ref fit, "Frame & fit")) {
                if (frameRows != null) using (new EditorGUI.DisabledScope(!canFrame)) if (GUILayout.Button("Frame selected table rows")) frameRows();
                using (new EditorGUI.DisabledScope(series == null)) if (GUILayout.Button("Fit X")) settings.Fit(series.FirstX, series.LastX);
                using (new EditorGUI.DisabledScope(view == null || double.IsNaN(view.Minimum))) if (GUILayout.Button("Fit Y")) {
                    settings.MinimumY = view.Minimum; settings.MaximumY = view.Maximum;
                }
            }
        }
    }
}
