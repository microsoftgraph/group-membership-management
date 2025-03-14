// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using DIConcreteTypes;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Hosts.MembershipAggregator
{
    public class TopicMessageSenderFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly ITopicMessageSenderService _topicMessageSenderRepository = null;

        public TopicMessageSenderFunction(
            ILoggingRepository loggingRepository,
            ITopicMessageSenderService topicMessageSenderRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _topicMessageSenderRepository = topicMessageSenderRepository ?? throw new ArgumentNullException(nameof(topicMessageSenderRepository));
        }

        [FunctionName(nameof(TopicMessageSenderFunction))]
        public async Task SendMessageAsync([ActivityTrigger] MembershipHttpRequest request)
        {

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"{nameof(TopicMessageSenderFunction)} function started",
                RunId = request.SyncJob.RunId
            }, VerbosityLevel.DEBUG);

            await _topicMessageSenderRepository.SendMessageAsync(request);

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"{nameof(TopicMessageSenderFunction)} function completed",
                RunId = request.SyncJob.RunId
            }, VerbosityLevel.DEBUG);
        }

    }
}