// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Models.Entities;
using Models.Notifications;
using Models.ServiceBus;
using Newtonsoft.Json;
using Polly;
using Polly.Retry;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading.Tasks;
using Models.Helpers;

namespace Hosts.GroupMembershipObtainer
{
    public class SGMembershipCalculator
    {
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly IBlobStorageRepository _blobStorageRepository;
        private readonly ILoggingRepository _log;
        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository;
        private readonly bool _isGroupMembershipDryRunEnabled;
        private readonly IServiceBusQueueRepository _notificationsQueueRepository;
        private readonly IDatabaseDestinationAttributesRepository _databaseDestinationAttributesRepository;

        public SGMembershipCalculator(IGraphGroupRepository graphGroupRepository,
                                      IBlobStorageRepository blobStorageRepository,
                                      IDatabaseSyncJobsRepository databaseSyncJobsRepository,
                                      IServiceBusQueueRepository notificationsQueueRepository,
                                      IDatabaseDestinationAttributesRepository databaseDestinationAttributesRepository,
                                      ILoggingRepository logging,
                                      IDryRunValue dryRun
                                      )
        {
            _graphGroupRepository = graphGroupRepository;
            _blobStorageRepository = blobStorageRepository;
            _log = logging;
            _databaseSyncJobsRepository = databaseSyncJobsRepository;
            _notificationsQueueRepository = notificationsQueueRepository;
            _databaseDestinationAttributesRepository = databaseDestinationAttributesRepository;
            _isGroupMembershipDryRunEnabled = dryRun.DryRunEnabled;
        }

        private const int NumberOfGraphRetries = 5;
        private AsyncRetryPolicy _graphRetryPolicy;
        private const string EmailSubject = "EmailSubject";
        private Guid _runId;
        public Guid RunId
        {
            get { return _runId; }
            set
            {
                _runId = value;
                _graphGroupRepository.RunId = value;
            }
        }

        public object GraphRepository { get; set; }

        public async Task<PolicyResult<bool>> GroupExistsAsync(Guid objectId, Guid runId)
        {
            // make this fresh every time because the lambda has to capture the run ID
            _graphRetryPolicy = Policy.Handle<SocketException>().WaitAndRetryAsync(NumberOfGraphRetries, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                   onRetry: async (ex, count) =>
                   {
                       await _log.LogMessageAsync(new LogMessage
                       {
                           Message = $"Got a transient SocketException. Retrying. This was try {count} out of {NumberOfGraphRetries}.\n" + ex.ToString(),
                           RunId = runId
                       });
                   });

            return await _graphRetryPolicy.ExecuteAndCaptureAsync(() => _graphGroupRepository.GroupExists(objectId));
        }

        public async Task<DeltaGroupInformation> GetFirstDeltaUsersPageAsync(string deltaLink)
        {
            var result = await _graphGroupRepository.GetFirstDeltaUsersPageAsync(deltaLink);
            return new DeltaGroupInformation
            {
                UsersToAdd = result.usersToAdd,
                UsersToRemove = result.usersToRemove,
                NextPageUrl = result.nextPageUrl,
                DeltaUrl = result.deltaUrl
            };
        }

        public async Task<DeltaGroupInformation> GetNextDeltaUsersPageAsync(string nextPageUrl)
        {
            var result = await _graphGroupRepository.GetNextDeltaUsersPageAsync(nextPageUrl);
            return new DeltaGroupInformation
            {
                UsersToAdd = result.usersToAdd,
                UsersToRemove = result.usersToRemove,
                NextPageUrl = result.nextPageUrl,
                DeltaUrl = result.deltaUrl
            };
        }

        public async Task<int> GetGroupsCountAsync(Guid objectId)
        {
            return await _graphGroupRepository.GetGroupsCountAsync(objectId);
        }

        public async Task<int> GetUsersCountAsync(Guid objectId)
        {
            return await _graphGroupRepository.GetUsersCountAsync(objectId);
        }

        public async Task<DeltaGroupInformation> GetFirstUsersPageAsync(Guid objectId, Guid runId)
        {
            await _log.LogMessageAsync(new LogMessage { RunId = runId, Message = $"Reading users from the group with ID {objectId}." });
            var result = await _graphGroupRepository.GetFirstUsersPageAsync(objectId);
            return new DeltaGroupInformation
            {
                UsersToAdd = result.users,
                NextPageUrl = result.nextPageUrl,
                DeltaUrl = result.deltaUrl
            };
        }

        public async Task<DeltaGroupInformation> GetNextUsersPageAsync(string nextPageUrl)
        {
            var result = await _graphGroupRepository.GetNextUsersPageAsync(nextPageUrl);
            return new DeltaGroupInformation
            {
                UsersToAdd = result.users,
                NextPageUrl = result.nextPageUrl,
                DeltaUrl = result.deltaUrl
            };
        }

