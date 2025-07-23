// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.ApplicationInsights;
using Microsoft.Data.SqlClient;
using Models;
using Models.ServiceBus;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using SqlMembershipObtainer.Entities;
using System.Data;
using System.Text.Json;

namespace Services
{
    public class SqlMembershipObtainerService : ISqlMembershipObtainerService
    {
        private readonly ISqlMembershipRepository _sqlMembershipRepository = null;
        private readonly IBlobStorageRepository _blobStorageRepository = null;
        private readonly IDatabaseSyncJobsRepository _syncJobRepository = null;
        private readonly IDatabaseGroupsRepository _databaseGroupsRepository = null;
        private readonly IDatabaseChannelsRepository _databaseChannelsRepository = null;
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly TelemetryClient _telemetryClient = null;
        private readonly bool _isSqlMembershipObtainerDryRunEnabled;
        private readonly IDataFactoryService _dataFactoryService = null;

        private enum Metric
        {
            MissingParentEntities
        }

        public SqlMembershipObtainerService(ISqlMembershipRepository sqlMembershipRepository,
                                    IBlobStorageRepository blobStorageRepository,
                                    IDatabaseSyncJobsRepository syncJobRepository,
                                    IDatabaseGroupsRepository databaseGroupsRepository,
                                    IDatabaseChannelsRepository databaseChannelsRepository,
                                    ILoggingRepository loggingRepository,
                                    TelemetryClient telemetryClient,
                                    IDryRunValue dryRun,
                                    IDataFactoryService dataFactoryService)
        {
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
            _databaseGroupsRepository = databaseGroupsRepository ?? throw new ArgumentNullException(nameof(databaseGroupsRepository));
            _databaseChannelsRepository = databaseChannelsRepository ?? throw new ArgumentNullException(nameof(databaseChannelsRepository));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
            _sqlMembershipRepository = sqlMembershipRepository ?? throw new ArgumentNullException(nameof(sqlMembershipRepository));
            _isSqlMembershipObtainerDryRunEnabled = dryRun == null ? throw new ArgumentNullException(nameof(dryRun)) : dryRun.DryRunEnabled;
            _dataFactoryService = dataFactoryService ?? throw new ArgumentNullException(nameof(dataFactoryService));
        }
        public async Task<GroupMembershipSenderResponse> GetChildEntitiesAsync(string filter, int personnelNumber, string tableName, int depth, SyncJob syncJob, Guid targetOfficeGroupId, int currentPart, bool exclusionary, string adaptiveCardTemplateDirectory)
        {
            var children = new List<PersonEntity>();

            try
            {
                children = await _sqlMembershipRepository.GetChildEntitiesAsync(filter, personnelNumber, tableName, depth);
            }
            catch (SqlException ex)
            {
                var exceptionMessage = $"Sql Exception in SqlMembershipObtainer with RunId: {syncJob.RunId}, TargetOfficeGroupId: {targetOfficeGroupId}";
                var ocSQLException = new SqlMembershipObtainerSQLException(exceptionMessage, ex, targetOfficeGroupId, syncJob.RunId);

                _telemetryClient.TrackException(ocSQLException, new Dictionary<string, string>()
                {
                    {"TargetOfficeGroupId", targetOfficeGroupId.ToString() },
                    {"RunId", syncJob.RunId.ToString() ?? string.Empty },
                    {"Exception", ex.Message }
                });

                throw ocSQLException;
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Retrieved a total of {children.Count} records from {tableName} table", RunId = syncJob.RunId });

            var profiles = children.Select(x => new GraphProfileInformation { PersonnelNumber = x.PersonnelNumber, Id = x.AzureObjectId }).ToList();

            var senderResponse = await SendGroupMembershipAsync(profiles, syncJob, targetOfficeGroupId, currentPart, exclusionary, adaptiveCardTemplateDirectory);

            return senderResponse;

        }

        public async Task<GroupMembershipSenderResponse> FilterChildEntitiesAsync(string query, string tableName, SyncJob syncJob, Guid targetOfficeGroupId, int currentPart, bool exclusionary, string adaptiveCardTemplateDirectory)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Beginning to filter entities from {tableName} table", RunId = syncJob.RunId });

            var filteredEntities = new List<PersonEntity>();

            try
            {
                filteredEntities = await _sqlMembershipRepository.FilterChildEntitiesAsync(query, tableName);
            }
            catch (SqlException ex)
            {
                var exceptionMessage = $"Sql Exception in SqlMembershipObtainer with RunId: {syncJob.RunId}, TargetOfficeGroupId: {targetOfficeGroupId}";
                var ocSQLException = new SqlMembershipObtainerSQLException(exceptionMessage, ex, targetOfficeGroupId, syncJob.RunId);

                _telemetryClient.TrackException(ocSQLException, new Dictionary<string, string>()
                {
                    {"TargetOfficeGroupId", targetOfficeGroupId.ToString() },
                    {"RunId", syncJob.RunId.ToString() ?? string.Empty },
                    {"Exception", ex.Message }
                });

                throw ocSQLException;
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Retrieved a total of {filteredEntities.Count} records from {tableName} table", RunId = syncJob.RunId });

            var profiles = filteredEntities.Select(x => new GraphProfileInformation { PersonnelNumber = x.PersonnelNumber, Id = x.AzureObjectId }).Distinct().ToList();

            var senderResponse = await SendGroupMembershipAsync(profiles, syncJob, targetOfficeGroupId, currentPart, exclusionary, adaptiveCardTemplateDirectory);

            return senderResponse;
        }

        public async Task<Guid> GetGroupIdAsync(SyncJob syncJob)
        {
            if (syncJob.MembershipType == MembershipTypes.TeamsChannelMembership.ToString())
            {
                var channel = syncJob.Channel ?? await _databaseChannelsRepository.GetChannelUsingSyncJobIdAsync(syncJob.Id);
                return channel.GroupId;
            }
            else if (syncJob.MembershipType == MembershipTypes.GroupMembership.ToString())
            {
                var group = syncJob.Group ?? await _databaseGroupsRepository.GetGroupUsingSyncJobIdAsync(syncJob.Id);
                return group.GroupId;
            }
            return Guid.Empty;
        }

        public async Task<GroupMembershipSenderResponse> SendGroupMembershipAsync(List<GraphProfileInformation> profiles, SyncJob syncJob, Guid groupId, int currentPart, bool exclusionary, string adaptiveCardTemplateDirectory = "")
        {
            var groupMemberToBeSent = new GroupMembership
            {
                SourceMembers = profiles.Select(x => new AzureADUser { ObjectId = Guid.Parse(x.Id) }).ToList(),
                Destination = new AzureADGroup { ObjectId = groupId },
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
            fileName = $"/{groupId}/{timeStamp}_{runId}_SqlMembership_{currentPart}.json";
            var start = DateTime.UtcNow;
            await _blobStorageRepository.UploadFileAsync(fileName, JsonSerializer.Serialize(groupMemberToBeSent));
            var end = DateTime.UtcNow;
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Time to upload file: {end - start}",
                RunId = syncJob.RunId
            });
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Sent {groupMemberToBeSent.SourceMembers.Count} members for group {groupId}", RunId = syncJob.RunId });
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"SqlMembershipObtainer service completed at: {DateTime.UtcNow}", RunId = syncJob.RunId });

