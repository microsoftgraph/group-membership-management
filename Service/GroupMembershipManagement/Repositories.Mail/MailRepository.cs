// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Graph;
using Microsoft.Graph.Auth;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Threading.Tasks;

namespace Repositories.Mail
{
    public class MailRepository : IMailRepository
    {
        private readonly ILocalizationRepository _localizationRepository;
        private readonly IGraphServiceClient _graphClient;

        public MailRepository(IGraphServiceClient graphClient, ILocalizationRepository localizationRepository)
        {
            _localizationRepository = localizationRepository ?? throw new ArgumentNullException(nameof(localizationRepository));
            _graphClient = graphClient ?? throw new ArgumentNullException(nameof(graphClient));
        }

        public async Task SendMailAsync(EmailMessage emailMessage)
        {     
            if (emailMessage is null)
            {
                throw new ArgumentNullException(nameof(emailMessage));
            }

            var message = new Message
            {
                Subject = _localizationRepository.TranslateSetting(emailMessage?.Subject),
                Body = new ItemBody
                {
                    ContentType = BodyType.Text,
                    Content = _localizationRepository.TranslateSetting(emailMessage?.Content, emailMessage?.AdditionalContentParams)
                }
            };

            if (!string.IsNullOrEmpty(emailMessage?.ToEmailAddresses))
                message.ToRecipients = GetEmailAddresses(emailMessage?.ToEmailAddresses);            

            if (!string.IsNullOrEmpty(emailMessage?.CcEmailAddresses))
                message.CcRecipients = GetEmailAddresses(emailMessage?.CcEmailAddresses);

            var securePassword = new SecureString();
            foreach (char c in emailMessage?.SenderPassword)
                securePassword.AppendChar(c);

            await _graphClient.Me
                    .SendMail(message, SaveToSentItems: true)
                    .Request().WithUsernamePassword(emailMessage?.SenderAddress, securePassword)
                    .PostAsync();

        }

        public IEnumerable<Recipient> GetEmailAddresses(string emailAddresses)
        {
            return emailAddresses.Split(',').Select(address => address.Trim()).ToList()
                                     .Select(address => new Recipient() { EmailAddress = new EmailAddress { Address = address } })
                                     .ToList();
        }
    }
}
