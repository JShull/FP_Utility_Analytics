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
            var typedDefinitions = source != null && source.HasTypedSchema ? source.TypedSchema : null;
            if (sheet.ColumnOrder != null && ReferenceEquals(definitions, sheet.TableDefinitions) && ReferenceEquals(typedDefinitions, sheet.TableTypedDefinitions)) return;
            try
            {
                var typedSchema = typedDefinitions?.Compile(sheet.Dataset);
                var program = source?.CompileProgram(sheet.Dataset);
                var next = (program == null || program.ColumnCount == 0) && typedSchema == null ? null : new FPWorkbenchTableView(sheet.Dataset, program, typedSchema);
                var widths = new Dictionary<string, float>();
                if (sheet.SchemaIds != null)
                    for (int c = 0; c < Math.Min(sheet.SchemaIds.Length, sheet.Columns.ManualWidths.Length); c++) widths[sheet.SchemaIds[c]] = sheet.Columns.ManualWidths[c];
                sheet.Table = next; sheet.PageTable = next ?? new FPWorkbenchTableView(sheet.Dataset); sheet.TableProgram = program; sheet.TableDefinitions = definitions;
                sheet.TableTypedDefinitions = typedDefinitions; sheet.TableTypedSchema = typedSchema;
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
                        sheet.Columns.ManualWidths[c] = widths.TryGetValue(sheet.PageTable.ColumnId(c), out float width) ? width : 0;
                sheet.SchemaIds = new string[sheet.PageTable.ColumnCount];
                for (int c = 0; c < sheet.SchemaIds.Length; c++) sheet.SchemaIds[c] = sheet.PageTable.ColumnId(c);
                sheet.Columns.Projection ??= new FPWorkbenchColumnProjection(); sheet.Columns.Projection.BindSchema(sheet.SchemaIds);
                sheet.ColumnOrder = sheet.Columns.Projection.Resolve(sheet.SchemaIds);
                if (sheet.Selection != null) {
                    try { sheet.Selection.Resolve(sheet.PageTable); }
                    catch (System.IO.InvalidDataException) { sheet.Selection = null; sheet.RangeAnchorColumn = null; }
                }
                sheet.FirstColumn = Math.Min(sheet.FirstColumn, Math.Max(0, ScrollingCount(sheet) - 1));
                if (sheet == Current) { CancelVirtualPage(); page = null; failedPage = null; }
            }
            catch (Exception ex) { error = "Table schema: " + ex.Message; }
        }
        private void CancelVirtualPage()
        {
            Cancel(virtualCancellation); virtualCancellation = null; ReleaseAfter(virtualPageWork, pendingVirtualMemory); virtualPageWork = null; pendingVirtualMemory = null;
            virtualMemory?.Dispose(); virtualMemory = null;
            virtualPage = null; requestedVirtualKey = null; failedVirtualKey = null;
        }
        private bool TickVirtualPage(Sheet sheet)
        {
            if (sheet?.Dataset == null || sheet.Table == null && !UsesProjection(sheet)) { if (virtualPageWork != null || virtualPage != null) CancelVirtualPage(); return false; }
            if (pageWork != null || page != null || rawMemory != null) { pageCancel?.Cancel(); AbandonRawPage(); }
            var snapshot = sheet.ShowOriginal ? null : SnapshotFor(sheet.Dataset.DatasetId);
            int count = WindowCount(sheet, visibleColumns);
            int[] projected = new int[count]; for (int c = 0; c < count; c++) projected[c] = SchemaColumn(sheet, sheet.FirstColumn + c);
            string key = sheet.Id + ":" + sheet.FirstRow + ":" + string.Join(",", projected) + ":" + count + ":" + (sheet.TableProgram?.Fingerprint ?? "none") + ":" + sheet.PageTable.TypedSchemaFingerprint + ":" + (snapshot?.Fingerprint ?? "original");
            bool changed = false;
            if (virtualPageWork != null && virtualPageWork.IsCompleted)
            {
                if (virtualPageWork.Status == TaskStatus.RanToCompletion && key == requestedVirtualKey) {
                    virtualMemory?.Dispose(); virtualMemory = pendingVirtualMemory; pendingVirtualMemory = null;
                    virtualPage = virtualPageWork.Result;
                    virtualMemory.Complete(RetainedPageBytes(virtualPage.CharacterCount, virtualPage.Values.Length, count, true)); failedVirtualKey = null;
                }
                else if (virtualPageWork.IsFaulted) { error = virtualPageWork.Exception.GetBaseException().Message; if (key == requestedVirtualKey) failedVirtualKey = key; }
                pendingVirtualMemory?.Dispose(); pendingVirtualMemory = null;
                virtualPageWork = null; changed = true;
            }
            bool missing = virtualPage == null || virtualPage.DatasetId != sheet.Dataset.DatasetId || virtualPage.FormulaFingerprint != (sheet.TableProgram?.Fingerprint ?? "none") ||
                virtualPage.TypedSchemaFingerprint != sheet.PageTable.TypedSchemaFingerprint || virtualPage.CorrectionFingerprint != (snapshot?.Fingerprint ?? "original") || virtualPage.FirstRow != sheet.FirstRow || !PageColumnsMatch(sheet, virtualPage) ||
                virtualPage.Values.Length > 0 && virtualPage.Values[0].Length != count;
            if (missing && virtualPageWork == null && key != failedVirtualKey)
            {
                virtualMemory?.Dispose(); virtualMemory = null; virtualPage = null;
                if (AdmitPage(sheet.Dataset, out pendingVirtualMemory)) {
                    Cancel(virtualCancellation); virtualCancellation = new CancellationTokenSource(); requestedVirtualKey = key;
                    try { virtualPageWork = sheet.PageTable.ReadProjectedPageAsync(sheet.FirstRow, RowsPerPage, projected, snapshot, virtualCancellation.Token); }
                    catch { pendingVirtualMemory.Dispose(); pendingVirtualMemory = null; throw; }
                }
            }
            else if (virtualPageWork != null && key != requestedVirtualKey) virtualCancellation.Cancel();
            return changed;
        }
        private void DrawVirtualRows(Sheet sheet, Rect rect, float indexWidth, int columns)
        {
            string correction = sheet.ShowOriginal ? "original" : SnapshotFor(sheet.Dataset.DatasetId)?.Fingerprint ?? "original";
            if (virtualPage == null || virtualPage.DatasetId != sheet.Dataset.DatasetId || virtualPage.FirstRow != sheet.FirstRow || !PageColumnsMatch(sheet, virtualPage) ||
                virtualPage.FormulaFingerprint != (sheet.TableProgram?.Fingerprint ?? "none") || virtualPage.TypedSchemaFingerprint != sheet.PageTable.TypedSchemaFingerprint || virtualPage.CorrectionFingerprint != correction)
            { GUI.Label(new Rect(rect.x + 5, rect.y + 24, rect.width, 20), "Reading table page…"); return; }
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
                    if (cell.Typed != null && cell.Typed.Effective.State == FPWorkbenchValueState.Invalid) EditorGUI.DrawRect(cellRect, new Color(.8f, .2f, .15f, .2f));
                    else if (cell.Typed != null && cell.Typed.Effective.State == FPWorkbenchValueState.Missing) EditorGUI.DrawRect(cellRect, new Color(.2f, .45f, .7f, .15f));
                    DrawTableSelection(sheet, virtualPage.FirstRow + r, virtualPage.Columns[c], cellRect);
                    DrawLinkedCell(sheet, virtualPage.FirstRow + r, virtualPage.Columns[c], cellRect);
                    string tooltip = cell.Calculated ? "Calculated value; change its definition instead of editing source data." : "Inspect/edit the original source-addressed cell.";
                    if (cell.Typed != null) tooltip += "\nDeclared " + cell.Typed.Effective.Type + ": " + cell.Typed.Effective.State + " (underlying " + cell.Typed.Effective.ParsedState + "). Raw field text is retained.";
                    if (!GUI.Button(cellRect, new GUIContent(cell.Masked ? "[masked]" : cell.Text, tooltip), EditorStyles.label)) continue;
                    SelectTableRange(sheet, virtualPage.FirstRow + r, virtualPage.Columns[c], Event.current.shift);
                    if (cell.Calculated) { sheet.PlotColumn = virtualPage.Columns[c]; status = "Calculated " + sheet.Table.ColumnName(virtualPage.Columns[c]) + " • row " + (sheet.FirstRow + r + 1) + " • " + cell.Text; }
                    else
                    {
                        int column = virtualPage.Columns[c]; sheet.PlotColumn = column;
                        SelectCell(sheet, sheet.FirstRow + r, column, cell.Original);
                        status = "Row " + (sheet.FirstRow + r + 1) + ", column " + (column + 1) + " • source: " + cell.Original;
                    }
                }
                DrawRowRangeOverlay(sheet, virtualPage.FirstRow + r, new Rect(rect.x, y, rect.width, 20));
            }
        }
    }
}
