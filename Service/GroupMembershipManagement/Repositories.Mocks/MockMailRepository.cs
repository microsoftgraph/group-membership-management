// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Repositories.Mocks
{
	public class MockMailRepository : IMailRepository
	{
		public List<EmailMessage> SentEmails { get; set; } = new List<EmailMessage>();
		public Task SendMailAsync(EmailMessage emailMessage, Guid? runId, bool useAdaptiveCard = true, string templateDirectory = "")
		{
			SentEmails.Add(emailMessage);
			return Task.CompletedTask;
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
