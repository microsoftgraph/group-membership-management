// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Repositories.Mail
{
    public class MailFallbackBuilder : IMailFallbackBuilder
    {
        private const int AddedCountIndex = 2;
        private const int RemovedCountIndex = 3;
        private const int RequestorIndex = 4;
        private const int RejectionReasonIndex = 2;
        private const int RejectionRequestorIndex = 3;
        private const int SubmissionRejectedRejectedAtIndex = 4;

        // SyncDisabled NotOwner AdditionalContentParams indices
        // (set by JobTrigger SubOrchestratorFunction for NotOwnerNotification):
        // [0]=GroupId, [1]=DestinationName, [2]=StatusDescription, [3]=GMMOwnerAppName, [4]=PausedAtUtc (ISO 8601)
        private const int GmmOwnerNameIndex = 3;
        private const int NoOwnerPausedAtIndex = 4;

        // SyncDisabled NoData AdditionalContentParams indices
        // (set by MembershipAggregator MembershipSubOrchestratorFunction for NoDataNotification):
        // [0]=GroupId, [1]=DestinationName, [2]=PausedAtUtc (ISO 8601)
        private const int NoDataPausedAtIndex = 2;

        // SyncDisabled GuestUsers AdditionalContentParams indices
        // (set by GraphUpdater Orchestrator / OrchestratorMultiLane for GuestUserFailureNotification):
        // [0]=GroupId, [1]=DestinationName, [2]=MembersAddedCount, [3]=MembersRemovedCount,
        // [4]=StatusDescription, [5]=PausedAtUtc (ISO 8601)
        private const int GuestUsersPausedAtIndex = 5;

        // SyncDisabled NestedGroupsFound AdditionalContentParams indices
        // (set by GroupMembershipObtainer SubOrchestratorFunction for NestedGroupsFoundNotification):
        // [0]=GroupId, [1]=DestinationName, [2]=NestedGroupsCount, [3]=NestedGroupsInfo,
        // [4]=StatusDescription, [5]=PausedAtUtc (ISO 8601)
        private const int NestedGroupsCountIndex = 2;
        private const int NestedGroupsListIndex = 3;
        private const int NestedGroupsFoundPausedAtIndex = 5;

        // Cap on nested-group names rendered; overridable via App Config "Mail:NestedGroupsDisplayLimit".
        private const int DefaultNestedGroupsDisplayLimit = 5;
        private readonly int _nestedGroupsDisplayLimit;

        // SyncDisabled NoDestinationGroup AdditionalContentParams indices
        // (set by JobTrigger SubOrchestratorFunction and GraphUpdater GroupValidatorFunction for
        // DestinationNotExistNotification):
        // [0]=GroupId, [1]=DestinationName|SupportEmail, [2]=StatusDescription, [3]=PausedAtUtc (ISO 8601)
        private const int NoDestinationGroupPausedAtIndex = 3;

        // SyncDisabled NoSourceGroup AdditionalContentParams indices
        // (set by GroupMembershipObtainer GroupValidatorFunction for SourceNotExistNotification):
        // [0]=GroupId, [1]=TargetGroupName, [2]=SourceObjectId, [3]=StatusDescription, [4]=PausedAtUtc (ISO 8601)
        private const int NoSourceGroupPausedAtIndex = 4;

        // SyncDisabled Threshold AdditionalContentParams indices (set by
        // NotifierService.SendThresholdEmailAsync when CardState == DisabledCard, and
        // NotifierService.SendNormalThresholdEmailAsync when sendDisableJobNotification=true;
        // content type = SyncJobDisabledEmailBody):
        // [0]=GroupName, [1]=GroupId, [2]=SupportEmailAddresses, [3]=LearnMoreAboutGMMUrl,
        // [4]=PausedAtUtc, [5]=ChangeQuantityForAdditions, [6]=ChangeQuantityForRemovals,
        // [7]=ChangePercentageForAdditions, [8]=ChangePercentageForRemovals,
        // [9]=ThresholdPercentageForAdditions, [10]=ThresholdPercentageForRemovals,
        // [11]=ExceededDirection ("Increase" | "Decrease" | "Both"). Slots [5]–[11] are optional;
        // when absent the description degrades to the static SyncDisabledFallback.Description.Threshold.
        private const int ThresholdPausedAtIndex = 4;
        private const int ThresholdAddedCountIndex = 5;
        private const int ThresholdRemovedCountIndex = 6;
        private const int ThresholdActualIncreasePctIndex = 7;
        private const int ThresholdActualDecreasePctIndex = 8;
        private const int ThresholdConfiguredIncreasePctIndex = 9;
        private const int ThresholdConfiguredDecreasePctIndex = 10;
        private const int ThresholdExceededDirectionIndex = 11;

        // SyncDisabled compact-detail reasons share the same layout: 2-3 row details table
        // (Group Email/Type when available, Paused At), suppressed requestor row, and the orange
        // "What to do" action-checklist with a PausedAt + NumberOfDaysBeforePurging deadline.
        // Add a reason here to opt into the shared rendering; per-reason knob is GetPausedAtIndex.
        private static readonly HashSet<string> _compactDetailReasons =
            new HashSet<string>(StringComparer.Ordinal) { "NoDestinationGroup", "NoSourceGroup", "NoOwner", "NoData", "GuestUsers", "NestedGroupsFound", "Threshold" };

        private static int GetPausedAtIndex(string disableReason) => disableReason switch
        {
            "NoDestinationGroup" => NoDestinationGroupPausedAtIndex,
            "NoSourceGroup" => NoSourceGroupPausedAtIndex,
            "NoOwner" => NoOwnerPausedAtIndex,
            "NoData" => NoDataPausedAtIndex,
            "GuestUsers" => GuestUsersPausedAtIndex,
            "NestedGroupsFound" => NestedGroupsFoundPausedAtIndex,
            "Threshold" => ThresholdPausedAtIndex,
            _ => -1
        };

        // JobPurgingWarning AdditionalContentParams indices (AzureMaintenanceService.SendWarningEmailAsync):
        // [0]Status [1]InactivitySince [2]NumberOfDaysBeforePurging [3]ScheduledPurgeDate
        // [4]GroupId [5]GroupName [6]InactivitySinceUtc(ISO) [7]ScheduledPurgeDateUtc(ISO).
        // [8]LastSuccessfulRunTimeUtc(ISO, empty when never run) [9]RejectionReason (SubmissionRejected only).
        // [10]GmmOwnerAppName (NotOwnerOfDestinationGroup only).
        // [6]–[10] are optional; missing values degrade gracefully.
        private const int PurgeWarningStatusIndex = 0;
        private const int PurgeWarningInactiveSinceIndex = 1;
        private const int PurgeWarningDaysBeforePurgingIndex = 2;
        private const int PurgeWarningScheduledPurgeDateIndex = 3;
        private const int PurgeWarningGroupNameIndex = 5;
        private const int PurgeWarningInactiveSinceUtcIndex = 6;
        private const int PurgeWarningScheduledPurgeDateUtcIndex = 7;
        private const int PurgeWarningLastSuccessfulRunTimeUtcIndex = 8;
        private const int PurgeWarningRejectionReasonIndex = 9;
        private const int PurgeWarningGmmOwnerAppNameIndex = 10;

        // FinalNotice (InactiveSyncJobNotification / SyncPurgedForInactivityEmailBody)
        // AdditionalContentParams indices (AzureMaintenanceService.SendPurgingEmailAsync):
        // [0]=GroupId, [1]=GroupName, [2]=LegacyDeletionDate (kept for adaptive card),
        // [3]=PriorStatus, [4]=AffiliationRemovedUtc (ISO 8601),
        // [5]=LastSuccessfulRunTimeUtc (ISO 8601, optional),
        // [6]=RejectedOnUtc (ISO 8601, SubmissionRejected variant only).
        private const int FinalNoticeGroupNameIndex = 1;
        private const int FinalNoticePriorStatusIndex = 3;
        private const int FinalNoticeAffiliationRemovedUtcIndex = 4;
        private const int FinalNoticeLastSuccessfulRunUtcIndex = 5;
        private const int FinalNoticeRejectedOnUtcIndex = 6;

        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly ILocalizationRepository _localizationRepository;
        private readonly ILogger<MailFallbackBuilder> _logger;
        private readonly IHandleInactiveJobsConfig _handleInactiveJobsConfig;
        private readonly IDatabaseDestinationAttributesRepository _destinationAttributesRepository;

        public MailFallbackBuilder(
            IGraphGroupRepository graphGroupRepository,
            ILocalizationRepository localizationRepository,
            ILogger<MailFallbackBuilder> logger,
            IHandleInactiveJobsConfig handleInactiveJobsConfig = null,
            int nestedGroupsDisplayLimit = DefaultNestedGroupsDisplayLimit,
            IDatabaseDestinationAttributesRepository destinationAttributesRepository = null)
        {
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _localizationRepository = localizationRepository ?? throw new ArgumentNullException(nameof(localizationRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _handleInactiveJobsConfig = handleInactiveJobsConfig;
            _nestedGroupsDisplayLimit = nestedGroupsDisplayLimit > 0 ? nestedGroupsDisplayLimit : DefaultNestedGroupsDisplayLimit;
            _destinationAttributesRepository = destinationAttributesRepository;
        }

        public async Task<string> BuildSyncStartedFallbackAsync(
            EmailMessage emailMessage, string destinationGroupName, string groupId, string jobUrl, string sentDate)
        {
            var requestor = GetParam(emailMessage, RequestorIndex);
            var rows = await BuildBaseRowsAsync(groupId, requestor);

            return FormatTemplate(
                HtmlTemplates.SyncStartedTemplate,
                prefix: "SyncStartedFallback",
                groupName: destinationGroupName,
                headerText: _localizationRepository.TranslateSetting("SyncStartedFallback.HeaderTitle"),
                description: _localizationRepository.TranslateSetting("SyncStartedFallback.Description", requestor),
                calloutBody: _localizationRepository.TranslateSetting("SyncStartedFallback.CalloutBody"),
                rows: rows,
                jobUrl: jobUrl,
                sentDate: sentDate
            );
        }

        public async Task<string> BuildSyncCompletedFallbackAsync(
            EmailMessage emailMessage, string destinationGroupName, string groupId, string jobUrl, string sentDate)
        {
            var addedCount   = GetParam(emailMessage, AddedCountIndex,   defaultValue: "0");
            var removedCount = GetParam(emailMessage, RemovedCountIndex,  defaultValue: "0");
            var requestor    = GetParam(emailMessage, RequestorIndex);

            var rows = await BuildBaseRowsAsync(groupId, requestor);

            return FormatTemplate(
                HtmlTemplates.SyncCompletedTemplate,
                prefix: "SyncCompletedFallback",
                groupName: destinationGroupName,
                headerText: _localizationRepository.TranslateSetting("SyncCompletedFallback.HeaderTitle"),
                description: _localizationRepository.TranslateSetting("SyncCompletedFallback.Description"),
                calloutBody: _localizationRepository.TranslateSetting("SyncCompletedFallback.CalloutBody", addedCount, removedCount),
                rows: rows,
                jobUrl: jobUrl,
                sentDate: sentDate
            );
        }

        public async Task<string> BuildSyncDisabledFallbackAsync(
            EmailMessage emailMessage, string destinationGroupName, string groupId, string jobUrl, string sentDate)
        {
            var disableReason = GetDisableReason(emailMessage.Content);

            // Generic / unknown content types fall through to the legacy adaptive-card + plain-text path
            // rather than rendering a styled email with no useful per-reason detail.
            if (disableReason == "Generic")
            {
                return null;
            }

            var gmmOwnerName = GetParam(emailMessage, GmmOwnerNameIndex);

            // Requestor row is suppressed for all Sync Disabled fallbacks. Compact-detail
            // reasons additionally render a "Paused At" row via BuildCompactDetailRowsAsync;
            // the paused-at timestamp index per reason is defined in GetPausedAtIndex.
            var isCompactDetail = _compactDetailReasons.Contains(disableReason);
            var rows = isCompactDetail
                ? await BuildCompactDetailRowsAsync(disableReason, groupId, emailMessage)
                : await BuildBaseRowsAsync(groupId, requestor: string.Empty);

            // {3} carries the nested-groups count so the NestedGroupsFound description can
            // surface it inline. Other reasons ignore the extra arg, which is harmless to
            // string.Format when the resx value contains no {3} token.
            var nestedGroupsCount = disableReason == "NestedGroupsFound"
                ? GetParam(emailMessage, NestedGroupsCountIndex)
                : string.Empty;

            // {4}/{5} surface the added/removed user counts for the GuestUsers description
            // ("Before pausing, GMM finished this sync with X users added and Y users removed.").
            // Other reasons' descriptions do not reference {4}/{5}, so the extra args are ignored
            // by string.Format.
            var addedCount = disableReason == "GuestUsers"
                ? GetParam(emailMessage, AddedCountIndex, defaultValue: "0")
                : string.Empty;
            var removedCount = disableReason == "GuestUsers"
                ? GetParam(emailMessage, RemovedCountIndex, defaultValue: "0")
                : string.Empty;

            // Threshold renders a direction-aware "This sync would add N members..." sentence
            // using the rich data from ThresholdNotification (slots [5]..[11]).
            string description = disableReason == "Threshold"
                ? BuildThresholdDescription(emailMessage)
                : _localizationRepository.TranslateSetting(
                    $"SyncDisabledFallback.Description.{disableReason}",
                    string.Empty, groupId ?? string.Empty, gmmOwnerName, nestedGroupsCount, addedCount, removedCount);

            var displayGroupName = !string.IsNullOrWhiteSpace(destinationGroupName)
                ? destinationGroupName
                : _localizationRepository.TranslateSetting("FallbackUnknownGroupName");
            var syncDisabledGroupName = disableReason == "NoDestinationGroup" && !string.IsNullOrWhiteSpace(destinationGroupName)
                ? _localizationRepository.TranslateSetting("SyncDisabledFallback.PreviouslyNamedPrefix") + destinationGroupName
                : displayGroupName;

            return FormatTemplate(
                HtmlTemplates.SyncDisabledTemplate,
                prefix: "SyncDisabledFallback",
                groupName: syncDisabledGroupName,
                headerText: _localizationRepository.TranslateSetting(
                    "SyncDisabledFallback.HeaderTitle",
                    _localizationRepository.TranslateSetting($"SyncDisabledFallback.HeaderReason.{disableReason}")),
                description: description,
                // Compact-detail reasons share the PausedShared callout body to avoid duplication.
                calloutBody: _localizationRepository.TranslateSetting(
                    isCompactDetail
                        ? "SyncDisabledFallback.CalloutBody.PausedShared"
                        : $"SyncDisabledFallback.CalloutBody.{disableReason}",
                    ActionByDays.ToString(CultureInfo.InvariantCulture)),
                rows: rows,
                jobUrl: jobUrl,
                sentDate: sentDate,
                actionChecklistHtml: BuildActionChecklistHtml(disableReason, emailMessage),
                extraCalloutHtml: BuildNestedGroupsCalloutHtml(disableReason, emailMessage),
                ctaLabelOverride: ResolveSyncDisabledCtaLabelOverride(disableReason)
            );
        }

        public async Task<string> BuildSubmissionRejectedFallbackAsync(
            EmailMessage emailMessage, string destinationGroupName, string groupId, string jobUrl, string sentDate)
        {
            _logger.LogInformation(
                "Building SubmissionRejected fallback HTML for group {GroupId} ({GroupName}).", groupId, destinationGroupName);

            var rejectionReason = GetParam(emailMessage, RejectionReasonIndex);
            var requestor       = GetParam(emailMessage, RejectionRequestorIndex);

            var rows = await BuildSubmissionRejectedRowsAsync(groupId, requestor);

            return FormatTemplate(
                HtmlTemplates.SubmissionRejectedTemplate,
                prefix: "SubmissionRejectedFallback",
                groupName: destinationGroupName,
                headerText: _localizationRepository.TranslateSetting("SubmissionRejectedFallback.HeaderTitle"),
                description: _localizationRepository.TranslateSetting("SubmissionRejectedFallback.Description"),
                calloutBody: _localizationRepository.TranslateSetting(
                    "SubmissionRejectedFallback.CalloutBody",
                    ActionByDays.ToString(CultureInfo.InvariantCulture)),
                rows: rows,
                jobUrl: jobUrl,
                sentDate: sentDate,
                actionChecklistHtml: BuildSubmissionRejectedActionChecklistHtml(emailMessage) + BuildReviewerFeedbackHtml(rejectionReason),
                extraCalloutHtml: string.Empty
            );
        }

        private async Task<StringBuilder> BuildSubmissionRejectedRowsAsync(string groupId, string requestor)
        {
            Func<string, string> encode = System.Net.WebUtility.HtmlEncode;
            var rows = new StringBuilder();

            var (groupAlias, groupType) = await FetchGroupMetaAsync(groupId);

            rows.Append(string.Format(HtmlTemplates.DetailsTableRow,
                _localizationRepository.TranslateSetting("FallbackDetailsRow.GroupAlias"),
                string.IsNullOrWhiteSpace(groupAlias) ? "N/A" : encode(groupAlias), ""));

            if (!string.IsNullOrEmpty(groupType))
            {
                rows.Append(string.Format(HtmlTemplates.DetailsTableRow,
                    _localizationRepository.TranslateSetting("FallbackDetailsRow.GroupType"),
                    encode(groupType), ""));
            }

            if (!string.IsNullOrWhiteSpace(requestor))
            {
                rows.Append(string.Format(HtmlTemplates.DetailsTableRow,
                    _localizationRepository.TranslateSetting("FallbackDetailsRow.SubmittedBy"),
                    encode(requestor), ""));
            }

            return rows;
        }

        private string BuildSubmissionRejectedActionChecklistHtml(EmailMessage emailMessage)
        {
            var body = _localizationRepository.TranslateSetting("SubmissionRejectedFallback.ActionChecklist.Body");
            if (string.IsNullOrWhiteSpace(body) || body == "SubmissionRejectedFallback.ActionChecklist.Body")
                return string.Empty;

            var rejectedAtUtc = TryParseIsoUtc(GetParam(emailMessage, SubmissionRejectedRejectedAtIndex)) ?? DateTime.UtcNow;
            var deadline = ConvertToPacific(rejectedAtUtc).AddDays(ActionByDays);
            var formatted = deadline.ToString("ddd, MMM d, yyyy", CultureInfo.InvariantCulture);
            string deadlineSpan = "&middot; by " + System.Net.WebUtility.HtmlEncode(formatted);

            var title = _localizationRepository.TranslateSetting("SyncDisabledFallback.ActionChecklist.Title");
            return string.Format(
                HtmlTemplates.OrangeActionChecklistHtml,
                System.Net.WebUtility.HtmlEncode(title),
                deadlineSpan,
                RenderActionChecklistBody(body));
        }

        private string BuildReviewerFeedbackHtml(string rejectionReason)
        {
            if (string.IsNullOrWhiteSpace(rejectionReason))
                return string.Empty;

            var bodyHtml = new StringBuilder();
            bodyHtml.Append("<p style=\"margin:0;\">")
                    .Append(System.Net.WebUtility.HtmlEncode(rejectionReason))
                    .Append("</p>");

            var title = _localizationRepository.TranslateSetting("SubmissionRejectedFallback.ReviewerFeedbackLabel");
            return string.Format(
                HtmlTemplates.GrayExtraCalloutHtml,
                System.Net.WebUtility.HtmlEncode(title),
                bodyHtml.ToString());
        }

        public async Task<string> BuildJobPurgingWarningFallbackAsync(
            EmailMessage emailMessage, string destinationGroupName, string groupId, string jobUrl, string sentDate)
        {
            var status              = GetParam(emailMessage, PurgeWarningStatusIndex);
            var inactiveSince       = GetParam(emailMessage, PurgeWarningInactiveSinceIndex);
            var daysBeforePurging   = GetParam(emailMessage, PurgeWarningDaysBeforePurgingIndex);
            var scheduledPurgeDate  = GetParam(emailMessage, PurgeWarningScheduledPurgeDateIndex);
            // Prefer the resolved DestinationGroupName, but fall back to the value carried in
            // AdditionalContentParams[5] (set by AzureMaintenanceService) when the caller could
            // not resolve a name. An empty group name corrupts the markdown bold parser via
            // **{5}** -> ****, so we treat blank as "unknown" rather than letting it propagate.
            var groupName = !string.IsNullOrWhiteSpace(destinationGroupName)
                ? destinationGroupName
                : GetParam(emailMessage, PurgeWarningGroupNameIndex, defaultValue: string.Empty);
            var hasRealGroupName = !string.IsNullOrWhiteSpace(groupName);
            if (!hasRealGroupName)
            {
                groupName = _localizationRepository.TranslateSetting("FallbackUnknownGroupName");
            }

            var statusKey = ResolvePurgeWarningStatusKey(status);

            // Generic / unknown statuses also fall through to the legacy adaptive-card + plain-text
            // path rather than rendering a styled email with no actionable detail.
            if (statusKey == "Generic")
            {
                return null;
            }

            // Mirror SyncDisabled NoDestinationGroup behaviour: when the destination group
            // no longer exists, surface the cached name as "Previously named: <name>".
            var displayGroupName = (statusKey == "DestinationGroupNotFound" && hasRealGroupName)
                ? _localizationRepository.TranslateSetting("SyncDisabledFallback.PreviouslyNamedPrefix") + groupName
                : groupName;
            var warningDays = WarningDays.ToString(CultureInfo.InvariantCulture);

            // Prefer ISO UTC timestamps (params [6]/[7]) so we can format dates with time + PT
            // suffix exactly like the Sync Disabled "PAUSED AT" row. Fall back to the plain
            // date-string params [1]/[3] when the producer hasn't been updated yet.
            var inactiveSinceUtc = TryParseIsoUtc(GetParam(emailMessage, PurgeWarningInactiveSinceUtcIndex));
            var scheduledPurgeUtc = TryParseIsoUtc(GetParam(emailMessage, PurgeWarningScheduledPurgeDateUtcIndex));

            var pausedAtDisplay = inactiveSinceUtc.HasValue
                ? FormatPausedAtPacific(inactiveSinceUtc.Value)
                : ReformatDateOnly(inactiveSince);

            var purgeDateDisplay = scheduledPurgeUtc.HasValue
                ? ConvertToPacific(scheduledPurgeUtc.Value).ToString("ddd, MMM d, yyyy", CultureInfo.InvariantCulture)
                : FormatPurgeDateForDisplay(scheduledPurgeDate);

            var headerText = _localizationRepository.TranslateSetting(
                "JobPurgingWarningFallback.HeaderReason", warningDays);

            var priorNotificationTitle = ResolvePriorNotificationTitle(statusKey);
            string descriptionKey = string.IsNullOrEmpty(priorNotificationTitle)
                ? "JobPurgingWarningFallback.Description.StatusOnly"
                : "JobPurgingWarningFallback.Description.PreviousNotification";

            var description = _localizationRepository.TranslateSetting(
                descriptionKey,
                status, inactiveSince, daysBeforePurging, purgeDateDisplay,
                string.Empty, groupName, warningDays, priorNotificationTitle);

            var calloutBody = _localizationRepository.TranslateSetting(
                "JobPurgingWarningFallback.CalloutBody", purgeDateDisplay);

            var rows = await BuildJobPurgingWarningRowsAsync(groupId, statusKey, pausedAtDisplay, emailMessage);

            var rejectionReason = statusKey == "SubmissionRejected"
                ? GetParam(emailMessage, PurgeWarningRejectionReasonIndex, defaultValue: string.Empty)
                : string.Empty;
            var reviewerFeedbackHtml = BuildReviewerFeedbackHtml(rejectionReason);

            var gmmOwnerAppName = GetParam(emailMessage, PurgeWarningGmmOwnerAppNameIndex, defaultValue: string.Empty);
            if (string.IsNullOrWhiteSpace(gmmOwnerAppName))
                gmmOwnerAppName = "GMM";

            return FormatTemplate(
                HtmlTemplates.JobPurgingWarningTemplate,
                prefix: "JobPurgingWarningFallback",
                groupName: displayGroupName,
                headerText: headerText,
                description: description,
                calloutBody: calloutBody,
                rows: rows,
                jobUrl: jobUrl,
                sentDate: sentDate,
                actionChecklistHtml: BuildJobPurgingWarningActionChecklistHtml(statusKey, purgeDateDisplay, groupId, gmmOwnerAppName) + reviewerFeedbackHtml,
                extraCalloutHtml: string.Empty,
                ctaLabelOverride: statusKey == "ThresholdExceeded"
                    ? ResolveSyncDisabledCtaLabelOverride("Threshold")
                    : null
            );
        }

        // Variant 2 (8 non-SubmissionRejected statuses): GROUP EMAIL + GROUP TYPE + PAUSED AT.
        // Variant 1 (SubmissionRejected): adds LAST SYNC as a 4th row.
        private async Task<StringBuilder> BuildJobPurgingWarningRowsAsync(string groupId, string statusKey, string inactiveSince, EmailMessage emailMessage)
        {
            Func<string, string> encode = System.Net.WebUtility.HtmlEncode;
            var rows = new StringBuilder();

            var (groupAlias, groupType) = await FetchGroupMetaAsync(groupId);

            if (statusKey == "DestinationGroupNotFound")
            {
                var cachedEmail = await TryGetCachedDestinationEmailAsync(emailMessage);
                if (!string.IsNullOrWhiteSpace(cachedEmail))
                {
                    rows.Append(string.Format(HtmlTemplates.DetailsTableRow,
                        _localizationRepository.TranslateSetting("FallbackDetailsRow.LastKnownEmail"),
                        encode(cachedEmail), ""));
                }
            }
            else
            {
                rows.Append(string.Format(HtmlTemplates.DetailsTableRow,
                    _localizationRepository.TranslateSetting("FallbackDetailsRow.GroupAlias"),
                    string.IsNullOrWhiteSpace(groupAlias) ? "N/A" : encode(groupAlias), ""));
            }

            if (!string.IsNullOrEmpty(groupType))
            {
                rows.Append(string.Format(HtmlTemplates.DetailsTableRow,
                    _localizationRepository.TranslateSetting("FallbackDetailsRow.GroupType"),
                    encode(groupType), ""));
            }

            if (!string.IsNullOrWhiteSpace(inactiveSince))
            {
                var labelKey = statusKey == "SubmissionRejected"
                    ? "FallbackDetailsRow.RejectedOn"
                    : "FallbackDetailsRow.PausedAt";
                rows.Append(string.Format(HtmlTemplates.DetailsTableRow,
                    _localizationRepository.TranslateSetting(labelKey),
                    encode(inactiveSince), ""));
            }

            // Always render Last Sync row: PST timestamp when LastSuccessfulRunTime is set,
            // otherwise the shared "Never - new onboarding attempt" sentinel. Producer emits
            // empty string when SyncJob.LastSuccessfulRunTime is at/below the SQL sentinel
            // (~ SqlDateTime.MinValue.Value).
            var lastSuccessfulRunUtc = TryParseIsoUtc(
                GetParam(emailMessage, PurgeWarningLastSuccessfulRunTimeUtcIndex));
            var lastSyncDisplay = lastSuccessfulRunUtc.HasValue
                ? FormatPausedAtPacific(lastSuccessfulRunUtc.Value)
                : _localizationRepository.TranslateSetting("Fallback.LastSyncNeverNewOnboarding");
            rows.Append(string.Format(HtmlTemplates.DetailsTableRow,
                _localizationRepository.TranslateSetting("FallbackDetailsRow.LastSync"),
                encode(lastSyncDisplay), ""));

            return rows;
        }

        private string BuildJobPurgingWarningActionChecklistHtml(string statusKey, string purgeDateDisplay, string groupId, string gmmOwnerAppName)
        {
            // For statuses that already have a Sync Disabled / Submission Rejected fallback,
            // reuse that email's WHAT TO DO body verbatim so wording stays consistent.
            // CustomerPaused / ThresholdExceeded keep their PurgingWarning-specific bodies.
            string bodyKey;
            string[] args = Array.Empty<string>();
            switch (statusKey)
            {
                case "DestinationGroupNotFound":
                    bodyKey = "SyncDisabledFallback.ActionChecklist.NoDestinationGroup.Body"; break;
                case "SecurityGroupNotFound":
                    bodyKey = "SyncDisabledFallback.ActionChecklist.NoSourceGroup.Body"; break;
                case "NotOwnerOfDestinationGroup":
                    bodyKey = "SyncDisabledFallback.ActionChecklist.NoOwner.Body";
                    args = new[] { gmmOwnerAppName ?? "GMM", groupId ?? string.Empty };
                    break;
                case "MembershipDataNotFound":
                    bodyKey = "SyncDisabledFallback.ActionChecklist.NoData.Body"; break;
                case "GuestUsersCannotBeAddedToUnifiedGroup":
                    bodyKey = "SyncDisabledFallback.ActionChecklist.GuestUsers.Body"; break;
                case "NestedGroupsFound":
                    bodyKey = "SyncDisabledFallback.ActionChecklist.NestedGroupsFound.Body";
                    args = new[] { groupId ?? string.Empty };
                    break;
                case "SubmissionRejected":
                    bodyKey = "SubmissionRejectedFallback.ActionChecklist.Body"; break;
                case "ThresholdExceeded":
                    bodyKey = "SyncDisabledFallback.ActionChecklist.Threshold.Body"; break;
                default:
                    bodyKey = $"JobPurgingWarningFallback.ActionChecklist.Body.{statusKey}"; break;
            }

            var body = _localizationRepository.TranslateSetting(bodyKey, args);
            if (string.IsNullOrWhiteSpace(body) || body == bodyKey)
                return string.Empty;

            string deadlineSpan = string.Empty;
            if (!string.IsNullOrWhiteSpace(purgeDateDisplay))
            {
                deadlineSpan = "&middot; by " + System.Net.WebUtility.HtmlEncode(purgeDateDisplay);
            }

            var title = _localizationRepository.TranslateSetting("JobPurgingWarningFallback.ActionChecklist.Title");
            var checklistHtml = string.Format(
                HtmlTemplates.OrangeActionChecklistHtml,
                System.Net.WebUtility.HtmlEncode(title),
                deadlineSpan,
                RenderActionChecklistBody(body));

            // GuestUsers reuses the SyncDisabled FootNote (advisory paragraph rendered
            // outside the orange box, matching the reference design).
            var footNoteKey = statusKey == "GuestUsersCannotBeAddedToUnifiedGroup"
                ? "SyncDisabledFallback.ActionChecklist.GuestUsers.FootNote"
                : null;
            return footNoteKey != null
                ? checklistHtml + BuildActionChecklistFootNoteHtml(footNoteKey)
                : checklistHtml;
        }

        // ── FinalNotice (affiliation removed) ─────────────────────────────────────
        // Produced by AzureMaintenance.PurgingEmailSenderFunction after a paused job
        // has aged past NumberOfDaysBeforePurging without resolution. Renders a single
        // template with description / action-checklist that varies by the prior Status
        // (SubmissionRejected vs. Generic).
        public async Task<string> BuildFinalNoticeFallbackAsync(
            EmailMessage emailMessage, string destinationGroupName, string groupId, string jobUrl, string sentDate)
        {
            var priorStatus = GetParam(emailMessage, FinalNoticePriorStatusIndex);
            var statusKey = ResolvePurgeWarningStatusKey(priorStatus);

            if (statusKey == "Generic")
            {
                return null;
            }

            var variantKey = ResolveFinalNoticeVariantKey(priorStatus);
            var actionByDays = ActionByDays.ToString(CultureInfo.InvariantCulture);

            var groupName = !string.IsNullOrWhiteSpace(destinationGroupName)
                ? destinationGroupName
                : GetParam(emailMessage, FinalNoticeGroupNameIndex, defaultValue: string.Empty);
            var hasRealGroupName = !string.IsNullOrWhiteSpace(groupName);
            if (!hasRealGroupName)
            {
                groupName = _localizationRepository.TranslateSetting("FallbackUnknownGroupName");
            }

            var displayGroupName = (statusKey == "DestinationGroupNotFound" && hasRealGroupName)
                ? _localizationRepository.TranslateSetting("SyncDisabledFallback.PreviouslyNamedPrefix") + groupName
                : groupName;

            // Description routing mirrors the JobPurgingWarning lede choices:
            //   SubmissionRejected  -> dedicated variant text (existing).
            //   CustomerPaused      -> dedicated "remained customer paused" text (no prior email).
            //   Statuses with a system-sent SyncDisabled fallback email
            //                        -> quote the prior notification title verbatim.
            //   Anything else fell through to the early null return above.
            string descriptionKey;
            string priorNotificationTitle = null;
            if (statusKey == "SubmissionRejected")
            {
                descriptionKey = "FinalNoticeFallback.Description.SubmissionRejected";
            }
            else if (statusKey == "CustomerPaused")
            {
                descriptionKey = "FinalNoticeFallback.Description.CustomerPaused";
            }
            else
            {
                priorNotificationTitle = ResolvePriorNotificationTitle(statusKey);
                descriptionKey = !string.IsNullOrEmpty(priorNotificationTitle)
                    ? "FinalNoticeFallback.Description.PreviousNotification"
                    : "FinalNoticeFallback.Description.Generic";
            }

            var description = _localizationRepository.TranslateSetting(
                descriptionKey, actionByDays, priorNotificationTitle ?? string.Empty);

            var rows = await BuildFinalNoticeRowsAsync(groupId, variantKey, statusKey, emailMessage);

            return FormatTemplate(
                HtmlTemplates.FinalNoticeTemplate,
                prefix: "FinalNoticeFallback",
                groupName: displayGroupName,
                headerText: _localizationRepository.TranslateSetting("FinalNoticeFallback.HeaderTitle"),
                description: description,
                // The reference design has no gray "what happens if you do nothing" callout for the
                // Final Notice variant - the action-checklist already explains next steps. Pass an
                // empty string so the shared FormatTemplate omits the gray callout block.
                calloutBody: string.Empty,
                rows: rows,
                jobUrl: jobUrl,
                sentDate: sentDate,
                actionChecklistHtml: BuildFinalNoticeActionChecklistHtml(variantKey),
                extraCalloutHtml: string.Empty
            );
        }

        // Maps the producer-supplied PriorStatus to a fallback variant key. SubmissionRejected
        // gets its own description / action-checklist body; every other status (including unknown)
        // collapses to the Generic variant.
        private static string ResolveFinalNoticeVariantKey(string priorStatus)
        {
            if (!string.IsNullOrWhiteSpace(priorStatus)
                && string.Equals(priorStatus, "SubmissionRejected", StringComparison.OrdinalIgnoreCase))
            {
                return "SubmissionRejected";
            }
            return "Generic";
        }

        // Builds up to 4 rows for the Final Notice details table:
        // 1. GROUP EMAIL (always; "N/A" when Graph has no alias)
        // 2. GROUP TYPE (when Graph returns one)
        // 3. AFFILIATION REMOVED (always when producer supplied a parseable timestamp)
        // 4. LAST SYNC (formatted Pacific timestamp; renders "Never - new onboarding attempt"
        //    for the SubmissionRejected variant when no successful run is on record).
        private async Task<StringBuilder> BuildFinalNoticeRowsAsync(string groupId, string variantKey, string statusKey, EmailMessage emailMessage)
        {
            Func<string, string> encode = System.Net.WebUtility.HtmlEncode;
            var rows = new StringBuilder();

            var (groupAlias, groupType) = await FetchGroupMetaAsync(groupId);

            if (statusKey == "DestinationGroupNotFound")
            {
                var cachedEmail = await TryGetCachedDestinationEmailAsync(emailMessage);
                if (!string.IsNullOrWhiteSpace(cachedEmail))
                {
                    rows.Append(string.Format(HtmlTemplates.DetailsTableRow,
                        _localizationRepository.TranslateSetting("FallbackDetailsRow.LastKnownEmail"),
                        encode(cachedEmail), ""));
                }
            }
            else
            {
                rows.Append(string.Format(HtmlTemplates.DetailsTableRow,
                    _localizationRepository.TranslateSetting("FallbackDetailsRow.GroupAlias"),
                    string.IsNullOrWhiteSpace(groupAlias) ? "N/A" : encode(groupAlias), ""));
            }

            if (!string.IsNullOrEmpty(groupType))
            {
                rows.Append(string.Format(HtmlTemplates.DetailsTableRow,
                    _localizationRepository.TranslateSetting("FallbackDetailsRow.GroupType"),
                    encode(groupType), ""));
            }

            var affiliationRemovedUtc = TryParseIsoUtc(
                GetParam(emailMessage, FinalNoticeAffiliationRemovedUtcIndex));
            if (affiliationRemovedUtc.HasValue)
            {
                rows.Append(string.Format(HtmlTemplates.DetailsTableRow,
                    _localizationRepository.TranslateSetting("FallbackDetailsRow.AffiliationRemoved"),
                    encode(FormatPausedAtPacific(affiliationRemovedUtc.Value)), ""));
            }

            // SubmissionRejected variant only: when the producer found a matching
            // SyncJobChanges row, surface when the configuration was originally rejected
            // so owners can correlate this notice with the prior rejection email.
            if (variantKey == "SubmissionRejected")
            {
                var rejectedOnUtc = TryParseIsoUtc(
                    GetParam(emailMessage, FinalNoticeRejectedOnUtcIndex));
                if (rejectedOnUtc.HasValue)
                {
                    rows.Append(string.Format(HtmlTemplates.DetailsTableRow,
                        _localizationRepository.TranslateSetting("FallbackDetailsRow.RejectedOn"),
                        encode(FormatPausedAtPacific(rejectedOnUtc.Value)), ""));
                }
            }

            // Always render Last Sync row: PST timestamp when LastSuccessfulRunTime is set,
            // otherwise the shared "Never - new onboarding attempt" sentinel. Producer emits
            // empty string when SyncJob.LastSuccessfulRunTime is at/below the SQL sentinel
            // (~ SqlDateTime.MinValue.Value).
            var lastSyncUtc = TryParseIsoUtc(
                GetParam(emailMessage, FinalNoticeLastSuccessfulRunUtcIndex));
            var lastSyncDisplay = lastSyncUtc.HasValue
                ? FormatPausedAtPacific(lastSyncUtc.Value)
                : _localizationRepository.TranslateSetting("Fallback.LastSyncNeverNewOnboarding");
            rows.Append(string.Format(HtmlTemplates.DetailsTableRow,
                _localizationRepository.TranslateSetting("FallbackDetailsRow.LastSync"),
                encode(lastSyncDisplay), ""));

            return rows;
        }

        // The Final Notice action-checklist is a single advisory paragraph (no ordered list,
        // no deadline). We reuse the orange-box renderer used by the SyncDisabled / Warning
        // templates with an empty deadline span so the layout stays consistent.
        private string BuildFinalNoticeActionChecklistHtml(string variantKey)
        {
            var bodyKey = $"FinalNoticeFallback.ActionChecklist.Body.{variantKey}";
            var body = _localizationRepository.TranslateSetting(bodyKey);
            if (string.IsNullOrWhiteSpace(body) || body == bodyKey)
                return string.Empty;

            var title = _localizationRepository.TranslateSetting("FinalNoticeFallback.ActionChecklist.Title");
            return string.Format(
                HtmlTemplates.OrangeActionChecklistHtml,
                System.Net.WebUtility.HtmlEncode(title),
                string.Empty,
                RenderActionChecklistBody(body));
        }

        private static string FormatPurgeDateForDisplay(string producerDate)
        {
            if (string.IsNullOrWhiteSpace(producerDate)) return string.Empty;
            if (DateTime.TryParseExact(producerDate, "MMMM dd, yyyy",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                return parsed.ToString("ddd, MMM d, yyyy", CultureInfo.InvariantCulture);
            }
            if (DateTime.TryParse(producerDate, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out parsed))
            {
                return parsed.ToString("ddd, MMM d, yyyy", CultureInfo.InvariantCulture);
            }
            return producerDate;
        }

        private static string ReformatDateOnly(string producerDate)
        {
            if (string.IsNullOrWhiteSpace(producerDate)) return string.Empty;
            if (DateTime.TryParseExact(producerDate, "MMMM dd, yyyy",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                return parsed.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
            }
            if (DateTime.TryParse(producerDate, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out parsed))
            {
                return parsed.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
            }
            return producerDate;
        }

        private int WarningDays => _handleInactiveJobsConfig?.NumberOfDaysBeforePurgingToSendWarning ?? 7;

        private string ResolvePurgeWarningStatusKey(string status)
        {
            if (Enum.TryParse<SyncStatus>(status, ignoreCase: true, out var parsed)
                && Array.IndexOf(PurgeEligibleStatuses.All, parsed) >= 0)
            {
                return parsed.ToString();
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                _logger.LogWarning(
                    "Unknown SyncStatus '{Status}' for JobPurgingWarning fallback; using Generic description.",
                    status);
            }
            return "Generic";
        }

        // Maps a Purging Warning statusKey to the prior fallback email's header line so the lede
        // quotes the real subject. CustomerPaused return null (no system
        // email was sent) and fall back to the StatusOnly lede.
        private string ResolvePriorNotificationTitle(string statusKey)
        {
            string syncDisabledReason = statusKey switch
            {
                "DestinationGroupNotFound"            => "NoDestinationGroup",
                "SecurityGroupNotFound"               => "NoSourceGroup",
                "NotOwnerOfDestinationGroup"          => "NoOwner",
                "MembershipDataNotFound"              => "NoData",
                "GuestUsersCannotBeAddedToUnifiedGroup" => "GuestUsers",
                "NestedGroupsFound"                   => "NestedGroupsFound",
                "ThresholdExceeded"                   => "Threshold",
                _ => null
            };

            if (syncDisabledReason != null)
            {
                var reasonFragment = _localizationRepository.TranslateSetting(
                    $"SyncDisabledFallback.HeaderReason.{syncDisabledReason}");
                if (!string.IsNullOrWhiteSpace(reasonFragment))
                {
                    return _localizationRepository.TranslateSetting(
                        "SyncDisabledFallback.HeaderTitle", reasonFragment);
                }
            }

            if (statusKey == "SubmissionRejected")
            {
                return _localizationRepository.TranslateSetting("SubmissionRejectedFallback.HeaderTitle");
            }

            return null;
        }

        // Picks the direction-specific Threshold description from AdditionalContentParams[11].
        // Falls back to the static SyncDisabledFallback.Description.Threshold when no direction is set.
        private string BuildThresholdDescription(EmailMessage emailMessage)
        {
            var direction = GetParam(emailMessage, ThresholdExceededDirectionIndex, defaultValue: "");
            if (string.IsNullOrEmpty(direction))
            {
                return _localizationRepository.TranslateSetting("SyncDisabledFallback.Description.Threshold");
            }

            var added = GetParam(emailMessage, ThresholdAddedCountIndex, defaultValue: "0");
            var removed = GetParam(emailMessage, ThresholdRemovedCountIndex, defaultValue: "0");
            var actualIncreasePct = GetParam(emailMessage, ThresholdActualIncreasePctIndex, defaultValue: "0");
            var actualDecreasePct = GetParam(emailMessage, ThresholdActualDecreasePctIndex, defaultValue: "0");
            var configuredIncreasePct = GetParam(emailMessage, ThresholdConfiguredIncreasePctIndex, defaultValue: "0");
            var configuredDecreasePct = GetParam(emailMessage, ThresholdConfiguredDecreasePctIndex, defaultValue: "0");

            return direction switch
            {
                "Both" => _localizationRepository.TranslateSetting(
                    "SyncDisabledFallback.Description.Threshold.Both",
                    added, removed,
                    actualIncreasePct, actualDecreasePct,
                    configuredIncreasePct, configuredDecreasePct),
                "Decrease" => _localizationRepository.TranslateSetting(
                    "SyncDisabledFallback.Description.Threshold.Decrease.OnlyRemovals",
                    removed, actualDecreasePct, configuredDecreasePct),
                _ /* Increase */ => _localizationRepository.TranslateSetting(
                    "SyncDisabledFallback.Description.Threshold.Increase.OnlyAdditions",
                    added, actualIncreasePct, configuredIncreasePct),
            };
        }

        private async Task<StringBuilder> BuildBaseRowsAsync(string groupId, string requestor)
        {
            var (groupAlias, groupType) = await FetchGroupMetaAsync(groupId);
            return BuildCommonRows(groupId, groupAlias, groupType, requestor);
        }

        private string FormatTemplate(
            string template, string prefix, string groupName,
            string headerText, string description, string calloutBody,
            StringBuilder rows, string jobUrl, string sentDate,
            string actionChecklistHtml = "",
            string extraCalloutHtml = "",
            string ctaLabelOverride = null)
        {
            var name = string.IsNullOrWhiteSpace(groupName) ? "N/A" : groupName;
            var ctaLabel = !string.IsNullOrEmpty(ctaLabelOverride)
                ? ctaLabelOverride
                : _localizationRepository.TranslateSetting($"{prefix}.CtaLabel");
            var result = string.Format(
                template,
                _localizationRepository.TranslateSetting($"{prefix}.Badge"),             // {0} badge
                System.Net.WebUtility.HtmlEncode(headerText),                                // {1} header text (varies)
                System.Net.WebUtility.HtmlEncode(name),                                  // {2} title
                ConvertContentToHtml(description),                                        // {3} description
                rows.ToString(),                                                          // {4} details table rows
                _localizationRepository.TranslateSetting($"{prefix}.CalloutTitle"),      // {5} callout title
                ConvertContentToHtml(calloutBody),                                        // {6} callout body
                ctaLabel,                                                                 // {7} CTA label
                System.Net.WebUtility.HtmlEncode(SanitizeUrl(jobUrl)),                   // {8} CTA url
                ConvertContentToHtml(_localizationRepository.TranslateSetting("Fallback.FooterExplanation", name)), // {9} footer (shared across all variants)
                sentDate,                                                                 // {10} sent date
                actionChecklistHtml ?? string.Empty,                                      // {11} optional action checklist row
                extraCalloutHtml ?? string.Empty                                          // {12} optional extra gray callout (e.g. nested groups)
            );

            return result
                .Replace("__BRAND_EYEBROW__",
                    System.Net.WebUtility.HtmlEncode(_localizationRepository.TranslateSetting("Fallback.BrandHeader.Eyebrow")))
                .Replace("__BRAND_WORDMARK__",
                    System.Net.WebUtility.HtmlEncode(_localizationRepository.TranslateSetting("Fallback.BrandHeader.Wordmark")));
        }

        // Per-reason CTA label override (e.g. Threshold -> "Review in GMM").
        // Returns null when no override is defined so the default "{prefix}.CtaLabel" wins.
        private string ResolveSyncDisabledCtaLabelOverride(string disableReason)
        {
            var key = $"SyncDisabledFallback.CtaLabel.{disableReason}";
            var label = _localizationRepository.TranslateSetting(key);
            return string.IsNullOrEmpty(label) || string.Equals(label, key, StringComparison.Ordinal)
                ? null
                : label;
        }

        // Builds the gray "Nested groups detected · N total" callout that sits between the
        // description and the action checklist for NestedGroupsFound. Returns string.Empty for
        // other disable reasons or when no nested-group list is available. Styling matches the
        // shared PausedShared callout so both gray boxes look identical (per reference design).
        private static readonly Regex _nestedGroupsObjectIdSuffixRegex =
            new Regex(@"\s*\([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\)\s*$",
                RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex _nestedGroupsLeadingBulletRegex =
            new Regex(@"^\s*[-*\u2022]\s*",
                RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private string BuildNestedGroupsCalloutHtml(string disableReason, EmailMessage emailMessage)
        {
            if (disableReason != "NestedGroupsFound")
                return string.Empty;

            var list = GetParam(emailMessage, NestedGroupsListIndex);
            if (string.IsNullOrWhiteSpace(list))
                return string.Empty;

            var lines = list.Replace("\r\n", "\n").Split('\n')
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => _nestedGroupsObjectIdSuffixRegex.Replace(_nestedGroupsLeadingBulletRegex.Replace(l, string.Empty), string.Empty).Trim())
                .Where(l => l.Length > 0)
                .ToArray();
            if (lines.Length == 0)
                return string.Empty;

            var totalCount = int.TryParse(
                GetParam(emailMessage, NestedGroupsCountIndex),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : lines.Length;

            var listHtml = new StringBuilder();
            listHtml.Append("<ul style=\"margin:0;padding-left:18px;\">");
            foreach (var l in lines.Take(_nestedGroupsDisplayLimit))
            {
                listHtml.Append("<li style=\"margin:2px 0;\">")
                        .Append(System.Net.WebUtility.HtmlEncode(l))
                        .Append("</li>");
            }
            listHtml.Append("</ul>");

            if (totalCount > _nestedGroupsDisplayLimit)
            {
                listHtml.Append("<p style=\"margin:8px 0 0;font-size:13.5px;line-height:1.5;color:#605E5C;\">")
                        .Append(System.Net.WebUtility.HtmlEncode(
                            _localizationRepository.TranslateSetting(
                                "SyncDisabledFallback.NestedGroupsFound.ListNote",
                                _nestedGroupsDisplayLimit.ToString(CultureInfo.InvariantCulture))))
                        .Append("</p>");
            }

            var title = _localizationRepository.TranslateSetting(
                "SyncDisabledFallback.NestedGroupsFound.ListHeading",
                totalCount.ToString(CultureInfo.InvariantCulture));

            return string.Format(
                HtmlTemplates.GrayExtraCalloutHtml,
                System.Net.WebUtility.HtmlEncode(title),
                listHtml.ToString());
        }

        private static string GetParam(EmailMessage emailMessage, int index, string defaultValue = "")
        {
            return emailMessage.AdditionalContentParams?.Length > index
                ? emailMessage.AdditionalContentParams[index]
                : defaultValue;
        }

        // Parse the producer-formatted PausedAt string (ISO 8601 round-trip / "o"). Returns null
        // on parse failure so the caller can suppress the row rather than rendering a misleading
        // timestamp. Result is always a UTC DateTime regardless of the offset present in the input.
        private DateTime? TryParseIsoUtc(string isoUtc)
        {
            if (string.IsNullOrWhiteSpace(isoUtc))
                return null;

            if (DateTime.TryParse(
                    isoUtc,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsed))
            {
                return parsed;
            }

            _logger.LogWarning(
                "Could not parse PausedAt '{PausedAt}' as ISO 8601 UTC; suppressing PAUSED AT row.",
                isoUtc);
            return null;
        }

        // Convert a UTC timestamp to Pacific Time. America/Los_Angeles is the IANA id and
        // resolves on Windows and Linux Function hosts via .NET's built-in fallback.
        private static DateTime ConvertToPacific(DateTime utc) =>
            TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(utc, DateTimeKind.Utc),
                TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles"));

        // Format the UTC pause time in Pacific Time for the PAUSED AT row, e.g.
        // "May 6, 2026 · 8:42 AM PT". The "PT" suffix is intentionally generic (not PST/PDT) so
        // the label reads correctly in both standard and daylight time.
        private static string FormatPausedAtPacific(DateTime pausedAtUtc) =>
            ConvertToPacific(pausedAtUtc).ToString(
                "MMM d, yyyy \u00B7 h:mm tt 'PT'",
                CultureInfo.InvariantCulture);

        // Compact details table for compact-detail reasons.
        // NoSourceGroup / NoOwner: GROUP EMAIL + GROUP TYPE (Graph lookup on the still-valid destination) + PAUSED AT.
        // NoDestinationGroup: PAUSED AT only — Graph cannot resolve a deleted group and the
        // producer-supplied value at index 1 is a name (not an email), already shown in the header.
        private async Task<StringBuilder> BuildCompactDetailRowsAsync(string disableReason, string groupId, EmailMessage emailMessage)
        {
            Func<string, string> encode = System.Net.WebUtility.HtmlEncode;
            var rows = new StringBuilder();

            if (disableReason != "NoDestinationGroup")
            {
                var (groupAlias, groupType) = await FetchGroupMetaAsync(groupId);

                rows.Append(string.Format(HtmlTemplates.DetailsTableRow,
                    _localizationRepository.TranslateSetting("FallbackDetailsRow.GroupAlias"),
                    string.IsNullOrWhiteSpace(groupAlias) ? "N/A" : encode(groupAlias), ""));

                if (!string.IsNullOrEmpty(groupType))
                {
                    rows.Append(string.Format(HtmlTemplates.DetailsTableRow,
                        _localizationRepository.TranslateSetting("FallbackDetailsRow.GroupType"),
                        encode(groupType), ""));
                }
            }
            else
            {
                // Destination group has been deleted in Entra, so Graph cannot return a current
                // email. Surface the cached email from the DestinationEmail table (populated by
                // DestinationAttributesUpdater) as "LAST KNOWN EMAIL" — mirrors how the cached
                // DestinationName is surfaced as "Previously named:".
                var cachedEmail = await TryGetCachedDestinationEmailAsync(emailMessage);
                if (!string.IsNullOrWhiteSpace(cachedEmail))
                {
                    rows.Append(string.Format(HtmlTemplates.DetailsTableRow,
                        _localizationRepository.TranslateSetting("FallbackDetailsRow.LastKnownEmail"),
                        encode(cachedEmail), ""));
                }
            }

            var pausedAtParam = GetParam(emailMessage, GetPausedAtIndex(disableReason));
            var pausedAtUtc = TryParseIsoUtc(pausedAtParam);
            if (pausedAtUtc.HasValue)
            {
                rows.Append(string.Format(HtmlTemplates.DetailsTableRow,
                    _localizationRepository.TranslateSetting("FallbackDetailsRow.PausedAt"),
                    encode(FormatPausedAtPacific(pausedAtUtc.Value)), ""));
            }

            return rows;
        }

        private async Task<string> TryGetCachedDestinationEmailAsync(EmailMessage emailMessage)
        {
            if (_destinationAttributesRepository == null || emailMessage == null || emailMessage.SyncJobId == Guid.Empty)
                return null;
            try
            {
                return await _destinationAttributesRepository.GetDestinationEmail(emailMessage.SyncJobId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read cached destination email for SyncJob {SyncJobId}; LAST KNOWN EMAIL row will be omitted.", emailMessage.SyncJobId);
                return null;
            }
        }

        // Days a paused job remains affiliated with GMM before purging. Sourced from
        // AzureMaintenance:NumberOfDaysBeforePurging (IHandleInactiveJobsConfig). Falls back
        // to 30 if the config isn't registered (e.g., in hosts that don't run purging logic).
        private int ActionByDays => _handleInactiveJobsConfig?.NumberOfDaysBeforePurging ?? 30;

        // Build the optional orange "What to do" action-checklist HTML block injected between
        // the description and the details table. Only emitted for reasons that have a body in
        // resx ("SyncDisabledFallback.ActionChecklist.<reason>.Body"); returns empty otherwise
        // so existing templates render unchanged. The deadline span is shown when a valid
        // PausedAt was supplied by the producer (PausedAt + ActionByDays, Pacific Time).
        private static string[] GetActionChecklistArgs(string disableReason, EmailMessage emailMessage) =>
            disableReason switch
            {
                "NoOwner" => new[]
                {
                    GetParam(emailMessage, GmmOwnerNameIndex),
                    GetParam(emailMessage, 0) // groupId — for the Entra Owners deep-link
                },
                "NestedGroupsFound" => new[]
                {
                    GetParam(emailMessage, 0) // groupId — for the Entra Members deep-link
                },
                _ => Array.Empty<string>()
            };

        private string BuildActionChecklistHtml(string disableReason, EmailMessage emailMessage)
        {
            var bodyKey = $"SyncDisabledFallback.ActionChecklist.{disableReason}.Body";
            var body = _localizationRepository.TranslateSetting(bodyKey, GetActionChecklistArgs(disableReason, emailMessage));
            if (string.IsNullOrWhiteSpace(body) || body == bodyKey)
                return string.Empty;

            string deadlineSpan = string.Empty;
            var pausedIdx = GetPausedAtIndex(disableReason);
            if (pausedIdx >= 0)
            {
                var pausedAtUtc = TryParseIsoUtc(GetParam(emailMessage, pausedIdx));
                if (pausedAtUtc.HasValue)
                {
                    var deadline = ConvertToPacific(pausedAtUtc.Value).AddDays(ActionByDays);
                    var formatted = deadline.ToString("ddd, MMM d, yyyy", CultureInfo.InvariantCulture);
                    deadlineSpan = "&middot; by " + System.Net.WebUtility.HtmlEncode(formatted);
                }
            }

            var title = _localizationRepository.TranslateSetting("SyncDisabledFallback.ActionChecklist.Title");
            var checklistHtml = string.Format(
                HtmlTemplates.OrangeActionChecklistHtml,
                System.Net.WebUtility.HtmlEncode(title),
                deadlineSpan,
                RenderActionChecklistBody(body));

            return checklistHtml + BuildActionChecklistFootNoteHtml(
                $"SyncDisabledFallback.ActionChecklist.{disableReason}.FootNote");
        }

        // Italic footnote paragraph rendered immediately after the orange action-checklist box.
        // Used to surface advisory text (e.g. "If guest users are required, they'll need to be
        // managed outside of GMM.") that the reference design places outside the box.
        private string BuildActionChecklistFootNoteHtml(string footNoteKey)
        {
            var footNote = _localizationRepository.TranslateSetting(footNoteKey);
            if (string.IsNullOrWhiteSpace(footNote) || footNote == footNoteKey)
                return string.Empty;
            return "<tr><td style=\"padding:0 24px 12px;font-size:13.5px;line-height:1.55;font-style:italic;color:#6b6b6b;\">"
                + ConvertContentToHtml(footNote)
                + "</td></tr>";
        }

        // Render the action-checklist body. If the body's lines start with "1. ", "2. ", ...
        // (matching the markdown ordered-list convention in resx), emit a real <ol><li> block
        // so the steps render as a numbered list. Otherwise fall back to the standard
        // markdown-to-HTML conversion (which supports **bold**, [text](url), and \n -> <br>).
        private static string RenderActionChecklistBody(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return string.Empty;

            var lines = body.Replace("\r\n", "\n").Split('\n');
            var itemRegex = new Regex(@"^\s*\d+\.\s+(.+)$");
            if (lines.Length > 1 && lines.All(l => string.IsNullOrWhiteSpace(l) || itemRegex.IsMatch(l)))
            {
                var sb = new StringBuilder();
                sb.Append("<ol style=\"margin:0;padding-left:20px;color:#4A3100;\">");
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var match = itemRegex.Match(line);
                    sb.Append("<li style=\"margin:4px 0;\">")
                      .Append(ConvertContentToHtml(match.Groups[1].Value))
                      .Append("</li>");
                }
                sb.Append("</ol>");
                return sb.ToString();
            }

            return ConvertContentToHtml(body);
        }

        private async Task<(string alias, string type)> FetchGroupMetaAsync(string groupId)
        {
            string groupAlias = string.Empty;
            string groupType = string.Empty;
            try
            {
                if (Guid.TryParse(groupId, out Guid gid))
                {
                    var email = await _graphGroupRepository.GetGroupEmailAsync(gid);
                    if (!string.IsNullOrEmpty(email))
                        groupAlias = email;

                    var endpoints = await _graphGroupRepository.GetGroupEndpointsAsync(gid);
                    if (endpoints.Contains("Outlook"))
                    {
                        var vivaEngageUrl = await _graphGroupRepository.GetGroupVivaEngageUrlAsync(gid);
                        if (!string.IsNullOrEmpty(vivaEngageUrl))
                            groupType = _localizationRepository.TranslateSetting("FallbackGroupType.M365GroupVivaEngage");
                        else if (endpoints.Any(e => string.Equals(e, "MicrosoftTeams", StringComparison.OrdinalIgnoreCase)))
                            groupType = _localizationRepository.TranslateSetting("FallbackGroupType.M365GroupTeams");
                        else
                            groupType = _localizationRepository.TranslateSetting("FallbackGroupType.M365Group");
                    }
                    else if (endpoints.Contains("SecurityGroup"))
                    {
                        groupType = _localizationRepository.TranslateSetting("FallbackGroupType.SecurityGroup");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch group metadata for {GroupId}; alias and type rows will be omitted.", groupId);
            }
            return (groupAlias, groupType);
        }

        private string GetDisableReason(string? contentType)
        {
            if (string.IsNullOrEmpty(contentType))
                return "Generic";

            return contentType switch
            {
                "SyncDisabledNoGroupEmailBody" => "NoDestinationGroup",
                "SyncDisabledNoSourceGroupEmailBody" => "NoSourceGroup",
                "SyncDisabledNoOwnerEmailBody" => "NoOwner",
                "GuestUserFailureEmailBody" => "GuestUsers",
                "NestedGroupsFoundEmailBody" => "NestedGroupsFound",
                "SyncPurgedForInactivityEmailBody" => "PurgedForInactivity",
                "NoDataEmailContent" => "NoData",
                "SyncJobDisabledEmailBody" => "Threshold",
                _ => "Generic"
            };
        }

        private StringBuilder BuildCommonRows(string groupId, string groupAlias, string groupType, string requestor)
        {
            Func<string, string> encode = System.Net.WebUtility.HtmlEncode;
            var rows = new StringBuilder();
            // Always render Group Alias for consistency across all fallback templates;
            // fall back to "N/A" when the destination has no mail (e.g. security groups).
            rows.Append(string.Format(HtmlTemplates.DetailsTableRow,
                _localizationRepository.TranslateSetting("FallbackDetailsRow.GroupAlias"),
                string.IsNullOrEmpty(groupAlias) ? "N/A" : encode(groupAlias), ""));
            if (!string.IsNullOrEmpty(groupType))
            {
                rows.Append(string.Format(HtmlTemplates.DetailsTableRow,
                    _localizationRepository.TranslateSetting("FallbackDetailsRow.GroupType"),
                    encode(groupType), ""));
            }
            rows.Append(string.Format(HtmlTemplates.DetailsTableRow,
                _localizationRepository.TranslateSetting("FallbackDetailsRow.ObjectId"),
                encode(groupId), ""));
            if (!string.IsNullOrEmpty(requestor))
            {
                rows.Append(string.Format(HtmlTemplates.DetailsTableRow,
                    _localizationRepository.TranslateSetting("FallbackDetailsRow.RequestedBy"),
                    encode(requestor), ""));
            }
            return rows;
        }

        private static string SanitizeUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return string.Empty;
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                return url;
            return string.Empty;
        }

        private static string ConvertContentToHtml(string content)
        {
            if (string.IsNullOrEmpty(content))
                return string.Empty;

            var html = System.Net.WebUtility.HtmlEncode(content);

            // Strip empty bold pairs (e.g. **{0}** where {0}="" produces ****, or stray ** **).
            // This protects the bold-pair parser below from being misaligned by empty placeholders,
            // which otherwise causes adjacent **bold** spans to render incorrectly and trailing ** to leak.
            // Narrowed from `\*{4,}` so that legitimate runs of asterisks in user-supplied content are preserved.
            html = Regex.Replace(html, @"\*\*\s*\*\*", "");

            // Convert **bold** to <strong>
            html = Regex.Replace(html, @"\*\*(.+?)\*\*", "<strong>$1</strong>");

            // Convert [text](url) to <a href="url">text</a>, but only allow http/https/mailto schemes
            html = Regex.Replace(html, @"\[(.+?)\]\((.+?)\)", match =>
            {
                var text = match.Groups[1].Value;
                var url = System.Net.WebUtility.HtmlDecode(match.Groups[2].Value);
                if (Uri.TryCreate(url, UriKind.Absolute, out Uri uri) &&
                    (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeMailto))
                {
                    return $"<a href=\"{System.Net.WebUtility.HtmlEncode(url)}\" style=\"color:#0078d4;text-decoration:underline;\">{text}</a>";
                }
                return text;
            });

            // Convert newlines to <br>
            html = html.Replace("\r\n", "<br>").Replace("\n", "<br>");

            return html;
        }
    }
}
