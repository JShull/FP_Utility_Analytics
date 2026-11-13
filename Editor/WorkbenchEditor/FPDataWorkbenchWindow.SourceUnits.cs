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
        [SerializeField] private bool unitCustomOverride;
        [SerializeField] private int unitPreset;
        private static readonly string[] UnitSymbols = { "", "1", "m", "cm", "mm", "km", "s", "ms", "us", "min", "h", "m/s", "m/s^2", "rad", "deg", "rad/s", "deg/s", "Hz", "kg", "g", "mg", "Pa", "kPa", "bar", "V", "mV", "A", "mA", "degC", "degF", "K", "psu" };
        private static readonly string[] UnitLabels = { "Unspecified", "Dimensionless (1)", "Metres (m)", "Centimetres (cm)", "Millimetres (mm)", "Kilometres (km)", "Seconds (s)", "Milliseconds (ms)", "Microseconds (us)", "Minutes (min)", "Hours (h)", "Velocity (m/s)", "Acceleration (m/s^2)", "Radians (rad)", "Degrees (deg)", "Angular velocity (rad/s)", "Angular velocity (deg/s)", "Frequency (Hz)", "Kilograms (kg)", "Grams (g)", "Milligrams (mg)", "Pressure (Pa)", "Pressure (kPa)", "Pressure (bar)", "Volts (V)", "Millivolts (mV)", "Amperes (A)", "Milliamperes (mA)", "Celsius (degC)", "Fahrenheit (degF)", "Kelvin (K)", "Practical salinity (psu)" };
        private static string SourceHeaderUnit(string name)
        {
            int end = name?.LastIndexOf(']') ?? -1, start = end < 0 ? -1 : name.LastIndexOf('[', end);
            return start >= 0 && end > start ? name.Substring(start + 1, end - start - 1).Trim() : "";
        }
        private static int FindUnitPreset(string value)
        {
            string normalized = (value ?? "").Trim().Replace("\u00b2", "^2").Replace("\u00b3", "^3");
            if (normalized == "\u00b0C") normalized = "degC"; else if (normalized == "\u00b0F") normalized = "degF";
            return Array.IndexOf(UnitSymbols, normalized);
        }
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
                string declared = definitions?.SourceUnits.Find(u => u.Column == unitColumn)?.Units;
                if (string.IsNullOrWhiteSpace(declared) && source != null && source.HasTypedSchema)
                    foreach (var column in source.TypedSchema.Columns) if (column.Column == unitColumn) { declared = column.Units; break; }
                unitDraft = declared ?? "";
                int known = FindUnitPreset(unitDraft); unitCustomOverride = known < 0;
                unitPreset = Math.Max(0, known);
                if (string.IsNullOrWhiteSpace(unitDraft)) { int suggested = FindUnitPreset(SourceHeaderUnit(sheet.Dataset.GetColumnName(unitColumn))); if (suggested > 0) { unitPreset = suggested; unitDraft = UnitSymbols[suggested]; } }
            }
            string header = SourceHeaderUnit(sheet.Dataset.GetColumnName(unitColumn));
            EditorGUILayout.LabelField("Header unit (label only)", string.IsNullOrEmpty(header) ? "None provided" : header);
            EditorGUILayout.LabelField("Applied units", ColumnUnitLabel(sheet, unitColumn));
            using (new EditorGUI.DisabledScope(unitCustomOverride)) {
                int next = EditorGUILayout.Popup("Unit preset", Mathf.Clamp(unitPreset, 0, UnitLabels.Length - 1), UnitLabels);
                if (next != unitPreset) { unitPreset = next; unitDraft = UnitSymbols[next]; }
            }
            bool custom = EditorGUILayout.Toggle("Custom override", unitCustomOverride);
            if (custom != unitCustomOverride) { unitCustomOverride = custom; if (!custom) unitDraft = UnitSymbols[unitPreset]; }
            if (unitCustomOverride) unitDraft = EditorGUILayout.TextField(new GUIContent("Declared units", "Blank means unspecified. Use symbolic products and integer powers, e.g. m/s^2; 1 means dimensionless."), unitDraft);
            EditorGUILayout.HelpBox("Presets/header suggestions are applied only with Apply declaration. A header unit is a label, not a numeric type or an applied declaration. Use Custom override for other symbolic units. No automatic conversion or calibration; applying validates dependent calculations.", MessageType.Info);
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
