// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using FuzzPhyte.Utility.Analytics.WorkBench.Editor;

    public sealed class FPWorkbenchCsvPreviewTests
    {
        private sealed class Reports : IProgress<FPWorkbenchCsvPreview>
        {
            public FPWorkbenchCsvPreview Value;
            public Action OnReport;
            public int Count;
            public void Report(FPWorkbenchCsvPreview value) { Value = value; Count++; OnReport?.Invoke(); }
        }
        [Test] public async Task PreviewPrecedesVerificationAndCancellationPreservesSourceAndRejectsLateInboxReports()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPPreviewCancel-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            using var cancel = new CancellationTokenSource();
            try {
                string source = Path.Combine(root, "source.csv"), cache = Path.Combine(root, "cache");
                const string text = "time,note\r\n0,\"quoted, value\"\r\n1,\"two\r\nlines\"\r\n2,\"a \"\"quote\"\"\"\r\n"; File.WriteAllText(source, text);
                var reports = new Reports { OnReport = cancel.Cancel };
                bool cancelled = false;
                try { await FPWorkbenchCsvDataset.ImportAsync(source, cache, new FPWorkbenchCsvProfile(), cancel.Token, preview: reports); }
                catch (OperationCanceledException) { cancelled = true; }
                Assert.That(cancelled, Is.True); Assert.That(reports.Count, Is.EqualTo(1)); Assert.That(Directory.Exists(cache), Is.False);
                Assert.That(File.ReadAllText(source), Is.EqualTo(text));
                var preview = reports.Value; Assert.That(preview.SourcePath, Is.EqualTo(source)); Assert.That(preview.SourceColumnCount, Is.EqualTo(2));
                Assert.That(preview.Rows.Count, Is.EqualTo(3)); Assert.That(preview.Rows[0][1], Is.EqualTo("quoted, value"));
                Assert.That(preview.Rows[1][1], Is.EqualTo("two\r\nlines")); Assert.That(preview.Rows[2][1], Is.EqualTo("a \"quote\""));
                Assert.That(((ICollection<string>)preview.Headers).IsReadOnly, Is.True);
                Assert.Throws<NotSupportedException>(() => ((IList<string>)preview.Rows[0])[0] = "changed");
                var type = typeof(FPDataWorkbenchWindow).GetNestedType("ImportPreviewReports", System.Reflection.BindingFlags.NonPublic);
                using var inbox = (IDisposable)Activator.CreateInstance(type, true);
                var receiver = (IProgress<FPWorkbenchCsvPreview>)inbox; receiver.Report(preview);
                Assert.That(type.GetMethod("Take").Invoke(inbox, null), Is.SameAs(preview));
                inbox.Dispose(); receiver.Report(preview);
                Assert.That(type.GetMethod("Take").Invoke(inbox, null), Is.Null, "A late worker report cannot resurrect closed preview state.");
            } finally { Directory.Delete(root, true); }
        }
        [Test] public async Task PreviewBoundsDoNotLimitTheVerifiedDataset()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPPreviewBounds-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try {
                var text = new StringBuilder();
                for (int c = 0; c < 34; c++) { if (c > 0) text.Append(','); text.Append("C").Append(c); } text.Append('\n');
                for (int r = 0; r < 40; r++) { for (int c = 0; c < 34; c++) { if (c > 0) text.Append(','); text.Append(r * 100 + c); } text.Append('\n'); }
                string path = Path.Combine(root, "source.csv"); File.WriteAllText(path, text.ToString()); var reports = new Reports();
                var data = await FPWorkbenchCsvDataset.ImportAsync(path, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile(), preview: reports);
                Assert.That(reports.Count, Is.EqualTo(1)); Assert.That(reports.Value.Rows.Count, Is.EqualTo(32));
                Assert.That(reports.Value.Headers.Count, Is.EqualTo(32)); Assert.That(reports.Value.SourceColumnCount, Is.EqualTo(34));
                Assert.That(data.RowCount, Is.EqualTo(40)); Assert.That(data.ColumnCount, Is.EqualTo(34));
                var tail = await data.ReadPageAsync(39, 1, 33, 1); Assert.That(tail.Values[0][0], Is.EqualTo("3933"));
            } finally { Directory.Delete(root, true); }
        }
        [Test] public async Task PreviewTextLimitKeepsWholeRowsAndFullImportContinues()
        {
            string root = Path.Combine(Path.GetTempPath(), "FPPreviewText-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try {
                string path = Path.Combine(root, "source.csv"); string value = new string('a', 300000);
                File.WriteAllText(path, "x,text\n0," + value + "\n1," + value + "\n"); var reports = new Reports();
                var data = await FPWorkbenchCsvDataset.ImportAsync(path, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile(), preview: reports);
                Assert.That(reports.Value.TextLimitReached, Is.True); Assert.That(reports.Value.Rows.Count, Is.EqualTo(1));
                Assert.That(reports.Value.Rows[0][1], Is.EqualTo(value)); Assert.That(data.RowCount, Is.EqualTo(2));
                var page = await data.ReadPageAsync(1, 1, 1, 1); Assert.That(page.Values[0][0], Is.EqualTo(value));
            } finally { Directory.Delete(root, true); }
        }
    }
}
