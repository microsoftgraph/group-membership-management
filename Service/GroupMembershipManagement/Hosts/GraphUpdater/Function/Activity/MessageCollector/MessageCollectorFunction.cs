// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Threading.Tasks;
using Entities;
using Entities.ServiceBus;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Newtonsoft.Json;
using Repositories.Contracts;

namespace Hosts.GraphUpdater
{
	public class MessageCollectorFunction
	{
		private readonly MessageCollector _messageCollector;
		private readonly ILoggingRepository _loggingRepository;

		public MessageCollectorFunction(MessageCollector messageCollector, ILoggingRepository loggingRepository)
		{
			_loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
			_messageCollector = messageCollector ?? throw new ArgumentNullException(nameof(messageCollector));
		}

		[FunctionName(nameof(MessageCollectorFunction))]
		public async Task<GroupMembershipMessageResponse> CollectMessagesAsync([ActivityTrigger] GraphUpdaterFunctionRequest request)
		{
			await _loggingRepository.LogMessageAsync(new LogMessage { Message = nameof(MessageCollectorFunction) + " function started", RunId = request.RunId });
			var body = new GroupMembershipMessage
			{
				Body = JsonConvert.DeserializeObject<GroupMembership>(request.Message),
				LockToken = request.MessageLockToken
			};

			var handleNewMessageResult = await _messageCollector.HandleNewMessageAsync(body, request.MessageSessionId);

			await _loggingRepository.LogMessageAsync(new LogMessage { Message = nameof(MessageCollectorFunction) + " function completed", RunId = request.RunId });

			return handleNewMessageResult;
		}
	}
}
