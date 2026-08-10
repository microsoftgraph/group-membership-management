// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Core;
using Azure.Identity;
using Hosts.SqlDataChecker;
using Microsoft.ApplicationInsights;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Models;
using Polly;
using Polly.Retry;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Entities;
using System.Data;
using System.Text.RegularExpressions;

namespace Services
{
    public class SqlDataCheckerValidatorService
    {
        private const string DefaultSqlMembershipSourceName = "SqlMembership";

        private readonly ILogger<SqlDataCheckerValidatorService> _logger;
        private readonly TelemetryClient _telemetryClient;
        private readonly string _sqlServerConnectionString;
        private readonly IDataFactoryRepository _dataFactoryRepository;
        private readonly IDatabaseSqlMembershipSourcesRepository _sqlMembershipSourcesRepository;
        private static readonly Regex _safeIdentifierRegex = new(@"^[a-zA-Z0-9_]+$", RegexOptions.Compiled);

        public SqlDataCheckerValidatorService(ILogger<SqlDataCheckerValidatorService> logger,
                                    TelemetryClient telemetryClient,
                                    IKeyVaultSecret<SqlDataCheckerValidatorService> sqlServerConnectionString,
                                    IDataFactoryRepository dataFactoryRepository,
                                    IDatabaseSqlMembershipSourcesRepository sqlMembershipSourcesRepository)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
            _sqlServerConnectionString = sqlServerConnectionString?.Secret ?? throw new ArgumentNullException(nameof(sqlServerConnectionString));
            _dataFactoryRepository = dataFactoryRepository ?? throw new ArgumentNullException(nameof(dataFactoryRepository));
            _sqlMembershipSourcesRepository = sqlMembershipSourcesRepository ?? throw new ArgumentNullException(nameof(sqlMembershipSourcesRepository));
        }

        public async Task<TableName> GetTableNamesAsync()
        {
            var adfRunId = await GetADFRunIdsAsync();
            var latestTableName = adfRunId.latest.Replace("-", "");
            var previousTableName = adfRunId.previous.Replace("-", "");
            var latestTableExists = CheckIfTableExists(latestTableName);
            var previousTableExists = CheckIfTableExists(previousTableName);

            if (latestTableExists)
                _logger.TableExists(latestTableName);
            else
                _logger.TableDoesNotExist(latestTableName);

            if (previousTableExists)
                _logger.TableExists(previousTableName);
            else
                _logger.TableDoesNotExist(previousTableName);

            return new TableName
            {
                Latest = latestTableExists ? latestTableName : "",
                Previous = previousTableExists ? previousTableName : ""
            };
        }

        private static void ValidateIdentifier(string name, string paramName)
        {
            if (string.IsNullOrWhiteSpace(name) || !_safeIdentifierRegex.IsMatch(name))
                throw new ArgumentException($"Invalid SQL identifier for {paramName}: '{name}'");
        }

        private async Task<(string latest, string previous)> GetADFRunIdsAsync()
        {
            var pipelineRunIds = await _dataFactoryRepository.GetCurrentRunAndPreviousSucceededRunIdsAsync();

            if (string.IsNullOrWhiteSpace(pipelineRunIds.current) || string.IsNullOrWhiteSpace(pipelineRunIds.previousSucceeded))
            {
                _logger.MissingPipelineRuns();
                throw new ArgumentException("Missing SqlDataChecker pipeline run(s)");
            }

            return (pipelineRunIds.current, pipelineRunIds.previousSucceeded);
        }

        public int GetRows(string tableName)
        {
            ValidateIdentifier(tableName, nameof(tableName));
            int numberOfRows = 0;
            var retryPolicy = GetRetryPolicy();
            try
            {
                var token = GetAccessToken();

                retryPolicy.Execute(() =>
                {
                    using (var conn = new SqlConnection(_sqlServerConnectionString))
                    {
                        conn.AccessToken = token.Token;
                        conn.Open();
                        var selectQuery = $"SELECT COUNT(*) FROM [users].[{tableName}]";
                        using (var cmd = new SqlCommand(selectQuery, conn))
                        {
                            numberOfRows = (int)cmd.ExecuteScalar();
                        }
                        conn.Close();
                    }
                });
            }

            catch (SqlException ex)
            {
                var exceptionMessage = "Sql Exception in SqlDataChecker - GetRows()";
                var scSQLException = new SqlDataCheckerSQLException(exceptionMessage, ex);

                _telemetryClient.TrackException(scSQLException, new Dictionary<string, string>()
                    {
                        {"Exception", ex.Message }
                    });

                throw scSQLException;
            }
            return numberOfRows;
        }

        private bool CheckIfTableExists(string tableName)
        {
            ValidateIdentifier(tableName, nameof(tableName));
            bool tableExists = false;
            var retryPolicy = GetRetryPolicy();
            try
            {
                var token = GetAccessToken();

                retryPolicy.Execute(() =>
                {
                    using (var conn = new SqlConnection(_sqlServerConnectionString))
                    {
                        conn.AccessToken = token.Token;
                        conn.Open();
                        var selectQuery = "SELECT COUNT(TABLE_NAME) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @tableName AND TABLE_SCHEMA = 'users'";
                        using (var cmd = new SqlCommand(selectQuery, conn))
                        {
                            cmd.Parameters.AddWithValue("@tableName", tableName);
                            var result = (int)cmd.ExecuteScalar();
                            tableExists = result > 0;
                        }
                        conn.Close();
                    }
                });
            }

            catch (SqlException ex)
            {
                var exceptionMessage = "Sql Exception in SqlDataChecker - CheckIfTableExists()";
                var scSQLException = new SqlDataCheckerSQLException(exceptionMessage, ex);

                _telemetryClient.TrackException(scSQLException, new Dictionary<string, string>()
                    {
                        {"Exception", ex.Message }
                    });

                throw scSQLException;
            }
            return tableExists;
        }

