// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Core.Pipeline;
using Azure.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.Graph.Models;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Polly;
using Polly.Retry;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using SqlMembershipObtainer.Entities;
using System;
using System.Collections.Concurrent;
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
        public async Task<Dictionary<int, string>> ValidateFiltersAsync(Dictionary<int, string> sqlFilters, string tableName)
        {
            ValidateTableName(tableName);

            var exceptionsList = new ConcurrentDictionary<int, string>();
            var validColumnNames = await GetColumnNamesAsync(tableName);

            var tasks = sqlFilters.Select(sqlFilter =>
            {
                var whereStatement = $@"SELECT * FROM [users].[{tableName}] WHERE {sqlFilter.Value}";

                var (isValid, errorMessage) = IsValidWhereClause(whereStatement, validColumnNames);

                if (!isValid)
                {
                    exceptionsList.TryAdd(sqlFilter.Key, errorMessage);
                }

                return Task.CompletedTask;
            });

            await Task.WhenAll(tasks);

            return exceptionsList.ToDictionary();
        }

        private (bool, string) IsValidWhereClause(string whereStatement, List<string> validColumnNames)
        {
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
                var columnCollector = new ColumnCollector();
                fragment.Accept(columnCollector);

                foreach (var column in columnCollector.ColumnNames)
                {
                    if (!validColumnNames.Contains(column, StringComparer.OrdinalIgnoreCase))
                    {
                        return (false, $"Invalid column name detected: {column}");
                    }
                }

                return (true, string.Empty);
            }
        }

        // Helper class to collect column names
        public class ColumnCollector : TSqlFragmentVisitor
        {
            public HashSet<string> ColumnNames { get; } = new();

            public override void Visit(ColumnReferenceExpression node)
            {
                if (node.MultiPartIdentifier != null)
                {
                    var column = node.MultiPartIdentifier.Identifiers.Last().Value;
                    ColumnNames.Add(column);
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
    }
}
