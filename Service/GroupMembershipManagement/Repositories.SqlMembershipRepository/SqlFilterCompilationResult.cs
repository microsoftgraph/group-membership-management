// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Repositories.SqlMembershipRepository
{
    /// <summary>
    /// The outcome of checking a sync job's SQL filter. Create it with <see cref="Valid"/> or <see cref="Invalid"/>.
    /// </summary>
    internal sealed class SqlFilterCompilationResult
    {
        private SqlFilterCompilationResult(
            bool isValid,
            IEnumerable<IdentityType> referencedIdentityTypes,
            IEnumerable<string> referencedUserColumnNames,
            string? error)
        {
            IsValid = isValid;
            ReferencedIdentityTypes = referencedIdentityTypes.Distinct().ToArray();
            ReferencedUserColumnNames = referencedUserColumnNames.Distinct(StringComparer.Ordinal).ToArray();
            UsesAgents = ReferencedIdentityTypes.Contains(IdentityType.Agents);
            Error = error;
        }

        /// <summary>
        /// True when the filter was accepted. When false, <see cref="Error"/> says why.
        /// </summary>
        [MemberNotNullWhen(false, nameof(Error))]
        public bool IsValid { get; }

        /// <summary>
        /// True when the filter uses at least one agents table column. Always false for a rejected filter.
        /// </summary>
        public bool UsesAgents { get; }

        /// <summary>
        /// The tables (users, agents) whose columns the filter uses, listed once. Empty when the filter was rejected
        /// or uses no columns.
        /// </summary>
        public IReadOnlyCollection<IdentityType> ReferencedIdentityTypes { get; }

        /// <summary>
        /// The users table columns the filter uses, spelled as they are in the users table and listed once. For the filter
        /// <c>department = 'Sales' AND agents.BlueprintId = 'alpha'</c> this is <c>Department</c>. The membership query
        /// carries only these columns, plus the ones it needs itself, through its intermediate results. Empty when the
        /// filter was rejected or uses no users columns.
        /// </summary>
        public IReadOnlyCollection<string> ReferencedUserColumnNames { get; }

        /// <summary>
        /// Why the filter was rejected; null when it was accepted.
        /// </summary>
        public string? Error { get; }

        /// <summary>
        /// Creates the result for an accepted filter.
        /// </summary>
        /// <param name="referencedIdentityTypes">The table of each column the filter uses. Must not be null;
        /// duplicates are removed. Pass an empty collection when there is no filter.</param>
        /// <param name="referencedUserColumnNames">The users table columns the filter uses, spelled as they are in the
        /// users table. Must not be null; exact duplicates are removed. Pass an empty collection when there is no filter
        /// or it uses no users columns.</param>
        public static SqlFilterCompilationResult Valid(
            IEnumerable<IdentityType> referencedIdentityTypes,
            IEnumerable<string> referencedUserColumnNames)
        {
            if (referencedIdentityTypes == null)
            {
                throw new ArgumentNullException(nameof(referencedIdentityTypes));
            }

            if (referencedUserColumnNames == null)
            {
                throw new ArgumentNullException(nameof(referencedUserColumnNames));
            }

            return new SqlFilterCompilationResult(
                isValid: true,
                referencedIdentityTypes,
                referencedUserColumnNames,
                error: null);
        }

        /// <summary>
        /// Creates the result for a rejected filter.
        /// </summary>
        /// <param name="error">Why the filter was rejected. Must not be empty.</param>
        public static SqlFilterCompilationResult Invalid(string error)
        {
            if (string.IsNullOrWhiteSpace(error))
            {
                throw new ArgumentException("A validation error is required.", nameof(error));
            }

            return new SqlFilterCompilationResult(
                isValid: false,
                Array.Empty<IdentityType>(),
                Array.Empty<string>(),
                error);
        }
    }
}
