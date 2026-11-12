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
            bool Busy() => (bool)type.GetProperty("Busy", flags).GetValue(window);
            try {
                string input = Path.Combine(root, "original.fpworkbook"), output = Path.Combine(root, "copy.fpworkbook");
                var document = new FPWorkbenchDocument { Name = "Recovery fixture" }; var save = FPWorkbenchDocumentFile.SaveAsync(document, input);
                while (!save.IsCompleted) yield return null; save.GetAwaiter().GetResult();
                byte[] original = File.ReadAllBytes(input);
                window = ScriptableObject.CreateInstance<FPWorkbenchRecoveryWindow>(); window.ShowUtility(); window.position = new Rect(30, 30, 700, 400);
                type.GetMethod("BeginRead", flags).Invoke(window, new object[] { input });
                var deadline = DateTime.UtcNow.AddSeconds(10); while (Busy() && DateTime.UtcNow < deadline) yield return null;
                Assert.That(Busy(), Is.False); Assert.That(type.GetField("candidate", flags).GetValue(window), Is.Not.Null);
                type.GetMethod("BeginRead", flags).Invoke(window, new object[] { Path.Combine(root, "missing.fpworkbook") });
                while (Busy() && DateTime.UtcNow < deadline) yield return null;
                Assert.That(type.GetField("originalPath", flags).GetValue(window), Is.EqualTo(input), "Failed metadata reads cannot retarget the prior candidate's protected input.");
                Assert.That(type.GetField("candidate", flags).GetValue(window), Is.Not.Null);
                type.GetMethod("BeginSave", flags).Invoke(window, new object[] { input });
                Assert.That(Busy(), Is.False); Assert.That((string)type.GetField("error", flags).GetValue(window), Does.Contain("new workbook copy"));
                type.GetMethod("BeginSave", flags).Invoke(window, new object[] { output });
                while (Busy() && DateTime.UtcNow < deadline) yield return null;
                Assert.That(File.Exists(output), Is.True); Assert.That(File.ReadAllBytes(input), Is.EqualTo(original));
            } finally { if (window != null) { window.Close(); UnityEngine.Object.DestroyImmediate(window); } Directory.Delete(root, true); }
        }
    }
}
