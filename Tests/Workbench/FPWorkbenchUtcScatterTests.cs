// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Tests
{
    using System;
    using System.Collections;
    using System.IO;
    using NUnit.Framework;
    using UnityEngine.TestTools;
    public sealed class FPWorkbenchUtcScatterTests
    {
        [UnityTest] public IEnumerator UtcScatterExcludesOutsideRowsKeepsSubmillisecondPrecisionAndReopensItsOrigin()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPUtcScatter-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try {
                string path = Path.Combine(root, "source.csv"), original = "date,time,depth\nbad,bad,0\n17.01.2025,23:59:59.999999,5\n18.01.2025,00:00:00.002499,6\n17.01.2025,23:59:59.999999,7\nbad,bad,8\n"; File.WriteAllText(path, original);
                var import = FPWorkbenchCsvDataset.ImportAsync(path, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile()); while (!import.IsCompleted) yield return null; var data = import.GetAwaiter().GetResult();
                var rows = new FPWorkbenchRowRange { Enabled = true, FirstRow = 1, EndRow = 4 };
                var binding = new FPWorkbenchSeriesBinding { XMode = FPWorkbenchXMode.UtcDateTime, DateColumn = 0, XColumn = 1, YColumn = 2, Rows = rows.Copy() };
                var build = FPWorkbenchPointCache.BuildAsync(data, new[] { 1, 2 }, rowRange: rows, xBinding: binding); while (!build.IsCompleted) yield return null; var cache = build.GetAwaiter().GetResult();
                Assert.That(cache.ValidCount, Is.EqualTo(3)); Assert.That(cache.InvalidCount, Is.Zero);
                Assert.That(cache.OriginUtcTicks, Is.EqualTo(new DateTime(2025, 1, 17, 23, 59, 59, DateTimeKind.Utc).AddTicks(9999990).Ticks));
                var query = cache.QueryAsync(cache.Bounds, 10); while (!query.IsCompleted) yield return null; var view = query.GetAwaiter().GetResult();
                Assert.That(view.Points.Count, Is.EqualTo(3)); Assert.That(view.Points[0].Row, Is.EqualTo(1)); Assert.That(view.Points[0].X, Is.Zero);
                Assert.That(view.Points[1].Row, Is.EqualTo(2)); Assert.That(view.Points[1].X, Is.EqualTo(.0025).Within(1e-15)); Assert.That(view.Points[2].X, Is.Zero, "Unsorted UTC scatter is supported.");
                var again = FPWorkbenchPointCache.BuildAsync(data, new[] { 1, 2 }, rowRange: rows, xBinding: binding); while (!again.IsCompleted) yield return null;
                Assert.That(again.Result.DirectoryPath, Is.EqualTo(cache.DirectoryPath)); Assert.That(again.Result.OriginUtcTicks, Is.EqualTo(cache.OriginUtcTicks));
                var reject = FPWorkbenchPointCache.BuildAsync(data, new[] { 1, 2 }, xBinding: binding); while (!reject.IsCompleted) yield return null;
                string error = null; try { reject.GetAwaiter().GetResult(); } catch (InvalidDataException ex) { error = ex.Message; }
                Assert.That(error, Does.Contain("table row 1"), "Errors use the table's one-based numbering.");
                var skip = FPWorkbenchPointCache.BuildAsync(data, new[] { 1, 2 }, invalidPolicy: FPWorkbenchInvalidPolicy.Skip, xBinding: binding); while (!skip.IsCompleted) yield return null;
                Assert.That(skip.Result.ValidCount, Is.EqualTo(3)); Assert.That(skip.Result.InvalidCount, Is.EqualTo(2)); Assert.That(File.ReadAllText(path), Is.EqualTo(original));
            } finally { Directory.Delete(root, true); }
        }
    }
}
