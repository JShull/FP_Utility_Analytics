// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using UnityEditor;

    public sealed partial class FPDataWorkbenchWindow
    {
        // Shared explicitly across Workbench windows in this domain; no source objects or page contents are stored here.
        private static readonly FPWorkbenchMemoryBudget pageMemory = FPWorkbenchEditorResources.Memory;
        private FPWorkbenchMemoryBudget.Reservation pendingRawMemory, rawMemory, pendingVirtualMemory, virtualMemory;
        private const long MaximumPagePayload = 8L * 1024 * 1024;
        private static bool AdmitPage(FPWorkbenchCsvDataset data, out FPWorkbenchMemoryBudget.Reservation reservation)
        {
            // Accounted estimate includes UTF-16 output, cell structures and parser/record scratch, not process heap/GC residency.
            long maximum = checked(MaximumPagePayload + (long)data.ImportProfile.MaximumRecordBytes * 8 + data.ColumnCount * 128L);
            return pageMemory.TryAcquire(maximum, out reservation);
        }
        private static long RetainedPageBytes(long characters, int rows, int columns, bool typed)
            => checked(4096L + characters * 2 + rows * 64L + (long)rows * columns * (typed ? 256 : 40));
        private static void ReleaseAfter(Task task, FPWorkbenchMemoryBudget.Reservation reservation)
        {
            if (reservation == null) { Observe(task); return; }
            reservation.ReleaseAfter(task);
        }
        private void AbandonRawPage()
        {
            Cancel(pageCancel); pageCancel = null;
            ReleaseAfter(pageWork, pendingRawMemory); pageWork = null; pendingRawMemory = null;
            rawMemory?.Dispose(); rawMemory = null; page = null;
        }
        private void DrawPageBudget()
        {
            EditorGUILayout.LabelField("View reservations", (pageMemory.UsedBytes / 1048576d).ToString("F1") + " / " + (pageMemory.LimitBytes / 1048576) + " MiB");
            EditorGUILayout.LabelField("Admitted reads", pageMemory.ActiveWorkers + " / " + pageMemory.MaximumWorkers);
            EditorGUILayout.LabelField("Background jobs", FPWorkbenchEditorResources.Scans.ActiveWorkers + " / " + FPWorkbenchEditorResources.Scans.MaximumWorkers);
        }
    }
}
