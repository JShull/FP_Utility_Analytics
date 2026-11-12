// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Tests
{
    using System;
    using System.IO;
    using System.Text;
    using System.Threading.Tasks;
    using NUnit.Framework;

    public sealed class FPWorkbenchFormulaTests
    {
        private string root;
        [SetUp] public void Setup() { root = Path.Combine(Path.GetTempPath(), "FPFormulaTest-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); }
        [TearDown] public void Teardown() { Directory.Delete(root, true); }
        private Task<FPWorkbenchCsvDataset> Import()
        {
            string source = Path.Combine(root, "source.csv"); File.WriteAllText(source, "a,b,c\n1,2,3\n", new UTF8Encoding(false));
            return FPWorkbenchCsvDataset.ImportAsync(source, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile());
        }
        [Test] public async Task TypedFormulaInputsPreserveCultureQualityAndIntegerPrecision()
        {
            var data = await Import();
            var schema = new FPWorkbenchTypedSchema { DatasetId = data.DatasetId, Columns = new[] {
                new FPWorkbenchColumnDeclaration { Column = 0, ColumnId = data.Cell(0, 0).ColumnId, Type = FPWorkbenchColumnType.Real, Culture = "de-DE", Units = "m", MissingTokens = new[] { "---" } },
                new FPWorkbenchColumnDeclaration { Column = 1, ColumnId = data.Cell(0, 1).ColumnId, Type = FPWorkbenchColumnType.UnsignedInteger },
                new FPWorkbenchColumnDeclaration { Column = 2, ColumnId = data.Cell(0, 2).ColumnId, Type = FPWorkbenchColumnType.Text } } };
            var set = new FPWorkbenchFormulaSet { DatasetId = data.DatasetId };
            set.Columns.Add(new FPWorkbenchDerivedColumn { Expression = "C1 * 2" });
            var program = FPWorkbenchFormulaProgram.Compile(data, set, schema.Compile(data));
            var evaluator = program.CreateEvaluator(); evaluator.SetRow(new[] { "1,25", "9007199254740993", "123" }, 0);
            Assert.That(evaluator.Derived(0).Number, Is.EqualTo(2.5));
            Assert.That(program.Units(0), Is.EqualTo("m"));
            Assert.That(evaluator.Source(1).Status, Is.EqualTo(FPWorkbenchFormulaStatus.IntegerPrecision));
            Assert.That(evaluator.Source(2).Status, Is.EqualTo(FPWorkbenchFormulaStatus.NonNumeric));
            evaluator.SetRow(new[] { "---", "9007199254740992", "123" }, 1);
            Assert.That(evaluator.Derived(0).Status, Is.EqualTo(FPWorkbenchFormulaStatus.Missing));
            Assert.That(evaluator.Source(1).IsValid, Is.True);
            var changes = new FPWorkbenchChangeSet { DatasetId = data.DatasetId };
            changes.Set(data.Cell(0, 0), "1", "", FPWorkbenchEditKind.Masked, "fixture");
            var masked = program.CreateEvaluator(changes.Snapshot()); masked.SetRow(new[] { "1", "2", "3" }, 0);
            Assert.That(masked.Derived(0).Status, Is.EqualTo(FPWorkbenchFormulaStatus.Missing));
            schema.Columns[0].Culture = "en-US";
            Assert.That(FPWorkbenchFormulaProgram.Compile(data, set, schema.Compile(data)).Fingerprint, Is.Not.EqualTo(program.Fingerprint));
            var restored = FPWorkbenchFormulaProgram.Compile(data, program.Definition);
            Assert.That(restored.Fingerprint, Is.EqualTo(program.Fingerprint));
            var copy = program.Definition; copy.TypedSchema.Columns[0].Culture = "en-US";
            Assert.That(program.Definition.TypedSchema.Columns[0].Culture, Is.EqualTo("de-DE"));
            set.SourceUnits.Add(new FPWorkbenchSourceUnit { Column = 0, Units = "s" });
            Assert.Throws<ArgumentException>(() => FPWorkbenchFormulaProgram.Compile(data, set, schema.Compile(data)));
        }
        [Test] public async Task TypedFormulaSnapshotsRejectContradictionsAndDoNotCoerceBooleanOrUtc()
        {
            var data = await Import();
            var schema = new FPWorkbenchTypedSchema { DatasetId = data.DatasetId, Columns = new[] {
                new FPWorkbenchColumnDeclaration { Column = 0, ColumnId = data.Cell(0, 0).ColumnId, Type = FPWorkbenchColumnType.Boolean },
                new FPWorkbenchColumnDeclaration { Column = 1, ColumnId = data.Cell(0, 1).ColumnId, Type = FPWorkbenchColumnType.UtcTimestamp } } };
            var set = new FPWorkbenchFormulaSet { DatasetId = data.DatasetId };
            var program = FPWorkbenchFormulaProgram.Compile(data, set, schema.Compile(data));
            var evaluator = program.CreateEvaluator(); evaluator.SetRow(new[] { "true", "2025-01-17 11:49:52.0000001", "3" }, 0);
            Assert.That(evaluator.Source(0).Status, Is.EqualTo(FPWorkbenchFormulaStatus.NonNumeric));
            Assert.That(evaluator.Source(1).Status, Is.EqualTo(FPWorkbenchFormulaStatus.NonNumeric));
            Assert.That(evaluator.Source(2).Number, Is.EqualTo(3));
            var definition = program.Definition; definition.HasTypedSchema = false;
            Assert.Throws<ArgumentException>(() => FPWorkbenchFormulaProgram.Compile(data, definition));
            definition.HasTypedSchema = true; definition.TypedSchema = null;
            Assert.Throws<ArgumentException>(() => FPWorkbenchFormulaProgram.Compile(data, definition));
            definition.TypedSchema = schema; definition.TypedSchema.Columns[0].ColumnId = "foreign";
            Assert.Throws<InvalidDataException>(() => FPWorkbenchFormulaProgram.Compile(data, definition));
            var legacy = FPWorkbenchFormulaProgram.Compile(data, set).CreateEvaluator();
            legacy.SetRow(new[] { "123", "2", "3" }, 0);
            Assert.That(legacy.Source(0).Number, Is.EqualTo(123));
        }
        [Test] public async Task ArithmeticPrecedenceDerivedDependenciesAndStableBindings()
        {
            var data = await Import(); var set = new FPWorkbenchFormulaSet { DatasetId = data.DatasetId };
            set.Columns.Add(new FPWorkbenchDerivedColumn { Name = "Sum", Expression = "C1 + C2" });
            set.Columns.Add(new FPWorkbenchDerivedColumn { Name = "Scaled", Expression = "-(D1 * 2) + C3 / 3" });
            var program = FPWorkbenchFormulaProgram.Compile(data, set); var evaluator = program.CreateEvaluator();
            evaluator.SetRow(new[] { "1", "2", "3" }, 0);
            Assert.That(evaluator.Derived(0).Number, Is.EqualTo(3)); Assert.That(evaluator.Derived(1).Number, Is.EqualTo(-5));
            Assert.That(program.SourceDependencies(0), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(program.SourceDependencies(1), Is.EqualTo(new[] { 0, 1, 2 }));
            var dependencies = program.SourceDependencies(1); dependencies[0] = 99;
            Assert.That(program.SourceDependencies(1), Is.EqualTo(new[] { 0, 1, 2 }), "Dependency arrays must not mutate the compiled program.");
            var saved = program.Definition; saved.Columns.Reverse(); saved.Columns[0].Name = "Renamed";
            var reordered = FPWorkbenchFormulaProgram.Compile(data, saved); var other = reordered.CreateEvaluator(); other.SetRow(new[] { "1", "2", "3" }, 0);
            Assert.That(reordered.SourceDependencies(0), Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(other.Derived(0).Number, Is.EqualTo(-5), "Stored D1 reference must follow the stable ID after list reorder.");
        }
        [Test] public async Task SourceUnitSymbolBoundRejectsTheSeventeenthIndependentSymbol()
        {
            var data = await Import(); var set = new FPWorkbenchFormulaSet { DatasetId = data.DatasetId };
            set.SourceUnits.Add(new FPWorkbenchSourceUnit { Column = 0, Units = "a*b*c*d*e*f*g*h*i*j*k*l*m*n*o*p" });
            var program = FPWorkbenchFormulaProgram.Compile(data, set);
            Assert.That(program.SourceUnits(0), Is.EqualTo("a*b*c*d*e*f*g*h*i*j*k*l*m*n*o*p"));
            set.SourceUnits[0].Units += "*q"; Assert.Throws<ArgumentException>(() => FPWorkbenchFormulaProgram.Compile(data, set));
            set.SourceUnits[0].Units = "a*b*c*d*e*f*g*h*i*j*k*l*m*n*o*p/a*q";
            Assert.That(FPWorkbenchFormulaProgram.Compile(data, set).SourceUnits(0), Does.Contain("q"), "Cancelling a symbol frees its slot.");
        }

        [Test] public async Task ExplicitErrorsAndCorrectionInputsRemainSeparateFromOriginals()
        {
            var data = await Import(); var set = new FPWorkbenchFormulaSet { DatasetId = data.DatasetId };
            set.Columns.Add(new FPWorkbenchDerivedColumn { Expression = "C1 / C2" });
            var program = FPWorkbenchFormulaProgram.Compile(data, set); var evaluator = program.CreateEvaluator();
            evaluator.SetRow(new[] { "1", "0", "3" }, 0); Assert.That(evaluator.Derived(0).Status, Is.EqualTo(FPWorkbenchFormulaStatus.DivideByZero));
            evaluator.SetRow(new[] { "", "2", "3" }, 0); Assert.That(evaluator.Derived(0).Status, Is.EqualTo(FPWorkbenchFormulaStatus.Missing));
            evaluator.SetRow(new[] { "9007199254740993", "2", "3" }, 0); Assert.That(evaluator.Derived(0).Status, Is.EqualTo(FPWorkbenchFormulaStatus.IntegerPrecision));
            evaluator.SetRow(new[] { "-18446744073709551615", "2", "3" }, 0); Assert.That(evaluator.Derived(0).Status, Is.EqualTo(FPWorkbenchFormulaStatus.IntegerPrecision));
            evaluator.SetRow(new[] { "+0009007199254740992", "2", "3" }, 0); Assert.That(evaluator.Derived(0).Number, Is.EqualTo(4503599627370496d));
            var changes = new FPWorkbenchChangeSet { DatasetId = data.DatasetId }; changes.Set(data.Cell(0, 0), "1", "10", FPWorkbenchEditKind.Number, "fixture");
            var effective = program.CreateEvaluator(changes.Snapshot()); string[] original = { "1", "2", "3" }; effective.SetRow(original, 0);
            Assert.That(effective.Derived(0).Number, Is.EqualTo(5)); Assert.That(original[0], Is.EqualTo("1"));
        }
        [Test] public async Task UnitCompatibilityAndCycleValidationAreExplicit()
        {
            var data = await Import(); var set = new FPWorkbenchFormulaSet { DatasetId = data.DatasetId };
            set.SourceUnits.Add(new FPWorkbenchSourceUnit { Column = 0, Units = "m" }); set.SourceUnits.Add(new FPWorkbenchSourceUnit { Column = 1, Units = "s" });
            set.Columns.Add(new FPWorkbenchDerivedColumn { Expression = "C1 / C2", ExpectedUnits = "m/s" });
            Assert.That(FPWorkbenchFormulaProgram.Compile(data, set).Units(0), Is.EqualTo("m*s^-1"));
            set.Columns[0].Expression = "C1 + C2"; Assert.Throws<ArgumentException>(() => FPWorkbenchFormulaProgram.Compile(data, set));
            set.SourceUnits.Clear(); set.Columns[0].ExpectedUnits = ""; set.Columns[0].Expression = "D2"; set.Columns.Add(new FPWorkbenchDerivedColumn { Expression = "D1" });
            Assert.Throws<ArgumentException>(() => FPWorkbenchFormulaProgram.Compile(data, set));
        }
        [Test] public async Task ReductionUsesWholeRangeAndExplicitInvalidPolicy()
        {
            string source = Path.Combine(root, "range.csv");
            var csv = new StringBuilder("value\n"); for (int i = 0; i < 1000; i++) csv.Append(i).Append('\n');
            File.WriteAllText(source, csv.ToString(), new UTF8Encoding(false));
            var data = await FPWorkbenchCsvDataset.ImportAsync(source, Path.Combine(root, "range-cache"), new FPWorkbenchCsvProfile());
            var set = new FPWorkbenchFormulaSet { DatasetId = data.DatasetId };
            set.Columns.Add(new FPWorkbenchDerivedColumn { Expression = "C1 * 2" });
            var program = FPWorkbenchFormulaProgram.Compile(data, set);
            var all = await FPWorkbenchReduction.CalculateAsync(data, program, 0, true, 0, 1000);
            Assert.That(all.Count, Is.EqualTo(1000)); Assert.That(all.Sum, Is.EqualTo(999000)); Assert.That(all.Mean, Is.EqualTo(999));
            var tile = await FPWorkbenchCalculatedPage.ReadAsync(data, program, 498, 4, 0, 1);
            Assert.That(tile.FirstRow, Is.EqualTo(498)); Assert.That(tile.Values[0][0].Number, Is.EqualTo(996));
            Assert.That(tile.Values[3][0].Number, Is.EqualTo(1002)); Assert.That(tile.FormulaFingerprint, Is.EqualTo(program.Fingerprint));
            var changes = new FPWorkbenchChangeSet { DatasetId = data.DatasetId };
            changes.Set(data.Cell(500, 0), "500", "", FPWorkbenchEditKind.Masked, "fixture");
            var range = await FPWorkbenchReduction.CalculateAsync(data, program, 0, false, 499, 502, FPWorkbenchInvalidPolicy.Skip, changes.Snapshot());
            Assert.That(range.Count, Is.EqualTo(2)); Assert.That(range.InvalidCount, Is.EqualTo(1)); Assert.That(range.Sum, Is.EqualTo(1000));
            var effectiveTile = await FPWorkbenchCalculatedPage.ReadAsync(data, program, 500, 1, 0, 1, changes.Snapshot());
            Assert.That(effectiveTile.Values[0][0].Status, Is.EqualTo(FPWorkbenchFormulaStatus.Missing));
            Assert.That(tile.Values[2][0].Number, Is.EqualTo(1000), "Previously returned tiles retain their revision.");
            try { await FPWorkbenchReduction.CalculateAsync(data, program, 0, false, 499, 502, changes: changes.Snapshot()); Assert.Fail("Expected invalid-row rejection."); }
            catch (InvalidDataException) { }
            var empty = await FPWorkbenchReduction.CalculateAsync(data, program, 0, false, 1000, 1000);
            Assert.That(empty.Sum, Is.Zero); Assert.That(double.IsNaN(empty.Mean), Is.True);
            using var cancelled = new System.Threading.CancellationTokenSource(); cancelled.Cancel();
            try { await FPWorkbenchReduction.CalculateAsync(data, program, 0, false, 0, 1000, cancellation: cancelled.Token); Assert.Fail("Expected cancellation."); }
            catch (System.OperationCanceledException) { }
        }
        [TestCase("C999")]
        [TestCase("System.IO.File.Delete(1)")]
        [TestCase("SUM(C1)")]
        [TestCase("1 / (2")]
        [TestCase("9007199254740993")]
        [TestCase("18446744073709551615")]
        public async Task UnsupportedOrUnsafeExpressionsAreRejected(string expression)
        {
            var data = await Import(); var set = new FPWorkbenchFormulaSet { DatasetId = data.DatasetId }; set.Columns.Add(new FPWorkbenchDerivedColumn { Expression = expression });
            Assert.Throws<ArgumentException>(() => FPWorkbenchFormulaProgram.Compile(data, set));
        }
        [Test] public async Task ReductionRetainsSmallResidualAndRejectsOverflow()
        {
            string source = Path.Combine(root, "residual.csv");
            File.WriteAllText(source, "value\n1e16\n1\n-1e16\n1e308\n1e308\n", new UTF8Encoding(false));
            var data = await FPWorkbenchCsvDataset.ImportAsync(source, Path.Combine(root, "residual-cache"), new FPWorkbenchCsvProfile());
            var program = FPWorkbenchFormulaProgram.Compile(data, new FPWorkbenchFormulaSet { DatasetId = data.DatasetId });
            var result = await FPWorkbenchReduction.CalculateAsync(data, program, 0, false, 0, 3);
            Assert.That(result.Sum, Is.EqualTo(1)); Assert.That(result.Count, Is.EqualTo(3));
            try { await FPWorkbenchReduction.CalculateAsync(data, program, 0, false, 3, 5); Assert.Fail("Expected explicit overflow."); }
            catch (OverflowException) { }
        }
    }
}
