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

    public sealed class FPWorkbenchSourceUnitWindowTests
    {
        [UnityTest] public IEnumerator SourceDeclarationsValidateDependenciesUndoAndPersistWithoutConvertingValues()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPSourceUnits-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            var window = ScriptableObject.CreateInstance<FPDataWorkbenchWindow>(); string cache = null;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic; Type type = window.GetType();
            try
            {
                string path = Path.Combine(root, "source.csv"), original = "x,y\n1,2\n3,4\n"; File.WriteAllText(path, original);
                window.ShowUtility(); window.position = new Rect(50, 50, 1100, 750); type.GetMethod("AddCsv", flags).Invoke(window, new object[] { path });
                object sheet = ((IList)type.GetField("sheets", flags).GetValue(window))[0]; Type st = sheet.GetType(); var deadline = DateTime.UtcNow.AddSeconds(10);
                while (st.GetField("Dataset").GetValue(sheet) == null && DateTime.UtcNow < deadline) yield return null;
                var data = (FPWorkbenchCsvDataset)st.GetField("Dataset").GetValue(sheet); Assert.That(data, Is.Not.Null); cache = data.CacheDirectory;
                bool Unit(int c, string value) => (bool)type.GetMethod("ApplySourceUnit", flags).Invoke(window, new object[] { sheet, c, value });
                Assert.That(Unit(0, "m"), Is.True); Assert.That(Unit(1, "m"), Is.True);
                var sources = (IList)type.GetField("registeredSources", flags).GetValue(window); FPWorkbenchSourceDocument Source() => (FPWorkbenchSourceDocument)sources[0];
                var definitions = JsonUtility.FromJson<FPWorkbenchFormulaSet>(JsonUtility.ToJson(Source().Calculations));
                definitions.Columns.Add(new FPWorkbenchDerivedColumn { Name = "Sum", Expression = "C1 + C2" });
                Assert.That(type.GetMethod("ApplyCalculations", flags).Invoke(window, new object[] { sheet, definitions }), Is.True);
                type.GetField("showSourceUnits", flags).SetValue(window, true); window.Repaint(); yield return null;
                var plot = (FPWorkbenchPlotSettings)st.GetField("Plot").GetValue(sheet); plot.StartX = 10; plot.EndX = 20;
                st.GetField("FirstRow").SetValue(sheet, 1L);
                Assert.That(Unit(1, "s"), Is.False); Assert.That(Source().Calculations.SourceUnits.Find(u => u.Column == 1).Units, Is.EqualTo("m"));
                Assert.That((string)type.GetField("error", flags).GetValue(window), Does.Contain("compatible"));
                Assert.That(Unit(1, ""), Is.True); Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); yield return null;
                var restored = FPWorkbenchFormulaProgram.Compile(data, Source().Calculations);
                Assert.That(restored.SourceUnits(1), Is.EqualTo("m")); Assert.That(restored.Units(0), Is.EqualTo("m"));
                Assert.That((long)st.GetField("FirstRow").GetValue(sheet), Is.EqualTo(1)); Assert.That(plot.StartX, Is.EqualTo(10));
                Undo.PerformRedo(); yield return null;
                Assert.That(FPWorkbenchFormulaProgram.Compile(data, Source().Calculations).SourceUnits(1), Is.EqualTo("unspecified"));
                Assert.That(Unit(1, "m"), Is.True); Assert.That(Unit(0, "m??"), Is.False);
                var document = (FPWorkbenchDocument)type.GetMethod("CaptureWorkbook", flags).Invoke(window, null);
                string target = Path.Combine(root, "units.fpworkbook"); var save = FPWorkbenchDocumentFile.SaveAsync(document, target); while (!save.IsCompleted) yield return null;
                Assert.That(save.IsFaulted, Is.False); var loading = FPWorkbenchDocumentFile.LoadAsync(target); while (!loading.IsCompleted) yield return null;
                Assert.That(loading.IsFaulted, Is.False);
                Assert.That(FPWorkbenchFormulaProgram.Compile(loading.Result.SourceDatasets[0], loading.Result.Document.Sources[0].Calculations).SourceUnits(0), Is.EqualTo("m"));
                Assert.That(File.ReadAllText(path), Is.EqualTo(original));
            }
            finally
            {
                window.Close(); UnityEngine.Object.DestroyImmediate(window);
                if (cache != null && Directory.Exists(cache)) Directory.Delete(cache, true);
                Directory.Delete(root, true);
            }
        }
    }
}
