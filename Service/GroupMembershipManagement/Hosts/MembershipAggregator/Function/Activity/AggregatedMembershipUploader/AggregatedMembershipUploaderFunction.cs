// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.MembershipAggregator.Helpers;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models;
using Models.Helpers;
using Models.ServiceBus;
using Repositories.Contracts;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Hosts.MembershipAggregator
{
    public class AggregatedMembershipUploaderFunction
    {
        private readonly ILogger<AggregatedMembershipUploaderFunction> _logger;
        private readonly IBlobStorageRepository _blobStorageRepository;

        public AggregatedMembershipUploaderFunction(ILogger<AggregatedMembershipUploaderFunction> logger, IBlobStorageRepository blobStorageRepository)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
        }

        [Function(nameof(AggregatedMembershipUploaderFunction))]
        public async Task<AggregatedMembershipUploadResponse> UploadAggregatedMembershipAsync([ActivityTrigger] AggregatedMembershipUploadRequest request)
        {
            using (_logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object>
            {
                ["CurrentPart"] = request.CurrentPart,
                ["TotalParts"] = request.TotalParts
            }))
            {
                _logger.StartingAggregatedUpload(request.GroupId);

                if (request.GroupId == Guid.Empty)
                {
                    return Failure("GroupId is required for aggregated membership upload.");
                }

                try
                {
                    if (request.SyncJob == null)
                    {
                        return Failure("Sync job information is required for aggregated membership upload.");
                    }

                    if (string.IsNullOrWhiteSpace(request.SourceMembershipFilePath))
                    {
                        return Failure("Source membership file path is missing.");
                    }

                    var metadata = await _blobStorageRepository.GetBlobMetadataAsync(request.SourceMembershipFilePath);
                    if (metadata == null || metadata.BlobStatus == BlobStatus.NotFound)
                    {
                        return Failure($"Source membership blob was not found at path {request.SourceMembershipFilePath}.");
                    }
                    var membersToAdd = DeserializeMembers(request.CompressedMembersToAddJson);
                    var membersToRemove = DeserializeMembers(request.CompressedMembersToRemoveJson);

                    var aggregatedMembership = BuildAggregatedMembership(request, membersToAdd, membersToRemove);
                    var memberCount = aggregatedMembership.SourceMembers.Count;

                    var currentTime = request.CurrentUtcDateTime == default ? DateTime.UtcNow : request.CurrentUtcDateTime;
                    var filePath = MembershipFilePathHelper.BuildFilePath(request.SyncJob, request.GroupId, "Aggregated", currentTime);

                    var serializerOptions = new JsonSerializerOptions
                    {
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
                    };

                    var content = TextCompressor.Compress(JsonSerializer.Serialize(aggregatedMembership, serializerOptions));
                    await _blobStorageRepository.UploadFileAsync(filePath, content);

                    _logger.AggregatedUploadComplete(filePath);

                    return new AggregatedMembershipUploadResponse
                    {
                        IsSuccessful = true,
                        FilePath = filePath,
                        MemberCount = memberCount
                    };
                }
                catch (Exception ex)
                {
                    _logger.AggregatedUploadFailed(ex, ex.Message);
                    return Failure(ex.Message);
                }
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

        private static GroupMembership BuildAggregatedMembership(
            AggregatedMembershipUploadRequest request,
            ICollection<AzureADUser> membersToAdd,
            ICollection<AzureADUser> membersToRemove)
        {
            var runId = request.SyncJob.RunId ?? Guid.Empty;

            var destination = new AzureADGroup
            {
                ObjectId = request.GroupId,
                Name = request.SyncJob.DestinationName?.Name,
                Email = request.SyncJob.DestinationEmail?.Email
            };

            var totalMembersToAdd = membersToAdd?.Count ?? 0;
            var totalMembersToRemove = membersToRemove?.Count ?? 0;
            var aggregatedMembershipMembers = ExtractAggregatedMembers(membersToAdd, membersToRemove);

            var aggregatedMembership = new GroupMembership
            {
                Destination = destination,
                SyncJobId = request.SyncJob.Id,
                SyncJob = request.SyncJob,
                RunId = runId,
                MembershipObtainerDryRunEnabled = request.SyncJob.IsDryRunEnabled,
                Exclusionary = false,
                ProjectedMemberCount = totalMembersToAdd + totalMembersToRemove,
                TotalMembersToAdd = totalMembersToAdd,
                TotalMembersToRemove = totalMembersToRemove,
                Query = request.SyncJob.Query,
                SourceMembers = aggregatedMembershipMembers
            };

            return aggregatedMembership;
        }

        private static List<AzureADUser> ExtractAggregatedMembers(
            ICollection<AzureADUser> membersToAdd,
            ICollection<AzureADUser> membersToRemove)
        {
            List<AzureADUser> aggregatedMembers;
            if (membersToAdd is List<AzureADUser> addList)
            {
                aggregatedMembers = addList;
            }
            else if (membersToAdd != null)
            {
                aggregatedMembers = new List<AzureADUser>(membersToAdd);
            }
            else
            {
                aggregatedMembers = new List<AzureADUser>();
            }

            if (membersToRemove != null && membersToRemove.Count > 0)
            {
                aggregatedMembers.AddRange(membersToRemove);
            }

            return aggregatedMembers;
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
