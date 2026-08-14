// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.AutoApprover;
using Microsoft.Extensions.Logging;
using Models;
using Models.ServiceBus;
using Models.SyncJobChange;
using Repositories.Contracts;
using Repositories.Contracts.Helpers;
using Services.AutoApprover.Contracts;
using Services.AutoApprover.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Services.AutoApprover
{
    public class AutoApproverService : IAutoApproverService
    {
        private readonly IDatabaseSyncJobsRepository _syncJobRepository;
        private readonly IDatabaseSettingsRepository _databaseSettingsRepository;
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly ISyncJobChangeRepository _syncJobChangeRepository;
        private readonly ISqlMembershipRepository _sqlMembershipRepository;
        private readonly IDataFactoryRepository _dataFactoryRepository;
        private readonly ILogger<AutoApproverService> _logger;

        public AutoApproverService(
            IDatabaseSyncJobsRepository syncJobRepository,
            IDatabaseSettingsRepository databaseSettingsRepository,
            IGraphGroupRepository graphGroupRepository,
            ISyncJobChangeRepository syncJobChangeRepository,
            ISqlMembershipRepository sqlMembershipRepository,
            IDataFactoryRepository dataFactoryRepository,
            ILogger<AutoApproverService> logger)
        {
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
            _databaseSettingsRepository = databaseSettingsRepository ?? throw new ArgumentNullException(nameof(databaseSettingsRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _syncJobChangeRepository = syncJobChangeRepository ?? throw new ArgumentNullException(nameof(syncJobChangeRepository));
            _sqlMembershipRepository = sqlMembershipRepository ?? throw new ArgumentNullException(nameof(sqlMembershipRepository));
            _dataFactoryRepository = dataFactoryRepository ?? throw new ArgumentNullException(nameof(dataFactoryRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task ProcessAutoApprovalAsync(AutoApprovalQueueMessage message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));

            var syncJob = await _syncJobRepository.GetSyncJobAsync(message.SyncJobId);
            if (syncJob == null)
            {
                _logger.SyncJobNotFound(message.SyncJobId);
                return;
            }

            // Every log emitted below carries the job, destination group and requestor, so a single
            // `where customDimensions.Id == '...'` returns the whole decision trail.
            using var logScope = _logger.BeginSyncJobScope(syncJob, new Dictionary<string, object>
            {
                ["Requestor"] = Convert.ToString(syncJob.Requestor)
            });

            if (syncJob.Status == SyncStatus.PendingConfiguration.ToString())
            {
                _logger.SyncJobPendingConfiguration(message.SyncJobId);
                return;
            }

            if (!string.Equals(syncJob.Status, SyncStatus.PendingAutoApproval.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                _logger.SyncJobStatusNotPendingReview(message.SyncJobId, syncJob.Status);
                return;
            }

            if (!Guid.TryParse(message.RequestorObjectId, out var requestorObjectId))
            {
                _logger.InvalidRequestorObjectId(message.SyncJobId);
                return;
            }

            var isGroupBasedAutoApprovalEnabled = await IsAutoApprovalForGroupBasedSyncsEnabledAsync();
            var isOrgLeaderAutoApprovalEnabled = await IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabledAsync();
            var isPerPartAutoApprovalEnabled = await IsPerPartAutoApprovalEnabledAsync();
            _logger.AutoApprovalModesResolved(isGroupBasedAutoApprovalEnabled, isOrgLeaderAutoApprovalEnabled, isPerPartAutoApprovalEnabled);

            var shouldAutoApprove = await ShouldAutoApproveJobAsync(syncJob.Query, message.RequestorObjectId, isGroupBasedAutoApprovalEnabled, isOrgLeaderAutoApprovalEnabled, isPerPartAutoApprovalEnabled);

            if (!shouldAutoApprove)
            {
                _logger.AutoApprovalNotGranted(message.SyncJobId);
                syncJob.Status = SyncStatus.PendingReview.ToString();
                await _syncJobRepository.UpdateSyncJobsAsync(new[] { syncJob });
                return;
            }

            if (syncJob.StartDate < DateTime.UtcNow)
            {
                syncJob.StartDate = DateTime.UtcNow.AddHours(24);
            }

            syncJob.Status = SyncStatus.Idle.ToString();

            var changedOnBehalfOfDisplayName = message.ChangedOnBehalfOfDisplayName;
            var changedOnBehalfOfObjectId = message.ChangedOnBehalfOfObjectId;

            var changeRecord = new SyncJobChange
            {
                SyncJobId = message.SyncJobId,
                ChangeTime = DateTime.UtcNow,
                ChangedByObjectId = requestorObjectId,
                ChangedByDisplayName = message.RequestorDisplayName,
                ChangeSource = SyncJobChangeSource.WebApp,
                ChangeReason = SyncJobChangeReason.OnboardingAutoApproved.ToString(),
                ChangeDetails = SyncJobSerializationHelper.SerializeSyncJob(syncJob),
                BusinessJustification = message.BusinessJustification,
                ChangedOnBehalfOfDisplayName = changedOnBehalfOfDisplayName != null && changedOnBehalfOfDisplayName != message.RequestorDisplayName ? changedOnBehalfOfDisplayName : null,
                ChangedOnBehalfOfObjectId = !string.IsNullOrEmpty(changedOnBehalfOfObjectId) && changedOnBehalfOfObjectId != message.RequestorObjectId ? new Guid(changedOnBehalfOfObjectId) : (Guid?)null
            };

            try
            {
                await _syncJobRepository.UpdateSyncJobsAsync(new[] { syncJob });
                await _syncJobChangeRepository.Save(changeRecord);
            }
            catch (Exception ex)
            {
                // The status update and the audit record are not written atomically. If the status was
                // already flipped to Idle but persisting the audit record failed, revert the job back to
                // PendingAutoApproval so the Service Bus retry re-runs the full approval path (and writes the
                // missing audit record) instead of short-circuiting on the "not PendingAutoApproval" guard.
                _logger.AutoApprovalPersistenceFailed(message.SyncJobId, ex);

                try
                {
                    syncJob.Status = SyncStatus.PendingAutoApproval.ToString();
                    await _syncJobRepository.UpdateSyncJobsAsync(new[] { syncJob });
                }
                catch (Exception revertEx)
                {
                    _logger.AutoApprovalRevertFailed(message.SyncJobId, revertEx);
                }

                throw;
            }

            _logger.SyncJobAutoApproved(message.SyncJobId);
        }

        private async Task<bool> ShouldAutoApproveJobAsync(string query, string userIdentity, bool isGroupBasedAutoApprovalEnabled, bool isOrgLeaderAutoApprovalEnabled, bool isPerPartAutoApprovalEnabled)
        {
            try
            {
                if (isPerPartAutoApprovalEnabled)
                {
                    var perPartApproval = await ShouldAutoApprovePerPartAsync(query, userIdentity);

                    if (!perPartApproval && (isGroupBasedAutoApprovalEnabled || isOrgLeaderAutoApprovalEnabled))
                        _logger.PerPartRuleTookPrecedence(isGroupBasedAutoApprovalEnabled, isOrgLeaderAutoApprovalEnabled);

                    return perPartApproval;
                }

                if (isGroupBasedAutoApprovalEnabled)
                {
                    var groupMembershipApproval = await ShouldAutoApproveGroupMembershipJobAsync(query);
                    if (groupMembershipApproval)
                        return true;
                }

                if (isOrgLeaderAutoApprovalEnabled)
                {
                    var sqlMembershipApproval = await ShouldAutoApproveSqlMembershipJobAsync(query, userIdentity);
                    if (sqlMembershipApproval)
                        return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.AutoApprovalCheckError(ex);
                return false;
            }
        }

        private async Task<bool> ShouldAutoApproveGroupMembershipJobAsync(string query)
        {
            try
            {
                if (!JsonParser.IsGroupMembershipOnlyQuery(query))
                    return false;

                var groupIds = JsonParser.GetGroupMembershipSourceIds(query);
                if (groupIds == null || groupIds.Count == 0)
                    return false;

                var groups = await _graphGroupRepository.GetGroupsAsync(groupIds);

                var hiddenGroupFound = groups.Any(group => string.Equals(group.Visibility, "HiddenMembership", StringComparison.OrdinalIgnoreCase));
                if (hiddenGroupFound)
                {
                    return false;
                }

                _logger.GroupVisibilityApprovalGranted(groupIds.Count);

                return true;
            }
            catch (Exception ex)
            {
                _logger.GroupMembershipApprovalCheckError(ex);
                return false;
            }
        }

        private async Task<bool> ShouldAutoApproveSqlMembershipJobAsync(string query, string userIdentity)
        {
            try
            {
                var userDetails = await GetUserOnPremisesImmutableIdAsync(userIdentity);
                if (string.IsNullOrEmpty(userDetails))
                    return false;

                if (!int.TryParse(userDetails, out var userImmutableId))
                    return false;

                if (!JsonParser.IsSingleSqlMembershipQueryWithManagerId(query, userImmutableId))
                    return false;

                _logger.SqlMembershipApprovalGranted();

                return true;
            }
            catch (Exception ex)
            {
                _logger.SqlMembershipApprovalCheckError(ex);
                return false;
            }
        }

        private async Task<string> GetUserOnPremisesImmutableIdAsync(string userIdentity)
        {
            try
            {
                var user = await _graphGroupRepository.GetUserWithOnPremisesImmutableIdAsync(userIdentity, null);
                return user?.OnPremisesImmutableId;
            }
            catch (Exception ex)
            {
                _logger.UserImmutableIdRetrievalError(ex);
                return null;
            }
        }

        private async Task<bool> IsAutoApprovalForGroupBasedSyncsEnabledAsync()
        {
            try
            {
                var setting = await _databaseSettingsRepository.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled);
                return setting != null ? bool.Parse(setting.SettingValue) : false;
            }
            catch (Exception ex)
            {
                _logger.AutoApprovalSettingRetrievalError(ex);
                return false;
            }
        }

        private async Task<bool> IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabledAsync()
        {
            try
            {
                var setting = await _databaseSettingsRepository.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled);
                return setting != null ? bool.Parse(setting.SettingValue) : false;
            }
            catch (Exception ex)
            {
                _logger.OrgLeaderSettingRetrievalError(ex);
                return false;
            }
        }

        private async Task<bool> IsPerPartAutoApprovalEnabledAsync()
        {
            try
            {
                var setting = await _databaseSettingsRepository.GetSettingByKeyAsync(SettingKey.IsPerPartAutoApprovalEnabled);
                return setting != null && string.Equals(setting.SettingValue, "true", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                _logger.SettingRetrievalError(nameof(SettingKey.IsPerPartAutoApprovalEnabled), ex);
                return false;
            }
        }

        // Per-Part Rule: evaluates each source part independently (Group parts by Graph visibility/owner,
        // SQL parts by manager-self identity) and approves the whole submission only when every part passes.
        // Any failure resolves to reject the part (fail-closed).
        private async Task<bool> ShouldAutoApprovePerPartAsync(string query, string userIdentity)
        {
            try
            {
                if (!JsonParser.TryParseParts(query, out var parts, out var parseFailure) || parts.Count == 0)
                {
                    if (parseFailure == QueryParseFailure.None)
                        parseFailure = QueryParseFailure.NoPartsInQuery;

                    _logger.PerPartQueryNotParsable(parseFailure.ToString());
                    return false;
                }

                int? employeeId = null;
                if (parts.Any(p => p.Type == SourcePart.SqlMembershipType))
                {
                    var tableName = await ResolveUsersTableNameAsync();
                    if (!string.IsNullOrEmpty(tableName))
                        employeeId = await ResolveEmployeeIdAsync(userIdentity, tableName);

                    _logger.SqlPartEvaluationContext(
                        string.IsNullOrEmpty(tableName) ? "(unresolved)" : tableName,
                        employeeId != null ? "resolved" : "not found");
                }

                var mix = new PartApprovalMix();

                for (var i = 0; i < parts.Count; i++)
                {
                    var part = parts[i];
                    PartRejection? rejection;

                    switch (part.Type)
                    {
                        case SourcePart.GroupMembershipType:
                            rejection = await EvaluateGroupPartAsync(part, userIdentity, i, mix);
                            break;
                        case SourcePart.SqlMembershipType:
                            rejection = EvaluateSqlPart(part, employeeId, i, mix);
                            break;
                        default:
                            rejection = Reject(part, i, SourcePartRejectionReason.Unknown_Type,
                                $"'{part.Type}' is not a source type the Per-Part Rule can evaluate");
                            break;
                    }

                    if (rejection != null)
                    {
                        _logger.PerPartRuleDeclined(i, part.Type, part.SourceDescriptor, rejection.Value.Reason.ToString(),
                            rejection.Value.Details, i + 1, parts.Count, mix.SqlApproved, mix.GroupPublic, mix.GroupOwner);
                        return false;
                    }
                }

                _logger.PerPartRuleApproved(mix.SqlApproved, mix.GroupPublic, mix.GroupOwner, parts.Count);
                return true;
            }
            catch (Exception ex)
            {
                _logger.PerPartRuleCheckError(ex);
                return false;
            }
        }

        private async Task<string> ResolveUsersTableNameAsync()
        {
            try
            {
                var runId = await _dataFactoryRepository.GetMostRecentSucceededRunIdAsync();
                if (string.IsNullOrEmpty(runId))
                {
                    _logger.RunIdRetrievalError(new InvalidOperationException("GetMostRecentSucceededRunIdAsync returned an empty run id."));
                    return null;
                }

                return runId.Replace("-", string.Empty);
            }
            catch (Exception ex)
            {
                _logger.RunIdRetrievalError(ex);
                return null;
            }
        }

        // Dedicated fail-closed boundary: any repository failure logs the specific EventId and yields
        // null, so it never surfaces as the generic PerPartRuleCheckError on the outer catch.
        private async Task<int?> ResolveEmployeeIdAsync(string azureObjectId, string tableName)
        {
            try
            {
                return await _sqlMembershipRepository.GetUserEmployeeIdAsync(azureObjectId, tableName);
            }
            catch (Exception ex)
            {
                _logger.EmployeeIdRetrievalError(ex);
                return null;
            }
        }

        // Returns null when the part is approved, otherwise the reason it was rejected and a
        // human-readable detail (group visibility, employee id mismatch, ...) explaining that reason.
        private async Task<PartRejection?> EvaluateGroupPartAsync(SourcePart part, string userIdentity, int partIndex, PartApprovalMix mix)
        {
            if (!Guid.TryParse(part.SourceGroupId, out var groupId))
                return Reject(part, partIndex, SourcePartRejectionReason.Group_NotFound, "source id is not a valid group id");

            AzureADGroup group;
            try
            {
                var groups = await _graphGroupRepository.GetGroupsAsync(new List<Guid> { groupId });
                group = groups?.FirstOrDefault(g => g.ObjectId == groupId);
            }
            catch (Exception ex)
            {
                _logger.GroupVisibilityError(partIndex, part.SourceGroupId, ex);
                return new PartRejection(SourcePartRejectionReason.Group_LookupFailed, "visibility lookup threw an exception");
            }

            if (group == null)
                return Reject(part, partIndex, SourcePartRejectionReason.Group_NotFound, "group was not found in the directory");

            var visibility = string.IsNullOrWhiteSpace(group.Visibility) ? "unknown" : group.Visibility;

            if (string.Equals(group.Visibility, "Public", StringComparison.OrdinalIgnoreCase))
            {
                _logger.GroupPartApprovedByPublicVisibility(partIndex, part.SourceGroupId);
                mix.GroupPublic++;
                return null;
            }

            List<AzureADUser> owners;
            try
            {
                owners = await _graphGroupRepository.GetGroupOwnersAsync(groupId);
            }
            catch (Exception ex)
            {
                _logger.GroupOwnersError(partIndex, part.SourceGroupId, ex);
                return new PartRejection(SourcePartRejectionReason.Owner_LookupFailed, $"visibility {visibility}, owner lookup threw an exception");
            }

            if (owners != null && owners.Any(o => string.Equals(o.ObjectId.ToString(), userIdentity, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.GroupPartApprovedByOwner(partIndex, part.SourceGroupId, visibility);
                mix.GroupOwner++;
                return null;
            }

            return Reject(part, partIndex, SourcePartRejectionReason.Owner_CheckFailed,
                $"visibility {visibility} requires ownership, and the requestor is not an owner");
        }

        // Returns null when the part is approved, otherwise the reason it was rejected and a detail.
        private PartRejection? EvaluateSqlPart(SourcePart part, int? employeeId, int partIndex, PartApprovalMix mix)
        {
            // Checked before the EmployeeId lookup: a filter-only SQL source names no manager, so no
            // requestor identity could ever approve it. Reporting a missing EmployeeId here would send
            // an operator after HR data that would not change the outcome.
            if (part.ManagerId == null)
                return Reject(part, partIndex, SourcePartRejectionReason.Sql_NoManagerId,
                    "the SQL source is filter-only and names no manager, so there is no manager-self relationship to verify; the submission needs manual review");

            if (employeeId == null)
                return Reject(part, partIndex, SourcePartRejectionReason.EmployeeId_Missing,
                    "the requestor has no EmployeeId in the users table");

            // Requestor is the manager referenced in the query — approve. Only manager.id is compared;
            // filter, manager.depth and exclusionary are deliberately not considered. Ignoring them is
            // the safe direction: SqlMembershipRepository.GetChildEntitiesAsync seeds its recursive CTE
            // on this manager and recurses strictly downward (ON e.ManagerId = emp.EmployeeId), so a
            // filter or depth can only narrow the requestor's own subtree, never reach outside it.
            if (employeeId.Value == part.ManagerId.Value)
            {
                _logger.SqlPartApprovedByManagerSelf(partIndex);
                mix.SqlApproved++;
                return null;
            }

            return Reject(part, partIndex, SourcePartRejectionReason.Requestor_Manager_Mismatch,
                "the requestor is not the manager referenced in the query");
        }

        private PartRejection Reject(SourcePart part, int partIndex, SourcePartRejectionReason reason, string details)
        {
            _logger.SourcePartRejected(partIndex, part.Type, part.SourceDescriptor, reason.ToString(), details);
            return new PartRejection(reason, details);
        }
    }
}
