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
            var groupName = destinationGroupName ?? string.Empty;
            var requestor = emailMessage.AdditionalContentParams?.Length > RequestorIndex
                ? emailMessage.AdditionalContentParams[RequestorIndex]
                : string.Empty;

            var (groupAlias, groupType) = await FetchGroupMetaAsync(groupId);
            var rows = BuildCommonRows(groupId, groupAlias, groupType, requestor);

            return string.Format(
                HtmlTemplates.SyncStartedTemplate,
                _localizationRepository.TranslateSetting("SyncStartedFallback.Badge"),                                                         // {0} badge
                System.Net.WebUtility.HtmlEncode(groupName),                                                                                    // {1} group name
                System.Net.WebUtility.HtmlEncode(groupName),                                                                                    // {2} title
                ConvertContentToHtml(_localizationRepository.TranslateSetting("SyncStartedFallback.Description", requestor)),                   // {3} description
                rows.ToString(),                                                                                                                 // {4} details table rows
                _localizationRepository.TranslateSetting("SyncStartedFallback.CalloutTitle"),                                                   // {5} callout title
                ConvertContentToHtml(_localizationRepository.TranslateSetting("SyncStartedFallback.CalloutBody")),                              // {6} callout body
                _localizationRepository.TranslateSetting("SyncStartedFallback.CtaLabel"),                                                       // {7} CTA label
                System.Net.WebUtility.HtmlEncode(SanitizeUrl(jobUrl)),                                                                          // {8} CTA url
                ConvertContentToHtml(_localizationRepository.TranslateSetting("SyncStartedFallback.FooterExplanation", groupName)),             // {9} footer explanation
                sentDate                                                                                                                         // {10} sent date
            );
        }

        public async Task<string> BuildSyncCompletedFallbackAsync(
            EmailMessage emailMessage, string destinationGroupName, string groupId, string jobUrl, string sentDate)
        {
            var groupName = destinationGroupName ?? string.Empty;
            var addedCount = emailMessage.AdditionalContentParams?.Length > AddedCountIndex
                ? emailMessage.AdditionalContentParams[AddedCountIndex]
                : "0";
            var removedCount = emailMessage.AdditionalContentParams?.Length > RemovedCountIndex
                ? emailMessage.AdditionalContentParams[RemovedCountIndex]
                : "0";
            var requestor = emailMessage.AdditionalContentParams?.Length > RequestorIndex
                ? emailMessage.AdditionalContentParams[RequestorIndex]
                : string.Empty;

            var (groupAlias, groupType) = await FetchGroupMetaAsync(groupId);
            var rows = BuildCommonRows(groupId, groupAlias, groupType, requestor);
            rows.Append(string.Format(HtmlTemplates.DetailsTableRow,
                _localizationRepository.TranslateSetting("FallbackDetailsRow.MembersAdded"),
                System.Net.WebUtility.HtmlEncode(addedCount),
                "font-weight:600;color:#107c10;"));
            rows.Append(string.Format(HtmlTemplates.DetailsTableRow,
                _localizationRepository.TranslateSetting("FallbackDetailsRow.MembersRemoved"),
                System.Net.WebUtility.HtmlEncode(removedCount),
                "font-weight:600;color:#a4262c;"));

            return string.Format(
                HtmlTemplates.SyncCompletedTemplate,
                _localizationRepository.TranslateSetting("SyncCompletedFallback.Badge"),                                                                        // {0} badge
                System.Net.WebUtility.HtmlEncode(groupName),                                                                                                    // {1} group name
                System.Net.WebUtility.HtmlEncode(groupName),                                                                                                    // {2} title
                ConvertContentToHtml(_localizationRepository.TranslateSetting("SyncCompletedFallback.Description")),                                            // {3} description
                rows.ToString(),                                                                                                                                 // {4} details table rows
                _localizationRepository.TranslateSetting("SyncCompletedFallback.CalloutTitle"),                                                                 // {5} callout title
                ConvertContentToHtml(_localizationRepository.TranslateSetting("SyncCompletedFallback.CalloutBody", addedCount, removedCount, groupName)),       // {6} callout body
                _localizationRepository.TranslateSetting("SyncCompletedFallback.CtaLabel"),                                                                     // {7} CTA label
                System.Net.WebUtility.HtmlEncode(SanitizeUrl(jobUrl)),                                                                                       // {8} CTA url
                ConvertContentToHtml(_localizationRepository.TranslateSetting("SyncCompletedFallback.FooterExplanation", groupName)),                           // {9} footer explanation
                sentDate                                                                                                                                         // {10} sent date
            );
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
                encode(groupId), "font-family:Consolas,'Courier New',monospace;font-size:13px;"));
            rows.Append(string.Format(HtmlTemplates.DetailsTableRow,
                _localizationRepository.TranslateSetting("FallbackDetailsRow.RequestedBy"),
                encode(requestor), ""));
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
