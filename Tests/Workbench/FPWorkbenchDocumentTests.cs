// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Tests
{
    using System;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;

    public sealed class FPWorkbenchDocumentTests
    {
        [Test] public async Task ActiveSourceProgramIsConsistentAcrossWorkbookAdmissionAndRelinking()
        {
            string raw = Path.Combine(root, "typed-source.csv"), moved = Path.Combine(root, "moved.csv");
            const string original = "x,y\n1,\"1,25\"\n2,---\n"; File.WriteAllText(raw, original);
            var data = await FPWorkbenchCsvDataset.ImportAsync(raw, Path.Combine(root, "typed-cache"), new FPWorkbenchCsvProfile());
            var source = new FPWorkbenchSourceDocument { DatasetId = data.DatasetId, SourcePath = data.SourcePath, CacheDirectory = data.CacheDirectory,
                Import = data.ImportProfile, HasTypedSchema = true, HasCalculations = true,
                TypedSchema = new FPWorkbenchTypedSchema { DatasetId = data.DatasetId, Columns = new[] {
                    new FPWorkbenchColumnDeclaration { Column = 1, ColumnId = data.Cell(0, 1).ColumnId, Type = FPWorkbenchColumnType.Real, Culture = "de-DE", Units = "m", MissingTokens = new[] { "---" } } } },
                Calculations = new FPWorkbenchFormulaSet { DatasetId = data.DatasetId } };
            source.Calculations.Columns.Add(new FPWorkbenchDerivedColumn { Expression = "C2 * 2" });
            string fingerprint = source.TypedSchema.Compile(data).Fingerprint;
            var document = new FPWorkbenchDocument(); document.Sources.Add(source);
            string path = Path.Combine(root, "typed-program.fpworkbook"); await FPWorkbenchDocumentFile.SaveAsync(document, path);
            var loaded = await FPWorkbenchDocumentFile.LoadAsync(path);
            var admitted = loaded.Document.Sources[0]; var program = admitted.CompileProgram(loaded.SourceDatasets[0]);
            Assert.That(program.TypedSchemaFingerprint, Is.EqualTo(fingerprint));
            Assert.That(admitted.Calculations.TypedSchema.Compile(data).Fingerprint, Is.EqualTo(fingerprint));
            var evaluator = program.CreateEvaluator(); evaluator.SetRow(new[] { "1", "1,25" }, 0);
            Assert.That(evaluator.Derived(0).Number, Is.EqualTo(2.5)); Assert.That(program.Units(0), Is.EqualTo("m"));
            File.Move(raw, moved);
            var recovered = await FPWorkbenchSourceRecovery.RelinkAsync(loaded.Document, data.DatasetId, moved, Path.Combine(root, "relinked"));
            var reopened = await FPWorkbenchCsvDataset.OpenAsync(recovered.Sources[0].CacheDirectory);
            Assert.That(recovered.Sources[0].CompileProgram(reopened).Fingerprint, Is.EqualTo(program.Fingerprint));
            Assert.That(File.ReadAllText(moved), Is.EqualTo(original)); Assert.That(loaded.Document.Sources[0].SourcePath, Is.EqualTo(raw));
            source.Calculations.Columns[0].Expression = "C1 + C2";
            source.Calculations.SourceUnits.Add(new FPWorkbenchSourceUnit { Column = 0, Units = "s" });
            Assert.Throws<InvalidDataException>(() => source.Validate(), "Admission must validate active type units with formulas before publishing a workbook.");
            admitted.HasCalculations = false; admitted.Calculations = null;
            Assert.That(admitted.CompileProgram(data).ColumnCount, Is.Zero); Assert.That(admitted.CompileProgram(data).TypedSchemaFingerprint, Is.EqualTo(fingerprint));
            admitted.HasTypedSchema = false;
            Assert.Throws<InvalidDataException>(() => admitted.CompileProgram(data));
        }
        [Test] public async Task TypedWorkbookRejectsInvalidActiveCorrectionsButAdmitsQualityActions()
        {
            var document = await MakeDocument(); document.Validate(); var reference = document.Sources[0];
            var data = await FPWorkbenchCsvDataset.OpenAsync(reference.CacheDirectory);
            reference.HasTypedSchema = true;
            reference.TypedSchema = new FPWorkbenchTypedSchema { DatasetId = data.DatasetId, Columns = new[] {
                new FPWorkbenchColumnDeclaration { Column = 1, ColumnId = data.Cell(0, 1).ColumnId, Type = FPWorkbenchColumnType.UnsignedInteger } } };
            var invalid = new FPWorkbenchChangeSet { DatasetId = data.DatasetId };
            invalid.Set(data.Cell(0, 1), "1", "-1", FPWorkbenchEditKind.Number, "type conflict"); document.Changes.Add(invalid);
            string path = Path.Combine(root, "bad-typed-edit.fpworkbook"); await FPWorkbenchDocumentFile.SaveAsync(document, path);
            try { await FPWorkbenchDocumentFile.LoadAsync(path); Assert.Fail("Declared unsigned values must reject negative correction overrides."); } catch (InvalidDataException) { }
            document.Changes.Clear(); var quality = new FPWorkbenchChangeSet { DatasetId = data.DatasetId };
            quality.Set(data.Cell(0, 1), "1", "", FPWorkbenchEditKind.Missing, "missing quality");
            quality.Set(data.Cell(1, 1), "2", "", FPWorkbenchEditKind.Masked, "mask quality"); document.Changes.Add(quality);
            string valid = Path.Combine(root, "typed-quality.fpworkbook"); await FPWorkbenchDocumentFile.SaveAsync(document, valid);
            var loaded = await FPWorkbenchDocumentFile.LoadAsync(valid);
            Assert.That(loaded.Document.Changes[0].Cells.Count, Is.EqualTo(2));
        }
        [Test] public async Task TypedSourceMetadataPersistsWithoutOpenSheetsAndVerifiesActualColumnIdentity()
        {
            var document = await MakeDocument(); document.Validate(); var source = document.Sources[0];
            var dataset = await FPWorkbenchCsvDataset.OpenAsync(source.CacheDirectory);
            source.HasTypedSchema = true;
            source.TypedSchema = new FPWorkbenchTypedSchema { DatasetId = dataset.DatasetId, Columns = new[] {
                new FPWorkbenchColumnDeclaration { Column = 1, ColumnId = dataset.Cell(0, 1).ColumnId, Type = FPWorkbenchColumnType.Real, Units = "m", MissingTokens = new[] { "---", "" } } } };
            string fingerprint = source.TypedSchema.Compile(dataset).Fingerprint;
            document.Sheets.Clear(); document.ActiveSheet = 0;
            string path = Path.Combine(root, "typed.fpworkbook"); byte[] original = File.ReadAllBytes(source.SourcePath);
            await FPWorkbenchDocumentFile.SaveAsync(document, path);
            var loaded = await FPWorkbenchDocumentFile.LoadAsync(path);
            Assert.That(loaded.Document.Sheets, Is.Empty); Assert.That(loaded.Document.Sources[0].HasTypedSchema, Is.True);
            Assert.That(loaded.Document.Sources[0].TypedSchema.Compile(loaded.SourceDatasets[0]).Fingerprint, Is.EqualTo(fingerprint));
            source.TypedSchema.Columns[0].ColumnId = "foreign";
            string bad = Path.Combine(root, "foreign-column.fpworkbook");
            try { await FPWorkbenchDocumentFile.SaveAsync(document, bad); Assert.Fail("Metadata validation must reject a foreign stable column ID."); } catch (InvalidDataException) { }
            File.WriteAllText(bad, UnityEngine.JsonUtility.ToJson(document));
            try { await FPWorkbenchDocumentFile.LoadAsync(bad); Assert.Fail("Verified source admission must reject a foreign stable column ID."); } catch (InvalidDataException) { }
            source.HasTypedSchema = false;
            Assert.Throws<InvalidDataException>(() => source.Validate());
            Assert.That(File.ReadAllBytes(source.SourcePath), Is.EqualTo(original));
        }
        private string root;
        [SetUp] public void Setup() { root = Path.Combine(Path.GetTempPath(), "FPWorkbookTest-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); }
        [TearDown] public void Teardown() { Directory.Delete(root, true); }
        private async Task<FPWorkbenchDocument> MakeDocument()
        {
            string csv = Path.Combine(root, "source.csv"); File.WriteAllText(csv, "x,depth\n0,1\n1,2\n", new UTF8Encoding(false));
            var dataset = await FPWorkbenchCsvDataset.ImportAsync(csv, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile());
            var columns = new FPWorkbenchColumnLayout(); columns.EnsureColumns(2); columns.ManualWidths[1] = 235;
            var document = new FPWorkbenchDocument { Name = "Deployment review" };
            document.Sheets.Add(new FPWorkbenchSheetDocument { Name = "Depth", DatasetId = dataset.DatasetId, SourcePath = csv, CacheDirectory = dataset.CacheDirectory, Columns = columns, Binding = new FPWorkbenchSeriesBinding { XMode = FPWorkbenchXMode.Numeric, YColumn = 1 }, ShowPlot = true });
            return document;
        }
        [Test] public async Task WorkbookRoundTripRetainsIdentitySettingsAndOriginalSource()
        {
            var document = await MakeDocument(); var sheet = document.Sheets[0];
            sheet.FirstRow = 1; sheet.Plot.StartX = .25; sheet.Plot.EndX = .75; sheet.Plot.InvertY = true; sheet.Plot.Title = "Dive profile";
            sheet.Plot.XLabel = "Elapsed time (s)"; sheet.Plot.YLabel = "Depth (m)";
            sheet.Plot.Style = FPWorkbenchPlotStyle.Step; sheet.Plot.LineWidth = 2.5f; sheet.Plot.MarkerSize = 7;
            sheet.Columns.ShowRowHeader = false; sheet.Columns.RowHeaderWidth = 125;
            string path = Path.Combine(root, "deployment.fpworkbook"); byte[] original = File.ReadAllBytes(sheet.SourcePath);
            await FPWorkbenchDocumentFile.SaveAsync(document, path);
            var loaded = await FPWorkbenchDocumentFile.LoadAsync(path);
            Assert.That(loaded.Document.Id, Is.EqualTo(document.Id)); Assert.That(loaded.Document.Sheets[0].Id, Is.EqualTo(sheet.Id));
            Assert.That(loaded.Document.Sheets[0].FirstRow, Is.EqualTo(1));
            Assert.That(loaded.Document.Sheets[0].Columns.ManualWidths[1], Is.EqualTo(235));
            Assert.That(loaded.Document.Sheets[0].Columns.ShowRowHeader, Is.False); Assert.That(loaded.Document.Sheets[0].Columns.RowHeaderWidth, Is.EqualTo(125));
            Assert.That(loaded.Document.Sheets[0].Plot.XLabel, Is.EqualTo("Elapsed time (s)"));
            Assert.That(loaded.Document.Sheets[0].Plot.YLabel, Is.EqualTo("Depth (m)"));
            Assert.That(loaded.Document.Sheets[0].Plot.Title, Is.EqualTo("Dive profile"));
            Assert.That(loaded.Document.Sheets[0].Plot.InvertY, Is.True);
            Assert.That(loaded.Document.Sheets[0].Plot.Style, Is.EqualTo(FPWorkbenchPlotStyle.Step));
            Assert.That(loaded.Document.Sheets[0].Plot.LineWidth, Is.EqualTo(2.5f));
            Assert.That(loaded.Document.Sheets[0].Plot.MarkerSize, Is.EqualTo(7));
            Assert.That(loaded.Datasets[0].DatasetId, Is.EqualTo(sheet.DatasetId));
            Assert.That(File.ReadAllBytes(sheet.SourcePath), Is.EqualTo(original));
        }
        [Test] public async Task PlotStylesValidateAndLegacySettingsKeepDefaults()
        {
            var legacy = UnityEngine.JsonUtility.FromJson<FPWorkbenchPlotSettings>("{\"StartX\":0,\"EndX\":1}");
            Assert.That(legacy.Style, Is.EqualTo(FPWorkbenchPlotStyle.Line));
            Assert.That(legacy.LineWidth, Is.EqualTo(1)); Assert.That(legacy.MarkerSize, Is.EqualTo(3));
            var document = await MakeDocument(); var plot = document.Sheets[0].Plot;
            foreach (FPWorkbenchPlotStyle style in Enum.GetValues(typeof(FPWorkbenchPlotStyle))) { plot.Style = style; document.Validate(); }
            plot.Style = (FPWorkbenchPlotStyle)99; Assert.Throws<InvalidDataException>(() => document.Validate());
            plot.Style = FPWorkbenchPlotStyle.Points; plot.LineWidth = float.NaN; Assert.Throws<InvalidDataException>(() => document.Validate());
            plot.LineWidth = 1; plot.MarkerSize = 100; Assert.Throws<InvalidDataException>(() => document.Validate());
        }

        [Test] public async Task ScatterFiguresRetainFrozenAxesAndRejectUnresolvedReferences()
        {
            var document = await MakeDocument(); var sheet = document.Sheets[0];
            var data = await FPWorkbenchCsvDataset.OpenAsync(sheet.CacheDirectory);
            var definitions = new FPWorkbenchFormulaSet { DatasetId = data.DatasetId };
            definitions.Columns.Add(new FPWorkbenchDerivedColumn { Name = "Scaled X", Expression = "C1 * 2" });
            var program = FPWorkbenchFormulaProgram.Compile(data, definitions);
            var figure = new FPWorkbenchFigureDocument { Kind = FPWorkbenchFigureKind.Scatter, DatasetId = data.DatasetId, Name = "XY",
                Binding = new FPWorkbenchSeriesBinding { XMode = FPWorkbenchXMode.Numeric, XDerivedId = program.ColumnId(0), YColumn = 1 },
                HasCalculationSnapshot = true, CalculationSnapshot = program.Definition, Colors = new[] { UnityEngine.Color.cyan }, PointBudget = 123 };
            figure.Plot.StartX = .25; figure.Plot.EndX = 1.5; figure.Plot.MinimumY = 1; figure.Plot.MaximumY = 3; figure.Plot.HasRange = true;
            document.Figures.Add(figure); string path = Path.Combine(root, "scatter.fpworkbook");
            await FPWorkbenchDocumentFile.SaveAsync(document, path);
            var loaded = await FPWorkbenchDocumentFile.LoadAsync(path); var restored = loaded.Document.Figures[0];
            Assert.That(restored.Kind, Is.EqualTo(FPWorkbenchFigureKind.Scatter)); Assert.That(restored.PointBudget, Is.EqualTo(123));
            Assert.That(restored.Binding.XDerivedId, Is.EqualTo(program.ColumnId(0))); Assert.That(restored.Plot.StartX, Is.EqualTo(.25));
            Assert.That(restored.Colors[0], Is.EqualTo(UnityEngine.Color.cyan));
            Assert.That(loaded.Document.Sources[0].HasCalculations, Is.False, "Figure snapshot is independent of current source calculations.");
            figure.Binding.XDerivedId = "removed"; await FPWorkbenchDocumentFile.SaveAsync(document, path);
            try { await FPWorkbenchDocumentFile.LoadAsync(path); Assert.Fail("Expected unresolved scatter axis rejection."); } catch (InvalidDataException) { }
            figure.Binding.XDerivedId = program.ColumnId(0); figure.PointBudget = 65537;
            Assert.Throws<InvalidDataException>(() => document.Validate());
        }

        [Test] public async Task Point3DFiguresValidateFrozenZAndCameraMetadata()
        {
            var document = await MakeDocument(); var sheet = document.Sheets[0]; var data = await FPWorkbenchCsvDataset.OpenAsync(sheet.CacheDirectory);
            var definitions = new FPWorkbenchFormulaSet { DatasetId = data.DatasetId };
            definitions.Columns.Add(new FPWorkbenchDerivedColumn { Name = "Z", Expression = "C1 + C2" });
            var program = FPWorkbenchFormulaProgram.Compile(data, definitions);
            var figure = new FPWorkbenchFigureDocument { Kind = FPWorkbenchFigureKind.Point3D, DatasetId = data.DatasetId, Name = "XYZ",
                Binding = new FPWorkbenchSeriesBinding { XMode = FPWorkbenchXMode.Numeric, XColumn = 0, YColumn = 1 },
                HasCalculationSnapshot = true, CalculationSnapshot = program.Definition, Colors = new[] { UnityEngine.Color.cyan }, PointBudget = 77,
                Spatial = new FPWorkbenchPoint3DSettings { ZDerivedId = program.ColumnId(0), Orthographic = true, Zoom = 2,
                    Pan = new UnityEngine.Vector3(.25f, -.5f, 0), Clip = true, MinimumX = 0, MaximumX = 1, MinimumY = 1, MaximumY = 2, MinimumZ = 1, MaximumZ = 3, PointSize = 7 } };
            document.Figures.Add(figure); string path = Path.Combine(root, "xyz.fpworkbook"); await FPWorkbenchDocumentFile.SaveAsync(document, path);
            var loaded = await FPWorkbenchDocumentFile.LoadAsync(path); var actual = loaded.Document.Figures[0];
            Assert.That(actual.Kind, Is.EqualTo(FPWorkbenchFigureKind.Point3D)); Assert.That(actual.PointBudget, Is.EqualTo(77));
            Assert.That(actual.Spatial.ZDerivedId, Is.EqualTo(program.ColumnId(0))); Assert.That(actual.Spatial.PointSize, Is.EqualTo(7));
            Assert.That(actual.Spatial.Zoom, Is.EqualTo(2)); Assert.That(actual.Spatial.Pan.x, Is.EqualTo(.25f)); Assert.That(actual.Spatial.Orthographic, Is.True);
            Assert.That(actual.Spatial.Clip, Is.True); Assert.That(actual.Spatial.MaximumZ, Is.EqualTo(3));
            Assert.That(loaded.Document.Sources[0].HasCalculations, Is.False);
            figure.Spatial.ZDerivedId = "removed"; await FPWorkbenchDocumentFile.SaveAsync(document, path);
            try { await FPWorkbenchDocumentFile.LoadAsync(path); Assert.Fail("Expected unresolved Z rejection."); } catch (InvalidDataException) { }
            figure.Spatial.ZDerivedId = program.ColumnId(0); figure.Spatial.Orbit = new UnityEngine.Quaternion(0, 0, 0, 0);
            Assert.Throws<InvalidDataException>(() => document.Validate());
            figure.Spatial.Orbit = UnityEngine.Quaternion.identity; figure.Spatial.Zoom = float.NaN; Assert.Throws<InvalidDataException>(() => document.Validate());
            figure.Spatial.Zoom = 1; figure.Spatial.MinimumZ = 5; Assert.Throws<InvalidDataException>(() => document.Validate());
        }

        [Test] public async Task SavedFiguresRetainBindingsColorsAndFrozenCorrections()
        {
            var document = await MakeDocument(); string dataset = document.Sheets[0].DatasetId;
            var snapshot = new FPWorkbenchChangeSet { DatasetId = dataset };
            snapshot.Set(new FPWorkbenchCell(dataset, 1, 1), "2", "9", FPWorkbenchEditKind.Number, "figure snapshot");
            var overlay = new FPWorkbenchFigureDocument { Name = "Comparison", DatasetId = dataset, Kind = FPWorkbenchFigureKind.Overlay,
                YColumns = new[] { 0, 1 }, Colors = new[] { UnityEngine.Color.red, UnityEngine.Color.cyan },
                Binding = new FPWorkbenchSeriesBinding { XMode = FPWorkbenchXMode.Numeric, XColumn = 0, YColumn = 1 }, HasCorrectionSnapshot = true, CorrectionSnapshot = snapshot };
            overlay.Axes.Right = new[] { false, true }; overlay.Axes.RightSettings.YLabel = "Right values";
            overlay.Plot.StartX = .25; overlay.Plot.EndX = .75;
            var histogram = new FPWorkbenchFigureDocument { Name = "Depth distribution", DatasetId = dataset, Kind = FPWorkbenchFigureKind.Histogram,
                HistogramColumn = 1, HistogramBins = 12, FirstRow = 0, EndRow = 2, Minimum = 0, Maximum = 10, Density = true, HasCorrectionSnapshot = true, CorrectionSnapshot = snapshot };
            document.Figures.Add(overlay); document.Figures.Add(histogram);
            string path = Path.Combine(root, "figures.fpworkbook"); var save = FPWorkbenchDocumentFile.SaveAsync(document, path);
            overlay.YColumns[0] = 1; histogram.HistogramBins = 100; await save;
            var loaded = await FPWorkbenchDocumentFile.LoadAsync(path);
            Assert.That(loaded.Document.Figures.Count, Is.EqualTo(2)); var actual = loaded.Document.Figures[0];
            Assert.That(actual.Axes.Right, Is.EqualTo(new[] { false, true })); Assert.That(actual.Axes.RightSettings.YLabel, Is.EqualTo("Right values"));
            Assert.That(actual.Id, Is.EqualTo(overlay.Id)); Assert.That(actual.YColumns, Is.EqualTo(new[] { 0, 1 }));
            Assert.That(actual.Colors[1], Is.EqualTo(UnityEngine.Color.cyan)); Assert.That(actual.Plot.StartX, Is.EqualTo(.25));
            Assert.That(actual.CorrectionSnapshot.Cells[0].Value, Is.EqualTo("9")); Assert.That(loaded.Document.Figures[1].HistogramBins, Is.EqualTo(12));
            Assert.That(loaded.Document.Figures[1].Density, Is.True);
        }
        [Test] public async Task SavedFigureSourceColumnsAndCorrectionPreconditionsAreVerifiedOnLoad()
        {
            var document = await MakeDocument(); var figure = new FPWorkbenchFigureDocument { DatasetId = document.Sheets[0].DatasetId,
                Kind = FPWorkbenchFigureKind.Histogram, HistogramColumn = 2, EndRow = 2 };
            document.Figures.Add(figure); string path = Path.Combine(root, "bad-figure.fpworkbook");
            await FPWorkbenchDocumentFile.SaveAsync(document, path);
            try { await FPWorkbenchDocumentFile.LoadAsync(path); Assert.Fail("Expected source schema rejection."); } catch (InvalidDataException) { }
            figure.HistogramColumn = 1; figure.CorrectionSnapshot = new FPWorkbenchChangeSet { DatasetId = figure.DatasetId };
            figure.HasCorrectionSnapshot = true;
            figure.CorrectionSnapshot.Set(new FPWorkbenchCell(figure.DatasetId, 1, 1), "wrong original", "9", FPWorkbenchEditKind.Number, "invalid fixture");
            await FPWorkbenchDocumentFile.SaveAsync(document, path);
            try { await FPWorkbenchDocumentFile.LoadAsync(path); Assert.Fail("Expected original-cell conflict."); } catch (InvalidDataException) { }
            document.Figures.Add(figure); Assert.Throws<InvalidDataException>(() => document.Validate());
        }
        [Test] public async Task RegisteredSourcesAndFiguresOutliveAllOpenSheetTabs()
        {
            var document = await MakeDocument(); document.Validate(); var source = document.Sources[0];
            document.Figures.Add(new FPWorkbenchFigureDocument { DatasetId = source.DatasetId, Kind = FPWorkbenchFigureKind.Histogram, HistogramColumn = 1, EndRow = 2 });
            document.Sheets.Clear(); document.ActiveSheet = 0;
            string path = Path.Combine(root, "closed-tabs.fpworkbook"); await FPWorkbenchDocumentFile.SaveAsync(document, path);
            var loaded = await FPWorkbenchDocumentFile.LoadAsync(path);
            Assert.That(loaded.Document.Sheets, Is.Empty); Assert.That(loaded.Datasets, Is.Empty);
            Assert.That(loaded.SourceDatasets.Count, Is.EqualTo(1)); Assert.That(loaded.SourceDatasets[0].DatasetId, Is.EqualTo(source.DatasetId));
            Assert.That(loaded.Document.Figures.Count, Is.EqualTo(1));
            Assert.Throws<IOException>(() => FPWorkbenchDocumentFile.SaveAsync(document, Path.Combine(source.CacheDirectory, "unsafe.fpworkbook")));
            document.Sources.Add(source); Assert.Throws<InvalidDataException>(() => document.Validate()); document.Sources.RemoveAt(1);
            File.AppendAllText(source.SourcePath, "3,4\n");
            try { await FPWorkbenchDocumentFile.LoadAsync(path); Assert.Fail("Closed-tab sources still require revision verification."); } catch (InvalidDataException) { }
        }
        [Test] public async Task SourceCalculationsPersistStableDependenciesAndUnitsWithoutOpenTabs()
        {
            var document = await MakeDocument(); document.Validate(); var source = document.Sources[0];
            source.HasCalculations = true; source.Calculations = new FPWorkbenchFormulaSet { DatasetId = source.DatasetId };
            source.Calculations.SourceUnits.Add(new FPWorkbenchSourceUnit { Column = 0, Units = "m" });
            source.Calculations.SourceUnits.Add(new FPWorkbenchSourceUnit { Column = 1, Units = "m" });
            source.Calculations.Columns.Add(new FPWorkbenchDerivedColumn { Name = "Sum", Expression = "C1 + C2", ExpectedUnits = "m" });
            source.Calculations.Columns.Add(new FPWorkbenchDerivedColumn { Name = "Twice", Expression = "D1 * 2", ExpectedUnits = "m" });
            string sumId = source.Calculations.Columns[0].Id; document.Sheets.Clear(); document.ActiveSheet = 0;
            string path = Path.Combine(root, "calculations.fpworkbook"); byte[] original = File.ReadAllBytes(source.SourcePath);
            var saving = FPWorkbenchDocumentFile.SaveAsync(document, path); source.Calculations.Columns.Reverse(); await saving;
            var loaded = await FPWorkbenchDocumentFile.LoadAsync(path); var restored = loaded.Document.Sources[0];
            Assert.That(restored.HasCalculations, Is.True); Assert.That(restored.Calculations.Columns[1].References[0].DerivedId, Is.EqualTo(sumId));
            var program = FPWorkbenchFormulaProgram.Compile(loaded.SourceDatasets[0], restored.Calculations);
            var page = await FPWorkbenchCalculatedPage.ReadAsync(loaded.SourceDatasets[0], program, 0, 2, 0, 2);
            Assert.That(page.Values[1][0].Number, Is.EqualTo(3)); Assert.That(page.Values[1][1].Number, Is.EqualTo(6)); Assert.That(program.Units(1), Is.EqualTo("m"));
            Assert.That(File.ReadAllBytes(source.SourcePath), Is.EqualTo(original));
            restored.Calculations.Columns[0].Expression = "D2";
            Assert.Throws<InvalidDataException>(() => loaded.Document.Validate(), "Cycles reject document validation.");
        }
        [Test] public async Task SourceCalculationsRejectColumnsOutsideVerifiedSchema()
        {
            var document = await MakeDocument(); document.Validate(); var source = document.Sources[0];
            source.HasCalculations = true; source.Calculations = new FPWorkbenchFormulaSet { DatasetId = source.DatasetId };
            source.Calculations.Columns.Add(new FPWorkbenchDerivedColumn { Expression = "C99" });
            string path = Path.Combine(root, "bad-calculations.fpworkbook"); await FPWorkbenchDocumentFile.SaveAsync(document, path);
            try { await FPWorkbenchDocumentFile.LoadAsync(path); Assert.Fail("Expected verified physical-column bound rejection."); } catch (InvalidDataException) { }
            source.Calculations.DatasetId = "wrong revision"; Assert.Throws<InvalidDataException>(() => document.Validate());
        }
        [Test] public async Task SaveSnapshotsBeforeBackgroundWorkAndCancellationPreservesPriorFile()
        {
            var document = await MakeDocument(); string path = Path.Combine(root, "test.fpworkbook");
            var save = FPWorkbenchDocumentFile.SaveAsync(document, path); document.Name = "Changed after snapshot"; await save;
            Assert.That((await FPWorkbenchDocumentFile.LoadAsync(path)).Document.Name, Is.EqualTo("Deployment review"));
            byte[] prior = File.ReadAllBytes(path); using var cancel = new CancellationTokenSource(); cancel.Cancel();
            try { await FPWorkbenchDocumentFile.SaveAsync(document, path, cancel.Token); Assert.Fail(); }
            catch (OperationCanceledException) { }
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(prior));
            await FPWorkbenchDocumentFile.SaveAsync(document, path);
            Assert.That((await FPWorkbenchDocumentFile.LoadAsync(path)).Document.Name, Is.EqualTo("Changed after snapshot"));
        }
        [TestCase("{}")]
        [TestCase("{\"Format\":\"FuzzPhyte.DataWorkbench\",\"Version\":99}")]
        [TestCase("not-json")]
        public async Task UnsupportedOrUnrelatedJsonIsRejected(string json)
        {
            string path = Path.Combine(root, "bad.fpworkbook"); File.WriteAllText(path, json);
            try { await FPWorkbenchDocumentFile.LoadAsync(path); Assert.Fail(); }
            catch (InvalidDataException) { }
        }
        [Test] public async Task MissingSourceAndCacheRevisionMismatchAreRejected()
        {
            var document = await MakeDocument(); string path = Path.Combine(root, "test.fpworkbook");
            document.Sheets[0].DatasetId = "wrong-revision"; await FPWorkbenchDocumentFile.SaveAsync(document, path);
            try { await FPWorkbenchDocumentFile.LoadAsync(path); Assert.Fail(); }
            catch (InvalidDataException) { }
            File.Delete(document.Sheets[0].SourcePath);
            try { await FPWorkbenchDocumentFile.LoadAsync(path); Assert.Fail(); }
            catch (InvalidDataException) { }
        }
        [Test] public async Task SaveCannotWriteInsideCacheAndDocumentIdentityIsUnique()
        {
            var document = await MakeDocument(); var sheet = document.Sheets[0];
            Assert.Throws<IOException>(() => FPWorkbenchDocumentFile.SaveAsync(document, Path.Combine(sheet.CacheDirectory, "unsafe.fpworkbook")));
            document.Sheets.Add(sheet);
            Assert.Throws<InvalidDataException>(() => document.Validate());
            var layout = new FPWorkbenchColumnLayout(); layout.EnsureColumns(40);
            Assert.That(layout.PreviousFirst(32, 500), Is.EqualTo(28));
            Assert.That(layout.PreviousFirst(2, 500), Is.EqualTo(0));
        }
    }
}
