// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Hosts.GroupMembershipObtainer
{
    /// <summary>
    /// Resolves the identity selection carried by GroupMembershipObtainer requests.
    /// </summary>
    public static class GroupIdentitySelection
    {
        /// <summary>
        /// Gets the permanent omitted selection for group parts: users and agent users.
        /// </summary>
        public static IdentityTypeSet GroupDefault => IdentityTypeSet.GroupDefault;

        /// <summary>
        /// Determines which member types a GroupMembership query part selects.
        /// {"type":"GroupMembership","source":"00000000-0000-4000-8000-000000000001",
        /// "identityTypes":["agents"]} selects agent users. Omitting identityTypes selects both people and
        /// agent users. An empty list, an unsupported value such as "devices", wrong letter case, a duplicate,
        /// or a nested identityTypes property is rejected.
        /// </summary>
        /// <param name="queryPart">
        /// One parsed JSON object. A non-object value fails. The object may omit identityTypes, which uses the
        /// permanent group default of people and agent users.
        /// </param>
        /// <param name="selection">
        /// On success, the resolved selection. On failure, the default <see cref="IdentityTypeSet"/> value.
        /// </param>
        /// <param name="explicitSelection">
        /// On success with identityTypes present, canonical lower-case names in stable order; on omission or
        /// failure, <see langword="null"/>.
        /// </param>
        /// <param name="error">
        /// On failure, a message describing the invalid JSON shape or value; on success, <see langword="null"/>.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when the object omits identityTypes or contains a valid users/agents list;
        /// otherwise <see langword="false"/>.
        /// </returns>
        public static bool TryParse(
            JsonElement queryPart,
            out IdentityTypeSet selection,
            out List<string> explicitSelection,
            out string error)
        {
            explicitSelection = null;

            if (!IdentityTypeSelection.TryParse(
                queryPart,
                IdentityTypeSet.GroupDefault,
                IdentityTypeSet.GroupDefault,
                out selection,
                out error))
            {
                return false;
            }

            var names = selection.ToTypeNames();
            if (names.Any(name =>
                !string.Equals(name, "users", StringComparison.Ordinal) &&
                !string.Equals(name, "agents", StringComparison.Ordinal)))
            {
                error = "Group identityTypes supports only 'users' and 'agents'.";
                return false;
            }

            if (queryPart.TryGetProperty(IdentityTypeSelection.PropertyName, out _))
            {
                explicitSelection = names.ToList();
            }

            return true;
        }

        /// <summary>
        /// Resolves a selection carried between GMO functions.
        /// </summary>
        /// <param name="identityTypes">
        /// Canonical names from an explicit group selection, or <see langword="null"/> for the group default.
        /// Empty, duplicate, unsupported, or wrong-case values throw.
        /// </param>
        /// <returns>The resolved set, using users and agents when the value is omitted.</returns>
        /// <exception cref="ArgumentException">The carried selection is invalid for a group part.</exception>
        public static IdentityTypeSet Resolve(IReadOnlyList<string> identityTypes)
        {
            if (!IdentityTypeSet.TryResolve(
                identityTypes,
                IdentityTypeSet.GroupDefault,
                IdentityTypeSet.GroupDefault,
                out var selection,
                out var error))
            {
                throw new ArgumentException(error, nameof(identityTypes));
            }

            var names = selection.ToTypeNames();
            if (names.Any(name =>
                !string.Equals(name, "users", StringComparison.Ordinal) &&
                !string.Equals(name, "agents", StringComparison.Ordinal)))
            {
                throw new ArgumentException(
                    "Group identityTypes supports only 'users' and 'agents'.",
                    nameof(identityTypes));
            }

            return selection;
        }

        /// <summary>
        /// Returns whether the carried selection chooses exactly one group member type.
        /// </summary>
        /// <param name="identityTypes">The explicit names, or <see langword="null"/> for both types.</param>
        /// <returns><see langword="true"/> for users-only or agents-only.</returns>
        public static bool IsOneType(IReadOnlyList<string> identityTypes) =>
            Resolve(identityTypes).ToTypeNames().Count == 1;

        /// <summary>
        /// Formats the carried selection for structured log entries.
        /// </summary>
        /// <param name="identityTypes">The explicit names, or <see langword="null"/> for the default.</param>
        /// <returns>A stable lower-case description of the resolved selection.</returns>
        public static string Describe(IReadOnlyList<string> identityTypes)
        {
            var description = string.Join(",", Resolve(identityTypes).ToTypeNames());
            return identityTypes == null ? $"{description} (default)" : description;
        }
    }
}
