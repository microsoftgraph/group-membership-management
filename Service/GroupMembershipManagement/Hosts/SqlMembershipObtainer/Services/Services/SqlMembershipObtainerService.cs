// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.SqlMembershipObtainer;
using Microsoft.ApplicationInsights;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Models;
using Models.ServiceBus;
using Models.SyncJobHistory;
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
        private readonly ISqlMembershipRepository _sqlMembershipRepository;
        private readonly IBlobStorageRepository _blobStorageRepository;
        private readonly ISyncJobStatusService _syncJobStatusService;
        private readonly IDatabaseGroupsRepository _databaseGroupsRepository;
        private readonly IDatabaseChannelsRepository _databaseChannelsRepository;
        private readonly ILogger<SqlMembershipObtainerService> _logger;
        private readonly TelemetryClient _telemetryClient;
        private readonly bool _isSqlMembershipObtainerDryRunEnabled;
        private readonly IDataFactoryService _dataFactoryService;

        private enum Metric
        {
            MissingParentEntities
        }

        public SqlMembershipObtainerService(ISqlMembershipRepository sqlMembershipRepository,
                                    IBlobStorageRepository blobStorageRepository,
                                    ISyncJobStatusService syncJobStatusService,
                                    IDatabaseGroupsRepository databaseGroupsRepository,
                                    IDatabaseChannelsRepository databaseChannelsRepository,
                                    ILogger<SqlMembershipObtainerService> logger,
                                    TelemetryClient telemetryClient,
                                    IDryRunValue dryRun,
                                    IDataFactoryService dataFactoryService)
        {
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
            _syncJobStatusService = syncJobStatusService ?? throw new ArgumentNullException(nameof(syncJobStatusService));
            _databaseGroupsRepository = databaseGroupsRepository ?? throw new ArgumentNullException(nameof(databaseGroupsRepository));
            _databaseChannelsRepository = databaseChannelsRepository ?? throw new ArgumentNullException(nameof(databaseChannelsRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
            _sqlMembershipRepository = sqlMembershipRepository ?? throw new ArgumentNullException(nameof(sqlMembershipRepository));
            _isSqlMembershipObtainerDryRunEnabled = dryRun == null ? throw new ArgumentNullException(nameof(dryRun)) : dryRun.DryRunEnabled;
            _dataFactoryService = dataFactoryService ?? throw new ArgumentNullException(nameof(dataFactoryService));
        }
        public async Task<MembershipFileResult> GetChildEntitiesAsync(string filter, int personnelNumber, string tableName, int depth, SyncJob syncJob, Guid targetOfficeGroupId, int currentPart, bool exclusionary)
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

            _logger.RecordsRetrieved(children.Count, tableName);

            var profiles = children.Select(x => new GraphProfileInformation { PersonnelNumber = x.PersonnelNumber, Id = x.AzureObjectId }).ToList();

            var senderResponse = await UploadMembershipFileAsync(profiles, syncJob, targetOfficeGroupId, currentPart, exclusionary);

            return senderResponse;

        }

        public async Task<MembershipFileResult> FilterChildEntitiesAsync(string query, string tableName, SyncJob syncJob, Guid targetOfficeGroupId, int currentPart, bool exclusionary)
        {
            _logger.BeginningFilterEntities(tableName);

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

            _logger.RecordsRetrieved(filteredEntities.Count, tableName);

            var profiles = filteredEntities.Select(x => new GraphProfileInformation { PersonnelNumber = x.PersonnelNumber, Id = x.AzureObjectId }).Distinct().ToList();

            var senderResponse = await UploadMembershipFileAsync(profiles, syncJob, targetOfficeGroupId, currentPart, exclusionary);

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

        public async Task<MembershipFileResult> UploadMembershipFileAsync(List<GraphProfileInformation> profiles, SyncJob syncJob, Guid groupId, int currentPart, bool exclusionary)
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
            _logger.FileUploadDuration(end - start);
            _logger.MembersSentForGroup(groupMemberToBeSent.SourceMembers.Count, groupId);
            _logger.ServiceCompleted(DateTime.UtcNow);

            return new MembershipFileResult {
                Status = status,
                FilePath = fileName
            };
        }

        public async Task<string> GetTableNameAsync(Guid? runId, Guid? targetOfficeGroupId)
        {
            var adfRunId = await GetADFRunIdAsync(runId);
            var tableName = adfRunId.Replace("-", "");
            var tableExists = await CheckIfTableExists(tableName, runId, targetOfficeGroupId);

            if (tableExists)
                _logger.TableNameExists(tableName);
            else
                _logger.TableNameDoesNotExist(tableName);

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
            _logger.UpdatingJobStatusToIdle(job.Id);

            var now = DateTime.UtcNow;
            var history = new SyncJobHistory
            {
                SyncJobId = job.Id,
                RunId = job.RunId ?? Guid.Empty,
                Status = SyncStatus.Idle.ToString(),
                UpdatedByFunction = "SqlMembershipObtainer",
                EndTime = now,
                UpdatedAt = now
            };

            job.Status = SyncStatus.Idle.ToString();

            await _syncJobStatusService.UpdateJobStatusAsync(job, SyncStatus.Idle, history, functionName: "SqlMembershipObtainer");
        }

        private async Task SetSyncJobStatusAsync(SyncJob syncJob, SyncStatus status)
        {
            _logger.SettingJobStatus(syncJob.Id, status.ToString());

            var now = DateTime.UtcNow;
            var history = new SyncJobHistory
            {
                SyncJobId = syncJob.Id,
                RunId = syncJob.RunId ?? Guid.Empty,
                Status = status.ToString(),
                UpdatedByFunction = "SqlMembershipObtainer",
                EndTime = status != SyncStatus.InProgress ? now : null,
                UpdatedAt = now
            };

            syncJob.Status = status.ToString();

            await _syncJobStatusService.UpdateJobStatusAsync(syncJob, status, history, functionName: "SqlMembershipObtainer");
        }

        private async Task<string> GetADFRunIdAsync(Guid? runId)
        {
            return await _dataFactoryService.GetMostRecentSucceededRunIdAsync(runId);
        }
    }
}