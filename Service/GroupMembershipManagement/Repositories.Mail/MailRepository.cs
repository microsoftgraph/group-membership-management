// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Graph;
using Microsoft.Graph.Auth;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.Security;
using System.Threading.Tasks;

namespace Repositories.Mail
{
    public class MailRepository : IMailRepository
    {
        private readonly ILocalizationRepository _localizationRepository;
        private readonly IGraphServiceClient _graphClient;
        private readonly string _senderAddress = null;
        private readonly string _senderPassword = null;

        public MailRepository(IGraphServiceClient graphClient, IEmailSender senderAddress, ILocalizationRepository localizationRepository)
        {
            _localizationRepository = localizationRepository ?? throw new ArgumentNullException(nameof(localizationRepository));
            _graphClient = graphClient ?? throw new ArgumentNullException(nameof(graphClient));
            _senderAddress = senderAddress.Email;
            _senderPassword = senderAddress.Password;
        }

        public async Task SendMail(string subject, string content, string toEmailAddress, string ccEmailAddress, params string[] additionalContentParams)
        {          

            var message = new Message
            {
                Subject = _localizationRepository.TranslateSetting(subject),
                Body = new ItemBody
                {
                    ContentType = BodyType.Text,
                    Content = _localizationRepository.TranslateSetting(content, additionalContentParams)
                },
                ToRecipients = new List<Recipient>()
                {
                    new Recipient
                    {
                        EmailAddress = new EmailAddress { Address = toEmailAddress }
                    }
                }
            };

            if (!string.IsNullOrEmpty(ccEmailAddress))
            {
                message.CcRecipients = new List<Recipient>()
                {
                    new Recipient
                    {
                        EmailAddress = new EmailAddress { Address = ccEmailAddress }
                    }
                };
            }            

            var securePassword = new SecureString();
            foreach (char c in _senderPassword)
                securePassword.AppendChar(c);

            await _graphClient.Me
                    .SendMail(message, SaveToSentItems: true)
                    .Request().WithUsernamePassword(_senderAddress, securePassword)
                    .PostAsync();

        }
    }
}
