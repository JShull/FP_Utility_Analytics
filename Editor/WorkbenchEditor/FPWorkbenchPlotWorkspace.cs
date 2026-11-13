// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.Reflection;
    using UnityEditor;
    using UnityEditorInternal;
    using UnityEngine;

    /// <summary>Native Unity split docking, isolated here because Unity exposes no public side-docking API.</summary>
    internal static class FPWorkbenchPlotWorkspace
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Type View = typeof(EditorWindow).Assembly.GetType("UnityEditor.View");
        private static readonly Type Dock = typeof(EditorWindow).Assembly.GetType("UnityEditor.DockArea");
        private static readonly Type Split = typeof(EditorWindow).Assembly.GetType("UnityEditor.SplitView");
        private static readonly FieldInfo Host = typeof(EditorWindow).GetField("m_Parent", Flags);
        private static int request;
        private static object Parent(object view) => view == null ? null : View?.GetProperty("parent", Flags)?.GetValue(view);
        private static Rect Bounds(object view) => (Rect)View.GetProperty("screenPosition", Flags).GetValue(view);
        internal static bool IsPaired(FPWorkbenchPlotSettingsWindow settings, FPWorkbenchFigureWindow figure)
        {
            if (settings == null || figure == null || Host == null || View == null || Split == null) return false;
            var left = Host.GetValue(settings); var right = Host.GetValue(figure); var parent = Parent(left);
            return left != null && right != null && left != right && parent != null && parent == Parent(right) && Split.IsInstanceOfType(parent) &&
                !(bool)Split.GetField("vertical", Flags).GetValue(parent) && Bounds(left).x < Bounds(right).x;
        }
        internal static void Open(FPWorkbenchPlotSettingsWindow settings, FPWorkbenchFigureWindow figure)
        {
            int current = ++request;
            // Moving native tabs during their OnGUI event invalidates the active GUI layout.
            EditorApplication.delayCall += () => {
                if (current != request || settings == null || figure == null) return;
                try { Pair(settings, figure); settings.WorkspaceError = null; }
                catch (Exception ex) { settings.WorkspaceError = "Could not arrange the plot workspace: " + ex.GetBaseException().Message + " Both tabs remain available for manual docking."; }
                if (settings != null) settings.Repaint();
            };
        }
        internal static void Pair(FPWorkbenchPlotSettingsWindow settings, FPWorkbenchFigureWindow figure)
        {
            if (IsPaired(settings, figure)) { settings.Show(); figure.Show(); settings.Focus(); return; }
            if (Host == null || View == null || Dock == null || Split == null) throw new NotSupportedException("This Unity version does not expose native split docking.");
            var dragSource = Dock.GetField("s_OriginalDragSource", Flags);
            var dragging = Dock.GetField("s_IsDragging", Flags);
            var dragOver = Split.GetMethod("DragOver", Flags);
            var dropType = typeof(EditorWindow).Assembly.GetType("UnityEditor.DropInfo");
            var dropRect = dropType?.GetField("rect", Flags); var dropArea = dropType?.GetField("dropArea", Flags);
            var perform = Split.GetMethod("PerformDrop", Flags);
            if (dragSource == null || dragging == null || dragOver == null || dropRect == null || dropArea == null || perform == null)
                throw new NotSupportedException("Native split-docking signatures changed.");
            if ((bool)dragging.GetValue(null)) throw new InvalidOperationException("Finish dragging the current tab, then reopen the workspace.");
            settings.Show(); figure.Show();
            if (!figure.docked) {
                var bounds = InternalEditorUtility.GetBoundsOfDesktopAtPoint(figure.position.center);
                float width = Mathf.Min(1400, bounds.width - 40), height = Mathf.Min(850, bounds.height - 80);
                var old = figure.position;
                figure.position = new Rect(Mathf.Clamp(old.x, bounds.x + 20, bounds.xMax - width - 20),
                    Mathf.Clamp(old.y, bounds.y + 40, bounds.yMax - height - 40), width, height);
            }
            var source = Host.GetValue(settings); var target = Host.GetValue(figure); var split = Parent(target);
            if (!Dock.IsInstanceOfType(source) || !Split.IsInstanceOfType(split)) throw new NotSupportedException("The figure has no dockable split host.");
            var area = Bounds(target); var mouse = new Vector2(area.x + 5, area.center.y);
            var info = dragOver.Invoke(split, new object[] { settings, mouse });
            if (info == null || !ReferenceEquals(dropArea.GetValue(info), split)) throw new InvalidOperationException("Unity refused the figure's left docking zone.");
            // Let Unity do the tab transfer, container cleanup, splitter creation and min-size reflow.
            // See UnityCsReference Editor/Mono/GUI/SplitView.cs (DragOver/PerformDrop).
            float leftWidth = Mathf.Clamp(area.width * .34f, settings.minSize.x, Mathf.Min(480, area.width - figure.minSize.x));
            dropRect.SetValue(info, new Rect(area.x, area.y, leftWidth, area.height));
            var previous = dragSource.GetValue(null);
            try {
                dragSource.SetValue(null, source);
                if (!(bool)perform.Invoke(split, new object[] { settings, info, mouse })) throw new InvalidOperationException("Unity did not accept the split.");
            } finally { dragSource.SetValue(null, previous); }
            if (!IsPaired(settings, figure)) throw new InvalidOperationException("The two tabs did not form a horizontal pair.");
            figure.Show(); settings.Focus();
        }
    }
}
