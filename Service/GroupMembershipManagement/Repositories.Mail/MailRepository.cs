// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Graph;
using Microsoft.Graph.Auth;
using Models;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;

namespace Repositories.Mail
{
    public class MailRepository : IMailRepository
    {
        private readonly ILocalizationRepository _localizationRepository;
        private readonly IGraphServiceClient _graphClient;
		private readonly ILoggingRepository _loggingRepository;

		public MailRepository(IGraphServiceClient graphClient, ILocalizationRepository localizationRepository, ILoggingRepository loggingRepository)
        {
            _localizationRepository = localizationRepository ?? throw new ArgumentNullException(nameof(localizationRepository));
            _graphClient = graphClient ?? throw new ArgumentNullException(nameof(graphClient));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        public async Task SendMailAsync(EmailMessage emailMessage, Guid? runId, bool useAdaptiveCard = true, string templateDirectory = "")
        {
            if (emailMessage is null)
            {
                throw new ArgumentNullException(nameof(emailMessage));
            }

            Message message;

            if (useAdaptiveCard)
            {
                message = GetAdaptiveCardMessage(emailMessage, templateDirectory);
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
                await _graphClient.Me
                        .SendMail(message, SaveToSentItems: true)
                        .Request().WithUsernamePassword(emailMessage?.SenderAddress, securePassword)
                        .PostAsync();
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

        public Message GetAdaptiveCardMessage(EmailMessage emailMessage, string templateDirectory)
        {
            var subjectContent = _localizationRepository.TranslateSetting(emailMessage?.Subject, emailMessage?.AdditionalSubjectParams);
            var messageContent = _localizationRepository.TranslateSetting(emailMessage?.Content, emailMessage?.AdditionalContentParams);

            string htmlContent = System.IO.File.ReadAllText(Path.Combine(templateDirectory, "Templates/DefaultCard.html"), Encoding.UTF8);

            string groupId = emailMessage?.AdditionalContentParams[0];
            htmlContent = htmlContent.Replace("{0}", subjectContent).Replace("{1}", messageContent).Replace("{2}", groupId);

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
		public IEnumerable<Recipient> GetEmailAddresses(string emailAddresses)
		{
			return emailAddresses.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(address => address.Trim()).ToList()
									 .Select(address => new Recipient() { EmailAddress = new EmailAddress { Address = address } })
									 .ToList();
		}
	}
}
