// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Tests
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using FuzzPhyte.Utility.Analytics.WorkBench.Editor;
    public sealed class FPWorkbenchFigureInterestTests
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private static object Get(object o, string field) => o.GetType().GetField(field, Flags).GetValue(o);
        private static object Call(object o, string method, params object[] args) => o.GetType().GetMethod(method, Flags).Invoke(o, args);
        private static List<FPWorkbenchFigurePoint> Points(object store) => (List<FPWorkbenchFigurePoint>)store.GetType().GetField("Values").GetValue(store);
        private static void Pick(FPWorkbenchScatterWindow window, int index)
        {
            var point = ((FPWorkbenchPointView)Get(window, "view")).Points[index]; var b = ((FPWorkbenchPointView)Get(window, "view")).Bounds;
            Rect rect = (Rect)Get(window, "inspectionRect"); var area = new Rect(rect.x + 80, rect.y + 25, rect.width - 95, rect.height - 85);
            var mouse = new Vector2(area.x + (float)((point.X - b.MinimumX) / (b.MaximumX - b.MinimumX)) * area.width,
                area.y + (float)(1 - (point.Y - b.MinimumY) / (b.MaximumY - b.MinimumY)) * area.height) + window.rootVisualElement.worldBound.position;
            window.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = mouse }); window.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = mouse });
        }
        [UnityTest] public IEnumerator UtcFigurePinsOriginalCellsExportsCalloutsAndPersistsSheetAssociation()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPInterest-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            var owner = ScriptableObject.CreateInstance<FPDataWorkbenchWindow>(); FPWorkbenchScatterWindow figure = null;
            try {
                string source = Path.Combine(root, "source.csv"); File.WriteAllText(source, "date,time,depth\n17.01.2025,12:00:00.000000,5\n17.01.2025,12:00:00.002500,6\n17.01.2025,12:00:00.005000,7\n"); owner.ShowUtility(); Call(owner, "AddCsv", source);
                var sheet = ((IList)Get(owner, "sheets"))[0]; var st = sheet.GetType(); var deadline = DateTime.UtcNow.AddSeconds(20);
                while (st.GetField("Dataset").GetValue(sheet) == null && DateTime.UtcNow < deadline) yield return null;
                var dataset = (FPWorkbenchCsvDataset)st.GetField("Dataset").GetValue(sheet); Assert.That(dataset, Is.Not.Null);
                string workbook = (string)Get(owner, "workbookId"), id = (string)st.GetField("Id").GetValue(sheet); st.GetField("Name").SetValue(sheet, "Timed depth");
                var binding = new FPWorkbenchSeriesBinding { XMode = FPWorkbenchXMode.UtcDateTime, DateColumn = 0, XColumn = 1, YColumn = 2 };
                st.GetField("Binding").SetValue(sheet, binding); st.GetField("PlotColumn").SetValue(sheet, 2);
                figure = FPWorkbenchScatterWindow.Open(workbook, dataset, 1, 2, sheetId: id, xBinding: binding); figure.position = new Rect(40, 40, 1200, 850);
                while ((Get(figure, "view") == null || (bool)figure.GetType().GetProperty("Busy", Flags).GetValue(figure)) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(Get(figure, "view"), Is.Not.Null); Assert.That(((FPWorkbenchPointCache)Get(figure, "cache")).OriginUtcTicks, Is.GreaterThan(0));
                var settings = (FPWorkbenchPlotSettings)Get(figure, "settings"); double before = settings.StartX;
                figure.Focus(); figure.Repaint(); yield return null; yield return null; Pick(figure, 0); yield return null;
                Assert.That(Get(figure, "selected"), Is.True); Assert.That(EditorWindow.focusedWindow, Is.SameAs(figure)); Assert.That(st.GetField("FirstColumn").GetValue(sheet), Is.EqualTo(2));
                var point = (FPWorkbenchFigurePoint)Call(figure, "CaptureInterest"); Assert.That(point.References[0].Columns.Length, Is.EqualTo(3), "UTC selection boxes date, time and Y.");
                var store = Get(figure, "pointsOfInterest"); string key = (string)figure.GetType().GetProperty("InterestAxisKey", Flags).GetValue(figure);
                store.GetType().GetMethod("Add", Flags).Invoke(store, new object[] { point, "Start", key }); Assert.That(Points(store).Count, Is.EqualTo(1));
                Undo.PerformUndo(); Assert.That(Points(store), Is.Empty); Assert.That(settings.StartX, Is.EqualTo(before));
                Undo.PerformRedo(); Assert.That(Points(store).Count, Is.EqualTo(1));
                figure.Repaint(); yield return null; yield return null; Pick(figure, 1); yield return null;
                point = (FPWorkbenchFigurePoint)Call(figure, "CaptureInterest"); store.GetType().GetMethod("Add", Flags).Invoke(store, new object[] { point, "Second point", key });
                Assert.That(Points(store).Count, Is.EqualTo(2));
                string output = Path.Combine(root, "Annotated.fpfigure");
                var export = (System.Threading.Tasks.Task<FPWorkbenchImageExportResult>)Call(figure, "ExportPng", output, 640, 480);
                while (!export.IsCompleted) yield return null; export.GetAwaiter().GetResult();
                File.Copy(Path.Combine(output, "figure.png"), Path.Combine(Path.GetTempPath(), "FPWorkbench-POI-preview.png"), true);
                var metadata = JsonUtility.FromJson(File.ReadAllText(Path.Combine(output, "provenance.json")), figure.GetType().GetNestedType("ScatterImageProvenance", BindingFlags.NonPublic));
                var saved = (FPWorkbenchFigureDocument)metadata.GetType().GetField("Figure").GetValue(metadata);
                Assert.That(saved.SourceSheetName, Is.EqualTo("Timed depth")); Assert.That(saved.Binding.XMode, Is.EqualTo(FPWorkbenchXMode.UtcDateTime)); Assert.That(saved.PointsOfInterest.Count, Is.EqualTo(2));
                var image = new Texture2D(2, 2);
                try { Assert.That(image.LoadImage(File.ReadAllBytes(Path.Combine(output, "figure.png"))), Is.True); Assert.That(image.width, Is.EqualTo(640));
                    int white = 0; var pixels = image.GetPixels32(); for (int y = 120; y < 440; y++) for (int x = 465; x < 638; x++) { var c = pixels[y * 640 + x]; if (c.r > 180 && c.g > 180 && c.b > 180) white++; }
                    Assert.That(white, Is.GreaterThan(60), "The reserved callout band must contain readable data labels.");
                } finally { UnityEngine.Object.DestroyImmediate(image); }
                var snapshot = (FPWorkbenchDocument)Call(owner, "CaptureWorkbook"); string path = Path.Combine(root, "Pinned.fpworkbook");
                var save = FPWorkbenchDocumentFile.SaveAsync(snapshot, path); while (!save.IsCompleted) yield return null; save.GetAwaiter().GetResult();
                var load = FPWorkbenchDocumentFile.LoadAsync(path); while (!load.IsCompleted) yield return null;
                using (var loaded = load.GetAwaiter().GetResult()) {
                    var recipe = loaded.Document.Figures.Find(f => f.Id == saved.Id); Assert.That(recipe, Is.Not.Null); Assert.That(recipe.PointsOfInterest.Count, Is.EqualTo(2)); Assert.That(recipe.SourceSheetName, Is.EqualTo("Timed depth"));
                }
                Call(owner, "CloseCurrentSheet"); Assert.That(((FPWorkbenchFigureDocument)Call(figure, "DescribeFigure")).SourceSheetName, Is.EqualTo("Timed depth"));
            } finally { if (figure != null) figure.Close(); owner.Close(); Directory.Delete(root, true); }
        }
        [Test] public void InvalidAnnotationsAreRejectedBeforeSaving()
        {
            Assert.Throws<InvalidDataException>(() => FPWorkbenchFigurePoint.Validate(new List<FPWorkbenchFigurePoint> { new FPWorkbenchFigurePoint { X = double.NaN } }));
            var points = new List<FPWorkbenchFigurePoint>(); for (int i = 0; i < 17; i++) points.Add(new FPWorkbenchFigurePoint());
            Assert.Throws<InvalidDataException>(() => FPWorkbenchFigurePoint.Validate(points));
        }
    }
}
