// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.SqlMembershipObtainer;
using Microsoft.ApplicationInsights;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Models;
using Models.Helpers;
using Models.ServiceBus;
using Models.SyncJobHistory;
using Repositories.Contracts;
using Repositories.Contracts.DestinationResolution;
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
        private readonly IDestinationResolver _destinationResolver;
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
                                    IDestinationResolver destinationResolver,
                                    ILogger<SqlMembershipObtainerService> logger,
                                    TelemetryClient telemetryClient,
                                    IDryRunValue dryRun,
                                    IDataFactoryService dataFactoryService)
        {
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
            _syncJobStatusService = syncJobStatusService ?? throw new ArgumentNullException(nameof(syncJobStatusService));
            _destinationResolver = destinationResolver ?? throw new ArgumentNullException(nameof(destinationResolver));
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

            ThrowIfNullIdentityColumns(children, tableName, syncJob, targetOfficeGroupId, isFilterPath: false);

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

            ThrowIfNullIdentityColumns(filteredEntities, tableName, syncJob, targetOfficeGroupId, isFilterPath: true);

            var profiles = filteredEntities.Select(x => new GraphProfileInformation { PersonnelNumber = x.PersonnelNumber, Id = x.AzureObjectId }).Distinct().ToList();

            var senderResponse = await UploadMembershipFileAsync(profiles, syncJob, targetOfficeGroupId, currentPart, exclusionary);

            return senderResponse;
        }

        public async Task<SqlMembershipEvaluationResult> EvaluateMembershipAsync(
            SqlMembershipEvaluationRequest request,
            SyncJob syncJob,
            Guid targetOfficeGroupId,
            int currentPart)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (syncJob == null)
            {
                throw new ArgumentNullException(nameof(syncJob));
            }

            try
            {
                var result =
                    await _sqlMembershipRepository.EvaluateMembershipAsync(request);

                _logger.RecordsRetrieved(
                    result.UserObjectIds.Count + result.AgentObjectIds.Count,
                    request.TableName);
                _logger.MembershipEvaluationCompleted(
                    syncJob.Id,
                    syncJob.RunId,
                    currentPart,
                    request.AdfRunId,
                    request.IdentityTypes.ToString(),
                    FormatReferencedIdentityTypes(result.ReferencedIdentityTypes),
                    result.SelectedHumanCount,
                    result.UserObjectIds.Count,
                    result.AgentObjectIds.Count);

                if (result.SkippedAgentRowCount > 0)
                {
                    _logger.InvalidAgentObjectIdsSkipped(
                        result.SkippedAgentRowCount,
                        request.TableName,
                        syncJob.Id,
                        syncJob.RunId,
                        currentPart,
                        request.AdfRunId);
                }

                return result;
            }
            catch (SqlException exception)
            {
                TrackMembershipEvaluationFailure(
                    exception,
                    request,
                    syncJob,
                    targetOfficeGroupId,
                    currentPart);

                var exceptionMessage =
                    $"Sql Exception in SqlMembershipObtainer with RunId: {syncJob.RunId}, TargetOfficeGroupId: {targetOfficeGroupId}";
                var sqlMembershipException =
                    new SqlMembershipObtainerSQLException(
                        exceptionMessage,
                        exception,
                        targetOfficeGroupId,
                        syncJob.RunId);

                throw sqlMembershipException;
            }
            catch (Exception exception)
            {
                TrackMembershipEvaluationFailure(
                    exception,
                    request,
                    syncJob,
                    targetOfficeGroupId,
                    currentPart);
                throw;
            }
        }

        public async Task<MembershipFileResult> UploadMembershipEvaluationAsync(
            SqlMembershipEvaluationResult evaluation,
            SyncJob syncJob,
            Guid groupId,
            int currentPart,
            bool exclusionary)
        {
            if (evaluation == null)
            {
                throw new ArgumentNullException(nameof(evaluation));
            }

            var sourceMembers = new List<AzureADUser>(
                evaluation.UserObjectIds.Count + evaluation.AgentObjectIds.Count);

            // Each list is already distinct, so only an agent ID that is also a user ID can repeat.
            var agentObjectIdsNotSelectedAsUsers = evaluation.AgentObjectIds.Count == 0
                ? null
                : new HashSet<Guid>(evaluation.AgentObjectIds);

            foreach (var userObjectId in evaluation.UserObjectIds)
            {
                sourceMembers.Add(new AzureADUser { ObjectId = userObjectId });
                agentObjectIdsNotSelectedAsUsers?.Remove(userObjectId);
            }

            if (agentObjectIdsNotSelectedAsUsers != null)
            {
                foreach (var agentObjectId in evaluation.AgentObjectIds)
                {
                    if (agentObjectIdsNotSelectedAsUsers.Contains(agentObjectId))
                    {
                        sourceMembers.Add(new AzureADUser { ObjectId = agentObjectId });
                    }
                }
            }

            return await UploadSourceMembersAsync(
                sourceMembers,
                syncJob,
                groupId,
                currentPart,
                exclusionary);
        }

        private void TrackMembershipEvaluationFailure(
            Exception exception,
            SqlMembershipEvaluationRequest request,
            SyncJob syncJob,
            Guid targetOfficeGroupId,
            int currentPart)
        {
            var identityTypes = request.IdentityTypes.ToString();

            _logger.MembershipEvaluationFailed(
                syncJob.Id,
                syncJob.RunId,
                currentPart,
                request.AdfRunId,
                identityTypes,
                exception);
            _telemetryClient.TrackException(
                exception,
                new Dictionary<string, string>
                {
                    ["JobId"] = syncJob.Id.ToString("D"),
                    ["RunId"] =
                        syncJob.RunId?.ToString("D") ?? string.Empty,
                    ["SyncRunId"] =
                        syncJob.RunId?.ToString("D") ?? string.Empty,
                    ["CurrentPart"] = currentPart.ToString(),
                    ["AdfRunId"] =
                        request.AdfRunId?.ToString("D") ?? string.Empty,
                    ["IdentityTypes"] = identityTypes,
                    ["TargetOfficeGroupId"] =
                        targetOfficeGroupId.ToString("D"),
                });
        }

        private static string FormatReferencedIdentityTypes(
            IReadOnlyCollection<IdentityType> referencedIdentityTypes)
        {
            if (referencedIdentityTypes.Count == 0)
            {
                return "none";
            }

            var orderedIdentityTypes = new[]
            {
                IdentityType.Users,
                IdentityType.Agents,
            };

            return string.Join(
                ",",
                orderedIdentityTypes
                    .Where(referencedIdentityTypes.Contains)
                    .Select(identityType => identityType.ToString().ToLowerInvariant()));
        }

        private void ThrowIfNullIdentityColumns(
            List<PersonEntity> entities, string tableName, SyncJob syncJob, Guid targetOfficeGroupId, bool isFilterPath)
        {
            ValidateColumn(entities, e => string.IsNullOrWhiteSpace(e.AzureObjectId), "AzureObjectId", tableName, syncJob, targetOfficeGroupId);
            if (isFilterPath)
                ValidateColumn(entities, e => string.IsNullOrWhiteSpace(e.PersonnelNumber), "EmployeeId", tableName, syncJob, targetOfficeGroupId);
        }

        private void ValidateColumn(
            List<PersonEntity> entities, Func<PersonEntity, bool> isNull, string columnName,
            string tableName, SyncJob syncJob, Guid targetOfficeGroupId)
        {
            var affectedCount = entities.Count(isNull);
            if (affectedCount == 0) return;

            _logger.NullHrColumnValuesDetected(affectedCount, tableName, columnName, syncJob.RunId, targetOfficeGroupId);

            var message = $"SqlMembershipObtainer failed: {affectedCount} HR row(s) in table {tableName} have a null/empty {columnName} for RunId: {syncJob.RunId}, TargetOfficeGroupId: {targetOfficeGroupId}. This indicates bad HR source data, not a code defect.";
            throw new InvalidOperationException(message);
        }

        public async Task<Guid> GetGroupIdAsync(SyncJob syncJob)
        {
            var destination = await _destinationResolver.ResolveAsync(syncJob);
            return destination switch
            {
                ResolvedGroupDestination groupDestination => groupDestination.ObjectId,
                ResolvedTeamsChannelDestination channelDestination => channelDestination.TeamObjectId,
                _ => Guid.Empty
            };
        }

        public async Task<MembershipFileResult> UploadMembershipFileAsync(List<GraphProfileInformation> profiles, SyncJob syncJob, Guid groupId, int currentPart, bool exclusionary)
        {
            var sourceMembers = profiles.Select(x => new AzureADUser { ObjectId = Guid.Parse(x.Id) }).ToList();
            return await UploadSourceMembersAsync(sourceMembers, syncJob, groupId, currentPart, exclusionary);
        }

        private async Task<MembershipFileResult> UploadSourceMembersAsync(List<AzureADUser> sourceMembers, SyncJob syncJob, Guid groupId, int currentPart, bool exclusionary)
        {
            sourceMembers.Sort(CanonicalMemberComparer<AzureADUser>.Instance);
            var groupMemberToBeSent = new GroupMembership
            {
                SourceMembers = sourceMembers,
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

        public async Task<TableNameResult> GetTableNameAsync(Guid? runId, Guid? targetOfficeGroupId)
        {
            var adfRunId = await GetADFRunIdAsync(runId);
            var tableName = adfRunId.Replace("-", "");
            var tableExists = await CheckIfTableExists(tableName, runId, targetOfficeGroupId);

            if (tableExists)
                _logger.TableNameExists(tableName);
            else
                _logger.TableNameDoesNotExist(tableName);

            return new TableNameResult
            {
                TableName = tableExists ? tableName : "",
                AdfRunId = ParseAdfRunIdGuid(adfRunId),
            };
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

        private Guid? ParseAdfRunIdGuid(string adfRunIdString)
        {
            if (Guid.TryParse(adfRunIdString, out var adfRunGuid))
            {
                return adfRunGuid;
            }

            _logger.LogWarning("AdfRunId is not a valid GUID: {AdfRunId}", adfRunIdString);
            return null;
        }
    }
}