        private RetryPolicy GetRetryPolicy()
        {
            return Policy.Handle<SqlException>().WaitAndRetry(3, _ => TimeSpan.FromMinutes(1));
        }

        public List<string> GetColumns(string tableName)
        {
            ValidateIdentifier(tableName, nameof(tableName));
            var columns = new List<string>();
            var retryPolicy = GetRetryPolicy();
            try
            {
                var token = GetAccessToken();

                retryPolicy.Execute(() =>
                {
                    using (var conn = new SqlConnection(_sqlServerConnectionString))
                    {
                        conn.AccessToken = token.Token;
                        conn.Open();
                        var selectQuery = "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @tableName AND TABLE_SCHEMA = 'users'";
                        using (var cmd = new SqlCommand(selectQuery, conn))
                        {
                            cmd.Parameters.AddWithValue("@tableName", tableName);
                            using (var reader = cmd.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    columns.Add(reader["COLUMN_NAME"]?.ToString() ?? string.Empty);
                                }
                            }
                        }
                        conn.Close();
                    }
                });
            }

            catch (SqlException ex)
            {
                var exceptionMessage = "Sql Exception in SqlDataChecker - GetColumnsAsync()";
                var scSQLException = new SqlDataCheckerSQLException(exceptionMessage, ex);

                _telemetryClient.TrackException(scSQLException, new Dictionary<string, string>()
                    {
                        {"Exception", ex.Message }
                    });

                throw scSQLException;
            }

            return columns;
        }

        public Dictionary<string, int> ValidateColumns(List<string> columns, string tableName)
        {
            ValidateIdentifier(tableName, nameof(tableName));
            foreach (var col in columns)
                ValidateIdentifier(col, "column");

            var nullDict = new Dictionary<string, int>();
            string query = "";

            for (int i = 0; i < columns.Count; i++)
            {
                query += $"SUM(CASE WHEN {columns[i]} IS NULL THEN 1 ELSE 0 END) {columns[i]}";
                query += (i != columns.Count - 1) ? ", " : "";
            }
            var selectQuery = $@"SELECT {query} FROM [users].[{tableName}]";
            var retryPolicy = GetRetryPolicy();
            try
            {
                var token = GetAccessToken();

                retryPolicy.Execute(() =>
                {
                    using (var conn = new SqlConnection(_sqlServerConnectionString))
                    {
                        conn.AccessToken = token.Token;
                        conn.Open();
                        using (var cmd = new SqlCommand(selectQuery, conn))
                        {
                            cmd.CommandTimeout = 0;
                            using (var reader = cmd.ExecuteReader(CommandBehavior.CloseConnection))
                            {
                                while (reader.Read())
                                {
                                    for (int i = 0; i < columns.Count; i++)
                                    {
                                        var value = reader.GetInt32(reader.GetOrdinal(columns[i]));
                                        nullDict.Add(columns[i], value);
                                    }
                                }
                                reader.Close();
                            }
                        }
                        conn.Close();
                    }
                });
            }

            catch (SqlException ex)
            {
                var exceptionMessage = "Sql Exception in SqlDataChecker - ValidateColumnAsync()";
                var scSQLException = new SqlDataCheckerSQLException(exceptionMessage, ex);

                _telemetryClient.TrackException(scSQLException, new Dictionary<string, string>()
                    {
                        {"Exception", ex.Message }
                    });

                throw scSQLException;
            }

            return nullDict;
        }

        public async Task<Dictionary<string, double>> GetColumnThresholdsAsync()
        {
            var thresholds = new Dictionary<string, double>();
            // The default source can legitimately be absent or have no attributes configured yet. Reading it
            // by name returns null in both cases (GetDefaultSourceAttributesAsync would throw when the row is
            // missing), and returning an empty set lets every column fall back to the default threshold
            // instead of failing the pipeline.
            var attributes = await _sqlMembershipSourcesRepository.GetSourceAttributesAsync(DefaultSqlMembershipSourceName);
            if (attributes == null)
            {
                _logger.NoStoredColumnThresholdsFound();
                return thresholds;
            }

            foreach (var attr in attributes)
            {
                if (attr == null || !attr.NullThreshold.HasValue || string.IsNullOrWhiteSpace(attr.Name))
                    continue;

                var clampedThreshold = Math.Max(0.0, Math.Min(1.0, attr.NullThreshold.Value));
                if (clampedThreshold != attr.NullThreshold.Value)
                {
                    _logger.ThresholdClamped(attr.Name, attr.NullThreshold.Value, clampedThreshold);
                }

                var columnName = attr.HasMapping ? attr.Name + "_Code" : attr.Name;
                thresholds[columnName] = clampedThreshold;
            }

            return thresholds;
        }

        private AccessToken GetAccessToken()
        {
            DefaultAzureCredential credential = new(DefaultAzureCredential.DefaultEnvironmentVariableName);

            return credential.GetToken(new TokenRequestContext(new[] { "https://database.windows.net/.default" }), CancellationToken.None);
        }
    }
}