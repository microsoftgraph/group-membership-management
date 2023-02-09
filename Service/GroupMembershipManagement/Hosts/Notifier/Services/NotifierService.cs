// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using System.Collections.Generic;
using System.Threading.Tasks;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Linq;
using Newtonsoft.Json;
using Repositories.Contracts.InjectConfig;
using Azure;
using Models.Entities;

namespace Services
{
    public class NotifierService : INotifierService
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IMailRepository _mailRepository = null;
        private readonly IEmailSenderRecipient _emailSenderAndRecipients = null;

        public NotifierService(
            ILoggingRepository loggingRepository,
            IMailRepository mailRepository,
            IEmailSenderRecipient emailSenderAndRecipients)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _mailRepository = mailRepository ?? throw new ArgumentNullException(nameof(mailRepository));
            _emailSenderAndRecipients = emailSenderAndRecipients ?? throw new ArgumentNullException(nameof(emailSenderAndRecipients));
        }

        public async Task SendEmailAsync(string recipientAddresses)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Sending email to recipient addresses." });

            var message = new EmailMessage
            {
                Subject = "TestSubject",
                Content = "TestContent",
                SenderAddress = _emailSenderAndRecipients.SenderAddress,
                SenderPassword = _emailSenderAndRecipients.SenderPassword,
                ToEmailAddresses = recipientAddresses,
                CcEmailAddresses = _emailSenderAndRecipients.SupportEmailAddresses,
            };
            
            await _mailRepository.SendMailAsync(message, null);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Sent email to recipient addresses." });
        }

    }
}
