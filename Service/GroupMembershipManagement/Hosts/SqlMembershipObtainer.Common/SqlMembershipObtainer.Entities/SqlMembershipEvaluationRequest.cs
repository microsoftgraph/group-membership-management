// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;
using System.Collections.Generic;

namespace SqlMembershipObtainer.Entities
{
    /// <summary>
    /// Describes one SQL membership evaluation: which users table to read, which optional manager scope and filter to
    /// apply, which identity types to emit, and which raw ADF run identifier should be carried into logs.
    /// </summary>
    public sealed class SqlMembershipEvaluationRequest
    {
        /// <summary>
        /// Creates a request for evaluating SQL membership from one same-run users table and, when needed, the matching
        /// agents table.
        /// </summary>
        /// <param name="managerEmployeeId">
        /// The root manager's EmployeeId for a manager-scoped rule. Pass null for a filter-only rule that starts from
        /// every row in the users table. The constructor accepts any integer value and leaves validation to the caller.
        /// </param>
        /// <param name="depth">
        /// The maximum management-chain depth to include when <paramref name="managerEmployeeId"/> is supplied. A value
        /// greater than 0 limits recursion to that depth; 0 or a negative value means no depth limit. The value is ignored
        /// for filter-only rules.
        /// </param>
        /// <param name="filter">
        /// The SQL WHERE fragment to apply, such as <c>Department = 'Sales'</c> or
        /// <c>agents.BlueprintId = 'alpha'</c>. Pass null, empty, or whitespace to apply no filter; syntax and column
        /// validation happen in the repository before execution.
        /// </param>
        /// <param name="identityTypes">
        /// The identities to emit. Pass users, agents, or both; an empty set throws <see cref="ArgumentException"/>.
        /// </param>
        /// <param name="tableName">
        /// The same-run table name without schema, for example <c>adfRun123</c>. Null, empty, or whitespace throws
        /// <see cref="ArgumentException"/>. Character validation happens in the repository before SQL execution.
        /// </param>
        /// <param name="adfRunId">
        /// The raw ADF run identifier to log with the evaluation. Null, empty, or whitespace throws
        /// <see cref="ArgumentException"/>.
        /// </param>
        public SqlMembershipEvaluationRequest(
            int? managerEmployeeId,
            int depth,
            string filter,
            IdentityTypeSet identityTypes,
            string tableName,
            string adfRunId)
        {
            if (identityTypes.IsEmpty)
            {
                throw new ArgumentException(
                    "At least one identity type must be selected.",
                    nameof(identityTypes));
            }

            if (string.IsNullOrWhiteSpace(tableName))
            {
                throw new ArgumentException(
                    "The same-run table name is required.",
                    nameof(tableName));
            }

            if (string.IsNullOrWhiteSpace(adfRunId))
            {
                throw new ArgumentException(
                    "The raw ADF run identifier is required.",
                    nameof(adfRunId));
            }

            ManagerEmployeeId = managerEmployeeId;
            Depth = depth;
            Filter = filter;
            IdentityTypes = identityTypes;
            TableName = tableName;
            AdfRunId = adfRunId;
        }

        /// <summary>
        /// Gets the root manager EmployeeId for a manager-scoped rule, or null when the rule should start from the whole
        /// users table and only apply the filter.
        /// </summary>
        public int? ManagerEmployeeId { get; }

        /// <summary>
        /// Gets the requested recursion depth for manager-scoped rules. Values greater than 0 cap recursion; 0 or negative
        /// values mean unbounded recursion and are ignored when no manager is supplied.
        /// </summary>
        public int Depth { get; }

        /// <summary>
        /// Gets the SQL WHERE fragment supplied by the caller, or null/empty/whitespace when no filter should be applied.
        /// </summary>
        public string Filter { get; }

        /// <summary>
        /// Gets the selected identity types to emit. The set is guaranteed not to be empty.
        /// </summary>
        public IdentityTypeSet IdentityTypes { get; }

        /// <summary>
        /// Gets the same-run users table name without schema; the matching agents table uses the same name when agents are
        /// needed.
        /// </summary>
        public string TableName { get; }

        /// <summary>
        /// Gets the raw ADF run identifier that should be written to membership-evaluation logs.
        /// </summary>
        public string AdfRunId { get; }

        /// <summary>
        /// Gets whether the request has no manager scope. True means the repository starts from every row in the users
        /// table; false means it starts at <see cref="ManagerEmployeeId"/> and walks the management chain.
        /// </summary>
        public bool IsFilterOnly => !ManagerEmployeeId.HasValue;

        /// <summary>
        /// Creates a request from the lower-case identity type names carried between functions.
        /// </summary>
        /// <param name="managerEmployeeId">
        /// The root manager's EmployeeId for a manager-scoped rule, or null for a filter-only rule.
        /// </param>
        /// <param name="depth">
        /// The manager-scope depth. Values greater than 0 cap recursion; 0 or a negative value means no depth limit.
        /// </param>
        /// <param name="filter">
        /// The SQL WHERE fragment to apply, or null/empty/whitespace for no filter.
        /// </param>
        /// <param name="identityTypes">
        /// The identity type names from the caller. Null means the old default, users only. Supported examples are
        /// <c>["users"]</c>, <c>["agents"]</c>, and <c>["users","agents"]</c>. Empty lists, repeated names,
        /// wrong-case names such as <c>"Users"</c>, and unknown names such as <c>"robots"</c> throw
        /// <see cref="InvalidOperationException"/>.
        /// </param>
        /// <param name="tableName">
        /// The same-run table name without schema. Null, empty, or whitespace throws <see cref="ArgumentException"/>.
        /// </param>
        /// <param name="adfRunId">
        /// The raw ADF run identifier for logging. Null, empty, or whitespace throws <see cref="ArgumentException"/>.
        /// </param>
        /// <returns>A request whose identity type set is valid and never empty.</returns>
        public static SqlMembershipEvaluationRequest FromIdentityTypeNames(
            int? managerEmployeeId,
            int depth,
            string filter,
            IReadOnlyList<string> identityTypes,
            string tableName,
            string adfRunId)
        {
            if (!IdentityTypeSet.TryResolve(
                identityTypes,
                IdentityTypeSet.UsersOnly,
                out var selection,
                out var error))
            {
                throw new InvalidOperationException(error);
            }

            return new SqlMembershipEvaluationRequest(
                managerEmployeeId,
                depth,
                filter,
                selection,
                tableName,
                adfRunId);
        }
    }
}
