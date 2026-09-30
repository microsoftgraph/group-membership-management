// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using SqlMembershipObtainer.Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public enum AttributeSearchMode
    {
        Prefix = 0,
        Substring = 1
    }

    public interface ISqlMembershipRepository
    {
        Task<List<PersonEntity>> GetChildEntitiesAsync(string filter, int personnelNumber, string tableName, int depth);
        Task<(int maxDepth, int id)> GetOrgLeaderDetailsAsync(string azureObjectId, string tableName);
        Task<List<PersonEntity>> FilterChildEntitiesAsync(string query, string tableName);
        Task<bool> IsUserInFilterAsync(string filter, string tableName, string azureObjectId);
        Task<bool> CheckIfTableExistsAsync(string tableName);
        Task<List<string>> GetColumnNamesAsync(string tableName);

        /// <summary>
        /// Evaluates one SQL membership rule and returns the selected user and agent object IDs.
        /// </summary>
        /// <param name="request">
        /// The table, optional manager scope, optional filter, and selected identity types to evaluate.
        /// Null throws <see cref="ArgumentNullException"/>. An empty identity selection, blank table name, or invalid
        /// table name is rejected before SQL execution.
        /// </param>
        /// <returns>
        /// The selected identities and logging counts. The user and agent lists can be empty. Agent rows whose object ID
        /// is not hyphenated GUID text, including a NULL object ID, are skipped and counted instead of returned. When
        /// agents are required, a missing or unreadable agents table, or one without the <c>AgentObjectId</c> and
        /// <c>AgentManagerId</c> columns, throws <see cref="InvalidOperationException"/> before the query runs.
        /// </returns>
        Task<SqlMembershipEvaluationResult> EvaluateMembershipAsync(SqlMembershipEvaluationRequest request);

        /// <summary>
        /// Answers whether one user or agent object ID is selected by one SQL membership rule.
        /// </summary>
        /// <param name="request">
        /// The table, optional manager scope, optional filter, and selected identity types to evaluate.
        /// Null throws <see cref="ArgumentNullException"/>. If the requested <paramref name="identityType"/> is not in
        /// the request's identity selection, the result is false unless validation fails first.
        /// </param>
        /// <param name="objectId">
        /// The user or agent object ID to probe. <see cref="Guid.Empty"/> throws <see cref="ArgumentException"/>.
        /// </param>
        /// <param name="identityType">
        /// The kind of identity to probe. Pass users for a user object ID or agents for an agent object ID. Unsupported
        /// enum values throw <see cref="ArgumentOutOfRangeException"/>.
        /// </param>
        /// <returns>
        /// True when the rule selects that identity; false when it does not. When agents are required, a missing or
        /// unreadable agents table, or one without the <c>AgentObjectId</c> and <c>AgentManagerId</c> columns, throws
        /// <see cref="InvalidOperationException"/> before the query runs.
        /// </returns>
        Task<bool> IsIdentitySelectedAsync(
            SqlMembershipEvaluationRequest request,
            Guid objectId,
            IdentityType identityType);
        Task<(int maxDepth, string azureObjectId)> GetOrgLeaderAsync(int employeeId, string tableName);
        Task<List<(string Name, string Type)>> GetColumnDetailsAsync(string tableName);
        Task<bool> CheckIfMappingsTableExistsAsync(string tableName);
        Task<List<(string Code, string Description)>> GetAttributeMappingsAsync(string attribute, string tableName);
        Task<(List<(string Code, string Description)> Mappings, bool HasMore)> GetAttributeMappingsPageAsync(string attribute, string tableName, string? search = null, int top = 500, AttributeSearchMode matchMode = AttributeSearchMode.Prefix);
        Task<List<(string Code, string Description)>> GetAttributeMappingsByCodesAsync(string attribute, string tableName, IEnumerable<string> codes);
        Task<List<string>> GetAttributeValuesAsync(string attribute, bool hasMapping, string tableName);

        /// <summary>
        /// Validates saved SQL filter fragments against the current users table and, only when needed, the matching agents
        /// table.
        /// </summary>
        /// <param name="sqlFilters">
        /// Filters keyed by the caller's filter index. Null throws <see cref="ArgumentNullException"/>. An empty
        /// dictionary returns an empty error dictionary without reading SQL. Each value is parsed as a WHERE fragment;
        /// for example, <c>Department = 'Sales'</c> is accepted when that column exists, while multiple statements are
        /// rejected. A column written as <c>users.Name</c> must be in the users table and one written as
        /// <c>agents.Name</c> must be in the agents table, so <c>users.BlueprintId</c> is rejected even when the agents
        /// table has <c>BlueprintId</c>. Any other prefix, such as <c>nosuch.Name</c>, is rejected. A column without a
        /// prefix is looked up in the users table first, then in the agents table; if it is in both tables and the filter
        /// also uses an agents column, it is rejected as ambiguous and must be prefixed.
        /// </param>
        /// <param name="tableName">
        /// The same-run table name without schema. Null, empty, whitespace, or unsupported characters throw
        /// <see cref="ArgumentException"/>.
        /// </param>
        /// <returns>
        /// A dictionary of validation errors keyed by the same indexes as <paramref name="sqlFilters"/>. An empty
        /// dictionary means all filters are valid. A filter that references agents columns gets "Required agents input is
        /// unavailable." when the agents table is missing or the caller cannot read it, and "Required agents input is
        /// missing columns: ..." naming <c>AgentObjectId</c> or <c>AgentManagerId</c> when the table lacks either one.
        /// While the agents table is missing or unreadable, only a column written with the <c>agents.</c> prefix, such as
        /// <c>agents.BlueprintId</c>, marks a filter as needing agents input; an unprefixed <c>BlueprintId</c> is reported
        /// as "Invalid column name detected: BlueprintId", like any other name that is not in the users table.
        /// </returns>
        Task<Dictionary<int, string>> ValidateFiltersAsync(Dictionary<int, string> sqlFilters, string tableName);
        Task<Dictionary<string, string>?> GetUserAttributesAsync(string azureObjectId, string tableName);
        Task<int?> GetUserEmployeeIdAsync(string azureObjectId, string tableName);
        Task<string?> GetUserAzureObjectIdAsync(int employeeId, string tableName);
        Task<Dictionary<string, Dictionary<string, string>>> GetUserAttributesBatchAsync(IEnumerable<string> userIds, string tableName);
    }
}
