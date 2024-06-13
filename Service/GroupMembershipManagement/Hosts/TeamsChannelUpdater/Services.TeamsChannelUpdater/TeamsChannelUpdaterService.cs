// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Models.Entities;
using Repositories.Contracts;
using Services.TeamsChannelUpdater.Contracts;
using Models.Notifications;
using Models.ServiceBus;
using System.Text.Json;
using System.Text;

namespace Services.TeamsChannelUpdater
{
    public class TeamsChannelUpdaterService : ITeamsChannelUpdaterService
    {
        private const int NumberOfGraphRetries = 5;
        private const string EmailSubject = "EmailSubject";

        private readonly ITeamsChannelRepository _teamsChannelRepository;
        private readonly IDatabaseSyncJobsRepository _syncJobRepository;
        private readonly ILoggingRepository _loggingRepository;
        private readonly IServiceBusQueueRepository _serviceBusQueueRepository;

        private Guid _runId;
        public Guid RunId
        {
            get { return _runId; }
            set
            {
                _runId = value;
                _teamsChannelRepository.RunId = value;
            }
        }

        public TeamsChannelUpdaterService(ITeamsChannelRepository teamsChannelRepository,
            IDatabaseSyncJobsRepository syncJobRepository, 
            ILoggingRepository loggingRepository,
            IServiceBusQueueRepository serviceBusQueueRepository)
        {
            _teamsChannelRepository = teamsChannelRepository ?? throw new ArgumentNullException(nameof(teamsChannelRepository));
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _serviceBusQueueRepository = serviceBusQueueRepository ?? throw new ArgumentNullException(nameof(serviceBusQueueRepository));
        }

        public async Task<SyncJob> GetSyncJobAsync(Guid syncJobId)
        {
            return await _syncJobRepository.GetSyncJobAsync(syncJobId);
        }


        public async Task UpdateSyncJobStatusAsync(SyncJob job, SyncStatus status, bool isDryRun, Guid runId)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Set job status to {status}.", RunId = runId });

            var isDryRunSync = job.IsDryRunEnabled || isDryRun;

            var currentDate = DateTime.UtcNow;
            if (isDryRunSync)
            {
                job.DryRunTimeStamp = currentDate;
            }
            else
            {
                if (status == SyncStatus.Idle)
                    job.LastSuccessfulRunTime = currentDate;

                job.LastRunTime = currentDate;
            }

            job.ScheduledDate = currentDate.AddHours(job.Period);
            job.RunId = runId;

            await _syncJobRepository.UpdateSyncJobStatusAsync(new[] { job }, status);

            string message = isDryRunSync
                                ? $"Dry Run of a sync to {job.TargetOfficeGroupId} is complete. Membership will not be updated."
                                : $"Syncing to {job.TargetOfficeGroupId} done.";

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = message, RunId = runId });
        }

        public async Task MarkSyncJobAsErroredAsync(SyncJob syncJob)
        {
            await _syncJobRepository.UpdateSyncJobStatusAsync(new[] { syncJob }, SyncStatus.Error);
        }

        public async Task<(int SuccessCount, List<AzureADTeamsUser> UsersToRetry, List<AzureADTeamsUser> UsersNotFound)> AddUsersToChannelAsync(AzureADTeamsChannel azureADTeamsChannel, List<AzureADTeamsUser> members)
        {
            var response = await _teamsChannelRepository.AddUsersToChannelAsync(azureADTeamsChannel, members);

            return response;
        }

        public async Task<(int SuccessCount, List<AzureADTeamsUser> UserRemovesFailed)> RemoveUsersFromChannelAsync(AzureADTeamsChannel azureADTeamsChannel, List<AzureADTeamsUser> members)
        {
            var response = await _teamsChannelRepository.RemoveUsersFromChannelAsync(azureADTeamsChannel, members);

            return response;
        }

        public async Task<string> GetGroupNameAsync(Guid groupId, Guid runId)
        {
            return await _teamsChannelRepository.GetGroupNameAsync(groupId, runId);
        }
        
        public async Task<List<AzureADUser>> GetGroupOwnersAsync(Guid groupObjectId, Guid runId, int top = 0)
        {
            return await _teamsChannelRepository.GetGroupOwnersAsync(groupObjectId, runId, top);
        }

        public async Task SendEmailAsync(SyncJob job, NotificationMessageType notificationType, string[] additionalContentParams)
        {
            var messageContent = new Dictionary<string, Object>
            {
                { "SyncJob", job },
                { "AdditionalContentParameters", additionalContentParams }
            };

            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(messageContent));
            var message = new ServiceBusMessage
            {
                MessageId = $"{job.Id}_{job.RunId}_{notificationType}",
                Body = body
            };
            message.ApplicationProperties.Add("MessageType", notificationType.ToString());

            await _serviceBusQueueRepository.SendMessageAsync(message);
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                RunId = job.RunId,
                Message = $"Sent message {message.MessageId} to service bus notifications queue "
            });
        }

    }
}
