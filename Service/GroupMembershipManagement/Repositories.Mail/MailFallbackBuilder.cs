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

        // SyncDisabled compact-detail reasons share the same layout: 2-3 row details table
        // (Group Email/Type when available, Paused At), suppressed requestor row, and the orange
        // "What to do" action-checklist with a PausedAt + NumberOfDaysBeforePurging deadline.
        // Add a reason here to opt into the shared rendering; per-reason knob is GetPausedAtIndex.
        private static readonly HashSet<string> _compactDetailReasons =
            new HashSet<string>(StringComparer.Ordinal) { "NoDestinationGroup", "NoSourceGroup", "NoOwner", "NoData", "GuestUsers", "NestedGroupsFound" };

        private static int GetPausedAtIndex(string disableReason) => disableReason switch
        {
            "NoDestinationGroup" => NoDestinationGroupPausedAtIndex,
            "NoSourceGroup" => NoSourceGroupPausedAtIndex,
            "NoOwner" => NoOwnerPausedAtIndex,
            "NoData" => NoDataPausedAtIndex,
            "GuestUsers" => GuestUsersPausedAtIndex,
            "NestedGroupsFound" => NestedGroupsFoundPausedAtIndex,
            _ => -1
        };

        // JobPurgingWarning AdditionalContentParams indices (set by AzureMaintenanceService.SendWarningEmailAsync):
        // [0]=Status, [1]=InactivitySince, [2]=NumberOfDaysBeforePurging, [3]=ScheduledPurgeDate, [4]=GroupId, [5]=GroupName
        private const int PurgeWarningStatusIndex = 0;
        private const int PurgeWarningInactiveSinceIndex = 1;
        private const int PurgeWarningDaysBeforePurgingIndex = 2;
        private const int PurgeWarningScheduledPurgeDateIndex = 3;
        private const int PurgeWarningGroupNameIndex = 5;

        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly ILocalizationRepository _localizationRepository;
        private readonly ILogger<MailFallbackBuilder> _logger;
        private readonly IHandleInactiveJobsConfig _handleInactiveJobsConfig;

        public MailFallbackBuilder(
            IGraphGroupRepository graphGroupRepository,
            ILocalizationRepository localizationRepository,
            ILogger<MailFallbackBuilder> logger,
            IHandleInactiveJobsConfig handleInactiveJobsConfig = null,
            int nestedGroupsDisplayLimit = DefaultNestedGroupsDisplayLimit)
        {
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _localizationRepository = localizationRepository ?? throw new ArgumentNullException(nameof(localizationRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _handleInactiveJobsConfig = handleInactiveJobsConfig;
            _nestedGroupsDisplayLimit = nestedGroupsDisplayLimit > 0 ? nestedGroupsDisplayLimit : DefaultNestedGroupsDisplayLimit;
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
                headerText: destinationGroupName ?? string.Empty,
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
                headerText: destinationGroupName ?? string.Empty,
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

            var description = _localizationRepository.TranslateSetting(
                $"SyncDisabledFallback.Description.{disableReason}",
                string.Empty, groupId ?? string.Empty, gmmOwnerName, nestedGroupsCount, addedCount, removedCount);

            return FormatTemplate(
                HtmlTemplates.SyncDisabledTemplate,
                prefix: "SyncDisabledFallback",
                groupName: disableReason == "NoDestinationGroup" && !string.IsNullOrWhiteSpace(destinationGroupName)
                    ? _localizationRepository.TranslateSetting("SyncDisabledFallback.PreviouslyNamedPrefix") + destinationGroupName
                    : destinationGroupName,
                headerText: _localizationRepository.TranslateSetting($"SyncDisabledFallback.HeaderReason.{disableReason}"),
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
                extraCalloutHtml: BuildNestedGroupsCalloutHtml(disableReason, emailMessage)
            );
        }

        public async Task<string> BuildSubmissionRejectedFallbackAsync(
            EmailMessage emailMessage, string destinationGroupName, string groupId, string jobUrl, string sentDate)
        {
            _logger.LogInformation(
                "Building SubmissionRejected fallback HTML for group {GroupId} ({GroupName}).", groupId, destinationGroupName);

            var rejectionReason = GetParam(emailMessage, RejectionReasonIndex);
            var requestor       = GetParam(emailMessage, RejectionRequestorIndex);

            var rows = await BuildBaseRowsAsync(groupId, requestor);

            return FormatTemplate(
                HtmlTemplates.SubmissionRejectedTemplate,
                prefix: "SubmissionRejectedFallback",
                groupName: destinationGroupName,
                headerText: destinationGroupName ?? string.Empty,
                description: _localizationRepository.TranslateSetting("SubmissionRejectedFallback.Description"),
                calloutBody: _localizationRepository.TranslateSetting("SubmissionRejectedFallback.CalloutBody", string.IsNullOrWhiteSpace(rejectionReason) ? "(not provided)" : rejectionReason),
                rows: rows,
                jobUrl: jobUrl,
                sentDate: sentDate
            );
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
            if (string.IsNullOrWhiteSpace(groupName))
            {
                groupName = _localizationRepository.TranslateSetting("FallbackUnknownGroupName");
            }

            var statusKey = ResolvePurgeWarningStatusKey(status);

            // Status-aware description and callout body. Uses positional tokens consistent with
            // the existing JobPurgingWarningEmailBody resource: {0}=Status, {1}=InactiveSince,
            // {2}=NumberOfDaysBeforePurging, {3}=ScheduledPurgeDate, {5}=GroupName.
            var description = _localizationRepository.TranslateSetting(
                $"JobPurgingWarningFallback.Description.{statusKey}",
                status, inactiveSince, daysBeforePurging, scheduledPurgeDate, string.Empty, groupName);
            var calloutBody = _localizationRepository.TranslateSetting(
                $"JobPurgingWarningFallback.CalloutBody.{statusKey}");

            var rows = await BuildBaseRowsAsync(groupId, requestor: string.Empty);
            if (!string.IsNullOrWhiteSpace(status))
            {
                rows.Append(string.Format(HtmlTemplates.DetailsTableRow,
                    _localizationRepository.TranslateSetting("FallbackDetailsRow.Status"),
                    System.Net.WebUtility.HtmlEncode(status),
                    "font-weight:600;color:#603900;"));
            }
            if (!string.IsNullOrWhiteSpace(inactiveSince))
            {
                rows.Append(string.Format(HtmlTemplates.DetailsTableRow,
                    _localizationRepository.TranslateSetting("FallbackDetailsRow.InactiveSince"),
                    System.Net.WebUtility.HtmlEncode(inactiveSince), ""));
            }
            if (!string.IsNullOrWhiteSpace(scheduledPurgeDate))
            {
                rows.Append(string.Format(HtmlTemplates.DetailsTableRow,
                    _localizationRepository.TranslateSetting("FallbackDetailsRow.ScheduledPurgeDate"),
                    System.Net.WebUtility.HtmlEncode(scheduledPurgeDate),
                    "font-weight:600;color:#603900;"));
            }

            return FormatTemplate(
                HtmlTemplates.JobPurgingWarningTemplate,
                prefix: "JobPurgingWarningFallback",
                groupName: destinationGroupName,
                headerText: _localizationRepository.TranslateSetting("JobPurgingWarningFallback.HeaderReason"),
                description: description,
                calloutBody: calloutBody,
                rows: rows,
                jobUrl: jobUrl,
                sentDate: sentDate
            );
        }

        // Normalizes the raw status string from the email message into the canonical PascalCase
        // SyncStatus name used in resx keys (JobPurgingWarningFallback.Description.{Status} /
        // .CalloutBody.{Status}). Round-tripping through the enum protects against case-mismatch
        // resx misses for inputs like "customerpaused".
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
            string extraCalloutHtml = "")
        {
            var name = string.IsNullOrWhiteSpace(groupName) ? "N/A" : groupName;
            return string.Format(
                template,
                _localizationRepository.TranslateSetting($"{prefix}.Badge"),             // {0} badge
                System.Net.WebUtility.HtmlEncode(headerText),                                // {1} header text (varies)
                System.Net.WebUtility.HtmlEncode(name),                                  // {2} title
                ConvertContentToHtml(description),                                        // {3} description
                rows.ToString(),                                                          // {4} details table rows
                _localizationRepository.TranslateSetting($"{prefix}.CalloutTitle"),      // {5} callout title
                ConvertContentToHtml(calloutBody),                                        // {6} callout body
                _localizationRepository.TranslateSetting($"{prefix}.CtaLabel"),          // {7} CTA label
                System.Net.WebUtility.HtmlEncode(SanitizeUrl(jobUrl)),                   // {8} CTA url
                ConvertContentToHtml(_localizationRepository.TranslateSetting($"{prefix}.FooterExplanation", name)), // {9} footer
                sentDate,                                                                 // {10} sent date
                actionChecklistHtml ?? string.Empty,                                      // {11} optional action checklist row
                extraCalloutHtml ?? string.Empty                                          // {12} optional extra gray callout (e.g. nested groups)
            );
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
            return string.Format(
                HtmlTemplates.OrangeActionChecklistHtml,
                System.Net.WebUtility.HtmlEncode(title),
                deadlineSpan,
                RenderActionChecklistBody(body));
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

            // Map email content types to disable reason keys for context-specific descriptions
            return contentType switch
            {
                "SyncThresholdBothEmailBody" => "Threshold",
                "SyncDisabledNoGroupEmailBody" => "NoDestinationGroup",
                "SyncDisabledNoSourceGroupEmailBody" => "NoSourceGroup",
                "SyncDisabledNoOwnerEmailBody" => "NoOwner",
                "SyncDisabledNoValidGroupIds" => "NotValidSource",
                "GuestUserFailureEmailBody" => "GuestUsers",
                "NestedGroupsFoundEmailBody" => "NestedGroupsFound",
                "SyncPurgedForInactivityEmailBody" => "PurgedForInactivity",
                "NoDataEmailContent" => "NoData",
                "SyncJobDisabledEmailBody" => "Generic",
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

            // Convert [text](url) to <a href="url">text</a>, but only allow http/https schemes
            html = Regex.Replace(html, @"\[(.+?)\]\((.+?)\)", match =>
            {
                var text = match.Groups[1].Value;
                var url = System.Net.WebUtility.HtmlDecode(match.Groups[2].Value);
                if (Uri.TryCreate(url, UriKind.Absolute, out Uri uri) &&
                    (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
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
