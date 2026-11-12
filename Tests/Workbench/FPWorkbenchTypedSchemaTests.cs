// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Tests
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;

    public sealed class FPWorkbenchTypedSchemaTests
    {
        [Test] public async Task TypedPagesRetainOriginalEffectiveQualityAndRevisionWithoutChangingRawText()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPTypedPage-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try {
                string path = Path.Combine(root, "source.csv");
                string original = "id;reading;status\n9007199254740993;---;OFF\n18446744073709551615;bad;ON\n3;12,5;ON\n"; File.WriteAllText(path, original);
                var data = await FPWorkbenchCsvDataset.ImportAsync(path, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile { Delimiter = ';' });
                var schema = new FPWorkbenchTypedSchema { DatasetId = data.DatasetId, Columns = new[] {
                    new FPWorkbenchColumnDeclaration { Column = 0, ColumnId = data.Cell(0, 0).ColumnId, Type = FPWorkbenchColumnType.UnsignedInteger },
                    new FPWorkbenchColumnDeclaration { Column = 1, ColumnId = data.Cell(0, 1).ColumnId, Type = FPWorkbenchColumnType.Real, Culture = "de-DE", MissingTokens = new[] { "---" } },
                    new FPWorkbenchColumnDeclaration { Column = 2, ColumnId = data.Cell(0, 2).ColumnId, Type = FPWorkbenchColumnType.Boolean, TrueToken = "ON", FalseToken = "OFF" } } };
                var compiled = schema.Compile(data); var table = new FPWorkbenchTableView(data, typedSchema: compiled);
                var changes = new FPWorkbenchChangeSet { DatasetId = data.DatasetId };
                changes.Set(data.Cell(0, 0), "9007199254740993", "9007199254740995", FPWorkbenchEditKind.Integer, "exact identifier");
                changes.Set(data.Cell(0, 1), "---", "1,25", FPWorkbenchEditKind.Text, "declared decimal culture");
                changes.Set(data.Cell(1, 1), "bad", "", FPWorkbenchEditKind.Masked, "bad sensor sample");
                changes.Set(data.Cell(2, 1), "12,5", "", FPWorkbenchEditKind.Missing, "explicit missing quality");
                compiled.ValidateChanges(changes);
                Assert.Throws<ArgumentException>(() => compiled.ValidateCorrection(0, "18446744073709551616", FPWorkbenchEditKind.Text));
                Assert.Throws<ArgumentException>(() => compiled.ValidateCorrection(0, "-1", FPWorkbenchEditKind.Text));
                Assert.Throws<ArgumentException>(() => compiled.ValidateCorrection(1, "not numeric", FPWorkbenchEditKind.Text));
                Assert.Throws<ArgumentException>(() => compiled.ValidateCorrection(2, "true", FPWorkbenchEditKind.Boolean));
                compiled.ValidateCorrection(2, "ON", FPWorkbenchEditKind.Text);
                var page = await table.ReadProjectedPageAsync(0, 3, new[] { 1, 0, 2 }, changes.Snapshot());
                Assert.That(page.Columns, Is.EqualTo(new[] { 1, 0, 2 })); Assert.That(page.TypedSchemaFingerprint, Is.EqualTo(compiled.Fingerprint));
                Assert.That(page.Values[0][0].Original, Is.EqualTo("---")); Assert.That(page.Values[0][0].Text, Is.EqualTo("1,25"));
                Assert.That(page.Values[0][0].Typed.Original.State, Is.EqualTo(FPWorkbenchValueState.Missing));
                Assert.That(page.Values[0][0].Typed.Effective.RealNumber, Is.EqualTo(1.25));
                Assert.That(page.Values[0][1].Typed.Original.UnsignedInteger, Is.EqualTo(9007199254740993UL));
                Assert.That(page.Values[0][1].Typed.Effective.UnsignedInteger, Is.EqualTo(9007199254740995UL));
                Assert.That(page.Values[1][1].Typed.Original.UnsignedInteger, Is.EqualTo(ulong.MaxValue));
                var masked = page.Values[1][0]; Assert.That(masked.Text, Is.Empty); Assert.That(masked.Typed.Effective.State, Is.EqualTo(FPWorkbenchValueState.Masked));
                Assert.That(masked.Typed.Effective.RawText, Is.EqualTo("bad")); Assert.That(masked.Typed.Effective.ParsedState, Is.EqualTo(FPWorkbenchValueState.Invalid));
                Assert.That(page.Values[2][0].Typed.Original.RealNumber, Is.EqualTo(12.5)); Assert.That(page.Values[2][0].Typed.Effective.State, Is.EqualTo(FPWorkbenchValueState.Missing));
                Assert.That(page.Values[0][2].Typed.Effective.State, Is.EqualTo(FPWorkbenchValueState.Valid)); Assert.That(page.Values[0][2].Typed.Effective.Boolean, Is.False);
                schema.Columns[1].MissingTokens = new[] { "---", "bad" };
                var next = new FPWorkbenchTableView(data, typedSchema: schema.Compile(data)); var nextPage = await next.ReadPageAsync(1, 1, 1, 1);
                Assert.That(nextPage.TypedSchemaFingerprint, Is.Not.EqualTo(page.TypedSchemaFingerprint));
                Assert.That(nextPage.Values[0][0].Typed.Original.State, Is.EqualTo(FPWorkbenchValueState.Missing));
                Assert.That(masked.Typed.Original.State, Is.EqualTo(FPWorkbenchValueState.Invalid));
                var legacy = await new FPWorkbenchTableView(data).ReadPageAsync(0, 1, 0, 2);
                Assert.That(legacy.TypedSchemaFingerprint, Is.EqualTo("none")); Assert.That(legacy.Values[0][1].Typed, Is.Null); Assert.That(legacy.Values[0][1].Text, Is.EqualTo("---"));
                Assert.That(File.ReadAllText(path), Is.EqualTo(original));
            }
            finally { Directory.Delete(root, true); }
        }
        private static FPWorkbenchColumnDeclaration Declare(FPWorkbenchColumnType type) => new FPWorkbenchColumnDeclaration { ColumnId = "fixture", Type = type };
        [Test] public void ExactIntegersRawTokensAndMasksRemainDistinct()
        {
            var signed = new FPWorkbenchColumnParser(Declare(FPWorkbenchColumnType.SignedInteger));
            Assert.That(signed.Parse(long.MaxValue.ToString(CultureInfo.InvariantCulture)).SignedInteger, Is.EqualTo(long.MaxValue));
            Assert.That(signed.Parse(long.MinValue.ToString(CultureInfo.InvariantCulture)).SignedInteger, Is.EqualTo(long.MinValue));
            Assert.That(signed.Parse("9007199254740993").SignedInteger, Is.EqualTo(9007199254740993L));
            Assert.That(signed.Parse("9223372036854775808").State, Is.EqualTo(FPWorkbenchValueState.Invalid));
            var unsigned = new FPWorkbenchColumnParser(Declare(FPWorkbenchColumnType.UnsignedInteger));
            Assert.That(unsigned.Parse("18446744073709551615").UnsignedInteger, Is.EqualTo(ulong.MaxValue));
            Assert.That(unsigned.Parse("-1").State, Is.EqualTo(FPWorkbenchValueState.Invalid));
            var declaration = Declare(FPWorkbenchColumnType.Real); declaration.MissingTokens = new[] { "", "---" };
            var real = new FPWorkbenchColumnParser(declaration); declaration.MissingTokens[1] = "changed";
            Assert.That(real.Parse("---").State, Is.EqualTo(FPWorkbenchValueState.Missing));
            Assert.That(real.Parse("not a number").State, Is.EqualTo(FPWorkbenchValueState.Invalid));
            var masked = real.Parse("12.5", true); Assert.That(masked.State, Is.EqualTo(FPWorkbenchValueState.Masked));
            Assert.That(masked.ParsedState, Is.EqualTo(FPWorkbenchValueState.Valid)); Assert.That(masked.RealNumber, Is.EqualTo(12.5)); Assert.That(masked.RawText, Is.EqualTo("12.5"));
            var badMask = real.Parse("bad", true); Assert.That(badMask.State, Is.EqualTo(FPWorkbenchValueState.Masked)); Assert.That(badMask.ParsedState, Is.EqualTo(FPWorkbenchValueState.Invalid));
            var text = new FPWorkbenchColumnParser(Declare(FPWorkbenchColumnType.Text));
            Assert.That(text.Parse("").State, Is.EqualTo(FPWorkbenchValueState.Valid)); Assert.That(text.Parse("0x00000000").RawText, Is.EqualTo("0x00000000"));
            Assert.That(text.Parse(null).State, Is.EqualTo(FPWorkbenchValueState.Missing));
        }
        [Test] public void CultureWhitespaceAndBooleanTokensRequireExplicitDeclarations()
        {
            var old = CultureInfo.CurrentCulture;
            try {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                var declaration = Declare(FPWorkbenchColumnType.Real); var invariant = new FPWorkbenchColumnParser(declaration);
                Assert.That(invariant.Parse("1.25").RealNumber, Is.EqualTo(1.25)); Assert.That(invariant.Parse("1,25").State, Is.EqualTo(FPWorkbenchValueState.Invalid));
                Assert.That(invariant.Parse(" 1.25 ").State, Is.EqualTo(FPWorkbenchValueState.Invalid));
                foreach (string bad in new[] { "NaN", "Infinity", "1e9999", "1,234.5" }) Assert.That(invariant.Parse(bad).State, Is.EqualTo(FPWorkbenchValueState.Invalid));
                declaration.Culture = "de-DE"; declaration.TrimWhitespace = true;
                var german = new FPWorkbenchColumnParser(declaration);
                Assert.That(german.Parse(" 1,25 ").RealNumber, Is.EqualTo(1.25)); Assert.That(german.Parse(" 1,25 ").RawText, Is.EqualTo(" 1,25 "));
                Assert.That(german.Parse("1.234,5").State, Is.EqualTo(FPWorkbenchValueState.Invalid));
                var boolean = Declare(FPWorkbenchColumnType.Boolean); boolean.TrueToken = "yes"; boolean.FalseToken = "no";
                Assert.That(new FPWorkbenchColumnParser(boolean).Parse("YES").State, Is.EqualTo(FPWorkbenchValueState.Invalid));
                boolean.IgnoreTokenCase = true; var insensitive = new FPWorkbenchColumnParser(boolean);
                Assert.That(insensitive.Parse("YES").Boolean, Is.True); Assert.That(insensitive.Parse("NO").Boolean, Is.False);
                boolean.MissingTokens = new[] { "YES" }; Assert.Throws<InvalidDataException>(() => new FPWorkbenchColumnParser(boolean));
            }
            finally { CultureInfo.CurrentCulture = old; }
        }
        [Test] public void UtcValuesRetainTicksAndRejectIncompleteOrImplicitZoneFormats()
        {
            var declaration = Declare(FPWorkbenchColumnType.UtcTimestamp); var parser = new FPWorkbenchColumnParser(declaration);
            long first = parser.Parse("2025-01-17 11:49:52.0000000").UtcTicks;
            Assert.That(parser.Parse("2025-01-17 11:49:52.0000001").UtcTicks - first, Is.EqualTo(1));
            Assert.That(parser.Parse("2025-02-29 11:49:52.0000000").State, Is.EqualTo(FPWorkbenchValueState.Invalid));
            Assert.That(parser.Parse("2025-01-17 11:49:52.0000000Z").State, Is.EqualTo(FPWorkbenchValueState.Invalid));
            declaration.UtcFormat = "MM-dd HH:mm:ss"; Assert.Throws<ArgumentException>(() => new FPWorkbenchColumnParser(declaration));
            declaration.UtcFormat = "yyyy-MM-dd HH:mm:ssK"; Assert.Throws<ArgumentException>(() => new FPWorkbenchColumnParser(declaration));
        }
        [Test] public async Task CompiledSchemasBindVerifiedPhysicalIdsAndFreezeDefinitions()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPTypedSchema-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try {
                string path = Path.Combine(root, "source.csv"), original = "id,value\n9007199254740993,---\n"; File.WriteAllText(path, original);
                var data = await FPWorkbenchCsvDataset.ImportAsync(path, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile());
                var schema = new FPWorkbenchTypedSchema { DatasetId = data.DatasetId, Columns = new[] {
                    new FPWorkbenchColumnDeclaration { Column = 1, ColumnId = data.Cell(0, 1).ColumnId, Type = FPWorkbenchColumnType.Real, MissingTokens = new[] { "---" }, Units = "m" },
                    new FPWorkbenchColumnDeclaration { Column = 0, ColumnId = data.Cell(0, 0).ColumnId, Type = FPWorkbenchColumnType.UnsignedInteger } } };
                var compiled = schema.Compile(data); var restored = JsonUtility.FromJson<FPWorkbenchTypedSchema>(JsonUtility.ToJson(schema));
                Assert.That(restored.Compile(data).Fingerprint, Is.EqualTo(compiled.Fingerprint));
                schema.Columns[0].MissingTokens[0] = "new";
                Assert.That(schema.Compile(data).Fingerprint, Is.Not.EqualTo(compiled.Fingerprint));
                Assert.That(compiled.TryGetParser(1, out var parser), Is.True); Assert.That(parser.Parse("---").State, Is.EqualTo(FPWorkbenchValueState.Missing));
                var copy = parser.Declaration; copy.MissingTokens[0] = "other";
                Assert.That(parser.Parse("---").State, Is.EqualTo(FPWorkbenchValueState.Missing)); Assert.That(compiled.TryGetParser(9, out _), Is.False);
                restored.Columns[0].ColumnId = "foreign"; Assert.Throws<InvalidDataException>(() => restored.Compile(data));
                Assert.Throws<InvalidDataException>(() => new FPWorkbenchTypedSchema { DatasetId = "foreign" }.Compile(data));
                Assert.That(File.ReadAllText(path), Is.EqualTo(original));
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
