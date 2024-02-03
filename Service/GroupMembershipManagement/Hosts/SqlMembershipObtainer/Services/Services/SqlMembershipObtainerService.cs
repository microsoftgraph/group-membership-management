// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Services.Contracts;
using Models.ServiceBus;
using Repositories.Contracts.InjectConfig;
using Microsoft.ApplicationInsights;
using System.Threading;
using Newtonsoft.Json;
using Polly;
using Azure.Identity;
using Polly.Retry;
using Microsoft.Data.SqlClient;
using System.Data;
using Models;
using SqlMembershipObtainer.Common.DependencyInjection;
using SqlMembershipObtainer.Entities;

namespace Services
{
    public class SqlMembershipObtainerService : ISqlMembershipObtainerService
    {
        private readonly IBlobStorageRepository _blobStorageRepository = null;
        private readonly IDatabaseSyncJobsRepository _syncJobRepository = null;
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly TelemetryClient _telemetryClient = null;
        private readonly ISqlMembershipObtainerServiceSecret _shouldStopSyncIfSourceNotPresentInGraph = null;
        private readonly ISqlMembershipObtainerServiceSecret _sqlServerConnectionString = null;
        private readonly bool _isSqlMembershipObtainerDryRunEnabled;
        private readonly IDataFactoryService _dataFactoryService = null;

        private enum Metric
        {
            MissingParentEntities
        }

        public SqlMembershipObtainerService(IBlobStorageRepository blobStorageRepository,
                                    IDatabaseSyncJobsRepository syncJobRepository,
                                    ILoggingRepository loggingRepository,
                                    TelemetryClient telemetryClient,
                                    ISqlMembershipObtainerServiceSecret shouldStopSyncIfSourceNotPresentInGraph,
                                    ISqlMembershipObtainerServiceSecret sqlServerConnectionString,
                                    IDryRunValue dryRun,
                                    IDataFactoryService dataFactoryService)
        {
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
            _shouldStopSyncIfSourceNotPresentInGraph = shouldStopSyncIfSourceNotPresentInGraph ?? throw new ArgumentNullException(nameof(shouldStopSyncIfSourceNotPresentInGraph));
            _sqlServerConnectionString = sqlServerConnectionString ?? throw new ArgumentNullException(nameof(sqlServerConnectionString));
            _isSqlMembershipObtainerDryRunEnabled = dryRun == null ? throw new ArgumentNullException(nameof(dryRun)) : dryRun.DryRunEnabled;
            _dataFactoryService = dataFactoryService ?? throw new ArgumentNullException(nameof(dataFactoryService));
        }

        public async Task<List<PersonEntity>> GetChildEntitiesAsync(string filter, int personnelNumber, string tableName, int depth, Guid? runId, Guid? targetOfficeGroupId)
        {
            var children = new List<PersonEntity>();
            var retryPolicy = GetRetryPolicy();

            try
            {
                var depthQuery = depth <= 0 ? "WHERE Depth > 0" : $" WHERE Depth <= {depth}";
                var filterQuery = string.IsNullOrWhiteSpace(filter) ? "" : $" AND {filter}";
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
                var token = credential.GetToken(new Azure.Core.TokenRequestContext(new[] { "https://database.windows.net/.default" }));

                retryPolicy.Execute(() =>
                {
                    using (var conn = new SqlConnection(_sqlServerConnectionString.SqlServerConnectionString))
                    {
                        conn.AccessToken = token.Token;
                        conn.Open();
                        using (var cmd = new SqlCommand(selectQuery, conn))
                        {
                            using (var reader = cmd.ExecuteReader(CommandBehavior.CloseConnection))
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
                                reader.Close();
                            }
                        }
                        conn.Close();
                    }
                });
            }
            catch (SqlException ex)
            {
                var exceptionMessage = $"Sql Exception in SqlMembershipObtainer with RunId: {runId}, TargetOfficeGroupId: {targetOfficeGroupId}";
                var ocSQLException = new SqlMembershipObtainerSQLException(exceptionMessage, ex, targetOfficeGroupId, runId);

                _telemetryClient.TrackException(ocSQLException, new Dictionary<string, string>()
                {
                    {"TargetOfficeGroupId", targetOfficeGroupId.ToString() },
                    {"RunId", runId.ToString() },
                    {"Exception", ex.Message }
                });

                throw ocSQLException;
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Retrieved a total of {children.Count} child entities from {tableName} table", RunId = runId });

            return children;
        }

        public async Task<List<PersonEntity>> FilterChildEntitiesAsync(string query, string tableName, Guid? runId, Guid? targetOfficeGroupId)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Beginning to filter entities from {tableName} table", RunId = runId });

            var filteredChildren = new List<PersonEntity>();
            var retryPolicy = GetRetryPolicy();
            try
            {
                var selectQuery = $"SELECT EmployeeId, AzureObjectId FROM {tableName} WHERE {query}";
                var credential = new DefaultAzureCredential();
                var token = credential.GetToken(new Azure.Core.TokenRequestContext(new[] { "https://database.windows.net/.default" }));
                retryPolicy.Execute(() =>
                {
                    using (var conn = new SqlConnection(_sqlServerConnectionString.SqlServerConnectionString))
                    {
                        conn.AccessToken = token.Token;
                        conn.Open();
                        using (var cmd = new SqlCommand(selectQuery, conn))
                        {
                            using (var reader = cmd.ExecuteReader(CommandBehavior.CloseConnection))
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
                                reader.Close();
                            }
                        }
                        conn.Close();
                    }
                });
            }
            catch (SqlException ex)
            {
                var exceptionMessage = $"Sql Exception in SqlMembershipObtainer with RunId: {runId}, TargetOfficeGroupId: {targetOfficeGroupId}";
                var ocSQLException = new SqlMembershipObtainerSQLException(exceptionMessage, ex, targetOfficeGroupId, runId);

                _telemetryClient.TrackException(ocSQLException, new Dictionary<string, string>()
                {
                    {"TargetOfficeGroupId", targetOfficeGroupId.ToString() },
                    {"RunId", runId.ToString() },
                    {"Exception", ex.Message }
                });

                throw ocSQLException;
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Retrieved a total of {filteredChildren.Count} filtered entities from {tableName} table", RunId = runId });

            return filteredChildren;
        }

