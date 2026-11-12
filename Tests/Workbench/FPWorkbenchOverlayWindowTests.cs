// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Tests
{
    using System;
    using System.Collections;
    using System.IO;
    using System.Reflection;
    using System.Text;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using FuzzPhyte.Utility.Analytics.WorkBench.Editor;

    public sealed class FPWorkbenchOverlayWindowTests
    {
        [UnityTest] public IEnumerator OverlayWaitsForSharedCapacityAndReleasesReservationsAfterClose()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPOverlayBudget-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            FPWorkbenchOverlayWindow window = null; FPWorkbenchMemoryBudget.Reservation blocker = null;
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance; var type = typeof(FPWorkbenchOverlayWindow);
            var budget = (FPWorkbenchMemoryBudget)typeof(FPDataWorkbenchWindow).GetField("pageMemory", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            long baseline = budget.UsedBytes; int leases = budget.LeaseCount;
            try {
                string path = Path.Combine(root, "source.csv"); File.WriteAllText(path, "x,y\n0,1\n1,2\n2,3\n");
                var importing = FPWorkbenchCsvDataset.ImportAsync(path, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile()); while (!importing.IsCompleted) yield return null;
                var data = importing.GetAwaiter().GetResult();
                Assert.That(budget.TryAcquire(budget.LimitBytes - budget.UsedBytes, out blocker), Is.True);
                window = FPWorkbenchOverlayWindow.Open(null, data, new FPWorkbenchSeriesBinding { XMode = FPWorkbenchXMode.Numeric, XColumn = 0 }, 1);
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!(bool)type.GetField("waitingForMemory", flags).GetValue(window) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(type.GetField("waitingForMemory", flags).GetValue(window), Is.EqualTo(true));
                Assert.That(type.GetField("pendingBuild", flags).GetValue(window), Is.Not.Null);
                Assert.That(type.GetField("building", flags).GetValue(window), Is.Null);
                var resources = type.Assembly.GetType("FuzzPhyte.Utility.Analytics.WorkBench.Editor.FPWorkbenchEditorResources");
                var scans = (FPWorkbenchMemoryBudget)resources.GetField("Scans", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                Assert.That(scans.LeaseCount, Is.Zero, "A refused construction must roll back its scan token.");
                Assert.That(type.GetField("querying", flags).GetValue(window), Is.Null); Assert.That(type.GetField("view", flags).GetValue(window), Is.Null);
                blocker.Dispose(); blocker = null;
                while (type.GetField("view", flags).GetValue(window) == null && DateTime.UtcNow < deadline) yield return null;
                Assert.That(type.GetField("view", flags).GetValue(window), Is.Not.Null); Assert.That(type.GetField("viewMemory", flags).GetValue(window), Is.Not.Null);
                var settings = (FPWorkbenchPlotSettings)type.GetField("settings", flags).GetValue(window); settings.StartX = .25; settings.EndX = 1.75;
                type.GetMethod("Tick", flags).Invoke(window, null);
                window.Close(); type.GetMethod("Tick", flags).Invoke(window, null);
                Assert.That(type.GetField("windowActive", flags).GetValue(window), Is.EqualTo(false)); Assert.That(type.GetField("querying", flags).GetValue(window), Is.Null);
                UnityEngine.Object.DestroyImmediate(window); window = null;
                while ((budget.UsedBytes != baseline || budget.LeaseCount != leases) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(budget.UsedBytes, Is.EqualTo(baseline)); Assert.That(budget.LeaseCount, Is.EqualTo(leases));
            }
            finally { blocker?.Dispose(); if (window != null) { window.Close(); UnityEngine.Object.DestroyImmediate(window); } Directory.Delete(root, true); }
        }
        [UnityTest] public IEnumerator AppendedSeriesShareViewportAndRestoreAfterReload()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPOverlayWindow-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            string source = Path.Combine(root, "source.csv"); var text = new StringBuilder("x,a,b\n");
            for (int i = 0; i < 1000; i++) text.Append(i).Append(',').Append(i).Append(',').Append(-i).Append('\n'); File.WriteAllText(source, text.ToString());
            var import = FPWorkbenchCsvDataset.ImportAsync(source, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile()); while (!import.IsCompleted) yield return null;
            Assert.That(import.IsFaulted, Is.False); var dataset = import.Result;
            FPWorkbenchOverlayWindow window = null, restored = null; const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var type = typeof(FPWorkbenchOverlayWindow);
            FPWorkbenchOverlayView View(FPWorkbenchOverlayWindow target) => (FPWorkbenchOverlayView)type.GetField("view", flags).GetValue(target);
            bool Busy(FPWorkbenchOverlayWindow target) => (bool)type.GetProperty("Busy", flags).GetValue(target);
            try
            {
                window = FPWorkbenchOverlayWindow.Open("workbook-fixture", dataset, new FPWorkbenchSeriesBinding { XMode = FPWorkbenchXMode.Numeric, XColumn = 0, YColumn = 1 }, 1);
                window.position = new Rect(70, 70, 900, 600); var deadline = DateTime.UtcNow.AddSeconds(10);
                while ((View(window) == null || Busy(window)) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(View(window), Is.Not.Null);
                var add = type.GetMethod("AddColumn", flags);
                Assert.That(add.Invoke(window, new object[] { "foreign", dataset.DatasetId, dataset.Cell(0, 2).ColumnId, 2 }), Is.False);
                Assert.That(add.Invoke(window, new object[] { "workbook-fixture", dataset.DatasetId, dataset.Cell(0, 2).ColumnId, 2 }), Is.True);
                while ((View(window) == null || Busy(window)) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(View(window).Series.Count, Is.EqualTo(2)); Assert.That(View(window).Minimum, Is.EqualTo(-999)); Assert.That(View(window).Maximum, Is.EqualTo(999));
                var settings = (FPWorkbenchPlotSettings)type.GetField("settings", flags).GetValue(window); settings.StartX = 100; settings.EndX = 200;
                window.Repaint(); yield return null;
                while ((View(window).StartX != 100 || Busy(window)) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(View(window).StartX, Is.EqualTo(100)); foreach (var series in View(window).Series) Assert.That(series.EndX, Is.EqualTo(200));
                var axes = (FPWorkbenchOverlayAxes)type.GetField("axes", flags).GetValue(window);
                Undo.IncrementCurrentGroup(); type.GetMethod("SetAxisSide", flags).Invoke(window, new object[] { 1, true });
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); Assert.That(axes.Right[1], Is.False);
                Undo.PerformRedo(); Assert.That(axes.Right[1], Is.True);
                Assert.That(View(window).StartX, Is.EqualTo(100)); Assert.That(View(window).Series.Count, Is.EqualTo(2));
 axes.RightSettings.YLabel = "Right channel"; axes.RightSettings.InvertY = true;
                Assert.That(axes.Resolve(View(window), settings, false).Minimum, Is.EqualTo(100));
                Assert.That(axes.Resolve(View(window), settings, true).Maximum, Is.EqualTo(-100));
                settings.XScale = FPWorkbenchAxisScale.Log10; settings.YScale = FPWorkbenchAxisScale.Log10;
                settings.Title = "Dive comparison"; settings.XLabel = "Elapsed time (s)"; settings.YLabel = "Depth (m)";
                type.GetField("showFigureSettings", flags).SetValue(window, true);
                foreach (FPWorkbenchPlotStyle style in Enum.GetValues(typeof(FPWorkbenchPlotStyle)))
                {
                    settings.Style = style; settings.LineWidth = 2; settings.MarkerSize = 5;
                    window.Repaint(); yield return null; yield return null;
                    Assert.That(View(window).StartX, Is.EqualTo(100));
                    var completed = View(window); string output = Path.Combine(root, style + ".fpfigure");
                    var pngWork0 = (System.Threading.Tasks.Task<FPWorkbenchImageExportResult>)type.GetMethod("ExportPng", flags).Invoke(window, new object[] { output, 640, 480 });
                    while (!pngWork0.IsCompleted) yield return null; pngWork0.GetAwaiter().GetResult();
                    Assert.That(View(window), Is.SameAs(completed)); Assert.That(Busy(window), Is.False);
                    var image = new Texture2D(2, 2);
                    try {
                        Assert.That(image.LoadImage(File.ReadAllBytes(Path.Combine(output, "figure.png"))), Is.True);
                        Assert.That(image.width, Is.EqualTo(640)); Assert.That(image.height, Is.EqualTo(480));
                        int colored = 0; foreach (var pixel in image.GetPixels32()) if (Math.Max(pixel.r, Math.Max(pixel.g, pixel.b)) - Math.Min(pixel.r, Math.Min(pixel.g, pixel.b)) > 60) colored++;
                        Assert.That(colored, Is.GreaterThan(100));
                    } finally { UnityEngine.Object.DestroyImmediate(image); }
                    var metadata = JsonUtility.FromJson(File.ReadAllText(Path.Combine(output, "provenance.json")), type.GetNestedType("OverlayImageProvenance", BindingFlags.NonPublic));
                    var exported = (FPWorkbenchFigureDocument)metadata.GetType().GetField("Figure").GetValue(metadata);
                    Assert.That(exported.Axes.Right, Is.EqualTo(new[] { false, true })); Assert.That(exported.Plot.YScale, Is.EqualTo(FPWorkbenchAxisScale.Log10));
                    Assert.That(exported.Axes.RightSettings.YScale, Is.EqualTo(FPWorkbenchAxisScale.Linear)); Assert.That(exported.Colors.Length, Is.EqualTo(2));
                }
                axes.Right[0] = true;
                var pngWork1 = (System.Threading.Tasks.Task<FPWorkbenchImageExportResult>)type.GetMethod("ExportPng", flags).Invoke(window, new object[] { Path.Combine(root, "right-only.fpfigure"), 640, 480 });
                while (!pngWork1.IsCompleted) yield return null; pngWork1.GetAwaiter().GetResult();
                axes.Right[0] = false;
                axes.RightSettings.YScale = FPWorkbenchAxisScale.Log10;
                string invalidOutput = Path.Combine(root, "invalid-log.fpfigure");
                Assert.That(Assert.Throws<TargetInvocationException>(() => type.GetMethod("ExportPng", flags).Invoke(window, new object[] { invalidOutput, 640, 480 })).InnerException, Is.TypeOf<ArgumentException>());
                Assert.That(Directory.Exists(invalidOutput), Is.False); axes.RightSettings.YScale = FPWorkbenchAxisScale.Linear;
                string json = EditorJsonUtility.ToJson(window); window.Close(); window = null;
                restored = ScriptableObject.CreateInstance<FPWorkbenchOverlayWindow>(); type.GetMethod("OnDisable", flags).Invoke(restored, null);
                EditorJsonUtility.FromJsonOverwrite(json, restored); type.GetMethod("OnEnable", flags).Invoke(restored, null); restored.Show();
                while ((View(restored) == null || Busy(restored)) && DateTime.UtcNow < deadline) yield return null;
                var restoredSettings = (FPWorkbenchPlotSettings)type.GetField("settings", flags).GetValue(restored);
                var restoredAxes = (FPWorkbenchOverlayAxes)type.GetField("axes", flags).GetValue(restored);
                Assert.That(restoredAxes.Right, Is.EqualTo(new[] { false, true }));
                Assert.That(restoredAxes.RightSettings.YLabel, Is.EqualTo("Right channel")); Assert.That(restoredAxes.RightSettings.InvertY, Is.True);
                Assert.That(restoredSettings.XScale, Is.EqualTo(FPWorkbenchAxisScale.Log10)); Assert.That(restoredSettings.YScale, Is.EqualTo(FPWorkbenchAxisScale.Log10));
                Assert.That(restoredAxes.RightSettings.YScale, Is.EqualTo(FPWorkbenchAxisScale.Linear));
                restoredAxes.RightSettings.YScale = FPWorkbenchAxisScale.Log10; restored.Repaint(); yield return null; yield return null;
                Assert.That(View(restored).Series[1].Minimum, Is.LessThan(0), "Invalid log presentation must not rewrite negative measurements.");
                restoredAxes.RightSettings.YScale = FPWorkbenchAxisScale.Linear;
                Assert.That(restoredSettings.Title, Is.EqualTo("Dive comparison"));
                Assert.That(restoredSettings.XLabel, Is.EqualTo("Elapsed time (s)"));
                Assert.That(restoredSettings.YLabel, Is.EqualTo("Depth (m)"));
                Assert.That((bool)type.GetField("showFigureSettings", flags).GetValue(restored), Is.True);
                Assert.That(restoredSettings.Style, Is.EqualTo(FPWorkbenchPlotStyle.Points));
                Assert.That(restoredSettings.LineWidth, Is.EqualTo(2)); Assert.That(restoredSettings.MarkerSize, Is.EqualTo(5));
                Assert.That(View(restored), Is.Not.Null); Assert.That(View(restored).Series.Count, Is.EqualTo(2)); Assert.That(View(restored).StartX, Is.EqualTo(100));
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
