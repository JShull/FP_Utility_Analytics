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

    public sealed class FPWorkbenchLinkedSelectionTests
    {
        [UnityTest] public IEnumerator RevealingSourceRowReopensSheetAndPreservesPlotAndLayer()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPLinked-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            var window = ScriptableObject.CreateInstance<FPDataWorkbenchWindow>(); string cache = null;
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance; Type type = window.GetType();
            try
            {
                string path = Path.Combine(root, "source.csv"); File.WriteAllText(path, "x,y,z\n2,3,4\n1,2,3\n0,1,2\n");
                window.ShowUtility(); window.position = new Rect(50, 50, 1000, 700); type.GetMethod("AddCsv", flags).Invoke(window, new object[] { path });
                var sheets = (IList)type.GetField("sheets", flags).GetValue(window); object sheet = sheets[0]; var st = sheet.GetType();
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (st.GetField("Dataset").GetValue(sheet) == null && DateTime.UtcNow < deadline) yield return null;
                var data = (FPWorkbenchCsvDataset)st.GetField("Dataset").GetValue(sheet); Assert.That(data, Is.Not.Null); cache = data.CacheDirectory;
                st.GetField("ShowOriginal").SetValue(sheet, true);
                var plot = (FPWorkbenchPlotSettings)st.GetField("Plot").GetValue(sheet); plot.StartX = .25; plot.EndX = 1.5;
                string owner = (string)type.GetField("workbookId", flags).GetValue(window);
                var reveal = type.GetMethod("RevealFigureRow", BindingFlags.NonPublic | BindingFlags.Static);
                Assert.That(reveal.Invoke(null, new object[] { owner, data.DatasetId, 1L, new[] { 0, 2 } }), Is.True);
                Assert.That((long)st.GetField("FirstRow").GetValue(sheet), Is.EqualTo(1));
                Assert.That((int[])type.GetField("linkedColumns", flags).GetValue(window), Is.EqualTo(new[] { 0, 2 }));
                Assert.That((bool)st.GetField("ShowOriginal").GetValue(sheet), Is.True);
                Assert.That(plot.StartX, Is.EqualTo(.25)); Assert.That(plot.EndX, Is.EqualTo(1.5));
                Assert.That(reveal.Invoke(null, new object[] { "foreign", data.DatasetId, 0L, new[] { 0 } }), Is.False);
                type.GetMethod("CloseCurrentSheet", flags).Invoke(window, null); Assert.That(sheets.Count, Is.Zero);
                ((IDictionary)type.GetField("admittedSources", flags).GetValue(window)).Clear();
                Assert.That(reveal.Invoke(null, new object[] { owner, data.DatasetId, 2L, new[] { 1 } }), Is.True);
                deadline = DateTime.UtcNow.AddSeconds(10);
                while ((sheets.Count == 0 || (long)type.GetField("linkedRow", flags).GetValue(window) != 2) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(sheets.Count, Is.EqualTo(1)); object reopened = sheets[0];
                Assert.That((long)reopened.GetType().GetField("FirstRow").GetValue(reopened), Is.EqualTo(2));
                Assert.That((int)reopened.GetType().GetField("FirstColumn").GetValue(reopened), Is.EqualTo(1));
                Assert.That((string)type.GetField("error", flags).GetValue(window), Is.Null);
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
