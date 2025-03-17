// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using DIConcreteTypes;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Models;
using Models.ServiceBus;
using Repositories.Contracts;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Hosts.MessageSplitter
{
    public class TopicMessageSenderFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IBlobStorageRepository _blobStorageRepository;
        private readonly IServiceBusTopicsRepository _membershipUpdaterSender;
        private readonly JsonSerializerOptions _jsonSerializerOptions;

        public TopicMessageSenderFunction(
            ILoggingRepository loggingRepository,
            [FromKeyedServices("membershipUpdaterSender")] IServiceBusTopicsRepository membershipUpdaterSender,
            IOptions<MultiLaneConfig> multilaneConfig,
            IBlobStorageRepository blobStorageRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
            _membershipUpdaterSender = membershipUpdaterSender ?? throw new ArgumentNullException(nameof(membershipUpdaterSender));
            _jsonSerializerOptions = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        }

        [Function(nameof(TopicMessageSenderFunction))]
        public async Task SendMessageAsync([ActivityTrigger] TopicMessageSenderRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"{nameof(TopicMessageSenderFunction)} function started",
                RunId = request.MembershipRequest.SyncJob.RunId
            }, VerbosityLevel.DEBUG);

            await SendMessagesAsync(request);

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"{nameof(TopicMessageSenderFunction)} function completed",
                RunId = request.MembershipRequest.SyncJob.RunId
            }, VerbosityLevel.DEBUG);
        }

        private async Task SendMessagesAsync(TopicMessageSenderRequest request)
        {
            var membershipRequests = await SplitGroupMembershipAsync(request);
            var destinationType = request.MembershipRequest.SyncJob.MembershipType;

            var index = 0;
            var messages = new List<ServiceBusMessage>();
            var targetSubscription = $"{destinationType}_{request.LaneSize}_{request.InstanceToUse}".ToLowerInvariant();

            foreach (var membership in membershipRequests)
            {
                membership.SyncJob = request.MembershipRequest.SyncJob;
                membership.ProjectedMemberCount = request.MembershipRequest.ProjectedMemberCount;
                membership.TotalMembersToAdd = request.MembershipRequest.MembersToBeAdded;
                membership.TotalMembersToRemove = request.MembershipRequest.MembersToBeRemoved;

                var body = JsonSerializer.Serialize(membership, _jsonSerializerOptions);
                var message = new ServiceBusMessage
                {
                    MessageId = $"{request.MembershipRequest.SyncJob.Id}_{request.MembershipRequest.SyncJob.RunId}_{destinationType}_{++index}",
                    Body = System.Text.Encoding.UTF8.GetBytes(body)
                };

                message.ApplicationProperties.Add("Type", targetSubscription);
                messages.Add(message);
            }

            await _membershipUpdaterSender.AddMessagesAsync(messages);
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Sent {messages.Count} messages with {request.MembershipRequest.MembersToBeUpdated} total operations to {targetSubscription} membership updater",
                RunId = request.MembershipRequest.SyncJob.RunId
            }, VerbosityLevel.INFO);


        }

        private async Task<GroupMembership[]> SplitGroupMembershipAsync(TopicMessageSenderRequest request)
        {
            var blobContent = await DownloadFileAsync(request.MembershipRequest);
            var groupMembership = System.Text.Json.JsonSerializer.Deserialize<GroupMembership>(blobContent);
            var groupMembershipChunks = groupMembership.Split(request.MessageSize);
            return groupMembershipChunks;
        }

        private async Task<string> DownloadFileAsync(MembershipHttpRequest request)
        {
            var blobResult = new BlobResult { BlobStatus = BlobStatus.NotFound };

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Downloading file {request.FilePath}", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);

            blobResult = await _blobStorageRepository.DownloadFileAsync(request.FilePath);
            if (blobResult.BlobStatus == BlobStatus.NotFound)
            {
                throw new FileNotFoundException($"File {request.FilePath} was not found");
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Downloaded file {request.FilePath}", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);
            return blobResult.Content;
        }
    }
}
