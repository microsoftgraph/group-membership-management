// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Identity;
using Microsoft.Data.SqlClient;
using Polly;
using Polly.Retry;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using SqlMembershipObtainer.Entities;
using System.Data;

namespace Repositories.SqlMembershipRepository
{
    public class SqlMembershipRepository : ISqlMembershipRepository
    {

        private readonly string _sqlServerConnectionString = null;

        public SqlMembershipRepository(IKeyVaultSecret<ISqlMembershipRepository> sqlServerConnectionString)
        {
            _sqlServerConnectionString = sqlServerConnectionString?.Secret ?? throw new ArgumentNullException(nameof(sqlServerConnectionString));
        }

        public async Task<List<PersonEntity>> GetChildEntitiesAsync(string filter, int personnelNumber, string tableName, int depth)
        {
            var children = new List<PersonEntity>();
            var retryPolicy = GetRetryPolicy();

            try
            {
                var depthQuery = depth <= 0 ? "WHERE Depth > 0" : $" WHERE Depth <= {depth}";
                var filterQuery = string.IsNullOrWhiteSpace(filter) ? "" : $" AND ({filter})";
                var selectQuery = @$"
                        WITH emp AS (
                              SELECT *, 1 AS Depth
                              FROM {tableName}
                              WHERE EmployeeId = {personnelNumber}

                              UNION ALL

                              SELECT e.*, emp.Depth + 1
                              FROM {tableName} e INNER JOIN emp
                              ON e.ManagerId = emp.EmployeeId
                        )
                        SELECT *
                        FROM emp e {depthQuery} {filterQuery}";

                var credential = new DefaultAzureCredential();
                var token = await credential.GetTokenAsync(new Azure.Core.TokenRequestContext(new[] { "https://database.windows.net/.default" }));

                await retryPolicy.Execute(async () =>
                {
                    using (var conn = new SqlConnection(_sqlServerConnectionString))
                    {
                        conn.AccessToken = token.Token;
                        await conn.OpenAsync();
                        using (var cmd = new SqlCommand(selectQuery, conn))
                        {
                            using (var reader = await cmd.ExecuteReaderAsync(CommandBehavior.CloseConnection))
                            {
                                int id = reader.GetOrdinal("EmployeeId");
                                int azureObjectId = reader.GetOrdinal("AzureObjectId");

                                while (reader.Read())
                                {
                                    var response = new PersonEntity
                                    {
                                        RowKey = reader.IsDBNull(id) ? null : reader.GetInt32(id).ToString(),
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
            var retryPolicy = GetRetryPolicy();
            int maxDepth = 0;
            int employeeId = 0;

            try
            {
                var selectDepthQuery = @$"
                    WITH emp AS (
                            SELECT *, 1 AS Depth
                            FROM {tableName}
                            WHERE AzureObjectId = '{azureObjectId}'

                            UNION ALL

                            SELECT e.*, emp.Depth + 1
                            FROM {tableName} e INNER JOIN emp
                            ON e.ManagerId = emp.EmployeeId
                    )
                    SELECT MAX(Depth) AS MaxDepth
                    FROM emp e
                ";

                var selectIdQuery = $"SELECT EmployeeId FROM {tableName} WHERE AzureObjectId = '{azureObjectId}'";
                var credential = new DefaultAzureCredential();
                var token = credential.GetToken(new Azure.Core.TokenRequestContext(new[] { "https://database.windows.net/.default" }));

                await retryPolicy.Execute(async () =>
                {
                    using (var conn = new SqlConnection(_sqlServerConnectionString))
                    {
                        conn.AccessToken = token.Token;
                        await conn.OpenAsync();
                        using (var cmd = new SqlCommand(selectDepthQuery, conn))
                        {
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
            var filteredChildren = new List<PersonEntity>();
            var retryPolicy = GetRetryPolicy();
            try
            {
                var selectQuery = $"SELECT EmployeeId, AzureObjectId FROM {tableName} WHERE {query}";
                var credential = new DefaultAzureCredential();
                var token = credential.GetToken(new Azure.Core.TokenRequestContext(new[] { "https://database.windows.net/.default" }));
                await retryPolicy.Execute(async () =>
                {
                    using (var conn = new SqlConnection(_sqlServerConnectionString))
                    {
                        conn.AccessToken = token.Token;
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
                                        RowKey = reader.IsDBNull(personnelNumber) ? null : reader.GetInt32(personnelNumber).ToString(),
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

        public async Task<bool> CheckIfTableExistsAsync(string tableName)
        {
            bool tableExists = false;
            var retryPolicy = GetRetryPolicy();
            try
            {
                var credential = new DefaultAzureCredential();
                var token = credential.GetToken(new Azure.Core.TokenRequestContext(new[] { "https://database.windows.net/.default" }));

                await retryPolicy.Execute(async () =>
                {
                    using (var conn = new SqlConnection(_sqlServerConnectionString))
                    {
                        conn.AccessToken = token.Token;
                        await conn.OpenAsync();
                        var selectQuery = $"SELECT count(TABLE_NAME) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = '{tableName}'";
                        using (var cmd = new SqlCommand(selectQuery, conn))
                        {
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
            var HRColumns = new List<string>();
            var retryPolicy = GetRetryPolicy();
            try
            {
                var selectQuery = $"SELECT name FROM sys.columns WHERE object_id = OBJECT_ID('{tableName}') ORDER BY name";
                var credential = new DefaultAzureCredential();
                var token = credential.GetToken(new Azure.Core.TokenRequestContext(new[] { "https://database.windows.net/.default" }));
                await retryPolicy.Execute(async () =>
                {
                    using (var conn = new SqlConnection(_sqlServerConnectionString))
                    {
                        conn.AccessToken = token.Token;
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
            var retryPolicy = GetRetryPolicy();
            int maxDepth = 0;
            string azureObjectId = "";

            try
            {
                var selectDepthQuery = @$"
                    WITH emp AS (
                            SELECT EmployeeId, 1 AS Depth
                            FROM {tableName}
                            WHERE EmployeeId = {employeeId}

                            UNION ALL

                            SELECT e.EmployeeId, emp.Depth + 1
                            FROM {tableName} e INNER JOIN emp
                            ON e.ManagerId = emp.EmployeeId
                    )
                    SELECT MAX(Depth) AS MaxDepth
                    FROM emp e
                ";

                var selectIdQuery = $"SELECT AzureObjectId FROM {tableName} WHERE EmployeeId = {employeeId}";

                await retryPolicy.Execute(async () =>
                {
                    using (var conn = new SqlConnection(_sqlServerConnectionString))
                    {
                        await conn.OpenAsync();
                        using (var cmd = new SqlCommand(selectDepthQuery, conn))
                        {
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

        private RetryPolicy GetRetryPolicy()
        {
            return Policy.Handle<SqlException>()
                         .WaitAndRetry(
                             3,
                             _ => TimeSpan.FromMinutes(1)
                         );
        }
    }
}
