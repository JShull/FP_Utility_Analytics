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
        [Test] public void AggregatedStylesRenderDistinctGeometryAndHonorPointSize()
        {
            var view = new FPWorkbenchSeriesView();
            void Set(string name, object value) => view.GetType().GetProperty(name).SetValue(view, value);
            Set("StartX", 0d); Set("EndX", 10d); Set("Minimum", 0d); Set("Maximum", 10d);
            Set("Bins", new[] { new FPWorkbenchSeriesBin { Count = 20, FirstIndex = 0, EndIndex = 20, FirstX = 1, LastX = 9, FirstY = 2, LastY = 8, Minimum = 1, Maximum = 9 } });
            var settings = new FPWorkbenchPlotSettings { StartX = 0, EndX = 10, LockY = true, MinimumY = 0, MaximumY = 10, MarkerSize = 3 };
            var render = typeof(FPWorkbenchFigureWindow).GetMethod("RenderScalarImage", BindingFlags.Static | BindingFlags.NonPublic);
            bool[] Pixels(FPWorkbenchPlotStyle style) {
                settings.Style = style;
                object[] arguments = { view, settings, "Fixture", 640, 480, false, 0 };
                var image = (Texture2D)render.Invoke(null, arguments);
                try {
                    Assert.That(arguments[5], Is.True, "Overview remains explicitly aggregated.");
                    var pixels = image.GetPixels32(); var mask = new bool[pixels.Length];
                    for (int i = 0; i < pixels.Length; i++) mask[i] = pixels[i].b > pixels[i].r + 30 && pixels[i].g > pixels[i].r + 30;
                    return mask;
                } finally { UnityEngine.Object.DestroyImmediate(image); }
            }
            var line = Pixels(FPWorkbenchPlotStyle.Line); var step = Pixels(FPWorkbenchPlotStyle.Step); var points = Pixels(FPWorkbenchPlotStyle.Points);
            int Count(bool[] pixels) => Array.FindAll(pixels, p => p).Length;
            Assert.That(Count(line), Is.GreaterThan(Count(points) + 100));
            Assert.That(Count(step), Is.GreaterThan(Count(points) + 100));
            Assert.That(step, Is.Not.EqualTo(line), "Step must retain its horizontal/vertical endpoint trace.");
            settings.MarkerSize = 9;
            Assert.That(Count(Pixels(FPWorkbenchPlotStyle.Points)), Is.GreaterThan(Count(points) * 2));
            Set("Representatives", new[] { new FPWorkbenchSeriesPoint(1, 2, 0, false), new FPWorkbenchSeriesPoint(3, 1, 5, false), new FPWorkbenchSeriesPoint(5, 9, 10, false), new FPWorkbenchSeriesPoint(9, 8, 19, false) });
            var bins = (FPWorkbenchSeriesBin[])view.GetType().GetProperty("Bins").GetValue(view); bins[0].RepresentativeCount = 4;
            settings.MarkerSize = 1; int smallLine = Count(Pixels(FPWorkbenchPlotStyle.Line)); settings.MarkerSize = 12;
            Assert.That(Count(Pixels(FPWorkbenchPlotStyle.Line)), Is.GreaterThan(smallLine + 100), "Line also draws adjustable markers at represented source samples.");
        }
        [Test] public void ScalarImageOmitsMixedLogBinsAndDoesNotConnectAcrossGaps()
        {
            var view = new FPWorkbenchSeriesView();
            void Set(string name, object value) => view.GetType().GetProperty(name).SetValue(view, value);
            Set("StartX", 1d); Set("EndX", 3d); Set("Minimum", -1d); Set("Maximum", 3d);
            var bins = new[] {
                new FPWorkbenchSeriesBin { Count = 2, FirstIndex = 0, EndIndex = 2, FirstX = 1, LastX = 1.5, Minimum = -1, Maximum = 2, FirstY = -1, LastY = 2 },
                new FPWorkbenchSeriesBin { Count = 1, FirstIndex = 2, EndIndex = 3, FirstX = 2, LastX = 2, Minimum = 2, Maximum = 2, FirstY = 2, LastY = 2 },
                new FPWorkbenchSeriesBin { Count = 1, GapCount = 1, FirstIndex = 3, EndIndex = 4, FirstX = 3, LastX = 3, Minimum = 3, Maximum = 3, FirstY = 3, LastY = 3 } };
            Set("Bins", bins);
            var settings = new FPWorkbenchPlotSettings { LockY = true, MinimumY = 1, MaximumY = 4, YScale = FPWorkbenchAxisScale.Log10, XScale = FPWorkbenchAxisScale.Log10 };
            var render = typeof(FPWorkbenchFigureWindow).GetMethod("RenderScalarImage", BindingFlags.Static | BindingFlags.NonPublic);
            int RenderPixels() {
                object[] arguments = { view, settings, "Fixture", 640, 480, false, 0 };
                var image = (Texture2D)render.Invoke(null, arguments);
                try {
                    Assert.That(arguments[5], Is.EqualTo(false)); Assert.That(arguments[6], Is.EqualTo(1));
                    int cyan = 0; foreach (var pixel in image.GetPixels32()) if (pixel.b > pixel.r + 30 && pixel.g > pixel.r + 30) cyan++;
                    return cyan;
                } finally { UnityEngine.Object.DestroyImmediate(image); }
            }
            Assert.That(RenderPixels(), Is.LessThan(30), "A gap must retain isolated sample markers.");
            bins[2].GapCount = 0;
            Assert.That(RenderPixels(), Is.GreaterThan(100), "Contiguous valid samples should connect.");
        }
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
                var stableView = session.View; session.Settings.YScale = FPWorkbenchAxisScale.Log10;
                foreach (FPWorkbenchPlotStyle style in Enum.GetValues(typeof(FPWorkbenchPlotStyle))) {
                    session.Settings.Style = style;
                    string output = Path.Combine(root, style + ".fpfigure");
                    var pngWork0 = (System.Threading.Tasks.Task<FPWorkbenchImageExportResult>)type.GetMethod("ExportPng", flags).Invoke(first, new object[] { output, 640, 480 });
                    while (!pngWork0.IsCompleted) yield return null; pngWork0.GetAwaiter().GetResult();
                    Assert.That(session.View, Is.SameAs(stableView)); Assert.That(session.IsBusy, Is.False);
                    var image = new Texture2D(2, 2);
                    try {
                        Assert.That(image.LoadImage(File.ReadAllBytes(Path.Combine(output, "figure.png"))), Is.True);
                        Assert.That(image.width, Is.EqualTo(640)); Assert.That(image.height, Is.EqualTo(480));
                        int cyan = 0; foreach (var pixel in image.GetPixels32()) if (pixel.b > pixel.r + 30 && pixel.g > pixel.r + 30) cyan++;
                        Assert.That(cyan, Is.GreaterThan(5));
                    } finally { UnityEngine.Object.DestroyImmediate(image); }
                    var metadata = JsonUtility.FromJson(File.ReadAllText(Path.Combine(output, "provenance.json")), type.GetNestedType("ScalarImageProvenance", BindingFlags.NonPublic));
                    var sheet = (FPWorkbenchSheetDocument)metadata.GetType().GetField("Sheet").GetValue(metadata);
                    Assert.That(sheet.DatasetId, Is.EqualTo(dataset.DatasetId)); Assert.That(sheet.Binding.YColumn, Is.EqualTo(1));
                    Assert.That(sheet.Plot.YScale, Is.EqualTo(FPWorkbenchAxisScale.Log10)); Assert.That(sheet.Plot.Style, Is.EqualTo(style));
                }
                double end = session.Settings.EndX; session.Settings.EndX += 1;
                string staleOutput = Path.Combine(root, "stale.fpfigure");
                Assert.That(Assert.Throws<TargetInvocationException>(() => type.GetMethod("ExportPng", flags).Invoke(first, new object[] { staleOutput, 640, 480 })).InnerException, Is.TypeOf<InvalidOperationException>());
                Assert.That(Directory.Exists(staleOutput), Is.False); session.Settings.EndX = end;
                first.Close();
                // A captured update callback after close must not resurrect a source/session lease.
                type.GetField("opening", flags).SetValue(first, System.Threading.Tasks.Task.FromResult(dataset));
                type.GetMethod("Tick", flags).Invoke(first, null);
                Assert.That(type.GetField("lease", flags).GetValue(first), Is.Null);
                Assert.That(session.LeaseCount, Is.EqualTo(1));
                first = null;
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
