// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Configuration;
using Models;
using Models.ServiceBus;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class JobStatusUpdaterFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IGraphUpdaterService _graphUpdaterService;
        private readonly ServiceBusClient _serviceBusClient;
        private readonly IConfiguration _configuration;

        public JobStatusUpdaterFunction(
                        ILoggingRepository loggingRepository,
                        IGraphUpdaterService graphUpdaterService,
                        ServiceBusClient serviceBusClient,
                        IConfiguration configuration)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _graphUpdaterService = graphUpdaterService ?? throw new ArgumentNullException(nameof(graphUpdaterService));
            _serviceBusClient = serviceBusClient ?? throw new ArgumentNullException(nameof(serviceBusClient));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        [FunctionName(nameof(JobStatusUpdaterFunction))]
        public async Task UpdateJobStatusAsync([ActivityTrigger] JobStatusUpdaterRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(JobStatusUpdaterFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);

            try
            {
                // Get job information for context
                var syncJob = await _graphUpdaterService.GetSyncJobAsync(request.JobId);
                if (syncJob == null)
                {
                    await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Sync job with ID {request.JobId} not found", RunId = request.RunId }, VerbosityLevel.INFO);
                    return;
                }

                // Create queue message for centralized processing in SyncJobUpdater
                var queueMessage = new JobStatusUpdateQueueMessage
                {
                    RunId = request.RunId,
                    JobId = request.JobId,
                    Status = request.Status,
                    ThresholdViolations = request.ThresholdViolations,
                    UsersAddedCount = request.UsersAddedCount,
                    UsersRemovedCount = request.UsersRemovedCount,
                    JobEndTime = request.JobEndTime,
                    UpdatedByFunction = "GraphUpdater"
                };

                // Send message to SyncJobUpdater queue
                var syncJobUpdaterQueue = _configuration["serviceBusSyncJobUpdaterQueue"];
                await using var sender = _serviceBusClient.CreateSender(syncJobUpdaterQueue);

                var messageBody = JsonSerializer.Serialize(queueMessage);
                var serviceBusMessage = new Azure.Messaging.ServiceBus.ServiceBusMessage(messageBody)
                {
                    MessageId = Guid.NewGuid().ToString()
                };

                serviceBusMessage.ApplicationProperties.Add("MessageType", "JobStatusUpdate");
                serviceBusMessage.ApplicationProperties.Add("RunId", request.RunId.ToString());
                serviceBusMessage.ApplicationProperties.Add("JobId", request.JobId.ToString());

                await sender.SendMessageAsync(serviceBusMessage);

                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Dispatched job status update queue message {serviceBusMessage.MessageId} for job {request.JobId} with status {request.Status}",
                    RunId = request.RunId
                }, VerbosityLevel.DEBUG);
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Error dispatching job status update: {ex.Message}",
                    RunId = request.RunId
                }, VerbosityLevel.INFO);
                throw;
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(JobStatusUpdaterFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
        }
    }
}