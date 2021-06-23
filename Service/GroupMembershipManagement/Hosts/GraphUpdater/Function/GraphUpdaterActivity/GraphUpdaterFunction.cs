// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Threading.Tasks;
using Entities;
using Entities.ServiceBus;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Newtonsoft.Json;
using Repositories.Contracts;

namespace Hosts.GraphUpdater
{
	public class GraphUpdaterFunction
	{
		private readonly SessionMessageCollector _messageCollector;
		private readonly ILoggingRepository _loggingRepository;

		public GraphUpdaterFunction(SessionMessageCollector messageCollector, ILoggingRepository loggingRepository)
		{
			_messageCollector = messageCollector;
			_loggingRepository = loggingRepository;
		}

		[FunctionName(nameof(GraphUpdaterFunction))]
		public async Task<GroupMembershipMessageResponse> Run([ActivityTrigger] GraphUpdaterFunctionRequest request)
		{
			await _loggingRepository.LogMessageAsync(new LogMessage { Message = nameof(GraphUpdaterFunction) + " function started" });
			var greq = request.Message;
			var body = new GroupMembershipMessage
			{
				Body = JsonConvert.DeserializeObject<GroupMembership>(request.Message),
				LockToken = request.MessageLockToken
			};

			var handleNewMessageResult = await _messageCollector.HandleNewMessageAsync(body, request.MessageSessionId);

			await _loggingRepository.LogMessageAsync(new LogMessage { Message = nameof(GraphUpdaterFunction) + " function completed" });

			return handleNewMessageResult;
		}

	}
}
