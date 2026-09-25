// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Models
{
    /// <summary>
    /// Reads and validates the optional identityTypes list on a membership query part.
    /// Intended for every membership source, including SQL and group membership.
    /// Each caller supplies the default used when the list is missing.
    /// </summary>
    public static class IdentityTypeSelection
    {
        public const string PropertyName = "identityTypes";

        // The System.Text.Json default, which the other readers of a query also use.
        private const int MaxNestingDepth = 64;

        private static readonly JsonDocumentOptions _documentOptions = new JsonDocumentOptions
        {
            MaxDepth = MaxNestingDepth,
        };

        /// <summary>
        /// Checks whether a query part mentions identityTypes anywhere, whether it is written correctly or not.
        /// Each of these returns true:
        /// <list type="bullet">
        /// <item>{"type":"SqlMembership","identityTypes":["agents"]}</item>
        /// <item>{"type":"SqlMembership","IdentityTypes":["agents"]} (wrong letter case)</item>
        /// <item>{"type":"SqlMembership","source":{"identityTypes":["agents"]}} (nested inside "source")</item>
        /// </list>
        /// A property name that cannot be decoded also returns true, because it might be identityTypes.
        /// A part with no identityTypes anywhere returns false.
        /// </summary>
        /// <remarks>
        /// Schema validators use this to refuse a query that uses identityTypes when the schema that checks
        /// identityTypes is not loaded, rather than accept a list that nothing has validated.
        /// </remarks>
        /// <param name="queryPart">One query part, as a JSON object. Any other kind of JSON value returns false.</param>
        /// <returns>True when the part mentions identityTypes, correctly or not.</returns>
        public static bool IsExplicitlySelected(JsonElement queryPart)
        {
            if (queryPart.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            try
            {
                return queryPart.TryGetProperty(PropertyName, out _) ||
                       FindMisplacedSelection(queryPart) != null;
            }
            catch (InvalidOperationException)
            {
                // A part that cannot be read cannot be shown to leave identityTypes out.
                return true;
            }
        }

        /// <summary>
        /// Checks whether the JSON text of a query part mentions identityTypes anywhere, whether it is written
        /// correctly or not. Each of these returns true:
        /// <list type="bullet">
        /// <item>{"type":"SqlMembership","identityTypes":["agents"]}</item>
        /// <item>{"type":"SqlMembership","IdentityTypes":["agents"]} (wrong letter case)</item>
        /// <item>{"type":"SqlMembership","source":{"identityTypes":["agents"]}} (nested inside "source")</item>
        /// </list>
        /// A part with no identityTypes anywhere returns false.
        /// </summary>
        /// <remarks>
        /// Schema validators use this to refuse a query that uses identityTypes when the schema that checks
        /// identityTypes is not loaded, rather than accept a list that nothing has validated.
        /// </remarks>
        /// <param name="queryPartJson">
        /// The JSON text of one query part. Empty text returns false. Each of these returns true, because the
        /// text cannot be shown to leave identityTypes out: text that is not valid JSON, text nested more than
        /// 64 levels deep, and a property name that cannot be decoded.
        /// </param>
        /// <returns>True when the part mentions identityTypes, correctly or not, or cannot be read.</returns>
        public static bool IsExplicitlySelected(string queryPartJson)
        {
            if (string.IsNullOrWhiteSpace(queryPartJson))
            {
                return false;
            }

            try
            {
                using var document = JsonDocument.Parse(queryPartJson, _documentOptions);
                return IsExplicitlySelected(document.RootElement);
            }
            catch (Exception exception) when (
                exception is JsonException ||
                exception is ArgumentException ||
                exception is InvalidOperationException)
            {
                return true;
            }
        }

        /// <summary>
        /// Checks whether a query part has identityTypes anywhere other than its one valid place: a single key
        /// spelled exactly "identityTypes" at the top level of the part. Each of these returns true:
        /// <list type="bullet">
        /// <item>{"type":"SqlMembership","IdentityTypes":["agents"]} (wrong letter case)</item>
        /// <item>{"type":"SqlMembership","source":{"identityTypes":["agents"]}} (nested inside "source")</item>
        /// <item>{"type":"SqlMembership","identityTypes":["users"],"identityTypes":["agents"]} (written twice)</item>
        /// </list>
        /// Nesting is found at any depth, including inside the identityTypes value itself. A property name that
        /// cannot be decoded also returns true, because it might be identityTypes.
        /// {"type":"SqlMembership","identityTypes":["agents"]} and a part with no identityTypes return false.
        /// </summary>
        /// <remarks>
        /// Validators use this to reject such a query before it runs.
        /// </remarks>
        /// <param name="queryPart">One query part, as a JSON object. Any other kind of JSON value returns false.</param>
        /// <returns>True when identityTypes appears in a place where it is not allowed.</returns>
        public static bool IsMisplaced(JsonElement queryPart) =>
            queryPart.ValueKind == JsonValueKind.Object &&
            FindMisplacedSelection(queryPart) != null;

        /// <summary>
        /// Works out which identity types one query part selects, from the part's JSON text.
        /// <list type="bullet">
        /// <item>{"type":"SqlMembership","identityTypes":["users","agents"]} selects users and agents.</item>
        /// <item>{"type":"SqlMembership"} has no list, so it selects <paramref name="whenOmitted"/>.</item>
        /// </list>
        /// It fails when:
        /// <list type="bullet">
        /// <item>the text is empty, is not valid JSON, is nested more than 64 levels deep, or is not a JSON
        /// object;</item>
        /// <item>identityTypes appears more than once, is in the wrong letter case, is nested inside another
        /// property such as "source", or a property name cannot be decoded;</item>
        /// <item>the list is not a non-empty array of lower-case supported names, such as "users" and "agents",
        /// each appearing once.</item>
        /// </list>
        /// </summary>
        /// <param name="queryPartJson">The JSON text of one query part, for example one element of a sync job's query.</param>
        /// <param name="whenOmitted">
        /// What to select when the part has no identityTypes property. Each membership source chooses this for
        /// rules written before identityTypes existed; for example, <see cref="IdentityTypeSet.UsersOnly"/> keeps
        /// those rules returning only users.
        /// </param>
        /// <param name="selection">The identity types the part selects. Only meaningful when this returns true.</param>
        /// <param name="error">When this returns false, a message that says what is wrong; otherwise null.</param>
        /// <returns>True when the part has a valid list or no list; false when the part must be rejected.</returns>
        public static bool TryParse(
            string queryPartJson,
            IdentityTypeSet whenOmitted,
            out IdentityTypeSet selection,
            out string error)
        {
            selection = default;
            error = null;

            if (string.IsNullOrWhiteSpace(queryPartJson))
            {
                error = "A query part must not be empty.";
                return false;
            }

            try
            {
                using var document = JsonDocument.Parse(queryPartJson, _documentOptions);
                return TryParse(
                    document.RootElement,
                    whenOmitted,
                    out selection,
                    out error);
            }
            catch (Exception exception) when (
                exception is JsonException ||
                exception is ArgumentException ||
                exception is InvalidOperationException)
            {
                error = $"A query part is not valid JSON or is nested more than {MaxNestingDepth} levels deep.";
                return false;
            }
        }

        /// <summary>
        /// Works out which identity types one query part selects, from a query part that is already parsed.
        /// <list type="bullet">
        /// <item>{"type":"SqlMembership","identityTypes":["users","agents"]} selects users and agents.</item>
        /// <item>{"type":"SqlMembership"} has no list, so it selects <paramref name="whenOmitted"/>.</item>
        /// </list>
        /// It fails when:
        /// <list type="bullet">
        /// <item>the part is not a JSON object;</item>
        /// <item>identityTypes appears more than once, is in the wrong letter case, is nested inside another
        /// property such as "source", or a property name cannot be decoded;</item>
        /// <item>the list is not a non-empty array of lower-case supported names, such as "users" and "agents",
        /// each appearing once.</item>
        /// </list>
        /// </summary>
        /// <param name="queryPart">One query part, as a JSON object.</param>
        /// <param name="whenOmitted">
        /// What to select when the part has no identityTypes property. Each membership source chooses this for
        /// rules written before identityTypes existed; for example, <see cref="IdentityTypeSet.UsersOnly"/> keeps
        /// those rules returning only users.
        /// </param>
        /// <param name="selection">The identity types the part selects. Only meaningful when this returns true.</param>
        /// <param name="error">When this returns false, a message that says what is wrong; otherwise null.</param>
        /// <returns>True when the part has a valid list or no list; false when the part must be rejected.</returns>
        public static bool TryParse(
            JsonElement queryPart,
            IdentityTypeSet whenOmitted,
            out IdentityTypeSet selection,
            out string error)
        {
            selection = default;
            error = null;

            if (queryPart.ValueKind != JsonValueKind.Object)
            {
                error = "A query part must be a JSON object.";
                return false;
            }

            var misplacedError = FindMisplacedSelection(queryPart);

            if (misplacedError != null)
            {
                error = misplacedError;
                return false;
            }

            if (!queryPart.TryGetProperty(PropertyName, out var rawSelection))
            {
                selection = whenOmitted;
                return true;
            }

            return TryParseProperty(rawSelection, out selection, out error);
        }

        /// <summary>
        /// Works out which identity types an identityTypes value selects, when the caller has already taken the
        /// value out of its query part. For example, ["agents","users"] selects users and agents.
        /// The caller must already have found the property: there is no default for a missing list here.
        /// </summary>
        /// <param name="rawSelection">
        /// Only the property's value, such as ["users"], not the whole query part. It must be a non-empty JSON
        /// array of lower-case supported names, such as "users" and "agents", each appearing once, in any order.
        /// Each of these fails: null, a value that is not an array, an empty array, an entry that is not a string
        /// or cannot be decoded, an empty or unknown name, a name in the wrong case such as "Users", and a
        /// repeated name.
        /// </param>
        /// <param name="selection">The identity types the value selects. Only meaningful when this returns true.</param>
        /// <param name="error">When this returns false, a message that says what is wrong; otherwise null.</param>
        /// <returns>True when the value is a valid list.</returns>
        public static bool TryParseProperty(
            JsonElement rawSelection,
            out IdentityTypeSet selection,
            out string error)
        {
            selection = default;
            error = null;

            if (rawSelection.ValueKind == JsonValueKind.Null)
            {
                error = $"{PropertyName} must not be null; omit it to use the source's legacy default.";
                return false;
            }

            if (rawSelection.ValueKind != JsonValueKind.Array)
            {
                error = $"{PropertyName} must be an array of strings.";
                return false;
            }

            var typeNames = new List<string>();

            foreach (var entry in rawSelection.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.String)
                {
                    error = $"{PropertyName} must contain only string values.";
                    return false;
                }

                try
                {
                    typeNames.Add(entry.GetString());
                }
                catch (InvalidOperationException)
                {
                    error = $"{PropertyName} entry at index {typeNames.Count} is not decodable text.";
                    return false;
                }
            }

            return IdentityTypeSet.TryResolve(
                typeNames,
                default,
                out selection,
                out error);
        }

        // Returns an error when identityTypes appears anywhere other than once, as a correctly spelled key at
        // the top level of the part, or when any property name cannot be decoded; otherwise null. The value of
        // that one valid key is searched too.
        private static string FindMisplacedSelection(JsonElement queryPart)
        {
            var selectionCount = 0;

            foreach (var property in queryPart.EnumerateObject())
            {
                if (NameMatches(property, out var refused))
                {
                    selectionCount++;

                    if (selectionCount > 1)
                    {
                        return $"{PropertyName} must appear only once in a query part.";
                    }
                }

                if (refused || ContainsSelection(property.Value))
                {
                    return $"{PropertyName} must be spelled exactly '{PropertyName}' and be a query-part-level property alongside 'type' and 'source'.";
                }
            }

            return null;
        }

        // Searches every nested object and array for an identityTypes key in any letter case,
        // or for a key name that cannot be decoded. Uses a stack rather than recursion so that
        // deeply nested input cannot overflow the call stack.
        private static bool ContainsSelection(JsonElement element)
        {
            var pending = new Stack<JsonElement>();
            pending.Push(element);

            while (pending.Count > 0)
            {
                var current = pending.Pop();

                if (current.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in current.EnumerateObject())
                    {
                        if (NameMatches(property, out var refused) || refused)
                        {
                            return true;
                        }

                        pending.Push(property.Value);
                    }
                }
                else if (current.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in current.EnumerateArray())
                    {
                        pending.Push(item);
                    }
                }
            }

            return false;
        }

        // Returns true when the key is exactly identityTypes. Sets refused when the key differs from
        // identityTypes only by letter case, or cannot be decoded: such a key may be a failed attempt to set
        // identityTypes, so it must not be treated as leaving the list out. At the top level of a part, the
        // schemas also reject both as unknown properties.
        private static bool NameMatches(
            JsonProperty property,
            out bool refused)
        {
            refused = false;

            try
            {
                if (property.NameEquals(PropertyName))
                {
                    return true;
                }

                refused = string.Equals(property.Name, PropertyName, StringComparison.OrdinalIgnoreCase);
                return false;
            }
            catch (InvalidOperationException)
            {
                refused = true;
                return false;
            }
        }
    }
}
