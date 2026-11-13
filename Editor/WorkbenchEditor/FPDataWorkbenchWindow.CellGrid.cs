// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using UnityEditor;
    using UnityEngine;
    public sealed partial class FPDataWorkbenchWindow
    {
        private void DrawCellGridControls(Sheet sheet)
        {
            sheet.CellGrid = EditorGUILayout.Toggle("Cell grid", sheet.CellGrid);
            if (sheet.CellGrid) sheet.CellGridOpacity = EditorGUILayout.Slider("Grid opacity", sheet.CellGridOpacity, 0, 1);
        }
        private void DrawCellGrid(Sheet sheet, Rect rect, float indexWidth, int columns)
        {
            if (Event.current.type != EventType.Repaint || !sheet.CellGrid || sheet.CellGridOpacity <= 0) return;
            // Match the existing table/header separator palette; appearance never changes data or cache requests.
            var color = new Color(.45f, .53f, .61f, sheet.CellGridOpacity);
            float pixel = 1f / EditorGUIUtility.pixelsPerPoint;
            int rows = (int)Math.Min(Math.Max(0, sheet.Dataset.RowCount - sheet.FirstRow), Math.Max(0, Math.Min(RowsPerPage, (int)(rect.height / 20) - 1)));
            float bottom = Math.Min(rect.yMax, rect.y + (rows + 1) * 20);
            for (int r = 0; r <= rows + 1; r++) {
                float y = Math.Min(bottom - pixel, rect.y + r * 20);
                EditorGUI.DrawRect(new Rect(rect.x, y, rect.width, pixel), color);
            }
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, pixel, bottom - rect.y), color);
            float edge = rect.x + indexWidth;
            if (indexWidth > 0) EditorGUI.DrawRect(new Rect(edge - pixel, rect.y, pixel, bottom - rect.y), color);
            for (int c = 0; c < columns; c++) {
                edge = Math.Min(rect.xMax, edge + columnPixels[c]);
                EditorGUI.DrawRect(new Rect(edge - pixel, rect.y, pixel, bottom - rect.y), color);
            }
        }
    }
}
