// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging;
using Models;
using Models.Helpers;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using Services.WebApi.Contracts;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Services
{
    public partial class GetRunExplanationHandler
    {
        private static class OwnerFriendlyFilterFormatter
        {
            private const string GenericCriteriaDescription = "the configured HR criteria";

            private static readonly Regex _codeAttributeRegex = new(
                @"\b(?<attribute>[A-Za-z_][A-Za-z0-9_]*_Code)\b",
                RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

            private static readonly Regex _predicateRegex = new(
                @"(?<attribute>\[?[A-Za-z_][A-Za-z0-9_]*\]?)\s*(?<operator>IS\s+NOT\s+NULL|IS\s+NULL|NOT\s+IN|NOT\s+LIKE|IN|LIKE|>=|<=|<>|!=|=|>|<)(?:\s*(?<value>\((?:[^()']|'(?:''|[^'])*')*\)|N?'(?:''|[^'])*'|[-+]?\d+(?:\.\d+)?|[A-Za-z_][A-Za-z0-9_.-]*))?",
                RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

            private static readonly Regex _valueRegex = new(
                @"N?'(?:''|[^'])*'|[^,]+",
                RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

            private static readonly IReadOnlyDictionary<string, string> _wordReplacements =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Cnt"] = "Count",
                    ["Dept"] = "Department",
                    ["Desc"] = "Description",
                    ["Id"] = "ID",
                    ["Ind"] = "Indicator",
                    ["Mgr"] = "Manager",
                    ["Nbr"] = "Number",
                    ["Num"] = "Number",
                    ["Org"] = "Organization"
                };

            public static HashSet<string> CollectCodeAttributes(IEnumerable<string?> filters)
            {
                var attributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var filter in filters)
                {
                    if (string.IsNullOrWhiteSpace(filter))
                    {
                        continue;
                    }

                    foreach (Match match in _codeAttributeRegex.Matches(filter))
                    {
                        attributes.Add(match.Groups["attribute"].Value);
                    }
                }

                return attributes;
            }

            public static string DescribeFilter(
                string? filter,
                IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? mappingDescriptions = null)
            {
                if (string.IsNullOrWhiteSpace(filter))
                {
                    return GenericCriteriaDescription;
                }

                var matches = _predicateRegex.Matches(filter);
                if (matches.Count == 0)
                {
                    return GenericCriteriaDescription;
                }

                var description = new StringBuilder();
                var previousEnd = 0;

                foreach (Match match in matches)
                {
                    if (!TryDescribeConnector(filter[previousEnd..match.Index], out var connector))
                    {
                        return GenericCriteriaDescription;
                    }

                    description.Append(connector);
                    description.Append(DescribePredicate(match, mappingDescriptions));
                    previousEnd = match.Index + match.Length;
                }

                if (!TryDescribeConnector(filter[previousEnd..], out var trailingConnector))
                {
                    return GenericCriteriaDescription;
                }

                description.Append(trailingConnector);

                var result = Regex.Replace(description.ToString(), @"\s+", " ").Trim();
                result = result.Replace("( ", "(", StringComparison.Ordinal)
                    .Replace(" )", ")", StringComparison.Ordinal);

                return string.IsNullOrWhiteSpace(result) ? GenericCriteriaDescription : result;
            }

            public static string HumanizeAttributeName(string attribute)
            {
                if (string.IsNullOrWhiteSpace(attribute))
                {
                    return "Attribute";
                }

                var name = attribute.Trim().Trim('[', ']');
                if (name.EndsWith("_Code", StringComparison.OrdinalIgnoreCase))
                {
                    name = name[..^"_Code".Length];
                }

                name = name.Replace('_', ' ');
                name = Regex.Replace(name, @"([a-z0-9])([A-Z])", "$1 $2");
                name = Regex.Replace(name, @"([A-Z]+)([A-Z][a-z])", "$1 $2");

                var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(word =>
                    {
                        if (_wordReplacements.TryGetValue(word, out var replacement))
                        {
                            return replacement;
                        }

                        if (word.All(char.IsUpper))
                        {
                            return word;
                        }

                        return char.ToUpperInvariant(word[0]) + word[1..];
                    });

                return string.Join(" ", words);
            }

            public static string DescribeAttributeValue(
                string attribute,
                string? value,
                IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? mappingDescriptions = null)
            {
                return DescribeValue(attribute, value ?? string.Empty, mappingDescriptions);
            }

            private static string DescribePredicate(
                Match match,
                IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? mappingDescriptions)
            {
                var attribute = match.Groups["attribute"].Value.Trim('[', ']');
                var displayName = HumanizeAttributeName(attribute);
                var normalizedOperator = Regex.Replace(match.Groups["operator"].Value, @"\s+", " ")
                    .ToUpperInvariant();

                if (normalizedOperator == "IS NULL")
                {
                    return $"{displayName} has no value";
                }

                if (normalizedOperator == "IS NOT NULL")
                {
                    return $"{displayName} has a value";
                }

                var rawValue = match.Groups["value"].Success
                    ? match.Groups["value"].Value
                    : string.Empty;

                if (normalizedOperator is "LIKE" or "NOT LIKE")
                {
                    return DescribeLikePredicate(displayName, normalizedOperator, rawValue);
                }

                var values = ParseValues(rawValue)
                    .Select(value => DescribeValue(attribute, value, mappingDescriptions))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (values.Count == 0)
                {
                    values.Add(attribute.EndsWith("_Code", StringComparison.OrdinalIgnoreCase)
                        ? "a configured value whose description is unavailable"
                        : "an unspecified value");
                }

                var formattedValue = JoinValues(values);
                var operatorText = normalizedOperator switch
                {
                    "=" => "is",
                    "<>" or "!=" => "is not",
                    ">=" => "is at least",
                    "<=" => "is at most",
                    ">" => "is greater than",
                    "<" => "is less than",
                    "IN" => "is one of",
                    "NOT IN" => "is not one of",
                    _ => "matches"
                };

                return $"{displayName} {operatorText} {formattedValue}";
            }

            private static string DescribeLikePredicate(string displayName, string normalizedOperator, string rawValue)
            {
                var value = Unquote(rawValue);
                var startsWithWildcard = value.StartsWith('%');
                var endsWithWildcard = value.EndsWith('%');
                var literal = value.Trim('%');
                var quoted = Quote(literal);
                var negated = normalizedOperator == "NOT LIKE";

                if (startsWithWildcard && endsWithWildcard)
                {
                    return $"{displayName} {(negated ? "does not contain" : "contains")} {quoted}";
                }

                if (startsWithWildcard)
                {
                    return $"{displayName} {(negated ? "does not end with" : "ends with")} {quoted}";
                }

                if (endsWithWildcard)
                {
                    return $"{displayName} {(negated ? "does not start with" : "starts with")} {quoted}";
                }

                return $"{displayName} {(negated ? "does not match" : "matches")} {quoted}";
            }

            private static List<string> ParseValues(string rawValue)
            {
                if (string.IsNullOrWhiteSpace(rawValue))
                {
                    return new List<string>();
                }

                var valueList = rawValue.Trim();
                if (valueList.StartsWith('(') && valueList.EndsWith(')'))
                {
                    valueList = valueList[1..^1];
                }

                return _valueRegex.Matches(valueList)
                    .Select(match => match.Value.Trim())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToList();
            }

            private static string DescribeValue(
                string attribute,
                string rawValue,
                IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? mappingDescriptions)
            {
                var value = Unquote(rawValue);
                var isCode = attribute.EndsWith("_Code", StringComparison.OrdinalIgnoreCase);

                if (isCode)
                {
                    var baseAttribute = attribute[..^"_Code".Length];
                    if (TryGetMapping(mappingDescriptions, attribute, baseAttribute, out var mappings)
                        && mappings.TryGetValue(value, out var description)
                        && !string.IsNullOrWhiteSpace(description))
                    {
                        return Quote(description);
                    }

                    return "a configured value whose description is unavailable";
                }

                if (IsBooleanAttribute(attribute))
                {
                    if (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase))
                    {
                        return "Yes";
                    }

                    if (value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase))
                    {
                        return "No";
                    }
                }

                if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
                {
                    return value;
                }

                return Quote(value);
            }

            private static bool TryGetMapping(
                IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? mappingDescriptions,
                string attribute,
                string baseAttribute,
                out IReadOnlyDictionary<string, string> mappings)
            {
                if (mappingDescriptions != null
                    && mappingDescriptions.TryGetValue(attribute, out var attributeMappings)
                    && attributeMappings != null)
                {
                    mappings = attributeMappings;
                    return true;
                }

                if (mappingDescriptions != null
                    && mappingDescriptions.TryGetValue(baseAttribute, out var baseAttributeMappings)
                    && baseAttributeMappings != null)
                {
                    mappings = baseAttributeMappings;
                    return true;
                }

                mappings = new Dictionary<string, string>();
                return false;
            }

            private static bool IsBooleanAttribute(string attribute)
            {
                return attribute.EndsWith("Ind", StringComparison.OrdinalIgnoreCase)
                    || attribute.EndsWith("Indicator", StringComparison.OrdinalIgnoreCase)
                    || attribute.EndsWith("Flag", StringComparison.OrdinalIgnoreCase);
            }

            private static string Unquote(string value)
            {
                var result = value.Trim();
                if (result.StartsWith("N'", StringComparison.OrdinalIgnoreCase) && result.EndsWith('\''))
                {
                    result = result[2..^1];
                }
                else if (result.StartsWith('\'') && result.EndsWith('\''))
                {
                    result = result[1..^1];
                }

                return result.Replace("''", "'", StringComparison.Ordinal);
            }

            private static string Quote(string value)
            {
                return $"\"{value.Replace("\"", "'", StringComparison.Ordinal)}\"";
            }

            private static string JoinValues(IReadOnlyList<string> values)
            {
                return values.Count switch
                {
                    0 => string.Empty,
                    1 => values[0],
                    2 => $"{values[0]} or {values[1]}",
                    _ => $"{string.Join(", ", values.Take(values.Count - 1))}, or {values[^1]}"
                };
            }

            private static bool TryDescribeConnector(string connector, out string description)
            {
                var withoutLogicalOperators = Regex.Replace(connector, @"\bAND\b|\bOR\b", string.Empty, RegexOptions.IgnoreCase);
                withoutLogicalOperators = withoutLogicalOperators
                    .Replace("(", string.Empty, StringComparison.Ordinal)
                    .Replace(")", string.Empty, StringComparison.Ordinal);

                if (!string.IsNullOrWhiteSpace(withoutLogicalOperators))
                {
                    description = string.Empty;
                    return false;
                }

                description = Regex.Replace(connector, @"\bAND\b", " and ", RegexOptions.IgnoreCase);
                description = Regex.Replace(description, @"\bOR\b", " or ", RegexOptions.IgnoreCase);
                return true;
            }
        }

        private static class OwnerFriendlyFilterMappingResolver
        {
            public static async Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> ResolveAsync(
                IEnumerable<string?> filters,
                Guid? runAdfRunId,
                ISqlMembershipRepository sqlMembershipRepository,
                IDataFactoryRepository dataFactoryRepository,
                ILogger logger)
            {
                var unresolvedAttributes = OwnerFriendlyFilterFormatter.CollectCodeAttributes(filters);
                var resolved = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

                if (unresolvedAttributes.Count == 0)
                {
                    return resolved;
                }

                var historicalTable = runAdfRunId.HasValue && runAdfRunId.Value != Guid.Empty
                    ? runAdfRunId.Value.ToString().Replace("-", string.Empty)
                    : null;

                if (!string.IsNullOrWhiteSpace(historicalTable))
                {
                    await LoadMappingsAsync(
                        historicalTable,
                        unresolvedAttributes,
                        resolved,
                        sqlMembershipRepository,
                        logger);
                }

                if (unresolvedAttributes.Count > 0)
                {
                    try
                    {
                        var latestAdfRunId = await dataFactoryRepository.GetMostRecentSucceededRunIdAsync();
                        if (!string.IsNullOrWhiteSpace(latestAdfRunId))
                        {
                            var latestTable = latestAdfRunId.Replace("-", string.Empty);
                            if (!string.Equals(latestTable, historicalTable, StringComparison.OrdinalIgnoreCase))
                            {
                                await LoadMappingsAsync(
                                    latestTable,
                                    unresolvedAttributes,
                                    resolved,
                                    sqlMembershipRepository,
                                    logger);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to resolve the latest ADF mappings table for owner-friendly AI criteria.");
                    }
                }

                return resolved;
            }

            private static async Task LoadMappingsAsync(
                string tableName,
                HashSet<string> unresolvedAttributes,
                Dictionary<string, IReadOnlyDictionary<string, string>> resolved,
                ISqlMembershipRepository sqlMembershipRepository,
                ILogger logger)
            {
                bool tableExists;
                try
                {
                    tableExists = await sqlMembershipRepository.CheckIfMappingsTableExistsAsync(tableName);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to verify ADF mappings table {TableName}; mapped filter descriptions will use the next available table.", tableName);
                    return;
                }

                if (!tableExists)
                {
                    return;
                }

                foreach (var codeAttribute in unresolvedAttributes.ToList())
                {
                    var baseAttribute = codeAttribute[..^"_Code".Length];
                    try
                    {
                        var mappings = await sqlMembershipRepository.GetAttributeMappingsAsync(baseAttribute, tableName);
                        var descriptionsByCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                        foreach (var mapping in mappings)
                        {
                            if (string.IsNullOrWhiteSpace(mapping.Code)
                                || string.IsNullOrWhiteSpace(mapping.Description))
                            {
                                continue;
                            }

                            descriptionsByCode.TryAdd(mapping.Code.Trim(), mapping.Description.Trim());
                        }

                        if (descriptionsByCode.Count > 0)
                        {
                            resolved[codeAttribute] = descriptionsByCode;
                            unresolvedAttributes.Remove(codeAttribute);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(
                            ex,
                            "Failed to load mapping descriptions for {Attribute} from {TableName}; owner-friendly criteria will not expose the raw code.",
                            baseAttribute,
                            tableName);
                    }
                }
            }
        }
    }
}
