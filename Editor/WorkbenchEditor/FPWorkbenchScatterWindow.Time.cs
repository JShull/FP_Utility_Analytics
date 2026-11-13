// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.Globalization;
    using UnityEditor;
    using UnityEngine;
    public sealed partial class FPWorkbenchScatterWindow
    {
        [SerializeField] private FPWorkbenchSeriesBinding spatialX, appliedXBinding;
        private FPWorkbenchSeriesBinding buildXBinding;
        private void EnsureXCoordinate()
        {
            if (spatialX != null || dataset == null) return;
            var inherited = FPDataWorkbenchWindow.FigureXBinding(workbookId, sourceSheetId);
            spatialX = inherited?.XMode == FPWorkbenchXMode.UtcDateTime && inherited.XColumn == xColumn
                ? inherited.Copy(dataset.ColumnCount) : new FPWorkbenchSeriesBinding { XMode = FPWorkbenchXMode.Numeric, XColumn = Math.Min(xColumn, dataset.ColumnCount - 1) };
        }
        private FPWorkbenchSeriesBinding ScatterBinding(bool applied)
        {
            EnsureXCoordinate(); var value = (applied && appliedXBinding != null ? appliedXBinding : spatialX).Copy(dataset.ColumnCount);
            int x = applied && cache != null ? appliedX : xColumn, y = applied && cache != null ? appliedY : yColumn;
            value.Rows = rowRange.Copy(); value.YColumn = y < dataset.ColumnCount ? y : 0; value.YDerivedId = y < dataset.ColumnCount ? "" : program.ColumnId(y - dataset.ColumnCount);
            if (value.XMode == FPWorkbenchXMode.Numeric) { value.XColumn = x < dataset.ColumnCount ? x : 0; value.XDerivedId = x < dataset.ColumnCount ? "" : program.ColumnId(x - dataset.ColumnCount); }
            else { value.XColumn = value.XMode == FPWorkbenchXMode.Row ? 0 : x; value.XDerivedId = ""; }
            return value.Copy(dataset.ColumnCount);
        }
        private void DrawXCoordinateControls()
        {
            EnsureXCoordinate();
            spatialX.XMode = (FPWorkbenchXMode)EditorGUILayout.EnumPopup("X coordinate", spatialX.XMode);
            if (spatialX.XMode != FPWorkbenchXMode.Numeric) spatialX.XDerivedId = "";
            if (spatialX.XMode == FPWorkbenchXMode.UtcDateTime) {
                var physical = new string[dataset.ColumnCount]; Array.Copy(names, physical, physical.Length);
                spatialX.DateColumn = EditorGUILayout.Popup("UTC date column", spatialX.DateColumn, physical);
                spatialX.UtcFormat = EditorGUILayout.TextField(new GUIContent("UTC format", FPWorkbenchPlotView.UtcFormatTooltip), spatialX.UtcFormat);
                GUILayout.Label("UTC X uses elapsed seconds from the first included timestamp.", EditorStyles.wordWrappedMiniLabel);
            }
        }
        private string XCoordinateName => appliedXBinding?.XMode == FPWorkbenchXMode.UtcDateTime ? "Elapsed time (s)" : appliedXBinding?.XMode == FPWorkbenchXMode.Row ? "Source row (0-based)" : names[appliedX];
        private string UtcCaption => cache != null && appliedXBinding?.XMode == FPWorkbenchXMode.UtcDateTime ? "Elapsed s from " + new DateTime(cache.OriginUtcTicks, DateTimeKind.Utc).ToString("yyyy-MM-dd HH:mm:ss.ffffff 'UTC'", CultureInfo.InvariantCulture) : "Source-row identities retained in provenance";
    }
}