            return new GroupMembershipSenderResponse {
                Status = status,
                FilePath = fileName
            };
        }

        public async Task<string> GetTableNameAsync(Guid? runId, Guid? targetOfficeGroupId)
        {
            var adfRunId = await GetADFRunIdAsync(runId);
            var tableName = adfRunId.Replace("-", "");
            var tableExists = await CheckIfTableExists(tableName, runId, targetOfficeGroupId);
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = tableExists ? $"{tableName} exists" : $"{tableName} does not exist",
                RunId = runId
            });
            return tableExists ? tableName : "";
        }

        private async Task<bool> CheckIfTableExists(string tableName, Guid? runId, Guid? targetOfficeGroupId)
        {
            bool tableExists = false;

            try
            {
                tableExists = await _sqlMembershipRepository.CheckIfTableExistsAsync(tableName);
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
                Message = $"Updating status of job {job.Id} to Idle."
            });

            await _syncJobRepository.UpdateSyncJobStatusAsync(new[] { job }, SyncStatus.Idle);
        }

        private async Task SetSyncJobStatusAsync(SyncJob syncJob, SyncStatus status)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Setting status of job {syncJob.Id} to {status}.",
                RunId = syncJob.RunId
            });

            await _syncJobRepository.UpdateSyncJobStatusAsync(new[] { syncJob }, status);
        }

        private async Task<string> GetADFRunIdAsync(Guid? runId)
        {
            return await _dataFactoryService.GetMostRecentSucceededRunIdAsync(runId);
        }
    }
}
