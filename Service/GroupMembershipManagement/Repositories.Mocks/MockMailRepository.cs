// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace Repositories.Mocks
{
	public class MockMailRepository : IMailRepository
	{
		public List<EmailMessage> SentEmails { get; set; } = new List<EmailMessage>();
		public Task<HttpResponseMessage> SendMailAsync(EmailMessage emailMessage, Guid? runId)
		{
			SentEmails.Add(emailMessage);
            var responseMessage = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("Mock email sent successfully.")
            };
            return Task.FromResult(responseMessage);
		}
	}

	public class MockEmail<T> : IEmailSenderRecipient
	{
		public string SenderAddress => "";

		public string SenderPassword => "";

		public string SyncCompletedCCAddresses => "";

		public string SyncDisabledCCAddresses => "";

        public string SupportEmailAddresses => "";
    }
}
