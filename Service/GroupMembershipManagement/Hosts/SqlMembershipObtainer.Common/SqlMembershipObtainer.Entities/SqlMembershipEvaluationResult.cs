// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SqlMembershipObtainer.Entities
{
    /// <summary>
    /// Reports the identities selected by a SQL membership evaluation and the counts needed for membership-evaluation
    /// logging.
    /// </summary>
    public sealed class SqlMembershipEvaluationResult
    {
        /// <summary>
        /// Creates a completed SQL membership evaluation result.
        /// </summary>
        /// <param name="userObjectIds">
        /// User object IDs selected for emission. Null throws <see cref="ArgumentNullException"/>. Duplicate values are
        /// removed. The all-zero GUID is accepted because HR rows without an Entra account use it and the legacy path
        /// passed it through.
        /// </param>
        /// <param name="agentObjectIds">
        /// Agent object IDs selected for emission. Null throws <see cref="ArgumentNullException"/>. Duplicate values are
        /// removed. The all-zero GUID throws <see cref="ArgumentException"/>.
        /// </param>
        /// <param name="selectedHumanCount">
        /// The number of distinct user object IDs the rule selected, counted before users are dropped from an agents-only
        /// result. A user with several agents counts once, and HR rows without an Entra account share the all-zero ID and
        /// count once together. A negative value throws <see cref="ArgumentOutOfRangeException"/>. A value smaller than
        /// the emitted user count throws <see cref="ArgumentException"/>.
        /// </param>
        /// <param name="referencedIdentityTypes">
        /// The table types referenced by the filter. Null throws <see cref="ArgumentNullException"/>. Duplicate values are
        /// removed. Pass an empty collection when there is no filter or the filter references no columns.
        /// </param>
        /// <param name="skippedAgentRowCount">
        /// The number of selected agent rows skipped because their agent object ID was missing, empty, or not the
        /// hyphenated GUID text the sync can emit. Omit it to record 0. A negative value throws
        /// <see cref="ArgumentOutOfRangeException"/>.
        /// </param>
        public SqlMembershipEvaluationResult(
            IEnumerable<Guid> userObjectIds,
            IEnumerable<Guid> agentObjectIds,
            int selectedHumanCount,
            IEnumerable<IdentityType> referencedIdentityTypes,
            int skippedAgentRowCount = 0)
        {
            UserObjectIds = DistinctIdentifiers(
                userObjectIds,
                nameof(userObjectIds),
                allowEmptyGuid: true);
            AgentObjectIds = DistinctIdentifiers(
                agentObjectIds,
                nameof(agentObjectIds),
                allowEmptyGuid: false);
            ReferencedIdentityTypes = referencedIdentityTypes?
                .Distinct()
                .ToArray() ??
                throw new ArgumentNullException(nameof(referencedIdentityTypes));

            if (selectedHumanCount < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(selectedHumanCount));
            }

            if (selectedHumanCount < UserObjectIds.Count)
            {
                throw new ArgumentException(
                    "The selected-human count must not be smaller than the emitted user set.",
                    nameof(selectedHumanCount));
            }

            SelectedHumanCount = selectedHumanCount;

            if (skippedAgentRowCount < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(skippedAgentRowCount));
            }

            SkippedAgentRowCount = skippedAgentRowCount;
        }

        /// <summary>
        /// Gets the distinct user object IDs emitted by the evaluation. The list can be empty and can contain the
        /// all-zero GUID.
        /// </summary>
        public IReadOnlyList<Guid> UserObjectIds { get; }

        /// <summary>
        /// Gets the distinct agent object IDs emitted by the evaluation. The list can be empty and never contains the
        /// all-zero GUID.
        /// </summary>
        public IReadOnlyList<Guid> AgentObjectIds { get; }

        /// <summary>
        /// Gets the number of distinct user object IDs the rule selected, counted before users are dropped from an
        /// agents-only result.
        /// </summary>
        public int SelectedHumanCount { get; }

        /// <summary>
        /// Gets the number of selected agent rows that were skipped because the agent object ID could not be emitted.
        /// </summary>
        public int SkippedAgentRowCount { get; }

        /// <summary>
        /// Gets the distinct identity table types referenced by the filter. The collection is empty when no filter column
        /// was referenced.
        /// </summary>
        public IReadOnlyCollection<IdentityType> ReferencedIdentityTypes { get; }

        // HR rows without an Entra account carry the all-zero user ID and have always been passed through.
        private static IReadOnlyList<Guid> DistinctIdentifiers(
            IEnumerable<Guid> identifiers,
            string parameterName,
            bool allowEmptyGuid)
        {
            if (identifiers == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            // The repository passes HashSets that are already distinct; copying one avoids building a second set.
            Guid[] distinct;

            if (identifiers is HashSet<Guid> identifierSet &&
                identifierSet.Comparer.Equals(EqualityComparer<Guid>.Default))
            {
                distinct = new Guid[identifierSet.Count];
                identifierSet.CopyTo(distinct);
            }
            else
            {
                distinct = identifiers.Distinct().ToArray();
            }

            if (!allowEmptyGuid &&
                distinct.Any(identifier => identifier == Guid.Empty))
            {
                throw new ArgumentException(
                    "Selected identifiers must not be empty GUIDs.",
                    parameterName);
            }

            return distinct;
        }
    }
}
