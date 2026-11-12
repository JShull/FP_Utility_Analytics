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

    public sealed class FPWorkbenchSourceBudgetWindowTests
    {
        [UnityTest] public IEnumerator ImportRefusesBeforeCreatingCacheCancelsPendingAndRecovers()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPImportAdmission-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = typeof(FPDataWorkbenchWindow);
            var resources = type.Assembly.GetType("FuzzPhyte.Utility.Analytics.WorkBench.Editor.FPWorkbenchEditorResources");
            var scans = (FPWorkbenchMemoryBudget)resources.GetField("Scans", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var memory = (FPWorkbenchMemoryBudget)resources.GetField("Memory", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            FPWorkbenchMemoryBudget.Reservation first = null, second = null, blocker = null;
            FPDataWorkbenchWindow window = null; string cache = null;
            try {
                string path = Path.Combine(root, "source.csv"); const string source = "x,y\n0,3\n1,4\n"; File.WriteAllText(path, source);
                Assert.That(scans.TryAcquire(1, out first), Is.True); Assert.That(scans.TryAcquire(1, out second), Is.True);
                window = ScriptableObject.CreateInstance<FPDataWorkbenchWindow>(); window.ShowUtility();
                type.GetMethod("AddCsv", flags).Invoke(window, new object[] { path });
                var sheets = (IList)type.GetField("sheets", flags).GetValue(window); var sheet = sheets[0]; var st = sheet.GetType();
                Assert.That(st.GetField("PendingLoading").GetValue(sheet), Is.Not.Null);
                Assert.That(st.GetField("Loading").GetValue(sheet), Is.Null);
                cache = (string)st.GetField("Cache").GetValue(sheet); Assert.That(Directory.Exists(cache), Is.False);
                type.GetMethod("CloseCurrentSheet", flags).Invoke(window, null);
                Assert.That(st.GetField("PendingLoading").GetValue(sheet), Is.Null); Assert.That(sheets.Count, Is.Zero);
                type.GetMethod("AddCsv", flags).Invoke(window, new object[] { path }); sheet = sheets[0]; cache = (string)st.GetField("Cache").GetValue(sheet);
                first.Dispose(); second.Dispose(); first = null; second = null;
                Assert.That(memory.TryAcquire(memory.LimitBytes - memory.UsedBytes, out blocker), Is.True);
                type.GetMethod("Tick", flags).Invoke(window, null);
                Assert.That(scans.LeaseCount, Is.Zero, "Memory refusal must roll back the scan ticket.");
                Assert.That(st.GetField("Loading").GetValue(sheet), Is.Null); Assert.That(Directory.Exists(cache), Is.False);
                blocker.Dispose(); blocker = null;
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (st.GetField("Dataset").GetValue(sheet) == null && DateTime.UtcNow < deadline) yield return null;
                var data = (FPWorkbenchCsvDataset)st.GetField("Dataset").GetValue(sheet); Assert.That(data, Is.Not.Null);
                Assert.That(data.RowCount, Is.EqualTo(2)); Assert.That(scans.LeaseCount, Is.Zero);
                Assert.That(((FPWorkbenchCsvProfile)st.GetField("Import").GetValue(sheet)).MaximumRecordBytes, Is.EqualTo(data.ImportProfile.MaximumRecordBytes));
                Assert.That(File.ReadAllText(path), Is.EqualTo(source));
                type.GetMethod("ApplyCorrection", flags).Invoke(window, new object[] { sheet, 0L, 1, "3", "30", FPWorkbenchEditKind.Number, "admission fixture", false });
                string changes = Path.Combine(root, "changes.fpchanges");
                Assert.That(memory.TryAcquire(memory.LimitBytes - memory.UsedBytes, out blocker), Is.True);
                type.GetMethod("BeginChangeFile", flags).Invoke(window, new object[] { changes, false });
                Assert.That(type.GetField("changeSave", flags).GetValue(window), Is.Null); Assert.That(File.Exists(changes), Is.False);
                Assert.That(scans.LeaseCount, Is.Zero);
                blocker.Dispose(); blocker = null;
                type.GetMethod("BeginChangeFile", flags).Invoke(window, new object[] { changes, false });
                deadline = DateTime.UtcNow.AddSeconds(10);
                while (type.GetField("changeSave", flags).GetValue(window) != null && DateTime.UtcNow < deadline) yield return null;
                Assert.That(File.Exists(changes), Is.True);
                Assert.That(memory.TryAcquire(memory.LimitBytes - memory.UsedBytes, out blocker), Is.True);
                type.GetMethod("BeginChangeFile", flags).Invoke(window, new object[] { changes, true });
                Assert.That(type.GetField("changeLoad", flags).GetValue(window), Is.Null); Assert.That(scans.LeaseCount, Is.Zero);
                blocker.Dispose(); blocker = null;
                type.GetMethod("BeginChangeFile", flags).Invoke(window, new object[] { changes, true });
                while (type.GetField("changeLoad", flags).GetValue(window) != null && DateTime.UtcNow < deadline) yield return null;
                Assert.That(type.GetField("error", flags).GetValue(window), Is.Null);
                Assert.That(File.ReadAllText(path), Is.EqualTo(source));
            } finally {
                first?.Dispose(); second?.Dispose(); blocker?.Dispose();
                if (window != null) { window.Close(); UnityEngine.Object.DestroyImmediate(window); }
                if (cache != null && Directory.Exists(cache)) Directory.Delete(cache, true);
                Directory.Delete(root, true);
            }
        }
    }
}
