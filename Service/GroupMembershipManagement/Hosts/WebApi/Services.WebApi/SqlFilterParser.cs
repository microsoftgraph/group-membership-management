// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Services
{
    /// <summary>
    /// Extracts the attributes and literal values referenced by a SQL WHERE-clause filter using the
    /// ScriptDom T-SQL parser. Parsing is explicit: a filter that cannot be read reports an error
    /// instead of being silently half-extracted.
    /// </summary>
    internal static class SqlFilterParser
    {
        /// <summary>
        /// Upper bound on the filter text handed to the parser. Real filters are orders of magnitude
        /// smaller; the cap exists so an oversized payload cannot drive parser work unbounded.
        /// </summary>
        public const int MaxFilterLength = 10000;

        /// <summary>
        /// Upper bound on parenthesis nesting. ScriptDom is a recursive-descent parser, so a deeply
        /// nested expression recurses once per level and can exhaust the stack. A StackOverflowException
        /// cannot be caught in .NET and would take the whole WebApi process down, so depth is rejected
        /// before parsing rather than handled after it.
        /// </summary>
        public const int MaxNestingDepth = 32;

        public static bool TryParse(
            string? filter,
            out IReadOnlyDictionary<string, IReadOnlyCollection<string>> attributeValues,
            out string error)
        {
            attributeValues = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(filter))
            {
                error = "The filter is empty.";
                return false;
            }

            if (filter.Length > MaxFilterLength)
            {
                error = $"The filter is longer than the {MaxFilterLength} character limit.";
                return false;
            }

            if (ExceedsNestingLimit(filter))
            {
                error = $"The filter nests more than {MaxNestingDepth} levels deep.";
                return false;
            }

            var parser = new TSql150Parser(false);
            using var reader = new StringReader($"SELECT 1 WHERE {filter}");
            var fragment = parser.Parse(reader, out IList<ParseError> errors);

            if (errors.Count > 0)
            {
                error = errors[0].Message;
                return false;
            }

            // Parse accepts a whole batch, so a filter carrying a statement separator would otherwise
            // contribute attributes from trailing statements. Only the synthetic WHERE clause is read.
            if (fragment is not TSqlScript script
                || script.Batches.Count != 1
                || script.Batches[0].Statements.Count != 1
                || script.Batches[0].Statements[0] is not SelectStatement selectStatement
                || selectStatement.QueryExpression is not QuerySpecification querySpecification
                || querySpecification.WhereClause is null)
            {
                error = "The filter must be a single WHERE-clause expression.";
                return false;
            }

            var visitor = new FilterVisitor();
            querySpecification.WhereClause.Accept(visitor);

            attributeValues = visitor.AttributeValues.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyCollection<string>)pair.Value.ToList(),
                StringComparer.OrdinalIgnoreCase);

            return true;
        }

        /// <summary>
        /// Measures parenthesis depth without parsing. Characters inside string literals are skipped
        /// so a legitimate value such as 'Contoso (EMEA)' is not mistaken for nesting.
        /// </summary>
        private static bool ExceedsNestingLimit(string filter)
        {
            var depth = 0;
            var inLiteral = false;

            for (var index = 0; index < filter.Length; index++)
            {
                var character = filter[index];

                if (inLiteral)
                {
                    if (character != '\'')
                    {
                        continue;
                    }

                    // '' is an escaped quote inside a literal, not the end of it.
                    if (index + 1 < filter.Length && filter[index + 1] == '\'')
                    {
                        index++;
                        continue;
                    }

                    inLiteral = false;
                    continue;
                }

                switch (character)
                {
                    case '\'':
                        inLiteral = true;
                        break;
                    case '(':
                        if (++depth > MaxNestingDepth)
                        {
                            return true;
                        }
                        break;
                    case ')':
                        if (depth > 0)
                        {
                            depth--;
                        }
                        break;
                }
            }

            return false;
        }

        private sealed class FilterVisitor : TSqlFragmentVisitor
        {
            public Dictionary<string, HashSet<string>> AttributeValues { get; } =
                new(StringComparer.OrdinalIgnoreCase);

            public override void Visit(ColumnReferenceExpression node)
            {
                TrackAttribute(node);
            }

            public override void Visit(BooleanComparisonExpression node)
            {
                TrackPair(node.FirstExpression, node.SecondExpression);
                TrackPair(node.SecondExpression, node.FirstExpression);
            }

            public override void Visit(InPredicate node)
            {
                var attribute = GetAttributeName(node.Expression);
                if (attribute == null || node.Values == null)
                {
                    return;
                }

                foreach (var value in node.Values)
                {
                    TrackValue(attribute, value);
                }
            }

            private void TrackPair(ScalarExpression? attributeSide, ScalarExpression? valueSide)
            {
                var attribute = GetAttributeName(attributeSide);
                if (attribute == null)
                {
                    return;
                }

                TrackValue(attribute, valueSide);
            }

            private void TrackAttribute(ColumnReferenceExpression? node)
            {
                var attribute = GetAttributeName(node);
                if (attribute != null)
                {
                    GetValues(attribute);
                }
            }

            private void TrackValue(string attribute, ScalarExpression? expression)
            {
                var values = GetValues(attribute);

                if (expression is Literal literal
                    && literal is not NullLiteral
                    && !string.IsNullOrWhiteSpace(literal.Value))
                {
                    values.Add(literal.Value);
                }
            }

            private HashSet<string> GetValues(string attribute)
            {
                if (!AttributeValues.TryGetValue(attribute, out var values))
                {
                    values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    AttributeValues[attribute] = values;
                }

                return values;
            }

            private static string? GetAttributeName(ScalarExpression? expression)
            {
                var identifiers = (expression as ColumnReferenceExpression)?.MultiPartIdentifier?.Identifiers;
                return identifiers != null && identifiers.Count > 0
                    ? identifiers[^1].Value
                    : null;
            }
        }
    }
}
