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

    public sealed class FPWorkbenchCalculatedGridTests
    {
        [UnityTest] public IEnumerator CalculatedGridPublishesRevisionsAndSupportsDefinitionUndo()
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            string root = Path.Combine(Path.GetTempPath(), "FPCalculatedGrid-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            string source = Path.Combine(root, "source.csv"); var csv = new StringBuilder("x,y\n"); for (int i = 0; i < 100; i++) csv.Append(i).Append(',').Append(i * 2).Append('\n'); File.WriteAllText(source, csv.ToString());
            var window = ScriptableObject.CreateInstance<FPDataWorkbenchWindow>(); var type = window.GetType(); string cache = null;
            FPWorkbenchTablePage Page() => (FPWorkbenchTablePage)type.GetField("virtualPage", flags).GetValue(window);
            try
            {
                window.ShowUtility(); window.position = new Rect(70, 70, 1100, 750); type.GetMethod("AddCsv", flags).Invoke(window, new object[] { source });
                object sheet = ((IList)type.GetField("sheets", flags).GetValue(window))[0]; var sheetType = sheet.GetType(); var deadline = DateTime.UtcNow.AddSeconds(10);
                while (sheetType.GetField("Dataset").GetValue(sheet) == null && DateTime.UtcNow < deadline) yield return null;
                var data = (FPWorkbenchCsvDataset)sheetType.GetField("Dataset").GetValue(sheet); Assert.That(data, Is.Not.Null); cache = data.CacheDirectory;
                sheetType.GetField("FirstRow").SetValue(sheet, 50L); var layout = (FPWorkbenchColumnLayout)sheetType.GetField("Columns").GetValue(sheet); layout.ManualWidths[0] = 155;
                var definitions = new FPWorkbenchFormulaSet { DatasetId = data.DatasetId }; definitions.Columns.Add(new FPWorkbenchDerivedColumn { Name = "Sum", Expression = "C1 + C2" });
                Assert.That(type.GetMethod("ApplyCalculations", flags).Invoke(window, new object[] { sheet, definitions }), Is.True);
                while ((Page() == null || Page().FirstRow != 50 || Page().Values[0].Length != 3) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(Page(), Is.Not.Null); Assert.That(Page().Values[0][2].Text, Is.EqualTo("150")); Assert.That(layout.ManualWidths[0], Is.EqualTo(155));
                Assert.That(Page().Values[0][2].Calculated, Is.True);
                Undo.PerformUndo(); yield return null;
                while (sheetType.GetField("Table").GetValue(sheet) != null && DateTime.UtcNow < deadline) yield return null;
                Assert.That(sheetType.GetField("Table").GetValue(sheet), Is.Null); Assert.That((long)sheetType.GetField("FirstRow").GetValue(sheet), Is.EqualTo(50));
                Undo.PerformRedo();
                while ((Page() == null || Page().Values[0].Length != 3) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(Page().Values[0][2].Text, Is.EqualTo("150"));
                type.GetMethod("ApplyCorrection", flags).Invoke(window, new object[] { sheet, 50L, 1, "100", "200", FPWorkbenchEditKind.Number, "fixture", false });
                while ((Page() == null || Page().Values[0][2].Text != "250") && DateTime.UtcNow < deadline) yield return null;
                Assert.That(Page().Values[0][2].Text, Is.EqualTo("250")); Assert.That(Page().Values[0][1].Original, Is.EqualTo("100"));
                sheetType.GetField("ShowOriginal").SetValue(sheet, true);
                while ((Page() == null || Page().Values[0][2].Text != "150") && DateTime.UtcNow < deadline) yield return null;
                Assert.That(Page().Values[0][2].Text, Is.EqualTo("150"));
                var document = (FPWorkbenchDocument)type.GetMethod("CaptureWorkbook", flags).Invoke(window, null);
                Assert.That(document.Sources[0].HasCalculations, Is.True); Assert.That(document.Sheets[0].Columns.ManualWidths.Length, Is.EqualTo(3));
                string path = Path.Combine(root, "calculated.fpworkbook"); var saving = FPWorkbenchDocumentFile.SaveAsync(document, path); while (!saving.IsCompleted) yield return null;
                var loading = FPWorkbenchDocumentFile.LoadAsync(path); while (!loading.IsCompleted) yield return null; Assert.That(loading.IsFaulted, Is.False);
                Assert.That(File.ReadAllText(source), Is.EqualTo(csv.ToString()));
            }
            finally { window.Close(); UnityEngine.Object.DestroyImmediate(window); if (cache != null && Directory.Exists(cache)) Directory.Delete(cache, true); Directory.Delete(root, true); }
        }
    }
}
