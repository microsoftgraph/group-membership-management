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
        private readonly IGraphServiceClient _graphClient;
        private readonly string _senderAddress = null;
        private readonly string _senderPassword = null;

        public MailRepository(IGraphServiceClient graphClient, ISenderEmail<IMailRepository> senderAddress)
        {
            _graphClient = graphClient ?? throw new ArgumentNullException(nameof(graphClient));
            _senderAddress = senderAddress.Email;
            _senderPassword = senderAddress.Password;
        }

        public async Task SendMail(string subject, string content, string recipientAddress)
        {          

            var message = new Message
            {
                Subject = subject,
                Body = new ItemBody
                {
                    ContentType = BodyType.Text,
                    Content = content
                },
                ToRecipients = new List<Recipient>()
                {
                    new Recipient
                    {
                        EmailAddress = new EmailAddress { Address = recipientAddress }
                    }
                }
            };                       

            var securePassword = new SecureString();
            foreach (char c in _senderPassword)
                securePassword.AppendChar(c);

            var saveToSentItems = true;

            await _graphClient.Me
                    .SendMail(message, saveToSentItems)
                    .Request().WithUsernamePassword(_senderAddress, securePassword)
                    .PostAsync();

        }
    }
}
