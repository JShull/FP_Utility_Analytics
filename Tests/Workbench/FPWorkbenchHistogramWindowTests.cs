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

    public sealed class FPWorkbenchHistogramWindowTests
    {
        [UnityTest] public IEnumerator HistogramScanAdmissionDoesNotOccupyInteractiveSlotsAndCancelsPendingWork()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPHistogramBudget-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            FPWorkbenchHistogramWindow window = null;
            FPWorkbenchMemoryBudget.Reservation a = null, b = null, blocker = null;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic; var type = typeof(FPWorkbenchHistogramWindow);
            var resources = type.Assembly.GetType("FuzzPhyte.Utility.Analytics.WorkBench.Editor.FPWorkbenchEditorResources");
            var scans = (FPWorkbenchMemoryBudget)resources.GetField("Scans", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var memory = (FPWorkbenchMemoryBudget)resources.GetField("Memory", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            long baseline = memory.UsedBytes; int baselineLeases = memory.LeaseCount;
            try {
                string source = Path.Combine(root, "source.csv"); File.WriteAllText(source, "y\n1\n2\n3\n");
                var importing = FPWorkbenchCsvDataset.ImportAsync(source, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile()); while (!importing.IsCompleted) yield return null;
                var data = importing.GetAwaiter().GetResult();
                Assert.That(scans.TryAcquire(1, out a), Is.True); Assert.That(scans.TryAcquire(1, out b), Is.True);
                window = FPWorkbenchHistogramWindow.Open(data, 0); var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!(bool)type.GetField("waitingForMemory", flags).GetValue(window) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(type.GetField("waitingForMemory", flags).GetValue(window), Is.EqualTo(true)); Assert.That(type.GetField("fitting", flags).GetValue(window), Is.Null);
                Assert.That(memory.TryAcquire(1024 * 1024, out var interactive), Is.True, "Occupied scan slots must leave interactive read admission available."); interactive.Dispose();
                a.Dispose(); a = null; b.Dispose(); b = null;
                while (type.GetField("result", flags).GetValue(window) == null && DateTime.UtcNow < deadline) yield return null;
                var result = type.GetField("result", flags).GetValue(window); Assert.That(result, Is.Not.Null);
                Assert.That(scans.ActiveWorkers, Is.Zero); Assert.That(type.GetField("resultMemory", flags).GetValue(window), Is.Not.Null);
                Assert.That(memory.TryAcquire(memory.LimitBytes - memory.UsedBytes, out blocker), Is.True);
                type.GetMethod("BeginFit", flags).Invoke(window, null);
                Assert.That(type.GetField("waitingForMemory", flags).GetValue(window), Is.EqualTo(true)); Assert.That(scans.ActiveWorkers, Is.Zero, "A refused byte reservation must return its scan slot.");
                type.GetMethod("CancelHistogram", flags).Invoke(window, null);
                Assert.That(type.GetProperty("Busy", flags).GetValue(window), Is.EqualTo(false)); Assert.That(type.GetField("result", flags).GetValue(window), Is.SameAs(result));
                blocker.Dispose(); blocker = null;
                type.GetMethod("BeginBuild", flags).Invoke(window, null);
                window.Close(); type.GetMethod("Tick", flags).Invoke(window, null);
                Assert.That(type.GetField("windowActive", flags).GetValue(window), Is.EqualTo(false));
                UnityEngine.Object.DestroyImmediate(window); window = null;
                while ((memory.UsedBytes != baseline || memory.LeaseCount != baselineLeases || scans.ActiveWorkers != 0) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(memory.UsedBytes, Is.EqualTo(baseline)); Assert.That(memory.LeaseCount, Is.EqualTo(baselineLeases)); Assert.That(scans.ActiveWorkers, Is.Zero);
            }
            finally { a?.Dispose(); b?.Dispose(); blocker?.Dispose(); if (window != null) { window.Close(); UnityEngine.Object.DestroyImmediate(window); } Directory.Delete(root, true); }
        }
        [UnityTest] public IEnumerator PhysicalHistogramRetainsTypedSnapshotAcrossReopen()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPTypedHistogram-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            FPWorkbenchHistogramWindow window = null, restored = null;
            try {
                string source = Path.Combine(root, "source.csv"); File.WriteAllText(source, "value\n\"1,25\"\n\"2,5\"\n---\n");
                var importing = FPWorkbenchCsvDataset.ImportAsync(source, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile());
                while (!importing.IsCompleted) yield return null;
                var data = importing.GetAwaiter().GetResult();
                var schema = new FPWorkbenchTypedSchema { DatasetId = data.DatasetId, Columns = new[] {
                    new FPWorkbenchColumnDeclaration { Column = 0, ColumnId = data.Cell(0, 0).ColumnId, Type = FPWorkbenchColumnType.Real, Culture = "de-DE", MissingTokens = new[] { "---" } } } };
                var program = FPWorkbenchFormulaProgram.Compile(data, new FPWorkbenchFormulaSet { DatasetId = data.DatasetId }, schema.Compile(data));
                window = FPWorkbenchHistogramWindow.Open(data, 0, calculationProgram: program);
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var type = window.GetType(); var deadline = DateTime.UtcNow.AddSeconds(10);
                FPWorkbenchHistogramResult Result(FPWorkbenchHistogramWindow w) => (FPWorkbenchHistogramResult)type.GetField("result", flags).GetValue(w);
                while (Result(window) == null && DateTime.UtcNow < deadline) yield return null;
                var result = Result(window); Assert.That(result, Is.Not.Null);
                Assert.That(result.IncludedCount, Is.EqualTo(2)); Assert.That(result.InvalidCount, Is.EqualTo(1));
                Assert.That(result.Edges[0], Is.EqualTo(1.25)); Assert.That(result.Edges[result.Edges.Count - 1], Is.EqualTo(2.5));
                var saved = (FPWorkbenchFigureDocument)type.GetMethod("DescribeFigure", flags).Invoke(window, null);
                Assert.That(saved.HasCalculationSnapshot, Is.True);
                Assert.That(saved.CalculationSnapshot.TypedSchema.Columns[0].Culture, Is.EqualTo("de-DE"));
                schema.Columns[0].Culture = "en-US";
                restored = (FPWorkbenchHistogramWindow)type.GetMethod("OpenSaved", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { null, data, saved });
                deadline = DateTime.UtcNow.AddSeconds(10);
                while (Result(restored) == null && DateTime.UtcNow < deadline) yield return null;
                Assert.That(Result(restored), Is.Not.Null); Assert.That(Result(restored).IncludedCount, Is.EqualTo(2));
                Assert.That(Result(restored).FormulaFingerprint, Is.EqualTo(program.Fingerprint));
            }
            finally {
                if (restored != null) { restored.Close(); UnityEngine.Object.DestroyImmediate(restored); }
                if (window != null) { window.Close(); UnityEngine.Object.DestroyImmediate(window); }
                Directory.Delete(root, true);
            }
        }
        [UnityTest] public IEnumerator HistogramBuildsSnapshotRetainsPreviousOnErrorAndRestoresMetadata()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPHistogramWindow-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            string source = Path.Combine(root, "source.csv"); File.WriteAllText(source, "value\n0\n1\n2\n3\n");
            var import = FPWorkbenchCsvDataset.ImportAsync(source, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile());
            while (!import.IsCompleted) yield return null; Assert.That(import.IsFaulted, Is.False);
            var dataset = import.Result; var changes = new FPWorkbenchChangeSet { DatasetId = dataset.DatasetId };
            changes.Set(dataset.Cell(3, 0), "3", "9", FPWorkbenchEditKind.Number, "fixture");
            FPWorkbenchHistogramWindow window = null, restored = null;
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var type = typeof(FPWorkbenchHistogramWindow);
            FPWorkbenchHistogramResult Result(FPWorkbenchHistogramWindow view) => (FPWorkbenchHistogramResult)type.GetField("result", flags).GetValue(view);
            try
            {
                window = FPWorkbenchHistogramWindow.Open(dataset, 0, changes.Snapshot()); window.position = new Rect(80, 80, 700, 480);
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (Result(window) == null && DateTime.UtcNow < deadline) yield return null;
                var result = Result(window); Assert.That(result, Is.Not.Null);
                Assert.That(result.Edges[result.Edges.Count - 1], Is.EqualTo(9)); Assert.That(result.IncludedCount, Is.EqualTo(4));
                Assert.That(result.CorrectionFingerprint, Is.EqualTo(changes.Snapshot().Fingerprint));
                changes.Set(dataset.Cell(3, 0), "3", "100", FPWorkbenchEditKind.Number, "later change");
                string before = (string)type.GetMethod("HistogramSettingsJson", flags).Invoke(window, null);
                Undo.IncrementCurrentGroup();
                type.GetField("bins", flags).SetValue(window, 2); type.GetMethod("RecordHistogramSettings", flags).Invoke(window, new object[] { before }); type.GetMethod("BeginBuild", flags).Invoke(window, null);
                while ((Result(window) == result || (bool)type.GetProperty("Busy", flags).GetValue(window)) && DateTime.UtcNow < deadline) yield return null;
                result = Result(window); Assert.That(result.Counts.Count, Is.EqualTo(2)); Assert.That(result.Edges[2], Is.EqualTo(9), "Viewer owns the selected revision snapshot.");
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                while (((bool)type.GetProperty("Busy", flags).GetValue(window)) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(Result(window).Counts.Count, Is.EqualTo(32));
                Undo.PerformRedo(); while (((bool)type.GetProperty("Busy", flags).GetValue(window)) && DateTime.UtcNow < deadline) yield return null;
                result = Result(window); Assert.That(result.Counts.Count, Is.EqualTo(2));
                before = (string)type.GetMethod("HistogramSettingsJson", flags).Invoke(window, null); Undo.IncrementCurrentGroup();
                type.GetMethod("ApplyPresentationPreset", flags).Invoke(window, new object[] { new FPWorkbenchHistogramPreset { Density = true, Grid = false }.ToJson() });
                Assert.That((bool)type.GetField("grid", flags).GetValue(window), Is.False);
                Assert.That(type.GetField("bins", flags).GetValue(window), Is.EqualTo(2));
                Assert.That(Result(window), Is.SameAs(result)); Assert.That((bool)type.GetProperty("Busy", flags).GetValue(window), Is.False);
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); Assert.That((bool)type.GetField("density", flags).GetValue(window), Is.False);
                Assert.That(Result(window), Is.SameAs(result)); Assert.That((bool)type.GetProperty("Busy", flags).GetValue(window), Is.False);
                Undo.PerformRedo(); Assert.That((bool)type.GetField("density", flags).GetValue(window), Is.True);
                Assert.That((bool)type.GetField("grid", flags).GetValue(window), Is.False);

                before = (string)type.GetMethod("HistogramSettingsJson", flags).Invoke(window, null); Undo.IncrementCurrentGroup();
                type.GetField("maximum", flags).SetValue(window, 5d); type.GetMethod("RecordHistogramSettings", flags).Invoke(window, new object[] { before });
                type.GetMethod("BeginBuild", flags).Invoke(window, null);
                while ((bool)type.GetProperty("Busy", flags).GetValue(window) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(Result(window).IncludedCount, Is.EqualTo(3));
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                while ((bool)type.GetProperty("Busy", flags).GetValue(window) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(Result(window).IncludedCount, Is.EqualTo(4));
                Undo.PerformRedo(); while ((bool)type.GetProperty("Busy", flags).GetValue(window) && DateTime.UtcNow < deadline) yield return null;
                type.GetMethod("BeginFit", flags).Invoke(window, null);
                while ((bool)type.GetProperty("Busy", flags).GetValue(window) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(Result(window).Edges[2], Is.EqualTo(9));
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                while ((bool)type.GetProperty("Busy", flags).GetValue(window) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(Result(window).Edges[2], Is.EqualTo(5));
                Undo.PerformRedo(); while ((bool)type.GetProperty("Busy", flags).GetValue(window) && DateTime.UtcNow < deadline) yield return null;
                result = Result(window); Assert.That(result.Edges[2], Is.EqualTo(9));
                type.GetField("bins", flags).SetValue(window, 0); type.GetMethod("BeginBuild", flags).Invoke(window, null);
                Assert.That(type.GetField("error", flags).GetValue(window), Is.Not.Null); Assert.That(Result(window), Is.SameAs(result));
                var descriptor = (FPWorkbenchFigureDocument)type.GetMethod("DescribeFigure", flags).Invoke(window, null);
                Assert.That(descriptor.HistogramBins, Is.EqualTo(2)); Assert.That(descriptor.Maximum, Is.EqualTo(9)); descriptor.Validate();
                string output = Path.Combine(root, "histogram.fpfigure");
                var pngWork0 = (System.Threading.Tasks.Task<FPWorkbenchImageExportResult>)type.GetMethod("ExportPng", flags).Invoke(window, new object[] { output, 640, 480 });
                while (!pngWork0.IsCompleted) yield return null; pngWork0.GetAwaiter().GetResult();
                Assert.That(Result(window), Is.SameAs(result)); Assert.That((bool)type.GetProperty("Busy", flags).GetValue(window), Is.False);
                var image = new Texture2D(2, 2);
                try {
                    Assert.That(image.LoadImage(File.ReadAllBytes(Path.Combine(output, "figure.png"))), Is.True);
                    Assert.That(image.width, Is.EqualTo(640)); Assert.That(image.height, Is.EqualTo(480));
                    int bars = 0, labels = 0;
                    foreach (var pixel in image.GetPixels32()) {
                        if (pixel.b > pixel.r + 30 && pixel.g > pixel.r + 30) bars++;
                        if (pixel.r > 180 && pixel.g > 180 && pixel.b > 180) labels++;
                    }
                    Assert.That(bars, Is.GreaterThan(100)); Assert.That(labels, Is.GreaterThan(20), "Export must include rendered axis labels.");
                } finally { UnityEngine.Object.DestroyImmediate(image); }
                var metadata = JsonUtility.FromJson(File.ReadAllText(Path.Combine(output, "provenance.json")), type.GetNestedType("HistogramImageProvenance", BindingFlags.NonPublic));
                Assert.That(metadata.GetType().GetField("Counts").GetValue(metadata), Is.EqualTo(new long[] { result.Counts[0], result.Counts[1] }));
                var exportedFigure = (FPWorkbenchFigureDocument)metadata.GetType().GetField("Figure").GetValue(metadata);
                Assert.That(exportedFigure.HistogramBins, Is.EqualTo(2), "Invalid query draft must not become export provenance.");
                Assert.That(exportedFigure.Maximum, Is.EqualTo(9));
                Assert.That(Assert.Throws<TargetInvocationException>(() => type.GetMethod("ExportPng", flags).Invoke(window, new object[] { output, 640, 480 })).InnerException, Is.TypeOf<IOException>());
                string json = EditorJsonUtility.ToJson(window); // Includes an invalid draft; last successful query must own reload.
                window.Close(); window = null;
                restored = ScriptableObject.CreateInstance<FPWorkbenchHistogramWindow>();
                type.GetMethod("OnDisable", flags).Invoke(restored, null);
                EditorJsonUtility.FromJsonOverwrite(json, restored); type.GetMethod("OnEnable", flags).Invoke(restored, null); restored.Show();
                while (Result(restored) == null && DateTime.UtcNow < deadline) yield return null;
                Assert.That(Result(restored), Is.Not.Null); Assert.That(Result(restored).Counts.Count, Is.EqualTo(2)); Assert.That(Result(restored).Edges[2], Is.EqualTo(9));
            }
            finally
            {
                if (window != null) { window.Close(); UnityEngine.Object.DestroyImmediate(window); }
                if (restored != null) { restored.Close(); UnityEngine.Object.DestroyImmediate(restored); }
                Directory.Delete(root, true);
            }
        }
    }
}
