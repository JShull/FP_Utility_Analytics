// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using UnityEngine;

    public enum FPWorkbenchEditKind { Text, Number, Integer, Boolean, Missing, Masked }
    [Serializable]
    public sealed class FPWorkbenchCellChange
    {
        public long Row;
        public int Column;
        public string Original, Value, Reason = "", EditedUtc;
        public FPWorkbenchEditKind Kind;
    }
    [Serializable]
    public sealed class FPWorkbenchChangeSet
    {
        public int Version = 1;
        public string DatasetId;
        public long Revision;
        public List<FPWorkbenchCellChange> Cells = new List<FPWorkbenchCellChange>();
        public const int MaximumCells = 8192, MaximumCharacters = 1024 * 1024;
        public void Validate()
        {
            if (Version != 1 || string.IsNullOrEmpty(DatasetId) || DatasetId.Length > 256 || Revision < 0 || Cells == null || Cells.Count > MaximumCells) throw new InvalidDataException("Invalid or oversized correction set.");
            long characters = 0; var keys = new HashSet<FPWorkbenchCell>();
            foreach (var cell in Cells)
            {
                if (cell == null || cell.Row < 0 || cell.Column < 0 || cell.Original == null || cell.Value == null || cell.Reason == null || cell.EditedUtc == null || cell.Reason.Length > 4096 || cell.EditedUtc.Length > 128 || !Enum.IsDefined(typeof(FPWorkbenchEditKind), cell.Kind) || !keys.Add(new FPWorkbenchCell(DatasetId, cell.Row, cell.Column))) throw new InvalidDataException("Invalid or duplicate cell correction.");
                try { ValidateValue(cell.Value, cell.Kind); }
                catch (ArgumentException ex) { throw new InvalidDataException("Invalid typed correction value.", ex); }
                characters += cell.Original.Length + cell.Value.Length + cell.Reason.Length + cell.EditedUtc.Length;
                if (characters > MaximumCharacters) throw new InvalidDataException("Correction text exceeds its bounded in-memory allowance. Indexed large change sets remain planned.");
            }
        }
        public static void ValidateValue(string value, FPWorkbenchEditKind kind)
        {
            if (!Enum.IsDefined(typeof(FPWorkbenchEditKind), kind)) throw new ArgumentException("Unknown correction kind.");
            if (value == null || value.Length > 65536) throw new ArgumentException("Correction value is missing or exceeds 65,536 characters.");
            if (kind == FPWorkbenchEditKind.Number && (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || double.IsNaN(number) || double.IsInfinity(number))) throw new ArgumentException("Enter a finite invariant-culture number.");
            if (kind == FPWorkbenchEditKind.Integer && !long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) throw new ArgumentException("Enter a signed 64-bit integer.");
            if (kind == FPWorkbenchEditKind.Boolean && !bool.TryParse(value, out _)) throw new ArgumentException("Enter true or false.");
        }
        public bool Set(FPWorkbenchCell address, string original, string value, FPWorkbenchEditKind kind, string reason)
        {
            if (Revision == long.MaxValue) throw new InvalidOperationException("Correction revision limit reached.");
            if (address.DatasetId != DatasetId) throw new InvalidOperationException("Correction source revision does not match.");
            ValidateValue(value, kind);
            if (original == null || reason == null || reason.Length > 4096) throw new ArgumentException("Original value/reason is invalid.");
            var old = Cells.Find(c => c.Row == address.Row && c.Column == address.Column);
            if (old != null && old.Original != original) throw new InvalidDataException("Original-cell precondition changed; correction was not applied.");
            if (old != null && old.Value == value && old.Kind == kind && old.Reason == reason) return false;
            var next = new FPWorkbenchCellChange { Row = address.Row, Column = address.Column, Original = original, Value = value, Kind = kind, Reason = reason, EditedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) };
            int index = old == null ? -1 : Cells.IndexOf(old);
            if (index < 0) Cells.Add(next); else Cells[index] = next;
            try { Validate(); }
            catch { if (index < 0) Cells.RemoveAt(Cells.Count - 1); else Cells[index] = old; throw; }
            Revision = checked(Revision + 1); return true;
        }
        public bool Revert(FPWorkbenchCell address)
        {
            if (Revision == long.MaxValue) throw new InvalidOperationException("Correction revision limit reached.");
            if (address.DatasetId != DatasetId) throw new InvalidOperationException("Correction source revision does not match.");
            int index = Cells.FindIndex(c => c.Row == address.Row && c.Column == address.Column);
            if (index < 0) return false; Cells.RemoveAt(index); Revision = checked(Revision + 1); return true;
        }
        public FPWorkbenchChangeSnapshot Snapshot()
        { Validate(); return new FPWorkbenchChangeSnapshot(JsonUtility.ToJson(this)); }
        public async Task ValidateSourceAsync(FPWorkbenchCsvDataset dataset, CancellationToken cancellation = default)
        {
            if (dataset.DatasetId != DatasetId) throw new InvalidDataException("Change-set source revision differs from the dataset.");
            var snapshot = Snapshot();
            foreach (var cell in snapshot.Entries)
            {
                cancellation.ThrowIfCancellationRequested();
                if (cell.Row >= dataset.RowCount || cell.Column >= dataset.ColumnCount) throw new InvalidDataException("Correction address is outside the source.");
                var page = await dataset.ReadPageAsync(cell.Row, 1, cell.Column, 1, cancellation).ConfigureAwait(false);
                if (page.Values[0][0] != cell.Original) throw new InvalidDataException("Correction original-cell precondition does not match the source.");
            }
        }
    }
    /// <summary>Immutable worker snapshot; keys always address original source cells.</summary>
    public sealed class FPWorkbenchChangeSnapshot
    {
        private readonly Dictionary<FPWorkbenchCell, FPWorkbenchCellChange> cells = new Dictionary<FPWorkbenchCell, FPWorkbenchCellChange>();
        public string DatasetId { get; }
        public string Fingerprint { get; }
        public string Json { get; }
        internal IEnumerable<FPWorkbenchCellChange> Entries => cells.Values;
        internal FPWorkbenchChangeSnapshot(string json)
        {
            Json = json;
            var data = JsonUtility.FromJson<FPWorkbenchChangeSet>(json); DatasetId = data.DatasetId;
            foreach (var cell in data.Cells) cells.Add(new FPWorkbenchCell(DatasetId, cell.Row, cell.Column), cell);
            using var sha = SHA256.Create(); Fingerprint = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(json))).Replace("-", "").ToLowerInvariant();
        }
        public bool TryGet(FPWorkbenchCell address, string original, out string value, out bool masked)
        {
            value = original; masked = false;
            if (address.DatasetId != DatasetId) throw new InvalidOperationException("Correction snapshot source differs.");
            if (!cells.TryGetValue(address, out var cell)) return false;
            if (original != cell.Original) throw new InvalidDataException("Correction original-cell precondition differs.");
            masked = cell.Kind == FPWorkbenchEditKind.Masked;
            value = masked || cell.Kind == FPWorkbenchEditKind.Missing ? "" : cell.Value;
            return true;
        }
    }
}
