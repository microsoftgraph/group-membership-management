// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using SqlMembershipObtainer.Entities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Repositories.SqlMembershipRepository
{
    /// <summary>
    /// Builds the SQL commands used to evaluate SQL membership rules and spot-check one selected identity.
    /// </summary>
    internal static class SqlMembershipQueryBuilder
    {
        private static readonly string[] _requiredUserColumnNames = { "AzureObjectId", "EmployeeId" };
        /// <summary>
        /// Builds the SQL command that returns all identities selected by one SQL membership request.
        /// </summary>
        /// <param name="request">
        /// The evaluation request to translate. Null throws <see cref="ArgumentNullException"/>. The request decides
        /// whether the source is the whole users table or a manager hierarchy, which identity types are emitted, and
        /// which same-run table name is used.
        /// </param>
        /// <param name="compilation">
        /// The validated filter analysis for <paramref name="request"/>. Null throws <see cref="ArgumentNullException"/>.
        /// An invalid compilation throws <see cref="ArgumentException"/> because callers must not build executable SQL
        /// from a rejected filter.
        /// </param>
        /// <returns>
        /// A query plan whose command returns user object IDs, a malformed-user-employee flag, and optionally agent object
        /// IDs with a <c>HasAgentRow</c> flag that is false when the user has no matching agent row. <c>RequiresAgents</c>
        /// is true when the command must join the agents table because agents are emitted or the filter references agents
        /// columns.
        /// </returns>
        public static SqlMembershipQueryPlan BuildEvaluation(
            SqlMembershipEvaluationRequest request,
            SqlFilterCompilationResult compilation)
        {
            var candidateQuery = BuildCandidateQuery(request, compilation);
            var returnsAgentColumns =
                candidateQuery.RequiresAgents &&
                request.IdentityTypes.Includes(IdentityType.Agents);

            return new SqlMembershipQueryPlan(
                candidateQuery.CommandPrefix +
                    BuildProjection(returnsAgentColumns) +
                    candidateQuery.Source,
                candidateQuery.RequiresAgents,
                returnsAgentColumns);
        }

        /// <summary>
        /// Builds the SQL command that answers whether one user or agent object ID is selected by a request.
        /// </summary>
        /// <param name="request">
        /// The evaluation request to translate. Null throws <see cref="ArgumentNullException"/>. If the request does not
        /// include <paramref name="identityType"/>, the generated command returns false for the match while still applying
        /// the same scope and filter validation rules.
        /// </param>
        /// <param name="compilation">
        /// The validated filter analysis for <paramref name="request"/>. Null throws <see cref="ArgumentNullException"/>.
        /// An invalid compilation throws <see cref="ArgumentException"/>.
        /// </param>
        /// <param name="identityType">
        /// The identity type to test. Pass users for a user object ID or agents for an agent object ID. Unsupported enum
        /// values throw <see cref="ArgumentOutOfRangeException"/>.
        /// </param>
        /// <returns>
        /// A query plan whose command returns one row with <c>Matched</c>, <c>MalformedUserObjectId</c>, and
        /// <c>MalformedUserEmployeeId</c>. Agent probes only match agent IDs that round-trip as hyphenated GUID text.
        /// </returns>
        public static SqlMembershipQueryPlan BuildProbe(
            SqlMembershipEvaluationRequest request,
            SqlFilterCompilationResult compilation,
            IdentityType identityType)
        {
            if (identityType != IdentityType.Users &&
                identityType != IdentityType.Agents)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(identityType),
                    identityType,
                    "Unsupported identity type.");
            }

            var candidateQuery = BuildCandidateQuery(request, compilation);

            return new SqlMembershipQueryPlan(
                candidateQuery.CommandPrefix +
                    BuildProbeProjection(
                        request.IdentityTypes,
                        identityType,
                        candidateQuery.RequiresAgents) +
                    candidateQuery.Source,
                candidateQuery.RequiresAgents);
        }

        /// <summary>
        /// Builds the shared part of every membership command: the <c>WITH</c> clause that finds the users in scope and,
        /// for a users-only filter, narrows them, followed by the <c>FROM</c> clause that optionally joins the agents table.
        /// The caller puts its own <c>SELECT</c> list between the two parts.
        /// </summary>
        /// <param name="request">The evaluation request. Null throws <see cref="ArgumentNullException"/>.</param>
        /// <param name="compilation">
        /// The filter analysis for <paramref name="request"/>. Null throws <see cref="ArgumentNullException"/>; a rejected
        /// filter throws <see cref="ArgumentException"/>.
        /// </param>
        /// <returns>
        /// <c>CommandPrefix</c> is the <c>WITH</c> clause, <c>Source</c> is the <c>FROM</c> clause (with the filter when it
        /// uses agents columns), and <c>RequiresAgents</c> is true when <c>Source</c> joins the agents table.
        /// </returns>
        private static (string CommandPrefix, string Source, bool RequiresAgents)
            BuildCandidateQuery(
                SqlMembershipEvaluationRequest request,
                SqlFilterCompilationResult compilation)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (compilation == null)
            {
                throw new ArgumentNullException(nameof(compilation));
            }

            if (!compilation.IsValid)
            {
                throw new ArgumentException(
                    "A valid filter compilation is required.",
                    nameof(compilation));
            }

            var requiresAgents =
                request.IdentityTypes.Includes(IdentityType.Agents) ||
                compilation.UsesAgents;
            var ctes = new List<string>
            {
                BuildUserScope(request, GetUserScopeColumnNames(compilation)),
            };
            var userSourceName = "[UserScope]";

            if (!compilation.UsesAgents &&
                !string.IsNullOrWhiteSpace(request.Filter))
            {
                ctes.Add(BuildFilteredUsers(request.Filter));
                userSourceName = "[FilteredUsers]";
            }

            var commandPrefix =
                "WITH " +
                string.Join("," + Environment.NewLine, ctes) +
                Environment.NewLine;

            return (
                commandPrefix,
                BuildCandidateSource(
                    request.TableName,
                    userSourceName,
                    request.Filter,
                    compilation,
                    requiresAgents),
                requiresAgents);
        }

        /// <summary>
        /// Lists the users columns the <c>[UserScope]</c> step must carry: the ones the rest of the command reads
        /// (<c>AzureObjectId</c> and <c>EmployeeId</c>), then any other users column the filter uses. Carrying only these,
        /// rather than every users column, keeps the rows the manager hierarchy walk stores small.
        /// </summary>
        /// <param name="compilation">The accepted filter analysis. Its referenced users columns are appended in order.</param>
        /// <returns>The column names, without duplicates (compared case-insensitively).</returns>
        private static IReadOnlyList<string> GetUserScopeColumnNames(SqlFilterCompilationResult compilation)
        {
            var columnNames = new List<string>(_requiredUserColumnNames);

            foreach (var columnName in compilation.ReferencedUserColumnNames)
            {
                if (!columnNames.Contains(columnName, StringComparer.OrdinalIgnoreCase))
                {
                    columnNames.Add(columnName);
                }
            }

            return columnNames;
        }

        /// <summary>
        /// Builds the <c>[UserScope]</c> step, which holds the users the rule can select before any filter is applied.
        /// Without a manager it is the whole users table. With a manager it walks the reporting chain down from that
        /// manager, who is at depth 1; when a depth limit is set, it goes no deeper than depth <c>@Depth</c>.
        /// </summary>
        /// <param name="request">The evaluation request; decides whether to walk a hierarchy and whether it is limited.</param>
        /// <param name="columnNames">The users columns to carry, from <see cref="GetUserScopeColumnNames"/>.</param>
        /// <returns>The step's SQL, for use inside a <c>WITH</c> clause.</returns>
        private static string BuildUserScope(
            SqlMembershipEvaluationRequest request,
            IReadOnlyList<string> columnNames)
        {
            if (request.IsFilterOnly)
            {
                return $"""
                [UserScope] AS
                (
                    SELECT {BuildColumnList("[source]", columnNames)}
                    FROM [users].{QuoteIdentifier(request.TableName)} AS [source]
                )
                """;
            }

            var recursionBound = request.Depth > 0
                ? Environment.NewLine + "    WHERE [parent].[Depth] < @Depth"
                : string.Empty;

            return $"""
            [UserScope] AS
            (
                SELECT {BuildColumnList("[source]", columnNames)}, CAST(1 AS int) AS [Depth]
                FROM [users].{QuoteIdentifier(request.TableName)} AS [source]
                WHERE [source].[EmployeeId] = @ManagerEmployeeId

                UNION ALL

                SELECT {BuildColumnList("[child]", columnNames)}, [parent].[Depth] + 1
                FROM [users].{QuoteIdentifier(request.TableName)} AS [child]
                INNER JOIN [UserScope] AS [parent]
                    ON [child].[ManagerId] = [parent].[EmployeeId]{recursionBound}
            )
            """;
        }

        /// <summary>
        /// Builds the <c>[FilteredUsers]</c> step, which applies a filter that uses only users columns to the users in
        /// scope. Applying it before the agents join means each user is filtered once, not once per agent.
        /// </summary>
        /// <param name="filter">The accepted filter text, inserted verbatim inside parentheses.</param>
        /// <returns>The step's SQL, for use inside a <c>WITH</c> clause. It carries only the columns the rest of the command reads.</returns>
        private static string BuildFilteredUsers(string filter)
        {
            return $"""
            [FilteredUsers] AS
            (
                SELECT {BuildColumnList("[users]", _requiredUserColumnNames)}
                FROM [UserScope] AS [users]
                WHERE ({filter})
            )
            """;
        }

        /// <summary>
        /// Builds the command's <c>FROM</c> clause. The users step is aliased <c>[users]</c> and the agents table
        /// <c>[agents]</c>, so filters can name either table. Every user is kept by the agents join, including users
        /// with no agents.
        /// </summary>
        /// <param name="tableName">The same-run table name shared by the users and agents tables.</param>
        /// <param name="userSourceName">The users step to read: <c>[UserScope]</c> or <c>[FilteredUsers]</c>.</param>
        /// <param name="filter">The filter text. Added as a <c>WHERE</c> clause only when it uses agents columns.</param>
        /// <param name="compilation">The accepted filter analysis.</param>
        /// <param name="requiresAgents">True to join the agents table; false reads the users step alone.</param>
        /// <returns>The <c>FROM</c> clause, with the agents join and the filter when they apply.</returns>
        private static string BuildCandidateSource(
            string tableName,
            string userSourceName,
            string filter,
            SqlFilterCompilationResult compilation,
            bool requiresAgents)
        {
            var filterClause =
                compilation.UsesAgents && !string.IsNullOrWhiteSpace(filter)
                    ? Environment.NewLine + "WHERE (" + filter + ")"
                    : string.Empty;

            if (!requiresAgents)
            {
                return $"""
                FROM {userSourceName} AS [users]{filterClause}
                """;
            }

            return $"""
            FROM {userSourceName} AS [users]
            LEFT JOIN [agents].{QuoteIdentifier(tableName)} AS [agents]
                ON [agents].[AgentManagerId] = [users].[EmployeeId]{filterClause}
            """;
        }

        /// <summary>
        /// Builds the <c>SELECT</c> list of the full evaluation command: one row per selected user, or per user and agent
        /// pair when agents are returned.
        /// </summary>
        /// <param name="returnsAgentColumns">True to add <c>AgentObjectId</c> and <c>HasAgentRow</c>.</param>
        /// <returns>
        /// The <c>SELECT</c> list with <c>UserObjectId</c>, <c>MalformedUserEmployeeId</c> (true when the user's
        /// EmployeeId is null) and, when requested, <c>AgentObjectId</c> and <c>HasAgentRow</c> (false when the
        /// user has no agents).
        /// </returns>
        private static string BuildProjection(bool returnsAgentColumns)
        {
            // The join compares AgentManagerId for equality, so it is never NULL on a matched agent row.
            var agentColumns = returnsAgentColumns
                ? """
                  ,
                      [agents].[AgentObjectId] AS [AgentObjectId],
                      CAST(CASE WHEN [agents].[AgentManagerId] IS NULL THEN 0 ELSE 1 END AS bit) AS [HasAgentRow]
                  """
                : string.Empty;

            return $"""
            SELECT
                [users].[AzureObjectId] AS [UserObjectId],
                CAST(CASE WHEN [users].[EmployeeId] IS NULL THEN 1 ELSE 0 END AS bit) AS [MalformedUserEmployeeId]{agentColumns}

            """;
        }

        /// <summary>
        /// Builds the <c>SELECT</c> list of the probe command, which collapses the whole candidate set into one row
        /// instead of returning it.
        /// </summary>
        /// <param name="identityTypes">The identity types the rule selects. A probe for a type outside it never matches.</param>
        /// <param name="identityType">Whether <c>@ObjectId</c> is a user or an agent object ID.</param>
        /// <param name="requiresAgents">True when the <c>FROM</c> clause joins the agents table.</param>
        /// <returns>
        /// The <c>SELECT</c> list with <c>Matched</c> (whether <c>@ObjectId</c> is selected) and the
        /// <c>MalformedUserObjectId</c> and <c>MalformedUserEmployeeId</c> flags, each true when any candidate has that
        /// problem, so the probe fails the same way the full evaluation would.
        /// </returns>
        private static string BuildProbeProjection(
            IdentityTypeSet identityTypes,
            IdentityType identityType,
            bool requiresAgents)
        {
            var targetIncluded = identityTypes.Includes(identityType);

            // The sync skips agent IDs that are not hyphenated GUID text, so they must not match here either. SQL's =
            // ignores trailing spaces, so the text must also be exactly 36 characters (72 bytes as nvarchar).
            var matchedExpression = identityType == IdentityType.Users
                ? targetIncluded
                    ? "TRY_CONVERT(uniqueidentifier, [users].[AzureObjectId]) = @ObjectId"
                    : "1 = 0"
                : targetIncluded && requiresAgents
                    ? """
                      TRY_CONVERT(uniqueidentifier, [agents].[AgentObjectId]) = @ObjectId
                                      AND DATALENGTH(CONVERT(nvarchar(4000), [agents].[AgentObjectId])) = 72
                                      AND LOWER(CONVERT(
                                          nvarchar(36),
                                          TRY_CONVERT(uniqueidentifier, [agents].[AgentObjectId]))) COLLATE Latin1_General_100_BIN2
                                          = LOWER([agents].[AgentObjectId]) COLLATE Latin1_General_100_BIN2
                      """
                    : "1 = 0";

            return $"""
            SELECT
                CAST(COALESCE(MAX(
                    CASE WHEN {matchedExpression} THEN 1 ELSE 0 END), 0) AS bit) AS [Matched],
                CAST(COALESCE(MAX(
                    CASE
                        WHEN [users].[AzureObjectId] IS NULL
                            OR TRY_CONVERT(uniqueidentifier, [users].[AzureObjectId]) IS NULL
                        THEN 1
                        ELSE 0
                    END), 0) AS bit) AS [MalformedUserObjectId],
                CAST(COALESCE(MAX(
                    CASE WHEN [users].[EmployeeId] IS NULL THEN 1 ELSE 0 END), 0) AS bit) AS [MalformedUserEmployeeId]

            """;
        }

        /// <summary>
        /// Builds a comma-separated column list such as <c>[source].[AzureObjectId], [source].[EmployeeId]</c>.
        /// </summary>
        /// <param name="tableAlias">The already-bracketed alias to qualify each column with, such as <c>[source]</c>.</param>
        /// <param name="columnNames">The unquoted column names, in output order.</param>
        /// <returns>The column list, with each name bracket-quoted.</returns>
        private static string BuildColumnList(
            string tableAlias,
            IEnumerable<string> columnNames)
        {
            return string.Join(
                ", ",
                columnNames.Select(columnName => tableAlias + "." + QuoteIdentifier(columnName)));
        }

        /// <summary>
        /// Wraps a SQL Server name in square brackets so it is read as a name, never as SQL. A <c>]</c> inside the name is
        /// doubled, so <c>odd]name</c> becomes <c>[odd]]name]</c>.
        /// </summary>
        /// <param name="identifier">The unquoted name. Null, empty or whitespace throws <see cref="ArgumentException"/>.</param>
        /// <returns>The bracket-quoted name.</returns>
        private static string QuoteIdentifier(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                throw new ArgumentException(
                    "An identifier is required.",
                    nameof(identifier));
            }

            return "[" + identifier.Replace("]", "]]") + "]";
        }
    }
}
