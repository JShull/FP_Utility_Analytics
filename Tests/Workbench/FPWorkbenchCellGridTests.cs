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
    public sealed class FPWorkbenchCellGridTests
    {
        [UnityTest] public IEnumerator SheetGridSettingsPersistIndependentlyThroughWorkbookSaveOpen()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPGrid-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            var window = ScriptableObject.CreateInstance<FPDataWorkbenchWindow>(); var type = window.GetType(); const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            IList Sheets() => (IList)type.GetField("sheets", flags).GetValue(window);
            try {
                string csv = Path.Combine(root, "source.csv"); File.WriteAllText(csv, "x,y\n1,2\n3,4\n5,6\n");
                window.ShowUtility(); window.position = new Rect(40, 40, 1000, 800); var deadline = DateTime.UtcNow.AddSeconds(12);
                for (int i = 0; i < 2; i++) {
                    type.GetMethod("AddCsv", flags).Invoke(window, new object[] { csv });
                    while (Sheets()[i].GetType().GetField("Dataset").GetValue(Sheets()[i]) == null && DateTime.UtcNow < deadline) yield return null;
                    Assert.That(Sheets()[i].GetType().GetField("Dataset").GetValue(Sheets()[i]), Is.Not.Null);
                }
                object first = Sheets()[0], second = Sheets()[1]; var st = first.GetType();
                st.GetField("CellGrid").SetValue(first, true); st.GetField("CellGridOpacity").SetValue(first, .42f);
                st.GetField("CellGrid").SetValue(second, false); st.GetField("CellGridOpacity").SetValue(second, .8f);
                type.GetField("active", flags).SetValue(window, 0);
                window.Repaint(); yield return null; yield return null;
                var document = (FPWorkbenchDocument)type.GetMethod("CaptureWorkbook", flags).Invoke(window, null);
                string path = Path.Combine(root, "grid.fpworkbook"); var saving = FPWorkbenchDocumentFile.SaveAsync(document, path); while (!saving.IsCompleted) yield return null; saving.GetAwaiter().GetResult(); var bytes = File.ReadAllBytes(path);
                type.GetMethod("OpenWorkbook", flags).Invoke(window, new object[] { path });
                while ((type.GetField("workbookLoad", flags).GetValue(window) != null || type.GetField("pendingWorkbookLoad", flags).GetValue(window) != null) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(type.GetField("error", flags).GetValue(window), Is.Null);
                Assert.That(st.GetField("CellGrid").GetValue(Sheets()[0]), Is.EqualTo(true)); Assert.That((float)st.GetField("CellGridOpacity").GetValue(Sheets()[0]), Is.EqualTo(.42f));
                Assert.That(st.GetField("CellGrid").GetValue(Sheets()[1]), Is.EqualTo(false)); Assert.That((float)st.GetField("CellGridOpacity").GetValue(Sheets()[1]), Is.EqualTo(.8f));
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes));
                var legacy = JsonUtility.FromJson<FPWorkbenchSheetDocument>("{}"); Assert.That(legacy.CellGrid, Is.False); Assert.That(legacy.CellGridOpacity, Is.EqualTo(.35f));
                document.Sheets[0].CellGridOpacity = float.NaN; Assert.Throws<InvalidDataException>(() => document.Validate());
                document.Sheets[0].CellGridOpacity = 1.1f; Assert.Throws<InvalidDataException>(() => document.Validate());
                document.Sheets[0].CellGridOpacity = 0; Assert.DoesNotThrow(() => document.Validate());
            } finally { window.Close(); Directory.Delete(root, true); }
        }
    }
}
