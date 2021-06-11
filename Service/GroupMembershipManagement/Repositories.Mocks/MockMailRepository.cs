// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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

	[ExcludeFromCodeCoverage]
	public class MockEmail<T> : IEmailSenderRecipient
	{
		public string SenderAddress => "";

		public string SenderPassword => "";

		public string SyncCompletedCCAddresses => "";

		public string SyncDisabledCCAddresses => "";
	}
}