        public async Task<GroupInformation> GetFirstTransitiveMembersPageAsync(Guid objectId, Guid runId)
        {
            await _log.LogMessageAsync(new LogMessage { RunId = runId, Message = $"Reading users from the group with ID {objectId}." });
            var result = await _graphGroupRepository.GetFirstTransitiveMembersPageAsync(objectId);
            return new GroupInformation
            {
                Users = result.users,
                NonUserGraphObjects = result.nonUserGraphObjects,
                NextPageUrl = result.nextPageUrl
            };
        }

        public async Task<GroupInformation> GetNextTransitiveMembersPageAsync(string nextPageUrl)
        {
            var result = await _graphGroupRepository.GetNextTransitiveMembersPageAsync(nextPageUrl);
            return new GroupInformation
            {
                Users = result.users,
                NonUserGraphObjects = result.nonUserGraphObjects,
                NextPageUrl = result.nextPageUrl
            };
        }

        public async Task<string> SendMembershipAsync(SyncJob syncJob, List<AzureADUser> allUsers, int currentPart, bool exclusionary)
        {
            var runId = syncJob.RunId.GetValueOrDefault();
            var groupMembership = new GroupMembership
            {
                SourceMembers = allUsers ?? new List<AzureADUser>(),
                Destination = new AzureADGroup { ObjectId = syncJob.TargetOfficeGroupId },
                RunId = runId,
                Exclusionary = exclusionary,
                SyncJobId = syncJob.Id,
                MembershipObtainerDryRunEnabled = _isGroupMembershipDryRunEnabled,
                Query = syncJob.Query
            };

            var timeStamp = DateTime.UtcNow.ToString("MMddyyyy-HHmm");
            var fileName = $"/{syncJob.TargetOfficeGroupId}/{timeStamp}_{runId}_GroupMembership_{currentPart}.json";
            await _blobStorageRepository.UploadFileAsync(fileName, JsonConvert.SerializeObject(groupMembership));

            return fileName;
        }

        public async Task SaveDeltaUsersAsync(SyncJob syncJob, Guid id, List<AzureADUser> users, string deltaLink)
        {
            var timeStamp = DateTime.UtcNow.ToString("MMddyyyy-HHmm");
            var fileName = $"/cache/delta_{id}_{timeStamp}.json";
            await _blobStorageRepository.UploadFileAsync(fileName, deltaLink);
            var groupMembership = new GroupMembership
            {
                SourceMembers = users ?? new List<AzureADUser>()
            };
            var datafileName = $"/cache/{id}_{timeStamp}.json";
            await _blobStorageRepository.UploadFileAsync(datafileName, JsonConvert.SerializeObject(groupMembership));
        }

        public async Task SendEmailAsync(SyncJob job, NotificationMessageType notificationType, string[] additionalContentParameters)
        {
            var messageContent = new Dictionary<string, Object>
            {
                { "SyncJob", job },
                { "AdditionalContentParameters", additionalContentParameters }
            };
            var body = System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(messageContent));
            var message = new ServiceBusMessage
            {
                MessageId = $"{job.Id}_{job.RunId}_{notificationType}",
                Body = body
            };
            message.ApplicationProperties.Add("MessageType", notificationType.ToString());
            await _notificationsQueueRepository.SendMessageAsync(message);
            await _log.LogMessageAsync(new LogMessage
            {
                RunId = job.RunId,
                Message = $"Sent message {message.MessageId} to service bus notifications queue "

            });

        }

        public async Task UpdateSyncJobStatusAsync(SyncJob job, SyncStatus status)
        {
            await _databaseSyncJobsRepository.UpdateSyncJobStatusAsync(new[] { job }, status);
        }

        public async Task<string> GetGroupNameAsync(Guid groupId)
        {
            return await _graphGroupRepository.GetGroupNameAsync(groupId);
        }
        public async Task<string> GetDestinationNameAsync(SyncJob job)
        {
            var destination = DestinationParser.ParseDestination(job);   
            if (destination == null)
            {
                await _log.LogMessageAsync(new LogMessage
                {
                    RunId = job.RunId,
                    Message = "Failed to parse destination from job."
                });
                return null;
            }
            // Try to get the name from the DestinationNames table first

            var destinationName = await _databaseDestinationAttributesRepository.GetDestinationName(job);

            if (destinationName != null)
            {
                return destinationName;
            }

            await _log.LogMessageAsync(new LogMessage
            {
                RunId = job.RunId,
                Message = "Destination name not found in database; attempting to retrieve from Graph"
            });

            if (destination.Type == "GroupMembership")
            {
                var objectId = destination.Value.ObjectId;
                return await _graphGroupRepository.GetGroupNameAsync(objectId);
            }
            
            return null;
            
        }
    }
}