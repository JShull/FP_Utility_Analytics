// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Tests
{
    using System;
    using System.Collections;
    using System.IO;
    using System.Reflection;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using FuzzPhyte.Utility.Analytics.WorkBench.Editor;

    public sealed class FPWorkbenchColumnDragTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        [UnityTest] public IEnumerator ColumnDropStagesYWithoutChangingViewportAndRejectsForeignIdentity()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPColumnDrag-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            string source = Path.Combine(root, "source.csv"); File.WriteAllText(source, "time,depth\n0,2\n1,3\n");
            var window = ScriptableObject.CreateInstance<FPDataWorkbenchWindow>(); string cache = null;
            try
            {
                window.ShowUtility(); window.position = new Rect(50, 50, 1100, 750);
                var type = window.GetType(); type.GetMethod("AddCsv", Private).Invoke(window, new object[] { source });
                object sheet = ((IList)type.GetField("sheets", Private).GetValue(window))[0]; var sheetType = sheet.GetType();
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (sheetType.GetField("Dataset").GetValue(sheet) == null && DateTime.UtcNow < deadline) yield return null;
                var dataset = (FPWorkbenchCsvDataset)sheetType.GetField("Dataset").GetValue(sheet); Assert.That(dataset, Is.Not.Null); cache = dataset.CacheDirectory;
                string workbook = (string)type.GetField("workbookId", Private).GetValue(window);
                var stage = type.GetMethod("StageColumnDrop", Private);
                Assert.That(stage.Invoke(window, new object[] { sheet, workbook, dataset.DatasetId, dataset.Cell(0, 1).ColumnId, 1, false }), Is.True);
                Assert.That((int)sheetType.GetField("PlotColumn").GetValue(sheet), Is.EqualTo(1));
                Assert.That((bool)sheetType.GetField("ShowPlot").GetValue(sheet), Is.False, "Drop stages a binding without rebuilding or changing the view.");
                Assert.That(stage.Invoke(window, new object[] { sheet, workbook, "foreign-source", dataset.Cell(0, 0).ColumnId, 0, false }), Is.False);
                Assert.That((int)sheetType.GetField("PlotColumn").GetValue(sheet), Is.EqualTo(1));
                Assert.That(stage.Invoke(window, new object[] { sheet, workbook, dataset.DatasetId, dataset.Cell(0, 1).ColumnId, 0, true }), Is.False, "Ordinal and stable identity must agree.");
                Assert.That(stage.Invoke(window, new object[] { sheet, workbook, dataset.DatasetId, dataset.Cell(0, 0).ColumnId, 0, true }), Is.True);
                var binding = (FPWorkbenchSeriesBinding)sheetType.GetField("Binding").GetValue(sheet);
                Assert.That(binding.XMode, Is.EqualTo(FPWorkbenchXMode.Numeric)); Assert.That(binding.XColumn, Is.Zero);
                var start = type.GetMethod("StartSeries", Private);
                start.Invoke(window, new object[] { sheet, false });
                var session = ((FPWorkbenchFigureSession.Lease)sheetType.GetField("FigureLease").GetValue(sheet)).Session;
                deadline = DateTime.UtcNow.AddSeconds(10);
                while ((session.View == null || session.Series.Binding.YColumn != 1) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(session.Series.Binding.YColumn, Is.EqualTo(1));
                sheetType.GetField("PlotColumn").SetValue(sheet, 0);
                start.Invoke(window, new object[] { sheet, false });
                while ((session.IsBusy || session.Series.Binding.YColumn != 0) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(session.Series.Binding.YColumn, Is.Zero);
                session.Settings.StartX = .25; session.Settings.EndX = .75; session.Settings.HasRange = true;
                sheetType.GetField("FirstRow").SetValue(sheet, 1L);
                Undo.PerformUndo();
                while ((session.IsBusy || session.Series.Binding.YColumn != 1) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(session.Series.Binding.YColumn, Is.EqualTo(1));
                Assert.That(session.Settings.StartX, Is.EqualTo(.25)); Assert.That(session.Settings.EndX, Is.EqualTo(.75));
                Assert.That((long)sheetType.GetField("FirstRow").GetValue(sheet), Is.EqualTo(1));
                Undo.PerformRedo();
                while ((session.IsBusy || session.Series.Binding.YColumn != 0) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(session.Series.Binding.YColumn, Is.Zero); Assert.That(session.Settings.StartX, Is.EqualTo(.25));
                var definitions = new FPWorkbenchFormulaSet { DatasetId = dataset.DatasetId };
                definitions.Columns.Add(new FPWorkbenchDerivedColumn { Name = "Sum", Expression = "C1 + C2" });
                definitions.Columns.Add(new FPWorkbenchDerivedColumn { Name = "Difference", Expression = "C2 - C1" });
                var apply = type.GetMethod("ApplyCalculations", Private); Assert.That(apply.Invoke(window, new object[] { sheet, definitions }), Is.True);
                yield return null;
                var table = (FPWorkbenchTableView)sheetType.GetField("Table").GetValue(sheet); Assert.That(table, Is.Not.Null);
                string derivedId = definitions.Columns[0].Id, columnId = table.ColumnId(2);
                var stageDerived = type.GetMethod("StageDerivedColumnDrop", Private);
                Assert.That(stageDerived.Invoke(window, new object[] { sheet, workbook, dataset.DatasetId, columnId, derivedId, false }), Is.True);
                Assert.That((int)sheetType.GetField("PlotColumn").GetValue(sheet), Is.EqualTo(2)); Assert.That(session.Settings.StartX, Is.EqualTo(.25));
                Assert.That(stageDerived.Invoke(window, new object[] { sheet, workbook, dataset.DatasetId, columnId, derivedId, true }), Is.True);
                Assert.That(((FPWorkbenchSeriesBinding)sheetType.GetField("Binding").GetValue(sheet)).XDerivedId, Is.EqualTo(derivedId));
                Assert.That(stageDerived.Invoke(window, new object[] { sheet, workbook, "foreign", columnId, derivedId, false }), Is.False);
                Assert.That(stageDerived.Invoke(window, new object[] { sheet, workbook, dataset.DatasetId, dataset.Cell(0, 0).ColumnId, derivedId, false }), Is.False);
                var program = (FPWorkbenchFormulaProgram)sheetType.GetField("TableProgram").GetValue(sheet); var reordered = program.Definition; reordered.Columns.Reverse();
                Assert.That(apply.Invoke(window, new object[] { sheet, reordered }), Is.True); yield return null;
                Assert.That(stageDerived.Invoke(window, new object[] { sheet, workbook, dataset.DatasetId, columnId, derivedId, false }), Is.True);
                Assert.That((int)sheetType.GetField("PlotColumn").GetValue(sheet), Is.EqualTo(3), "Drop follows stable ID after definition reorder.");
                start.Invoke(window, new object[] { sheet, true });
                while ((session.View == null || session.IsBusy || session.Series.Binding.YDerivedId != derivedId) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(session.Series.Binding.YDerivedId, Is.EqualTo(derivedId)); Assert.That(session.View.Maximum, Is.EqualTo(4));
            }
            finally
            {
                DragAndDrop.SetGenericData("FuzzPhyte.Workbench.Column.v1", null); window.Close(); UnityEngine.Object.DestroyImmediate(window);
                if (cache != null && Directory.Exists(cache)) Directory.Delete(cache, true); Directory.Delete(root, true);
            }
        }
    }
}
