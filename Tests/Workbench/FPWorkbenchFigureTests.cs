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

    public sealed class FPWorkbenchFigureTests
    {
        [UnityTest] public IEnumerator SharedFiguresRetainOneSessionThroughCloseAndSerializedRestore()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPFigureTest-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            string source = Path.Combine(root, "source.csv"); File.WriteAllText(source, "x,y\n0,1\n1,2\n2,3\n");
            var import = FPWorkbenchCsvDataset.ImportAsync(source, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile());
            while (!import.IsCompleted) yield return null;
            Assert.That(import.IsFaulted, Is.False); var dataset = import.Result;
            FPWorkbenchFigureWindow first = null, second = null, restored = null;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            try
            {
                var document = new FPWorkbenchSheetDocument { SourcePath = source, CacheDirectory = dataset.CacheDirectory, DatasetId = dataset.DatasetId, Binding = new FPWorkbenchSeriesBinding { XMode = FPWorkbenchXMode.Numeric, XColumn = 0, YColumn = 1 } };
                string key = Guid.NewGuid().ToString("N");
                first = FPWorkbenchFigureWindow.Open(key, document, dataset);
                second = FPWorkbenchFigureWindow.Open(key, document, dataset);
                var type = typeof(FPWorkbenchFigureWindow);
                FPWorkbenchFigureSession Session(FPWorkbenchFigureWindow window) => ((FPWorkbenchFigureSession.Lease)type.GetField("lease", flags).GetValue(window)).Session;
                var session = Session(first);
                Assert.That(Session(second), Is.SameAs(session)); Assert.That(session.LeaseCount, Is.EqualTo(2));
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (session.View == null && DateTime.UtcNow < deadline) yield return null;
                Assert.That(session.View, Is.Not.Null); Assert.That(session.View.Maximum, Is.EqualTo(3));
                first.Close(); first = null;
                Assert.That(session.IsDisposed, Is.False); Assert.That(session.LeaseCount, Is.EqualTo(1));
                session.Settings.Title = "Unsaved shared title";
                type.GetMethod("Capture", flags).Invoke(second, null);
                string state = UnityEditor.EditorJsonUtility.ToJson(second);
                restored = ScriptableObject.CreateInstance<FPWorkbenchFigureWindow>();
                type.GetMethod("OnDisable", flags).Invoke(restored, null);
                UnityEditor.EditorJsonUtility.FromJsonOverwrite(state, restored);
                type.GetMethod("OnEnable", flags).Invoke(restored, null);
                Assert.That(Session(restored), Is.SameAs(session));
                Assert.That(Session(restored).Settings.Title, Is.EqualTo("Unsaved shared title"));
                second.Close(); second = null;
                Assert.That(session.IsDisposed, Is.False);
                UnityEngine.Object.DestroyImmediate(restored); restored = null;
                Assert.That(session.IsDisposed, Is.True);
                Assert.Throws<ObjectDisposedException>(() => session.Poll());
                restored = ScriptableObject.CreateInstance<FPWorkbenchFigureWindow>();
                type.GetMethod("OnDisable", flags).Invoke(restored, null);
                UnityEditor.EditorJsonUtility.FromJsonOverwrite(state, restored);
                type.GetMethod("OnEnable", flags).Invoke(restored, null);
                deadline = DateTime.UtcNow.AddSeconds(10);
                while (type.GetField("lease", flags).GetValue(restored) == null && DateTime.UtcNow < deadline) yield return null;
                var reopened = Session(restored);
                while (reopened.View == null && DateTime.UtcNow < deadline) yield return null;
                Assert.That(reopened, Is.Not.SameAs(session)); Assert.That(reopened.View.Maximum, Is.EqualTo(3));
                Assert.That(reopened.Settings.Title, Is.EqualTo("Unsaved shared title"));
                long revision = reopened.Revision; reopened.Settings.InvertY = !reopened.Settings.InvertY;
                reopened.Poll(); Assert.That(reopened.Revision, Is.GreaterThan(revision));
            }
            finally
            {
                if (first != null) first.Close(); if (second != null) second.Close();
                if (restored != null) UnityEngine.Object.DestroyImmediate(restored);
                Directory.Delete(root, true);
            }
        }
    }
}
