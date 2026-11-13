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

    public sealed class FPWorkbenchPlotSettingsWindowTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static object Get(object value, string field) => value.GetType().GetField(field, Private).GetValue(value);
        private static object Call(object value, string method, params object[] args) => value.GetType().GetMethod(method, Private).Invoke(value, args);
        private static void Target(FPWorkbenchPlotSettingsWindow panel, object value) => panel.GetType().GetMethod("Target", Private, null, new[] { value.GetType() }, null).Invoke(panel, new[] { value });
        [UnityTest] public IEnumerator PanelSharesSheetStateSurvivesSerializationAndOwnsNoSessionLease()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPPlotSettings-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            var owner = ScriptableObject.CreateInstance<FPDataWorkbenchWindow>();
            FPWorkbenchFigureWindow figure = null; FPWorkbenchPlotSettingsWindow panel = null, restored = null;
            try {
                string source = Path.Combine(root, "source.csv"); File.WriteAllText(source, "x,y,z\n0,2,10\n1,3,11\n2,5,12\n");
                owner.ShowUtility(); Call(owner, "AddCsv", source);
                var sheet = ((IList)Get(owner, "sheets"))[0]; var st = sheet.GetType(); var deadline = DateTime.UtcNow.AddSeconds(15);
                while (st.GetField("Dataset").GetValue(sheet) == null && DateTime.UtcNow < deadline) yield return null;
                var dataset = (FPWorkbenchCsvDataset)st.GetField("Dataset").GetValue(sheet); Assert.That(dataset, Is.Not.Null);
                var binding = (FPWorkbenchSeriesBinding)st.GetField("Binding").GetValue(sheet); binding.XMode = FPWorkbenchXMode.Numeric; binding.XColumn = 0;
                st.GetField("PlotColumn").SetValue(sheet, 1); Call(owner, "StartSeries", sheet, true);
                var session = ((FPWorkbenchFigureSession.Lease)st.GetField("FigureLease").GetValue(sheet)).Session;
                while (session.View == null && DateTime.UtcNow < deadline) yield return null;
                Assert.That(session.View, Is.Not.Null);
                string key = (string)Get(owner, "workbookId") + ":" + st.GetField("Id").GetValue(sheet);
                var document = (FPWorkbenchSheetDocument)owner.GetType().GetMethod("DescribeSheet", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new[] { sheet });
                figure = FPWorkbenchFigureWindow.Open(key, document, dataset);
                panel = ScriptableObject.CreateInstance<FPWorkbenchPlotSettingsWindow>(); panel.Show(); Target(panel, figure); panel.Focus();
                int leases = session.LeaseCount;
                Assert.That(Get(panel, "owner"), Is.SameAs(owner));
                Assert.That(Get(panel, "sheetId"), Is.EqualTo(document.Id));
                Assert.That(((FPWorkbenchFigureSession.Lease)Get(figure, "lease")).Session.Settings, Is.SameAs(st.GetField("Plot").GetValue(sheet)));
                // The panel edits the actual shared object, which is also the workbook capture source.
                var undo = new FPWorkbenchPresentationUndoForTest(session.Settings);
                try {
                    session.Settings.Title = "Shared panel title"; session.Settings.Style = FPWorkbenchPlotStyle.Step; session.Settings.Grid = false; undo.End();
                    Undo.PerformUndo(); Assert.That(session.Settings.Title, Is.Not.EqualTo("Shared panel title"));
                    Undo.PerformRedo(); Assert.That(session.Settings.Title, Is.EqualTo("Shared panel title"));
                } finally { undo.Dispose(); }
                var snapshot = (FPWorkbenchDocument)Call(owner, "CaptureWorkbook");
                Assert.That(snapshot.Sheets[0].Plot.Title, Is.EqualTo("Shared panel title")); Assert.That(snapshot.Sheets[0].Plot.Style, Is.EqualTo(FPWorkbenchPlotStyle.Step));
                Assert.That(snapshot.Sheets[0].Plot.Grid, Is.False);
                string path = Path.Combine(root, "Shared.fpworkbook");
                var save = FPWorkbenchDocumentFile.SaveAsync(snapshot, path);
                while (!save.IsCompleted) yield return null; save.GetAwaiter().GetResult();
                var load = FPWorkbenchDocumentFile.LoadAsync(path);
                while (!load.IsCompleted) yield return null;
                using (var loaded = load.GetAwaiter().GetResult()) {
                    Assert.That(loaded.Document.Sheets[0].Plot.Title, Is.EqualTo("Shared panel title"));
                    Assert.That(loaded.Document.Sheets[0].Plot.Style, Is.EqualTo(FPWorkbenchPlotStyle.Step));
                    Assert.That(loaded.Document.Sheets[0].Plot.Grid, Is.False);
                }
                string state = EditorJsonUtility.ToJson(panel); panel.Close(); panel = null;
                Assert.That(session.LeaseCount, Is.EqualTo(leases), "Closing settings must not retain or release a data lease.");
                restored = ScriptableObject.CreateInstance<FPWorkbenchPlotSettingsWindow>(); EditorJsonUtility.FromJsonOverwrite(state, restored); restored.Show();
                Call(restored, "ResolveTarget"); Assert.That(Get(restored, "owner"), Is.SameAs(owner)); Assert.That(Get(restored, "figure"), Is.SameAs(figure));
                restored.Close(); restored = null;
                owner.Close(); owner = null;
                Assert.That(session.IsDisposed, Is.False); Assert.That(session.LeaseCount, Is.EqualTo(1), "The detached graph outlives its source sheet.");
                panel = ScriptableObject.CreateInstance<FPWorkbenchPlotSettingsWindow>(); panel.Show(); Target(panel, figure);
                Assert.That(Get(panel, "owner"), Is.Null); Assert.That(Get(panel, "figure"), Is.SameAs(figure));
                panel.Close(); panel = null; figure.Close(); figure = null; Assert.That(session.IsDisposed, Is.True);
            } finally {
                if (panel != null) panel.Close(); if (restored != null) restored.Close(); if (figure != null) figure.Close(); if (owner != null) owner.Close(); Directory.Delete(root, true);
            }
        }
        // Exercise the same existing presentation Undo used by the panel without duplicating its implementation.
        private sealed class FPWorkbenchPresentationUndoForTest : IDisposable
        {
            private readonly object undo; private readonly Type type;
            public FPWorkbenchPresentationUndoForTest(FPWorkbenchPlotSettings settings) {
                type = typeof(FPDataWorkbenchWindow).Assembly.GetType("FuzzPhyte.Utility.Analytics.WorkBench.Editor.FPWorkbenchPresentationUndo");
                undo = Activator.CreateInstance(type, new object[] { (Action)(() => {}) }); type.GetMethod("Begin").Invoke(undo, new object[] { settings });
            }
            public void End() => type.GetMethod("End").Invoke(undo, null);
            public void Dispose() => ((IDisposable)undo).Dispose();
        }
        [UnityTest] public IEnumerator DetachedBindingAndPointInspectionRemainAvailableWithoutSheet()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPDetachedSettings-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            FPWorkbenchFigureWindow figure = null; FPWorkbenchPlotSettingsWindow panel = null;
            try {
                string source = Path.Combine(root, "source.csv"); File.WriteAllText(source, "x,y,z\n0,2,10\n1,3,11\n2,5,12\n");
                var importing = FPWorkbenchCsvDataset.ImportAsync(source, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile());
                while (!importing.IsCompleted) yield return null; var data = importing.GetAwaiter().GetResult();
                figure = FPWorkbenchFigureWindow.Open(Guid.NewGuid().ToString("N"), new FPWorkbenchSheetDocument {
                    Name = "Detached fixture", DatasetId = data.DatasetId, SourcePath = source, CacheDirectory = data.CacheDirectory,
                    Binding = new FPWorkbenchSeriesBinding { XMode = FPWorkbenchXMode.Numeric, XColumn = 0, YColumn = 1 },
                    Plot = new FPWorkbenchPlotSettings { Title = "Detached fixture" }
                }, data);
                figure.position = new Rect(80, 80, 900, 650);
                var session = ((FPWorkbenchFigureSession.Lease)Get(figure, "lease")).Session;
                var deadline = DateTime.UtcNow.AddSeconds(15);
                while (session.View == null && DateTime.UtcNow < deadline) yield return null;
                Assert.That(session.View, Is.Not.Null);
                panel = ScriptableObject.CreateInstance<FPWorkbenchPlotSettingsWindow>(); panel.Show(); Target(panel, figure);
                var candidate = session.RequestedBinding; candidate.YColumn = 2;
                Call(figure, "ApplyStandaloneBinding", candidate);
                while ((session.IsBusy || session.Series?.Binding.YColumn != 2) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(session.View.Maximum, Is.EqualTo(12));
                Undo.PerformUndo(); Assert.That(session.RequestedBinding.YColumn, Is.EqualTo(1));
                Undo.PerformRedo(); Assert.That(session.RequestedBinding.YColumn, Is.EqualTo(2));
                while (session.IsBusy && DateTime.UtcNow < deadline) yield return null;
                figure.Repaint(); yield return null; yield return null;
                Rect rect = (Rect)Get(figure, "inspectionRect"); Assert.That(rect.width, Is.GreaterThan(100));
                var plotView = figure.GetType().Assembly.GetType("FuzzPhyte.Utility.Analytics.WorkBench.Editor.FPWorkbenchPlotView");
                object[] args = { rect, session.View, session.Settings, new FPWorkbenchSeriesPoint(1, 11, 1, false), null };
                Assert.That((bool)plotView.GetMethod("TryProjectScalar", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args), Is.True);
                var screen = (Vector2)args[4];
                figure.Focus(); figure.Repaint(); yield return null; yield return null;
                rect = (Rect)Get(figure, "inspectionRect"); args[0] = rect;
                Assert.That((bool)plotView.GetMethod("TryProjectScalar", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args), Is.True);
                screen = (Vector2)args[4];
                // SendEvent takes host-view coordinates; normal dockable windows include the tab strip.
                screen += figure.rootVisualElement.worldBound.position;
                figure.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = screen });
                figure.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = screen });
                while (Get(figure, "selectedPoint") == null && DateTime.UtcNow < deadline) yield return null;
                Assert.That(Get(figure, "selectedPoint"), Is.Not.Null, Get(figure, "pointStatus")?.ToString());
                Assert.That(((FPWorkbenchSeriesPoint)Get(figure, "selectedPoint")).Row, Is.EqualTo(1));
                Assert.That(((FPWorkbenchSeriesPoint)Get(figure, "selectedPoint")).Y, Is.EqualTo(11));
                session.Settings.Fit(0, 1.5); yield return null;
                Assert.That(Get(figure, "selectedPoint"), Is.Not.Null, "A pan/zoom keeps the graph's selected source reference until cleared or closed.");
                int leases = session.LeaseCount; panel.Close(); panel = null; Assert.That(session.LeaseCount, Is.EqualTo(leases));
                figure.Close(); figure = null; Assert.That(session.IsDisposed, Is.True);
            } finally { if (panel != null) panel.Close(); if (figure != null) figure.Close(); Directory.Delete(root, true); }
        }
    }
}
