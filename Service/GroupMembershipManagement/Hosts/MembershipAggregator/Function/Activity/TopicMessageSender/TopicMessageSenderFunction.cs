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
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Hosts.MembershipAggregator
{
    public class TopicMessageSenderFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IServiceBusTopicsRepository _serviceBusTopicsRepository = null;
        private readonly IServiceBusTopicsRepository _messageSplitterSender = null;
        private readonly MultiLaneConfig _multilaneConfig = null;
        private const string MESSAGE_SUBSCRIPTION_SMALL = "Small";
        private const string MESSAGE_SUBSCRIPTION_MEDIUM = "Medium";
        private const string MESSAGE_SUBSCRIPTION_LARGE = "Large";
        private const string MESSAGE_SUBSCRIPTION_ONBOARDING = "Onboarding";
        private const string LANE_SIZE_PROPERTY = "LaneSize";

        public TopicMessageSenderFunction(
            ILoggingRepository loggingRepository,
            IServiceBusTopicsRepository serviceBusTopicsRepository,
            [FromKeyedServices("messageSplitterSender")] IServiceBusTopicsRepository messageSplitterSender,
            IOptions<MultiLaneConfig> multilaneConfig)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _serviceBusTopicsRepository = serviceBusTopicsRepository ?? throw new ArgumentNullException(nameof(serviceBusTopicsRepository));
            _messageSplitterSender = messageSplitterSender ?? throw new ArgumentNullException(nameof(messageSplitterSender));
            _multilaneConfig = multilaneConfig?.Value ?? throw new ArgumentNullException(nameof(multilaneConfig));
        }

        [FunctionName(nameof(TopicMessageSenderFunction))]
        public async Task SendMessageAsync([ActivityTrigger] MembershipHttpRequest request)
        {

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"{nameof(TopicMessageSenderFunction)} function started",
                RunId = request.SyncJob.RunId
            }, VerbosityLevel.DEBUG);

            var destinations = JArray.Parse(request.SyncJob.Destination);
            var destinationType = destinations.SelectTokens("$..type").Select(x => x.Value<string>()).First();
            var body = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(request));

            var message = new Models.ServiceBus.ServiceBusMessage
            {
                MessageId = $"{request.SyncJob.Id}_{request.SyncJob.RunId}_{destinationType}",
                Body = body
            };

            message.ApplicationProperties.Add("Type", destinationType);

            // Send message to the appropriate queue or topic
            if (_multilaneConfig.IsEnabled)
            {
                await SendMessageToTopicAsync(message, request);
            }
            else
            {
                await _serviceBusTopicsRepository.AddMessageAsync(message);
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Sent message to {destinationType} membership updater",
                    RunId = request.SyncJob.RunId
                }, VerbosityLevel.INFO);
            }

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"{nameof(TopicMessageSenderFunction)} function completed",
                RunId = request.SyncJob.RunId
            }, VerbosityLevel.DEBUG);
        }

        private async Task SendMessageToTopicAsync(Models.ServiceBus.ServiceBusMessage message, MembershipHttpRequest request)
        {
            if (request.SyncJob.LastSuccessfulRunTime == System.Data.SqlTypes.SqlDateTime.MinValue)
            {
                // New jobs
                message.ApplicationProperties.Add(LANE_SIZE_PROPERTY, MESSAGE_SUBSCRIPTION_ONBOARDING);
            }
            else if (request.MembersToBeUpdated <= _multilaneConfig.Small)
            {
                message.ApplicationProperties.Add(LANE_SIZE_PROPERTY, MESSAGE_SUBSCRIPTION_SMALL);
            }
            else if (request.MembersToBeUpdated <= _multilaneConfig.Medium)
            {
                message.ApplicationProperties.Add(LANE_SIZE_PROPERTY, MESSAGE_SUBSCRIPTION_MEDIUM);
            }
            else
            {
                // Large
                message.ApplicationProperties.Add(LANE_SIZE_PROPERTY, MESSAGE_SUBSCRIPTION_LARGE);
            }

            await _messageSplitterSender.AddMessageAsync(message);

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Sent message to {message.ApplicationProperties[LANE_SIZE_PROPERTY]} lane with {request.MembersToBeUpdated} operations.",
                RunId = request.SyncJob.RunId
            }, VerbosityLevel.INFO);
        }
    }
}