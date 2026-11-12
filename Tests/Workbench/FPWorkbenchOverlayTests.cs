// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Tests
{
    using System;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;

    public sealed class FPWorkbenchOverlayTests
    {
        private string root;
        [SetUp] public void Setup() { root = Path.Combine(Path.GetTempPath(), "FPOverlay-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); }
        [TearDown] public void Teardown() { Directory.Delete(root, true); }
        [Test] public async Task TypedPhysicalOverlayRetainsProgramIdentityAndPublishesDeclaredValues()
        {
            string source = Path.Combine(root, "typed.csv"); File.WriteAllText(source, "x,a,b\n0,\"1,25\",3\n1,---,4\n");
            var data = await FPWorkbenchCsvDataset.ImportAsync(source, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile());
            var schema = new FPWorkbenchTypedSchema { DatasetId = data.DatasetId, Columns = new[] {
                new FPWorkbenchColumnDeclaration { Column = 1, ColumnId = data.Cell(0, 1).ColumnId, Type = FPWorkbenchColumnType.Real, Culture = "de-DE", MissingTokens = new[] { "---" } } } };
            var program = FPWorkbenchFormulaProgram.Compile(data, new FPWorkbenchFormulaSet { DatasetId = data.DatasetId }, schema.Compile(data));
            var overlay = await FPWorkbenchOverlay.BuildAsync(data, new FPWorkbenchSeriesBinding { XMode = FPWorkbenchXMode.Numeric, XColumn = 0 }, new[] { 1, 2 }, calculations: program);
            var view = await overlay.QueryAsync(0, 1, 32);
            Assert.That(view.FormulaFingerprint, Is.EqualTo(program.Fingerprint));
            Assert.That(view.Minimum, Is.EqualTo(1.25)); Assert.That(view.Maximum, Is.EqualTo(4));
            foreach (var series in view.Series) Assert.That(series.FormulaFingerprint, Is.EqualTo(program.Fingerprint));
            Assert.That(view.Series[0].Bins[1].InvalidCount, Is.EqualTo(1));
        }
        [Test] public async Task SharedXOverlayPublishesOneRangeAndUnionExtremaWithCorrectionIdentity()
        {
            string source = Path.Combine(root, "source.csv"); var csv = new StringBuilder("x,a,b\n");
            for (int i = 0; i < 2000; i++) csv.Append(i).Append(',').Append(i).Append(',').Append(-i).Append('\n');
            File.WriteAllText(source, csv.ToString(), new UTF8Encoding(false));
            var data = await FPWorkbenchCsvDataset.ImportAsync(source, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile());
            var changes = new FPWorkbenchChangeSet { DatasetId = data.DatasetId }; changes.Set(data.Cell(500, 1), "500", "9999", FPWorkbenchEditKind.Number, "fixture");
            var common = new FPWorkbenchSeriesBinding { XMode = FPWorkbenchXMode.Numeric, XColumn = 0, YColumn = 1 };
            int[] selected = { 1, 2 }; var overlay = await FPWorkbenchOverlay.BuildAsync(data, common, selected, changes.Snapshot());
            selected[0] = 0; common.XColumn = 2;
            var full = await overlay.QueryAsync(0, 1999, 100);
            Assert.That(full.Series.Count, Is.EqualTo(2)); Assert.That(full.YColumns[0], Is.EqualTo(1));
            Assert.That(full.Minimum, Is.EqualTo(-1999)); Assert.That(full.Maximum, Is.EqualTo(9999));
            Assert.That(full.CorrectionFingerprint, Is.EqualTo(changes.Snapshot().Fingerprint));
            var leftTask = overlay.QueryAsync(100, 200, 40); var rightTask = overlay.QueryAsync(1400, 1500, 40);
            var left = await leftTask; var right = await rightTask;
            foreach (var view in left.Series) { Assert.That(view.StartX, Is.EqualTo(100)); Assert.That(view.EndX, Is.EqualTo(200)); }
            foreach (var view in right.Series) { Assert.That(view.StartX, Is.EqualTo(1400)); Assert.That(view.EndX, Is.EqualTo(1500)); }
            Assert.That(left.Minimum, Is.EqualTo(-200)); Assert.That(left.Maximum, Is.EqualTo(200));
            var axes = new FPWorkbenchOverlayAxes { Right = new[] { false, true } }; var settings = new FPWorkbenchPlotSettings();
            var leftRange = axes.Resolve(left, settings, false); var rightRange = axes.Resolve(left, settings, true);
            Assert.That(leftRange.Minimum, Is.EqualTo(100)); Assert.That(leftRange.Maximum, Is.EqualTo(200));
            Assert.That(rightRange.Minimum, Is.EqualTo(-200)); Assert.That(rightRange.Maximum, Is.EqualTo(-100));
            axes.RightSettings.YScale = FPWorkbenchAxisScale.Log10; Assert.Throws<ArgumentException>(() => axes.Resolve(left, settings, true));
            axes.RightSettings.YScale = FPWorkbenchAxisScale.Linear;
            axes.RightSettings.LockY = true; axes.RightSettings.MinimumY = -1000; axes.RightSettings.MaximumY = 1000;
            Assert.That(axes.Resolve(left, settings, true).Maximum, Is.EqualTo(1000));
            Assert.That(axes.Resolve(left, settings, true, false).Maximum, Is.EqualTo(-100));
            Assert.That(axes.Resolve(left, settings, false).Maximum, Is.EqualTo(200), "Right limits cannot alter the left scale.");
            axes.Right[0] = true; Assert.That(axes.Resolve(left, settings, false).HasValues, Is.False);
            Assert.That(axes.Resolve(left, settings, true, false).Minimum, Is.EqualTo(-200));
            Assert.That(axes.Resolve(left, settings, true, false).Maximum, Is.EqualTo(200));
            axes.Right = new[] { true }; Assert.Throws<InvalidDataException>(() => axes.Validate(2));
            Assert.Throws<InvalidDataException>(() => axes.Validate(int.MaxValue));
            axes.Right = Array.Empty<bool>(); axes.Validate(2); Assert.That(axes.Right, Is.EqualTo(new[] { false, false }), "Legacy figures default every series to left Y.");
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            try { await overlay.QueryAsync(0, 10, 10, cancellation.Token); Assert.Fail("Expected cancellation."); } catch (OperationCanceledException) { }
            try { await FPWorkbenchOverlay.BuildAsync(data, new FPWorkbenchSeriesBinding(), new[] { 1, 1 }); Assert.Fail("Expected duplicate rejection."); } catch (ArgumentException) { }
        }
    }
}
