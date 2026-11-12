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
    public sealed class FPWorkbenchLogWindowTests
    {
        [UnityTest] public IEnumerator ScalarAndScatterUseLogSettingsAndPersistWithoutChangingSourceValues()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPLogWindow-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            FPWorkbenchFigureWindow scalar = null; FPWorkbenchScatterWindow scatter = null;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            try {
                string path = Path.Combine(root, "source.csv"); File.WriteAllText(path, "x,y\n1,1\n10,10\n100,100\n"); byte[] original = File.ReadAllBytes(path);
                var importing = FPWorkbenchCsvDataset.ImportAsync(path, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile());
                while (!importing.IsCompleted) yield return null; var data = importing.GetAwaiter().GetResult();
                var sheet = new FPWorkbenchSheetDocument { DatasetId = data.DatasetId, SourcePath = data.SourcePath, CacheDirectory = data.CacheDirectory,
                    Binding = new FPWorkbenchSeriesBinding { XMode = FPWorkbenchXMode.Numeric, YColumn = 1 }, ShowPlot = true,
                    Plot = new FPWorkbenchPlotSettings { HasRange = true, StartX = 1, EndX = 100, MinimumY = 1, MaximumY = 100, LockY = true,
                        XScale = FPWorkbenchAxisScale.Log10, YScale = FPWorkbenchAxisScale.Log10 } };
                scalar = FPWorkbenchFigureWindow.Open("log-fixture", sheet, data); scalar.position = new Rect(20, 20, 850, 600);
                var lease = (FPWorkbenchFigureSession.Lease)scalar.GetType().GetField("lease", flags).GetValue(scalar);
                var deadline = DateTime.UtcNow.AddSeconds(15);
                while ((lease.Session.IsBusy || lease.Session.View == null) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(lease.Session.View.Bins[1].FirstY, Is.EqualTo(10)); scalar.Repaint(); yield return null;
                var document = new FPWorkbenchDocument(); document.Sheets.Add(sheet);
                string saved = Path.Combine(root, "log.fpworkbook"); var saving = FPWorkbenchDocumentFile.SaveAsync(document, saved); while (!saving.IsCompleted) yield return null; saving.GetAwaiter().GetResult();
                var loading = FPWorkbenchDocumentFile.LoadAsync(saved); while (!loading.IsCompleted) yield return null;
                Assert.That(loading.GetAwaiter().GetResult().Document.Sheets[0].Plot.XScale, Is.EqualTo(FPWorkbenchAxisScale.Log10));
                Assert.That(loading.Result.Document.Sheets[0].Plot.YScale, Is.EqualTo(FPWorkbenchAxisScale.Log10));
                scatter = FPWorkbenchScatterWindow.Open("log-fixture", data, 0, 1); scatter.position = new Rect(100, 60, 950, 700); var st = scatter.GetType();
                while (((bool)st.GetProperty("Busy", flags).GetValue(scatter) || st.GetField("view", flags).GetValue(scatter) == null) && DateTime.UtcNow < deadline) yield return null;
                var settings = (FPWorkbenchPlotSettings)st.GetField("settings", flags).GetValue(scatter); settings.XScale = settings.YScale = FPWorkbenchAxisScale.Log10;
                scatter.Repaint(); yield return null;
                st.GetMethod("Navigate", flags).Invoke(scatter, new object[] { .5, .5, .5, 0d, 0d });
                Assert.That(Math.Sqrt(settings.StartX * settings.EndX), Is.EqualTo(10).Within(1e-12));
                Assert.That(Math.Sqrt(settings.MinimumY * settings.MaximumY), Is.EqualTo(10).Within(1e-12));
                Assert.That(settings.EndX / settings.StartX, Is.EqualTo(10).Within(1e-12));
                var descriptor = (FPWorkbenchFigureDocument)st.GetMethod("DescribeFigure", flags).Invoke(scatter, null); descriptor.Validate();
                Assert.That(descriptor.Plot.YScale, Is.EqualTo(FPWorkbenchAxisScale.Log10));
                double validMinimum = descriptor.Plot.MinimumY; descriptor.Plot.MinimumY = 0; Assert.Throws<InvalidDataException>(() => descriptor.Validate()); descriptor.Plot.MinimumY = validMinimum;
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(original));
            } finally {
                if (scalar != null) { scalar.Close(); UnityEngine.Object.DestroyImmediate(scalar); }
                if (scatter != null) { scatter.Close(); UnityEngine.Object.DestroyImmediate(scatter); }
                Directory.Delete(root, true);
            }
        }
    }
}
