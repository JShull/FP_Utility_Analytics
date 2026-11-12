// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using UnityEngine;

    public static class FPWorkbenchChangesFile
    {
        [Serializable] private sealed class Document
        {
            public string Format = "FuzzPhyte.DataWorkbench.Changes";
            public int Version = 1;
            public List<FPWorkbenchChangeSet> Sources = new List<FPWorkbenchChangeSet>();
        }
        [Serializable] private struct Marker { public string Format; public int Version; }
        public static Task SaveAsync(IReadOnlyList<FPWorkbenchChangeSet> changes, string path,
            IReadOnlyList<FPWorkbenchCsvDataset> protectedSources, CancellationToken cancellation = default)
        {
            string target = Path.GetFullPath(path);
            if (!target.EndsWith(".fpchanges", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Use a .fpchanges filename.");
            if (protectedSources == null) throw new ArgumentNullException(nameof(protectedSources));
            foreach (var data in protectedSources)
            {
                var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                string cache = Path.GetFullPath(data.CacheDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (string.Equals(target, Path.GetFullPath(data.SourcePath), comparison) || target.StartsWith(cache, comparison)) throw new IOException("Save changes outside sources and caches.");
            }
            var document = new Document(); foreach (var source in changes) document.Sources.Add(source);
            Validate(document);
            byte[] bytes = new UTF8Encoding(false, true).GetBytes(JsonUtility.ToJson(document, true));
            if (bytes.Length > FPWorkbenchDocumentFile.MaximumDocumentBytes) throw new InvalidDataException("Change metadata exceeds 4 MiB.");
            return FPWorkbenchDocumentFile.WriteAtomicAsync(bytes, target, cancellation);
        }
        public static Task<List<FPWorkbenchChangeSet>> LoadAsync(string path, IReadOnlyList<FPWorkbenchCsvDataset> datasets, CancellationToken cancellation = default)
        {
            string source = Path.GetFullPath(path);
            if (datasets == null) throw new ArgumentNullException(nameof(datasets));
            var registered = new Dictionary<string, FPWorkbenchCsvDataset>(); foreach (var data in datasets) registered[data.DatasetId] = data;
            return Task.Run(async () =>
            {
                cancellation.ThrowIfCancellationRequested();
                using var input = File.OpenRead(source);
                if (input.Length > FPWorkbenchDocumentFile.MaximumDocumentBytes) throw new InvalidDataException("Change metadata exceeds 4 MiB.");
                var bytes = new byte[(int)input.Length]; int read = 0;
                while (read < bytes.Length) { cancellation.ThrowIfCancellationRequested(); int count = input.Read(bytes, read, bytes.Length - read); if (count == 0) throw new EndOfStreamException(); read += count; }
                Document document;
                try
                {
                    string json = new UTF8Encoding(false, true).GetString(bytes); var marker = JsonUtility.FromJson<Marker>(json);
                    if (marker.Format != "FuzzPhyte.DataWorkbench.Changes" || marker.Version != 1) throw new InvalidDataException("Not a supported correction document.");
                    document = JsonUtility.FromJson<Document>(json);
                }
                catch (ArgumentException ex) { throw new InvalidDataException("Invalid correction JSON.", ex); }
                Validate(document);
                foreach (var changes in document.Sources)
                {
                    if (!registered.TryGetValue(changes.DatasetId, out var data)) throw new InvalidDataException("A correction source revision is not open; import its original data first.");
                    await changes.ValidateSourceAsync(data, cancellation).ConfigureAwait(false);
                }
                return document.Sources;
            }, cancellation);
        }
        private static void Validate(Document document)
        {
            if (document == null || document.Version != 1 || document.Format != "FuzzPhyte.DataWorkbench.Changes" || document.Sources == null || document.Sources.Count > 64) throw new InvalidDataException("Invalid correction document.");
            var ids = new HashSet<string>();
            foreach (var source in document.Sources) { if (source == null || !ids.Add(source.DatasetId)) throw new InvalidDataException("Duplicate correction source."); source.Validate(); }
        }
    }
}
