// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Repositories.Contracts.Helpers;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.MembershipAggregator
{
    public class TopicMessageSenderFunction
    {
        private readonly ILogger<TopicMessageSenderFunction> _logger;
        private readonly ITopicMessageSenderService _topicMessageSenderRepository;

        public TopicMessageSenderFunction(
            ILogger<TopicMessageSenderFunction> logger,
            ITopicMessageSenderService topicMessageSenderRepository)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _topicMessageSenderRepository = topicMessageSenderRepository ?? throw new ArgumentNullException(nameof(topicMessageSenderRepository));
        }

        [Function(nameof(TopicMessageSenderFunction))]
        public async Task SendMessageAsync([ActivityTrigger] TopicMessageSenderRequest request)
        {
            using (_logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object>
            {
                ["CurrentPart"] = request.CurrentPart,
                ["TotalParts"] = request.TotalParts
            }))
            {
                _logger.FunctionStarted(nameof(TopicMessageSenderFunction));
                await _topicMessageSenderRepository.SendMessageAsync(request.MembershipHttpRequest);
                _logger.FunctionCompleted(nameof(TopicMessageSenderFunction));
            }
        }
    }
}