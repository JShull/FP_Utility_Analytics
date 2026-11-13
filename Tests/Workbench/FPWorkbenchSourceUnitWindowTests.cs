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
        [UnityTest] public IEnumerator HeaderUnitSuggestionsNeedExplicitApplyAndCustomDeclarationsRemainSupported()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPUnitPreset-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            var window = ScriptableObject.CreateInstance<FPDataWorkbenchWindow>(); var type = window.GetType(); const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            try {
                string path = Path.Combine(root, "source.csv"), original = "Temperature [\u00b0C],Acceleration [m/s\u00b2]\n12,1\n13,2\n"; File.WriteAllText(path, original);
                window.ShowUtility(); window.position = new Rect(50, 50, 1100, 800); type.GetMethod("AddCsv", flags).Invoke(window, new object[] { path });
                var sheet = ((IList)type.GetField("sheets", flags).GetValue(window))[0]; var st = sheet.GetType(); var deadline = DateTime.UtcNow.AddSeconds(10);
                while (st.GetField("Dataset").GetValue(sheet) == null && DateTime.UtcNow < deadline) yield return null;
                var data = (FPWorkbenchCsvDataset)st.GetField("Dataset").GetValue(sheet); Assert.That(data, Is.Not.Null);
                type.GetField("showSourceUnits", flags).SetValue(window, true); window.Repaint(); yield return null; yield return null;
                Assert.That(type.GetField("unitDraft", flags).GetValue(window), Is.EqualTo("degC")); Assert.That(type.GetField("unitCustomOverride", flags).GetValue(window), Is.EqualTo(false));
                Assert.That(type.GetMethod("ColumnUnitLabel", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { sheet, 0 }), Is.EqualTo("unspecified"), "Header suggestions must not silently declare units.");
                var symbols = (string[])type.GetField("UnitSymbols", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                var labels = (string[])type.GetField("UnitLabels", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null); Assert.That(labels.Length, Is.EqualTo(symbols.Length));
                foreach (string symbol in symbols) { var definition = new FPWorkbenchFormulaSet { DatasetId = data.DatasetId }; definition.SourceUnits.Add(new FPWorkbenchSourceUnit { Column = 0, Units = symbol }); Assert.DoesNotThrow(() => FPWorkbenchFormulaProgram.Compile(data, definition)); }
                Assert.That(type.GetMethod("ApplySourceUnit", flags).Invoke(window, new object[] { sheet, 0, "degC" }), Is.EqualTo(true));
                yield return null; yield return null;
                Assert.That(((FPWorkbenchFormulaProgram)st.GetField("TableProgram").GetValue(sheet)).SourceUnits(0), Is.EqualTo("degC"));
                type.GetField("unitColumn", flags).SetValue(window, 1); window.Repaint(); yield return null; yield return null;
                Assert.That(type.GetField("unitDraft", flags).GetValue(window), Is.EqualTo("m/s^2"));
                type.GetField("unitCustomOverride", flags).SetValue(window, true); type.GetField("unitDraft", flags).SetValue(window, "furlong/s");
                Assert.That(type.GetMethod("ApplySourceUnit", flags).Invoke(window, new object[] { sheet, 1, "furlong/s" }), Is.EqualTo(true));
                yield return null; yield return null;
                Assert.That(((FPWorkbenchFormulaProgram)st.GetField("TableProgram").GetValue(sheet)).SourceUnits(1), Is.EqualTo("furlong*s^-1"));
                Assert.That(File.ReadAllText(path), Is.EqualTo(original));
            } finally { window.Close(); Directory.Delete(root, true); }
        }
        [UnityTest] public IEnumerator TypedDeclarationAuthoringValidatesAtomicallyAndSupportsUndoClearAndPersistence()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPTypeAuthor-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            var window = ScriptableObject.CreateInstance<FPDataWorkbenchWindow>(); string cache = null;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic; var type = window.GetType();
            try {
                string path = Path.Combine(root, "source.csv"), original = "x,y\n1,2\n3,4\n"; File.WriteAllText(path, original);
                window.ShowUtility(); window.position = new Rect(50, 50, 1100, 750); type.GetMethod("AddCsv", flags).Invoke(window, new object[] { path });
                object sheet = ((IList)type.GetField("sheets", flags).GetValue(window))[0]; var st = sheet.GetType(); var deadline = DateTime.UtcNow.AddSeconds(10);
                while (st.GetField("Dataset").GetValue(sheet) == null && DateTime.UtcNow < deadline) yield return null;
                var data = (FPWorkbenchCsvDataset)st.GetField("Dataset").GetValue(sheet); Assert.That(data, Is.Not.Null); cache = data.CacheDirectory;
                var sources = (IList)type.GetField("registeredSources", flags).GetValue(window); FPWorkbenchSourceDocument Source() => (FPWorkbenchSourceDocument)sources[0];
                bool Apply(FPWorkbenchColumnDeclaration declaration) => (bool)type.GetMethod("ApplyTypedDeclaration", flags).Invoke(window, new object[] { sheet, 1, declaration });
                var correction = type.GetMethod("ApplyCorrection", flags);
                Assert.That(correction.Invoke(window, new object[] { sheet, 1L, 1, "4", "bad", FPWorkbenchEditKind.Text, "fixture", false }), Is.EqualTo(true));
                var declaration = new FPWorkbenchColumnDeclaration { Type = FPWorkbenchColumnType.Real, Units = "m", MissingTokens = new[] { "---" } };
                Assert.That(Apply(declaration), Is.False); Assert.That(Source().HasTypedSchema, Is.False);
                Assert.That((string)type.GetField("error", flags).GetValue(window), Does.Contain("conflicts"));
                correction.Invoke(window, new object[] { sheet, 1L, 1, "4", "", FPWorkbenchEditKind.Text, "revert", true });
                st.GetField("FirstRow").SetValue(sheet, 1L);
                Assert.That(Apply(declaration), Is.True); string fingerprint = Source().TypedSchema.Compile(data).Fingerprint;
                declaration.Units = "later mutation";
                Assert.That(Source().TypedSchema.Columns[0].Units, Is.EqualTo("m"));
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); Assert.That(Source().HasTypedSchema, Is.False);
                Undo.PerformRedo(); Assert.That(Source().TypedSchema.Compile(data).Fingerprint, Is.EqualTo(fingerprint));
                type.GetMethod("EnsureTableSchema", flags).Invoke(window, new[] { sheet });
                Assert.That(((FPWorkbenchFormulaProgram)st.GetField("TableProgram").GetValue(sheet)).TypedSchemaFingerprint, Is.EqualTo(fingerprint));
                var definitions = new FPWorkbenchFormulaSet { DatasetId = data.DatasetId };
                definitions.SourceUnits.Add(new FPWorkbenchSourceUnit { Column = 0, Units = "m" });
                definitions.Columns.Add(new FPWorkbenchDerivedColumn { Expression = "C1 + C2" });
                Assert.That(type.GetMethod("ApplyCalculations", flags).Invoke(window, new object[] { sheet, definitions }), Is.EqualTo(true));
                var incompatible = new FPWorkbenchColumnDeclaration { Type = FPWorkbenchColumnType.Real, Units = "s" };
                Assert.That(Apply(incompatible), Is.False);
                Assert.That(Source().TypedSchema.Compile(data).Fingerprint, Is.EqualTo(fingerprint));
                Assert.That(FPWorkbenchFormulaProgram.Compile(data, Source().Calculations).Units(0), Is.EqualTo("m"));
                type.GetField("showColumnTypes", flags).SetValue(window, true); window.Repaint(); yield return null;
                Assert.That(Apply(null), Is.True); Assert.That(Source().HasTypedSchema, Is.False);
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); Assert.That(Source().TypedSchema.Compile(data).Fingerprint, Is.EqualTo(fingerprint));
                Assert.That(Source().Calculations.TypedSchema.Compile(data).Fingerprint, Is.EqualTo(fingerprint));
                Assert.That(st.GetField("FirstRow").GetValue(sheet), Is.EqualTo(1L));
                var document = (FPWorkbenchDocument)type.GetMethod("CaptureWorkbook", flags).Invoke(window, null);
                string output = Path.Combine(root, "typed.fpworkbook"); var save = FPWorkbenchDocumentFile.SaveAsync(document, output); while (!save.IsCompleted) yield return null; save.GetAwaiter().GetResult();
                var load = FPWorkbenchDocumentFile.LoadAsync(output); while (!load.IsCompleted) yield return null;
                Assert.That(load.GetAwaiter().GetResult().Document.Sources[0].TypedSchema.Compile(data).Fingerprint, Is.EqualTo(fingerprint));
                Assert.That(File.ReadAllText(path), Is.EqualTo(original));
            }
            finally {
                window.Close(); UnityEngine.Object.DestroyImmediate(window);
                if (cache != null && Directory.Exists(cache)) Directory.Delete(cache, true);
                Directory.Delete(root, true);
            }
        }
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
                Source().HasTypedSchema = true;
                Source().TypedSchema = new FPWorkbenchTypedSchema { DatasetId = data.DatasetId, Columns = new[] {
                    new FPWorkbenchColumnDeclaration { Column = 1, ColumnId = data.Cell(0, 1).ColumnId, Type = FPWorkbenchColumnType.Real, Units = "m", MissingTokens = new[] { "---" } } } };
                string typedFingerprint = Source().TypedSchema.Compile(data).Fingerprint;
                type.GetMethod("RememberSource", flags).Invoke(window, new object[] { data, "Recalled source" });
                Assert.That(Source().HasTypedSchema, Is.True); Assert.That(Source().TypedSchema.Compile(data).Fingerprint, Is.EqualTo(typedFingerprint));
                deadline = DateTime.UtcNow.AddSeconds(10);
                FPWorkbenchTablePage typedPage = null;
                while (DateTime.UtcNow < deadline) {
                    typedPage = (FPWorkbenchTablePage)type.GetField("virtualPage", flags).GetValue(window);
                    if (typedPage != null && typedPage.TypedSchemaFingerprint == typedFingerprint) break;
                    yield return null;
                }
                Assert.That(typedPage, Is.Not.Null); Assert.That(typedPage.TypedSchemaFingerprint, Is.EqualTo(typedFingerprint));
                var typedProgram = (FPWorkbenchFormulaProgram)st.GetField("TableProgram").GetValue(sheet);
                Assert.That(typedProgram.TypedSchemaFingerprint, Is.EqualTo(typedFingerprint));
                var frozenProgram = FPWorkbenchFormulaProgram.Compile(data, typedProgram.Definition);
                Assert.That(frozenProgram.TypedSchemaFingerprint, Is.EqualTo(typedFingerprint));
                var typedEvaluator = typedProgram.CreateEvaluator(); typedEvaluator.SetRow(new[] { "3", "---" }, 1);
                Assert.That(typedEvaluator.Derived(0).Status, Is.EqualTo(FPWorkbenchFormulaStatus.Missing));
                int typedIndex = Array.IndexOf(typedPage.Columns, 1);
                Assert.That(typedPage.Values[0][typedIndex].Typed.Effective.RealNumber, Is.EqualTo(4));
                var correction = type.GetMethod("ApplyCorrection", flags);
                Assert.That(Assert.Throws<TargetInvocationException>(() => correction.Invoke(window, new object[] { sheet, 1L, 1, "4", "bad", FPWorkbenchEditKind.Text, "reject", false })).InnerException, Is.TypeOf<InvalidDataException>());
                Assert.That(((IList)type.GetField("corrections", flags).GetValue(window)).Count, Is.EqualTo(0));
                Assert.That(correction.Invoke(window, new object[] { sheet, 1L, 1, "4", "6", FPWorkbenchEditKind.Text, "typed correction", false }), Is.EqualTo(true));
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); Assert.That(((IList)type.GetField("corrections", flags).GetValue(window)).Count, Is.EqualTo(0));
                Undo.PerformRedo(); Assert.That(((IList)type.GetField("corrections", flags).GetValue(window)).Count, Is.EqualTo(1));
                Assert.That(st.GetField("FirstRow").GetValue(sheet), Is.EqualTo(1L)); Assert.That(plot.StartX, Is.EqualTo(10));
                var document = (FPWorkbenchDocument)type.GetMethod("CaptureWorkbook", flags).Invoke(window, null);
                string target = Path.Combine(root, "units.fpworkbook"); var save = FPWorkbenchDocumentFile.SaveAsync(document, target); while (!save.IsCompleted) yield return null;
                Assert.That(save.IsFaulted, Is.False); var loading = FPWorkbenchDocumentFile.LoadAsync(target); while (!loading.IsCompleted) yield return null;
                Assert.That(loading.IsFaulted, Is.False);
                Assert.That(FPWorkbenchFormulaProgram.Compile(loading.Result.SourceDatasets[0], loading.Result.Document.Sources[0].Calculations).SourceUnits(0), Is.EqualTo("m"));
                Assert.That(loading.Result.Document.Sources[0].TypedSchema.Compile(loading.Result.SourceDatasets[0]).Fingerprint, Is.EqualTo(typedFingerprint));
                Source().HasCalculations = false; Source().Calculations = null;
                type.GetMethod("EnsureTableSchema", flags).Invoke(window, new[] { sheet });
                var physicalOnly = (FPWorkbenchFormulaProgram)st.GetField("TableProgram").GetValue(sheet);
                Assert.That(physicalOnly, Is.Not.Null); Assert.That(physicalOnly.ColumnCount, Is.Zero);
                Assert.That(physicalOnly.TypedSchemaFingerprint, Is.EqualTo(typedFingerprint));
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
