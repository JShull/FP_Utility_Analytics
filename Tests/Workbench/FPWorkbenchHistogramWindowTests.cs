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
                type.GetField("bins", flags).SetValue(window, 2); type.GetMethod("BeginBuild", flags).Invoke(window, null);
                while ((Result(window) == result || (bool)type.GetProperty("Busy", flags).GetValue(window)) && DateTime.UtcNow < deadline) yield return null;
                result = Result(window); Assert.That(result.Counts.Count, Is.EqualTo(2)); Assert.That(result.Edges[2], Is.EqualTo(9), "Viewer owns the selected revision snapshot.");
                string json = EditorJsonUtility.ToJson(window);
                type.GetField("bins", flags).SetValue(window, 0); type.GetMethod("BeginBuild", flags).Invoke(window, null);
                Assert.That(type.GetField("error", flags).GetValue(window), Is.Not.Null); Assert.That(Result(window), Is.SameAs(result));
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
