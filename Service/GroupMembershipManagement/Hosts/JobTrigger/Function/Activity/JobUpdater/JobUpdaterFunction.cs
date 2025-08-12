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

namespace Hosts.JobTrigger
{
    public class JobUpdaterFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IJobTriggerService _jobTriggerService;
        private readonly ServiceBusClient _serviceBusClient;
        private readonly IConfiguration _configuration;

        public JobUpdaterFunction(
            ILoggingRepository loggingRepository,
            IJobTriggerService jobTriggerService,
            ServiceBusClient serviceBusClient,
            IConfiguration configuration)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _jobTriggerService = jobTriggerService ?? throw new ArgumentNullException(nameof(jobTriggerService));
            _serviceBusClient = serviceBusClient ?? throw new ArgumentNullException(nameof(serviceBusClient));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        [FunctionName(nameof(JobUpdaterFunction))]
        public async Task UpdateJobAsync([ActivityTrigger] JobUpdaterRequest request)
        {
            if (request.SyncJob == null)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(JobUpdaterFunction)} function called with null SyncJob", RunId = Guid.Empty }, VerbosityLevel.INFO);
                return;
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(JobUpdaterFunction)} function started", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);

            try
            {
                var now = DateTime.UtcNow;
                if (request.Status == SyncStatus.InProgress)
                {
                    await _loggingRepository.LogMessageAsync(new LogMessage
                    {
                        RunId = request.SyncJob.RunId,
                        Message = "Starting job."
                    });

                    request.SyncJob.LastSuccessfulStartTime = now;
                }

                if (request.Status == SyncStatus.StuckInProgress)
                {
                    await _loggingRepository.LogMessageAsync(new LogMessage
                    {
                        RunId = request.SyncJob.RunId,
                        Message = "Restarting job stuck in InProgress."
                    });

                    request.SyncJob.LastRunTime = now;
                    request.SyncJob.LastSuccessfulStartTime = now;
                }

                // Create queue message for centralized processing in SyncJobUpdater
                var queueMessage = new JobStatusUpdateQueueMessage
                {
                    RunId = request.SyncJob.RunId ?? Guid.Empty,
                    JobId = request.SyncJob.Id,
                    NewStatus = request.Status ?? Enum.Parse<SyncStatus>(request.SyncJob.Status),
                    UpdatedByFunction = "JobTrigger",
                    SyncJob = request.SyncJob,
                    JobStartTime = request.Status == SyncStatus.InProgress || request.Status == SyncStatus.StuckInProgress
                        ? now
                        : null
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
                serviceBusMessage.ApplicationProperties.Add("RunId", queueMessage.RunId.ToString());
                serviceBusMessage.ApplicationProperties.Add("JobId", queueMessage.JobId.ToString());

                await sender.SendMessageAsync(serviceBusMessage);

                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Dispatched job status update queue message {serviceBusMessage.MessageId} for job {queueMessage.JobId} with status {queueMessage.NewStatus}",
                    RunId = request.SyncJob.RunId
                }, VerbosityLevel.DEBUG);
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Error dispatching job status update: {ex.Message}",
                    RunId = request.SyncJob.RunId
                }, VerbosityLevel.INFO);
                throw;
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(JobUpdaterFunction)} function completed", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);
        }
    }
}