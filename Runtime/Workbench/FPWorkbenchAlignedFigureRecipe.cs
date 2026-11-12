// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using UnityEngine;

    /// <summary>Regeneratable aligned XY recipe referencing original sources, not transient derived CSV/index paths.</summary>
    [Serializable] public sealed class FPWorkbenchAlignedFigureRecipe
    {
        public int Version = 1;
        public string LeftDatasetId, RightDatasetId, LeftValueColumnId, RightValueColumnId;
        public FPWorkbenchAlignmentOptions Alignment = new FPWorkbenchAlignmentOptions();
        public FPWorkbenchExportInvalidPolicy InvalidValues;
        public bool HasLeftCalculations, HasRightCalculations, HasLeftCorrections, HasRightCorrections;
        public FPWorkbenchFormulaSet LeftCalculations, RightCalculations;
        public FPWorkbenchChangeSet LeftCorrections, RightCorrections;
        public static FPWorkbenchAlignedFigureRecipe FromValues(FPWorkbenchAlignedValuesMetadata values)
        {
            if (values == null || values.Alignment == null) throw new ArgumentNullException(nameof(values));
            var result = new FPWorkbenchAlignedFigureRecipe { LeftDatasetId = values.Alignment.LeftDatasetId, RightDatasetId = values.Alignment.RightDatasetId,
                LeftValueColumnId = values.LeftColumnId, RightValueColumnId = values.RightColumnId, Alignment = values.Alignment.Options, InvalidValues = values.InvalidPolicy,
                HasLeftCalculations = values.HasLeftCalculations, HasRightCalculations = values.HasRightCalculations,
                LeftCalculations = values.LeftCalculations, RightCalculations = values.RightCalculations,
                HasLeftCorrections = values.Alignment.HasLeftCorrectionSnapshot, HasRightCorrections = values.Alignment.HasRightCorrectionSnapshot,
                LeftCorrections = values.Alignment.LeftCorrections, RightCorrections = values.Alignment.RightCorrections };
            result = JsonUtility.FromJson<FPWorkbenchAlignedFigureRecipe>(JsonUtility.ToJson(result)); result.Validate(); return result;
        }
        public void Validate()
        {
            if (Version != 1 || string.IsNullOrEmpty(LeftDatasetId) || LeftDatasetId.Length > 256 || string.IsNullOrEmpty(RightDatasetId) || RightDatasetId.Length > 256 ||
                string.IsNullOrEmpty(LeftValueColumnId) || LeftValueColumnId.Length > 512 || string.IsNullOrEmpty(RightValueColumnId) || RightValueColumnId.Length > 512 ||
                Alignment == null || !Enum.IsDefined(typeof(FPWorkbenchExportInvalidPolicy), InvalidValues)) throw new InvalidDataException("Invalid aligned figure recipe.");
            try { Alignment = Alignment.Copy(); }
            catch (ArgumentException ex) { throw new InvalidDataException("Invalid aligned figure key policy.", ex); }
            LeftCalculations = Calculations(HasLeftCalculations, LeftCalculations, LeftDatasetId);
            RightCalculations = Calculations(HasRightCalculations, RightCalculations, RightDatasetId);
            LeftCorrections = Corrections(HasLeftCorrections, LeftCorrections, LeftDatasetId);
            RightCorrections = Corrections(HasRightCorrections, RightCorrections, RightDatasetId);
            Resolve(LeftDatasetId, 4096, LeftValueColumnId, HasLeftCalculations ? FPWorkbenchFormulaProgram.CompileSchema(LeftDatasetId, 4096, LeftCalculations) : null);
            Resolve(RightDatasetId, 4096, RightValueColumnId, HasRightCalculations ? FPWorkbenchFormulaProgram.CompileSchema(RightDatasetId, 4096, RightCalculations) : null);
        }
        private static FPWorkbenchFormulaSet Calculations(bool present, FPWorkbenchFormulaSet definition, string id)
        {
            if (!present)
            { if (definition != null && !string.IsNullOrEmpty(definition.DatasetId)) throw new InvalidDataException("Aligned calculation presence contradicts its snapshot."); return null; }
            if (definition == null || definition.DatasetId != id) throw new InvalidDataException("Aligned calculation snapshot is missing or foreign.");
            try { return FPWorkbenchFormulaProgram.CompileSchema(id, 4096, definition).Definition; }
            catch (ArgumentException ex) { throw new InvalidDataException("Invalid aligned calculation snapshot.", ex); }
        }
        private static FPWorkbenchChangeSet Corrections(bool present, FPWorkbenchChangeSet changes, string id)
        {
            if (!present)
            { if (changes != null && !string.IsNullOrEmpty(changes.DatasetId)) throw new InvalidDataException("Aligned correction presence contradicts its snapshot."); return null; }
            if (changes == null || changes.DatasetId != id) throw new InvalidDataException("Aligned correction snapshot is missing or foreign."); changes.Validate(); return changes;
        }
        private static int Resolve(string dataset, int physicalCount, string id, FPWorkbenchFormulaProgram program)
        {
            string prefix = dataset + ":column:";
            if (id.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(id.Substring(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int column) && column >= 0 && column < physicalCount) return column;
            if (program != null) for (int c = 0; c < program.ColumnCount; c++) if (id == dataset + ":derived:" + program.ColumnId(c)) return physicalCount + c;
            throw new InvalidDataException("Aligned value column identity is unresolved: " + id);
        }
        public void ValidateSources(FPWorkbenchCsvDataset left, FPWorkbenchCsvDataset right)
        {
            Validate();
            if (left == null || right == null || left.DatasetId != LeftDatasetId || right.DatasetId != RightDatasetId) throw new InvalidDataException("Aligned source revision is missing or differs.");
            if (Alignment.Mode == FPWorkbenchAlignmentMode.SharedSourceRows)
            {
                if (left.DatasetId != right.DatasetId || left.RowCount != right.RowCount || left.ColumnCount != right.ColumnCount || JsonUtility.ToJson(left.ImportProfile) != JsonUtility.ToJson(right.ImportProfile)) throw new InvalidDataException("Shared rows require identical source revisions and interpretations.");
            }
            else if (Alignment.LeftKeyColumn < 0 || Alignment.LeftKeyColumn >= left.ColumnCount || Alignment.RightKeyColumn < 0 || Alignment.RightKeyColumn >= right.ColumnCount) throw new InvalidDataException("Aligned key exceeds the actual source schema.");
            try
            {
                Resolve(LeftDatasetId, left.ColumnCount, LeftValueColumnId, HasLeftCalculations ? FPWorkbenchFormulaProgram.Compile(left, LeftCalculations) : null);
                Resolve(RightDatasetId, right.ColumnCount, RightValueColumnId, HasRightCalculations ? FPWorkbenchFormulaProgram.Compile(right, RightCalculations) : null);
            }
            catch (ArgumentException ex) { throw new InvalidDataException("Aligned definitions differ from the actual source schema.", ex); }
        }
        public async Task<FPWorkbenchAlignedValues> BuildAsync(FPWorkbenchCsvDataset left, FPWorkbenchCsvDataset right, CancellationToken cancellation = default)
        {
            var recipe = JsonUtility.FromJson<FPWorkbenchAlignedFigureRecipe>(JsonUtility.ToJson(this)); recipe.ValidateSources(left, right);
            var lp = recipe.HasLeftCalculations ? FPWorkbenchFormulaProgram.Compile(left, recipe.LeftCalculations) : null;
            var rp = recipe.HasRightCalculations ? FPWorkbenchFormulaProgram.Compile(right, recipe.RightCalculations) : null;
            int x = Resolve(left.DatasetId, left.ColumnCount, recipe.LeftValueColumnId, lp), y = Resolve(right.DatasetId, right.ColumnCount, recipe.RightValueColumnId, rp);
            var mapping = await FPWorkbenchAlignment.BuildAsync(left, right, recipe.Alignment, recipe.HasLeftCorrections ? recipe.LeftCorrections.Snapshot() : null,
                recipe.HasRightCorrections ? recipe.RightCorrections.Snapshot() : null, cancellation).ConfigureAwait(false);
            return await FPWorkbenchAlignedValues.BuildAsync(mapping, x, y, lp, rp, recipe.InvalidValues, cancellation).ConfigureAwait(false);
        }
    }
}
