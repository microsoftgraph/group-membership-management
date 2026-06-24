// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.AutoApprover;
using Microsoft.Extensions.Logging;
using Models;
using Models.ServiceBus;
using Models.SyncJobChange;
using Repositories.Contracts;
using Services.AutoApprover.Contracts;
using Services.AutoApprover.Helpers;
using System;
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
        private readonly ILogger<AutoApproverService> _logger;

        public AutoApproverService(
            IDatabaseSyncJobsRepository syncJobRepository,
            IDatabaseSettingsRepository databaseSettingsRepository,
            IGraphGroupRepository graphGroupRepository,
            ISyncJobChangeRepository syncJobChangeRepository,
            ILogger<AutoApproverService> logger)
        {
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
            _databaseSettingsRepository = databaseSettingsRepository ?? throw new ArgumentNullException(nameof(databaseSettingsRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _syncJobChangeRepository = syncJobChangeRepository ?? throw new ArgumentNullException(nameof(syncJobChangeRepository));
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
            var shouldAutoApprove = await ShouldAutoApproveJobAsync(syncJob.Query, message.RequestorObjectId, isGroupBasedAutoApprovalEnabled, isOrgLeaderAutoApprovalEnabled);

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

        public async Task MoveJobToPendingReviewAsync(Guid syncJobId)
        {
            var syncJob = await _syncJobRepository.GetSyncJobAsync(syncJobId);
            if (syncJob == null)
            {
                _logger.SyncJobNotFound(syncJobId);
                return;
            }

            // Only transition jobs still awaiting auto-approval. If the job has already advanced
            // (e.g. approved to Idle or already moved to PendingReview), this is a no-op so the
            // call is idempotent under Service Bus at-least-once redelivery.
            if (!string.Equals(syncJob.Status, SyncStatus.PendingAutoApproval.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            syncJob.Status = SyncStatus.PendingReview.ToString();
            await _syncJobRepository.UpdateSyncJobsAsync(new[] { syncJob });
            _logger.SyncJobMovedToPendingReview(syncJobId);
        }

        private async Task<bool> ShouldAutoApproveJobAsync(string query, string userIdentity, bool isGroupBasedAutoApprovalEnabled, bool isOrgLeaderAutoApprovalEnabled)
        {
            try
            {
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
    }
}
