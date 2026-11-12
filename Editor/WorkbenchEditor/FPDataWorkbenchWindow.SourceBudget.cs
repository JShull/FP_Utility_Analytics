// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.Threading.Tasks;

    public sealed partial class FPDataWorkbenchWindow
    {
        private static bool IsLoading(Sheet sheet) => sheet != null && (sheet.Loading != null || sheet.PendingLoading != null);
        private void QueueSheetLoad(Sheet sheet, Func<Task<FPWorkbenchCsvDataset>> request, FPWorkbenchCsvProfile profile)
        {
            sheet.Import = profile.Copy();
            sheet.LoadBytes = FPWorkbenchEditorResources.ProfileBytes(profile, 8L * 1024 * 1024);
            sheet.PendingLoading = request; TryStartSheetLoad(sheet);
        }
        private void TryStartSheetLoad(Sheet sheet)
        {
            try {
                sheet.WaitingForLoad = !FPWorkbenchEditorResources.TryStartScan(sheet.LoadBytes, ref sheet.PendingLoading, ref sheet.Loading, ref sheet.LoadMemory);
            } catch (Exception ex) { sheet.WaitingForLoad = false; error = ex.Message; }
        }
        private static void CancelSheetLoad(Sheet sheet)
        {
            ClearImportPreview(sheet);
            Cancel(sheet.Lifetime); sheet.Lifetime = null;
            if (sheet.LoadMemory != null) sheet.LoadMemory.ReleaseAfter(sheet.Loading); else Observe(sheet.Loading);
            sheet.LoadMemory = null; sheet.PendingLoading = null; sheet.Loading = null; sheet.WaitingForLoad = false;
        }
        private void QueueSourceOpening(FPWorkbenchSourceDocument source)
        {
            var cache = source.CacheDirectory; var token = sourceCancellation.Token;
            sourceOpeningBytes = FPWorkbenchEditorResources.ProfileBytes(source.Import, 8L * 1024 * 1024);
            pendingSourceOpening = () => FPWorkbenchCsvDataset.OpenAsync(cache, token); TryStartSourceOpening();
        }
        private void TryStartSourceOpening()
        {
            try {
                if (!FPWorkbenchEditorResources.TryStartScan(sourceOpeningBytes, ref pendingSourceOpening, ref sourceOpening, ref sourceOpeningMemory))
                    status = "Source verification is waiting for shared memory/background capacity…";
            } catch (Exception ex) { error = ex.Message; }
        }
    }
}
