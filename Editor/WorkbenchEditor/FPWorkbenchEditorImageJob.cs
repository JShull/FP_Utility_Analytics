// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using UnityEditor;
    using UnityEngine;

    /// <summary>Main-thread capture/status adapter. Encoding/publication and admission belong to the runtime worker.</summary>
    internal sealed class FPWorkbenchEditorImageJob : IDisposable
    {
        private Action repaint;
        private CancellationTokenSource cancellation;
        private Task<FPWorkbenchImageExportResult> work;
        private string status;
        private bool disposed;
        private double captureMilliseconds;
        private FPWorkbenchImageExportResult result;
        internal bool IsBusy => work != null;
        internal CancellationToken Token => cancellation?.Token ?? CancellationToken.None;
        public FPWorkbenchEditorImageJob(Action repaint) { this.repaint = repaint; }
        internal void Start(Func<Task<FPWorkbenchImageExportResult>> capture)
        {
            if (disposed) return;
            if (work != null || FPWorkbenchImageExport.IsActive) { status = "Another image export is active; wait or cancel it first."; repaint?.Invoke(); return; }
            cancellation = new CancellationTokenSource(); result = null;
            try {
                var clock = Stopwatch.StartNew(); work = capture(); captureMilliseconds = clock.Elapsed.TotalMilliseconds;
                if (work == null) throw new InvalidOperationException("Image capture did not return an export task.");
                status = "Encoding/publishing image in background…";
                EditorApplication.update += Tick; AssemblyReloadEvents.beforeAssemblyReload += Dispose;
            }
            catch (Exception ex) { status = ex.Message; work = null; cancellation.Dispose(); cancellation = null; }
            repaint?.Invoke();
        }
        private void Tick()
        {
            if (work == null || !work.IsCompleted) return;
            try {
                result = work.GetAwaiter().GetResult();
                status = "Image exported • capture " + captureMilliseconds.ToString("F1") + " ms • encode " + result.EncodeMilliseconds.ToString("F1") + " ms • publish " + result.PublishMilliseconds.ToString("F1") + " ms";
            }
            catch (OperationCanceledException) { status = "Image export cancelled; no pending output was published."; }
            catch (Exception ex) { status = "Image export failed: " + ex.Message; }
            Detach(); work = null; cancellation.Dispose(); cancellation = null; repaint?.Invoke();
        }
        internal void Cancel() { cancellation?.Cancel(); status = "Cancelling image export after the current encoder step…"; repaint?.Invoke(); }
        internal void Draw()
        {
            if (!string.IsNullOrEmpty(status)) EditorGUILayout.LabelField(status, EditorStyles.wordWrappedMiniLabel);
            if (IsBusy && GUILayout.Button("Cancel image export")) Cancel();
            if (!IsBusy && result != null && GUILayout.Button("Show exported image folder")) EditorUtility.RevealInFinder(result.Directory);
        }
        private void Detach() { EditorApplication.update -= Tick; AssemblyReloadEvents.beforeAssemblyReload -= Dispose; }
        public void Dispose()
        {
            if (disposed) return; disposed = true; cancellation?.Cancel(); Detach();
            if (work != null) work.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            work = null; cancellation?.Dispose(); cancellation = null; repaint = null;
        }
    }
}
