// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using UnityEditor;
    using UnityEngine;

    public sealed partial class FPDataWorkbenchWindow
    {
        // Worker callbacks cannot resurrect retained preview contents after the owning sheet closes.
        private sealed class ImportPreviewReports : IProgress<FPWorkbenchCsvPreview>, IDisposable
        {
            private readonly object gate = new object();
            private FPWorkbenchCsvPreview pending;
            private bool closed;
            public void Report(FPWorkbenchCsvPreview value) { lock (gate) if (!closed) pending = value; }
            public FPWorkbenchCsvPreview Take() { lock (gate) { var value = pending; pending = null; return value; } }
            public void Dispose() { lock (gate) { closed = true; pending = null; } }
        }
        private static void ClearImportPreview(Sheet sheet)
        {
            sheet.PreviewReports?.Dispose(); sheet.PreviewReports = null; sheet.Preview = null;
        }
        private void DrawImportPreview(Sheet sheet)
        {
            var preview = sheet.Preview;
            EditorGUILayout.HelpBox("Unverified import preview • initial source rows only. Full coverage and content identity are not verified yet. Editing and plotting wait for indexing to finish.", MessageType.Warning);
            if (preview.TextLimitReached) EditorGUILayout.HelpBox("The preview text limit was reached. Full indexing continues; this is not an empty recording.", MessageType.Info);
            GUILayout.Label("Preview retains up to 32 rows / 32 columns. This panel shows up to 12 rows / 8 columns; long cell displays are shortened.", EditorStyles.miniLabel);
            Rect rect = GUILayoutUtility.GetRect(100, 10000, 100, 10000, GUILayout.ExpandHeight(true));
            if (Event.current.type != EventType.Repaint) return;
            EditorGUI.DrawRect(rect, new Color(.10f, .10f, .10f));
            int columns = Math.Min(preview.Headers.Count, Math.Max(0, Math.Min(8, (int)((rect.width - 64) / 110))));
            int rows = Math.Min(preview.Rows.Count, Math.Max(0, Math.Min(12, (int)(rect.height / 22) - 1)));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 64, rect.height), new Color(.15f, .19f, .21f));
            GUI.BeginClip(rect);
            try {
                GUI.Label(new Rect(4, 0, 58, 22), "Row #", EditorStyles.miniBoldLabel);
                string Display(string value) => value.Length > 128 ? value.Substring(0, 128) + "…" : value;
                for (int c = 0; c < columns; c++) GUI.Label(new Rect(68 + c * 110, 0, 106, 22), Display(preview.Headers[c]), EditorStyles.miniBoldLabel);
                for (int r = 0; r < rows; r++) {
                    GUI.Label(new Rect(4, (r + 1) * 22, 58, 22), (r + 1).ToString(), EditorStyles.miniLabel);
                    for (int c = 0; c < columns; c++) GUI.Label(new Rect(68 + c * 110, (r + 1) * 22, 106, 22), Display(preview.Rows[r][c]), EditorStyles.miniLabel);
                }
            } finally { GUI.EndClip(); }
        }
    }
}
