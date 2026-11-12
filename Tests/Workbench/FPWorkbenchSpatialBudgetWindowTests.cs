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

    public sealed class FPWorkbenchSpatialBudgetWindowTests
    {
        [UnityTest] public IEnumerator SpatialViewsDeferPayloadsAndReleaseAfterReplacementAndClose()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPSpatialBudget-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            EditorWindow window = null; FPWorkbenchMemoryBudget.Reservation blocker = null;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var budget = (FPWorkbenchMemoryBudget)typeof(FPDataWorkbenchWindow).GetField("pageMemory", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var resources = typeof(FPDataWorkbenchWindow).Assembly.GetType("FuzzPhyte.Utility.Analytics.WorkBench.Editor.FPWorkbenchEditorResources");
            var scans = (FPWorkbenchMemoryBudget)resources.GetField("Scans", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            FPWorkbenchMemoryBudget.Reservation scanA = null, scanB = null;
            try {
                string path = Path.Combine(root, "source.csv"); File.WriteAllText(path, "x,y,z\n0,1,2\n1,2,3\n2,3,4\n");
                var importing = FPWorkbenchCsvDataset.ImportAsync(path, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile()); while (!importing.IsCompleted) yield return null;
                var data = importing.GetAwaiter().GetResult();
                for (int kind = 0; kind < 2; kind++) {
                    long baseline = budget.UsedBytes; int leases = budget.LeaseCount;
                    Assert.That(budget.TryAcquire(budget.LimitBytes - budget.UsedBytes, out blocker), Is.True);
                    Assert.That(scans.TryAcquire(1, out scanA), Is.True); Assert.That(scans.TryAcquire(1, out scanB), Is.True);
                    bool xyz = kind == 1;
                    window = xyz ? (EditorWindow)FPWorkbenchPoint3DWindow.Open(null, data, 0, 1, 2) : FPWorkbenchScatterWindow.Open(null, data, 0, 1);
                    var type = window.GetType(); var deadline = DateTime.UtcNow.AddSeconds(10);
                    while (!(bool)type.GetField("waitingForMemory", flags).GetValue(window) && DateTime.UtcNow < deadline) yield return null;
                    Assert.That(type.GetField("waitingForMemory", flags).GetValue(window), Is.EqualTo(true));
                    Assert.That(type.GetField("pendingBuild", flags).GetValue(window), Is.Not.Null);
                    Assert.That(type.GetField("building", flags).GetValue(window), Is.Null, "No construction task queues while background admission is full.");
                    scanA.Dispose(); scanB.Dispose(); scanA = null; scanB = null;
                    type.GetMethod("Tick", flags).Invoke(window, null);
                    Assert.That(scans.LeaseCount, Is.Zero, "Payload refusal rolls back background admission.");
                    Assert.That(type.GetField(xyz ? "preparing" : "querying", flags).GetValue(window), Is.Null);
                    Assert.That(type.GetField(xyz ? "batch" : "view", flags).GetValue(window), Is.Null);
                    if (xyz) Assert.That(type.GetField("renderer", flags).GetValue(window), Is.Null);
                    blocker.Dispose(); blocker = null;
                    while ((type.GetField(xyz ? "batch" : "view", flags).GetValue(window) == null || (bool)type.GetProperty("Busy", flags).GetValue(window)) && DateTime.UtcNow < deadline) yield return null;
                    Assert.That(type.GetField(xyz ? "batchMemory" : "viewMemory", flags).GetValue(window), Is.Not.Null);
                    Assert.That(budget.UsedBytes, Is.GreaterThan(baseline));
                    if (xyz) {
                        var previous = type.GetField("batch", flags).GetValue(window); var renderer = type.GetField("renderer", flags).GetValue(window);
                        Assert.That(budget.TryAcquire(budget.LimitBytes - budget.UsedBytes, out blocker), Is.True);
                        type.GetMethod("BeginBuild", flags).Invoke(window, null);
                        while (!(bool)type.GetField("waitingForMemory", flags).GetValue(window) && DateTime.UtcNow < deadline) yield return null;
                        Assert.That(type.GetField("waitingForMemory", flags).GetValue(window), Is.EqualTo(true));
                        type.GetMethod("CancelPointUpdate", flags).Invoke(window, null);
                        Assert.That(type.GetProperty("Busy", flags).GetValue(window), Is.EqualTo(false));
                        Assert.That(type.GetField("batch", flags).GetValue(window), Is.SameAs(previous));
                        Assert.That(type.GetField("renderer", flags).GetValue(window), Is.SameAs(renderer));
                        Assert.That(type.GetField("pendingBatch", flags).GetValue(window), Is.Null);
                        Assert.That(type.GetField("pendingBuild", flags).GetValue(window), Is.Null);
                        blocker.Dispose(); blocker = null;
                        type.GetMethod("BeginBuild", flags).Invoke(window, null);
                    }
                    else {
                        var settings = (FPWorkbenchPlotSettings)type.GetField("settings", flags).GetValue(window); settings.StartX = .25; settings.EndX = 1.75;
                        type.GetMethod("Tick", flags).Invoke(window, null);
                    }
                    var closingWork = (System.Threading.Tasks.Task)type.GetField(xyz ? "building" : "querying", flags).GetValue(window);
                    window.Close(); type.GetMethod("Tick", flags).Invoke(window, null);
                    Assert.That(type.GetField("windowActive", flags).GetValue(window), Is.EqualTo(false));
                    Assert.That(type.GetField(xyz ? "preparing" : "querying", flags).GetValue(window), Is.Null);
                    if (xyz) Assert.That(type.GetField("renderer", flags).GetValue(window), Is.Null);
                    UnityEngine.Object.DestroyImmediate(window); window = null;
                    while (closingWork != null && !closingWork.IsCompleted && DateTime.UtcNow < deadline) yield return null;
                    if (closingWork != null) { Assert.That(closingWork.IsCompleted, Is.True); _ = closingWork.Exception; }
                    while ((budget.UsedBytes != baseline || budget.LeaseCount != leases) && DateTime.UtcNow < deadline) yield return null;
                    Assert.That(budget.UsedBytes, Is.EqualTo(baseline)); Assert.That(budget.LeaseCount, Is.EqualTo(leases));
                }
            }
            finally { scanA?.Dispose(); scanB?.Dispose(); blocker?.Dispose(); if (window != null) { window.Close(); UnityEngine.Object.DestroyImmediate(window); } Directory.Delete(root, true); }
        }
    }
}
