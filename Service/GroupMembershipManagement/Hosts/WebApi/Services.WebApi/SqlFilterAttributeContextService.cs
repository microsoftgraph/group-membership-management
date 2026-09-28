// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts;
using Services.WebApi.Contracts;
using System.Text;
using System.Text.RegularExpressions;

namespace Services
{
    public class SqlFilterAttributeContextService : ISqlFilterAttributeContextService
    {
        private const string CodeSuffix = "_Code";
        private const int MaxPromptValueLength = 200;

        private readonly ILogger<SqlFilterAttributeContextService> _logger;
        private readonly IDatabaseSqlMembershipSourcesRepository _databaseSqlMembershipSourcesRepository;
        private readonly IDataFactoryRepository _dataFactoryRepository;
        private readonly ISqlMembershipRepository _sqlMembershipRepository;
        private readonly SqlFilterAttributeContextCache _cache;

        public SqlFilterAttributeContextService(
            ILogger<SqlFilterAttributeContextService> logger,
            IDatabaseSqlMembershipSourcesRepository databaseSqlMembershipSourcesRepository,
            IDataFactoryRepository dataFactoryRepository,
            ISqlMembershipRepository sqlMembershipRepository,
            SqlFilterAttributeContextCache cache)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _databaseSqlMembershipSourcesRepository = databaseSqlMembershipSourcesRepository ?? throw new ArgumentNullException(nameof(databaseSqlMembershipSourcesRepository));
            _dataFactoryRepository = dataFactoryRepository ?? throw new ArgumentNullException(nameof(dataFactoryRepository));
            _sqlMembershipRepository = sqlMembershipRepository ?? throw new ArgumentNullException(nameof(sqlMembershipRepository));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        public async Task<string> BuildAttributeContextAsync(IEnumerable<string?> filters)
        {
            if (filters == null)
            {
                return string.Empty;
            }

            var referencedValues = CollectReferencedValues(filters);
            if (referencedValues.Count == 0)
            {
                return string.Empty;
            }

            var labels = await GetAttributeLabelsAsync();
            var codeDescriptions = await GetCodeDescriptionsAsync(referencedValues);

            return FormatContext(referencedValues, labels, codeDescriptions);
        }

        private Dictionary<string, HashSet<string>> CollectReferencedValues(IEnumerable<string?> filters)
        {
            var referencedValues = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var filter in filters)
            {
                if (string.IsNullOrWhiteSpace(filter))
                {
                    continue;
                }

                if (!SqlFilterParser.TryParse(filter, out var attributeValues, out var error))
                {
                    _logger.LogWarning(
                        "Unable to parse a SQL filter while building AI title attribute context: {ParseError}. The title will be generated without attribute context for this filter.",
                        error);
                    continue;
                }

                foreach (var attributeValue in attributeValues)
                {
                    if (!referencedValues.TryGetValue(attributeValue.Key, out var values))
                    {
                        values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        referencedValues[attributeValue.Key] = values;
                    }

                    values.UnionWith(attributeValue.Value);
                }
            }

            return referencedValues;
        }

        private async Task<IReadOnlyDictionary<string, string>> GetAttributeLabelsAsync()
        {
            return await _cache.GetAttributeLabelsAsync(async () =>
            {
                var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                List<SqlMembershipAttribute>? storedAttributes;
                try
                {
                    storedAttributes = await _databaseSqlMembershipSourcesRepository.GetDefaultSourceAttributesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load admin-configured attribute labels; AI titles will use humanized attribute names.");
                    return ((IReadOnlyDictionary<string, string>)labels, SqlFilterAttributeContextCache.UnresolvedDuration);
                }

                if (storedAttributes == null)
                {
                    return ((IReadOnlyDictionary<string, string>)labels, SqlFilterAttributeContextCache.UnresolvedDuration);
                }

                foreach (var attribute in storedAttributes)
                {
                    if (string.IsNullOrWhiteSpace(attribute?.Name) || string.IsNullOrWhiteSpace(attribute.CustomLabel))
                    {
                        continue;
                    }

                    labels[attribute.Name.Trim()] = attribute.CustomLabel.Trim();
                }

                return ((IReadOnlyDictionary<string, string>)labels, SqlFilterAttributeContextCache.ResolvedDuration);
            });
        }

        /// <summary>
        /// Resolves the ADF mappings table, or null when it cannot be reached. Cached, because this
        /// runs on a debounced editing path and the run id lookup is an ARM control-plane call.
        /// </summary>
        private async Task<string?> GetMappingsTableNameAsync()
        {
            return await _cache.GetMappingsTableNameAsync(async () =>
            {
                try
                {
                    var adfRunId = await _dataFactoryRepository.GetMostRecentSucceededRunIdAsync();
                    if (string.IsNullOrWhiteSpace(adfRunId))
                    {
                        _logger.LogWarning("No succeeded ADF run was found; AI titles will not describe coded attribute values.");
                        return (null, SqlFilterAttributeContextCache.UnresolvedDuration);
                    }

                    var tableName = adfRunId.Replace("-", string.Empty);

                    if (!await _sqlMembershipRepository.CheckIfMappingsTableExistsAsync(tableName))
                    {
                        _logger.LogWarning("ADF mappings table {TableName} does not exist; AI titles will not describe coded attribute values.", tableName);
                        return (null, SqlFilterAttributeContextCache.UnresolvedDuration);
                    }

                    return (tableName, SqlFilterAttributeContextCache.ResolvedDuration);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to resolve the ADF mappings table; AI titles will not describe coded attribute values.");
                    return (null, SqlFilterAttributeContextCache.UnresolvedDuration);
                }
            });
        }

