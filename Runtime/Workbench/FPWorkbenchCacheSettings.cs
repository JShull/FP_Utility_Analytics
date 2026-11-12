// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using UnityEngine;

    /// <summary>Small local JSON configuration. Runtime callers may instead pass an explicit root and quota to the store.</summary>
    [Serializable] public sealed class FPWorkbenchCacheSettings
    {
        public int Version = 1;
        public string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FuzzPhyte", "Analytics", "Workbench", "ManagedCache");
        public long QuotaBytes = 4L * 1024 * 1024 * 1024;
        public static string LocalSettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FuzzPhyte", "Analytics", "Workbench", "cache-settings.json");
        public void Validate()
        {
            if (Version != 1 || string.IsNullOrWhiteSpace(Root) || Root.Length > 8192 || QuotaBytes < 1024 * 1024)
                throw new InvalidDataException("Cache settings require a dedicated root and a quota of at least 1 MiB.");
            string full = Path.GetFullPath(Root);
            if (!Path.IsPathRooted(Root) || full.TrimEnd(Path.DirectorySeparatorChar) == Path.GetPathRoot(full).TrimEnd(Path.DirectorySeparatorChar))
                throw new InvalidDataException("Choose an absolute dedicated cache folder, not a drive root.");
            foreach (string segment in full.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                if (string.Equals(segment, "Assets", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The managed cache root must be outside Unity Assets.");
            Root = full;
        }
        public static Task<FPWorkbenchCacheSettings> LoadAsync(string path, CancellationToken cancellation = default)
        {
            string file = Path.GetFullPath(path);
            return Task.Run(() => {
                cancellation.ThrowIfCancellationRequested();
                if (!File.Exists(file)) return new FPWorkbenchCacheSettings();
                if (new FileInfo(file).Length > 65536) throw new InvalidDataException("Cache settings exceed 64 KiB.");
                var settings = JsonUtility.FromJson<FPWorkbenchCacheSettings>(File.ReadAllText(file, new UTF8Encoding(false, true)));
                if (settings == null) throw new InvalidDataException("Cache settings are empty.");
                settings.Validate(); return settings;
            }, cancellation);
        }
        public Task SaveAsync(string path, CancellationToken cancellation = default)
        {
            Validate(); byte[] bytes = new UTF8Encoding(false, true).GetBytes(JsonUtility.ToJson(this, true));
            return FPWorkbenchDocumentFile.WriteAtomicAsync(bytes, Path.GetFullPath(path), cancellation);
        }
    }
}
