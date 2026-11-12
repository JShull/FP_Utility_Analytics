// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    using System.IO;

    /// <summary>Persistent verified-source reference independent of open sheet tabs.</summary>
    [Serializable]
    public sealed class FPWorkbenchSourceDocument
    {
        public string DatasetId, SourcePath, CacheDirectory, Name = "Source";
        public FPWorkbenchCsvProfile Import = new FPWorkbenchCsvProfile();
        public bool HasCalculations;
        public FPWorkbenchFormulaSet Calculations;
        public void Validate()
        {
            if (string.IsNullOrEmpty(DatasetId) || DatasetId.Length > 256 || string.IsNullOrEmpty(SourcePath) || SourcePath.Length > 8192 ||
                string.IsNullOrEmpty(CacheDirectory) || CacheDirectory.Length > 8192 || Name == null || Name.Length > 512 || Import == null)
                throw new InvalidDataException("Invalid registered source identity/path/profile.");
            Import.Copy();
            if (!HasCalculations)
            {
                if (Calculations != null && !string.IsNullOrEmpty(Calculations.DatasetId)) throw new InvalidDataException("Calculation presence marker contradicts its definitions.");
                Calculations = null;
            }
            else
            {
                if (Calculations == null || Calculations.DatasetId != DatasetId) throw new InvalidDataException("Calculations belong to another source revision or are missing.");
                try { Calculations = FPWorkbenchFormulaProgram.CompileSchema(DatasetId, Import.MaximumColumns, Calculations).Definition; }
                catch (ArgumentException ex) { throw new InvalidDataException("Invalid source calculation definitions.", ex); }
            }
        }
    }
}
