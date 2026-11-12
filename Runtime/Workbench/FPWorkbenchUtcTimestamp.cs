// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    using System.Globalization;
    /// <summary>Complete, explicitly declared Gregorian UTC timestamps shared by plot and alignment engines.</summary>
    internal static class FPWorkbenchUtcTimestamp
    {
        private static readonly string[] DateFormats = { "dd.MM.yyyy", "yyyy-MM-dd", "MM/dd/yyyy", "dd/MM/yyyy", "yyyy/MM/dd" };
        internal static void ValidateFormat(string format)
        {
            if (string.IsNullOrEmpty(format) || format.Length > 64) throw new ArgumentException("Declare a bounded complete UTC date/time format.");
            var parts = format.Split(' ');
            if (parts.Length != 2 || Array.IndexOf(DateFormats, parts[0]) < 0 || !parts[1].StartsWith("HH:mm:ss", StringComparison.Ordinal))
                throw new ArgumentException("UTC timestamps require a supported four-digit-year date and HH:mm:ss time, optionally with 1–7 required fractional digits.");
            string fraction = parts[1].Substring(8);
            if (fraction.Length == 0) return;
            if (fraction.Length < 2 || fraction.Length > 8 || fraction[0] != '.' && fraction[0] != ',') throw new ArgumentException("UTC fractional seconds require a decimal separator and 1–7 f digits.");
            for (int i = 1; i < fraction.Length; i++) if (fraction[i] != 'f') throw new ArgumentException("Only required f digits are supported for UTC fractions; timezone/clock transforms are explicit separate operations.");
        }
        // Callers validate once at binding admission, not once per source row.
        internal static bool TryRead(string date, string time, string validatedFormat, out long ticks)
        {
            ticks = 0;
            if (date == null || time == null || !DateTime.TryParseExact(date.Trim() + " " + time.Trim(), validatedFormat,
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var utc)) return false;
            ticks = utc.Ticks; return true;
        }
    }
}
