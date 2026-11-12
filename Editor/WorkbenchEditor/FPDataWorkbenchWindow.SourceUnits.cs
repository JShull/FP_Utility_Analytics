// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using UnityEditor;
    using UnityEngine;

    public sealed partial class FPDataWorkbenchWindow
    {
        [SerializeField] private bool showSourceUnits;
        [SerializeField] private int unitColumn;
        [SerializeField] private string unitDraft = "";
        private string unitDataset;
        private int loadedUnitColumn = -1;
        private FPWorkbenchFormulaSet loadedUnitDefinitions;
        private bool ApplySourceUnit(Sheet sheet, int column, string units)
        {
            if (column < 0 || column >= sheet.Dataset.ColumnCount) { error = "Choose a physical source column."; return false; }
            var source = registeredSources.Find(s => s.DatasetId == sheet.Dataset.DatasetId);
            var current = source != null && source.HasCalculations ? source.Calculations : null;
            var next = current == null ? new FPWorkbenchFormulaSet { DatasetId = sheet.Dataset.DatasetId } : JsonUtility.FromJson<FPWorkbenchFormulaSet>(JsonUtility.ToJson(current));
            next.SourceUnits.RemoveAll(u => u.Column == column);
            if (!string.IsNullOrWhiteSpace(units)) next.SourceUnits.Add(new FPWorkbenchSourceUnit { Column = column, Units = units.Trim() });
            bool changed = ApplyCalculations(sheet, next);
            if (changed) status = "Source unit declaration applied; dependent calculations validated. Measurements were not converted.";
            return changed;
        }
        private void DrawSourceUnitControls(Sheet sheet)
        {
            showSourceUnits = EditorGUILayout.Foldout(showSourceUnits, "Source units", true); if (!showSourceUnits) return;
            unitColumn = EditorGUILayout.Popup("Source column", Mathf.Clamp(unitColumn, 0, sheet.Dataset.ColumnCount - 1), sheet.ColumnNames);
            var source = registeredSources.Find(s => s.DatasetId == sheet.Dataset.DatasetId);
            var definitions = source != null && source.HasCalculations ? source.Calculations : null;
            if (unitDataset != sheet.Dataset.DatasetId || loadedUnitColumn != unitColumn || !ReferenceEquals(loadedUnitDefinitions, definitions))
            {
                unitDataset = sheet.Dataset.DatasetId; loadedUnitColumn = unitColumn; loadedUnitDefinitions = definitions;
                unitDraft = definitions?.SourceUnits.Find(u => u.Column == unitColumn)?.Units ?? "";
            }
            unitDraft = EditorGUILayout.TextField(new GUIContent("Declared units", "Blank means unspecified. Use symbolic products and integer powers, e.g. m/s^2; 1 means dimensionless."), unitDraft);
            EditorGUILayout.HelpBox("Declare units from trusted metadata. Blank: unknown; 1: dimensionless. Symbols are case-sensitive: m and cm are distinct. No automatic conversion or calibration. Applying validates all dependent calculations.", MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Apply declaration")) ApplySourceUnit(sheet, unitColumn, unitDraft);
                if (GUILayout.Button("Clear declaration")) ApplySourceUnit(sheet, unitColumn, "");
            }
        }
        private static string ColumnUnitLabel(Sheet sheet, int column)
        {
            if (sheet.TableProgram == null) return "unspecified";
            return column < sheet.Dataset.ColumnCount ? sheet.TableProgram.SourceUnits(column) : sheet.TableProgram.Units(column - sheet.Dataset.ColumnCount);
        }
    }
}
