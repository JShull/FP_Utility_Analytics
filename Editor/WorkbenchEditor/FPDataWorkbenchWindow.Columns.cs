// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using UnityEditor;
    using UnityEngine;

    public sealed partial class FPDataWorkbenchWindow
    {
        private float[] columnPixels = Array.Empty<float>();
        private Sheet sizingSheet;
        private int sizingColumn = -1, columnControl;
        private float sizingStartX, sizingStartWidth;
        private static string ColumnPreferenceKey(string id) => "FuzzPhyte.Analytics.Workbench.ColumnWidths." + id;
        private void RestoreColumns(Sheet sheet)
        {
            string json = EditorPrefs.GetString(ColumnPreferenceKey(sheet.Dataset.DatasetId), "");
            try { sheet.Columns = json.Length > 0 && json.Length <= FPWorkbenchDocumentFile.MaximumDocumentBytes ? JsonUtility.FromJson<FPWorkbenchColumnLayout>(json) : null; }
            catch (ArgumentException) { sheet.Columns = null; }
            if (sheet.Columns == null || sheet.Columns.Version < 1 || sheet.Columns.Version > 2) sheet.Columns = new FPWorkbenchColumnLayout();
            sheet.Columns.EnsureColumns(TableColumnCount(sheet.Dataset));
            try { sheet.Columns.Projection ??= new FPWorkbenchColumnProjection(); sheet.Columns.Projection.Validate(); }
            catch (System.IO.InvalidDataException) { sheet.Columns.Projection = new FPWorkbenchColumnProjection(); }
        }
        private static void SaveColumns(Sheet sheet)
        {
            if (sheet?.Dataset != null && sheet.Columns != null)
                EditorPrefs.SetString(ColumnPreferenceKey(sheet.Dataset.DatasetId), JsonUtility.ToJson(sheet.Columns));
        }
        private void ColumnInput()
        {
            columnControl = GUIUtility.GetControlID(731653, FocusType.Passive);
            if (sizingColumn == -1 || sizingSheet == null || GUIUtility.hotControl != columnControl) return;
            var e = Event.current;
            if (e.type == EventType.MouseDrag)
            {
                if (sizingColumn == -2) sizingSheet.Columns.RowHeaderWidth = Mathf.Clamp(sizingStartWidth + e.mousePosition.x - sizingStartX, 40, 320);
                else sizingSheet.Columns.ManualWidths[sizingColumn] = Mathf.Clamp(sizingStartWidth + e.mousePosition.x - sizingStartX, 40, 1600);
                Repaint(); e.Use();
            }
            else if (e.type == EventType.MouseUp || (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape))
            {
                SaveColumns(sizingSheet); sizingColumn = -1; sizingSheet = null; GUIUtility.hotControl = 0; e.Use(); Repaint();
            }
        }
        private void ColumnGrip(Sheet sheet, int column, Rect grip, float width)
        {
            EditorGUIUtility.AddCursorRect(grip, MouseCursor.ResizeHorizontal);
            var e = Event.current;
            if (e.type == EventType.ContextClick && grip.Contains(e.mousePosition))
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("Reset to Auto"), sheet.Columns.ManualWidths[column] == 0, () => ResetColumn(sheet, column));
                menu.ShowAsContext(); e.Use();
            }
            else if (e.type == EventType.MouseDown && e.button == 0 && grip.Contains(e.mousePosition))
            {
                if (e.clickCount == 2) ResetColumn(sheet, column);
                else { sizingSheet = sheet; sizingColumn = column; sizingStartX = e.mousePosition.x; sizingStartWidth = width; GUIUtility.hotControl = columnControl; }
                e.Use();
            }
            if (e.type == EventType.Repaint) EditorGUI.DrawRect(new Rect(grip.center.x, grip.y + 2, 1, grip.height - 4), new Color(1, 1, 1, .2f));
        }
        private void ResetColumn(Sheet sheet, int column)
        {
            sheet.Columns.ManualWidths[column] = 0; SaveColumns(sheet); Repaint();
        }
        private static float IndexWidth(Sheet sheet) => sheet.Columns.ShowRowHeader ? sheet.Columns.RowHeaderWidth : 0;
        private void RowHeaderGrip(Sheet sheet, Rect header)
        {
            var edge = new Rect(header.xMax - 4, header.y, 8, 20);
            EditorGUIUtility.AddCursorRect(edge, MouseCursor.ResizeHorizontal);
            var e = Event.current;
            if (e.type == EventType.ContextClick && header.Contains(e.mousePosition))
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("Hide Row #"), false, () => { sheet.Columns.ShowRowHeader = false; SaveColumns(sheet); Repaint(); });
                menu.AddItem(new GUIContent("Reset width"), false, () => { sheet.Columns.RowHeaderWidth = 76; SaveColumns(sheet); Repaint(); });
                menu.ShowAsContext(); e.Use();
            }
            else if (e.type == EventType.MouseDown && e.button == 0 && edge.Contains(e.mousePosition))
            {
                if (e.clickCount == 2) { sheet.Columns.RowHeaderWidth = 76; SaveColumns(sheet); Repaint(); }
                else { sizingSheet = sheet; sizingColumn = -2; sizingStartX = e.mousePosition.x; sizingStartWidth = header.width; GUIUtility.hotControl = columnControl; }
                e.Use();
            }
        }
    }
}
