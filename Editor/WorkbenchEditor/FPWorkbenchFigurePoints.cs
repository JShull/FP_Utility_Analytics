// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using UnityEditor;
    using UnityEngine;
    using FuzzPhyte.Utility.Editor;
    /// <summary>Resizable in-window divider shared by figure settings panels.</summary>
    [Serializable] internal sealed class FPWorkbenchFigureDivider
    {
        [SerializeField] private float width = 360;
        [NonSerialized] private float startMouse, startWidth;
        [NonSerialized] private Rect lastRect;
        internal float Width(float windowWidth) => Mathf.Clamp(width, 220, Mathf.Max(220, windowWidth - 300));
        internal void Draw(EditorWindow owner, int control)
        {
            Rect rect = GUILayoutUtility.GetRect(7, 7, 1, 10000, GUILayout.Width(7), GUILayout.ExpandHeight(true));
            if (Event.current.type == EventType.Repaint) lastRect = rect;
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);
            var e = Event.current;
            if (e.type == EventType.Repaint) EditorGUI.DrawRect(new Rect(rect.center.x - 1, rect.y, 2, rect.height), new Color(.55f, .55f, .55f, .55f));
            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition) && GUIUtility.hotControl == 0) {
                GUIUtility.hotControl = control; startMouse = e.mousePosition.x; startWidth = Width(owner.position.width); e.Use();
            } else if (GUIUtility.hotControl == control && e.type == EventType.MouseDrag) {
                width = Mathf.Clamp(startWidth + e.mousePosition.x - startMouse, 220, Mathf.Max(220, owner.position.width - 300));
                e.Use(); owner.Repaint();
            } else if (GUIUtility.hotControl == control && (e.type == EventType.MouseUp || e.type == EventType.Ignore)) {
                GUIUtility.hotControl = 0; e.Use(); owner.Repaint();
            }
        }
    }
    [Serializable] internal sealed class FPWorkbenchFigurePoints : IDisposable
    {
        public List<FPWorkbenchFigurePoint> Values = new List<FPWorkbenchFigurePoint>();
        [SerializeField] private string label = "Point of interest";
        [SerializeField] private bool manual;
        [SerializeField] private double x, y, z;
        [SerializeField] private int manualSeries;
        private string entryError;
        [Serializable] private sealed class Payload { public List<FPWorkbenchFigurePoint> Points; }
        [NonSerialized] private FPWorkbenchPresentationUndoState undo;
        [NonSerialized] private string observed;
        [NonSerialized] private Action undoRefresh;
        private string SavePoints() => JsonUtility.ToJson(new Payload { Points = Values });
        private void RestoreUndo()
        {
            if (undo == null || undo.Json == observed) return; observed = undo.Json;
            Values = JsonUtility.FromJson<Payload>(observed).Points; undoRefresh?.Invoke();
        }
        public void Dispose()
        {
            Undo.undoRedoPerformed -= RestoreUndo;
            if (undo != null) { Undo.ClearUndo(undo); UnityEngine.Object.DestroyImmediate(undo); undo = null; }
            undoRefresh = null;
        }
        private void EnsureUndo()
        {
            if (undo != null) return;
            undo = ScriptableObject.CreateInstance<FPWorkbenchPresentationUndoState>(); undo.hideFlags = HideFlags.HideAndDontSave;
            undo.Json = SavePoints(); observed = undo.Json; Undo.undoRedoPerformed += RestoreUndo;
        }
        internal void Add(FPWorkbenchFigurePoint value, string name, string axisKey)
        {
            var point = value.Copy(); point.Label = name; point.AxisKey = axisKey;
            var candidate = Snapshot(); candidate.Add(point); FPWorkbenchFigurePoint.Validate(candidate);
            EnsureUndo(); undo.Json = SavePoints(); observed = undo.Json;
            Undo.IncrementCurrentGroup(); Undo.RegisterCompleteObjectUndo(undo, "Pin figure point of interest");
            Values.Add(point); undo.Json = SavePoints(); observed = undo.Json; EditorUtility.SetDirty(undo); undoRefresh?.Invoke();
        }
        internal List<FPWorkbenchFigurePoint> Snapshot() => Values.ConvertAll(p => p.Copy());
        internal void Restore(List<FPWorkbenchFigurePoint> points) { Values = points == null ? new List<FPWorkbenchFigurePoint>() : points.ConvertAll(p => p.Copy()); }
        internal bool Draw(Func<FPWorkbenchFigurePoint> selected, string axisKey, bool hasZ, int seriesCount = 1, Action onUndo = null)
        {
            undoRefresh = onUndo;
            EnsureUndo();
            undo.Json = SavePoints(); observed = undo.Json;
            bool changed = false; FPMeshPreviewEditorUtility.DrawSectionDivider(); GUILayout.Label("Points of interest", EditorStyles.boldLabel);
            label = EditorGUILayout.TextField("Label", label); if (label.Length > 128) label = label.Substring(0, 128);
            using (new EditorGUI.DisabledScope(selected == null || Values.Count >= 16)) if (GUILayout.Button("Pin selected point")) {
                Add(selected(), label, axisKey); changed = true;
            }
            manual = EditorGUILayout.Foldout(manual, "Specify coordinates", true);
            if (manual) {
                if (seriesCount > 1) manualSeries = EditorGUILayout.IntSlider("Series", manualSeries + 1, 1, seriesCount) - 1;
                x = EditorGUILayout.DoubleField("X", x); y = EditorGUILayout.DoubleField("Y", y); if (hasZ) z = EditorGUILayout.DoubleField("Z", z);
                using (new EditorGUI.DisabledScope(Values.Count >= 16)) if (GUILayout.Button("Add coordinate annotation")) {
                    var point = new FPWorkbenchFigurePoint { Label = label, AxisKey = axisKey, X = x, Y = y, Z = hasZ ? z : 0, HasZ = hasZ, Series = Math.Min(manualSeries, Math.Max(0, seriesCount - 1)), Details = "Coordinate annotation" };
                    try { Add(point, label, axisKey); changed = true; entryError = null; } catch (System.IO.InvalidDataException ex) { entryError = ex.Message; }
                }
            }
            if (!string.IsNullOrEmpty(entryError)) EditorGUILayout.HelpBox(entryError, MessageType.Warning);
            for (int i = 0; i < Values.Count; i++) {
                var point = Values[i];
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
                    string name = EditorGUILayout.TextField((i + 1) + ".", point.Label);
                    if (name != point.Label) { point.Label = name.Length > 128 ? name.Substring(0, 128) : name; changed = true; }
                    bool include = EditorGUILayout.Toggle("Include in export", !point.ExcludeFromExport);
                    bool showValues = EditorGUILayout.Toggle("Show values in export", !point.HideExportValues);
                    if (include == point.ExcludeFromExport) { point.ExcludeFromExport = !include; changed = true; }
                    if (showValues == point.HideExportValues) { point.HideExportValues = !showValues; changed = true; }
                    EditorGUI.BeginChangeCheck();
                    bool customColor = EditorGUILayout.Toggle("Custom export color", point.UseExportColor);
                    Color exportColor = point.ExportColor;
                    if (customColor) exportColor = EditorGUILayout.ColorField("Export color", exportColor);
                    bool automatic = EditorGUILayout.Toggle("Automatic precision", point.ExportDecimalPlacesPlusOne == 0);
                    int decimals = point.ExportDecimalPlacesPlusOne == 0 ? 3 : point.ExportDecimalPlacesPlusOne - 1;
                    if (!automatic) decimals = EditorGUILayout.IntSlider("Decimal places", decimals, 0, 9);
                    bool atPoint = EditorGUILayout.Toggle("Label next to point", point.ExportLabelAtPoint);
                    bool showRow = EditorGUILayout.Toggle(new GUIContent("Show row in export", "Show the original source row number in this point's exported label."), !point.HideExportRow);
                    if (EditorGUI.EndChangeCheck()) {
                        point.UseExportColor = customColor; point.ExportColor = exportColor;
                        point.ExportDecimalPlacesPlusOne = automatic ? 0 : decimals + 1;
                        point.ExportLabelAtPoint = atPoint; point.HideExportRow = !showRow; changed = true;
                    }
                    GUILayout.Label(Coordinates(point), EditorStyles.wordWrappedMiniLabel);
                    if (point.AxisKey != axisKey) GUILayout.Label("Different binding • retained, hidden from current graph", EditorStyles.miniLabel);
                    if (GUILayout.Button("Remove point")) { Values.RemoveAt(i--); changed = true; }
                }
            }
            if (Values.Count > 0 && GUILayout.Button("Clear points of interest")) { Values.Clear(); changed = true; }
            if (changed && undo.Json != SavePoints()) {
                Undo.IncrementCurrentGroup(); Undo.RegisterCompleteObjectUndo(undo, "Edit figure points of interest");
                undo.Json = SavePoints(); observed = undo.Json; EditorUtility.SetDirty(undo);
            }
            return changed;
        }
        internal static string Coordinates(FPWorkbenchFigurePoint p)
        {
            string value = "X " + p.X.ToString("G9", CultureInfo.InvariantCulture) + " • Y " + p.Y.ToString("G9", CultureInfo.InvariantCulture);
            if (p.HasZ) value += " • Z " + p.Z.ToString("G9", CultureInfo.InvariantCulture);
            foreach (var r in p.References) value += " • row " + (r.Row + 1).ToString("N0");
            return value;
        }
        private static string Wrap(string value, int columns)
        {
            var lines = new List<string>(); value ??= "";
            while (value.Length > columns) { int split = value.LastIndexOf(' ', Math.Min(columns, value.Length - 1), Math.Min(columns, value.Length)); if (split < columns / 2) split = columns; lines.Add(value.Substring(0, split)); value = value.Substring(split).TrimStart(); }
            lines.Add(value); return string.Join("\n", lines);
        }
        private static string FormatValue(double value, FPWorkbenchFigurePoint point) =>
            value.ToString(point.ExportDecimalPlacesPlusOne == 0 ? "G9" : "F" + (point.ExportDecimalPlacesPlusOne - 1), CultureInfo.InvariantCulture);
        private static string Callout(FPWorkbenchFigurePoint point, int number, int columns)
        {
            string text = Wrap(number + ". " + point.Label, columns);
            if (!point.HideExportValues) {
                text += "\n" + Wrap("X: " + FormatValue(point.X, point), columns) + "\n" + Wrap("Y: " + FormatValue(point.Y, point), columns);
                if (point.HasZ) text += "\n" + Wrap("Z: " + FormatValue(point.Z, point), columns);
                if (!string.IsNullOrEmpty(point.Details) && !string.Equals(point.Details, "Source sample", StringComparison.Ordinal)) text += "\n" + Wrap(point.Details, columns);
            }
            if (!point.HideExportRow) foreach (var r in point.References) text += "\nRow: " + (r.Row + 1).ToString("N0");
            return text;
        }
        internal bool HasExportPoints(string axisKey) => Values.Exists(p => p.AxisKey == axisKey && !p.ExcludeFromExport);
        internal float BandWidth(int width, int height, string axisKey)
        {
            int active = 0; foreach (var point in Values) if (point.AxisKey == axisKey && !point.ExcludeFromExport && !point.ExportLabelAtPoint) active++;
            if (active == 0) return 0;
            if (width < 640) throw new ArgumentException("Points-of-interest exports need at least 640 pixels width.");
            float band = Mathf.Clamp(width * .23f, 190, 320); int columns = Math.Max(18, (int)((band - 20) / 7)); float needed = 180;
            for (int i = 0; i < Values.Count; i++) if (Values[i].AxisKey == axisKey && !Values[i].ExcludeFromExport && !Values[i].ExportLabelAtPoint) needed += (Callout(Values[i], i + 1, columns).Split('\n').Length + 1) * 13 + 18;
            if (needed > height) throw new ArgumentException("Increase PNG height or reduce the points of interest so every callout fits (needs at least " + Mathf.CeilToInt(needed) + " pixels height).");
            return band;
        }
        internal void Export(FPWorkbenchImageCanvas canvas, Rect plot, int width, int height, string axisKey,
            Func<FPWorkbenchFigurePoint, Vector2?> project, Color color, string sheetName)
        {
            float band = BandWidth(width, height, axisKey);
            float x = width - band + 8, y = height - 35; int columns = Math.Max(18, (int)((band - 20) / 7));
            if (band > 0) { canvas.Text("Points of interest", x, y, TextAnchor.UpperLeft); y -= 24; }
            for (int i = 0; i < Values.Count; i++) {
                var point = Values[i]; if (point.AxisKey != axisKey || point.ExcludeFromExport) continue;
                var screen = project(point); Color pointColor = point.UseExportColor ? point.ExportColor : color;
                if (screen.HasValue) {
                    var p = screen.Value;
                    for (int step = 0; step < 32; step++) {
                        float first = step * Mathf.PI / 16, last = (step + 1) * Mathf.PI / 16;
                        canvas.Line(p.x + Mathf.Cos(first) * 7, p.y + Mathf.Sin(first) * 7, p.x + Mathf.Cos(last) * 7, p.y + Mathf.Sin(last) * 7, pointColor, 2);
                    }
                    if (point.ExportLabelAtPoint) {
                        string inline = Callout(point, i + 1, 24);
                        int lines = inline.Split('\n').Length;
                        if (lines * 13 + 16 > height) throw new ArgumentException("Increase PNG height or shorten the point label so its annotation fits.");
                        float labelX = Mathf.Clamp(p.x + 12, 5, Mathf.Max(5, width - 180));
                        float labelY = Mathf.Clamp(p.y + 12 + lines * 13, lines * 13 + 5, height - 8);
                        canvas.Quad(labelX - 4, labelY - 8, 3, 8, pointColor);
                        canvas.Text(inline, labelX, labelY, TextAnchor.UpperLeft);
                    } else canvas.Text((i + 1).ToString(), p.x + 12, p.y + 12, TextAnchor.MiddleLeft);
                }
                if (point.ExportLabelAtPoint) continue;
                string text = Callout(point, i + 1, columns) + (screen.HasValue ? "" : "\n(outside current view)");
                canvas.Quad(x - 5, y - 8, 3, 8, pointColor); canvas.Text(text, x, y, TextAnchor.UpperLeft);
                y -= text.Split('\n').Length * 13 + 18;
            }
        }
        internal static Vector2? ProjectXY(FPWorkbenchFigurePoint point, Rect plot, double minX, double maxX, double minY, double maxY,
            FPWorkbenchAxisScale xScale, FPWorkbenchAxisScale yScale, bool invert, bool image)
        {
            if (point.X < minX || point.X > maxX || point.Y < minY || point.Y > maxY) return null;
            try {
                var x = new FPWorkbenchAxisMapping(minX, maxX, xScale); var y = new FPWorkbenchAxisMapping(minY, maxY, yScale); double fy = y.Fraction(point.Y);
                if (invert) fy = 1 - fy; if (!image) fy = 1 - fy;
                return new Vector2(plot.x + (float)x.Fraction(point.X) * plot.width, plot.y + (float)fy * plot.height);
            } catch (ArgumentException) { return null; }
        }
        internal void DrawMarkers(string axisKey, Func<FPWorkbenchFigurePoint, Vector2?> project, Color color)
        {
            if (Event.current.type != EventType.Repaint) return;
            Handles.BeginGUI(); var previous = Handles.color;
            try {
                for (int i = 0; i < Values.Count; i++) {
                    if (Values[i].AxisKey != axisKey) continue; var screen = project(Values[i]); if (!screen.HasValue) continue;
                    Handles.color = Color.black; Handles.DrawWireDisc(screen.Value, Vector3.forward, 9);
                    Handles.color = Values[i].UseExportColor ? Values[i].ExportColor : color; Handles.DrawWireDisc(screen.Value, Vector3.forward, 7);
                    GUI.Label(new Rect(screen.Value.x + 9, screen.Value.y - 14, 42, 20), (i + 1).ToString(), EditorStyles.boldLabel);
                }
            } finally { Handles.color = previous; Handles.EndGUI(); }
        }
    }
}
