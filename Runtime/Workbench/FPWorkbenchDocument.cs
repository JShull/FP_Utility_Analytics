// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using UnityEngine;

    [Serializable]
    public sealed class FPWorkbenchSheetDocument
    {
        public string Id = Guid.NewGuid().ToString("N"), Name = "Sheet", SourcePath, CacheDirectory, DatasetId;
        public long FirstRow;
        public int FirstColumn, PlotColumn;
        public bool ShowPlot;
        public bool ShowOriginal;
        public FPWorkbenchCsvProfile Import = new FPWorkbenchCsvProfile();
        public FPWorkbenchSeriesBinding Binding = new FPWorkbenchSeriesBinding();
        public FPWorkbenchPlotSettings Plot = new FPWorkbenchPlotSettings();
        public FPWorkbenchTableSelection Selection;
        public FPWorkbenchColumnLayout Columns = new FPWorkbenchColumnLayout();
    }

    [Serializable]
    public sealed class FPWorkbenchDocument
    {
        public string Format = "FuzzPhyte.DataWorkbench";
        public int Version = 1;
        public string Id = Guid.NewGuid().ToString("N"), Name = "Workbook";
        public int ActiveSheet;
        public List<FPWorkbenchSheetDocument> Sheets = new List<FPWorkbenchSheetDocument>();
        public List<FPWorkbenchChangeSet> Changes = new List<FPWorkbenchChangeSet>();
        public List<FPWorkbenchFigureDocument> Figures = new List<FPWorkbenchFigureDocument>();
        public List<FPWorkbenchSourceDocument> Sources = new List<FPWorkbenchSourceDocument>();
        public void Validate()
        {
            if (Format != "FuzzPhyte.DataWorkbench" || Version != 1 || string.IsNullOrEmpty(Id) || Id.Length > 128 || Name == null || Name.Length > 512 || Sheets == null || Sheets.Count > 8 || ActiveSheet < 0 || (Sheets.Count == 0 ? ActiveSheet != 0 : ActiveSheet >= Sheets.Count)) throw new InvalidDataException("Unsupported or invalid workbook document.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var sheet in Sheets)
            {
                if (sheet == null || string.IsNullOrEmpty(sheet.Id) || sheet.Id.Length > 128 || !ids.Add(sheet.Id) || string.IsNullOrEmpty(sheet.Name) || sheet.Name.Length > 512 || string.IsNullOrEmpty(sheet.DatasetId) || sheet.DatasetId.Length > 256 || string.IsNullOrEmpty(sheet.SourcePath) || string.IsNullOrEmpty(sheet.CacheDirectory) || sheet.SourcePath.Length > 8192 || sheet.CacheDirectory.Length > 8192 || sheet.FirstRow < 0 || sheet.FirstColumn < 0 || sheet.PlotColumn < 0 || sheet.Import == null || sheet.Binding == null || sheet.Plot == null || sheet.Columns == null)
                    throw new InvalidDataException("Invalid sheet identity, source, layout or binding.");
                sheet.Import.Copy();
                try { sheet.Binding.Copy(sheet.Import.MaximumColumns); }
                catch (ArgumentException ex) { throw new InvalidDataException("Invalid sheet plot binding.", ex); }
                sheet.Columns.Projection ??= new FPWorkbenchColumnProjection(); sheet.Columns.Projection.Validate();
                if (sheet.Selection != null && string.IsNullOrEmpty(sheet.Selection.DatasetId)) sheet.Selection = null;
                if (sheet.Selection != null) { sheet.Selection.Validate(); if (sheet.Selection.DatasetId != sheet.DatasetId) throw new InvalidDataException("Sheet selection belongs to another source."); }
                ValidatePlot(sheet.Plot);
                if ((sheet.Columns.Version != 1 && sheet.Columns.Version != 2) || sheet.Columns.ManualWidths == null || sheet.Columns.ManualWidths.Length > FPWorkbenchColumnLayout.MaximumColumns) throw new InvalidDataException("Invalid column layout.");
                if (sheet.Columns.Version == 2 && (float.IsNaN(sheet.Columns.RowHeaderWidth) || float.IsInfinity(sheet.Columns.RowHeaderWidth) || sheet.Columns.RowHeaderWidth < 40 || sheet.Columns.RowHeaderWidth > 320)) throw new InvalidDataException("Invalid row-header width.");
                foreach (float width in sheet.Columns.ManualWidths) if (float.IsNaN(width) || float.IsInfinity(width) || (width != 0 && (width < 40 || width > 1600))) throw new InvalidDataException("Invalid manual column width.");
            }
            Sources ??= new List<FPWorkbenchSourceDocument>();
            foreach (var sheet in Sheets)
                if (!Sources.Exists(s => s != null && s.DatasetId == sheet.DatasetId))
                    Sources.Add(new FPWorkbenchSourceDocument { DatasetId = sheet.DatasetId, SourcePath = sheet.SourcePath, CacheDirectory = sheet.CacheDirectory, Name = sheet.Name, Import = sheet.Import.Copy() });
            if (Sources.Count > 64) throw new InvalidDataException("A workbook supports up to 64 registered sources.");
            var registered = new HashSet<string>(StringComparer.Ordinal);
            foreach (var source in Sources)
            {
                if (source == null) throw new InvalidDataException("Missing registered source.");
                source.Validate(); if (!registered.Add(source.DatasetId)) throw new InvalidDataException("Duplicate registered source revision.");
            }
            Changes ??= new List<FPWorkbenchChangeSet>();
            if (Changes.Count > 64) throw new InvalidDataException("Too many correction source revisions.");
            var sources = new HashSet<string>(StringComparer.Ordinal);
            foreach (var changes in Changes)
            { if (changes == null || !sources.Add(changes.DatasetId)) throw new InvalidDataException("Duplicate/invalid correction source."); changes.Validate(); }
            Figures ??= new List<FPWorkbenchFigureDocument>();
            if (Figures.Count > 32) throw new InvalidDataException("A workbook supports up to 32 saved figures.");
            var figureIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var figure in Figures)
            {
                if (figure == null || !figureIds.Add(figure.Id)) throw new InvalidDataException("Duplicate/invalid figure identity.");
                figure.Validate();
                if (figure.Kind == FPWorkbenchFigureKind.AlignedScatter && !registered.Contains(figure.AlignedRecipe.RightDatasetId)) throw new InvalidDataException("Missing registered right source for aligned figure.");
                if (!registered.Contains(figure.DatasetId)) throw new InvalidDataException("Missing registered source for figure: " + figure.Name);
            }
        }
        internal static void ValidatePlot(FPWorkbenchPlotSettings p)
        {
            if (p == null || !Enum.IsDefined(typeof(FPWorkbenchAxisScale), p.XScale) || !Enum.IsDefined(typeof(FPWorkbenchAxisScale), p.YScale) || !Enum.IsDefined(typeof(FPWorkbenchPlotStyle), p.Style) || !Finite(p.LineWidth) || p.LineWidth < 1 || p.LineWidth > 8 || !Finite(p.MarkerSize) || p.MarkerSize < 1 || p.MarkerSize > 16 || !Finite(p.StartX) || !Finite(p.EndX) || p.EndX <= p.StartX || !Finite(p.MinimumY) || !Finite(p.MaximumY) || p.MaximumY < p.MinimumY ||
                !Finite(p.XTickSpacing) || !Finite(p.YTickSpacing) || p.XTickSpacing < 0 || p.YTickSpacing < 0 || p.Title == null || p.Title.Length > 4096 ||
                p.XLabel == null || p.XLabel.Length > 4096 || p.YLabel == null || p.YLabel.Length > 4096) throw new InvalidDataException("Invalid plot range, grid spacing or labels.");
            if (p.XScale == FPWorkbenchAxisScale.Log10 && p.HasRange && p.StartX <= 0 || p.YScale == FPWorkbenchAxisScale.Log10 && p.LockY && p.MinimumY <= 0)
                throw new InvalidDataException("Logarithmic plot limits must be positive.");
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public sealed class FPWorkbenchLoadedDocument
    {
        public FPWorkbenchDocument Document { get; }
        public IReadOnlyList<FPWorkbenchCsvDataset> Datasets { get; }
        public IReadOnlyList<FPWorkbenchCsvDataset> SourceDatasets { get; }
        internal FPWorkbenchLoadedDocument(FPWorkbenchDocument document, FPWorkbenchCsvDataset[] datasets, FPWorkbenchCsvDataset[] sourceDatasets)
        { Document = document; Datasets = Array.AsReadOnly(datasets); SourceDatasets = Array.AsReadOnly(sourceDatasets); }
    }

    /// <summary>Bounded versioned document persistence. No source data is embedded or overwritten.</summary>
    public static class FPWorkbenchDocumentFile
    {
        [Serializable] private struct Marker { public string Format; public int Version; }
        public const int MaximumDocumentBytes = 4 * 1024 * 1024;
        public static Task SaveAsync(FPWorkbenchDocument document, string path, CancellationToken cancellation = default)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            document.Validate();
            string target = Path.GetFullPath(path);
            if (!target.EndsWith(".fpworkbook", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Use a .fpworkbook filename.");
            foreach (var source in document.Sources)
                if (SamePath(target, source.SourcePath) || Inside(target, source.CacheDirectory)) throw new IOException("Save the workbook outside source files and cache directories.");
            string json = JsonUtility.ToJson(document, true); // Snapshot before scheduling; callers may continue editing.
            byte[] bytes = new UTF8Encoding(false, true).GetBytes(json);
            if (bytes.Length > MaximumDocumentBytes) throw new InvalidDataException("Workbook metadata exceeds 4 MiB.");
            return WriteAtomicAsync(bytes, target, cancellation);
        }
        internal static Task WriteAtomicAsync(byte[] bytes, string target, CancellationToken cancellation)
        {
            return Task.Run(() =>
            {
                cancellation.ThrowIfCancellationRequested();
                string parent = Path.GetDirectoryName(target); Directory.CreateDirectory(parent);
                string temporary = Path.Combine(parent, ".fpworkbook-" + Guid.NewGuid().ToString("N"));
                try
                {
                    using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    { output.Write(bytes, 0, bytes.Length); output.Flush(true); }
                    cancellation.ThrowIfCancellationRequested();
                    if (File.Exists(target)) File.Replace(temporary, target, null); else File.Move(temporary, target);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }, cancellation);
        }
        /// <summary>Reads bounded, structurally validated metadata only. Sources are NOT admitted; use LoadAsync before displaying data.</summary>
        public static Task<FPWorkbenchDocument> ReadMetadataAsync(string path, CancellationToken cancellation = default)
        {
            string source = Path.GetFullPath(path);
            return Task.Run(() => ReadMetadata(source, cancellation), cancellation);
        }
        private static FPWorkbenchDocument ReadMetadata(string source, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            byte[] bytes;
            using (var file = File.OpenRead(source))
            {
                if (file.Length > MaximumDocumentBytes) throw new InvalidDataException("Workbook metadata exceeds 4 MiB.");
                bytes = new byte[(int)file.Length]; int read = 0;
                while (read < bytes.Length) { cancellation.ThrowIfCancellationRequested(); int n = file.Read(bytes, read, bytes.Length - read); if (n == 0) throw new EndOfStreamException(); read += n; }
            }
            FPWorkbenchDocument document;
            try
            {
                string json = new UTF8Encoding(false, true).GetString(bytes);
                var marker = JsonUtility.FromJson<Marker>(json);
                if (marker.Format != "FuzzPhyte.DataWorkbench" || marker.Version != 1) throw new InvalidDataException("Not a supported Data Workbench document.");
                document = JsonUtility.FromJson<FPWorkbenchDocument>(json);
            }
            catch (ArgumentException ex) { throw new InvalidDataException("Invalid workbook JSON.", ex); }
            if (document == null) throw new InvalidDataException("Empty workbook document.");
            document.Validate();
            return document;
        }
        public static Task<FPWorkbenchLoadedDocument> LoadAsync(string path, CancellationToken cancellation = default)
        {
            string source = Path.GetFullPath(path);
            return Task.Run(async () =>
            {
                cancellation.ThrowIfCancellationRequested();
                var document = ReadMetadata(source, cancellation);
                var sourceDatasets = new FPWorkbenchCsvDataset[document.Sources.Count];
                for (int i = 0; i < sourceDatasets.Length; i++)
                {
                    cancellation.ThrowIfCancellationRequested(); var sourceDocument = document.Sources[i];
                    var data = await FPWorkbenchCsvDataset.OpenAsync(sourceDocument.CacheDirectory, cancellation).ConfigureAwait(false);
                    if (data.DatasetId != sourceDocument.DatasetId || !SamePath(data.SourcePath, sourceDocument.SourcePath) ||
                        data.ImportProfile.Delimiter != sourceDocument.Import.Delimiter || data.ImportProfile.EncodingCodePage != sourceDocument.Import.EncodingCodePage)
                        throw new InvalidDataException("Registered source revision/profile differs: " + sourceDocument.Name);
                    sourceDatasets[i] = data;
                    if (sourceDocument.HasCalculations)
                    {
                        try { sourceDocument.Calculations = FPWorkbenchFormulaProgram.Compile(data, sourceDocument.Calculations).Definition; }
                        catch (ArgumentException ex) { throw new InvalidDataException("Calculations differ from verified source schema: " + sourceDocument.Name, ex); }
                    }
                }
                var datasets = new FPWorkbenchCsvDataset[document.Sheets.Count];
                for (int i = 0; i < datasets.Length; i++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var sheet = document.Sheets[i];
                    var dataset = Array.Find(sourceDatasets, d => d.DatasetId == sheet.DatasetId);
                    var sourceMetadata = document.Sources.Find(s => s.DatasetId == sheet.DatasetId);
                    int tableColumns = dataset.ColumnCount + (sourceMetadata.HasCalculations ? sourceMetadata.Calculations.Columns.Count : 0);
                    if (!SamePath(sheet.SourcePath, dataset.SourcePath) || !SamePath(sheet.CacheDirectory, dataset.CacheDirectory) || sheet.DatasetId != dataset.DatasetId) throw new InvalidDataException("Workbook source revision does not match its referenced cache: " + sheet.Name);
                    if (sheet.FirstRow > Math.Max(0, dataset.RowCount - 1) || sheet.FirstColumn >= tableColumns || sheet.PlotColumn >= tableColumns) throw new InvalidDataException("Saved table position is outside the source: " + sheet.Name);
                    var actualImport = dataset.ImportProfile;
                    if (sheet.Import.Delimiter != actualImport.Delimiter || sheet.Import.EncodingCodePage != actualImport.EncodingCodePage) throw new InvalidDataException("Saved import interpretation differs from the indexed source: " + sheet.Name);
                    if (sheet.Columns.ManualWidths.Length != 0 && sheet.Columns.ManualWidths.Length != dataset.ColumnCount && sheet.Columns.ManualWidths.Length != tableColumns) throw new InvalidDataException("Column layout differs from source schema: " + sheet.Name);
                    if (sheet.Columns.Projection.Order.Length > 0) {
                        var program = sourceMetadata.HasCalculations ? FPWorkbenchFormulaProgram.Compile(dataset, sourceMetadata.Calculations) : null;
                        var table = new FPWorkbenchTableView(dataset, program); var identities = new string[table.ColumnCount];
                        for (int c = 0; c < identities.Length; c++) identities[c] = table.ColumnId(c);
                        var visible = sheet.Columns.Projection.Resolve(identities);
                        if (sheet.FirstColumn >= visible.Length) throw new InvalidDataException("Saved table column position exceeds its visible projection.");
                    }
                    if (sheet.Selection != null) {
                        var program = sourceMetadata.HasCalculations ? FPWorkbenchFormulaProgram.Compile(dataset, sourceMetadata.Calculations) : null;
                        sheet.Selection.Resolve(new FPWorkbenchTableView(dataset, program));
                    }
                    sheet.Binding.Copy(dataset.ColumnCount); sheet.Columns.EnsureColumns(tableColumns);
                    if (sheet.Binding.HasDerivedY || sheet.Binding.HasDerivedX)
                    {
                        try
                        {
                            var program = sourceMetadata.HasCalculations ? FPWorkbenchFormulaProgram.Compile(dataset, sourceMetadata.Calculations) : null;
                            sheet.Binding.ResolveDerivedX(program);
                            if (sheet.Binding.HasDerivedY) sheet.PlotColumn = dataset.ColumnCount + sheet.Binding.ResolveDerivedY(program);
                        }
                        catch (ArgumentException ex) { throw new InvalidDataException("Calculated plot binding is unresolved: " + sheet.Name, ex); }
                    }
                    datasets[i] = dataset;
                }
                foreach (var changes in document.Changes)
                {
                    var dataset = Array.Find(sourceDatasets, d => d.DatasetId == changes.DatasetId);
                    if (dataset != null) await changes.ValidateSourceAsync(dataset, cancellation).ConfigureAwait(false);
                }
                foreach (var figure in document.Figures)
                {
                    var dataset = Array.Find(sourceDatasets, d => d.DatasetId == figure.DatasetId);
                    var secondary = figure.Kind == FPWorkbenchFigureKind.AlignedScatter ? Array.Find(sourceDatasets, d => d.DatasetId == figure.AlignedRecipe.RightDatasetId) : null;
                    figure.ValidateSource(dataset, secondary);
                    if (figure.Kind == FPWorkbenchFigureKind.AlignedScatter)
                    {
                        if (figure.AlignedRecipe.HasLeftCorrections) await figure.AlignedRecipe.LeftCorrections.ValidateSourceAsync(dataset, cancellation).ConfigureAwait(false);
                        if (figure.AlignedRecipe.HasRightCorrections) await figure.AlignedRecipe.RightCorrections.ValidateSourceAsync(secondary, cancellation).ConfigureAwait(false);
                    }
                    if (figure.CorrectionSnapshot != null) await figure.CorrectionSnapshot.ValidateSourceAsync(dataset, cancellation).ConfigureAwait(false);
                }
                return new FPWorkbenchLoadedDocument(document, datasets, sourceDatasets);
            }, cancellation);
        }
        private static bool SamePath(string first, string second) => string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        private static bool Inside(string file, string directory)
        {
            string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(file).StartsWith(root, Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
    }
}
