// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    /// <summary>Stable column identities own presentation order and visibility; source schema ordinals never move.</summary>
    [Serializable] public sealed class FPWorkbenchColumnProjection
    {
        public int Version = 1;
        public string[] Order = Array.Empty<string>();
        public string[] Hidden = Array.Empty<string>();
        public string[] Frozen = Array.Empty<string>();
        public const int MaximumFrozenColumns = 8;
        public void Validate()
        {
            if (Version != 1 || Order == null || Hidden == null || Frozen == null || Frozen.Length > MaximumFrozenColumns || Order.Length > FPWorkbenchColumnLayout.MaximumColumns || Hidden.Length > Order.Length)
                throw new InvalidDataException("Invalid column projection metadata.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in Order) if (string.IsNullOrEmpty(id) || id.Length > 512 || !seen.Add(id)) throw new InvalidDataException("Column projection identities must be bounded and unique.");
            var hidden = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in Hidden) if (id == null || !seen.Contains(id) || !hidden.Add(id)) throw new InvalidDataException("Hidden columns must belong to the ordered schema.");
            var frozen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in Frozen) if (id == null || !seen.Contains(id) || hidden.Contains(id) || !frozen.Add(id))
                throw new InvalidDataException("Frozen columns must be unique visible schema identities.");
            if (Order.Length > 0 && Hidden.Length == Order.Length) throw new InvalidDataException("Keep at least one visible column.");
        }
        /// <summary>Explicit schema transition: retains surviving identities, removes deleted definitions and appends new columns.</summary>
        public void BindSchema(string[] schema)
        {
            Validate(); var map = Schema(schema);
            var next = new List<string>(); foreach (string id in Order) if (map.ContainsKey(id)) next.Add(id);
            var seen = new HashSet<string>(next, StringComparer.Ordinal); foreach (string id in schema) if (seen.Add(id)) next.Add(id);
            var hidden = new List<string>(); foreach (string id in Hidden) if (map.ContainsKey(id)) hidden.Add(id);
            if (hidden.Count == next.Count) hidden.Remove(next[0]);
            var frozen = new List<string>(); foreach (string id in Frozen) if (map.ContainsKey(id) && !hidden.Contains(id)) frozen.Add(id);
            Order = next.ToArray(); Hidden = hidden.ToArray(); Frozen = frozen.ToArray(); Validate();
        }
        public int[] Resolve(string[] schema)
        {
            Validate(); var map = Schema(schema);
            if (Order.Length != schema.Length) throw new InvalidDataException("Bind the projection to its schema before resolving.");
            var hidden = new HashSet<string>(Hidden, StringComparer.Ordinal); var result = new List<int>();
            foreach (string id in Order) {
                if (!map.TryGetValue(id, out int c)) throw new InvalidDataException("Column projection contains a foreign or removed identity.");
                if (!hidden.Contains(id)) result.Add(c);
            }
            return result.ToArray();
        }
        public void SetHidden(string id, bool hide)
        {
            Validate(); if (Array.IndexOf(Order, id) < 0) throw new ArgumentException("Column identity is not bound.");
            var next = new List<string>(Hidden);
            if (hide) { if (!next.Contains(id)) { if (next.Count + 1 == Order.Length) throw new InvalidOperationException("Keep at least one visible column."); next.Add(id); } }
            else next.Remove(id);
            Hidden = next.ToArray();
            if (hide) { var frozen = new List<string>(Frozen); frozen.Remove(id); Frozen = frozen.ToArray(); }
        }
        public void SetFrozen(string id, bool freeze)
        {
            Validate();
            if (Array.IndexOf(Order, id) < 0 || Array.IndexOf(Hidden, id) >= 0) throw new ArgumentException("Freeze a visible bound column.");
            var next = new List<string>(Frozen);
            if (freeze && !next.Contains(id)) {
                if (next.Count == MaximumFrozenColumns) throw new InvalidOperationException("At most eight identifier columns can be frozen.");
                next.Add(id);
            }
            else if (!freeze) next.Remove(id);
            Frozen = next.ToArray();
        }
        /// <summary>Frozen columns in display order, then a window of non-frozen columns. First indexes only the scrolling columns.</summary>
        public int[] ResolveWindow(string[] schema, int first, int maximumColumns)
        {
            int[] visible = Resolve(schema);
            if (maximumColumns < 1 || maximumColumns > 32 || first < 0) throw new ArgumentOutOfRangeException();
            var pinned = new HashSet<string>(Frozen, StringComparer.Ordinal);
            var fixedColumns = new List<int>(); var scrolling = new List<int>();
            foreach (int column in visible) { if (pinned.Contains(schema[column])) fixedColumns.Add(column); else scrolling.Add(column); }
            if (first > Math.Max(0, scrolling.Count - 1)) throw new ArgumentOutOfRangeException(nameof(first));
            if (fixedColumns.Count > maximumColumns || scrolling.Count > 0 && fixedColumns.Count == maximumColumns)
                throw new ArgumentException("Leave a viewport column available for scrolling data.");
            for (int c = first; c < scrolling.Count && fixedColumns.Count < maximumColumns; c++) fixedColumns.Add(scrolling[c]);
            return fixedColumns.ToArray();
        }
        public void Move(string id, int position)
        {
            Validate(); int current = Array.IndexOf(Order, id);
            if (current < 0 || position < 0 || position >= Order.Length) throw new ArgumentOutOfRangeException();
            var next = new List<string>(Order); next.RemoveAt(current); next.Insert(position, id); Order = next.ToArray();
        }
        private static Dictionary<string, int> Schema(string[] schema)
        {
            if (schema == null || schema.Length < 1 || schema.Length > FPWorkbenchColumnLayout.MaximumColumns) throw new ArgumentException("Invalid projection schema bounds.");
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int c = 0; c < schema.Length; c++) {
                if (string.IsNullOrEmpty(schema[c]) || schema[c].Length > 512 || result.ContainsKey(schema[c])) throw new ArgumentException("Invalid or duplicate schema identity.");
                result.Add(schema[c], c);
            }
            return result;
        }
    }
}
