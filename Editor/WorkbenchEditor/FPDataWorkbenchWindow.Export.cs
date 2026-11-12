// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using UnityEditor;
    using UnityEngine;

    public sealed partial class FPDataWorkbenchWindow
    {
        [SerializeField] private bool showExport;
        [SerializeField] private int exportFirstColumn, exportColumnCount = 1;
        [SerializeField] private long exportFirstRow, exportEndRow;
        [SerializeField] private string exportSheetId;
        [SerializeField] private FPWorkbenchExportInvalidPolicy exportInvalidPolicy;
        private Task<FPWorkbenchCsvExportResult> exportWork;
        private CancellationTokenSource exportCancellation;
        private string exportStatus;
        private void CancelExport()
        { Cancel(exportCancellation); exportCancellation = null; Observe(exportWork); exportWork = null; }
        private bool TickExport()
        {
            if (exportWork == null || !exportWork.IsCompleted) return false;
            try { var result = exportWork.GetAwaiter().GetResult(); exportStatus = "Exported " + (result.EndRow - result.FirstRow) + " rows; " + result.InvalidCells + " invalid calculated cells, " + result.MaskedCells + " masked cells."; }
            catch (OperationCanceledException) { exportStatus = "Export cancelled; no product published."; }
            catch (Exception ex) { exportStatus = "Export failed: " + ex.Message; }
            exportWork = null; exportCancellation?.Dispose(); exportCancellation = null; return true;
        }
        private void DrawExportControls(Sheet sheet)
        {
            showExport = EditorGUILayout.Foldout(showExport, "Materialized CSV export", true);
            if (!showExport) return;
            if (exportSheetId != sheet.Id)
            {
                exportSheetId = sheet.Id; exportFirstRow = 0; exportEndRow = sheet.Dataset.RowCount;
                exportFirstColumn = 0; exportColumnCount = Math.Min(32, DisplayColumnCount(sheet));
            }
            using (new EditorGUI.DisabledScope(exportWork != null))
            {
                exportFirstRow = EditorGUILayout.LongField("First row (0-based)", exportFirstRow);
                exportEndRow = EditorGUILayout.LongField("End row (exclusive)", exportEndRow);
                exportFirstColumn = EditorGUILayout.IntField("First column (1-based)", exportFirstColumn + 1) - 1;
                exportColumnCount = EditorGUILayout.IntField("Column count", exportColumnCount);
                exportInvalidPolicy = (FPWorkbenchExportInvalidPolicy)EditorGUILayout.EnumPopup("Invalid calculations", exportInvalidPolicy);
                EditorGUILayout.HelpBox((sheet.ShowOriginal ? "Original" : "Effective") + " layer, current calculations. Choose 1–256 consecutive columns. Exports a new folder containing CSV and provenance; existing products are not replaced.", MessageType.Info);
                if (GUILayout.Button("Export selected range…"))
                {
                    try
                    {
                        int total = DisplayColumnCount(sheet);
                        if (exportFirstColumn < 0 || exportColumnCount < 1 || exportColumnCount > 256 || exportFirstColumn > total - exportColumnCount) throw new ArgumentException("Choose columns inside this sheet (maximum 256).");
                        string path = EditorUtility.SaveFilePanel("Choose a new export folder name", "", sheet.Name + "-export", "fpexport");
                        if (!string.IsNullOrEmpty(path))
                        {
                            var selected = new int[exportColumnCount]; for (int c = 0; c < selected.Length; c++) selected[c] = exportFirstColumn + c;
                            exportCancellation = new CancellationTokenSource();
                            exportWork = FPWorkbenchCsvExport.WriteAsync(sheet.Dataset, path, selected, exportFirstRow, exportEndRow,
                                sheet.TableProgram, sheet.ShowOriginal ? null : SnapshotFor(sheet.Dataset.DatasetId), exportInvalidPolicy, cancellation: exportCancellation.Token);
                            exportStatus = "Writing " + Path.GetFileName(path) + " in background…";
                        }
                    }
                    catch (Exception ex) { exportStatus = "Export failed: " + ex.Message; exportCancellation?.Dispose(); exportCancellation = null; }
                }
            }
            if (exportWork != null && GUILayout.Button("Cancel export")) exportCancellation?.Cancel();
            if (!string.IsNullOrEmpty(exportStatus)) EditorGUILayout.HelpBox(exportStatus, MessageType.Info);
        }
    }
}
