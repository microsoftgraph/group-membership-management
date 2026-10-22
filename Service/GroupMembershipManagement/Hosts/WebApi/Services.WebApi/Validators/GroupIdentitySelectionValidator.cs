// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Services.WebApi.Validators
{
    public sealed record GroupIdentitySelectionValidationError(string ErrorCode, string Message);

    public static class GroupIdentitySelectionValidator
    {
        private static readonly Regex _groupIdentifierPattern = new(
            "^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}\\z",
            RegexOptions.CultureInvariant);

        /// <summary>
        /// Checks explicit identityTypes properties on group-based parts in a stored job query. A selected group
        /// source must be a current GroupMembership part with one string source and a non-empty users/agents
        /// selection. SqlMembership parts are ignored so their existing validator remains authoritative.
        /// Selected legacy array sources, SecurityGroup parts, unsupported group types, invalid values, duplicate
        /// properties, wrong letter case, and nested identityTypes properties are rejected.
        /// </summary>
        /// <param name="query">
        /// The JSON array stored as the job query. Null, empty, malformed, or non-array input is left to the
        /// existing query validators and returns no identity-selection error.
        /// </param>
        /// <returns>
        /// Null when the query has no explicit group selection, contains only SqlMembership selections, or every
        /// explicit group selection is supported; otherwise an error that the WebApi create or update path returns
        /// as a bad request.
        /// </returns>
        public static GroupIdentitySelectionValidationError? Validate(string? query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return null;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(query);
            }
            catch (JsonException)
            {
                return null;
            }

            using (document)
            {
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return null;
                }

                foreach (var part in document.RootElement.EnumerateArray())
                {
                    if (part.ValueKind != JsonValueKind.Object ||
                        !IdentityTypeSelection.IsExplicitlySelected(part))
                    {
                        continue;
                    }

                    if (!part.TryGetProperty("type", out var typeElement) ||
                        typeElement.ValueKind != JsonValueKind.String)
                    {
                        return InvalidSelection();
                    }

                    var partType = typeElement.GetString();
                    if (string.Equals(partType, "SqlMembership", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (IdentityTypeSelection.IsMisplaced(part))
                    {
                        return InvalidSelection();
                    }

                    if (!string.Equals(partType, "GroupMembership", StringComparison.Ordinal) ||
                        !part.TryGetProperty("source", out var sourceElement) ||
                        sourceElement.ValueKind != JsonValueKind.String ||
                        sourceElement.GetString() is not string source ||
                        source.Length != 36 ||
                        !_groupIdentifierPattern.IsMatch(source) ||
                        !IdentityTypeSelection.TryParse(
                            part,
                            IdentityTypeSet.GroupDefault,
                            IdentityTypeSet.GroupDefault,
                            out _,
                            out _))
                    {
                        return InvalidSelection();
                    }
                }
            }

            return null;
        }

        private static GroupIdentitySelectionValidationError InvalidSelection() =>
            new(
                "InvalidGroupIdentitySelection",
                "identityTypes is allowed only on a GroupMembership part with one group identifier and a valid users/agents selection.");
    }
}
