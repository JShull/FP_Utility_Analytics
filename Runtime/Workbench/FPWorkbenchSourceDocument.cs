// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    using System.IO;
    using UnityEngine;

    /// <summary>Persistent verified-source reference independent of open sheet tabs.</summary>
    [Serializable]
    public sealed class FPWorkbenchSourceDocument
    {
        public string DatasetId, SourcePath, CacheDirectory, Name = "Source";
        public FPWorkbenchCsvProfile Import = new FPWorkbenchCsvProfile();
        public bool HasCalculations;
        public FPWorkbenchFormulaSet Calculations;
        public bool HasTypedSchema;
        public FPWorkbenchTypedSchema TypedSchema;
        public void Validate()
        {
            if (string.IsNullOrEmpty(DatasetId) || DatasetId.Length > 256 || string.IsNullOrEmpty(SourcePath) || SourcePath.Length > 8192 ||
                string.IsNullOrEmpty(CacheDirectory) || CacheDirectory.Length > 8192 || Name == null || Name.Length > 512 || Import == null)
                throw new InvalidDataException("Invalid registered source identity/path/profile.");
            Import.Copy();
            if (!HasTypedSchema) {
                if (TypedSchema != null && !string.IsNullOrEmpty(TypedSchema.DatasetId)) throw new InvalidDataException("Typed-schema presence marker contradicts its declaration.");
                TypedSchema = null;
            }
            else {
                if (TypedSchema == null || TypedSchema.DatasetId != DatasetId) throw new InvalidDataException("Typed schema is missing or belongs to another source revision.");
                TypedSchema = TypedSchema.Copy();
            }
            if (!HasCalculations)
            {
                if (Calculations != null && !string.IsNullOrEmpty(Calculations.DatasetId)) throw new InvalidDataException("Calculation presence marker contradicts its definitions.");
                Calculations = null;
            }
            else
            {
                if (Calculations == null || Calculations.DatasetId != DatasetId) throw new InvalidDataException("Calculations belong to another source revision or are missing.");
            }
            if (HasCalculations || HasTypedSchema) {
                try {
                    var program = FPWorkbenchFormulaProgram.CompileSchema(DatasetId, Import.MaximumColumns, ActiveDefinitions());
                    if (HasCalculations) Calculations = program.Definition;
                }
                catch (ArgumentException ex) { throw new InvalidDataException("Invalid source calculation/type definitions.", ex); }
            }
        }
        private FPWorkbenchFormulaSet ActiveDefinitions()
        {
            var definitions = HasCalculations ? JsonUtility.FromJson<FPWorkbenchFormulaSet>(JsonUtility.ToJson(Calculations))
                : new FPWorkbenchFormulaSet { DatasetId = DatasetId };
            if (HasTypedSchema) { definitions.HasTypedSchema = true; definitions.TypedSchema = TypedSchema; }
            return definitions;
        }
        /// <summary>Compiles active source interpretation against verified physical columns. Frozen figures use their own definitions.</summary>
        public FPWorkbenchFormulaProgram CompileProgram(FPWorkbenchCsvDataset dataset)
        {
            if (dataset == null || dataset.DatasetId != DatasetId) throw new InvalidDataException("Source program belongs to another verified revision.");
            if (!HasTypedSchema && TypedSchema != null && !string.IsNullOrEmpty(TypedSchema.DatasetId) ||
                !HasCalculations && Calculations != null && !string.IsNullOrEmpty(Calculations.DatasetId))
                throw new InvalidDataException("Source interpretation presence markers contradict their metadata.");
            if (!HasCalculations && !HasTypedSchema) return null;
            if (HasTypedSchema) {
                if (TypedSchema == null) throw new InvalidDataException("Source typed schema is missing.");
                TypedSchema.Compile(dataset);
            }
            if (HasCalculations && Calculations == null) throw new InvalidDataException("Source calculations are missing.");
            return FPWorkbenchFormulaProgram.Compile(dataset, ActiveDefinitions());
        }
    }
}
