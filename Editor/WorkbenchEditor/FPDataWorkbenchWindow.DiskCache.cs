// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.Threading.Tasks;
    using UnityEditor;
    using UnityEngine;

    public sealed partial class FPDataWorkbenchWindow
    {
        [SerializeField] private bool showDiskCache;
        [SerializeField] private string cacheRoot;
        [SerializeField] private double cacheQuotaGiB = 4;
        private Task<long> measuringCache;
        private Task<FPWorkbenchDiskCacheStore.TrimResult> trimmingCache;
        private Task applyingCache;
        private long measuredCacheBytes = -1;
        private string cacheFeedback;
        private bool TickDiskCache()
        {
            bool changed = false;
            if (measuringCache?.IsCompleted == true) {
                if (measuringCache.Status == TaskStatus.RanToCompletion) measuredCacheBytes = measuringCache.Result;
                else if (measuringCache.IsFaulted) cacheFeedback = measuringCache.Exception.GetBaseException().Message;
                measuringCache = null; changed = true;
            }
            if (trimmingCache?.IsCompleted == true) {
                if (trimmingCache.Status == TaskStatus.RanToCompletion) {
                    var result = trimmingCache.Result; measuredCacheBytes = result.RemainingBytes;
                    cacheFeedback = "Removed " + result.RemovedEntries + " unused entries (" + EditorUtility.FormatBytes(result.ReclaimedBytes) + ")." +
                        (result.WithinTarget ? "" : " Active, pending or protected content was retained. Close views/finish workers, increase the quota, or choose another dedicated root.");
                } else if (trimmingCache.IsFaulted) cacheFeedback = trimmingCache.Exception.GetBaseException().Message;
                trimmingCache = null; changed = true;
            }
            if (applyingCache?.IsCompleted == true) {
                if (applyingCache.Status == TaskStatus.RanToCompletion) { cacheFeedback = "Cache settings saved and applied."; measuredCacheBytes = -1; }
                else if (applyingCache.IsFaulted) cacheFeedback = applyingCache.Exception.GetBaseException().Message;
                applyingCache = null; changed = true;
            }
            return changed;
        }
        private void DrawDiskCacheControls()
        {
            showDiskCache = EditorGUILayout.Foldout(showDiskCache, "Disk cache", true);
            if (!showDiskCache) return;
            var settings = FPWorkbenchEditorCache.Settings;
            if (string.IsNullOrEmpty(cacheRoot)) { cacheRoot = settings?.Root ?? new FPWorkbenchCacheSettings().Root; cacheQuotaGiB = (settings?.QuotaBytes ?? 4L * 1024 * 1024 * 1024) / (double)(1024L * 1024 * 1024); }
            cacheRoot = EditorGUILayout.TextField("Dedicated root", cacheRoot);
            if (GUILayout.Button("Choose cache folder…")) {
                string selected = EditorUtility.OpenFolderPanel("Dedicated cache folder outside Assets", cacheRoot, "");
                if (!string.IsNullOrEmpty(selected)) cacheRoot = selected;
            }
            cacheQuotaGiB = EditorGUILayout.DoubleField("Quota (GiB)", cacheQuotaGiB);
            EditorGUILayout.LabelField("Used (measured)", measuredCacheBytes < 0 ? "Refresh usage" : EditorUtility.FormatBytes(measuredCacheBytes));
            EditorGUILayout.LabelField("Write reservations", EditorUtility.FormatBytes(FPWorkbenchEditorCache.ReservedBytes));
            if (settings != null) EditorGUILayout.LabelField(new GUIContent("Active root: " + settings.Root, settings.Root), EditorStyles.miniLabel);
            if (settings != null && measuredCacheBytes > settings.QuotaBytes)
                EditorGUILayout.HelpBox("Measured cache usage exceeds the active quota. Trim unused entries, close views/finish workers, increase the quota, or choose another root. Protected content will be retained.", MessageType.Warning);
            using (new EditorGUI.DisabledScope(measuringCache != null || trimmingCache != null || applyingCache != null)) {
                using (new EditorGUILayout.HorizontalScope()) {
                    if (GUILayout.Button("Refresh usage")) measuringCache = FPWorkbenchEditorCache.MeasureAsync();
                    if (GUILayout.Button("Trim unused")) trimmingCache = FPWorkbenchEditorCache.TrimAsync();
                }
                if (GUILayout.Button("Apply root / quota")) {
                    try {
                        if (double.IsNaN(cacheQuotaGiB) || double.IsInfinity(cacheQuotaGiB) || cacheQuotaGiB <= 0 || cacheQuotaGiB > long.MaxValue / (double)(1024L * 1024 * 1024)) throw new ArgumentException("Enter a finite positive quota.");
                        var candidate = new FPWorkbenchCacheSettings { Root = cacheRoot, QuotaBytes = checked((long)(cacheQuotaGiB * 1024 * 1024 * 1024)) };
                        candidate.Validate(); applyingCache = FPWorkbenchEditorCache.ApplyAsync(candidate);
                    } catch (Exception ex) { cacheFeedback = ex.Message; }
                }
            }
            EditorGUILayout.HelpBox("Only explicitly owned generated caches can be evicted. Legacy caches, originals, workbooks and exports are protected. Reservations are estimates; neither value measures process RAM.", MessageType.Info);
            if (!string.IsNullOrEmpty(cacheFeedback)) EditorGUILayout.HelpBox(cacheFeedback, MessageType.Info);
        }
    }
}
