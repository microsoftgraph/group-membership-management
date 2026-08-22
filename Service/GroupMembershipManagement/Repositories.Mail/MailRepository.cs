// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions;
using Models;
using Models.Helpers;
using Polly.Wrap;
using Repositories.Contracts;
using Repositories.Contracts.Constants;
using Repositories.Contracts.Helpers;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Repositories.Mail
{
    public class MailRepository : IMailRepository
    {
        private readonly IMailConfig _mailConfig;
        private readonly ILocalizationRepository _localizationRepository;
        private readonly GraphServiceClient _graphClient;
        private readonly ILogger<MailRepository> _mailRepositoryLogger;
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly IDatabaseSettingsRepository _settingsRepository;
        private readonly IRetryPolicyProvider _retryPolicyProvider;
        private readonly TelemetryClient _telemetryClient;
        private readonly IMailFallbackBuilder _mailFallbackBuilder;

        public MailRepository(
            GraphServiceClient graphClient, 
            IMailConfig mailAdaptiveCardConfig, 
            ILocalizationRepository localizationRepository, 
            ILogger<MailRepository> mailRepositoryLogger,
            IGraphGroupRepository graphGroupRepository,
            IDatabaseSettingsRepository settingsRepository,
            IRetryPolicyProvider retryPolicyProvider,
            TelemetryClient telemetryClient,
            IMailFallbackBuilder mailFallbackBuilder
            )
        {
            _graphClient = graphClient ?? throw new ArgumentNullException(nameof(graphClient));
            _mailConfig = mailAdaptiveCardConfig ?? throw new ArgumentNullException(nameof(mailAdaptiveCardConfig));
            _localizationRepository = localizationRepository ?? throw new ArgumentNullException(nameof(localizationRepository));
            _mailRepositoryLogger = mailRepositoryLogger ?? throw new ArgumentNullException(nameof(mailRepositoryLogger));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
            _retryPolicyProvider = retryPolicyProvider ?? throw new ArgumentNullException(nameof(retryPolicyProvider));
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
            _mailFallbackBuilder = mailFallbackBuilder ?? throw new ArgumentNullException(nameof(mailFallbackBuilder));
        }

        public async Task<HttpResponseMessage> SendMailAsync(EmailMessage emailMessage, Guid? runId)
        {
            if (_mailConfig.SkipEmailNotifications)
            {
                _mailRepositoryLogger.LogInformationWithRunId(runId, "Email notifications are disabled.");

                return new HttpResponseMessage(System.Net.HttpStatusCode.Accepted)
                {
                    ReasonPhrase = "Email notifications are disabled."
                };
            }

            if (emailMessage is null)
            {
                throw new ArgumentNullException(nameof(emailMessage));
            }

            await TryAssignGroupNameAsync(emailMessage, runId);

            Message message;

            if (emailMessage.IsHTML)
            {
                message = GetHTMLMessage(emailMessage);
            }
            else if (_mailConfig.IsAdaptiveCardEnabled)
            {
                message = await GetAdaptiveCardMessage(emailMessage);
            }
            else
            {
                message = GetSimpleMessage(emailMessage);
            }

            if (!string.IsNullOrEmpty(emailMessage?.ToEmailAddresses))
                message.ToRecipients = GetEmailAddresses(emailMessage?.ToEmailAddresses);

            if (!string.IsNullOrEmpty(emailMessage?.CcEmailAddresses))
                message.CcRecipients = GetEmailAddresses(emailMessage?.CcEmailAddresses);

            var securePassword = new SecureString();
            foreach (char c in emailMessage?.SenderPassword)
                securePassword.AppendChar(c);

            HttpResponseMessage httpResponse = null;

            try
            {
                var executionPolicy = GetHttpResponseMessageRetryPolicy(runId);

                httpResponse = await executionPolicy.ExecuteAsync(async () =>
                {
                    var nativeResponseHandler = new NativeResponseHandler();
                    if (_mailConfig.GMMHasSendMailApplicationPermissions)
                    {
                        var body = new Microsoft.Graph.Users.Item.SendMail.SendMailPostRequestBody
                        {
                            Message = message,
                            SaveToSentItems = true
                        };

                        await _graphClient.Users[_mailConfig.SenderAddress].SendMail.PostAsync(body, config =>
                        {
                            config.Options.Add(new ResponseHandlerOption { ResponseHandler = nativeResponseHandler });
                        });
                        return nativeResponseHandler.Value as HttpResponseMessage;
                    }
                    else
                    {
                        var body = new Microsoft.Graph.Me.SendMail.SendMailPostRequestBody
                        {
                            Message = message,
                            SaveToSentItems = true
                        };

                        await _graphClient.Me.SendMail.PostAsync(body, config =>
                        {
                            config.Options.Add(new ResponseHandlerOption { ResponseHandler = nativeResponseHandler });
                        });
                        return nativeResponseHandler.Value as HttpResponseMessage;
                    }
                });
            }
            catch (ServiceException ex) when (ex.GetBaseException().GetType().Name == "MsalUiRequiredException")
            {
                _mailRepositoryLogger.LogInformationWithRunId(runId, "Email cannot be sent because Mail.Send permission has not been granted.");
            }
            catch (ServiceException ex) when (ex.Message.Contains("MailboxNotEnabledForRESTAPI"))
            {
                _mailRepositoryLogger.LogInformationWithRunId(runId, "Email cannot be sent because required licenses are missing in the service account.");
            }
            catch (Exception ex)
            {
                _mailRepositoryLogger.LogErrorWithRunId(runId, $"Email cannot be sent due to an unexpected exception.\n{ex}", ex);
            }
            if (httpResponse != null)
            {
                await GraphTelemetryHelper.TrackResourceUnitsAsync(httpResponse, QueryType.Other, runId, _mailRepositoryLogger, _telemetryClient);
            }
            if (httpResponse == null)
            {
                string errorMessage = "Failed to send email due to an unexpected exception.";
                _mailRepositoryLogger.LogInformationWithRunId(runId, errorMessage);
                return new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError)
                {
                    ReasonPhrase = errorMessage
                };
            }
            if (httpResponse.IsSuccessStatusCode)
            {
                _mailRepositoryLogger.LogInformationWithRunId(runId, "Email sent successfully.");
            }
            else
            {
                _mailRepositoryLogger.LogInformationWithRunId(runId, $"Failed to send email: {httpResponse.StatusCode} - {await httpResponse.Content.ReadAsStringAsync()}");
            }
            return httpResponse;
        }

        private Message GetHTMLMessage(EmailMessage emailMessage)
        {
            var message = GetSimpleMessage(emailMessage);
            message.Body.ContentType = BodyType.Html;
            return message;
        }

        public async Task<Message> GetAdaptiveCardMessage(EmailMessage emailMessage)
        {
            var subjectContent = _localizationRepository.TranslateSetting(emailMessage?.Subject, emailMessage?.AdditionalContentParams);

            // Most notification types put the destination GroupId at AdditionalContentParams[0],
            // but JobPurgingWarning (AzureMaintenanceService.SendWarningEmailAsync) puts the
            // SyncStatus at [0] and the GroupId at [4]. Pick the right index per type so the
            // styled fallback renders the correct OBJECT ID and resolves the destination group.
            string groupId = GetParamSafe(emailMessage, GetGroupIdIndex(emailMessage?.Content));
            string destinationGroupName = string.IsNullOrEmpty(emailMessage?.DestinationGroupName) ? "" : emailMessage.DestinationGroupName;
            var urlSetting = await _settingsRepository.GetSettingByKeyAsync(SettingKey.UIUrl);

            string UIUrl = urlSetting?.SettingValue ?? "";
            string jobUrl = UiUrlBuilder.BuildJobDetailsUrl(UIUrl, emailMessage.SyncJobId);
            string historyUrl = UiUrlBuilder.BuildJobDetailsUrl(UIUrl, emailMessage.SyncJobId, includeHistory: true);
            string onboardingUrl = UiUrlBuilder.BuildOnboardingUrl(UIUrl);

            var sentDate = DateTime.UtcNow.ToString("MMM dd, yyyy");
            string htmlContent;

            if (_mailConfig.EnableStyledFallbackEmails)
            {
                var styledContext = new StyledEmailContext(
                    GroupId: groupId,
                    DestinationGroupName: ResolveStyledDestinationGroupName(emailMessage, destinationGroupName),
                    UIUrl: UIUrl,
                    JobUrl: jobUrl,
                    HistoryUrl: historyUrl,
                    OnboardingUrl: onboardingUrl,
                    SentDate: sentDate);

                var styledFallback = await TryBuildStyledBodyAsync(emailMessage, styledContext);

                if (styledFallback != null)
                {
                    // Styled informational email. Owners act via the in-body deep-link CTAs
                    // (e.g. "Review in GMM" / run history) baked into the styled template.
                    htmlContent = WrapStyledBodyWithoutAdaptiveCard(styledFallback, styledContext.DestinationGroupName);
                }
                else
                {
                    // No styled template for this type yet: send the message as a plain HTML body.
                    htmlContent = BuildPlainFallback(emailMessage);
                }
            }
            else
            {
                // Styled emails disabled: send every notification as a plain HTML body.
                htmlContent = BuildPlainFallback(emailMessage);
            }

            var message = new Message
            {
                Subject = subjectContent,
                Body = new ItemBody
                {
                    ContentType = BodyType.Html,
                    Content = htmlContent
                }
            };
            return message;
        }

        public Message GetSimpleMessage(EmailMessage emailMessage)
        {
            var messageContent = _localizationRepository.TranslateSetting(emailMessage?.Content, emailMessage?.AdditionalContentParams);
            messageContent = messageContent.Replace("**", "");
            var simpleLinkPattern = @"\[.*?\]\((.*?)\)";
            messageContent = Regex.Replace(messageContent, simpleLinkPattern, "$1");

            var message = new Message
            {
                Subject = _localizationRepository.TranslateSetting(emailMessage?.Subject, emailMessage?.AdditionalSubjectParams),
                Body = new ItemBody
                {
                    ContentType = BodyType.Text,
                    Content = messageContent
                }
            };

            if (!string.IsNullOrEmpty(emailMessage?.ToEmailAddresses))
                message.ToRecipients = GetEmailAddresses(emailMessage?.ToEmailAddresses);

            if (!string.IsNullOrEmpty(emailMessage?.CcEmailAddresses))
                message.CcRecipients = GetEmailAddresses(emailMessage?.CcEmailAddresses);

            return message;
        }

        public List<Recipient> GetEmailAddresses(string emailAddresses)
        {
            return emailAddresses.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(address => address.Trim()).ToList()
                                     .Select(address => new Recipient() { EmailAddress = new EmailAddress { Address = address } })
                                     .ToList();
        }

        private async Task TryAssignGroupNameAsync(EmailMessage emailMessage, Guid? runId)
        {
            if (emailMessage?.AdditionalContentParams == null || !emailMessage.AdditionalContentParams.Any())
            {
                return;
            }

            var groupIdIndex = GetGroupIdIndex(emailMessage.Content);
            var rawGroupId = GetParamSafe(emailMessage, groupIdIndex);

            if (Guid.TryParse(rawGroupId, out Guid groupId))
            {
                _mailRepositoryLogger.LogInformationWithRunId(runId, $"Successfully parsed group ID: {groupId}");

                string groupName = await _graphGroupRepository.GetGroupNameAsync(groupId);
                if (!string.IsNullOrEmpty(groupName))
                {
                    emailMessage.DestinationGroupName = groupName;
                }
            }
            else
            {
                _mailRepositoryLogger.LogInformationWithRunId(runId, $"The provided value '{rawGroupId}' is not a valid GUID.");
            }
        }

        // Most notification types put GroupId at AdditionalContentParams[0]; JobPurgingWarning
        // puts it at [4]; SyncJobDisabledEmailBody (threshold) puts it at [1].
        private static int GetGroupIdIndex(string? content)
        {
            if (string.Equals(content, NotificationConstants.JobPurgingWarningEmailBody, StringComparison.OrdinalIgnoreCase))
                return 4;
            if (string.Equals(content, NotificationConstants.SyncJobDisabledEmailBody, StringComparison.OrdinalIgnoreCase))
                return 1;
            return 0;
        }

        private static string GetParamSafe(EmailMessage emailMessage, int index)
        {
            return emailMessage?.AdditionalContentParams != null && emailMessage.AdditionalContentParams.Length > index
                ? emailMessage.AdditionalContentParams[index]
                : string.Empty;
        }
        private AsyncPolicyWrap<HttpResponseMessage> GetHttpResponseMessageRetryPolicy(Guid? runId)
        {
            var retryAfterPolicy = _retryPolicyProvider.CreateRetryAfterPolicy(runId);
            var exceptionHandlingPolicy = _retryPolicyProvider.CreateExceptionHandlingPolicy(runId);

            return retryAfterPolicy.WrapAsync(exceptionHandlingPolicy);
        }

        // Builds the styled threshold HTML body used by NotifierService.SendThresholdEmailAsync.
        public async Task<string?> BuildStyledFallbackEmailHtmlAsync(EmailMessage emailMessage)
        {
            if (emailMessage is null)
            {
                throw new ArgumentNullException(nameof(emailMessage));
            }

            if (!_mailConfig.EnableStyledFallbackEmails)
            {
                return null;
            }

            await TryAssignGroupNameAsync(emailMessage, runId: null);

            string groupId = GetParamSafe(emailMessage, GetGroupIdIndex(emailMessage?.Content));
            string destinationGroupName = string.IsNullOrEmpty(emailMessage?.DestinationGroupName) ? "" : emailMessage.DestinationGroupName;

            var urlSetting = await _settingsRepository.GetSettingByKeyAsync(SettingKey.UIUrl);
            string UIUrl = urlSetting?.SettingValue ?? "";
            // "Review in GMM" deep-links straight to the job's Threshold Exceeded take-action dialog.
            string reviewUrl = UiUrlBuilder.BuildJobDetailsUrl(UIUrl, emailMessage.SyncJobId, includeHistory: true, takeAction: true);
            var sentDate = DateTime.UtcNow.ToString("MMM dd, yyyy");

            string styledFallback = null;
            if (IsSyncDisabledNotification(emailMessage?.Content))
                styledFallback = await _mailFallbackBuilder.BuildSyncDisabledFallbackAsync(emailMessage, destinationGroupName, groupId, reviewUrl, sentDate);

            if (string.IsNullOrEmpty(styledFallback))
            {
                return null;
            }

            return WrapStyledBodyWithoutAdaptiveCard(styledFallback, destinationGroupName);
        }

        // Context shared by every styled notification template: the resolved destination group
        // and the set of UI deep-links each template can use for its call-to-action button.
        private sealed record StyledEmailContext(
            string GroupId,
            string DestinationGroupName,
            string UIUrl,
            string JobUrl,
            string HistoryUrl,
            string OnboardingUrl,
            string SentDate);

        private static bool IsFinalNotice(string? content) =>
            string.Equals(content, NotificationConstants.SyncPurgedForInactivityEmailBody, StringComparison.OrdinalIgnoreCase);

        // DestinationNotExist emails describe a group that Graph can no longer resolve, so when no
        // live name was found fall back to the cached name captured in AdditionalContentParams[1].
        private static string ResolveStyledDestinationGroupName(EmailMessage emailMessage, string destinationGroupName)
        {
            if (!string.IsNullOrEmpty(destinationGroupName)
                || !string.Equals(emailMessage?.Content, NotificationConstants.DestinationNotExistContent, StringComparison.OrdinalIgnoreCase))
            {
                return destinationGroupName;
            }

            var cachedName = GetParamSafe(emailMessage, 1);
            return !string.IsNullOrEmpty(cachedName) && !string.Equals(cachedName, "NAME NOT FOUND", StringComparison.OrdinalIgnoreCase)
                ? cachedName
                : destinationGroupName;
        }

        // Returns the branded HTML body for notification types that have a styled template.
        // Types with no template return null and are rendered as plain HTML.
        // Order matters: IsSyncDisabledNotification is a broad substring match, so the more
        // specific content types above it must be tested first.
        private async Task<string?> TryBuildStyledBodyAsync(EmailMessage emailMessage, StyledEmailContext context)
        {
            var content = emailMessage?.Content;

            if (string.Equals(content, "SyncStartedEmailBody", StringComparison.OrdinalIgnoreCase))
                return await _mailFallbackBuilder.BuildSyncStartedFallbackAsync(emailMessage, context.DestinationGroupName, context.GroupId, context.HistoryUrl, context.SentDate);

            if (string.Equals(content, "SyncCompletedEmailBody", StringComparison.OrdinalIgnoreCase))
                return await _mailFallbackBuilder.BuildSyncCompletedFallbackAsync(emailMessage, context.DestinationGroupName, context.GroupId, context.HistoryUrl, context.SentDate);

            if (string.Equals(content, NotificationConstants.JobPurgingWarningEmailBody, StringComparison.OrdinalIgnoreCase))
            {
                // ThresholdExceeded purge warnings deep-link to the take-action dialog (like Sync Disabled); status is AdditionalContentParams[0].
                var purgeWarningStatus = GetParamSafe(emailMessage, 0);
                var purgeWarningCtaUrl = string.Equals(purgeWarningStatus, "ThresholdExceeded", StringComparison.OrdinalIgnoreCase)
                    ? UiUrlBuilder.BuildJobDetailsUrl(context.UIUrl, emailMessage.SyncJobId, includeHistory: true, takeAction: true)
                    : context.HistoryUrl;
                return await _mailFallbackBuilder.BuildJobPurgingWarningFallbackAsync(emailMessage, context.DestinationGroupName, context.GroupId, purgeWarningCtaUrl, context.SentDate);
            }

            if (IsFinalNotice(content))
            {
                // The sync job is already purged, so a /jobdetails deep-link would 404.
                // Send owners to the onboarding page instead, matching the "Start a new onboarding" CTA.
                return await _mailFallbackBuilder.BuildFinalNoticeFallbackAsync(emailMessage, context.DestinationGroupName, context.GroupId, context.OnboardingUrl, context.SentDate);
            }

            if (IsSyncDisabledNotification(content))
            {
                // Threshold-disabled emails deep-link "Review in GMM" to the take-action dialog; other reasons use history.
                var disabledCtaUrl = string.Equals(content, NotificationConstants.SyncJobDisabledEmailBody, StringComparison.OrdinalIgnoreCase)
                    ? UiUrlBuilder.BuildJobDetailsUrl(context.UIUrl, emailMessage.SyncJobId, includeHistory: true, takeAction: true)
                    : context.HistoryUrl;
                return await _mailFallbackBuilder.BuildSyncDisabledFallbackAsync(emailMessage, context.DestinationGroupName, context.GroupId, disabledCtaUrl, context.SentDate);
            }

            if (string.Equals(content, NotificationConstants.SubmissionRejectedEmailBody, StringComparison.OrdinalIgnoreCase))
                return await _mailFallbackBuilder.BuildSubmissionRejectedFallbackAsync(emailMessage, context.DestinationGroupName, context.GroupId, context.JobUrl, context.SentDate);

            return null;
        }

        private static string WrapStyledBodyWithoutAdaptiveCard(string body, string groupName) =>
            $@"<!DOCTYPE html>
<html lang=""en"">
<head>
  <meta http-equiv=""Content-Type"" content=""text/html; charset=utf-8"">
  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
  <title>{System.Net.WebUtility.HtmlEncode(groupName ?? string.Empty)}</title>
</head>
<body style=""margin:0;padding:0;background-color:#f3f2f1;-webkit-font-smoothing:antialiased;"">
{body}
</body>
</html>";

        // Notification types with no styled template are sent as a plain HTML body. The <pre>
        // wrapper preserves the line breaks of multi-line message bodies, such as the normal
        // threshold email's numbered "Reply All" option list.
        private string BuildPlainFallback(EmailMessage emailMessage)
        {
            var simpleMessage = GetSimpleMessage(emailMessage);

            var plainHtmlTemplate = @"<html>
                <head>
                  <meta http-equiv=""Content-Type"" content=""text/html; charset=utf-8"">
                </head>
                <body>
                <pre>{0}</pre>
                </body>
                </html>";

            return string.Format(plainHtmlTemplate, simpleMessage.Body.Content);
        }

        private bool IsSyncDisabledNotification(string? contentType)
        {
            if (string.IsNullOrEmpty(contentType))
                return false;

            return contentType.Contains("Disabled", StringComparison.OrdinalIgnoreCase)
                || contentType.Contains("Failure", StringComparison.OrdinalIgnoreCase)
                || contentType.Contains("NoData", StringComparison.OrdinalIgnoreCase)
                || contentType.Contains("NestedGroupsFound", StringComparison.OrdinalIgnoreCase);
        }
    }
}
