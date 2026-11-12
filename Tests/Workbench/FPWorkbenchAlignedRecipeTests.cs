// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Tests
{
    using System;
    using System.IO;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;

    public sealed class FPWorkbenchAlignedRecipeTests
    {
        [Test] public async Task TypedPhysicalValuesRetainIndependentSchemasWhenProductsAreRegenerated()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPTypedRecipe-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try {
                string a = Path.Combine(root, "a.csv"), b = Path.Combine(root, "b.csv");
                File.WriteAllText(a, "key,x\n1,\"1,25\"\n2,\"2,5\"\n"); File.WriteAllText(b, "key,y\n1,3.5\n2,---\n");
                var left = await FPWorkbenchCsvDataset.ImportAsync(a, Path.Combine(root, "a-cache"), new FPWorkbenchCsvProfile());
                var right = await FPWorkbenchCsvDataset.ImportAsync(b, Path.Combine(root, "b-cache"), new FPWorkbenchCsvProfile());
                FPWorkbenchTypedSchema Schema(FPWorkbenchCsvDataset data, string culture) => new FPWorkbenchTypedSchema { DatasetId = data.DatasetId, Columns = new[] {
                    new FPWorkbenchColumnDeclaration { Column = 1, ColumnId = data.Cell(0, 1).ColumnId, Type = FPWorkbenchColumnType.Real, Culture = culture, MissingTokens = new[] { "---" } } } };
                var ls = Schema(left, "de-DE"); var rs = Schema(right, "");
                var lp = FPWorkbenchFormulaProgram.Compile(left, new FPWorkbenchFormulaSet { DatasetId = left.DatasetId }, ls.Compile(left));
                var rp = FPWorkbenchFormulaProgram.Compile(right, new FPWorkbenchFormulaSet { DatasetId = right.DatasetId }, rs.Compile(right));
                var mapping = await FPWorkbenchAlignment.BuildAsync(left, right, new FPWorkbenchAlignmentOptions { KeyDomain = "ID", KeyKind = FPWorkbenchAlignmentKeyKind.Decimal });
                var values = await FPWorkbenchAlignedValues.BuildAsync(mapping, 1, 1, lp, rp, FPWorkbenchExportInvalidPolicy.EmptyField);
                var page = await values.Dataset.ReadPageAsync(0, 2, 0, 4);
                Assert.That(page.Values[0], Is.EqualTo(new[] { "0", "0", "1.25", "3.5" }));
                Assert.That(page.Values[1], Is.EqualTo(new[] { "1", "1", "2.5", "" }));
                var recipe = FPWorkbenchAlignedFigureRecipe.FromValues(values.Metadata);
                string json = JsonUtility.ToJson(recipe); ls.Columns[0].Culture = "en-US";
                string product = Path.GetDirectoryName(values.Dataset.SourcePath), index = values.Dataset.CacheDirectory;
                Directory.Delete(index, true); Directory.Delete(product, true); Directory.Delete(mapping.DirectoryPath, true);
                var restored = JsonUtility.FromJson<FPWorkbenchAlignedFigureRecipe>(json);
                var rebuilt = await restored.BuildAsync(left, right);
                Assert.That((await rebuilt.Dataset.ReadPageAsync(0, 2, 0, 4)).Values[0], Is.EqualTo(page.Values[0]));
                Assert.That(rebuilt.Metadata.LeftFormulaFingerprint, Is.EqualTo(lp.Fingerprint));
                Assert.That(rebuilt.Metadata.RightFormulaFingerprint, Is.EqualTo(rp.Fingerprint));
                Assert.That(restored.LeftCalculations.TypedSchema.Columns[0].Culture, Is.EqualTo("de-DE"));
            }
            finally { Directory.Delete(root, true); }
        }
        [Test] public async Task WorkbookRecipeRegeneratesDeletedProductsFromFrozenOriginalInputs()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPRecipe-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try
            {
                string a = Path.Combine(root, "a.csv"), b = Path.Combine(root, "b.csv"); File.WriteAllText(a, "key,x\n1,10\n3,30\n5,50\n"); File.WriteAllText(b, "key,y\n0,100\n3,300\n5,500\n");
                var left = await FPWorkbenchCsvDataset.ImportAsync(a, Path.Combine(root, "a-cache"), new FPWorkbenchCsvProfile());
                var right = await FPWorkbenchCsvDataset.ImportAsync(b, Path.Combine(root, "b-cache"), new FPWorkbenchCsvProfile());
                var changes = new FPWorkbenchChangeSet { DatasetId = right.DatasetId }; changes.Set(right.Cell(1, 1), "300", "900", FPWorkbenchEditKind.Number, "frozen recipe");
                var options = new FPWorkbenchAlignmentOptions { KeyDomain = "ID", KeyKind = FPWorkbenchAlignmentKeyKind.Decimal };
                var mapping = await FPWorkbenchAlignment.BuildAsync(left, right, options, rightChanges: changes.Snapshot());
                var defs = new FPWorkbenchFormulaSet { DatasetId = left.DatasetId }; defs.Columns.Add(new FPWorkbenchDerivedColumn { Name = "X doubled", Expression = "C2 * 2" });
                var program = FPWorkbenchFormulaProgram.Compile(left, defs); var values = await FPWorkbenchAlignedValues.BuildAsync(mapping, 2, 1, program);
                var recipe = FPWorkbenchAlignedFigureRecipe.FromValues(values.Metadata);
                var document = new FPWorkbenchDocument(); document.Sources.Add(values.LeftReference); document.Sources.Add(values.RightReference);
                var figure = new FPWorkbenchFigureDocument { Kind = FPWorkbenchFigureKind.AlignedScatter, DatasetId = left.DatasetId, AlignedRecipe = recipe,
                    Binding = new FPWorkbenchSeriesBinding { XMode = FPWorkbenchXMode.Numeric }, Colors = new[] { Color.cyan }, PointBudget = 321 };
                figure.Plot.StartX = 65; figure.Plot.EndX = 95; figure.Plot.MinimumY = 400; figure.Plot.MaximumY = 1000; figure.Plot.HasRange = true;
                document.Figures.Add(figure); string path = Path.Combine(root, "aligned.fpworkbook"); await FPWorkbenchDocumentFile.SaveAsync(document, path);
                string product = Path.GetDirectoryName(values.Dataset.SourcePath), index = values.Dataset.CacheDirectory;
                Assert.That(product, Does.StartWith(left.CacheDirectory + Path.DirectorySeparatorChar)); Assert.That(index, Does.StartWith(left.CacheDirectory + Path.DirectorySeparatorChar));
                Directory.Delete(index, true); Directory.Delete(product, true); Directory.Delete(mapping.DirectoryPath, true);
                changes.Set(right.Cell(1, 1), "300", "999", FPWorkbenchEditKind.Number, "later live correction");
                var loaded = await FPWorkbenchDocumentFile.LoadAsync(path); Assert.That(loaded.Document.Sources.Count, Is.EqualTo(2)); Assert.That(loaded.Document.Sheets, Is.Empty);
                Assert.That(loaded.Document.Sources[0].HasCalculations, Is.False, "Recipe calculations do not depend on current source definitions.");
                var saved = loaded.Document.Figures[0]; Assert.That(saved.Kind, Is.EqualTo(FPWorkbenchFigureKind.AlignedScatter)); Assert.That(saved.Plot.StartX, Is.EqualTo(65)); Assert.That(saved.PointBudget, Is.EqualTo(321));
                var rebuilt = await saved.AlignedRecipe.BuildAsync(loaded.SourceDatasets[0], loaded.SourceDatasets[1]);
                Assert.That((await rebuilt.Dataset.ReadPageAsync(0, 1, 0, 4)).Values[0], Is.EqualTo(new[] { "1", "1", "60", "900" }));
                Assert.That(File.Exists(rebuilt.Dataset.SourcePath), Is.True);
                document.Sources.RemoveAt(1); Assert.Throws<InvalidDataException>(() => document.Validate()); document.Sources.Add(values.RightReference);
                recipe.RightValueColumnId = right.DatasetId + ":column:3000"; await FPWorkbenchDocumentFile.SaveAsync(document, path);
                try { await FPWorkbenchDocumentFile.LoadAsync(path); Assert.Fail("Actual source schema must reject a generically bounded but absent value column."); } catch (InvalidDataException) { }
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
