// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts;
using System;
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

        public MailFallbackBuilder(
            IGraphGroupRepository graphGroupRepository,
            ILocalizationRepository localizationRepository,
            ILogger<MailFallbackBuilder> logger)
        {
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _localizationRepository = localizationRepository ?? throw new ArgumentNullException(nameof(localizationRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
                description: _localizationRepository.TranslateSetting("SyncCompletedFallback.Description", destinationGroupName ?? string.Empty, groupId ?? string.Empty),
                calloutBody: _localizationRepository.TranslateSetting("SyncCompletedFallback.CalloutBody", addedCount, removedCount, destinationGroupName ?? string.Empty),
                rows: rows,
                jobUrl: jobUrl,
                sentDate: sentDate
            );
        }

        public async Task<string> BuildSyncDisabledFallbackAsync(
            EmailMessage emailMessage, string destinationGroupName, string groupId, string jobUrl, string sentDate)
        {
            var requestor    = GetParam(emailMessage, RequestorIndex);
            var rows         = await BuildBaseRowsAsync(groupId, requestor);
            var disableReason = GetDisableReason(emailMessage.Content);

            return FormatTemplate(
                HtmlTemplates.SyncDisabledTemplate,
                prefix: "SyncDisabledFallback",
                groupName: destinationGroupName,
                headerText: _localizationRepository.TranslateSetting($"SyncDisabledFallback.HeaderReason.{disableReason}"),
                description: _localizationRepository.TranslateSetting($"SyncDisabledFallback.Description.{disableReason}", requestor, groupId ?? string.Empty),
                calloutBody: _localizationRepository.TranslateSetting($"SyncDisabledFallback.CalloutBody.{disableReason}"),
                rows: rows,
                jobUrl: jobUrl,
                sentDate: sentDate
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
            StringBuilder rows, string jobUrl, string sentDate)
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
                sentDate                                                                  // {10} sent date
            );
        }

        private static string GetParam(EmailMessage emailMessage, int index, string defaultValue = "")
        {
            return emailMessage.AdditionalContentParams?.Length > index
                ? emailMessage.AdditionalContentParams[index]
                : defaultValue;
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
                        groupType = !string.IsNullOrEmpty(vivaEngageUrl)
                            ? _localizationRepository.TranslateSetting("FallbackGroupType.M365GroupVivaEngage")
                            : _localizationRepository.TranslateSetting("FallbackGroupType.M365Group");
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
            if (!string.IsNullOrEmpty(groupAlias))
            {
                rows.Append(string.Format(HtmlTemplates.DetailsTableRow,
                    _localizationRepository.TranslateSetting("FallbackDetailsRow.GroupAlias"),
                    encode(groupAlias), ""));
            }
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
