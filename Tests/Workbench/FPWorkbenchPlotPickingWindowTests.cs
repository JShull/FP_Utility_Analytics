// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Tests
{
    using System;
    using System.Collections;
    using System.Globalization;
    using System.IO;
    using System.Reflection;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using FuzzPhyte.Utility.Analytics.WorkBench.Editor;
    public sealed class FPWorkbenchPlotPickingWindowTests
    {
        [UnityTest] public IEnumerator EmbeddedClickMarksTheActualPointAndBoxesOnlyItsAxesWithYInView()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPPickWindow-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var window = ScriptableObject.CreateInstance<FPDataWorkbenchWindow>(); var type = window.GetType();
            try {
                string path = Path.Combine(root, "source.csv"); var lines = new string[21]; lines[0] = "date,time,noise,depth,z";
                for (int i = 0; i < 20; i++) lines[i + 1] = new DateTime(2025, 1, 17, 11, 49, 52, DateTimeKind.Utc).AddTicks(i * 25000).ToString("dd.MM.yyyy,HH:mm:ss.ffffff", CultureInfo.InvariantCulture) + ",999," + (i + 1) + ",0";
                File.WriteAllLines(path, lines); window.ShowUtility(); window.position = new Rect(40, 40, 1100, 800);
                type.GetMethod("AddCsv", flags).Invoke(window, new object[] { path });
                object sheet = ((IList)type.GetField("sheets", flags).GetValue(window))[0]; var st = sheet.GetType(); var deadline = DateTime.UtcNow.AddSeconds(15);
                while (st.GetField("Dataset").GetValue(sheet) == null && DateTime.UtcNow < deadline) yield return null;
                var data = (FPWorkbenchCsvDataset)st.GetField("Dataset").GetValue(sheet); Assert.That(data, Is.Not.Null);
                var binding = (FPWorkbenchSeriesBinding)st.GetField("Binding").GetValue(sheet); binding.XMode = FPWorkbenchXMode.UtcDateTime; binding.DateColumn = 0; binding.XColumn = 1;
                binding.Rows = new FPWorkbenchRowRange { Enabled = true, FirstRow = 4, EndRow = 12 }; st.GetField("PlotColumn").SetValue(sheet, 3);
                var settings = (FPWorkbenchPlotSettings)st.GetField("Plot").GetValue(sheet); settings.InvertY = true; settings.LockY = true; settings.MinimumY = 0; settings.MaximumY = 20;
                type.GetMethod("StartSeries", flags).Invoke(window, new object[] { sheet, true });
                while ((type.GetField("summary", flags).GetValue(window) == null || ((Rect)type.GetField("embeddedPlotRect", flags).GetValue(window)).width < 100) && DateTime.UtcNow < deadline) { window.Repaint(); yield return null; }
                type.GetMethod("ChangeColumnProjection", flags).Invoke(window, new object[] { sheet, data.Cell(0, 0).ColumnId, 0, (bool?)true });
                window.Repaint(); yield return null; yield return null;
                var view = (FPWorkbenchSeriesView)type.GetField("summary", flags).GetValue(window); Assert.That(view, Is.Not.Null);
                Rect rect = (Rect)type.GetField("embeddedPlotRect", flags).GetValue(window);
                var plotView = type.Assembly.GetType("FuzzPhyte.Utility.Analytics.WorkBench.Editor.FPWorkbenchPlotView");
                var project = plotView.GetMethod("TryProjectScalar", BindingFlags.Static | BindingFlags.NonPublic);
                var point = new FPWorkbenchSeriesPoint(.0075, 8, 7, false); var args = new object[] { rect, view, settings, point, null };
                Assert.That((bool)project.Invoke(null, args), Is.True); var screen = (Vector2)args[4];
                window.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = screen }); window.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = screen });
                while (type.GetField("pickedPlotPoint", flags).GetValue(window) == null && DateTime.UtcNow < deadline) yield return null;
                Assert.That(type.GetField("pickedPlotPoint", flags).GetValue(window), Is.Not.Null, type.GetField("status", flags).GetValue(window)?.ToString());
                var picked = (FPWorkbenchSeriesPoint)type.GetField("pickedPlotPoint", flags).GetValue(window); Assert.That(picked.Row, Is.EqualTo(7)); Assert.That(picked.Y, Is.EqualTo(8));
                Assert.That(st.GetField("FirstRow").GetValue(sheet), Is.EqualTo(7)); Assert.That(st.GetField("FirstColumn").GetValue(sheet), Is.EqualTo(3));
                Assert.That((int[])type.GetField("linkedColumns", flags).GetValue(window), Is.EqualTo(new[] { 0, 1, 3 }));
                Assert.That(type.GetField("linkedCellsOnly", flags).GetValue(window), Is.EqualTo(true));
                var layout = (FPWorkbenchColumnLayout)st.GetField("Columns").GetValue(sheet); Assert.That(layout.Projection.Hidden, Does.Not.Contain(data.Cell(0, 0).ColumnId));
                type.GetMethod("CancelPlotPick", flags).Invoke(window, new object[] { true }); Assert.That(type.GetField("pickedPlotPoint", flags).GetValue(window), Is.Null);
                Assert.That(type.GetField("linkedRow", flags).GetValue(window), Is.EqualTo(-1)); Assert.That(type.GetField("error", flags).GetValue(window), Is.Null);
                Assert.That(File.ReadAllLines(path), Is.EqualTo(lines));
            } finally { window.Close(); Directory.Delete(root, true); }
        }
    }
}
