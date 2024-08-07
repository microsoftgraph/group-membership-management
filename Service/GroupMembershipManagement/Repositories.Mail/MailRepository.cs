// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Graph;
using Microsoft.Graph.Models;
using Models;
using Models.AdaptiveCards;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using AdaptiveCards.Templating;
using System.Text.RegularExpressions;
using System.Net.Http;
using Polly.Wrap;
using Microsoft.Kiota.Abstractions;
using Microsoft.Azure.Documents;
using System.Net;

namespace Repositories.Mail
{
    public class MailRepository : IMailRepository
    {
        private readonly IMailConfig _mailConfig;
        private readonly ILocalizationRepository _localizationRepository;
        private readonly GraphServiceClient _graphClient;
        private readonly ILoggingRepository _loggingRepository;
        private readonly string _actionableEmailProviderId;
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly IDatabaseSettingsRepository _settingsRepository;
        private readonly IRetryPolicyProvider _retryPolicyProvider;

        public MailRepository(
            GraphServiceClient graphClient, 
            IMailConfig mailAdaptiveCardConfig, 
            ILocalizationRepository localizationRepository, 
            ILoggingRepository loggingRepository, 
            string actionableEmailProviderId, 
            IGraphGroupRepository graphGroupRepository,
            IDatabaseSettingsRepository settingsRepository,
            IRetryPolicyProvider retryPolicyProvider
            )
        {
            _graphClient = graphClient ?? throw new ArgumentNullException(nameof(graphClient));
            _mailConfig = mailAdaptiveCardConfig ?? throw new ArgumentNullException(nameof(mailAdaptiveCardConfig));
            _localizationRepository = localizationRepository ?? throw new ArgumentNullException(nameof(localizationRepository));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _actionableEmailProviderId = actionableEmailProviderId ?? throw new ArgumentNullException(nameof(actionableEmailProviderId));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
            _retryPolicyProvider = retryPolicyProvider ?? throw new ArgumentNullException(nameof(retryPolicyProvider));
        }

        public async Task<HttpResponseMessage> SendMailAsync(EmailMessage emailMessage, Guid? runId)
        {
            if (_mailConfig.SkipEmailNotifications)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    RunId = runId,
                    Message = "Email notifications are disabled."
                });

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
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    RunId = runId,
                    Message = "Email cannot be sent because Mail.Send permission has not been granted."
                });
            }
            catch (ServiceException ex) when (ex.Message.Contains("MailboxNotEnabledForRESTAPI"))
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    RunId = runId,
                    Message = "Email cannot be sent because required licenses are missing in the service account."
                });
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    RunId = runId,
                    Message = $"Email cannot be sent due to an unexpected exception.\n{ex}"
                });
            }
            if (httpResponse == null)
            {
                string errorMessage = "Failed to send email due to an unexpected exception.";
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    RunId = runId,
                    Message = errorMessage
                });
                return new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError)
                {
                    ReasonPhrase = errorMessage
                };
            }
            if (httpResponse.IsSuccessStatusCode)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    RunId = runId,
                    Message = "Email sent successfully."
                });
            }
            else
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    RunId = runId,
                    Message = $"Failed to send email: {httpResponse.StatusCode} - {await httpResponse.Content.ReadAsStringAsync()}"
                });
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
            var subjectContent = _localizationRepository.TranslateSetting(emailMessage?.Subject, emailMessage?.AdditionalSubjectParams);
            var messageContent = _localizationRepository.TranslateSetting(emailMessage?.Content, emailMessage?.AdditionalContentParams);

            string adaptiveCardJson = _localizationRepository.TranslateSetting(CardTemplate.DefaultCardTemplate);

            string groupId = emailMessage?.AdditionalContentParams[0];
            string destinationGroupName = string.IsNullOrEmpty(emailMessage?.DestinationGroupName) ? "" : emailMessage.DestinationGroupName;
            var urlSetting = await _settingsRepository.GetSettingByKeyAsync(SettingKey.UIUrl);
            var dashboardUrlSetting = await _settingsRepository.GetSettingByKeyAsync(SettingKey.DashboardUrl);

            string UIUrl = urlSetting?.SettingValue ?? "";
            string dashboardUrl = dashboardUrlSetting?.SettingValue ?? "";

            var cardData = new DefaultCardTemplate
            {
                ProviderId = _actionableEmailProviderId,
                SubjectContent = subjectContent,
                MessageContent = messageContent,
                GroupId = groupId,
                CardCreatedTime = DateTime.UtcNow,
                DestinationGroupName = destinationGroupName,
                UIUrl = UIUrl,
                DashboardUrl = dashboardUrl
            };

            var template = new AdaptiveCardTemplate(adaptiveCardJson);
            var adaptiveCard = template.Expand(cardData);

            var htmlTemplate = @"<html>
                <head
                  <meta http-equiv=""Content-Type"" content=""text/html; charset=utf-8"">
                  <script type=""application/adaptivecard+json"">
                 {0}
                  </script>
                </head>
                <body>
                </body>
                </html>";

            var htmlContent = string.Format(htmlTemplate, adaptiveCard);

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

            if (Guid.TryParse(emailMessage.AdditionalContentParams[0], out Guid groupId))
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    RunId = runId,
                    Message = $"Successfully parsed group ID: {groupId}"
                });

                string groupName = await _graphGroupRepository.GetGroupNameAsync(groupId);
                if (!string.IsNullOrEmpty(groupName))
                {
                    emailMessage.DestinationGroupName = groupName;
                }
            }
            else
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    RunId = runId,
                    Message = $"The provided value '{emailMessage.AdditionalContentParams[0]}' is not a valid GUID."
                });
            }
        }
        private AsyncPolicyWrap<HttpResponseMessage> GetHttpResponseMessageRetryPolicy(Guid? runId)
        {
            var retryAfterPolicy = _retryPolicyProvider.CreateRetryAfterPolicy(runId);
            var exceptionHandlingPolicy = _retryPolicyProvider.CreateExceptionHandlingPolicy(runId);

            return retryAfterPolicy.WrapAsync(exceptionHandlingPolicy);
        }
    }
}
