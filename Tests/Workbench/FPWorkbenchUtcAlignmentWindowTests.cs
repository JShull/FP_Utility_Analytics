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
    public sealed class FPWorkbenchUtcAlignmentWindowTests
    {
        [UnityTest] public IEnumerator UtcWindowRestoresAppliedCompositeKeysAndGeneratesScatter()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPUtcWindow-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            FPWorkbenchAlignmentWindow window = null, restored = null; FPWorkbenchScatterWindow plot = null;
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance; var type = typeof(FPWorkbenchAlignmentWindow);
            bool Busy(FPWorkbenchAlignmentWindow w) => (bool)type.GetProperty("Busy", flags).GetValue(w);
            FPWorkbenchAlignmentMetadata Meta(FPWorkbenchAlignmentWindow w) => (FPWorkbenchAlignmentMetadata)type.GetField("metadata", flags).GetValue(w);
            try {
                string a = Path.Combine(root, "a.csv"), b = Path.Combine(root, "b.csv");
                File.WriteAllText(a, "date,time,x\n31.12.2025,23:59:59.9999999,1\n01.01.2026,00:00:00.0000001,2\n");
                File.WriteAllText(b, "time,date,y\n23:59:59.9999999,2025-12-31,10\n00:00:00.0000001,2026-01-01,20\n");
                var aa = FPWorkbenchCsvDataset.ImportAsync(a, Path.Combine(root, "a-cache"), new FPWorkbenchCsvProfile());
                var bb = FPWorkbenchCsvDataset.ImportAsync(b, Path.Combine(root, "b-cache"), new FPWorkbenchCsvProfile());
                while (!aa.IsCompleted || !bb.IsCompleted) yield return null;
                var left = aa.GetAwaiter().GetResult(); var right = bb.GetAwaiter().GetResult();
                FPWorkbenchSourceDocument Ref(FPWorkbenchCsvDataset data) => new FPWorkbenchSourceDocument { DatasetId = data.DatasetId, SourcePath = data.SourcePath, CacheDirectory = data.CacheDirectory, Import = data.ImportProfile };
                window = FPWorkbenchAlignmentWindow.Open("utc-fixture", new[] { Ref(left), Ref(right) }, null); window.position = new Rect(30, 30, 1000, 850);
                var deadline = DateTime.UtcNow.AddSeconds(15); while (Busy(window) && DateTime.UtcNow < deadline) yield return null;
                var options = (FPWorkbenchAlignmentOptions)type.GetField("options", flags).GetValue(window);
                options.KeyKind = FPWorkbenchAlignmentKeyKind.UtcDateTime; options.KeyDomain = "Declared UTC fixture";
                options.LeftKeyColumn = 1; options.RightKeyColumn = 0; options.RightDateColumn = 1;
                options.LeftUtcFormat = "dd.MM.yyyy HH:mm:ss.fffffff"; options.RightUtcFormat = "yyyy-MM-dd HH:mm:ss.fffffff";
                type.GetMethod("BeginBuild", flags).Invoke(window, null);
                while ((Busy(window) || Meta(window) == null) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(Meta(window)?.MatchedPairs, Is.EqualTo(2));
                type.GetField("leftValueColumn", flags).SetValue(window, 2); type.GetField("rightValueColumn", flags).SetValue(window, 2);
                type.GetMethod("BeginPlot", flags).Invoke(window, null);
                while ((Busy(window) || type.GetField("generatedPlot", flags).GetValue(window) == null) && DateTime.UtcNow < deadline) yield return null;
                plot = (FPWorkbenchScatterWindow)type.GetField("generatedPlot", flags).GetValue(window); Assert.That(plot, Is.Not.Null);
                var pt = plot.GetType(); while ((pt.GetField("view", flags).GetValue(plot) == null || (bool)pt.GetProperty("Busy", flags).GetValue(plot)) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(((FPWorkbenchPointView)pt.GetField("view", flags).GetValue(plot)).MatchingCount, Is.EqualTo(2));
                string json = EditorJsonUtility.ToJson(window); window.Close(); UnityEngine.Object.DestroyImmediate(window); window = null;
                restored = ScriptableObject.CreateInstance<FPWorkbenchAlignmentWindow>(); type.GetMethod("OnDisable", flags).Invoke(restored, null);
                EditorJsonUtility.FromJsonOverwrite(json, restored); type.GetMethod("OnEnable", flags).Invoke(restored, null); restored.ShowUtility();
                while ((Busy(restored) || Meta(restored) == null) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(Meta(restored)?.MatchedPairs, Is.EqualTo(2)); Assert.That(Meta(restored).Options.KeyKind, Is.EqualTo(FPWorkbenchAlignmentKeyKind.UtcDateTime));
                Assert.That(Meta(restored).Options.LeftUtcFormat, Is.EqualTo("dd.MM.yyyy HH:mm:ss.fffffff"));
                Assert.That(Meta(restored).Options.RightDateColumn, Is.EqualTo(1)); Assert.That(Meta(restored).Options.RightKeyColumn, Is.EqualTo(0));
                Assert.That(Meta(restored).Options.RightUtcFormat, Is.EqualTo("yyyy-MM-dd HH:mm:ss.fffffff"));
            } finally {
                if (plot != null) { plot.Close(); UnityEngine.Object.DestroyImmediate(plot); }
                if (window != null) { window.Close(); UnityEngine.Object.DestroyImmediate(window); }
                if (restored != null) { restored.Close(); UnityEngine.Object.DestroyImmediate(restored); }
                Directory.Delete(root, true);
            }
        }
    }
}
