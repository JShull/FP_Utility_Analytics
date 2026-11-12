// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using UnityEditor;
    using UnityEngine;

    public sealed partial class FPDataWorkbenchWindow
    {
        private Task<FPWorkbenchTablePage> virtualPageWork;
        private FPWorkbenchTablePage virtualPage;
        private CancellationTokenSource virtualCancellation;
        private string requestedVirtualKey, failedVirtualKey;
        private int TableColumnCount(FPWorkbenchCsvDataset dataset)
        {
            var source = registeredSources?.Find(s => s.DatasetId == dataset.DatasetId);
            return dataset.ColumnCount + (source != null && source.HasCalculations ? source.Calculations.Columns.Count : 0);
        }
        private static int DisplayColumnCount(Sheet sheet) => sheet.Table?.ColumnCount ?? sheet.Dataset.ColumnCount;
        private void EnsureTableSchema(Sheet sheet)
        {
            var source = registeredSources?.Find(s => s.DatasetId == sheet.Dataset.DatasetId);
            var definitions = source != null && source.HasCalculations ? source.Calculations : null;
            if (ReferenceEquals(definitions, sheet.TableDefinitions)) return;
            try
            {
                var program = definitions == null ? null : FPWorkbenchFormulaProgram.Compile(sheet.Dataset, definitions);
                var next = program == null || program.ColumnCount == 0 ? null : new FPWorkbenchTableView(sheet.Dataset, program);
                var widths = new Dictionary<string, float>();
                if (sheet.Table != null)
                    for (int c = 0; c < Math.Min(sheet.Table.ColumnCount, sheet.Columns.ManualWidths.Length); c++) widths[sheet.Table.ColumnId(c)] = sheet.Columns.ManualWidths[c];
                sheet.Table = next; sheet.TableProgram = program; sheet.TableDefinitions = definitions;
                sheet.TableNames = next == null ? null : new string[next.ColumnCount];
                if (next != null) for (int c = 0; c < next.ColumnCount; c++) sheet.TableNames[c] = (c + 1) + ": " + (next.IsCalculated(c) ? "ƒ " : "") + next.ColumnName(c);
                if (sheet.Binding.HasDerivedY)
                {
                    try { sheet.PlotColumn = sheet.Dataset.ColumnCount + sheet.Binding.ResolveDerivedY(program); }
                    catch (ArgumentException) when (!sheet.ShowPlot || sheet.FigureLease?.Session.RequestedBinding.YDerivedId != sheet.Binding.YDerivedId) { sheet.Binding.YDerivedId = ""; sheet.PlotColumn = 0; }
                }
                if (sheet.Binding.HasDerivedX)
                    try { sheet.Binding.ResolveDerivedX(program); }
                    catch (ArgumentException) when (!sheet.ShowPlot || sheet.FigureLease?.Session.RequestedBinding.XDerivedId != sheet.Binding.XDerivedId) { sheet.Binding.XDerivedId = ""; sheet.Binding.XColumn = 0; }
                sheet.Columns.EnsureColumns(DisplayColumnCount(sheet));
                if (widths.Count > 0)
                    for (int c = 0; c < DisplayColumnCount(sheet); c++)
                        sheet.Columns.ManualWidths[c] = widths.TryGetValue(next == null ? sheet.Dataset.Cell(0, c).ColumnId : next.ColumnId(c), out float width) ? width : 0;
                sheet.FirstColumn = Math.Min(sheet.FirstColumn, DisplayColumnCount(sheet) - 1);
                if (sheet == Current) { CancelVirtualPage(); page = null; failedPage = null; }
            }
            catch (Exception ex) { error = "Calculated table schema: " + ex.Message; }
        }
        private void CancelVirtualPage()
        {
            Cancel(virtualCancellation); virtualCancellation = null; Observe(virtualPageWork); virtualPageWork = null;
            virtualPage = null; requestedVirtualKey = null; failedVirtualKey = null;
        }
        private bool TickVirtualPage(Sheet sheet)
        {
            if (sheet?.Table == null) { if (virtualPageWork != null || virtualPage != null) CancelVirtualPage(); return false; }
            if (pageWork != null) { pageCancel?.Cancel(); Observe(pageWork); pageWork = null; }
            var snapshot = sheet.ShowOriginal ? null : SnapshotFor(sheet.Dataset.DatasetId);
            int count = Math.Min(visibleColumns, sheet.Table.ColumnCount - sheet.FirstColumn);
            string key = sheet.Id + ":" + sheet.FirstRow + ":" + sheet.FirstColumn + ":" + count + ":" + sheet.TableProgram.Fingerprint + ":" + (snapshot?.Fingerprint ?? "original");
            bool changed = false;
            if (virtualPageWork != null && virtualPageWork.IsCompleted)
            {
                if (virtualPageWork.Status == TaskStatus.RanToCompletion && key == requestedVirtualKey) { virtualPage = virtualPageWork.Result; failedVirtualKey = null; }
                else if (virtualPageWork.IsFaulted) { error = virtualPageWork.Exception.GetBaseException().Message; if (key == requestedVirtualKey) failedVirtualKey = key; }
                virtualPageWork = null; changed = true;
            }
            bool missing = virtualPage == null || virtualPage.DatasetId != sheet.Dataset.DatasetId || virtualPage.FormulaFingerprint != sheet.TableProgram.Fingerprint ||
                virtualPage.CorrectionFingerprint != (snapshot?.Fingerprint ?? "original") || virtualPage.FirstRow != sheet.FirstRow || virtualPage.FirstColumn != sheet.FirstColumn ||
                virtualPage.Values.Length > 0 && virtualPage.Values[0].Length != count;
            if (missing && virtualPageWork == null && key != failedVirtualKey)
            {
                Cancel(virtualCancellation); virtualCancellation = new CancellationTokenSource(); requestedVirtualKey = key;
                virtualPageWork = sheet.Table.ReadPageAsync(sheet.FirstRow, RowsPerPage, sheet.FirstColumn, count, snapshot, virtualCancellation.Token);
            }
            else if (virtualPageWork != null && key != requestedVirtualKey) virtualCancellation.Cancel();
            return changed;
        }
        private void DrawVirtualRows(Sheet sheet, Rect rect, float indexWidth, int columns)
        {
            string correction = sheet.ShowOriginal ? "original" : SnapshotFor(sheet.Dataset.DatasetId)?.Fingerprint ?? "original";
            if (virtualPage == null || virtualPage.DatasetId != sheet.Dataset.DatasetId || virtualPage.FirstRow != sheet.FirstRow || virtualPage.FirstColumn != sheet.FirstColumn ||
                virtualPage.FormulaFingerprint != sheet.TableProgram.Fingerprint || virtualPage.CorrectionFingerprint != correction)
            { GUI.Label(new Rect(rect.x + 5, rect.y + 24, rect.width, 20), "Reading calculated table page…"); return; }
            int rows = Math.Min(virtualPage.Values.Length, Math.Max(0, (int)(rect.height / 20) - 1));
            for (int r = 0; r < rows; r++)
            {
                float y = rect.y + (r + 1) * 20, edge = rect.x + indexWidth;
                DrawLinkedRow(sheet, virtualPage.FirstRow + r, new Rect(rect.x, y, rect.width, 20));
                if (indexWidth > 0) GUI.Label(new Rect(rect.x + 3, y, indexWidth - 6, 20), (virtualPage.FirstRow + r + 1).ToString(CultureInfo.InvariantCulture), EditorStyles.miniLabel);
                for (int c = 0; c < columns; c++)
                {
                    Rect cellRect = new Rect(edge + 3, y, Math.Max(1, Math.Min(columnPixels[c] - 6, rect.xMax - edge - 3)), 20); edge += columnPixels[c];
                    if (c >= virtualPage.Values[r].Length) { GUI.Label(cellRect, "…", EditorStyles.miniLabel); continue; }
                    var cell = virtualPage.Values[r][c];
                    if (cell.Calculated) EditorGUI.DrawRect(cellRect, cell.CalculationStatus == FPWorkbenchFormulaStatus.Valid ? new Color(.4f, .3f, .65f, .12f) : new Color(.7f, .25f, .2f, .2f));
                    else if (cell.Masked || cell.Text != cell.Original) EditorGUI.DrawRect(cellRect, new Color(.65f, .4f, .1f, .2f));
                    DrawLinkedCell(sheet, virtualPage.FirstRow + r, sheet.FirstColumn + c, cellRect);
                    if (!GUI.Button(cellRect, new GUIContent(cell.Masked ? "[masked]" : cell.Text, cell.Calculated ? "Calculated value; change its definition instead of editing source data." : "Inspect/edit the original source-addressed cell."), EditorStyles.label)) continue;
                    if (cell.Calculated) { sheet.PlotColumn = sheet.FirstColumn + c; status = "Calculated " + sheet.Table.ColumnName(sheet.FirstColumn + c) + " • row " + (sheet.FirstRow + r + 1) + " • " + cell.Text; }
                    else
                    {
                        int column = sheet.FirstColumn + c; sheet.PlotColumn = column;
                        SelectCell(sheet, sheet.FirstRow + r, column, cell.Original);
                        status = "Row " + (sheet.FirstRow + r + 1) + ", column " + (column + 1) + " • source: " + cell.Original;
                    }
                }
            }
        }
    }
}
