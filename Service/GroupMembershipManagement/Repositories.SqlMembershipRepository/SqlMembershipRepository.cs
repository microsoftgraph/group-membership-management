// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Core.Pipeline;
using Azure.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.Graph.Models;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Models;
using Polly;
using Polly.Retry;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using SqlMembershipObtainer.Entities;
using System;
using System.Collections.Generic;
using System.Data;
using System.Text.RegularExpressions;

namespace Repositories.SqlMembershipRepository
{
    public class SqlMembershipRepository : ISqlMembershipRepository
    {

        private readonly string _sqlServerConnectionString = null;

        // Table names can start with a letter, digit, or underscore (e.g. GUID-based names)
        private static readonly Regex ValidTableNamePattern = new(@"^[A-Za-z0-9_][A-Za-z0-9_\-]*$", RegexOptions.Compiled);
        // Attribute names must start with a letter or underscore (standard SQL column identifiers)
        private static readonly Regex ValidAttributeNamePattern = new(@"^[A-Za-z_][A-Za-z0-9_\-]*$", RegexOptions.Compiled);

        // The agents join and projection read these columns; any other agents column is needed only if a filter names it.
        private static readonly string[] _requiredAgentColumnNames = { "AgentObjectId", "AgentManagerId" };

        // Caps the browse/search page for attribute mappings. Large attributes (CostCenter is ~300K rows)
        // must never be returned to the UI in a single response.
        public const int DefaultAttributeMappingsPageSize = 500;
        public const int MaxAttributeMappingsPageSize = 500;
        // Upper bound of codes accepted by a single resolve call, well under SQL Server's 2100 parameter limit.
        public const int MaxAttributeMappingCodesPerRequest = 1000;

        public SqlMembershipRepository(IKeyVaultSecret<ISqlMembershipRepository> sqlServerConnectionString)
        {
            _sqlServerConnectionString = sqlServerConnectionString?.Secret ?? throw new ArgumentNullException(nameof(sqlServerConnectionString));
        }

        private static void ValidateTableName(string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName))
                throw new ArgumentException($"'{nameof(tableName)}' cannot be null or empty.", nameof(tableName));