        public async Task<(SyncStatus Status, string FilePath)> SendGroupMembershipAsync(List<GraphProfileInformation> profiles, SyncJob syncJob, int currentPart, bool exclusionary, string adaptiveCardTemplateDirectory = "")
        {
            var groupMemberToBeSent = new GroupMembership
            {
                SourceMembers = profiles.Select(x => new AzureADUser { ObjectId = Guid.Parse(x.Id) }).ToList(),
                Destination = new AzureADGroup { ObjectId = syncJob.TargetOfficeGroupId },
                SyncJobId = syncJob.Id,
                RunId = syncJob.RunId.Value,
                Exclusionary = exclusionary,
                MembershipObtainerDryRunEnabled = _isSqlMembershipObtainerDryRunEnabled,
                Query = syncJob.Query
            };
            string fileName = null;
            var status = SyncStatus.InProgress;
            var runId = syncJob.RunId.GetValueOrDefault();
            var timeStamp = DateTime.UtcNow.ToString("MMddyyyy-HHmm");
            fileName = $"/{syncJob.TargetOfficeGroupId}/{timeStamp}_{runId}_SqlMembership_{currentPart}.json";
            var start = DateTime.UtcNow;
            await _blobStorageRepository.UploadFileAsync(fileName, JsonConvert.SerializeObject(groupMemberToBeSent));
            var end = DateTime.UtcNow;
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Time to upload file: {end - start}",
                RunId = syncJob.RunId
            });
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Sent {groupMemberToBeSent.SourceMembers.Count} members for group {syncJob.TargetOfficeGroupId}", RunId = syncJob.RunId });
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"SqlMembershipObtainer service completed at: {DateTime.UtcNow}", RunId = syncJob.RunId });

            return (status, fileName);
        }

        public async Task<string> GetTableNameAsync(Guid? runId, Guid? targetOfficeGroupId)
        {
            var adfRunId = await GetADFRunIdAsync(runId);
            var tableName = string.Concat("tbl", adfRunId.Replace("-", ""));
            var tableExists = CheckIfTableExists(tableName, runId, targetOfficeGroupId);
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = tableExists ? $"{tableName} exists" : $"{tableName} does not exist",
                RunId = runId
            });
            return tableExists ? tableName : "";
        }

        private bool CheckIfTableExists(string tableName, Guid? runId, Guid? targetOfficeGroupId)
        {
            bool tableExists = false;
            var retryPolicy = GetRetryPolicy();
            try
            {
                var credential = new DefaultAzureCredential();
                var token = credential.GetToken(new Azure.Core.TokenRequestContext(new[] { "https://database.windows.net/.default" }));

                retryPolicy.Execute(() =>
                {
                    using (var conn = new SqlConnection(_sqlServerConnectionString.SqlServerConnectionString))
                    {
                        conn.AccessToken = token.Token;
                        conn.Open();
                        var selectQuery = $"SELECT count(TABLE_NAME) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = '{tableName}'";
                        using (var cmd = new SqlCommand(selectQuery, conn))
                        {
                            var result = (int)cmd.ExecuteScalar();
                            tableExists = result > 0;
                        }
                        conn.Close();
                    }
                });
            }

            catch (SqlException ex)
            {
                var exceptionMessage = $"Sql Exception in SqlMembershipObtainer with RunId: {runId}, TargetOfficeGroupId: {targetOfficeGroupId}";
                var ocSQLException = new SqlMembershipObtainerSQLException(exceptionMessage, ex, targetOfficeGroupId, runId);

                _telemetryClient.TrackException(ocSQLException, new Dictionary<string, string>()
                    {
                        {"TargetOfficeGroupId", targetOfficeGroupId.ToString() },
                        {"RunId", runId.ToString() },
                        {"Exception", ex.Message }
                    });

                throw ocSQLException;
            }

            return tableExists;
        }

        public async Task UpdateSyncJobStatusToIdleAsync(SyncJob job)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                RunId = job.RunId,
                Message = $"Updating job status for target group {job.TargetOfficeGroupId} to Idle."
            });

            await _syncJobRepository.UpdateSyncJobStatusAsync(new[] { job }, SyncStatus.Idle);
        }

        private async Task SetSyncJobStatusAsync(SyncJob syncJob, SyncStatus status)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Setting sync job to {status} for the group {syncJob.TargetOfficeGroupId}.",
                RunId = syncJob.RunId
            });

            await _syncJobRepository.UpdateSyncJobStatusAsync(new[] { syncJob }, status);
        }

        private RetryPolicy GetRetryPolicy()
        {
            return Policy.Handle<SqlException>()
                         .WaitAndRetry(
                             3,
                             _ => TimeSpan.FromMinutes(1)
                         );
        }

        private async Task<string> GetADFRunIdAsync(Guid? runId)
        {
            return await _dataFactoryService.GetMostRecentSucceededRunIdAsync(runId);
        }
    }
}
