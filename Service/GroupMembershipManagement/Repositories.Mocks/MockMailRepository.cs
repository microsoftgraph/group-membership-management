// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Repositories.Contracts;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Repositories.Mocks
{
    public class MockMailRepository : IMailRepository
    {
        public List<EmailMessage> SentEmails { get; set; } = new List<EmailMessage>();
        public Task SendMailAsync(EmailMessage emailMessage)
        {
            SentEmails.Add(emailMessage);
            return Task.CompletedTask;
        }
    }
}
