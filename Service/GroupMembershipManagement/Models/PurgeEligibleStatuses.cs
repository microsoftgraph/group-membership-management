// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Models
{
    /// <summary>
    /// Single source of truth for sync job statuses that are eligible for purging by AzureMaintenance.
    /// AzureMaintenanceService consumes <see cref="All"/> to query/purge jobs; MailFallbackBuilder
    /// consumes <see cref="All"/> (via <see cref="System.Enum.TryParse{TEnum}(string, bool, out TEnum)"/>)
    /// to render status-specific JobPurgingWarning fallback emails. Adding a new status here
    /// automatically keeps both sides in sync — but the corresponding localization keys
    /// (JobPurgingWarningFallback.Description.{Status} and JobPurgingWarningFallback.CalloutBody.{Status})
    /// must still be added or the fallback will degrade to the Generic description.
    /// </summary>
    public static class PurgeEligibleStatuses
    {
        public static readonly SyncStatus[] All =
        [
            SyncStatus.CustomerPaused,
            SyncStatus.DestinationGroupNotFound,
            SyncStatus.MembershipDataNotFound,
            SyncStatus.NotOwnerOfDestinationGroup,
            SyncStatus.SecurityGroupNotFound,
            SyncStatus.ThresholdExceeded,
            SyncStatus.SubmissionRejected,
            SyncStatus.GuestUsersCannotBeAddedToUnifiedGroup,
            SyncStatus.NestedGroupsFound
        ];
    }
}
