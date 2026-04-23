// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

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
        private readonly ILoggingRepository _loggingRepository;

        public AutoApproverService(
            IDatabaseSyncJobsRepository syncJobRepository,
            IDatabaseSettingsRepository databaseSettingsRepository,
            IGraphGroupRepository graphGroupRepository,
            ISyncJobChangeRepository syncJobChangeRepository,
            ILoggingRepository loggingRepository)
        {
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
            _databaseSettingsRepository = databaseSettingsRepository ?? throw new ArgumentNullException(nameof(databaseSettingsRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _syncJobChangeRepository = syncJobChangeRepository ?? throw new ArgumentNullException(nameof(syncJobChangeRepository));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        public async Task ProcessAutoApprovalAsync(AutoApprovalQueueMessage message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));

            var syncJob = await _syncJobRepository.GetSyncJobAsync(message.SyncJobId);
            if (syncJob == null)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"AutoApprover: Sync job {message.SyncJobId} not found."
                });
                return;
            }

            if (syncJob.Status == SyncStatus.PendingConfiguration.ToString())
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"AutoApprover: Sync job {message.SyncJobId} is pending configuration. Skipping auto-approval."
                });
                return;
            }

            if (!string.Equals(syncJob.Status, SyncStatus.PendingReview.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"AutoApprover: Sync job {message.SyncJobId} status is {syncJob.Status}. Skipping auto-approval."
                });
                return;
            }

            if (!Guid.TryParse(message.RequestorObjectId, out var requestorObjectId))
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"AutoApprover: Invalid requestor object ID for sync job {message.SyncJobId}."
                });
                return;
            }

            var isGroupBasedAutoApprovalEnabled = await IsAutoApprovalForGroupBasedSyncsEnabledAsync();
            var isOrgLeaderAutoApprovalEnabled = await IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabledAsync();
            var shouldAutoApprove = await ShouldAutoApproveJobAsync(syncJob.Query, message.RequestorObjectId, isGroupBasedAutoApprovalEnabled, isOrgLeaderAutoApprovalEnabled);

            if (!shouldAutoApprove)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"AutoApprover: Auto-approval not granted for sync job {message.SyncJobId}."
                });
                return;
            }

            if (syncJob.StartDate < DateTime.UtcNow)
            {
                syncJob.StartDate = DateTime.UtcNow.AddHours(24);
            }

            syncJob.Status = SyncStatus.Idle.ToString();

            await _syncJobRepository.UpdateSyncJobsAsync(new[] { syncJob });

            var changedOnBehalfOfDisplayName = message.ChangedOnBehalfOfDisplayName;
            var changedOnBehalfOfObjectId = message.ChangedOnBehalfOfObjectId;

            await _syncJobChangeRepository.Save(new SyncJobChange
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
            });

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"AutoApprover: Sync job {message.SyncJobId} auto-approved."
            });
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
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Error during auto-approval check: {ex.Message}"
                });
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

                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Auto-approval granted: All {groupIds.Count} source groups have acceptable visibility."
                });

                return true;
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Error during GroupMembership auto-approval check: {ex.Message}"
                });
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

                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Auto-approval granted: Single SqlMembership query with manager ID matching requestor's onPremisesImmutableId."
                });

                return true;
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Error during SqlMembership auto-approval check: {ex.Message}"
                });
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
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Error retrieving user onPremisesImmutableId: {ex.Message}"
                });
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
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Error retrieving auto-approval setting: {ex.Message}"
                });
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
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Error retrieving org leader auto-approval setting: {ex.Message}"
                });
                return false;
            }
        }
    }
}
