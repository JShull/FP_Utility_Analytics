// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Tests
{
    using System;
    using System.Collections;
    using System.IO;
    using System.Reflection;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using FuzzPhyte.Utility.Analytics.WorkBench.Editor;

    public sealed class FPWorkbenchDerivedHistogramWindowTests
    {
        [UnityTest] public IEnumerator CalculatedHistogramFreezesFormulaAndCorrectionThroughWorkbookReopen()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            string root = Path.Combine(Path.GetTempPath(), "FPDerivedHistogram-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            string source = Path.Combine(root, "source.csv"); File.WriteAllText(source, "value\n0\n1\n2\n3\n");
            var import = FPWorkbenchCsvDataset.ImportAsync(source, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile()); while (!import.IsCompleted) yield return null;
            Assert.That(import.IsFaulted, Is.False); var data = import.Result;
            var definitions = new FPWorkbenchFormulaSet { DatasetId = data.DatasetId }; definitions.Columns.Add(new FPWorkbenchDerivedColumn { Name = "Scaled", Expression = "C1 * 2" });
            var program = FPWorkbenchFormulaProgram.Compile(data, definitions); var changes = new FPWorkbenchChangeSet { DatasetId = data.DatasetId };
            changes.Set(data.Cell(3, 0), "3", "9", FPWorkbenchEditKind.Number, "fixture");
            FPWorkbenchHistogramWindow first = null, restored = null; var type = typeof(FPWorkbenchHistogramWindow);
            FPWorkbenchHistogramResult Result(FPWorkbenchHistogramWindow window) => (FPWorkbenchHistogramResult)type.GetField("result", flags).GetValue(window);
            try
            {
                first = FPWorkbenchHistogramWindow.Open(data, 0, changes.Snapshot(), calculationProgram: program, derivedColumnId: program.ColumnId(0));
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (Result(first) == null && DateTime.UtcNow < deadline) yield return null;
                Assert.That(Result(first), Is.Not.Null); Assert.That(Result(first).Derived, Is.True); Assert.That(Result(first).Edges[Result(first).Edges.Count - 1], Is.EqualTo(18));
                definitions.Columns[0].Expression = "C1 * 100"; changes.Set(data.Cell(3, 0), "3", "100", FPWorkbenchEditKind.Number, "later edit");
                var figure = (FPWorkbenchFigureDocument)type.GetMethod("DescribeFigure", flags).Invoke(first, null); figure.Validate();
                Assert.That(figure.HasCalculationSnapshot, Is.True); Assert.That(figure.CalculationSnapshot.Columns[0].Expression, Is.EqualTo("C1 * 2"));
                var document = new FPWorkbenchDocument();
                document.Sources.Add(new FPWorkbenchSourceDocument { DatasetId = data.DatasetId, SourcePath = source, CacheDirectory = data.CacheDirectory, Import = data.ImportProfile });
                document.Figures.Add(figure); string path = Path.Combine(root, "histogram.fpworkbook");
                var saving = FPWorkbenchDocumentFile.SaveAsync(document, path); while (!saving.IsCompleted) yield return null; Assert.That(saving.IsFaulted, Is.False);
                var loading = FPWorkbenchDocumentFile.LoadAsync(path); while (!loading.IsCompleted) yield return null; Assert.That(loading.IsFaulted, Is.False);
                Assert.That(loading.Result.Document.Sources[0].HasCalculations, Is.False, "Figure formulas are independent of current source definitions.");
                first.Close(); first = null;
                restored = (FPWorkbenchHistogramWindow)type.GetMethod("OpenSaved", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null,
                    new object[] { document.Id, loading.Result.SourceDatasets[0], loading.Result.Document.Figures[0] });
                while (Result(restored) == null && DateTime.UtcNow < deadline) yield return null;
                Assert.That(Result(restored), Is.Not.Null); Assert.That(Result(restored).Edges[Result(restored).Edges.Count - 1], Is.EqualTo(18));
                Assert.That(Result(restored).FormulaFingerprint, Is.EqualTo(program.Fingerprint)); Assert.That(Result(restored).IncludedCount, Is.EqualTo(4));
                var bad = loading.Result.Document; bad.Figures[0].HistogramDerivedId = "removed-definition";
                var badSave = FPWorkbenchDocumentFile.SaveAsync(bad, Path.Combine(root, "bad.fpworkbook")); while (!badSave.IsCompleted) yield return null;
                var badLoad = FPWorkbenchDocumentFile.LoadAsync(Path.Combine(root, "bad.fpworkbook")); while (!badLoad.IsCompleted) yield return null;
                Assert.That(badLoad.IsFaulted, Is.True); Assert.That(badLoad.Exception.GetBaseException(), Is.TypeOf<InvalidDataException>());
            }
            finally
            {
                if (first != null) { first.Close(); UnityEngine.Object.DestroyImmediate(first); }
                if (restored != null) { restored.Close(); UnityEngine.Object.DestroyImmediate(restored); }
                Directory.Delete(root, true);
            }
        }
    }
}