            if (!ValidTableNamePattern.IsMatch(tableName))
                throw new ArgumentException($"'{nameof(tableName)}' contains invalid characters. Only letters, digits, underscores, and hyphens are allowed.", nameof(tableName));
        }

        private static void ValidateAttributeName(string attribute)
        {
            if (string.IsNullOrWhiteSpace(attribute))
                throw new ArgumentException($"'{nameof(attribute)}' cannot be null or empty.", nameof(attribute));

            if (!ValidAttributeNamePattern.IsMatch(attribute))
                throw new ArgumentException($"'{nameof(attribute)}' contains invalid characters. Only letters, digits, underscores, and hyphens are allowed, and must start with a letter or underscore.", nameof(attribute));
        }

        public async Task<List<PersonEntity>> GetChildEntitiesAsync(string filter, int personnelNumber, string tableName, int depth)
        {
            ValidateTableName(tableName);

            var children = new List<PersonEntity>();
            var retryPolicy = GetRetryPolicyAsync();

            try
            {
                var depthQuery = depth <= 0 ? "WHERE Depth > 0" : " WHERE Depth <= @Depth";
                var filterQuery = string.IsNullOrWhiteSpace(filter) ? "" : $" AND ({filter})";
                var selectQuery = @$"
                        WITH emp AS (
                              SELECT *, 1 AS Depth
                              FROM [users].[{tableName}]
                              WHERE EmployeeId = @PersonnelNumber

                              UNION ALL

                              SELECT e.*, emp.Depth + 1
                              FROM [users].[{tableName}] e INNER JOIN emp
                              ON e.ManagerId = emp.EmployeeId
                        )
                        SELECT *
                        FROM emp e {depthQuery} {filterQuery}";

                await retryPolicy.ExecuteAsync(async () =>
                {
                    using (var conn = new SqlConnection(_sqlServerConnectionString))
                    {
                        await conn.OpenAsync();
                        using (var cmd = new SqlCommand(selectQuery, conn))
                        {
                            cmd.Parameters.Add(new SqlParameter("@PersonnelNumber", SqlDbType.Int) { Value = personnelNumber });
                            if (depth > 0)
                            {
                                cmd.Parameters.Add(new SqlParameter("@Depth", SqlDbType.Int) { Value = depth });
                            }

                            using (var reader = await cmd.ExecuteReaderAsync(CommandBehavior.CloseConnection))
                            {
                                int id = reader.GetOrdinal("EmployeeId");
                                int azureObjectId = reader.GetOrdinal("AzureObjectId");

                                while (reader.Read())
                                {
                                    var response = new PersonEntity
                                    {
                                        PersonnelNumber = reader.IsDBNull(id) ? null : reader.GetInt32(id).ToString(),
                                        AzureObjectId = reader.IsDBNull(azureObjectId) ? null : reader.GetString(azureObjectId)
                                    };
                                    children.Add(response);
                                }
                                await reader.CloseAsync();
                            }
                        }
                        conn.Close();
                    }
                });
            }
            catch (SqlException ex)
            {
                throw ex;
            }

            return children;
        }

        public async Task<(int maxDepth, int id)> GetOrgLeaderDetailsAsync(string azureObjectId, string tableName)
        {
            ValidateTableName(tableName);

            var retryPolicy = GetRetryPolicyAsync();
            int maxDepth = 0;
            int employeeId = 0;

            try
            {
                var selectDepthQuery = @$"
                    WITH emp AS (
                            SELECT *, 1 AS Depth
                            FROM [users].[{tableName}]
                            WHERE AzureObjectId = @AzureObjectId

                            UNION ALL

                            SELECT e.*, emp.Depth + 1
                            FROM [users].[{tableName}] e INNER JOIN emp
                            ON e.ManagerId = emp.EmployeeId
                    )
                    SELECT MAX(Depth) AS MaxDepth
                    FROM emp e
                ";

                var selectIdQuery = $"SELECT EmployeeId FROM [users].[{tableName}] WHERE AzureObjectId = @AzureObjectId";

                await retryPolicy.ExecuteAsync(async () =>
                {
                    using (var conn = new SqlConnection(_sqlServerConnectionString))
                    {
                        await conn.OpenAsync();
                        using (var cmd = new SqlCommand(selectDepthQuery, conn))
                        {
                            cmd.Parameters.Add(new SqlParameter("@AzureObjectId", SqlDbType.NVarChar, 128) { Value = azureObjectId });

                            using (var reader = await cmd.ExecuteReaderAsync())
                            {
                                int maxDepthOrdinal = reader.GetOrdinal("MaxDepth");

                                while (reader.Read())
                                {
                                    maxDepth = reader.IsDBNull(maxDepthOrdinal) ? 0 : reader.GetInt32(maxDepthOrdinal);
                                }
                                reader.Close();
                            }
                        }

                        using (var cmd = new SqlCommand(selectIdQuery, conn))
                        {
                            cmd.Parameters.Add(new SqlParameter("@AzureObjectId", SqlDbType.NVarChar, 128) { Value = azureObjectId });

                            using (var reader = cmd.ExecuteReader())
                            {
                                int idOrdinal = reader.GetOrdinal("EmployeeId");

                                while (reader.Read())
                                {
                                    employeeId = reader.IsDBNull(idOrdinal) ? 0 : reader.GetInt32(idOrdinal);
                                }
                                await reader.CloseAsync();
                            }
                        }

                        conn.Close();
                    }
                });
            }
            catch (SqlException ex)
            {
                throw ex;
            }

            return (maxDepth, employeeId);
        }

        public async Task<List<PersonEntity>> FilterChildEntitiesAsync(string query, string tableName)
        {
            ValidateTableName(tableName);

            var filteredChildren = new List<PersonEntity>();
            var retryPolicy = GetRetryPolicyAsync();
            try
            {
                var selectQuery = $"SELECT EmployeeId, AzureObjectId FROM [users].[{tableName}] WHERE {query}";

                await retryPolicy.ExecuteAsync(async () =>
                {
                    using (var conn = new SqlConnection(_sqlServerConnectionString))
                    {
                        await conn.OpenAsync();
                        using (var cmd = new SqlCommand(selectQuery, conn))
                        {
                            using (var reader = await cmd.ExecuteReaderAsync(CommandBehavior.CloseConnection))
                            {
                                int personnelNumber = reader.GetOrdinal("EmployeeId");
                                int azureObjectId = reader.GetOrdinal("AzureObjectId");
                                while (reader.Read())
                                {
                                    var response = new PersonEntity
                                    {
                                        PersonnelNumber = reader.IsDBNull(personnelNumber) ? null : reader.GetInt32(personnelNumber).ToString(),
                                        AzureObjectId = reader.IsDBNull(azureObjectId) ? null : reader.GetString(azureObjectId)
                                    };
                                    filteredChildren.Add(response);
                                }
                                await reader.CloseAsync();
                            }
                        }
                        conn.Close();
                    }
                });
            }
            catch (SqlException ex)
            {
                throw ex;
            }

            return filteredChildren;
        }

        public async Task<bool> IsUserInFilterAsync(string filter, string tableName, string azureObjectId)
        {
            ValidateTableName(tableName);

            if (string.IsNullOrWhiteSpace(filter) || string.IsNullOrWhiteSpace(azureObjectId))
            {
                return false;
            }

            var retryPolicy = GetRetryPolicyAsync();
            var isMember = false;

            // Validate the filter using the same WHERE-clause parser used for membership obtaining
            // before interpolating it into the executed statement. This guards against malformed or
            // malicious filters from stored job configuration altering the query (SQL injection).
            var validColumnNames = await GetColumnNamesAsync(tableName);
            var validationStatement = $"SELECT * FROM [users].[{tableName}] WHERE ({filter})";
            var (isValidFilter, _) = IsValidWhereClause(validationStatement, validColumnNames);
            if (!isValidFilter)
            {
                return false;
            }

            try
            {
                var selectQuery = $"SELECT TOP 1 1 FROM [users].[{tableName}] WHERE AzureObjectId = @AzureObjectId AND ({filter})";

                await retryPolicy.ExecuteAsync(async () =>
                {
                    using (var conn = new SqlConnection(_sqlServerConnectionString))
                    {
                        await conn.OpenAsync();
                        using (var cmd = new SqlCommand(selectQuery, conn))
                        {
                            cmd.Parameters.Add(new SqlParameter("@AzureObjectId", SqlDbType.NVarChar, 128) { Value = azureObjectId });
                            var result = await cmd.ExecuteScalarAsync();
                            isMember = result != null && result != DBNull.Value;
                        }
                        conn.Close();
                    }
                });
            }
            catch (SqlException ex)
            {
                throw ex;
            }

            return isMember;
        }

        public async Task<bool> CheckIfTableExistsAsync(string tableName)
        {
            ValidateTableName(tableName);

            bool tableExists = false;
            var retryPolicy = GetRetryPolicyAsync();
            try
            {
                await retryPolicy.ExecuteAsync(async () =>
                {
                    using (var conn = new SqlConnection(_sqlServerConnectionString))
                    {
                        await conn.OpenAsync();
                        var selectQuery = "SELECT count(TABLE_NAME) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @TableName AND TABLE_SCHEMA = 'users'";
                        using (var cmd = new SqlCommand(selectQuery, conn))
                        {
                            cmd.Parameters.Add(new SqlParameter("@TableName", SqlDbType.NVarChar, 128) { Value = tableName });
                            var result = (int)cmd.ExecuteScalar();
                            tableExists = result > 0;
                        }
                        await conn.CloseAsync();
                    }
                });
            }
            catch (SqlException ex)
            {
               throw ex;
            }

            return tableExists;
        }

        public async Task<List<string>> GetColumnNamesAsync(string tableName)
        {
            ValidateTableName(tableName);

            var HRColumns = new List<string>();
            var retryPolicy = GetRetryPolicyAsync();
            try
            {
                var selectQuery = $"SELECT name FROM sys.columns WHERE object_id = OBJECT_ID('[users].[{tableName}]') ORDER BY name";

                await retryPolicy.ExecuteAsync(async () =>
                {
                    using (var conn = new SqlConnection(_sqlServerConnectionString))
                    {
                        await conn.OpenAsync();
                        using (var cmd = new SqlCommand(selectQuery, conn))
                        {
                            using (var reader = await cmd.ExecuteReaderAsync(CommandBehavior.CloseConnection))
                            {
                                int name = reader.GetOrdinal("name");

                                while (reader.Read())
                                {
                                    var columnName = reader.IsDBNull(name) ? null : reader.GetString(name);
                                    HRColumns.Add(columnName);
                                }
                                reader.Close();
                            }
                        }
                        await conn.CloseAsync();
                    }
                });
            }
            catch (SqlException ex)
            {
                throw ex;
            }

            return HRColumns;
        }

        private async Task<(List<string> UserColumnNames, List<string> AgentColumnNames)>
            GetFilterColumnNamesAsync(string tableName)
        {
            ValidateTableName(tableName);

            // Column metadata stays visible when SELECT is denied, so an agents table the caller cannot read reports no
            // columns and is treated like a missing one.
            const string selectQuery = """
                SELECT
                    [name],
                    CAST(CASE WHEN [object_id] = OBJECT_ID(@AgentsTableName) THEN 1 ELSE 0 END AS bit) AS [IsAgentsColumn]
                FROM sys.columns
                WHERE [object_id] = OBJECT_ID(@UsersTableName)
                    OR ([object_id] = OBJECT_ID(@AgentsTableName)
                        AND HAS_PERMS_BY_NAME(@AgentsTableName, N'OBJECT', N'SELECT') = 1)
                """;

            var userColumnNames = new List<string>();
            var agentColumnNames = new List<string>();
            var retryPolicy = GetRetryPolicyAsync();

            await retryPolicy.ExecuteAsync(async () =>
            {
                userColumnNames.Clear();
                agentColumnNames.Clear();

                using (var connection = new SqlConnection(_sqlServerConnectionString))
                {
                    await connection.OpenAsync();

                    using (var command = new SqlCommand(selectQuery, connection))
                    {
                        command.Parameters.Add(new SqlParameter("@UsersTableName", SqlDbType.NVarChar) { Value = $"[users].[{tableName}]" });
                        command.Parameters.Add(new SqlParameter("@AgentsTableName", SqlDbType.NVarChar) { Value = $"[agents].[{tableName}]" });

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                var columnNames = reader.GetBoolean(1) ? agentColumnNames : userColumnNames;
                                columnNames.Add(reader.GetString(0));
                            }
                        }
                    }
                }
            });

            return (userColumnNames, agentColumnNames);
        }

        /// <summary>
        /// Validates one SQL filter against known users and agents columns, and reports which table types the filter
        /// references.
        /// </summary>
        /// <param name="filter">
        /// The WHERE fragment as stored on the sync job, such as <c>Department = 'Sales'</c> or
        /// <c>agents.BlueprintId = 'alpha'</c>. A <c>users.</c> or <c>agents.</c> prefix, in any casing and with or without
        /// brackets, limits the column to that table. A column without either prefix is looked up in the users table first
        /// and then in the agents table. Empty or syntactically invalid filters return an invalid compilation; this helper
        /// does not treat an empty filter as "no filter".
        /// </param>
        /// <param name="tableName">
        /// The users and agents table name without schema. The value is embedded in the parse-only SELECT text and is
        /// expected to have already been validated by the caller.
        /// </param>
        /// <param name="userColumnNames">
        /// The users table's column names. An unprefixed column present in both collections counts as a users column.
        /// </param>
        /// <param name="agentColumnNames">
        /// The agents table's column names. Pass an empty collection when the agents table is missing or unreadable. In
        /// that case a column written with the <c>agents.</c> prefix counts as an agents column, so the caller can fail the
        /// rule with the agents input check, while an unprefixed column that is not a users column is rejected as invalid.
        /// </param>
        /// <returns>
        /// A valid compilation with the referenced identity types and the users columns the filter uses (spelled as in
        /// <paramref name="userColumnNames"/>), or an invalid compilation with the parser error or
        /// <c>Invalid column name detected: {name}</c>. The name is shown without its prefix, so
        /// <c>users.BlueprintId</c> is reported as <c>BlueprintId</c>. It does not throw for bad filter syntax.
        /// </returns>
        internal static SqlFilterCompilationResult CheckFilter(
            string filter,
            string tableName,
            IReadOnlyCollection<string> userColumnNames,
            IReadOnlyCollection<string> agentColumnNames)
        {
            var whereStatement = $"SELECT * FROM [users].[{tableName}] WHERE {filter}";
            var (isValid, error) = TryCollectColumns(whereStatement, out var columnCollector);

            if (!isValid)
            {
                return SqlFilterCompilationResult.Invalid(error);
            }

            var agentsTableIsReadable = agentColumnNames.Count > 0;
            var referencedIdentityTypes = new List<IdentityType>();
            var referencedUserColumnNames = new List<string>();

            foreach (var (qualifier, columnName) in columnCollector.ColumnReferences)
            {
                var namesUsersTable = string.Equals(qualifier, "users", StringComparison.OrdinalIgnoreCase);
                var namesAgentsTable = string.Equals(qualifier, "agents", StringComparison.OrdinalIgnoreCase);

                if (!namesAgentsTable)
                {
                    var matchingUserColumnNames = userColumnNames
                        .Where(userColumnName => string.Equals(userColumnName, columnName, StringComparison.OrdinalIgnoreCase))
                        .ToArray();

                    if (matchingUserColumnNames.Length > 0)
                    {
                        referencedIdentityTypes.Add(IdentityType.Users);
                        referencedUserColumnNames.AddRange(matchingUserColumnNames);
                        continue;
                    }
                }

                if (!namesUsersTable &&
                    agentColumnNames.Contains(columnName, StringComparer.OrdinalIgnoreCase))
                {
                    referencedIdentityTypes.Add(IdentityType.Agents);
                    continue;
                }

                // Only the agents. prefix shows that a column was meant for a missing agents table; an unprefixed name
                // could just as well be a misspelled users column.
                if (namesAgentsTable && !agentsTableIsReadable)
                {
                    referencedIdentityTypes.Add(IdentityType.Agents);
                    continue;
                }

                return SqlFilterCompilationResult.Invalid($"Invalid column name detected: {columnName}");
            }

            return SqlFilterCompilationResult.Valid(referencedIdentityTypes, referencedUserColumnNames);
        }

        /// <summary>
        /// Evaluates one SQL membership rule and returns the selected user and agent object IDs.
        /// </summary>
        /// <param name="request">
        /// The table, optional manager scope, optional filter, selected identity types, and raw ADF run ID to evaluate.
        /// Null throws <see cref="ArgumentNullException"/>. Empty identity selections, blank or invalid table names, and
        /// blank raw ADF run IDs are rejected before the query is built. A null or empty filter means no filter.
        /// </param>
        /// <returns>
        /// A result containing distinct emitted users and agents. The lists can be empty. Users are emitted only when
        /// requested; selected human rows are still counted for logging. Malformed agent object IDs, including a matched
        /// agent row whose object ID is NULL, are skipped and counted. Missing required users rows, malformed selected
        /// user object IDs, and missing filter-only EmployeeId values throw <see cref="InvalidOperationException"/>.
        /// When agents are required, a missing or unreadable agents table, or one without the <c>AgentObjectId</c> and
        /// <c>AgentManagerId</c> columns, also throws <see cref="InvalidOperationException"/> before the query runs.
        /// </returns>
        public async Task<SqlMembershipEvaluationResult> EvaluateMembershipAsync(
            SqlMembershipEvaluationRequest request)
        {
            ValidateEvaluationRequest(request);

            var (compilation, agentColumnNames) = await PrepareEvaluationCompilationAsync(request);

            var plan = SqlMembershipQueryBuilder.BuildEvaluation(
                request,
                compilation);
            EnsureAgentsInputIsUsable(plan, agentColumnNames);
            var retryPolicy = GetMembershipEvaluationRetryPolicyAsync();
            var skippedAgentRowCount = 0;

            var selected = await ExecuteWithFreshEvaluationBuffersAsync(
                operation => retryPolicy.ExecuteAsync(operation),
                async (attemptUsers, attemptAgents) =>
                {
                    skippedAgentRowCount = 0;

                    using (var connection = new SqlConnection(_sqlServerConnectionString))
                    {
                        await connection.OpenAsync();

                        using (var command = new SqlCommand(plan.CommandText, connection))
                        {
                            AddEvaluationParameters(command, request, plan);

                            using (var reader = await command.ExecuteReaderAsync(CommandBehavior.CloseConnection))
                            {
                                while (await reader.ReadAsync())
                                {
                                    if (reader.IsDBNull(0))
                                    {
                                        throw new InvalidOperationException(
                                            $"[users].[{request.TableName}] contains a selected row without an AzureObjectId.");
                                    }

                                    if (request.IsFilterOnly &&
                                        reader.GetBoolean(1))
                                    {
                                        throw new InvalidOperationException(
                                            $"[users].[{request.TableName}] contains a selected row without an EmployeeId.");
                                    }

                                    // HR rows without an Entra account carry the all-zero ID; they have always passed through.
                                    if (!Guid.TryParse(reader.GetString(0), out var userObjectId))
                                    {
                                        throw new InvalidOperationException(
                                            $"[users].[{request.TableName}] contains a selected AzureObjectId that is not a GUID.");
                                    }

                                    attemptUsers.Add(userObjectId);

                                    if (!plan.ReturnsAgentColumns ||
                                        !reader.GetBoolean(3))
                                    {
                                        continue;
                                    }

                                    var agentObjectIdText = reader.IsDBNull(2) ? null : reader.GetString(2);

                                    // TryParseExact "D" still accepts "+" and "0x" inside a group, so require the text to round-trip.
                                    if (agentObjectIdText == null ||
                                        !Guid.TryParseExact(agentObjectIdText, "D", out var agentObjectId) ||
                                        agentObjectId == Guid.Empty ||
                                        !string.Equals(agentObjectId.ToString("D"), agentObjectIdText, StringComparison.OrdinalIgnoreCase))
                                    {
                                        skippedAgentRowCount++;
                                        continue;
                                    }

                                    attemptAgents.Add(agentObjectId);
                                }
                            }
                        }
                    }
                });

            var emittedUsers = request.IdentityTypes.Includes(Models.IdentityType.Users)
                ? selected.Users
                : Enumerable.Empty<Guid>();

            return new SqlMembershipEvaluationResult(
                emittedUsers,
                selected.Agents,
                selected.Users.Count,
                compilation.ReferencedIdentityTypes,
                skippedAgentRowCount);
        }

        /// <summary>
        /// Answers whether one user or agent object ID is selected by one SQL membership rule.
        /// </summary>
        /// <param name="request">
        /// The table, optional manager scope, optional filter, selected identity types, and raw ADF run ID to evaluate.
        /// Null throws <see cref="ArgumentNullException"/>. If <paramref name="identityType"/> is not selected by the
        /// request, the method returns false after any required scope, filter, and agents-input validation succeeds.
        /// </param>
        /// <param name="objectId">
        /// The user or agent object ID to test. <see cref="Guid.Empty"/> throws <see cref="ArgumentException"/>.
        /// </param>
        /// <param name="identityType">
        /// The kind of identity represented by <paramref name="objectId"/>. Pass users for a user object ID or agents for
        /// an agent object ID. Unsupported enum values throw <see cref="ArgumentOutOfRangeException"/>.
        /// </param>
        /// <returns>
        /// True when the rule selects that identity; false when it does not. For agents, only hyphenated GUID text in the
        /// agents table can match. When agents are required, a missing or unreadable agents table, or one without the
        /// <c>AgentObjectId</c> and <c>AgentManagerId</c> columns, throws <see cref="InvalidOperationException"/>.
        /// </returns>
        public async Task<bool> IsIdentitySelectedAsync(
            SqlMembershipEvaluationRequest request,
            Guid objectId,
            Models.IdentityType identityType)
        {
            ValidateEvaluationRequest(request);

            if (objectId == Guid.Empty)
            {
                throw new ArgumentException(
                    "The object identifier must not be empty.",
                    nameof(objectId));
            }

            if (identityType != Models.IdentityType.Users &&
                identityType != Models.IdentityType.Agents)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(identityType),
                    identityType,
                    "Unsupported identity type.");
            }

            var (compilation, agentColumnNames) = await PrepareEvaluationCompilationAsync(request);
            var plan = SqlMembershipQueryBuilder.BuildProbe(
                request,
                compilation,
                identityType);
            EnsureAgentsInputIsUsable(plan, agentColumnNames);
            var isSelected = false;
            var retryPolicy = GetMembershipEvaluationRetryPolicyAsync();

            await retryPolicy.ExecuteAsync(async () =>
            {
                using (var connection = new SqlConnection(_sqlServerConnectionString))
                {
                    await connection.OpenAsync();

                    using (var command = new SqlCommand(plan.CommandText, connection))
                    {
                        AddEvaluationParameters(command, request, plan);
                        command.Parameters.Add(
                            new SqlParameter("@ObjectId", SqlDbType.UniqueIdentifier)
                            {
                                Value = objectId,
                            });

                        using (var reader = await command.ExecuteReaderAsync(CommandBehavior.CloseConnection))
                        {
                            if (!await reader.ReadAsync())
                            {
                                throw new InvalidOperationException(
                                    "SQL membership evaluation did not return a probe result.");
                            }

                            if (reader.GetBoolean(1))
                            {
                                throw new InvalidOperationException(
                                    $"[users].[{request.TableName}] contains a selected AzureObjectId that is not a GUID.");
                            }

                            if (request.IsFilterOnly &&
                                reader.GetBoolean(2))
                            {
                                throw new InvalidOperationException(
                                    $"[users].[{request.TableName}] contains a selected row without an EmployeeId.");
                            }

                            isSelected = reader.GetBoolean(0);
                        }
                    }
                }
            });

            return isSelected;
        }

        private static void ValidateEvaluationRequest(
            SqlMembershipEvaluationRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            ValidateTableName(request.TableName);

            if (request.IdentityTypes.IsEmpty)
            {
                throw new ArgumentException(
                    "At least one identity type must be selected.",
                    nameof(request));
            }

            if (string.IsNullOrWhiteSpace(request.AdfRunId))
            {
                throw new ArgumentException(
                    "The raw ADF run identifier is required.",
                    nameof(request));
            }
        }

        private async Task<(SqlFilterCompilationResult Compilation, IReadOnlyCollection<string> AgentColumnNames)>
            PrepareEvaluationCompilationAsync(
                SqlMembershipEvaluationRequest request)
        {
            var (userColumnNames, agentColumnNames) =
                await GetFilterColumnNamesAsync(request.TableName);

            if (userColumnNames.Count == 0)
            {
                throw new InvalidOperationException(
                    $"The required users table [users].[{request.TableName}] is unavailable.");
            }

            if (string.IsNullOrWhiteSpace(request.Filter))
            {
                return (SqlFilterCompilationResult.Valid(Array.Empty<IdentityType>(), Array.Empty<string>()), agentColumnNames);
            }

            var compilation = CheckFilter(
                request.Filter,
                request.TableName,
                userColumnNames,
                agentColumnNames);

            if (!compilation.IsValid)
            {
                throw new InvalidOperationException(compilation.Error);
            }

            return (compilation, agentColumnNames);
        }

        private static void AddEvaluationParameters(
            SqlCommand command,
            SqlMembershipEvaluationRequest request,
            SqlMembershipQueryPlan plan)
        {
            if (request.ManagerEmployeeId.HasValue)
            {
                command.Parameters.Add(
                    new SqlParameter("@ManagerEmployeeId", SqlDbType.Int)
                    {
                        Value = request.ManagerEmployeeId.Value,
                    });
            }

            if (request.ManagerEmployeeId.HasValue &&
                request.Depth > 0)
            {
                command.Parameters.Add(
                    new SqlParameter("@Depth", SqlDbType.Int)
                    {
                        Value = request.Depth,
                    });
            }
        }

        private static void EnsureAgentsInputIsUsable(
            SqlMembershipQueryPlan plan,
            IReadOnlyCollection<string> agentColumnNames)
        {
            if (!plan.RequiresAgents)
            {
                return;
            }

            var agentsInputError = GetAgentsInputError(agentColumnNames);

            if (agentsInputError != null)
            {
                throw new InvalidOperationException(agentsInputError);
            }
        }

        private static string? GetAgentsInputError(
            IReadOnlyCollection<string> agentColumnNames)
        {
            if (agentColumnNames.Count == 0)
            {
                return "Required agents input is unavailable.";
            }

            var missingColumnNames = _requiredAgentColumnNames
                .Where(name => !agentColumnNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (missingColumnNames.Count == 0)
            {
                return null;
            }

            return $"Required agents input is missing columns: {string.Join(", ", missingColumnNames)}.";
        }

        /// <summary>
        /// Runs a retryable evaluation operation with new result buffers for each attempt.
        /// </summary>
        /// <param name="executeWithRetry">
        /// The retry wrapper that invokes the supplied operation. Null throws <see cref="ArgumentNullException"/>. If the
        /// wrapper completes without invoking the operation, the method returns empty user and agent sets.
        /// </param>
        /// <param name="executeAttempt">
        /// The operation to run for one attempt. It receives fresh user and agent sets to fill. Null throws
        /// <see cref="ArgumentNullException"/>. Exceptions from an attempt are handled by <paramref name="executeWithRetry"/>
        /// and propagate if the retry wrapper gives up.
        /// </param>
        /// <returns>
        /// The user and agent sets from the successful attempt. IDs collected by failed attempts are discarded rather than
        /// mixed into the final result.
        /// </returns>
        internal static async Task<(HashSet<Guid> Users, HashSet<Guid> Agents)>
            ExecuteWithFreshEvaluationBuffersAsync(
                Func<Func<Task>, Task> executeWithRetry,
                Func<HashSet<Guid>, HashSet<Guid>, Task> executeAttempt)
        {
            if (executeWithRetry == null)
            {
                throw new ArgumentNullException(nameof(executeWithRetry));
            }

            if (executeAttempt == null)
            {
                throw new ArgumentNullException(nameof(executeAttempt));
            }

            HashSet<Guid>? selectedUsers = null;
            HashSet<Guid>? selectedAgents = null;

            await executeWithRetry(async () =>
            {
                var attemptUsers = new HashSet<Guid>();
                var attemptAgents = new HashSet<Guid>();

                await executeAttempt(attemptUsers, attemptAgents);

                selectedUsers = attemptUsers;
                selectedAgents = attemptAgents;
            });

            return (
                selectedUsers ?? new HashSet<Guid>(),
                selectedAgents ?? new HashSet<Guid>());
        }

        public async Task<(int maxDepth, string azureObjectId)> GetOrgLeaderAsync(int employeeId, string tableName)
        {
            ValidateTableName(tableName);

            var retryPolicy = GetRetryPolicyAsync();
            int maxDepth = 0;
            string azureObjectId = "";

            try
            {
                var selectDepthQuery = @$"
                    WITH emp AS (
                            SELECT EmployeeId, 1 AS Depth
                            FROM [users].[{tableName}]
                            WHERE EmployeeId = @EmployeeId

                            UNION ALL

                            SELECT e.EmployeeId, emp.Depth + 1
                            FROM [users].[{tableName}] e INNER JOIN emp
                            ON e.ManagerId = emp.EmployeeId
                    )
                    SELECT MAX(Depth) AS MaxDepth
                    FROM emp e
                ";

                var selectIdQuery = $"SELECT AzureObjectId FROM [users].[{tableName}] WHERE EmployeeId = @EmployeeId";

                await retryPolicy.ExecuteAsync(async () =>
                {
                    using (var conn = new SqlConnection(_sqlServerConnectionString))
                    {
                        await conn.OpenAsync();
                        using (var cmd = new SqlCommand(selectDepthQuery, conn))
                        {
                            cmd.Parameters.Add(new SqlParameter("@EmployeeId", SqlDbType.Int) { Value = employeeId });

                            using (var reader = await cmd.ExecuteReaderAsync())
                            {
                                int maxDepthOrdinal = reader.GetOrdinal("MaxDepth");

                                if (reader.Read())
                                {
                                    maxDepth = reader.IsDBNull(maxDepthOrdinal) ? 0 : reader.GetInt32(maxDepthOrdinal);
                                }
                                await reader.CloseAsync();
                            }
                        }

                        using (var cmd = new SqlCommand(selectIdQuery, conn))
                        {
                            cmd.Parameters.Add(new SqlParameter("@EmployeeId", SqlDbType.Int) { Value = employeeId });

                            using (var reader = cmd.ExecuteReader())
                            {
                                int idOrdinal = reader.GetOrdinal("AzureObjectId");

                                if (reader.Read())
                                {
                                    azureObjectId = reader.IsDBNull(idOrdinal) ? String.Empty : reader.GetString(idOrdinal);
                                }
                                await reader.CloseAsync();
                            }
                        }

                        await conn.CloseAsync();
                    }
                });
            }
            catch (SqlException ex)
            {
                throw ex;
            }

            return (maxDepth, azureObjectId);
        }

        public async Task<List<(string Name, string Type)>> GetColumnDetailsAsync(string tableName)
        {
            ValidateTableName(tableName);

            var columnDetails = new List<(string Name, string Type)>();
            var retryPolicy = GetRetryPolicyAsync();
            try
            {
                var selectQuery = $@"
                    SELECT c.name, t.name AS type
                    FROM sys.columns AS c
                    JOIN sys.types AS t ON c.user_type_id = t.user_type_id
                    WHERE c.object_id = OBJECT_ID('[users].[{tableName}]')
                    ORDER BY c.name";

                await retryPolicy.ExecuteAsync(async () =>
                {
                    using (var conn = new SqlConnection(_sqlServerConnectionString))
                    {
                        await conn.OpenAsync();
                        using (var cmd = new SqlCommand(selectQuery, conn))
                        {
                            using (var reader = await cmd.ExecuteReaderAsync(CommandBehavior.CloseConnection))
                            {
                                int nameOrdinal = reader.GetOrdinal("name");
                                int typeOrdinal = reader.GetOrdinal("type");

                                while (reader.Read())
                                {
                                    var columnName = reader.IsDBNull(nameOrdinal) ? null : reader.GetString(nameOrdinal);
                                    var columnType = reader.IsDBNull(typeOrdinal) ? null : reader.GetString(typeOrdinal);
                                    columnDetails.Add((columnName, columnType));
                                }
                                reader.Close();
                            }
                        }
                        await conn.CloseAsync();
                    }
                });
            }
            catch (SqlException ex)
            {
                throw ex;
            }

            return columnDetails;
        }

        public async Task<bool> CheckIfMappingsTableExistsAsync(string tableName)
        {
            ValidateTableName(tableName);

            bool tableExists = false;
            var retryPolicy = GetRetryPolicyAsync();
            try
            {
                await retryPolicy.ExecuteAsync(async () =>
                {
                    using (var conn = new SqlConnection(_sqlServerConnectionString))
                    {
                        await conn.OpenAsync();
                        var selectQuery = "SELECT count(TABLE_NAME) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @TableName AND TABLE_SCHEMA = 'mappings'";
                        using (var cmd = new SqlCommand(selectQuery, conn))
                        {
                            cmd.Parameters.Add(new SqlParameter("@TableName", SqlDbType.NVarChar, 128) { Value = tableName });
                            var result = (int)cmd.ExecuteScalar();
                            tableExists = result > 0;
                        }
                        await conn.CloseAsync();
                    }
                });
            }
            catch (SqlException ex)
            {
                throw ex;
            }

            return tableExists;
        }

        public async Task<List<(string Code, string Description)>> GetAttributeMappingsAsync(string attribute, string tableName)
        {
            ValidateTableName(tableName);
            ValidateAttributeName(attribute);

            var attributeMappings = new List<(string Code, string Description)>();
            var retryPolicy = GetRetryPolicyAsync();

            try
            {
                var selectQuery = $"SELECT DISTINCT Code, Description FROM [mappings].[{tableName}] WHERE ColumnName = @Attribute";

                await retryPolicy.ExecuteAsync(async () =>
                {
                    using (var conn = new SqlConnection(_sqlServerConnectionString))
                    {
                        await conn.OpenAsync();
                        using (var cmd = new SqlCommand(selectQuery, conn))
                        {
                            cmd.Parameters.Add(new SqlParameter("@Attribute", SqlDbType.NVarChar, 128) { Value = attribute });
                            using (var reader = await cmd.ExecuteReaderAsync(CommandBehavior.CloseConnection))
                            {
                                int codeOrdinal = reader.GetOrdinal("Code");
                                int descriptionOrdinal = reader.GetOrdinal("Description");

                                while (reader.Read())
                                {

                                    var code = reader.IsDBNull(codeOrdinal) ? null : reader.GetString(codeOrdinal).Trim();
                                    var description = reader.IsDBNull(descriptionOrdinal) ? null : reader.GetString(descriptionOrdinal).Trim();
                                    attributeMappings.Add((code, description));
                                }
                                await reader.CloseAsync();
                            }
                        }
                        await conn.CloseAsync();
                    }
                });
            }
            catch (SqlException ex)
            {
                throw ex;
            }

            return attributeMappings;
        }

        /// <summary>
        /// Returns a capped page of attribute mappings, optionally filtered by a search on Code or Description.
        /// HasMore reports whether more rows matched than were returned, so the caller can offer server-side search.
        /// </summary>
        public async Task<(List<(string Code, string Description)> Mappings, bool HasMore)> GetAttributeMappingsPageAsync(string attribute, string tableName, string? search = null, int top = DefaultAttributeMappingsPageSize, AttributeSearchMode matchMode = AttributeSearchMode.Prefix)
        {
            ValidateTableName(tableName);
            ValidateAttributeName(attribute);

            if (top < 1)
            {
                top = DefaultAttributeMappingsPageSize;
            }
            else if (top > MaxAttributeMappingsPageSize)
            {
                top = MaxAttributeMappingsPageSize;
            }

            var attributeMappings = new List<(string Code, string Description)>();
            var hasMore = false;
            var retryPolicy = GetRetryPolicyAsync();
            var hasSearch = !string.IsNullOrWhiteSpace(search);

            var searchClause = hasSearch
                ? @" AND (Code LIKE @Search ESCAPE '\' OR Description LIKE @Search ESCAPE '\')"
                : string.Empty;
            var exactMatchOrder = hasSearch
                ? "CASE WHEN Code = @ExactSearch OR Description = @ExactSearch THEN 0 ELSE 1 END, "
                : string.Empty;
            var selectQuery = $"SELECT TOP (@Top) Code, Description FROM (SELECT DISTINCT Code, Description FROM [mappings].[{tableName}] WHERE ColumnName = @Attribute{searchClause}) AS Mappings ORDER BY {exactMatchOrder}Description, Code";

            await retryPolicy.ExecuteAsync(async () =>
            {
                attributeMappings.Clear();

                using (var conn = new SqlConnection(_sqlServerConnectionString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new SqlCommand(selectQuery, conn))
                    {
                        cmd.Parameters.Add(new SqlParameter("@Attribute", SqlDbType.NVarChar, 128) { Value = attribute });
                        // Request one extra row so we can tell whether more matches exist beyond this page.
                        cmd.Parameters.Add(new SqlParameter("@Top", SqlDbType.Int) { Value = top + 1 });

                        if (hasSearch)
                        {
                            var trimmedSearch = search.Trim();
                            var escaped = EscapeLikePattern(trimmedSearch);
                            var searchPattern = matchMode == AttributeSearchMode.Substring
                                ? "%" + escaped + "%"
                                : escaped + "%";
                            cmd.Parameters.Add(new SqlParameter("@Search", SqlDbType.NVarChar, 256) { Value = searchPattern });
                            cmd.Parameters.Add(new SqlParameter("@ExactSearch", SqlDbType.NVarChar, 256) { Value = trimmedSearch });
                        }

                        using (var reader = await cmd.ExecuteReaderAsync(CommandBehavior.CloseConnection))
                        {
                            int codeOrdinal = reader.GetOrdinal("Code");
                            int descriptionOrdinal = reader.GetOrdinal("Description");

                            while (await reader.ReadAsync())
                            {
                                var code = reader.IsDBNull(codeOrdinal) ? null : reader.GetString(codeOrdinal).Trim();
                                var description = reader.IsDBNull(descriptionOrdinal) ? null : reader.GetString(descriptionOrdinal).Trim();
                                attributeMappings.Add((code, description));
                            }
                            await reader.CloseAsync();
                        }
                    }
                    await conn.CloseAsync();
                }
            });

            if (attributeMappings.Count > top)
            {
                hasMore = true;
                attributeMappings.RemoveRange(top, attributeMappings.Count - top);
            }

            return (attributeMappings, hasMore);
        }

        /// <summary>
        /// Resolves an explicit set of codes to their descriptions. Used so values already saved on a sync job
        /// always display their real description, regardless of what the capped browse page happened to return.
        /// </summary>
        public async Task<List<(string Code, string Description)>> GetAttributeMappingsByCodesAsync(string attribute, string tableName, IEnumerable<string> codes)
        {
            ValidateTableName(tableName);
            ValidateAttributeName(attribute);

            var attributeMappings = new List<(string Code, string Description)>();

            if (codes == null)
            {
                return attributeMappings;
            }

            var distinctCodes = codes
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxAttributeMappingCodesPerRequest)
                .ToList();

            if (distinctCodes.Count == 0)
            {
                return attributeMappings;
            }

            var retryPolicy = GetRetryPolicyAsync();
            var parameterNames = distinctCodes.Select((_, index) => "@Code" + index).ToList();
            var selectQuery = $"SELECT DISTINCT Code, Description FROM [mappings].[{tableName}] WHERE ColumnName = @Attribute AND Code IN ({string.Join(", ", parameterNames)})";

            await retryPolicy.ExecuteAsync(async () =>
            {
                attributeMappings.Clear();

                using (var conn = new SqlConnection(_sqlServerConnectionString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new SqlCommand(selectQuery, conn))
                    {
                        cmd.Parameters.Add(new SqlParameter("@Attribute", SqlDbType.NVarChar, 128) { Value = attribute });

                        for (var index = 0; index < distinctCodes.Count; index++)
                        {
                            cmd.Parameters.Add(new SqlParameter(parameterNames[index], SqlDbType.NVarChar, 256) { Value = distinctCodes[index] });
                        }

                        using (var reader = await cmd.ExecuteReaderAsync(CommandBehavior.CloseConnection))
                        {
                            int codeOrdinal = reader.GetOrdinal("Code");
                            int descriptionOrdinal = reader.GetOrdinal("Description");

                            while (await reader.ReadAsync())
                            {
                                var code = reader.IsDBNull(codeOrdinal) ? null : reader.GetString(codeOrdinal).Trim();
                                var description = reader.IsDBNull(descriptionOrdinal) ? null : reader.GetString(descriptionOrdinal).Trim();
                                attributeMappings.Add((code, description));
                            }
                            await reader.CloseAsync();
                        }
                    }
                    await conn.CloseAsync();
                }
            });

            return attributeMappings;
        }

        // Escapes LIKE metacharacters so a user's search text is always treated as a literal prefix.
        private static string EscapeLikePattern(string value)
        {
            return value
                .Replace(@"\", @"\\")
                .Replace("%", @"\%")
                .Replace("_", @"\_")
                .Replace("[", @"\[");
        }

        public async Task<List<string>> GetAttributeValuesAsync(string attribute, bool hasMapping, string tableName)
        {
            ValidateTableName(tableName);
            ValidateAttributeName(attribute);

            var attributeValues = new List<string>();
            var retryPolicy = GetRetryPolicyAsync();

            var schema = hasMapping ? "mappings" : "users";
            var column = hasMapping ? "Description" : $"{attribute}";
            var whereClause = hasMapping ? " WHERE ColumnName = @Attribute" : "";

            try
            {
                var selectQuery = $"SELECT DISTINCT TOP(10) [{column}] FROM [{schema}].[{tableName}]" + whereClause;

                await retryPolicy.ExecuteAsync(async () =>
                {
                    using (var conn = new SqlConnection(_sqlServerConnectionString))
                    {
                        await conn.OpenAsync();
                        using (var cmd = new SqlCommand(selectQuery, conn))
                        {
                            if (hasMapping)
                            {
                                cmd.Parameters.Add(new SqlParameter("@Attribute", SqlDbType.NVarChar, 128) { Value = attribute });
                            }

                            using (var reader = await cmd.ExecuteReaderAsync(CommandBehavior.CloseConnection))
                            {
                                int valueOrdinal = reader.GetOrdinal($"{column}");

                                while (reader.Read())
                                {
                                    var value = reader.IsDBNull(valueOrdinal) ? null : reader.GetValue(valueOrdinal)?.ToString()?.Trim();

                                    if (value != null)
                                    {
                                        attributeValues.Add(value);
                                    }
                                }
                                await reader.CloseAsync();
                            }
                        }
                        await conn.CloseAsync();
                    }
                });
            }
            catch (SqlException ex)
            {
                throw ex;
            }

            return attributeValues;
        }
        /// <summary>
        /// Validates saved SQL filter fragments against the current users table and, only when needed, the matching agents
        /// table.
        /// </summary>
        /// <param name="sqlFilters">
        /// Filters keyed by caller-owned filter index. Null throws <see cref="ArgumentNullException"/>. An empty
        /// dictionary returns an empty error dictionary without opening a SQL connection. Each value is parsed as a WHERE
        /// fragment; multiple statements and unknown columns are reported as errors. A column written as
        /// <c>users.Name</c> must be in the users table and one written as <c>agents.Name</c> must be in the agents table,
        /// so <c>users.BlueprintId</c> is rejected even when the agents table has <c>BlueprintId</c>. A column without
        /// either prefix is looked up in the users table first, then in the agents table.
        /// </param>
        /// <param name="tableName">
        /// The same-run table name without schema. Null, empty, whitespace, or unsupported characters throw
        /// <see cref="ArgumentException"/>.
        /// </param>
        /// <returns>
        /// Errors keyed by the corresponding input index. An empty dictionary means every filter passed validation. If the
        /// users table is unavailable, every filter gets a users-table error. A filter that needs agents input gets
        /// "Required agents input is unavailable." when the agents table is missing or the caller cannot read it, and
        /// "Required agents input is missing columns: ..." naming <c>AgentObjectId</c> or <c>AgentManagerId</c> when
        /// the table lacks either column. While the agents table is missing or unreadable, only a column written with the
        /// <c>agents.</c> prefix, such as <c>agents.BlueprintId</c>, marks a filter as needing agents input; an unprefixed
        /// <c>BlueprintId</c> is reported as "Invalid column name detected: BlueprintId", like any other name that is not
        /// in the users table.
        /// </returns>
        public async Task<Dictionary<int, string>> ValidateFiltersAsync(Dictionary<int, string> sqlFilters, string tableName)
        {
            ValidateTableName(tableName);

            if (sqlFilters == null)
            {
                throw new ArgumentNullException(nameof(sqlFilters));
            }

            var errors = new Dictionary<int, string>();
            if (sqlFilters.Count == 0)
            {
                return errors;
            }

            var (userColumnNames, agentColumnNames) = await GetFilterColumnNamesAsync(tableName);
            if (userColumnNames.Count == 0)
            {
                foreach (var filter in sqlFilters)
                {
                    errors.Add(
                        filter.Key,
                        $"The required users table [users].[{tableName}] is unavailable.");
                }

                return errors;
            }

            var filtersRequiringAgents = new List<int>();

            foreach (var filter in sqlFilters)
            {
                var compilation = CheckFilter(filter.Value, tableName, userColumnNames, agentColumnNames);

                if (!compilation.IsValid)
                {
                    errors.Add(filter.Key, compilation.Error);
                    continue;
                }

                if (compilation.UsesAgents)
                {
                    filtersRequiringAgents.Add(filter.Key);
                }
            }

            if (filtersRequiringAgents.Count == 0)
            {
                return errors;
            }

            var agentsInputError = GetAgentsInputError(agentColumnNames);

            if (agentsInputError == null)
            {
                return errors;
            }

            foreach (var filterIndex in filtersRequiringAgents)
            {
                errors[filterIndex] = agentsInputError;
            }

            return errors;
        }

        private (bool, string) IsValidWhereClause(string whereStatement, List<string> validColumnNames)
        {
            var (isValid, error) = TryCollectColumns(whereStatement, out var columnCollector);

            if (!isValid)
            {
                return (false, error);
            }

            foreach (var column in columnCollector.ColumnNames)
            {
                if (!validColumnNames.Contains(column, StringComparer.OrdinalIgnoreCase))
                {
                    return (false, $"Invalid column name detected: {column}");
                }
            }

            return (true, string.Empty);
        }

        private static (bool, string) TryCollectColumns(string whereStatement, out ColumnCollector columnCollector)
        {
            columnCollector = new ColumnCollector();
            var parser = new TSql150Parser(false);
            using var reader = new StringReader(whereStatement);
            var fragment = parser.Parse(reader, out IList<ParseError> errors);

            if (errors.Count > 0)
            {
                return (false, errors[0].Message.ToString());
            }
            else
            {
                // Count the number of T-SQL statements, there should only be 1, the SELECT .. WHERE clause we set
                if (fragment is TSqlScript script && script.Batches != null)
                {
                    int statementCount = script.Batches
                    .SelectMany(batch => batch.Statements)
                    .Count();

                    if (statementCount > 1)
                        return (false, "Multiple SQL statements are not allowed.");
                }

                // Collect column names
                fragment.Accept(columnCollector);

                return (true, string.Empty);
            }
        }

        /// <summary>
        /// Collects the column references from parsed T-SQL.
        /// </summary>
        public class ColumnCollector : TSqlFragmentVisitor
        {
            /// <summary>
            /// Gets the distinct column names found in the parsed SQL fragment. Qualified references such as
            /// <c>agents.BlueprintId</c> contribute only <c>BlueprintId</c>.
            /// </summary>
            public HashSet<string> ColumnNames { get; } = new();

            /// <summary>
            /// Gets every column reference in the order it appears, with the table prefix it was written with. Brackets
            /// are removed and the original casing is kept. <c>[agents].[BlueprintId]</c> gives the qualifier
            /// <c>agents</c> and the name <c>BlueprintId</c>; <c>BlueprintId</c> gives a null qualifier; a longer
            /// reference such as <c>x.agents.BlueprintId</c> gives the qualifier <c>x.agents</c>.
            /// </summary>
            public List<(string? Qualifier, string Name)> ColumnReferences { get; } = new();

            /// <summary>
            /// Records the column name and table prefix from one column reference node when the node has an identifier.
            /// </summary>
            /// <param name="node">The parsed column reference. Null is not supplied by ScriptDom.</param>
            public override void Visit(ColumnReferenceExpression node)
            {
                if (node.MultiPartIdentifier != null)
                {
                    var identifiers = node.MultiPartIdentifier.Identifiers;
                    var column = identifiers.Last().Value;
                    var qualifier = identifiers.Count > 1
                        ? string.Join(".", identifiers.Take(identifiers.Count - 1).Select(identifier => identifier.Value))
                        : null;

                    ColumnNames.Add(column);
                    ColumnReferences.Add((qualifier, column));
                }
            }
        }


        private AsyncRetryPolicy GetRetryPolicyAsync()
        {
            return Policy.Handle<SqlException>()
                         .WaitAndRetryAsync(
                             5,
                             attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt))
                         );
        }

        private AsyncRetryPolicy GetMembershipEvaluationRetryPolicyAsync()
        {
            // Permission, missing-object, unknown-column or table-prefix, ambiguous-column, and type-mismatch errors fail
            // the same way on every attempt.
            return Policy.Handle<SqlException>(
                             exception => exception.Number != 229 &&
                                          exception.Number != 207 &&
                                          exception.Number != 208 &&
                                          exception.Number != 4104 &&
                                          exception.Number != 209 &&
                                          exception.Number != 206 &&
                                          exception.Number != 245 &&
                                          exception.Number != 529 &&
                                          exception.Number != 8116)
                         .WaitAndRetryAsync(
                             5,
                             attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt))
                         );
        }

        public async Task<int?> GetUserEmployeeIdAsync(string azureObjectId, string tableName)
        {
            ValidateTableName(tableName);
            int? employeeId = null;
            var retryPolicy = GetRetryPolicyAsync();

            try
            {
                var selectQuery = $"SELECT EmployeeId FROM [users].[{tableName}] WHERE AzureObjectId = @AzureObjectId";

                await retryPolicy.ExecuteAsync(async () =>
                {
                    using (var conn = new SqlConnection(_sqlServerConnectionString))
                    {
                        await conn.OpenAsync();
                        using (var cmd = new SqlCommand(selectQuery, conn))
                        {
                            cmd.Parameters.Add(new SqlParameter("@AzureObjectId", SqlDbType.NVarChar, 128) { Value = azureObjectId });

                            using (var reader = await cmd.ExecuteReaderAsync(CommandBehavior.CloseConnection))
                            {
                                if (await reader.ReadAsync())
                                {
                                    int idOrdinal = reader.GetOrdinal("EmployeeId");
                                    employeeId = reader.IsDBNull(idOrdinal) ? (int?)null : reader.GetInt32(idOrdinal);
                                }
                                await reader.CloseAsync();
                            }
                        }
                        await conn.CloseAsync();
                    }
                });
            }
            catch (SqlException ex)
            {
                throw ex;
            }

            return employeeId;
        }

        public async Task<string?> GetUserAzureObjectIdAsync(int employeeId, string tableName)
        {
            ValidateTableName(tableName);
            string? azureObjectId = null;
            var retryPolicy = GetRetryPolicyAsync();

            try
            {
                var selectQuery = $"SELECT AzureObjectId FROM [users].[{tableName}] WHERE EmployeeId = @EmployeeId";

                await retryPolicy.ExecuteAsync(async () =>
                {
                    using (var conn = new SqlConnection(_sqlServerConnectionString))
                    {
                        await conn.OpenAsync();
                        using (var cmd = new SqlCommand(selectQuery, conn))
                        {
                            cmd.Parameters.Add(new SqlParameter("@EmployeeId", SqlDbType.Int) { Value = employeeId });

                            using (var reader = await cmd.ExecuteReaderAsync(CommandBehavior.CloseConnection))
                            {
                                if (await reader.ReadAsync())
                                {
                                    int idOrdinal = reader.GetOrdinal("AzureObjectId");
                                    azureObjectId = reader.IsDBNull(idOrdinal) ? null : reader.GetString(idOrdinal);
                                }
                                await reader.CloseAsync();
                            }
                        }
                        await conn.CloseAsync();
                    }
                });
            }
            catch (SqlException ex)
            {
                throw ex;
            }

            return azureObjectId;
        }

        public async Task<Dictionary<string, string>?> GetUserAttributesAsync(string azureObjectId, string tableName)
        {
            ValidateTableName(tableName);
            Dictionary<string, string>? attributes = null;
            var retryPolicy = GetRetryPolicyAsync();

            try
            {
                var selectQuery = $"SELECT * FROM [users].[{tableName}] WHERE AzureObjectId = @AzureObjectId";

                await retryPolicy.ExecuteAsync(async () =>
                {
                    using (var conn = new SqlConnection(_sqlServerConnectionString))
                    {
                        await conn.OpenAsync();
                        using (var cmd = new SqlCommand(selectQuery, conn))
                        {
                            cmd.Parameters.AddWithValue("@AzureObjectId", azureObjectId);

                            using (var reader = await cmd.ExecuteReaderAsync(CommandBehavior.CloseConnection))
                            {
                                if (await reader.ReadAsync())
                                {
                                    attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                                    for (int i = 0; i < reader.FieldCount; i++)
                                    {
                                        var columnName = reader.GetName(i);
                                        var value = reader.IsDBNull(i) ? null : reader.GetValue(i)?.ToString()?.Trim();
                                        if (value != null)
                                        {
                                            attributes[columnName] = value;
                                        }
                                    }
                                }
                                await reader.CloseAsync();
                            }
                        }
                        await conn.CloseAsync();
                    }
                });
            }
            catch (SqlException ex)
            {
                throw ex;
            }

            return attributes;
        }

        public async Task<Dictionary<string, Dictionary<string, string>>> GetUserAttributesBatchAsync(IEnumerable<string> userIds, string tableName)
        {
            ValidateTableName(tableName);

            var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            if (userIds == null)
            {
                return result;
            }

            var distinctIds = userIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (distinctIds.Count == 0)
            {
                return result;
            }

            // SQL Server commands cap at 2100 parameters; chunk well under that for safety.
            const int chunkSize = 1000;
            var retryPolicy = GetRetryPolicyAsync();

            for (int offset = 0; offset < distinctIds.Count; offset += chunkSize)
            {
                var chunk = distinctIds.Skip(offset).Take(chunkSize).ToList();
                var parameterNames = new List<string>(chunk.Count);
                for (int i = 0; i < chunk.Count; i++)
                {
                    parameterNames.Add($"@p{i}");
                }

                var selectQuery = $"SELECT * FROM [users].[{tableName}] WHERE AzureObjectId IN ({string.Join(",", parameterNames)})";

                await retryPolicy.ExecuteAsync(async () =>
                {
                    using (var conn = new SqlConnection(_sqlServerConnectionString))
                    {
                        await conn.OpenAsync();
                        using (var cmd = new SqlCommand(selectQuery, conn))
                        {
                            for (int i = 0; i < chunk.Count; i++)
                            {
                                cmd.Parameters.AddWithValue(parameterNames[i], chunk[i]);
                            }

                            using (var reader = await cmd.ExecuteReaderAsync(CommandBehavior.CloseConnection))
                            {
                                int azureObjectIdOrdinal = -1;
                                for (int i = 0; i < reader.FieldCount; i++)
                                {
                                    if (string.Equals(reader.GetName(i), "AzureObjectId", StringComparison.OrdinalIgnoreCase))
                                    {
                                        azureObjectIdOrdinal = i;
                                        break;
                                    }
                                }

                                while (await reader.ReadAsync())
                                {
                                    if (azureObjectIdOrdinal < 0 || reader.IsDBNull(azureObjectIdOrdinal))
                                    {
                                        continue;
                                    }

                                    var userKey = reader.GetValue(azureObjectIdOrdinal)?.ToString()?.Trim();
                                    if (string.IsNullOrEmpty(userKey))
                                    {
                                        continue;
                                    }

                                    var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                                    for (int i = 0; i < reader.FieldCount; i++)
                                    {
                                        var columnName = reader.GetName(i);
                                        var value = reader.IsDBNull(i) ? null : reader.GetValue(i)?.ToString()?.Trim();
                                        if (value != null)
                                        {
                                            attributes[columnName] = value;
                                        }
                                    }

                                    result[userKey] = attributes;
                                }
                                await reader.CloseAsync();
                            }
                        }
                        await conn.CloseAsync();
                    }
                });
            }

            return result;
        }
    }
}
