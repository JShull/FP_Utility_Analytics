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

    public sealed class FPWorkbenchDetachedSourceBudgetTests
    {
        [UnityTest] public IEnumerator EveryDetachedFigureDefersSerializedSourceReopeningAndRecovers()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPDetachedAdmission-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var resources = typeof(FPDataWorkbenchWindow).Assembly.GetType("FuzzPhyte.Utility.Analytics.WorkBench.Editor.FPWorkbenchEditorResources");
            var scans = (FPWorkbenchMemoryBudget)resources.GetField("Scans", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            FPWorkbenchMemoryBudget.Reservation first = null, second = null; EditorWindow window = null;
            bool Ready(EditorWindow w, int kind) {
                var t = w.GetType();
                if (kind == 0) {
                    var lease = (FPWorkbenchFigureSession.Lease)t.GetField("lease", flags).GetValue(w);
                    return lease != null && lease.Session.View != null && !lease.Session.IsBusy;
                }
                return !(bool)t.GetProperty("Busy", flags).GetValue(w) &&
                    t.GetField(kind == 1 ? "result" : kind == 4 ? "batch" : "view", flags).GetValue(w) != null;
            }
            try {
                string path = Path.Combine(root, "source.csv"); File.WriteAllText(path, "x,y,z\n0,1,2\n1,2,3\n2,3,4\n");
                var importing = FPWorkbenchCsvDataset.ImportAsync(path, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile());
                while (!importing.IsCompleted) yield return null; var data = importing.GetAwaiter().GetResult();
                var binding = new FPWorkbenchSeriesBinding { XMode = FPWorkbenchXMode.Numeric, XColumn = 0, YColumn = 1 };
                for (int kind = 0; kind < 5; kind++) {
                    switch (kind) {
                        case 0: window = FPWorkbenchFigureWindow.Open(Guid.NewGuid().ToString("N"), new FPWorkbenchSheetDocument { Name = "Fixture", SourcePath = path, CacheDirectory = data.CacheDirectory, DatasetId = data.DatasetId, Import = data.ImportProfile, Binding = binding }, data); break;
                        case 1: window = FPWorkbenchHistogramWindow.Open(data, 1); break;
                        case 2: window = FPWorkbenchOverlayWindow.Open(null, data, binding, 1); break;
                        case 3: window = FPWorkbenchScatterWindow.Open(null, data, 0, 1); break;
                        default: window = FPWorkbenchPoint3DWindow.Open(null, data, 0, 1, 2); break;
                    }
                    var deadline = DateTime.UtcNow.AddSeconds(10);
                    while (!Ready(window, kind) && DateTime.UtcNow < deadline) yield return null;
                    Assert.That(Ready(window, kind), Is.True, "Initial figure " + kind);
                    var type = window.GetType(); type.GetMethod("Capture", flags)?.Invoke(window, null);
                    string json = EditorJsonUtility.ToJson(window); window.Close(); UnityEngine.Object.DestroyImmediate(window); window = null;
                    Assert.That(scans.TryAcquire(1, out first), Is.True); Assert.That(scans.TryAcquire(1, out second), Is.True);
                    window = (EditorWindow)ScriptableObject.CreateInstance(type); type.GetMethod("OnDisable", flags).Invoke(window, null);
                    EditorJsonUtility.FromJsonOverwrite(json, window); type.GetMethod("OnEnable", flags).Invoke(window, null); window.ShowUtility();
                    type.GetMethod("Tick", flags).Invoke(window, null);
                    Assert.That(type.GetField("pendingOpening", flags).GetValue(window), Is.True, "Deferred figure " + kind);
                    Assert.That(type.GetField("opening", flags).GetValue(window), Is.Null);
                    Assert.That(type.GetField("openingMemory", flags).GetValue(window), Is.Null);
                    first.Dispose(); second.Dispose(); first = null; second = null;
                    deadline = DateTime.UtcNow.AddSeconds(10);
                    while (!Ready(window, kind) && DateTime.UtcNow < deadline) yield return null;
                    Assert.That(Ready(window, kind), Is.True, "Restored figure " + kind);
                    Assert.That(type.GetField("error", flags).GetValue(window), Is.Null);
                    window.Close(); type.GetMethod("Tick", flags).Invoke(window, null);
                    Assert.That(type.GetField("opening", flags).GetValue(window), Is.Null);
                    Assert.That(type.GetField("openingMemory", flags).GetValue(window), Is.Null);
                    UnityEngine.Object.DestroyImmediate(window); window = null;
                    Assert.That(scans.LeaseCount, Is.Zero);
                }
            } finally {
                first?.Dispose(); second?.Dispose(); if (window != null) { window.Close(); UnityEngine.Object.DestroyImmediate(window); }
                Directory.Delete(root, true);
            }
        }
    }
}
