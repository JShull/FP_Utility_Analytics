// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Tests
{
    using System;
    using System.Collections;
    using System.IO;
    using System.Reflection;
    using System.Text;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using FuzzPhyte.Utility.Analytics.WorkBench.Editor;

    public sealed class FPWorkbenchDerivedPlotWindowTests
    {
        [UnityTest] public IEnumerator DerivedYRebuildsOnEditsAndDetachedFigureRestoresItsFormula()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            string root = Path.Combine(Path.GetTempPath(), "FPDerivedPlotWindow-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            string source = Path.Combine(root, "source.csv"); var csv = new StringBuilder("x,y\n"); for (int i = 0; i < 1000; i++) csv.Append(i).Append(',').Append(i * 2).Append('\n'); File.WriteAllText(source, csv.ToString());
            var owner = ScriptableObject.CreateInstance<FPDataWorkbenchWindow>(); FPWorkbenchFigureWindow figure = null, restored = null; string cache = null;
            var type = owner.GetType();
            try
            {
                owner.ShowUtility(); owner.position = new Rect(60, 60, 1100, 750); type.GetMethod("AddCsv", flags).Invoke(owner, new object[] { source });
                object sheet = ((IList)type.GetField("sheets", flags).GetValue(owner))[0]; var sheetType = sheet.GetType(); var deadline = DateTime.UtcNow.AddSeconds(15);
                while (sheetType.GetField("Dataset").GetValue(sheet) == null && DateTime.UtcNow < deadline) yield return null;
                var dataset = (FPWorkbenchCsvDataset)sheetType.GetField("Dataset").GetValue(sheet); Assert.That(dataset, Is.Not.Null); cache = dataset.CacheDirectory;
                var definitions = new FPWorkbenchFormulaSet { DatasetId = dataset.DatasetId }; definitions.Columns.Add(new FPWorkbenchDerivedColumn { Name = "Sum", Expression = "C1 + C2" });
                var apply = type.GetMethod("ApplyCalculations", flags); Assert.That(apply.Invoke(owner, new object[] { sheet, definitions }), Is.True);
                yield return null; sheetType.GetField("PlotColumn").SetValue(sheet, 2);
                var binding = (FPWorkbenchSeriesBinding)sheetType.GetField("Binding").GetValue(sheet); binding.XMode = FPWorkbenchXMode.Numeric; binding.XColumn = 0;
                type.GetMethod("StartSeries", flags).Invoke(owner, new object[] { sheet, true });
                var session = ((FPWorkbenchFigureSession.Lease)sheetType.GetField("FigureLease").GetValue(sheet)).Session;
                while (session.View == null && DateTime.UtcNow < deadline) yield return null;
                Assert.That(session.View.Maximum, Is.EqualTo(2997)); Assert.That(session.YName, Is.EqualTo("Sum")); Assert.That(session.Series.Binding.YDerivedId, Is.EqualTo(definitions.Columns[0].Id));
                session.Settings.StartX = 123; session.Settings.EndX = 456;
                while ((session.View.StartX != 123 || session.IsBusy) && DateTime.UtcNow < deadline) yield return null;
                var next = session.CalculationProgram.Definition; next.Columns[0].Expression = "C2 - C1";
                Assert.That(apply.Invoke(owner, new object[] { sheet, next }), Is.True);
                while ((session.View == null || session.View.Maximum != 456) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(session.View.Maximum, Is.EqualTo(456)); Assert.That(session.Settings.StartX, Is.EqualTo(123));
                type.GetMethod("ApplyCorrection", flags).Invoke(owner, new object[] { sheet, 300L, 1, "600", "6000", FPWorkbenchEditKind.Number, "fixture", false });
                while ((session.View == null || session.View.Maximum != 5700) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(session.View.Maximum, Is.EqualTo(5700));
                Undo.PerformUndo(); while ((session.View == null || session.View.Maximum != 456) && DateTime.UtcNow < deadline) yield return null;
                Undo.PerformUndo(); while ((session.View == null || session.View.Maximum != 1368) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(session.View.Maximum, Is.EqualTo(1368)); Assert.That(session.Settings.StartX, Is.EqualTo(123));
                var document = (FPWorkbenchDocument)type.GetMethod("CaptureWorkbook", flags).Invoke(owner, null);
                Assert.That(document.Sheets[0].PlotColumn, Is.EqualTo(2)); string path = Path.Combine(root, "derived.fpworkbook");
                var saving = FPWorkbenchDocumentFile.SaveAsync(document, path); while (!saving.IsCompleted) yield return null;
                var loading = FPWorkbenchDocumentFile.LoadAsync(path); while (!loading.IsCompleted) yield return null; Assert.That(loading.IsFaulted, Is.False);
                var sheetDocument = document.Sheets[0]; string key = document.Id + ":" + sheetDocument.Id;
                figure = FPWorkbenchFigureWindow.Open(key, sheetDocument, dataset, session.CalculationProgram);
                var figureType = figure.GetType(); figureType.GetMethod("Capture", flags).Invoke(figure, null); string json = EditorJsonUtility.ToJson(figure);
                figure.Close(); figure = null; owner.Close();
                restored = ScriptableObject.CreateInstance<FPWorkbenchFigureWindow>(); figureType.GetMethod("OnDisable", flags).Invoke(restored, null);
                EditorJsonUtility.FromJsonOverwrite(json, restored); figureType.GetMethod("OnEnable", flags).Invoke(restored, null); restored.Show();
                FPWorkbenchFigureSession restoredSession = null;
                while (DateTime.UtcNow < deadline)
                { restoredSession = ((FPWorkbenchFigureSession.Lease)figureType.GetField("lease", flags).GetValue(restored))?.Session; if (restoredSession?.View != null) break; yield return null; }
                Assert.That(restoredSession?.View, Is.Not.Null); Assert.That(restoredSession.View.Maximum, Is.EqualTo(1368)); Assert.That(restoredSession.YName, Is.EqualTo("Sum"));
                Assert.That(File.ReadAllText(source), Is.EqualTo(csv.ToString()));
            }
            finally
            {
                if (figure != null) { figure.Close(); UnityEngine.Object.DestroyImmediate(figure); }
                if (restored != null) { restored.Close(); UnityEngine.Object.DestroyImmediate(restored); }
                owner.Close(); UnityEngine.Object.DestroyImmediate(owner); if (cache != null && Directory.Exists(cache)) Directory.Delete(cache, true); Directory.Delete(root, true);
            }
        }
    }
}
