// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Security.Cryptography;
    using System.Text;
    using UnityEngine;

    [Serializable] public sealed class FPWorkbenchFormulaReference
    {
        public string Token, SourceColumnId, DerivedId;
        public int SourceColumn = -1;
    }
    [Serializable] public sealed class FPWorkbenchDerivedColumn
    {
        public string Id = Guid.NewGuid().ToString("N"), Name = "Calculated", Expression = "C1 + C2", ExpectedUnits = "";
        public List<FPWorkbenchFormulaReference> References = new List<FPWorkbenchFormulaReference>();
    }
    [Serializable] public sealed class FPWorkbenchSourceUnit
    {
        public int Column;
        public string Units = "";
    }
    [Serializable] public sealed class FPWorkbenchFormulaSet
    {
        public int Version = 1;
        public string DatasetId;
        public List<FPWorkbenchDerivedColumn> Columns = new List<FPWorkbenchDerivedColumn>();
        public List<FPWorkbenchSourceUnit> SourceUnits = new List<FPWorkbenchSourceUnit>();
        public bool HasTypedSchema;
        public FPWorkbenchTypedSchema TypedSchema;
    }
    public enum FPWorkbenchFormulaStatus { Valid, Missing, NonNumeric, DivideByZero, NonFinite, IntegerPrecision }
    public readonly struct FPWorkbenchFormulaValue
    {
        public double Number { get; }
        public FPWorkbenchFormulaStatus Status { get; }
        public bool IsValid => Status == FPWorkbenchFormulaStatus.Valid;
        public FPWorkbenchFormulaValue(double number, FPWorkbenchFormulaStatus status = FPWorkbenchFormulaStatus.Valid) { Number = number; Status = status; }
        public override string ToString() => IsValid ? Number.ToString("R", CultureInfo.InvariantCulture) : "#" + Status.ToString().ToUpperInvariant();
    }
    /// <summary>Bounded arithmetic expressions with stable source/derived references; no code evaluation or implicit resampling.</summary>
    public sealed class FPWorkbenchFormulaProgram
    {
        private readonly FPWorkbenchFormulaSet definition;
        private readonly int sourceColumns;
        private readonly FPWorkbenchCompiledSchema typedSchema;
        public string TypedSchemaFingerprint => typedSchema?.Fingerprint ?? "none";
        private readonly Dictionary<string, int> derived = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly FPWorkbenchFormulaUnits[] sourceUnits, resultUnits;
        private readonly Node[] roots;
        public string DatasetId => definition.DatasetId;
        public string Fingerprint { get; private set; }
        public int ColumnCount => roots.Length;
        public FPWorkbenchFormulaSet Definition => JsonUtility.FromJson<FPWorkbenchFormulaSet>(JsonUtility.ToJson(definition));
        public string Name(int index) => definition.Columns[index].Name;
        public string Expression(int index) => definition.Columns[index].Expression;
        public string ColumnId(int index) => definition.Columns[index].Id;
        public string SourceUnits(int index)
        { if (index < 0 || index >= sourceColumns) throw new ArgumentOutOfRangeException(nameof(index)); return sourceUnits[index].ToString(); }
        public string Units(int index) => resultUnits[index].ToString();
        /// <summary>Sorted distinct physical columns contributing to one calculated value on the same source row.</summary>
        public int[] SourceDependencies(int column)
        {
            if (column < 0 || column >= ColumnCount) throw new ArgumentOutOfRangeException(nameof(column));
            var sources = new SortedSet<int>(); var visited = new HashSet<int>();
            void Visit(int index)
            {
                if (!visited.Add(index)) return;
                foreach (var reference in definition.Columns[index].References)
                {
                    if (reference.SourceColumn >= 0) sources.Add(reference.SourceColumn);
                    else if (!string.IsNullOrEmpty(reference.DerivedId)) Visit(derived[reference.DerivedId]);
                }
            }
            Visit(column); var result = new int[sources.Count]; sources.CopyTo(result); return result;
        }
        // Compare decimal digits before conversion: Int64 parsing alone misses larger IDs.
        private static bool InexactInteger(string text)
        {
            text = text.Trim(); int start = text.Length > 0 && (text[0] == '+' || text[0] == '-') ? 1 : 0;
            if (start == text.Length) return false;
            for (int i = start; i < text.Length; i++) if (text[i] < '0' || text[i] > '9') return false;
            while (start < text.Length && text[start] == '0') start++;
            const string limit = "9007199254740992";
            int length = text.Length - start;
            return length > limit.Length || length == limit.Length && string.CompareOrdinal(text, start, limit, 0, length) > 0;
        }
        public static FPWorkbenchFormulaProgram Compile(FPWorkbenchCsvDataset dataset, FPWorkbenchFormulaSet formulas)
        {
            if (dataset == null || formulas == null) throw new ArgumentNullException();
            return new FPWorkbenchFormulaProgram(dataset.DatasetId, dataset.ColumnCount, formulas);
        }
        public static FPWorkbenchFormulaProgram Compile(FPWorkbenchCsvDataset dataset, FPWorkbenchFormulaSet formulas, FPWorkbenchCompiledSchema schema)
        {
            if (dataset == null || formulas == null || schema == null) throw new ArgumentNullException();
            if (schema.DatasetId != dataset.DatasetId || schema.SourceColumnCount != dataset.ColumnCount) throw new ArgumentException("Formula typed schema source differs.");
            var snapshot = JsonUtility.FromJson<FPWorkbenchFormulaSet>(JsonUtility.ToJson(formulas));
            snapshot.HasTypedSchema = true; snapshot.TypedSchema = schema.Definition;
            return Compile(dataset, snapshot);
        }
        public static FPWorkbenchFormulaProgram CompileSchema(string datasetId, int maximumColumns, FPWorkbenchFormulaSet formulas)
        {
            if (string.IsNullOrEmpty(datasetId) || datasetId.Length > 256 || maximumColumns < 1 || maximumColumns > 4096 || formulas == null) throw new ArgumentException("Invalid bounded formula schema.");
            return new FPWorkbenchFormulaProgram(datasetId, maximumColumns, formulas);
        }
        private FPWorkbenchFormulaProgram(string datasetId, int count, FPWorkbenchFormulaSet formulas)
        {
            // JsonUtility can materialize a default object for a null serializable field.
            if (formulas.HasTypedSchema && formulas.TypedSchema == null) throw new ArgumentException("Formula typed schema snapshot is missing.");
            definition = JsonUtility.FromJson<FPWorkbenchFormulaSet>(JsonUtility.ToJson(formulas)); sourceColumns = count;
            if (definition.Version != 1 || definition.DatasetId != datasetId || definition.Columns == null || definition.Columns.Count > 64 || definition.SourceUnits == null || definition.SourceUnits.Count > count) throw new ArgumentException("Invalid formula source/version or definition bounds.");
            if (definition.HasTypedSchema) {
                if (definition.TypedSchema == null) throw new ArgumentException("Formula typed schema snapshot is missing.");
                typedSchema = definition.TypedSchema.CompileBounded(datasetId, count); definition.TypedSchema = typedSchema.Definition;
            }
            else {
                if (definition.TypedSchema != null && !string.IsNullOrEmpty(definition.TypedSchema.DatasetId)) throw new ArgumentException("Formula typed schema presence marker contradicts its snapshot.");
                definition.TypedSchema = null;
            }
            roots = new Node[definition.Columns.Count]; resultUnits = new FPWorkbenchFormulaUnits[roots.Length]; sourceUnits = new FPWorkbenchFormulaUnits[count];
            for (int i = 0; i < count; i++) sourceUnits[i] = FPWorkbenchFormulaUnits.Parse("");
            var unitColumns = new HashSet<int>();
            foreach (var unit in definition.SourceUnits)
            {
                if (unit == null || unit.Column < 0 || unit.Column >= count || !unitColumns.Add(unit.Column)) throw new ArgumentException("Invalid/duplicate source unit declaration.");
                sourceUnits[unit.Column] = FPWorkbenchFormulaUnits.Parse(unit.Units);
            }
            if (typedSchema != null) foreach (var column in definition.TypedSchema.Columns) {
                var units = FPWorkbenchFormulaUnits.Parse(column.Units);
                if (units.Known && sourceUnits[column.Column].Known && !units.Equivalent(sourceUnits[column.Column])) throw new ArgumentException("Typed and formula source unit declarations differ.");
                if (units.Known) sourceUnits[column.Column] = units;
            }
            for (int i = 0; i < roots.Length; i++)
            {
                var column = definition.Columns[i];
                if (column == null || string.IsNullOrEmpty(column.Id) || column.Id.Length > 128 || string.IsNullOrEmpty(column.Name) || column.Name.Length > 256 || string.IsNullOrWhiteSpace(column.Expression) || column.Expression.Length > 4096 || column.References == null || column.References.Count > 128 || !derived.TryAdd(column.Id, i)) throw new ArgumentException("Invalid/duplicate derived-column definition.");
            }
            for (int i = 0; i < roots.Length; i++) roots[i] = new Parser(this, definition.Columns[i]).Parse();
            var visiting = new bool[roots.Length];
            for (int i = 0; i < roots.Length; i++) ResolveUnits(i, visiting);
            using var sha = SHA256.Create(); Fingerprint = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(JsonUtility.ToJson(definition)))).Replace("-", "").ToLowerInvariant();
        }
        private FPWorkbenchFormulaUnits ResolveUnits(int column, bool[] visiting)
        {
            if (resultUnits[column] != null) return resultUnits[column];
            if (visiting[column]) throw new ArgumentException("Calculated columns contain a dependency cycle.");
            visiting[column] = true; var unit = roots[column].Units(this, visiting);
            var expected = FPWorkbenchFormulaUnits.Parse(definition.Columns[column].ExpectedUnits);
            if (expected.Known && !expected.Equivalent(unit)) throw new ArgumentException("Calculated output units do not match the declared inputs/result.");
            visiting[column] = false; return resultUnits[column] = unit;
        }
        public Evaluator CreateEvaluator(FPWorkbenchChangeSnapshot changes = null)
        {
            if (changes != null && changes.DatasetId != DatasetId) throw new ArgumentException("Formula correction source differs.");
            return new Evaluator(this, changes);
        }
        public sealed class Evaluator
        {
            private readonly FPWorkbenchFormulaProgram program;
            private readonly FPWorkbenchChangeSnapshot changes;
            private readonly FPWorkbenchFormulaValue[] sourceValues, derivedValues;
            private readonly bool[] sourceReady, derivedReady;
            private string[] row;
            private long rowIndex;
            internal Evaluator(FPWorkbenchFormulaProgram program, FPWorkbenchChangeSnapshot changes)
            {
                this.program = program; this.changes = changes;
                sourceValues = new FPWorkbenchFormulaValue[program.sourceColumns]; sourceReady = new bool[sourceValues.Length];
                derivedValues = new FPWorkbenchFormulaValue[program.ColumnCount]; derivedReady = new bool[derivedValues.Length];
            }
            public void SetRow(string[] originalValues, long index)
            {
                if (originalValues == null || originalValues.Length != program.sourceColumns || index < 0) throw new ArgumentException("Formula row shape/identity differs from source.");
                row = originalValues; rowIndex = index;
                Array.Clear(sourceReady, 0, sourceReady.Length); Array.Clear(derivedReady, 0, derivedReady.Length);
            }
            public FPWorkbenchFormulaValue Source(int column)
            {
                if (row == null) throw new InvalidOperationException("Set an input row first.");
                if (sourceReady[column]) return sourceValues[column];
                string value = row[column];
                bool qualityMissing = false;
                if (changes != null && changes.TryGet(new FPWorkbenchCell(program.DatasetId, rowIndex, column), value, out string effective, out bool masked, out var kind))
                { value = effective; qualityMissing = masked || kind == FPWorkbenchEditKind.Missing; }
                FPWorkbenchFormulaValue result;
                if (qualityMissing) result = new FPWorkbenchFormulaValue(0, FPWorkbenchFormulaStatus.Missing);
                else if (program.typedSchema != null && program.typedSchema.TryGetParser(column, out var typedParser)) result = ReadTyped(typedParser.Parse(value));
                else if (string.IsNullOrWhiteSpace(value)) result = new FPWorkbenchFormulaValue(0, FPWorkbenchFormulaStatus.Missing);
                else if (InexactInteger(value)) result = new FPWorkbenchFormulaValue(0, FPWorkbenchFormulaStatus.IntegerPrecision);
                else if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)) result = new FPWorkbenchFormulaValue(0, FPWorkbenchFormulaStatus.NonNumeric);
                else result = FPWorkbenchSeriesBinding.Finite(number) ? new FPWorkbenchFormulaValue(number) : new FPWorkbenchFormulaValue(0, FPWorkbenchFormulaStatus.NonFinite);
                sourceReady[column] = true; return sourceValues[column] = result;
            }
            private static FPWorkbenchFormulaValue ReadTyped(FPWorkbenchTypedValue value)
            {
                if (value.State == FPWorkbenchValueState.Missing || value.State == FPWorkbenchValueState.Masked) return new FPWorkbenchFormulaValue(0, FPWorkbenchFormulaStatus.Missing);
                if (value.State != FPWorkbenchValueState.Valid) return new FPWorkbenchFormulaValue(0, FPWorkbenchFormulaStatus.NonNumeric);
                switch (value.Type) {
                    case FPWorkbenchColumnType.Real: return new FPWorkbenchFormulaValue(value.RealNumber);
                    case FPWorkbenchColumnType.SignedInteger:
                        return value.SignedInteger < -9007199254740992L || value.SignedInteger > 9007199254740992L
                            ? new FPWorkbenchFormulaValue(0, FPWorkbenchFormulaStatus.IntegerPrecision) : new FPWorkbenchFormulaValue(value.SignedInteger);
                    case FPWorkbenchColumnType.UnsignedInteger:
                        return value.UnsignedInteger > 9007199254740992UL ? new FPWorkbenchFormulaValue(0, FPWorkbenchFormulaStatus.IntegerPrecision) : new FPWorkbenchFormulaValue(value.UnsignedInteger);
                    default: return new FPWorkbenchFormulaValue(0, FPWorkbenchFormulaStatus.NonNumeric);
                }
            }
            public FPWorkbenchFormulaValue Derived(int column)
            {
                if (row == null) throw new InvalidOperationException("Set an input row first.");
                if (!derivedReady[column]) { derivedValues[column] = program.roots[column].Evaluate(this); derivedReady[column] = true; }
                return derivedValues[column];
            }
        }
        private abstract class Node
        {
            public abstract FPWorkbenchFormulaValue Evaluate(Evaluator row);
            public abstract FPWorkbenchFormulaUnits Units(FPWorkbenchFormulaProgram program, bool[] visiting);
        }
        private sealed class Constant : Node
        {
            public double Value;
            public override FPWorkbenchFormulaValue Evaluate(Evaluator row) => new FPWorkbenchFormulaValue(Value);
            public override FPWorkbenchFormulaUnits Units(FPWorkbenchFormulaProgram program, bool[] visiting) => FPWorkbenchFormulaUnits.Parse("1");
        }
        private sealed class Reference : Node
        {
            public int Index;
            public bool IsDerived;
            public override FPWorkbenchFormulaValue Evaluate(Evaluator row) => IsDerived ? row.Derived(Index) : row.Source(Index);
            public override FPWorkbenchFormulaUnits Units(FPWorkbenchFormulaProgram program, bool[] visiting) => IsDerived ? program.ResolveUnits(Index, visiting) : program.sourceUnits[Index];
        }
        private sealed class Operation : Node
        {
            public char Operator;
            public Node Left, Right;
            public override FPWorkbenchFormulaValue Evaluate(Evaluator row)
            {
                var first = Left.Evaluate(row); if (!first.IsValid) return first;
                if (Right == null) return new FPWorkbenchFormulaValue(-first.Number);
                var second = Right.Evaluate(row); if (!second.IsValid) return second;
                if (Operator == '/' && second.Number == 0) return new FPWorkbenchFormulaValue(0, FPWorkbenchFormulaStatus.DivideByZero);
                double value = Operator == '+' ? first.Number + second.Number : Operator == '-' ? first.Number - second.Number : Operator == '*' ? first.Number * second.Number : first.Number / second.Number;
                return FPWorkbenchSeriesBinding.Finite(value) ? new FPWorkbenchFormulaValue(value) : new FPWorkbenchFormulaValue(0, FPWorkbenchFormulaStatus.NonFinite);
            }
            public override FPWorkbenchFormulaUnits Units(FPWorkbenchFormulaProgram program, bool[] visiting)
            {
                var first = Left.Units(program, visiting);
                if (Right == null) return first;
                var second = Right.Units(program, visiting);
                return Operator == '+' || Operator == '-' ? FPWorkbenchFormulaUnits.Additive(first, second) : FPWorkbenchFormulaUnits.Product(first, second, Operator == '/' ? -1 : 1);
            }
        }
        private sealed class Parser
        {
            private readonly FPWorkbenchFormulaProgram program;
            private readonly FPWorkbenchDerivedColumn column;
            private int position, depth, nodes;
            public Parser(FPWorkbenchFormulaProgram program, FPWorkbenchDerivedColumn column) { this.program = program; this.column = column; }
            public Node Parse()
            {
                var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var binding in column.References) if (binding == null || string.IsNullOrEmpty(binding.Token) || !tokens.Add(binding.Token)) throw new ArgumentException("Invalid/duplicate formula reference binding.");
                Node result = Expression(); Space();
                if (position != column.Expression.Length) throw new ArgumentException("Unexpected formula text at " + position + ". Only arithmetic and C1/D1 references are supported.");
                return result;
            }
            private void Space() { while (position < column.Expression.Length && char.IsWhiteSpace(column.Expression[position])) position++; }
            private bool Take(char value) { Space(); if (position >= column.Expression.Length || column.Expression[position] != value) return false; position++; return true; }
            private Node Expression()
            {
                Node node = Product();
                while (true) { if (Take('+')) node = Binary('+', node, Product()); else if (Take('-')) node = Binary('-', node, Product()); else return node; }
            }
            private Node Product()
            {
                Node node = Primary();
                while (true) { if (Take('*')) node = Binary('*', node, Primary()); else if (Take('/')) node = Binary('/', node, Primary()); else return node; }
            }
            private Node Binary(char operation, Node left, Node right)
            { Bound(); return new Operation { Operator = operation, Left = left, Right = right }; }
            private void Bound() { if (++nodes > 128) throw new ArgumentException("Formula exceeds 128 expression nodes."); }
            private Node Primary()
            {
                if (++depth > 32) throw new ArgumentException("Formula nesting exceeds 32 levels.");
                try
                {
                    if (Take('+')) return Primary();
                    if (Take('-')) { Bound(); return new Operation { Operator = '-', Left = Primary() }; }
                    if (Take('(')) { Node value = Expression(); if (!Take(')')) throw new ArgumentException("Missing closing formula parenthesis."); return value; }
                    Space(); int start = position;
                    if (position < column.Expression.Length && char.IsLetter(column.Expression[position]))
                    {
                        position++; while (position < column.Expression.Length && char.IsDigit(column.Expression[position])) position++;
                        string token = column.Expression.Substring(start, position - start).ToUpperInvariant();
                        if (token.Length < 2 || (token[0] != 'C' && token[0] != 'D') || !int.TryParse(token.Substring(1), out int ordinal) || ordinal < 1) throw new ArgumentException("Use C1, C2… for source columns or D1, D2… for calculated columns.");
                        var binding = column.References.Find(r => string.Equals(r.Token, token, StringComparison.OrdinalIgnoreCase));
                        if (binding == null)
                        {
                            binding = new FPWorkbenchFormulaReference { Token = token };
                            if (token[0] == 'C') { binding.SourceColumn = ordinal - 1; binding.SourceColumnId = new FPWorkbenchCell(program.DatasetId, 0, ordinal - 1).ColumnId; }
                            else { if (ordinal > program.definition.Columns.Count) throw new ArgumentException("Calculated-column reference is out of range."); binding.DerivedId = program.definition.Columns[ordinal - 1].Id; }
                            column.References.Add(binding);
                        }
                        Bound();
                        if (!string.IsNullOrEmpty(binding.DerivedId))
                        { if (!program.derived.TryGetValue(binding.DerivedId, out int target)) throw new ArgumentException("Calculated reference target was removed."); return new Reference { IsDerived = true, Index = target }; }
                        if (binding.SourceColumn < 0 || binding.SourceColumn >= program.sourceColumns || binding.SourceColumnId != new FPWorkbenchCell(program.DatasetId, 0, binding.SourceColumn).ColumnId) throw new ArgumentException("Source-column reference identity differs from the dataset.");
                        return new Reference { Index = binding.SourceColumn };
                    }
                    while (position < column.Expression.Length && (char.IsDigit(column.Expression[position]) || column.Expression[position] == '.')) position++;
                    if (position < column.Expression.Length && (column.Expression[position] == 'e' || column.Expression[position] == 'E'))
                    { position++; if (position < column.Expression.Length && (column.Expression[position] == '-' || column.Expression[position] == '+')) position++; while (position < column.Expression.Length && char.IsDigit(column.Expression[position])) position++; }
                    string literal = column.Expression.Substring(start, position - start);
                    if (!double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || !FPWorkbenchSeriesBinding.Finite(number)) throw new ArgumentException("Invalid finite numeric literal at " + start + ".");
                    if (InexactInteger(literal)) throw new ArgumentException("Integer literal exceeds exact double precision.");
                    Bound(); return new Constant { Value = number };
                }
                finally { depth--; }
            }
        }
    }
}
