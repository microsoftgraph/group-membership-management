// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.MembershipAggregator.Helpers;
using Microsoft.Azure.Functions.Worker;
using Models;
using Models.Helpers;
using Models.ServiceBus;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Hosts.MembershipAggregator
{
    public class AggregatedMembershipUploaderFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IBlobStorageRepository _blobStorageRepository;

        public AggregatedMembershipUploaderFunction(ILoggingRepository loggingRepository, IBlobStorageRepository blobStorageRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
        }

        [Function(nameof(AggregatedMembershipUploaderFunction))]
        public async Task<AggregatedMembershipUploadResponse> UploadAggregatedMembershipAsync([ActivityTrigger] AggregatedMembershipUploadRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Starting aggregated membership upload for GroupId {request.GroupId}",
                RunId = request.RunId
            }, VerbosityLevel.DEBUG);

            if (request.GroupId == Guid.Empty)
            {
                return Failure("GroupId is required for aggregated membership upload.");
            }

            if (string.IsNullOrWhiteSpace(request.SourceMembershipFilePath))
            {
                return Failure("Source membership file path is missing.");
            }

            try
            {
                var sourceBlob = await _blobStorageRepository.DownloadFileAsync(request.SourceMembershipFilePath);
                if (sourceBlob.BlobStatus == BlobStatus.NotFound)
                {
                    return Failure($"Source membership blob was not found at path {request.SourceMembershipFilePath}.");
                }

                var sourceMembershipJson = TryDecompress(sourceBlob.Content);
                var sourceMembership = JsonSerializer.Deserialize<GroupMembership>(sourceMembershipJson);
                if (sourceMembership == null)
                {
                    return Failure("Source membership payload could not be deserialized.");
                }

                var membersToAdd = DeserializeMembers(request.CompressedMembersToAddJson);
                var membersToRemove = DeserializeMembers(request.CompressedMembersToRemoveJson);

                var aggregatedMembership = (GroupMembership)sourceMembership.Clone();
                aggregatedMembership.SourceMembers.Clear();

                if (membersToAdd != null)
                {
                    aggregatedMembership.SourceMembers.AddRange(membersToAdd);
                }

                if (membersToRemove != null)
                {
                    aggregatedMembership.SourceMembers.AddRange(membersToRemove);
                }

                var memberCount = aggregatedMembership.SourceMembers.Count;

                var currentTime = request.CurrentUtcDateTime == default ? DateTime.UtcNow : request.CurrentUtcDateTime;
                var filePath = MembershipFilePathHelper.BuildFilePath(request.SyncJob, request.GroupId, "Aggregated", currentTime);

                var serializerOptions = new JsonSerializerOptions
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
                };

                var content = TextCompressor.Compress(JsonSerializer.Serialize(aggregatedMembership, serializerOptions));
                await _blobStorageRepository.UploadFileAsync(filePath, content);

                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Aggregated membership file uploaded to {filePath}",
                    RunId = request.RunId
                }, VerbosityLevel.DEBUG);

                return new AggregatedMembershipUploadResponse
                {
                    IsSuccessful = true,
                    FilePath = filePath,
                    MemberCount = memberCount
                };
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Aggregated membership upload failed: {ex.Message}",
                    RunId = request.RunId
                }, VerbosityLevel.DEBUG);

                return Failure(ex.Message);
            }
        }

        private static string TryDecompress(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return content;
            }

            try
            {
                return TextCompressor.Decompress(content);
            }
            catch (FormatException)
            {
                return content;
            }
        }

        private static ICollection<AzureADUser> DeserializeMembers(string compressedMembersJson)
        {
            if (string.IsNullOrWhiteSpace(compressedMembersJson))
            {
                return Array.Empty<AzureADUser>();
            }

            var decompressed = TextCompressor.Decompress(compressedMembersJson);
            return JsonSerializer.Deserialize<ICollection<AzureADUser>>(decompressed) ?? Array.Empty<AzureADUser>();
        }

        private static AggregatedMembershipUploadResponse Failure(string message)
        {
            return new AggregatedMembershipUploadResponse
            {
                IsSuccessful = false,
                ErrorMessage = message
            };
        }
    }
}
