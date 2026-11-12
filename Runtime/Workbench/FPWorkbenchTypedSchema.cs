// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using UnityEngine;

    public enum FPWorkbenchColumnType { Text, SignedInteger, UnsignedInteger, Real, Boolean, UtcTimestamp }
    public enum FPWorkbenchValueState { Valid, Missing, Invalid, Masked }
    [Serializable] public sealed class FPWorkbenchColumnDeclaration
    {
        public int Column;
        public string ColumnId, Units = "", Culture = "", UtcFormat = "yyyy-MM-dd HH:mm:ss.fffffff";
        public FPWorkbenchColumnType Type;
        public string[] MissingTokens = Array.Empty<string>();
        public bool TrimWhitespace, IgnoreTokenCase;
        public string TrueToken = "true", FalseToken = "false";
        internal FPWorkbenchColumnDeclaration Copy()
        {
            if (Column < 0 || Column > 4095 || string.IsNullOrEmpty(ColumnId) || ColumnId.Length > 512 || Units == null || Units.Length > 128 ||
                Culture == null || Culture.Length > 64 || UtcFormat == null || UtcFormat.Length > 64 || !Enum.IsDefined(typeof(FPWorkbenchColumnType), Type) ||
                MissingTokens == null || MissingTokens.Length > 16 || string.IsNullOrEmpty(TrueToken) || TrueToken.Length > 64 || string.IsNullOrEmpty(FalseToken) || FalseToken.Length > 64)
                throw new InvalidDataException("Invalid bounded column declaration.");
            var copy = (FPWorkbenchColumnDeclaration)MemberwiseClone(); copy.MissingTokens = (string[])MissingTokens.Clone();
            try { copy.Culture = CultureInfo.GetCultureInfo(Culture).Name; }
            catch (CultureNotFoundException ex) { throw new InvalidDataException("Unknown numeric culture.", ex); }
            if (Type == FPWorkbenchColumnType.UtcTimestamp) FPWorkbenchUtcTimestamp.ValidateFormat(UtcFormat);
            var comparer = IgnoreTokenCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            string Normalize(string value) => TrimWhitespace ? value.Trim() : value;
            var missing = new HashSet<string>(comparer);
            for (int i = 0; i < copy.MissingTokens.Length; i++) {
                string token = copy.MissingTokens[i];
                if (token == null || token.Length > 64 || !missing.Add(Normalize(token))) throw new InvalidDataException("Missing tokens must be bounded, non-null and distinct under the declared token policy.");
            }
            if (Type == FPWorkbenchColumnType.Boolean && (comparer.Equals(Normalize(TrueToken), Normalize(FalseToken)) || missing.Contains(Normalize(TrueToken)) || missing.Contains(Normalize(FalseToken))))
                throw new InvalidDataException("Boolean and missing tokens must be distinct.");
            return copy;
        }
    }
    public readonly struct FPWorkbenchTypedValue
    {
        public readonly string RawText;
        public readonly FPWorkbenchColumnType Type;
        public readonly FPWorkbenchValueState ParsedState;
        public readonly bool IsMasked;
        public FPWorkbenchValueState State => IsMasked ? FPWorkbenchValueState.Masked : ParsedState;
        // Values are authoritative only for the matching Type and ParsedState.Valid. A mask retains the parsed value for inspection.
        public readonly long SignedInteger, UtcTicks;
        public readonly ulong UnsignedInteger;
        public readonly double RealNumber;
        public readonly bool Boolean;
        internal FPWorkbenchTypedValue(string text, FPWorkbenchColumnType type, FPWorkbenchValueState state, bool masked,
            long signed = 0, ulong unsigned = 0, double real = 0, bool boolean = false, long ticks = 0)
        { RawText = text; Type = type; ParsedState = state; IsMasked = masked; SignedInteger = signed; UnsignedInteger = unsigned; RealNumber = real; Boolean = boolean; UtcTicks = ticks; }
    }
    /// <summary>Validated once; strict per-cell parsing never infers culture, null tokens, integer-to-double conversion or timezone.</summary>
    public sealed class FPWorkbenchColumnParser
    {
        private readonly FPWorkbenchColumnDeclaration declaration;
        private readonly CultureInfo culture;
        private readonly HashSet<string> missing;
        private readonly StringComparer comparer;
        public FPWorkbenchColumnDeclaration Declaration => declaration.Copy();
        public FPWorkbenchColumnType Type => declaration.Type;
        public string Units => declaration.Units;
        public FPWorkbenchColumnParser(FPWorkbenchColumnDeclaration declaration)
        {
            this.declaration = declaration?.Copy() ?? throw new ArgumentNullException(nameof(declaration));
            culture = CultureInfo.GetCultureInfo(this.declaration.Culture);
            comparer = this.declaration.IgnoreTokenCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            missing = new HashSet<string>(comparer); foreach (string token in this.declaration.MissingTokens) missing.Add(Normalize(token));
        }
        private string Normalize(string value) => declaration.TrimWhitespace ? value.Trim() : value;
        public FPWorkbenchTypedValue Parse(string text, bool masked = false)
        {
            var type = declaration.Type;
            if (text == null) return new FPWorkbenchTypedValue(text, type, FPWorkbenchValueState.Missing, masked);
            string value = Normalize(text);
            if (missing.Contains(value)) return new FPWorkbenchTypedValue(text, type, FPWorkbenchValueState.Missing, masked);
            switch (type) {
                case FPWorkbenchColumnType.Text: return new FPWorkbenchTypedValue(text, type, FPWorkbenchValueState.Valid, masked);
                case FPWorkbenchColumnType.SignedInteger:
                    if (long.TryParse(value, NumberStyles.AllowLeadingSign, culture, out long signed)) return new FPWorkbenchTypedValue(text, type, FPWorkbenchValueState.Valid, masked, signed: signed); break;
                case FPWorkbenchColumnType.UnsignedInteger:
                    if (ulong.TryParse(value, NumberStyles.None, culture, out ulong unsigned)) return new FPWorkbenchTypedValue(text, type, FPWorkbenchValueState.Valid, masked, unsigned: unsigned); break;
                case FPWorkbenchColumnType.Real:
                    if (double.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, culture, out double real) && !double.IsNaN(real) && !double.IsInfinity(real))
                        return new FPWorkbenchTypedValue(text, type, FPWorkbenchValueState.Valid, masked, real: real); break;
                case FPWorkbenchColumnType.Boolean:
                    if (comparer.Equals(value, Normalize(declaration.TrueToken))) return new FPWorkbenchTypedValue(text, type, FPWorkbenchValueState.Valid, masked, boolean: true);
                    if (comparer.Equals(value, Normalize(declaration.FalseToken))) return new FPWorkbenchTypedValue(text, type, FPWorkbenchValueState.Valid, masked); break;
                case FPWorkbenchColumnType.UtcTimestamp:
                    if (DateTime.TryParseExact(value, declaration.UtcFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var utc))
                        return new FPWorkbenchTypedValue(text, type, FPWorkbenchValueState.Valid, masked, ticks: utc.Ticks); break;
            }
            return new FPWorkbenchTypedValue(text, type, FPWorkbenchValueState.Invalid, masked);
        }
    }
    [Serializable] public sealed class FPWorkbenchTypedSchema
    {
        public int Version = 1;
        public string DatasetId;
        public FPWorkbenchColumnDeclaration[] Columns = Array.Empty<FPWorkbenchColumnDeclaration>();
        public FPWorkbenchTypedSchema Copy()
        {
            if (Version != 1 || string.IsNullOrEmpty(DatasetId) || DatasetId.Length > 256 || Columns == null || Columns.Length > 4096) throw new InvalidDataException("Invalid typed source schema.");
            var copy = new FPWorkbenchTypedSchema { DatasetId = DatasetId, Columns = new FPWorkbenchColumnDeclaration[Columns.Length] };
            var ordinals = new HashSet<int>(); var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < Columns.Length; i++) {
                var c = Columns[i]?.Copy() ?? throw new InvalidDataException("Null column declaration.");
                if (!ordinals.Add(c.Column) || !ids.Add(c.ColumnId)) throw new InvalidDataException("Duplicate typed column identity/ordinal."); copy.Columns[i] = c;
            }
            Array.Sort(copy.Columns, (a, b) => a.Column.CompareTo(b.Column));
            if (Encoding.UTF8.GetByteCount(JsonUtility.ToJson(copy)) > 1048576) throw new InvalidDataException("Typed schema exceeds 1 MiB.");
            return copy;
        }
        public FPWorkbenchCompiledSchema Compile(FPWorkbenchCsvDataset dataset)
        {
            if (dataset == null) throw new ArgumentNullException(nameof(dataset));
            return CompileBounded(dataset.DatasetId, dataset.ColumnCount);
        }
        internal FPWorkbenchCompiledSchema CompileBounded(string datasetId, int sourceColumns)
        {
            var copy = Copy();
            if (copy.DatasetId != datasetId || sourceColumns < 1 || sourceColumns > 4096) throw new InvalidDataException("Typed schema belongs to another source revision or column bound.");
            var parsers = new Dictionary<int, FPWorkbenchColumnParser>();
            foreach (var column in copy.Columns) {
                if (column.Column >= sourceColumns || column.ColumnId != new FPWorkbenchCell(datasetId, 0, column.Column).ColumnId)
                    throw new InvalidDataException("Typed column identity differs from verified physical schema.");
                parsers.Add(column.Column, new FPWorkbenchColumnParser(column));
            }
            string fingerprint; using (var hash = SHA256.Create()) fingerprint = Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(JsonUtility.ToJson(copy))));
            return new FPWorkbenchCompiledSchema(copy.DatasetId, fingerprint, sourceColumns, parsers);
        }
    }
    public sealed class FPWorkbenchCompiledSchema
    {
        public string DatasetId { get; }
        public string Fingerprint { get; }
        public int SourceColumnCount { get; }
        private readonly Dictionary<int, FPWorkbenchColumnParser> parsers;
        internal FPWorkbenchCompiledSchema(string dataset, string fingerprint, int sourceColumns, Dictionary<int, FPWorkbenchColumnParser> parsers)
        { DatasetId = dataset; Fingerprint = fingerprint; SourceColumnCount = sourceColumns; this.parsers = parsers; }
        public bool TryGetParser(int sourceColumn, out FPWorkbenchColumnParser parser) => parsers.TryGetValue(sourceColumn, out parser);
        public FPWorkbenchTypedSchema Definition
        {
            get {
                var columns = new FPWorkbenchColumnDeclaration[parsers.Count]; int i = 0;
                foreach (var parser in parsers.Values) columns[i++] = parser.Declaration;
                return new FPWorkbenchTypedSchema { DatasetId = DatasetId, Columns = columns }.Copy();
            }
        }
        public void ValidateCorrection(int sourceColumn, string value, FPWorkbenchEditKind kind)
        {
            if (sourceColumn < 0 || sourceColumn >= SourceColumnCount) throw new ArgumentOutOfRangeException(nameof(sourceColumn));
            FPWorkbenchChangeSet.ValidateValue(value, kind);
            if (kind == FPWorkbenchEditKind.Missing || kind == FPWorkbenchEditKind.Masked || !parsers.TryGetValue(sourceColumn, out var parser)) return;
            if (parser.Parse(value).State == FPWorkbenchValueState.Invalid)
                throw new ArgumentException("Correction is invalid for declared " + parser.Type + " column " + (sourceColumn + 1) + ". Original data and current corrections are retained.");
        }
        public void ValidateChanges(FPWorkbenchChangeSet changes)
        {
            if (changes == null || changes.DatasetId != DatasetId) throw new InvalidDataException("Typed correction source differs.");
            changes.Validate();
            foreach (var cell in changes.Cells) {
                try { ValidateCorrection(cell.Column, cell.Value, cell.Kind); }
                catch (ArgumentException ex) { throw new InvalidDataException("Correction row " + cell.Row + " conflicts with the declared source schema.", ex); }
            }
        }
    }
}
