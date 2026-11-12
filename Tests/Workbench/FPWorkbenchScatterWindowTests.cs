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

    public sealed class FPWorkbenchScatterWindowTests
    {
        [UnityTest] public IEnumerator UnsortedScatterRetainsCoverageNavigatesAndRestoresSnapshot()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPScatterWindow-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            FPWorkbenchScatterWindow window = null, restored = null; const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            Type type = typeof(FPWorkbenchScatterWindow);
            FPWorkbenchPointView View(FPWorkbenchScatterWindow w) => (FPWorkbenchPointView)type.GetField("view", flags).GetValue(w);
            bool Busy(FPWorkbenchScatterWindow w) => (bool)type.GetProperty("Busy", flags).GetValue(w);
            try
            {
                string source = Path.Combine(root, "source.csv"); File.WriteAllText(source, "x,y\n2,3\n0,1\n1,2\n0,4\n");
                var import = FPWorkbenchCsvDataset.ImportAsync(source, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile()); while (!import.IsCompleted) yield return null;
                Assert.That(import.IsFaulted, Is.False); var data = import.Result;
                window = FPWorkbenchScatterWindow.Open("fixture", data, 0, 1); window.position = new Rect(50, 50, 900, 650);
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while ((View(window) == null || Busy(window)) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(View(window), Is.Not.Null); Assert.That(View(window).MatchingCount, Is.EqualTo(4));
                Assert.That(View(window).Points[0].X, Is.EqualTo(2)); Assert.That(View(window).Points[1].X, Is.Zero);
                type.GetField("maximumPoints", flags).SetValue(window, 2); window.Repaint(); yield return null;
                while ((View(window).Points.Count != 2 || Busy(window)) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(View(window).Decimated, Is.True); Assert.That(View(window).MatchingCount, Is.EqualTo(4));
                var published = View(window); string imageOutput = Path.Combine(root, "scatter.fpfigure");
                type.GetField("invalidPolicy", flags).SetValue(window, FPWorkbenchInvalidPolicy.Skip);
                var pngWork0 = (System.Threading.Tasks.Task<FPWorkbenchImageExportResult>)type.GetMethod("ExportPng", flags).Invoke(window, new object[] { imageOutput, 640, 480 });
                while (!pngWork0.IsCompleted) yield return null; pngWork0.GetAwaiter().GetResult();
                Assert.That(View(window), Is.SameAs(published)); Assert.That(Busy(window), Is.False);
                var image = new Texture2D(2, 2);
                try {
                    Assert.That(image.LoadImage(File.ReadAllBytes(Path.Combine(imageOutput, "figure.png"))), Is.True);
                    Assert.That(image.width, Is.EqualTo(640)); Assert.That(image.height, Is.EqualTo(480));
                    int colored = 0; foreach (var pixel in image.GetPixels32()) if (pixel.b > pixel.r + 30 && pixel.g > pixel.r + 30) colored++;
                    Assert.That(colored, Is.GreaterThan(2));
                } finally { UnityEngine.Object.DestroyImmediate(image); }
                var metadata = JsonUtility.FromJson(File.ReadAllText(Path.Combine(imageOutput, "provenance.json")), type.GetNestedType("ScatterImageProvenance", BindingFlags.NonPublic));
                Assert.That(metadata.GetType().GetField("Sampled").GetValue(metadata), Is.EqualTo(true));
                Assert.That(metadata.GetType().GetField("MatchingPoints").GetValue(metadata), Is.EqualTo(4L));
                Assert.That(metadata.GetType().GetField("DisplayedRows").GetValue(metadata), Is.EqualTo(new[] { published.Points[0].Row, published.Points[1].Row }));
                var savedImage = (FPWorkbenchFigureDocument)metadata.GetType().GetField("Figure").GetValue(metadata);
                Assert.That(savedImage.PointBudget, Is.EqualTo(2)); Assert.That(savedImage.InvalidPolicy, Is.EqualTo(FPWorkbenchInvalidPolicy.Reject), "Unapplied policy draft is not the cache's policy.");
                type.GetField("invalidPolicy", flags).SetValue(window, FPWorkbenchInvalidPolicy.Reject);
                type.GetMethod("Navigate", flags).Invoke(window, new object[] { .5, .5, .5, 0d, 0d }); yield return null;
                while ((Busy(window) || View(window).Bounds.MinimumX != .5) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(View(window).Bounds.MinimumX, Is.EqualTo(.5)); Assert.That(View(window).MatchingCount, Is.EqualTo(1));
                var settings = (FPWorkbenchPlotSettings)type.GetField("settings", flags).GetValue(window); settings.XLabel = "X units";
                type.GetField("showSettings", flags).SetValue(window, true); window.Repaint(); yield return null;
                string json = EditorJsonUtility.ToJson(window); window.Close(); window = null;
                restored = ScriptableObject.CreateInstance<FPWorkbenchScatterWindow>(); type.GetMethod("OnDisable", flags).Invoke(restored, null);
                EditorJsonUtility.FromJsonOverwrite(json, restored); type.GetMethod("OnEnable", flags).Invoke(restored, null); restored.Show();
                deadline = DateTime.UtcNow.AddSeconds(10);
                while ((View(restored) == null || Busy(restored)) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(type.GetField("alignedOrigin", flags).GetValue(restored), Is.Null, "Ordinary scatter must not gain phantom aligned provenance after JSON reload.");
                Assert.That(View(restored), Is.Not.Null); Assert.That(View(restored).Bounds.MinimumX, Is.EqualTo(.5));
                Assert.That(((FPWorkbenchPlotSettings)type.GetField("settings", flags).GetValue(restored)).XLabel, Is.EqualTo("X units"));
                Assert.That((string)type.GetField("error", flags).GetValue(restored), Is.Null);
                var payloadType = type.Assembly.GetType("FuzzPhyte.Utility.Analytics.WorkBench.Editor.FPWorkbenchColumnDragPayload");
                object payload = Activator.CreateInstance(payloadType);
                payloadType.GetField("Workbook").SetValue(payload, "fixture"); payloadType.GetField("Dataset").SetValue(payload, data.DatasetId);
                payloadType.GetField("ColumnId").SetValue(payload, data.Cell(0, 1).ColumnId); payloadType.GetField("Column").SetValue(payload, 1);
                Assert.That(type.GetMethod("StageColumn", flags).Invoke(restored, new[] { payload, (object)true }), Is.True);
                Assert.That(View(restored).Bounds.MinimumX, Is.EqualTo(.5), "Staging must not navigate.");
                payloadType.GetField("Dataset").SetValue(payload, "foreign");
                Assert.That(type.GetMethod("StageColumn", flags).Invoke(restored, new[] { payload, (object)false }), Is.False);
                var descriptor = (FPWorkbenchFigureDocument)type.GetMethod("DescribeFigure", flags).Invoke(restored, null);
                Assert.That(descriptor.Binding.XColumn, Is.Zero, "Unapplied staged axes must not replace the plotted binding.");
                Assert.That(descriptor.PointBudget, Is.EqualTo(2)); Assert.That(descriptor.Kind, Is.EqualTo(FPWorkbenchFigureKind.Scatter));
                restored.Close(); UnityEngine.Object.DestroyImmediate(restored); restored = null;
                restored = (FPWorkbenchScatterWindow)type.GetMethod("OpenSaved", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { "fixture", data, descriptor });
                deadline = DateTime.UtcNow.AddSeconds(10);
                while ((View(restored) == null || Busy(restored)) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(View(restored), Is.Not.Null); Assert.That(View(restored).Bounds.MinimumX, Is.EqualTo(.5));
                Assert.That(View(restored).MatchingCount, Is.EqualTo(1));
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
