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
