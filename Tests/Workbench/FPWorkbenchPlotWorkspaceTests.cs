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
    public sealed class FPWorkbenchPlotWorkspaceTests
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        [UnityTest] public IEnumerator NativePairReusesBothTabsPreservesViewportAndReleasesOnlyClosedOwners()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPWorkspace-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            FPWorkbenchFigureWindow figure = null; FPWorkbenchPlotSettingsWindow panel = null;
            try {
                string source = Path.Combine(root, "source.csv"); File.WriteAllText(source, "x,y\n0,2\n1,3\n2,5\n");
                var import = FPWorkbenchCsvDataset.ImportAsync(source, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile());
                while (!import.IsCompleted) yield return null; var data = import.GetAwaiter().GetResult();
                var settings = new FPWorkbenchPlotSettings { HasRange = true, StartX = .25, EndX = 1.75 };
                figure = FPWorkbenchFigureWindow.Open(Guid.NewGuid().ToString("N"), new FPWorkbenchSheetDocument { Name = "Workspace fixture", DatasetId = data.DatasetId,
                    SourcePath = source, CacheDirectory = data.CacheDirectory, Plot = settings,
                    Binding = new FPWorkbenchSeriesBinding { XMode = FPWorkbenchXMode.Numeric, XColumn = 0, YColumn = 1 } }, data);
                var session = ((FPWorkbenchFigureSession.Lease)figure.GetType().GetField("lease", Flags).GetValue(figure)).Session;
                panel = ScriptableObject.CreateInstance<FPWorkbenchPlotSettingsWindow>(); panel.Show();
                panel.GetType().GetMethod("Target", Flags, null, new[] { typeof(FPWorkbenchFigureWindow) }, null).Invoke(panel, new object[] { figure });
                var workspace = panel.GetType().Assembly.GetType("FuzzPhyte.Utility.Analytics.WorkBench.Editor.FPWorkbenchPlotWorkspace");
                var pair = workspace.GetMethod("Pair", BindingFlags.Static | BindingFlags.NonPublic);
                var paired = workspace.GetMethod("IsPaired", BindingFlags.Static | BindingFlags.NonPublic);
                pair.Invoke(null, new object[] { panel, figure }); yield return null;
                Assert.That((bool)paired.Invoke(null, new object[] { panel, figure }), Is.True);
                Assert.That(panel.docked && figure.docked, Is.True);
                Assert.That(panel.position.xMax, Is.EqualTo(figure.position.x).Within(4)); Assert.That(panel.position.height, Is.EqualTo(figure.position.height).Within(4));
                Assert.That(settings.StartX, Is.EqualTo(.25)); Assert.That(settings.EndX, Is.EqualTo(1.75)); Assert.That(session.LeaseCount, Is.EqualTo(1));
                Rect left = panel.position, right = figure.position; pair.Invoke(null, new object[] { panel, figure }); yield return null;
                Assert.That(panel.position, Is.EqualTo(left)); Assert.That(figure.position, Is.EqualTo(right));
                // Use the same tab-transfer operations as Unity's native drag-out path.
                var host = typeof(EditorWindow).GetField("m_Parent", Flags).GetValue(panel);
                host.GetType().GetMethod("RemoveTab", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                    new[] { typeof(EditorWindow), typeof(bool), typeof(bool) }, null).Invoke(host, new object[] { panel, true, false });
                typeof(EditorWindow).GetMethod("CreateNewWindowForEditorWindow", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { panel, false, true, true });
                panel.position = new Rect(90, 90, 400, 700); yield return null;
                Assert.That((bool)paired.Invoke(null, new object[] { panel, figure }), Is.False);
                Assert.That(session.LeaseCount, Is.EqualTo(1));
                pair.Invoke(null, new object[] { panel, figure }); yield return null;
                Assert.That((bool)paired.Invoke(null, new object[] { panel, figure }), Is.True);
                panel.Close(); panel = null; Assert.That(session.IsDisposed, Is.False);
                figure.Close(); figure = null; Assert.That(session.IsDisposed, Is.True);
            } finally { if (panel != null) panel.Close(); if (figure != null) figure.Close(); Directory.Delete(root, true); }
        }
        [UnityTest] public IEnumerator SheetWorkspaceReusesFigureWithoutRefittingOrAddingLeases()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPWorkspaceOwner-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            var owner = ScriptableObject.CreateInstance<FPDataWorkbenchWindow>(); FPWorkbenchFigureWindow figure = null;
            try {
                string source = Path.Combine(root, "source.csv"); File.WriteAllText(source, "x,y\n0,2\n1,3\n2,5\n"); owner.ShowUtility();
                owner.GetType().GetMethod("AddCsv", Flags).Invoke(owner, new object[] { source });
                var sheet = ((IList)owner.GetType().GetField("sheets", Flags).GetValue(owner))[0]; var st = sheet.GetType(); var deadline = DateTime.UtcNow.AddSeconds(15);
                while (st.GetField("Dataset").GetValue(sheet) == null && DateTime.UtcNow < deadline) yield return null;
                Assert.That(st.GetField("Dataset").GetValue(sheet), Is.Not.Null);
                var binding = (FPWorkbenchSeriesBinding)st.GetField("Binding").GetValue(sheet); binding.XMode = FPWorkbenchXMode.Numeric; binding.XColumn = 0; st.GetField("PlotColumn").SetValue(sheet, 1);
                var settings = (FPWorkbenchPlotSettings)st.GetField("Plot").GetValue(sheet); settings.HasRange = true; settings.StartX = .25; settings.EndX = 1.75;
                var get = owner.GetType().GetMethod("GetOrCreatePlotFigure", Flags);
                figure = (FPWorkbenchFigureWindow)get.Invoke(owner, null); Assert.That(figure, Is.Not.Null);
                Assert.That(get.Invoke(owner, null), Is.SameAs(figure));
                var session = ((FPWorkbenchFigureSession.Lease)st.GetField("FigureLease").GetValue(sheet)).Session;
                Assert.That(session.LeaseCount, Is.EqualTo(2)); Assert.That(settings.StartX, Is.EqualTo(.25)); Assert.That(settings.EndX, Is.EqualTo(1.75));
            } finally { if (figure != null) figure.Close(); owner.Close(); Directory.Delete(root, true); }
        }
    }
}
