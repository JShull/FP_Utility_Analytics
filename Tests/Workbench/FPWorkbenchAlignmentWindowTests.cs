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

    public sealed class FPWorkbenchAlignmentWindowTests
    {
        [UnityTest] public IEnumerator AlignmentWindowAdmitsBuildsPagesAndRestoresFrozenCorrectionMapping()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPAlignWindow-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            FPWorkbenchAlignmentWindow window = null, restored = null; FPWorkbenchScatterWindow plot = null; const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            Type type = typeof(FPWorkbenchAlignmentWindow);
            FPWorkbenchAlignmentMetadata Meta(FPWorkbenchAlignmentWindow w) => (FPWorkbenchAlignmentMetadata)type.GetField("metadata", flags).GetValue(w);
            bool Busy(FPWorkbenchAlignmentWindow w) => (bool)type.GetProperty("Busy", flags).GetValue(w);
            try
            {
                string a = Path.Combine(root, "a.csv"), b = Path.Combine(root, "b.csv"); File.WriteAllText(a, "key,x\n1,10\n2,20\n4,40\n"); File.WriteAllText(b, "key,y\n0,5\n2,15\n4,25\n");
                var loadA = FPWorkbenchCsvDataset.ImportAsync(a, Path.Combine(root, "a-cache"), new FPWorkbenchCsvProfile()); var loadB = FPWorkbenchCsvDataset.ImportAsync(b, Path.Combine(root, "b-cache"), new FPWorkbenchCsvProfile());
                while (!loadA.IsCompleted || !loadB.IsCompleted) yield return null;
                Assert.That(loadA.IsFaulted || loadB.IsFaulted, Is.False); var left = loadA.Result; var right = loadB.Result;
                FPWorkbenchSourceDocument Source(FPWorkbenchCsvDataset data, string name) => new FPWorkbenchSourceDocument { DatasetId = data.DatasetId, SourcePath = data.SourcePath, CacheDirectory = data.CacheDirectory, Name = name, Import = data.ImportProfile };
                var changes = new FPWorkbenchChangeSet { DatasetId = left.DatasetId }; changes.Set(left.Cell(0, 0), "1", "0", FPWorkbenchEditKind.Number, "key fixture");
                string frozenFingerprint = changes.Snapshot().Fingerprint;
                var leftReference = Source(left, "Left"); leftReference.HasCalculations = true;
                leftReference.Calculations = new FPWorkbenchFormulaSet { DatasetId = left.DatasetId };
                leftReference.Calculations.Columns.Add(new FPWorkbenchDerivedColumn { Name = "Doubled X", Expression = "C2 * 2" });
                window = FPWorkbenchAlignmentWindow.Open("fixture", new[] { leftReference, Source(right, "Right") }, new[] { changes }); window.position = new Rect(50, 50, 1000, 750);
                leftReference.Calculations.Columns[0].Expression = "C2 * 9";
                changes.Set(left.Cell(0, 0), "1", "-1", FPWorkbenchEditKind.Number, "later external revision");
                var deadline = DateTime.UtcNow.AddSeconds(10); while (Busy(window) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(type.GetField("left", flags).GetValue(window), Is.Not.Null); Assert.That(type.GetField("right", flags).GetValue(window), Is.Not.Null);
                var options = (FPWorkbenchAlignmentOptions)type.GetField("options", flags).GetValue(window); options.KeyKind = FPWorkbenchAlignmentKeyKind.Decimal; options.KeyDomain = "Observation ID";
                type.GetMethod("BeginBuild", flags).Invoke(window, null);
                while ((Busy(window) || Meta(window) == null || type.GetField("pairs", flags).GetValue(window) == null) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(Meta(window).MatchedPairs, Is.EqualTo(2)); Assert.That(Meta(window).UnmatchedLeftRows, Is.EqualTo(1));
                var pairs = (FPWorkbenchAlignedRows[])type.GetField("pairs", flags).GetValue(window); Assert.That(pairs[0].LeftRow, Is.EqualTo(1)); Assert.That(pairs[0].RightRow, Is.EqualTo(1));
                options.LeftUnits = "m"; type.GetMethod("BeginBuild", flags).Invoke(window, null);
                Assert.That((string)type.GetField("error", flags).GetValue(window), Does.Contain("units")); Assert.That(Meta(window).MatchedPairs, Is.EqualTo(2));
                options.LeftUnits = "1"; type.GetField("effective", flags).SetValue(window, true); type.GetMethod("BeginBuild", flags).Invoke(window, null);
                deadline = DateTime.UtcNow.AddSeconds(10); while ((Busy(window) || Meta(window).MatchedPairs != 3) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(Meta(window).MatchedPairs, Is.EqualTo(3)); Assert.That(Meta(window).LeftCorrectionFingerprint, Is.EqualTo(frozenFingerprint));
                type.GetField("leftValueColumn", flags).SetValue(window, 2); type.GetField("rightValueColumn", flags).SetValue(window, 1);
                type.GetMethod("BeginPlot", flags).Invoke(window, null);
                deadline = DateTime.UtcNow.AddSeconds(10);
                while ((Busy(window) || type.GetField("generatedPlot", flags).GetValue(window) == null) && DateTime.UtcNow < deadline) yield return null;
                plot = (FPWorkbenchScatterWindow)type.GetField("generatedPlot", flags).GetValue(window); Assert.That(plot, Is.Not.Null);
                Type pt = plot.GetType();
                while ((pt.GetField("view", flags).GetValue(plot) == null || (bool)pt.GetProperty("Busy", flags).GetValue(plot)) && DateTime.UtcNow < deadline) yield return null;
                var plotted = (FPWorkbenchPointView)pt.GetField("view", flags).GetValue(plot); Assert.That(plotted, Is.Not.Null); Assert.That(plotted.MatchingCount, Is.EqualTo(3));
                Assert.That(plotted.Points[0].X, Is.EqualTo(20)); Assert.That(plotted.Points[0].Y, Is.EqualTo(5));
                object origin = pt.GetField("alignedOrigin", flags).GetValue(plot); Assert.That(origin, Is.Not.Null);
                Assert.That((string)origin.GetType().GetField("Left").GetValue(origin), Is.EqualTo(left.DatasetId));
                Assert.That((string)origin.GetType().GetField("Right").GetValue(origin), Is.EqualTo(right.DatasetId));
                var descriptor = (FPWorkbenchFigureDocument)pt.GetMethod("DescribeFigure", flags).Invoke(plot, null);
                Assert.That(descriptor.Kind, Is.EqualTo(FPWorkbenchFigureKind.AlignedScatter)); Assert.That(descriptor.DatasetId, Is.EqualTo(left.DatasetId));
                Assert.That(descriptor.AlignedRecipe.RightDatasetId, Is.EqualTo(right.DatasetId));
                var material = (FPWorkbenchCsvDataset)pt.GetField("dataset", flags).GetValue(plot); string product = Path.GetDirectoryName(material.SourcePath), index = material.CacheDirectory;
                plot.Close(); UnityEngine.Object.DestroyImmediate(plot); plot = null;
                Assert.That(product, Does.StartWith(left.CacheDirectory + Path.DirectorySeparatorChar)); Assert.That(index, Does.StartWith(left.CacheDirectory + Path.DirectorySeparatorChar));
                Directory.Delete(index, true); Directory.Delete(product, true);
                plot = (FPWorkbenchScatterWindow)pt.GetMethod("OpenSavedAligned", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { "fixture", left, right, descriptor });
                deadline = DateTime.UtcNow.AddSeconds(10);
                while ((pt.GetField("view", flags).GetValue(plot) == null || (bool)pt.GetProperty("Busy", flags).GetValue(plot)) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(((FPWorkbenchPointView)pt.GetField("view", flags).GetValue(plot)).MatchingCount, Is.EqualTo(3));
                Assert.That(((FPWorkbenchPlotSettings)pt.GetField("settings", flags).GetValue(plot)).StartX, Is.EqualTo(descriptor.Plot.StartX));
                plot.Close(); UnityEngine.Object.DestroyImmediate(plot); plot = null;
                type.GetField("firstPair", flags).SetValue(window, 1L); window.Repaint(); yield return null;
                while ((long)type.GetField("displayedPage", flags).GetValue(window) != 1 && DateTime.UtcNow < deadline) yield return null;
                string json = EditorJsonUtility.ToJson(window); window.Close(); UnityEngine.Object.DestroyImmediate(window); window = null;
                restored = ScriptableObject.CreateInstance<FPWorkbenchAlignmentWindow>(); type.GetMethod("OnDisable", flags).Invoke(restored, null);
                EditorJsonUtility.FromJsonOverwrite(json, restored); type.GetMethod("OnEnable", flags).Invoke(restored, null); restored.Show();
                deadline = DateTime.UtcNow.AddSeconds(10);
                while ((Busy(restored) || Meta(restored) == null || (long)type.GetField("displayedPage", flags).GetValue(restored) != 1) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(Meta(restored).MatchedPairs, Is.EqualTo(3)); Assert.That((long)type.GetField("displayedPage", flags).GetValue(restored), Is.EqualTo(1));
                Assert.That(((FPWorkbenchAlignedRows[])type.GetField("pairs", flags).GetValue(restored))[0].LeftRow, Is.EqualTo(1));
                Assert.That((string)type.GetField("error", flags).GetValue(restored), Is.Null);
            }
            finally
            {
                if (plot != null) { plot.Close(); UnityEngine.Object.DestroyImmediate(plot); }
                if (window != null) { window.Close(); UnityEngine.Object.DestroyImmediate(window); }
                if (restored != null) { restored.Close(); UnityEngine.Object.DestroyImmediate(restored); }
                Directory.Delete(root, true);
            }
        }
    }
}
