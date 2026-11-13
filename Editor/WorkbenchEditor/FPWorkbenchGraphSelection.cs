// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;

    /// <summary>Transient, per-graph point references. No source, cache, worker or session is retained.</summary>
    internal sealed class FPWorkbenchGraphSelection : IDisposable
    {
        internal sealed class Reference
        {
            internal string Workbook, Sheet, Dataset, Primary;
            internal long Row;
            internal string[] Columns;
            internal bool RowHeader;
        }
        private static readonly List<FPWorkbenchGraphSelection> live = new List<FPWorkbenchGraphSelection>();
        private static readonly Color[] palette = { new Color(1, .76f, .15f), new Color(1, .35f, .75f), new Color(.35f, 1, .5f),
            new Color(.4f, .65f, 1), new Color(1, .5f, .2f), new Color(.75f, .5f, 1), new Color(.15f, .9f, .9f), new Color(1, .35f, .35f) };
        internal readonly EditorWindow Owner;
        internal readonly Color Color;
        internal Reference[] References { get; private set; } = Array.Empty<Reference>();
        private Reference[] current = Array.Empty<Reference>(), pinned = Array.Empty<Reference>();
        private readonly int slot;
        private bool disposed;
        internal bool IsAlive => !disposed && Owner != null;
        internal FPWorkbenchGraphSelection(EditorWindow owner)
        {
            Owner = owner;
            while (live.Exists(s => s.IsAlive && s.slot == slot)) slot++;
            Color = slot < palette.Length ? palette[slot] : UnityEngine.Color.HSVToRGB((slot * .61803398875f) % 1, .7f, 1);
            live.Add(this);
        }
        internal static Reference Axes(string workbook, string sheet, FPWorkbenchCsvDataset dataset, FPWorkbenchFormulaProgram program,
            long row, int[] columns, int primary, bool rowHeader = true)
        {
            var table = new FPWorkbenchTableView(dataset, program); var ids = new string[columns.Length];
            for (int i = 0; i < columns.Length; i++) ids[i] = table.ColumnId(columns[i]);
            return new Reference { Workbook = workbook, Sheet = sheet, Dataset = dataset.DatasetId, Row = row, Columns = ids, Primary = table.ColumnId(primary), RowHeader = rowHeader };
        }
        internal static Reference Physical(string workbook, string dataset, long row, int[] columns)
        {
            var ids = new string[columns.Length]; for (int i = 0; i < ids.Length; i++) ids[i] = new FPWorkbenchCell(dataset, 0, columns[i]).ColumnId;
            return new Reference { Workbook = workbook, Dataset = dataset, Row = row, Columns = ids, Primary = ids.Length > 0 ? ids[0] : null, RowHeader = true };
        }
        internal static Reference Scalar(string workbook, string sheet, string dataset, FPWorkbenchSeriesBinding binding, long row)
        {
            string Source(int c) => new FPWorkbenchCell(dataset, 0, c).ColumnId;
            string y = binding.HasDerivedY ? dataset + ":derived:" + binding.YDerivedId : Source(binding.YColumn);
            var columns = new List<string> { y };
            if (binding.XMode == FPWorkbenchXMode.Numeric) columns.Add(binding.HasDerivedX ? dataset + ":derived:" + binding.XDerivedId : Source(binding.XColumn));
            else if (binding.XMode == FPWorkbenchXMode.UtcDateTime) { columns.Add(Source(binding.DateColumn)); columns.Add(Source(binding.XColumn)); }
            return new Reference { Workbook = workbook, Sheet = sheet, Dataset = dataset, Row = row, Columns = columns.ToArray(), Primary = y, RowHeader = true };
        }
        internal void Select(params Reference[] references)
        {
            if (!IsAlive) return;
            current = references ?? Array.Empty<Reference>(); MergeReferences();
            foreach (var window in Resources.FindObjectsOfTypeAll<FPDataWorkbenchWindow>()) {
                if (current.Length > 0) window.NavigateGraphSelection(this, current[0]);
                window.Repaint();
            }
        }
        private void MergeReferences() { var all = new List<Reference>(current); all.AddRange(pinned); References = all.ToArray(); }
        internal FPWorkbenchFigurePointReference[] CaptureReferences()
        {
            var result = new FPWorkbenchFigurePointReference[current.Length];
            for (int i = 0; i < current.Length; i++) { var r = current[i]; result[i] = new FPWorkbenchFigurePointReference { DatasetId = r.Dataset, SheetId = r.Sheet ?? "", Row = r.Row, Columns = (string[])r.Columns.Clone(), PrimaryColumn = r.Primary ?? "" }; }
            return result;
        }
        internal void SetPinned(string workbook, System.Collections.Generic.List<FPWorkbenchFigurePoint> points)
        {
            var refs = new List<Reference>(); foreach (var point in points) foreach (var r in point.References)
                refs.Add(new Reference { Workbook = workbook, Sheet = r.SheetId, Dataset = r.DatasetId, Row = r.Row, Columns = (string[])r.Columns.Clone(), Primary = r.PrimaryColumn, RowHeader = true });
            pinned = refs.ToArray(); MergeReferences(); RepaintTables();
        }
        internal void Clear() { current = Array.Empty<Reference>(); MergeReferences(); RepaintTables(); }
        public void Dispose() { if (disposed) return; disposed = true; live.Remove(this); pinned = Array.Empty<Reference>(); Clear(); }
        private static void RepaintTables() { foreach (var window in Resources.FindObjectsOfTypeAll<FPDataWorkbenchWindow>()) window.Repaint(); }
        internal static Color[] CellColors(string workbook, string dataset, long row, string column)
        {
            var colors = new List<Color>();
            foreach (var graph in live) if (graph.IsAlive)
                foreach (var cell in graph.References)
                    if (cell.Workbook == workbook && cell.Dataset == dataset && cell.Row == row &&
                        (column == null ? cell.RowHeader : Array.IndexOf(cell.Columns, column) >= 0)) { colors.Add(graph.Color); break; }
            return colors.ToArray();
        }
        internal static void DrawCell(string workbook, string dataset, long row, string column, Rect rect)
        {
            if (Event.current.type != EventType.Repaint) return;
            bool Matches(FPWorkbenchGraphSelection graph) {
                if (!graph.IsAlive) return false;
                foreach (var cell in graph.References) if (cell.Workbook == workbook && cell.Dataset == dataset && cell.Row == row &&
                    (column == null ? cell.RowHeader : Array.IndexOf(cell.Columns, column) >= 0)) return true;
                return false;
            }
            int count = 0; foreach (var graph in live) if (Matches(graph)) count++;
            if (count == 0) return;
            float stroke = Mathf.Min(2, rect.height / Math.Max(4, count * 3f)); int index = 0;
            foreach (var graph in live) if (Matches(graph)) {
                float inset = index++ * stroke * 1.5f; var r = new Rect(rect.x + inset, rect.y + inset, rect.width - inset * 2, rect.height - inset * 2);
                EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, stroke), graph.Color); EditorGUI.DrawRect(new Rect(r.x, r.yMax - stroke, r.width, stroke), graph.Color);
                EditorGUI.DrawRect(new Rect(r.x, r.y, stroke, r.height), graph.Color); EditorGUI.DrawRect(new Rect(r.xMax - stroke, r.y, stroke, r.height), graph.Color);
            }
        }

        internal void Ring(Vector2 point)
        {
            Handles.BeginGUI(); var previous = Handles.color;
            try { Handles.color = UnityEngine.Color.black; Handles.DrawWireDisc(point, Vector3.forward, 9); Handles.color = Color; Handles.DrawWireDisc(point, Vector3.forward, 7); }
            finally { Handles.color = previous; Handles.EndGUI(); }
        }
        internal void Legend(string text)
        {
            var old = GUI.color; GUI.color = Color;
            try { GUILayout.Label("● " + text, EditorStyles.miniLabel, GUILayout.MinWidth(0)); } finally { GUI.color = old; }
        }
    }
}
