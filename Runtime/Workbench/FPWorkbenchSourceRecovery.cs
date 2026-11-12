// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using UnityEngine;

    /// <summary>Relinks one original revision in a detached workbook snapshot. Never substitutes changed measurements.</summary>
    public static class FPWorkbenchSourceRecovery
    {
        public static async Task<FPWorkbenchDocument> RelinkAsync(FPWorkbenchDocument document, string datasetId,
            string replacementCsv, string cacheParent, CancellationToken cancellation = default)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            var copy = JsonUtility.FromJson<FPWorkbenchDocument>(JsonUtility.ToJson(document)); copy.Validate();
            var source = copy.Sources.Find(s => s.DatasetId == datasetId) ?? throw new ArgumentException("Source revision is not registered.");
            string target = Path.Combine(Path.GetFullPath(cacheParent), "relinked-" + Guid.NewGuid().ToString("N"));
            bool imported = false, accepted = false;
            try {
                var data = await FPWorkbenchCsvDataset.ImportAsync(replacementCsv, target, source.Import, cancellation).ConfigureAwait(false); imported = true;
                if (data.DatasetId != source.DatasetId) throw new InvalidDataException("Replacement CSV differs from the saved recording. Relinking cannot retarget annotations to changed data.");
                var program = source.CompileProgram(data);
                if (source.HasCalculations) source.Calculations = program.Definition;
                foreach (var changes in copy.Changes) if (changes.DatasetId == datasetId) {
                    await changes.ValidateSourceAsync(data, cancellation).ConfigureAwait(false);
                    if (source.HasTypedSchema) source.TypedSchema.Compile(data).ValidateChanges(changes);
                }
                foreach (var figure in copy.Figures) {
                    if (figure.DatasetId == datasetId && figure.CorrectionSnapshot != null) await figure.CorrectionSnapshot.ValidateSourceAsync(data, cancellation).ConfigureAwait(false);
                    if (figure.Kind == FPWorkbenchFigureKind.AlignedScatter) {
                        var recipe = figure.AlignedRecipe;
                        if (recipe.LeftDatasetId == datasetId && recipe.HasLeftCorrections) await recipe.LeftCorrections.ValidateSourceAsync(data, cancellation).ConfigureAwait(false);
                        if (recipe.RightDatasetId == datasetId && recipe.HasRightCorrections) await recipe.RightCorrections.ValidateSourceAsync(data, cancellation).ConfigureAwait(false);
                    }
                }
                cancellation.ThrowIfCancellationRequested(); source.SourcePath = data.SourcePath; source.CacheDirectory = data.CacheDirectory;
                foreach (var sheet in copy.Sheets) if (sheet.DatasetId == datasetId) { sheet.SourcePath = data.SourcePath; sheet.CacheDirectory = data.CacheDirectory; }
                copy.Validate(); accepted = true; return copy;
            }
            finally {
                // Only a fresh directory successfully created by this operation is eligible for failure cleanup.
                if (imported && !accepted && Directory.Exists(target)) Directory.Delete(target, true);
            }
        }
    }
}
