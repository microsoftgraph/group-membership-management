// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using DIConcreteTypes;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Models;
using Models.Helpers;
using Models.ServiceBus;
using Repositories.Contracts;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Hosts.MessageSplitter
{
    public class TopicMessageSenderFunction
    {
        private const int MaxBatchSizeInBytes = 256 * 1024;
        private const int ThrottleDelayMs = 500;

        private readonly ILogger<TopicMessageSenderFunction> _logger;
        private readonly IBlobStorageRepository _blobStorageRepository;
        private readonly IServiceBusTopicsRepository _membershipUpdaterSender;
        private readonly JsonSerializerOptions _jsonSerializerOptions;

        public TopicMessageSenderFunction(
            ILogger<TopicMessageSenderFunction> logger,
            [FromKeyedServices("membershipUpdaterSender")] IServiceBusTopicsRepository membershipUpdaterSender,
            IBlobStorageRepository blobStorageRepository)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
            _membershipUpdaterSender = membershipUpdaterSender ?? throw new ArgumentNullException(nameof(membershipUpdaterSender));
            _jsonSerializerOptions = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        }

        [Function(nameof(TopicMessageSenderFunction))]
        public async Task SendMessageAsync([ActivityTrigger] TopicMessageSenderRequest request)
        {
            using (_logger.BeginSyncJobScope(request.MembershipRequest.SyncJob))
            {
                _logger.FunctionStarted(nameof(TopicMessageSenderFunction));
                await SendMessagesAsync(request);
                _logger.FunctionCompleted(nameof(TopicMessageSenderFunction));
            }
        }

        private async Task SendMessagesAsync(TopicMessageSenderRequest request)
        {
            var membershipRequests = await SplitGroupMembershipAsync(request);
            var destinationType = request.MembershipRequest.SyncJob.MembershipType;

            var index = 0;
            var messages = new List<ServiceBusMessage>();
            var targetSubscription = $"{destinationType}_{request.LaneSize}_{request.InstanceToUse}".ToLowerInvariant();
            var isLargeLane = string.Equals(request.LaneSize, "Large", StringComparison.OrdinalIgnoreCase);

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

                if (isLargeLane)
                {
                    message.SessionId = request.MembershipRequest.SyncJob.RunId.ToString();
                }

                messages.Add(message);
            }

            var currentBatch = new List<ServiceBusMessage>();
            long currentBatchSize = 0;
            int totalSent = 0;

            foreach (var message in messages)
            {
                int messageSize = message.Body?.Length ?? 0;

                if (messageSize > MaxBatchSizeInBytes)
                {
                    _logger.MessageExceedsBatchSize(message.MessageId);

                    await _membershipUpdaterSender.AddMessagesAsync(new List<ServiceBusMessage> { message });
                    totalSent++;
                    continue;
                }

                if (currentBatchSize + messageSize > MaxBatchSizeInBytes && currentBatch.Count > 0)
                {

                    await _membershipUpdaterSender.AddMessagesAsync(currentBatch);
                    totalSent += currentBatch.Count;
                    currentBatch.Clear();
                    currentBatchSize = 0;
                    await Task.Delay(ThrottleDelayMs);
                }

                currentBatch.Add(message);
                currentBatchSize += messageSize;
            }

            if (currentBatch.Count > 0)
            {
                await _membershipUpdaterSender.AddMessagesAsync(currentBatch);
                totalSent += currentBatch.Count;
            }

            _logger.SentMessagesToUpdater(totalSent, request.MembershipRequest.MembersToBeUpdated, targetSubscription);
        }

        private async Task<GroupMembership[]> SplitGroupMembershipAsync(TopicMessageSenderRequest request)
        {
            var blobContent = await DownloadFileAsync(request.MembershipRequest);
            string jsonContent;

            try
            {
                // First, try to decompress
                jsonContent = TextCompressor.Decompress(blobContent);
            }
            catch (Exception e) when (e is FormatException || e is InvalidDataException || e is IOException)
            {
                jsonContent = blobContent;
            }

            var groupMembership = System.Text.Json.JsonSerializer.Deserialize<GroupMembership>(jsonContent);
            var groupMembershipChunks = groupMembership.Split(request.MessageSize);
            return groupMembershipChunks;
        }

        private async Task<string> DownloadFileAsync(MembershipHttpRequest request)
        {
            var blobResult = new BlobResult { BlobStatus = BlobStatus.NotFound };

            _logger.DownloadingFile(request.FilePath);

            blobResult = await _blobStorageRepository.DownloadFileAsync(request.FilePath);
            if (blobResult.BlobStatus == BlobStatus.NotFound)
            {
                throw new FileNotFoundException($"File {request.FilePath} was not found");
            }

            _logger.DownloadedFile(request.FilePath);
            return blobResult.Content;
        }
    }
}
