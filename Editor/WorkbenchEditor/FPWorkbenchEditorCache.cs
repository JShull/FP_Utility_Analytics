// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using UnityEditor;
    using UnityEngine;

    /// <summary>Explicit domain lifetime for the local cache store and source-admission handoffs.</summary>
    internal static class FPWorkbenchEditorCache
    {
        private static readonly object gate = new object();
        private static readonly Dictionary<FPWorkbenchCsvDataset, IDisposable> pending = new Dictionary<FPWorkbenchCsvDataset, IDisposable>();
        private static readonly List<Action> clients = new List<Action>();
        private static readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private static Task<FPWorkbenchManagedCache> opening;
        private static FPWorkbenchDiskCacheStore store;
        private static bool closing;
        internal static FPWorkbenchCacheSettings Settings { get; private set; }
        internal static long ReservedBytes => store?.ReservedBytes ?? 0;
        static FPWorkbenchEditorCache()
        { AssemblyReloadEvents.beforeAssemblyReload += Shutdown; EditorApplication.quitting += Shutdown; }
        internal static void Register(Action close) { lock (gate) if (!clients.Contains(close)) clients.Add(close); }
        internal static void Unregister(Action close) { lock (gate) clients.Remove(close); }
        internal static Task<FPWorkbenchManagedCache> Ready()
        {
            lock (gate) {
                if (closing) throw new OperationCanceledException("Workbench cache is closing for Editor reload.");
                return opening ??= Initialize();
            }
        }
        private static async Task<FPWorkbenchManagedCache> Initialize()
        {
            var settings = await FPWorkbenchCacheSettings.LoadAsync(FPWorkbenchCacheSettings.LocalSettingsPath, lifetime.Token).ConfigureAwait(false);
            settings.Validate();
            var opened = await FPWorkbenchDiskCacheStore.OpenAsync(settings.Root, settings.QuotaBytes, lifetime.Token).ConfigureAwait(false);
            lock (gate) { store = opened; Settings = settings; }
            return new FPWorkbenchManagedCache(opened);
        }
        private static FPWorkbenchCsvDataset Offer(FPWorkbenchManagedCache.SourceLease lease)
        {
            lock (gate) {
                if (closing) { lease.Dispose(); throw new OperationCanceledException("Source admission was cancelled by Editor reload."); }
                pending.Add(lease.Dataset, lease); return lease.Dataset;
            }
        }
        internal static IDisposable Retain(FPWorkbenchCsvDataset data)
        {
            try { return data.RetainCache(); }
            finally { ReleaseAdmission(data); }
        }
        internal static void HoldResult(FPWorkbenchCsvDataset data)
        {
            var pin = data.RetainCache(); if (pin == null) return;
            lock (gate) {
                if (closing) { pin.Dispose(); throw new OperationCanceledException("Cache result was cancelled by Editor reload."); }
                if (pending.ContainsKey(data)) pin.Dispose(); else pending.Add(data, pin);
            }
        }
        internal static void ReleaseAdmission(FPWorkbenchCsvDataset data)
        {
            if (data == null) return;
            lock (gate) if (pending.TryGetValue(data, out var lease)) { pending.Remove(data); lease.Dispose(); }
        }
        internal static void Abandon(Task<FPWorkbenchCsvDataset> task)
        {
            if (task == null) return;
            _ = task.ContinueWith(t => { if (t.Status == TaskStatus.RanToCompletion) ReleaseAdmission(t.Result); else _ = t.Exception; }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        internal static void Abandon(Task<FPWorkbenchLoadedDocument> task)
        {
            if (task == null) return;
            _ = task.ContinueWith(t => { if (t.Status == TaskStatus.RanToCompletion) t.Result.Dispose(); else _ = t.Exception; }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        internal static void Abandon(Task<FPWorkbenchCsvDataset[]> task)
        {
            if (task == null) return;
            _ = task.ContinueWith(t => { if (t.Status == TaskStatus.RanToCompletion) foreach (var data in t.Result) ReleaseAdmission(data); else _ = t.Exception; }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        internal static void Abandon(Task<FPWorkbenchAlignedValues> task)
        {
            if (task == null) return;
            _ = task.ContinueWith(t => { if (t.Status == TaskStatus.RanToCompletion) ReleaseAdmission(t.Result.Dataset); else _ = t.Exception; }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        internal static async Task<FPWorkbenchCsvDataset> ImportAsync(string path, FPWorkbenchCsvProfile profile, CancellationToken token, IProgress<double> progress, IProgress<FPWorkbenchCsvPreview> preview)
        {
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
            var cache = await Ready().ConfigureAwait(false);
            return Offer(await cache.ImportAsync(path, profile, cancel.Token, progress, preview).ConfigureAwait(false));
        }
        internal static async Task<FPWorkbenchCsvDataset> OpenAsync(string directory, CancellationToken token)
        {
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
            var cache = await Ready().ConfigureAwait(false);
            return Offer(await cache.OpenExistingAsync(directory, cancel.Token).ConfigureAwait(false));
        }
        internal static async Task<FPWorkbenchCsvDataset> OpenAsync(FPWorkbenchSourceDocument source, CancellationToken token)
        {
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
            var cache = await Ready().ConfigureAwait(false);
            return Offer(await cache.OpenAsync(source, cancel.Token).ConfigureAwait(false));
        }
        internal static async Task<FPWorkbenchLoadedDocument> LoadWorkbookAsync(string path, CancellationToken token)
        {
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
            var cache = await Ready().ConfigureAwait(false);
            var loaded = await FPWorkbenchDocumentFile.LoadAsync(path, cancel.Token, cache).ConfigureAwait(false);
            if (cancel.IsCancellationRequested) { loaded.Dispose(); cancel.Token.ThrowIfCancellationRequested(); }
            return loaded;
        }
        internal static async Task<FPWorkbenchAlignedValues> HoldAlignedAsync(Func<Task<FPWorkbenchAlignedValues>> request, FPWorkbenchCsvDataset left, FPWorkbenchCsvDataset right)
        {
            using var a = left.RetainCache(); using var b = right.RetainCache();
            var result = await request().ConfigureAwait(false); HoldResult(result.Dataset); return result;
        }
        internal static async Task<long> MeasureAsync()
        { await Ready().ConfigureAwait(false); return await store.MeasureAsync(lifetime.Token).ConfigureAwait(false); }
        internal static async Task<FPWorkbenchDiskCacheStore.TrimResult> TrimAsync()
        { await Ready().ConfigureAwait(false); return await store.TrimAsync(0, lifetime.Token).ConfigureAwait(false); }
        internal static async Task ApplyAsync(FPWorkbenchCacheSettings settings)
        {
            settings.Validate();
            Task<FPWorkbenchManagedCache> previous, reconfiguration;
            lock (gate) {
                if (closing) throw new OperationCanceledException();
                previous = opening; reconfiguration = Reconfigure(previous, settings); opening = reconfiguration;
            }
            try { await reconfiguration.ConfigureAwait(false); }
            catch { lock (gate) if (ReferenceEquals(opening, reconfiguration) && store != null) opening = previous; throw; }
        }
        private static async Task<FPWorkbenchManagedCache> Reconfigure(Task<FPWorkbenchManagedCache> previous, FPWorkbenchCacheSettings settings)
        {
            // Ready returns this task while reconfiguration runs, preventing new admission into a closing store.
            if (previous != null) { try { await previous.ConfigureAwait(false); } catch { /* A corrected root can recover failed initialization. */ } }
            if (store != null) {
                if (!store.IsIdle) {
                    throw new IOException("Close all table and figure views and wait for workers before changing the cache root/quota. Then retry Apply.");
                }
                await store.FlushAsync(lifetime.Token).ConfigureAwait(false); store.Dispose(); store = null;
            }
            var opened = await FPWorkbenchDiskCacheStore.OpenAsync(settings.Root, settings.QuotaBytes, lifetime.Token).ConfigureAwait(false);
            try { await settings.SaveAsync(FPWorkbenchCacheSettings.LocalSettingsPath, lifetime.Token).ConfigureAwait(false); }
            catch { opened.Dispose(); throw; }
            lock (gate) { store = opened; Settings = settings; }
            return new FPWorkbenchManagedCache(opened);
        }
        private static void Shutdown()
        {
            Action[] snapshot;
            lock (gate) { if (closing) return; closing = true; snapshot = clients.ToArray(); }
            lifetime.Cancel();
            foreach (var close in snapshot) try { close(); } catch (Exception ex) { Debug.LogException(ex); }
            lock (gate) { foreach (var lease in pending.Values) lease.Dispose(); pending.Clear(); }
            try {
                if (opening != null) { try { opening.GetAwaiter().GetResult(); } catch (OperationCanceledException) { } }
                if (store == null) return;
                var limit = DateTime.UtcNow.AddSeconds(10);
                while (!store.IsIdle && DateTime.UtcNow < limit) Task.Delay(10).GetAwaiter().GetResult();
                if (!store.IsIdle) throw new InvalidOperationException("Workbench cache shutdown still has active leases after cancellation.");
                store.FlushAsync().GetAwaiter().GetResult(); store.Dispose(); store = null;
            } catch (Exception ex) { Debug.LogError("Workbench cache shutdown: " + ex.Message); }
        }
    }
}
