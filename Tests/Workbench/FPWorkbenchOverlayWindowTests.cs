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
                }
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
