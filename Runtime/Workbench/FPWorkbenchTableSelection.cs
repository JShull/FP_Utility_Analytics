// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    [Serializable] public sealed class FPWorkbenchTableSelection
    {
        public string DatasetId;
        public long FirstRow, EndRow;
        public string[] ColumnIds = Array.Empty<string>();
        public void Validate()
        {
            if (string.IsNullOrEmpty(DatasetId) || DatasetId.Length > 256 || FirstRow < 0 || EndRow <= FirstRow || ColumnIds == null || ColumnIds.Length < 1 || ColumnIds.Length > 32)
                throw new InvalidDataException("Selection requires source rows and 1–32 stable column identities.");
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in ColumnIds) if (string.IsNullOrEmpty(id) || id.Length > 512 || !unique.Add(id)) throw new InvalidDataException("Selection columns must be bounded and unique.");
        }
        public int[] Resolve(FPWorkbenchTableView table)
        {
            Validate(); if (table == null || DatasetId != table.DatasetId || EndRow > table.RowCount) throw new InvalidDataException("Selection source revision/rows differ from its table.");
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int c = 0; c < table.ColumnCount; c++) map.Add(table.ColumnId(c), c);
            var result = new int[ColumnIds.Length]; for (int c = 0; c < result.Length; c++)
                if (!map.TryGetValue(ColumnIds[c], out result[c])) throw new InvalidDataException("Selected column was removed or belongs to another schema.");
            return result;
        }
    }
}
