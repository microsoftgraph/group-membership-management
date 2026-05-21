// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using AdaptiveCards.Templating;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions;
using Models;
using Models.AdaptiveCards;
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
        private readonly string _actionableEmailProviderId;
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
            string actionableEmailProviderId, 
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
            _actionableEmailProviderId = actionableEmailProviderId ?? throw new ArgumentNullException(nameof(actionableEmailProviderId));
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
            var titleContent = string.IsNullOrEmpty(emailMessage?.Title) ? _localizationRepository.TranslateSetting(emailMessage?.Subject, emailMessage?.AdditionalContentParams) :
                                                                           _localizationRepository.TranslateSetting(emailMessage?.Title, emailMessage?.AdditionalContentParams);
            var subjectContent = _localizationRepository.TranslateSetting(emailMessage?.Subject, emailMessage?.AdditionalContentParams);
            var messageContent = _localizationRepository.TranslateSetting(emailMessage?.Content, emailMessage?.AdditionalContentParams);

            string adaptiveCardJson = _localizationRepository.TranslateSetting(CardTemplate.DefaultCardTemplate);

            // Most notification types put the destination GroupId at AdditionalContentParams[0],
            // but JobPurgingWarning (AzureMaintenanceService.SendWarningEmailAsync) puts the
            // SyncStatus at [0] and the GroupId at [4]. Pick the right index per type so the
            // styled fallback renders the correct OBJECT ID and resolves the destination group.
            string groupId = GetParamSafe(emailMessage, GetGroupIdIndex(emailMessage?.Content));
            string destinationGroupName = string.IsNullOrEmpty(emailMessage?.DestinationGroupName) ? "" : emailMessage.DestinationGroupName;
            var urlSetting = await _settingsRepository.GetSettingByKeyAsync(SettingKey.UIUrl);
            var dashboardUrlSetting = await _settingsRepository.GetSettingByKeyAsync(SettingKey.DashboardUrl);

            string UIUrl = urlSetting?.SettingValue ?? "";
            string dashboardUrl = dashboardUrlSetting?.SettingValue ?? "";
            string jobUrl = urlSetting?.SettingValue + "/jobdetails/" + emailMessage.SyncJobId.ToString() ?? "";

            var cardData = new DefaultCardTemplate
            {
                ProviderId = _actionableEmailProviderId,
                TitleContent = titleContent,
                SubjectContent = subjectContent,
                MessageContent = messageContent,
                GroupId = groupId,
                CardCreatedTime = DateTime.UtcNow,
                DestinationGroupName = destinationGroupName,
                UIUrl = UIUrl,
                DashboardUrl = dashboardUrl,
                JobUrl = jobUrl
            };

            var template = new AdaptiveCardTemplate(adaptiveCardJson);
            var adaptiveCard = template.Expand(cardData);

            var sentDate = DateTime.UtcNow.ToString("MMM dd, yyyy");
            string htmlContent;

            if (_mailConfig.EnableStyledFallbackEmails)
            {
                string styledFallback = null;

                string fallbackDestinationGroupName = destinationGroupName;
                if (string.IsNullOrEmpty(fallbackDestinationGroupName)
                    && string.Equals(emailMessage?.Content, NotificationConstants.DestinationNotExistContent, StringComparison.OrdinalIgnoreCase))
                {
                    var cachedName = GetParamSafe(emailMessage, 1);
                    if (!string.IsNullOrEmpty(cachedName) && !string.Equals(cachedName, "NAME NOT FOUND", StringComparison.OrdinalIgnoreCase))
                    {
                        fallbackDestinationGroupName = cachedName;
                    }
                }

                if (string.Equals(emailMessage?.Content, "SyncStartedEmailBody", StringComparison.OrdinalIgnoreCase))
                    styledFallback = await _mailFallbackBuilder.BuildSyncStartedFallbackAsync(emailMessage, fallbackDestinationGroupName, groupId, jobUrl, sentDate);
                else if (string.Equals(emailMessage?.Content, "SyncCompletedEmailBody", StringComparison.OrdinalIgnoreCase))
                    styledFallback = await _mailFallbackBuilder.BuildSyncCompletedFallbackAsync(emailMessage, fallbackDestinationGroupName, groupId, jobUrl, sentDate);
                else if (string.Equals(emailMessage?.Content, NotificationConstants.JobPurgingWarningEmailBody, StringComparison.OrdinalIgnoreCase))
                    styledFallback = await _mailFallbackBuilder.BuildJobPurgingWarningFallbackAsync(emailMessage, fallbackDestinationGroupName, groupId, jobUrl, sentDate);
                else if (IsSyncDisabledNotification(emailMessage?.Content))
                    styledFallback = await _mailFallbackBuilder.BuildSyncDisabledFallbackAsync(emailMessage, fallbackDestinationGroupName, groupId, jobUrl, sentDate);
                else if (string.Equals(emailMessage?.Content, NotificationConstants.SubmissionRejectedEmailBody, StringComparison.OrdinalIgnoreCase))
                    styledFallback = await _mailFallbackBuilder.BuildSubmissionRejectedFallbackAsync(emailMessage, fallbackDestinationGroupName, groupId, jobUrl, sentDate);

                if (styledFallback != null)
                {
                    htmlContent = WrapStyledFallback(styledFallback, fallbackDestinationGroupName, adaptiveCard);
                }
                else
                {
                    // Legacy adaptive-card + plain-text fallback for notification types
                    // that have not yet been migrated to a styled HTML template.
                    htmlContent = BuildLegacyFallback(emailMessage, adaptiveCard);
                }
            }
            else
            {
                // Feature disabled: use legacy adaptive-card + plain-text fallback for all notification types.
                htmlContent = BuildLegacyFallback(emailMessage, adaptiveCard);
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
        // (AzureMaintenanceService.SendWarningEmailAsync) puts Status at [0] and GroupId at [4].
        private static int GetGroupIdIndex(string? content)
        {
            return string.Equals(content, NotificationConstants.JobPurgingWarningEmailBody, StringComparison.OrdinalIgnoreCase)
                ? 4
                : 0;
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

        private static string WrapStyledFallback(string body, string groupName, string adaptiveCard) =>
            $@"<!DOCTYPE html>
<html lang=""en"">
<head>
  <meta http-equiv=""Content-Type"" content=""text/html; charset=utf-8"">
  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
  <title>{System.Net.WebUtility.HtmlEncode(groupName)}</title>
  <script type=""application/adaptivecard+json"">
{adaptiveCard}
  </script>
</head>
<body style=""margin:0;padding:0;background-color:#f3f2f1;-webkit-font-smoothing:antialiased;"">
{body}
</body>
</html>";

        private string BuildLegacyFallback(EmailMessage emailMessage, string adaptiveCard)
        {
            var simpleMessage = GetSimpleMessage(emailMessage);
            var fallbackHTMLContent = simpleMessage.Body.Content;

            var legacyHtmlTemplate = @"<html>
                <head
                  <meta http-equiv=""Content-Type"" content=""text/html; charset=utf-8"">
                  <script type=""application/adaptivecard+json"">
                 {0}
                  </script>
                </head>
                <body>
                <p style=""color: red;"">Warning: Group Membership Management (GMM) notifications are powered by Outlook Actionable Messages. The following is a fallback message that you will see if the Actionable Message fails to render.</p>
                <h1>Original Message</h1>
                <pre>{1}</pre>
                </body>
                </html>";

            return string.Format(legacyHtmlTemplate, adaptiveCard, fallbackHTMLContent);
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
