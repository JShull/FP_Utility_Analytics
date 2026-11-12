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
    public sealed class FPWorkbenchRecoveryWindowTests
    {
        [UnityTest] public IEnumerator RecoveryMetadataRetainsPriorCandidateOnFailureAndProtectsOriginalWorkbook()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPRecoverWindow-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            FPWorkbenchRecoveryWindow window = null; const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var type = typeof(FPWorkbenchRecoveryWindow);
            var resources = type.Assembly.GetType("FuzzPhyte.Utility.Analytics.WorkBench.Editor.FPWorkbenchEditorResources");
            var memory = (FPWorkbenchMemoryBudget)resources.GetField("Memory", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var scans = (FPWorkbenchMemoryBudget)resources.GetField("Scans", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            FPWorkbenchMemoryBudget.Reservation blocker = null, first = null, second = null;
            bool Busy() => (bool)type.GetProperty("Busy", flags).GetValue(window);
            try {
                string input = Path.Combine(root, "original.fpworkbook"), output = Path.Combine(root, "copy.fpworkbook");
                var document = new FPWorkbenchDocument { Name = "Recovery fixture" }; var save = FPWorkbenchDocumentFile.SaveAsync(document, input);
                while (!save.IsCompleted) yield return null; save.GetAwaiter().GetResult();
                byte[] original = File.ReadAllBytes(input);
                window = ScriptableObject.CreateInstance<FPWorkbenchRecoveryWindow>(); window.ShowUtility(); window.position = new Rect(30, 30, 700, 400);
                Assert.That(memory.TryAcquire(memory.LimitBytes - memory.UsedBytes, out blocker), Is.True);
                type.GetMethod("BeginRead", flags).Invoke(window, new object[] { input });
                Assert.That(type.GetField("pendingReading", flags).GetValue(window), Is.Not.Null);
                Assert.That(type.GetField("reading", flags).GetValue(window), Is.Null);
                Assert.That(scans.LeaseCount, Is.Zero);
                blocker.Dispose(); blocker = null;
                var deadline = DateTime.UtcNow.AddSeconds(10); while (Busy() && DateTime.UtcNow < deadline) yield return null;
                Assert.That(Busy(), Is.False); Assert.That(type.GetField("candidate", flags).GetValue(window), Is.Not.Null);
                type.GetMethod("BeginRead", flags).Invoke(window, new object[] { Path.Combine(root, "missing.fpworkbook") });
                while (Busy() && DateTime.UtcNow < deadline) yield return null;
                Assert.That(type.GetField("originalPath", flags).GetValue(window), Is.EqualTo(input), "Failed metadata reads cannot retarget the prior candidate's protected input.");
                Assert.That(type.GetField("candidate", flags).GetValue(window), Is.Not.Null);
                type.GetMethod("BeginSave", flags).Invoke(window, new object[] { input });
                Assert.That(Busy(), Is.False); Assert.That((string)type.GetField("error", flags).GetValue(window), Does.Contain("new workbook copy"));
                Assert.That(scans.TryAcquire(1, out first), Is.True); Assert.That(scans.TryAcquire(1, out second), Is.True);
                type.GetMethod("BeginSave", flags).Invoke(window, new object[] { output });
                Assert.That(type.GetField("pendingSaving", flags).GetValue(window), Is.Not.Null);
                Assert.That(type.GetField("saving", flags).GetValue(window), Is.Null); Assert.That(File.Exists(output), Is.False);
                first.Dispose(); second.Dispose(); first = null; second = null;
                while (Busy() && DateTime.UtcNow < deadline) yield return null;
                Assert.That(File.Exists(output), Is.True); Assert.That(File.ReadAllBytes(input), Is.EqualTo(original));
                window.Close(); type.GetMethod("Tick", flags).Invoke(window, null);
                Assert.That(type.GetField("windowActive", flags).GetValue(window), Is.False);
                Assert.That(type.GetField("operationMemory", flags).GetValue(window), Is.Null);
            } finally { blocker?.Dispose(); first?.Dispose(); second?.Dispose(); if (window != null) { window.Close(); UnityEngine.Object.DestroyImmediate(window); } Directory.Delete(root, true); }
        }
    }
}
