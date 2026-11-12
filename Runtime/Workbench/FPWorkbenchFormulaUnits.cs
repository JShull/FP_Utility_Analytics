// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;

    internal sealed class FPWorkbenchFormulaUnits
    {
        private readonly SortedDictionary<string, int> powers = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public bool Known { get; private set; }
        public static FPWorkbenchFormulaUnits Parse(string text)
        {
            var result = new FPWorkbenchFormulaUnits { Known = !string.IsNullOrWhiteSpace(text) };
            if (!result.Known) return result;
            if (text.Length > 128) throw new ArgumentException("Unit declaration is too long.");
            string compact = text.Replace(" ", ""); int position = 0, sign = 1;
            while (position < compact.Length)
            {
                int start = position;
                while (position < compact.Length && (char.IsLetter(compact[position]) || compact[position] == '1')) position++;
                if (start == position) throw new ArgumentException("Units use symbols, *, / and integer exponents, e.g. m/s^2 or 1.");
                string symbol = compact.Substring(start, position - start); int exponent = 1;
                if (position < compact.Length && compact[position] == '^')
                {
                    position++; start = position;
                    if (position < compact.Length && (compact[position] == '-' || compact[position] == '+')) position++;
                    while (position < compact.Length && char.IsDigit(compact[position])) position++;
                    if (!int.TryParse(compact.Substring(start, position - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out exponent) || Math.Abs((long)exponent) > 8) throw new ArgumentException("Unit exponents must be between -8 and 8.");
                }
                if (symbol != "1") result.Add(symbol, sign * exponent);
                if (position == compact.Length) break;
                char operation = compact[position++];
                if ((operation != '*' && operation != '/') || position == compact.Length) throw new ArgumentException("Invalid unit operation.");
                sign = operation == '/' ? -1 : 1;
            }
            return result;
        }
        private void Add(string symbol, int power)
        {
            bool exists = powers.TryGetValue(symbol, out int prior); int next = checked(prior + power);
            if (Math.Abs(next) > 16 || next != 0 && !exists && powers.Count >= 16) throw new ArgumentException("Unit expression exceeds its bound.");
            if (next == 0) powers.Remove(symbol); else powers[symbol] = next;
        }
        public bool Equivalent(FPWorkbenchFormulaUnits other)
        {
            if (!Known || !other.Known || powers.Count != other.powers.Count) return false;
            foreach (var pair in powers) if (!other.powers.TryGetValue(pair.Key, out int value) || value != pair.Value) return false;
            return true;
        }
        public static FPWorkbenchFormulaUnits Additive(FPWorkbenchFormulaUnits first, FPWorkbenchFormulaUnits second)
        {
            if (!first.Known || !second.Known) return Parse("");
            if (!first.Equivalent(second)) throw new ArgumentException("Addition/subtraction requires compatible declared units.");
            return first;
        }
        public static FPWorkbenchFormulaUnits Product(FPWorkbenchFormulaUnits first, FPWorkbenchFormulaUnits second, int sign)
        {
            if (!first.Known || !second.Known) return Parse("");
            var result = Parse("1");
            foreach (var pair in first.powers) result.Add(pair.Key, pair.Value);
            foreach (var pair in second.powers) result.Add(pair.Key, pair.Value * sign);
            return result;
        }
        public override string ToString()
        {
            if (!Known) return "unspecified";
            if (powers.Count == 0) return "1";
            var text = new StringBuilder();
            foreach (var pair in powers) { if (text.Length > 0) text.Append('*'); text.Append(pair.Key); if (pair.Value != 1) text.Append('^').Append(pair.Value.ToString(CultureInfo.InvariantCulture)); }
            return text.ToString();
        }
    }
}
