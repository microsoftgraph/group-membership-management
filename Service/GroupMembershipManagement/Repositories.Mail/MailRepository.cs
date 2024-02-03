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

namespace Repositories.Mail
{
    public class MailRepository : IMailRepository
    {
        private readonly IMailAdaptiveCardConfig _mailAdaptiveCardConfig;
        private readonly ILocalizationRepository _localizationRepository;
        private readonly GraphServiceClient _graphClient;
        private readonly ILoggingRepository _loggingRepository;
        private readonly string _actionableEmailProviderId;

        public MailRepository(GraphServiceClient graphClient, IMailAdaptiveCardConfig mailAdaptiveCardConfig, ILocalizationRepository localizationRepository, ILoggingRepository loggingRepository, string actionableEmailProviderId)
        {
            _graphClient = graphClient ?? throw new ArgumentNullException(nameof(graphClient));
            _mailAdaptiveCardConfig = mailAdaptiveCardConfig ?? throw new ArgumentNullException(nameof(mailAdaptiveCardConfig));
            _localizationRepository = localizationRepository ?? throw new ArgumentNullException(nameof(localizationRepository));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _actionableEmailProviderId = actionableEmailProviderId ?? throw new ArgumentNullException(nameof(actionableEmailProviderId));
        }

        public async Task SendMailAsync(EmailMessage emailMessage, Guid? runId)
        {
            if (emailMessage is null)
            {
                throw new ArgumentNullException(nameof(emailMessage));
            }

            Message message;

            if (emailMessage.IsHTML)
            {
                message = GetHTMLMessage(emailMessage);
            }
            else if (_mailAdaptiveCardConfig.IsAdaptiveCardEnabled)
            {
                message = GetAdaptiveCardMessage(emailMessage);
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

            try
            {
                var body = new Microsoft.Graph.Me.SendMail.SendMailPostRequestBody
                {
                    Message = message,
                    SaveToSentItems = true
                };

                await _graphClient.Me.SendMail.PostAsync(body);
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

        }

        private Message GetHTMLMessage(EmailMessage emailMessage)
        {
            var message = GetSimpleMessage(emailMessage);
            message.Body.ContentType = BodyType.Html;
            return message;
        }

        public Message GetAdaptiveCardMessage(EmailMessage emailMessage)
        {
            var subjectContent = _localizationRepository.TranslateSetting(emailMessage?.Subject, emailMessage?.AdditionalSubjectParams);
            var messageContent = _localizationRepository.TranslateSetting(emailMessage?.Content, emailMessage?.AdditionalContentParams);

            string adaptiveCardJson = _localizationRepository.TranslateSetting(CardTemplate.DefaultCardTemplate);

            string groupId = emailMessage?.AdditionalContentParams[0];
            string groupName = emailMessage?.AdditionalContentParams[1];

            var cardData = new DefaultCardTemplate
            {
                ProviderId = _actionableEmailProviderId,
                SubjectContent = subjectContent,
                MessageContent = messageContent,
                GroupId = groupId,
                CardCreatedTime = DateTime.UtcNow,
                DestinationGroupName = groupName
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
    }
}
