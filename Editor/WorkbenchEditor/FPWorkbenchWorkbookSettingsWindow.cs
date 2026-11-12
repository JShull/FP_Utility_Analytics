// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using UnityEditor;
    using UnityEngine;

    /// <summary>Dockable controls for one Workbench window; the owner retains workbook state and workers.</summary>
    public sealed class FPWorkbenchWorkbookSettingsWindow : EditorWindow
    {
        [SerializeField] private FPDataWorkbenchWindow owner;
        [SerializeField] private string ownerId;
        [SerializeField] private Vector2 scroll;
        internal static FPWorkbenchWorkbookSettingsWindow Open(FPDataWorkbenchWindow workbench)
        {
            foreach (var existing in Resources.FindObjectsOfTypeAll<FPWorkbenchWorkbookSettingsWindow>())
                if (existing.owner == workbench || existing.ownerId == workbench.SettingsOwnerId) { existing.Show(); existing.Focus(); return existing; }
            var window = CreateInstance<FPWorkbenchWorkbookSettingsWindow>(); window.owner = workbench; window.ownerId = workbench.SettingsOwnerId;
            window.titleContent = new GUIContent("Workbook Settings"); window.Show();
            window.position = new Rect(workbench.position.x + 40, workbench.position.y + 40, 520, 680);
            return window;
        }
        private void OnEnable() { minSize = new Vector2(360, 300); }
        private void OnInspectorUpdate() { if (owner != null) Repaint(); }
        private void OnGUI()
        {
            if (owner == null && !string.IsNullOrEmpty(ownerId))
                foreach (var workbench in Resources.FindObjectsOfTypeAll<FPDataWorkbenchWindow>())
                    if (workbench.SettingsOwnerId == ownerId) { owner = workbench; break; }
            if (owner == null) {
                EditorGUILayout.HelpBox("The associated Data Workbench window is closed. Open Workbook Settings from the Workbench you want to manage.", MessageType.Info);
                return;
            }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            try {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
                    GUILayout.Label("Workbook Settings", EditorStyles.boldLabel);
                    EditorGUI.BeginChangeCheck(); owner.DrawWorkbookSettings();
                    if (EditorGUI.EndChangeCheck()) owner.Repaint();
                }
            }
            finally { EditorGUILayout.EndScrollView(); }
        }
    }
}
