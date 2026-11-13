// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using UnityEditor;
    using UnityEngine;
    public sealed partial class FPDataWorkbenchWindow
    {
        private FPWorkbenchGraphSelection pendingGraph;
        private FPWorkbenchGraphSelection.Reference pendingGraphReference;
        private bool graphSelectionNavigation;
        internal void NavigateGraphSelection(FPWorkbenchGraphSelection graph, FPWorkbenchGraphSelection.Reference reference)
        {
            if (reference.Workbook != workbookId) return;
            pendingGraph = graph; pendingGraphReference = reference; graphSelectionNavigation = true;
            var source = registeredSources.Find(s => s.DatasetId == reference.Dataset);
            if (!sheets.Exists(s => s.Dataset?.DatasetId == reference.Dataset) && source != null) OpenRegisteredSheet(source);
            TickGraphNavigation(); Repaint();
        }
        private bool TickGraphNavigation()
        {
            if (pendingGraphReference == null) return false;
            if (pendingGraph == null || !pendingGraph.IsAlive || Array.IndexOf(pendingGraph.References, pendingGraphReference) < 0) { pendingGraph = null; pendingGraphReference = null; return false; }
            var target = pendingGraphReference;
            if (target.Workbook != workbookId) { pendingGraph = null; pendingGraphReference = null; return false; }
            int index = sheets.FindIndex(s => s.Dataset?.DatasetId == target.Dataset && s.Id == target.Sheet);
            if (index < 0) index = sheets.FindIndex(s => s.Dataset?.DatasetId == target.Dataset);
            if (index < 0) return false;
            var sheet = sheets[index]; EnsureTableSchema(sheet);
            if (target.Row < 0 || target.Row >= sheet.Dataset.RowCount) { pendingGraphReference = null; return false; }
            var projection = JsonUtility.FromJson<FPWorkbenchColumnProjection>(JsonUtility.ToJson(sheet.Columns.Projection)); string before = JsonUtility.ToJson(projection);
            foreach (string id in target.Columns) if (Array.IndexOf(sheet.SchemaIds, id) >= 0) projection.SetHidden(id, false);
            if (before != JsonUtility.ToJson(projection)) {
                sheet.ProjectionUndo ??= new FPWorkbenchColumnProjectionUndo(value => { if (sheets.Contains(sheet)) ApplyColumnProjection(sheet, value); });
                sheet.ProjectionUndo.Record(before, JsonUtility.ToJson(projection)); ApplyColumnProjection(sheet, projection);
            }
            int y = Array.IndexOf(sheet.SchemaIds, target.Primary); int offset = 0;
            if (y >= 0 && Array.IndexOf(sheet.Columns.Projection.Frozen, target.Primary) < 0) {
                foreach (int column in sheet.ColumnOrder) { if (Array.IndexOf(sheet.Columns.Projection.Frozen, sheet.SchemaIds[column]) >= 0) continue; if (column == y) break; offset++; }
                sheet.FirstColumn = offset;
            }
            active = index; sheet.FirstRow = target.Row; selectedDataset = null; page = null; failedPage = null; CancelVirtualPage();
            status = "Graph selection: row " + (target.Row + 1) + ". Colored axis cells remain marked while their graphs are open. Table uses its current Original/Effective layer.";
            pendingGraph = null; pendingGraphReference = null; return true;
        }
    }
}