        private async Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> GetCodeDescriptionsAsync(
            IReadOnlyDictionary<string, HashSet<string>> referencedValues)
        {
            var resolved = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            var codeAttributes = referencedValues.Keys
                .Where(attribute => attribute.EndsWith(CodeSuffix, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (codeAttributes.Count == 0)
            {
                return resolved;
            }

            var tableName = await GetMappingsTableNameAsync();
            if (string.IsNullOrWhiteSpace(tableName))
            {
                return resolved;
            }

            foreach (var codeAttribute in codeAttributes)
            {
                var baseAttribute = codeAttribute[..^CodeSuffix.Length];
                var codes = referencedValues[codeAttribute];
                if (codes.Count == 0)
                {
                    continue;
                }

                try
                {
                    // Only the codes the filters actually use are resolved, so a high-cardinality
                    // attribute never pulls its whole mapping list back just to describe a value or two.
                    var mappings = await _sqlMembershipRepository.GetAttributeMappingsByCodesAsync(baseAttribute, tableName, codes);
                    var descriptionsByCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var mapping in mappings)
                    {
                        if (string.IsNullOrWhiteSpace(mapping.Code) || string.IsNullOrWhiteSpace(mapping.Description))
                        {
                            continue;
                        }

                        descriptionsByCode.TryAdd(mapping.Code.Trim(), mapping.Description.Trim());
                    }

                    if (descriptionsByCode.Count > 0)
                    {
                        resolved[codeAttribute] = descriptionsByCode;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to load mapping descriptions for {Attribute} from {TableName}; AI titles will not describe its coded values.",
                        baseAttribute,
                        tableName);
                }
            }

            return resolved;
        }

        private static string FormatContext(
            IReadOnlyDictionary<string, HashSet<string>> referencedValues,
            IReadOnlyDictionary<string, string> labels,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> codeDescriptions)
        {
            var lines = new List<string>();

            foreach (var attribute in referencedValues.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                var line = new StringBuilder();
                line.Append("- ").Append(Sanitize(attribute)).Append(": labelled \"").Append(Sanitize(ResolveLabel(attribute, labels))).Append('"');

                if (attribute.EndsWith(CodeSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    codeDescriptions.TryGetValue(attribute, out var descriptions);
                    var describedValues = new List<string>();

                    foreach (var value in referencedValues[attribute].OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
                    {
                        if (descriptions != null
                            && descriptions.TryGetValue(value, out var description)
                            && !string.IsNullOrWhiteSpace(description))
                        {
                            describedValues.Add($"'{Sanitize(value)}' means \"{Sanitize(description)}\"");
                        }
                        else
                        {
                            describedValues.Add($"'{Sanitize(value)}' has no known meaning");
                        }
                    }

                    if (describedValues.Count > 0)
                    {
                        line.Append(". Codes used: ").Append(string.Join("; ", describedValues));
                    }
                }

                lines.Add(line.ToString());
            }

            if (lines.Count == 0)
            {
                return string.Empty;
            }

            var context = new StringBuilder();
            context.AppendLine();
            context.AppendLine("Attribute reference for the filters above:");
            foreach (var line in lines)
            {
                context.AppendLine(line);
            }

            context.AppendLine("Use the labels above instead of the raw column names.");
            context.AppendLine("Codes are organization-specific. Only use a code's meaning when it is listed above.");
            context.AppendLine("If a code has no known meaning, omit the value or refer to it generically (for example \"a specific country\"); never guess what it stands for.");

            return context.ToString();
        }

        /// <summary>
        /// Renders a value as prompt data rather than prompt instructions. Attribute names, admin-configured
        /// labels, filter literals and mapping descriptions are all externally controlled, so line breaks and
        /// the surrounding quote characters are neutralized and the length is bounded.
        /// </summary>
        private static string Sanitize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "(unknown)";
            }

            var builder = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                if (char.IsControl(character))
                {
                    builder.Append(' ');
                }
                else if (character is '"' or '\'')
                {
                    // Keeps a value from closing the delimiter it is quoted in and continuing as instructions.
                    builder.Append(' ');
                }
                else
                {
                    builder.Append(character);
                }
            }

            var sanitized = Regex.Replace(builder.ToString(), @"\s+", " ").Trim();

            if (sanitized.Length == 0)
            {
                return "(unknown)";
            }

            return sanitized.Length > MaxPromptValueLength
                ? sanitized[..MaxPromptValueLength] + "..."
                : sanitized;
        }

        private static string ResolveLabel(string attribute, IReadOnlyDictionary<string, string> labels)
        {
            var name = attribute.Trim().Trim('[', ']');

            if (labels.TryGetValue(name, out var label) && !string.IsNullOrWhiteSpace(label))
            {
                return label;
            }

            if (name.EndsWith(CodeSuffix, StringComparison.OrdinalIgnoreCase))
            {
                var baseAttribute = name[..^CodeSuffix.Length];
                if (labels.TryGetValue(baseAttribute, out var baseLabel) && !string.IsNullOrWhiteSpace(baseLabel))
                {
                    return baseLabel;
                }

                name = baseAttribute;
            }

            return Humanize(name);
        }

        private static string Humanize(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "Attribute";
            }

            var humanized = name.Replace('_', ' ');
            humanized = Regex.Replace(humanized, @"([a-z0-9])([A-Z])", "$1 $2");
            humanized = Regex.Replace(humanized, @"([A-Z]+)([A-Z][a-z])", "$1 $2");

            var words = humanized
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(word => word.All(char.IsUpper) ? word : char.ToUpperInvariant(word[0]) + word[1..]);

            return string.Join(" ", words);
        }
    }
}
