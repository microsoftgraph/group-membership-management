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
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
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
        public async Task<AggregatedMembershipUploadResponse> UploadAggregatedMembershipAsync(
            [ActivityTrigger] AggregatedMembershipUploadRequest request,
            CancellationToken cancellationToken = default)
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

                    if (request.MembersToAddCount < 0 || request.MembersToRemoveCount < 0)
                    {
                        return Failure("Delta member counts cannot be negative.");
                    }

                    var hasAnyStagedDeltaPath =
                        !string.IsNullOrWhiteSpace(request.MembersToAddFilePath)
                        || !string.IsNullOrWhiteSpace(request.MembersToRemoveFilePath);
                    var useStagedDeltaFiles = request.UseStagedDeltaFiles ?? hasAnyStagedDeltaPath;
                    IAsyncEnumerable<AzureADUser> members;
                    int membersToAddCount;
                    int membersToRemoveCount;

                    if (useStagedDeltaFiles)
                    {
                        if (string.IsNullOrWhiteSpace(request.MembersToAddFilePath)
                            || string.IsNullOrWhiteSpace(request.MembersToRemoveFilePath))
                        {
                            return Failure("Both staged addition and removal paths are required.");
                        }

                        membersToAddCount = request.MembersToAddCount;
                        membersToRemoveCount = request.MembersToRemoveCount;
                        members = ReadStagedDeltaAsync(request, cancellationToken);
                    }
                    else
                    {
                        var hasExplicitTransportMode = request.UseStagedDeltaFiles.HasValue;
                        var hasDeclaredChanges =
                            request.MembersToAddCount > 0
                            || request.MembersToRemoveCount > 0;
                        if (hasDeclaredChanges
                            && string.IsNullOrWhiteSpace(request.CompressedMembersToAddJson)
                            && string.IsNullOrWhiteSpace(request.CompressedMembersToRemoveJson))
                        {
                            return Failure("Delta counts report changes, but no staged or legacy delta members were supplied.");
                        }

                        var membersToAdd = DeserializeMembers(request.CompressedMembersToAddJson);
                        var membersToRemove = DeserializeMembers(request.CompressedMembersToRemoveJson);
                        membersToAddCount = membersToAdd.Count;
                        membersToRemoveCount = membersToRemove.Count;

                        if ((hasExplicitTransportMode || hasDeclaredChanges)
                            && (membersToAddCount != request.MembersToAddCount
                                || membersToRemoveCount != request.MembersToRemoveCount))
                        {
                            return Failure("Declared delta counts do not match the legacy delta members.");
                        }

                        members = ReadLegacyDeltaAsync(membersToAdd, membersToRemove, cancellationToken);
                    }

                    var currentTime = request.CurrentUtcDateTime == default ? DateTime.UtcNow : request.CurrentUtcDateTime;
                    var filePath = MembershipFilePathHelper.BuildFilePath(request.SyncJob, request.GroupId, "Aggregated", currentTime);
                    var aggregatedMembership = BuildAggregatedMembership(request, membersToAddCount, membersToRemoveCount);
                    await _blobStorageRepository.WriteMembershipAsync(filePath, aggregatedMembership, members, cancellationToken: cancellationToken);

                    _logger.AggregatedUploadComplete(filePath);

                    return new AggregatedMembershipUploadResponse
                    {
                        IsSuccessful = true,
                        FilePath = filePath,
                        MemberCount = membersToAddCount + membersToRemoveCount
                    };
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OutOfMemoryException)
                {
                    throw;
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
            int membersToAddCount,
            int membersToRemoveCount)
        {
            var runId = request.SyncJob.RunId ?? Guid.Empty;

            var destination = new AzureADGroup
            {
                ObjectId = request.GroupId,
                Name = request.SyncJob.DestinationName?.Name,
                Email = request.SyncJob.DestinationEmail?.Email
            };

            var aggregatedMembership = new GroupMembership
            {
                Destination = destination,
                SyncJobId = request.SyncJob.Id,
                SyncJob = request.SyncJob,
                RunId = runId,
                MembershipObtainerDryRunEnabled = request.SyncJob.IsDryRunEnabled,
                Exclusionary = false,
                ProjectedMemberCount = membersToAddCount + membersToRemoveCount,
                TotalMembersToAdd = membersToAddCount,
                TotalMembersToRemove = membersToRemoveCount,
                Query = request.SyncJob.Query
            };

            return aggregatedMembership;
        }

        private async IAsyncEnumerable<AzureADUser> ReadStagedDeltaAsync(
            AggregatedMembershipUploadRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var member in ReadAndValidateStagedMembersAsync(request.MembersToAddFilePath, MembershipAction.Add, request.MembersToAddCount, cancellationToken))
            {
                yield return member;
            }

            await foreach (var member in ReadAndValidateStagedMembersAsync(request.MembersToRemoveFilePath, MembershipAction.Remove, request.MembersToRemoveCount, cancellationToken))
            {
                yield return member;
            }
        }

        private async IAsyncEnumerable<AzureADUser> ReadAndValidateStagedMembersAsync(
            string path,
            MembershipAction expectedAction,
            int expectedCount,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var actualCount = 0;
            await foreach (var member in _blobStorageRepository.StreamMembershipAsync(path, cancellationToken: cancellationToken))
            {
                if (member.MembershipAction != expectedAction)
                {
                    throw new InvalidDataException(
                        $"Staged delta member '{member.ObjectId}' in '{path}' has action " +
                        $"{member.MembershipAction}, expected {expectedAction}.");
                }

                actualCount++;
                yield return member;
            }

            if (actualCount != expectedCount)
            {
                throw new InvalidDataException($"Staged delta '{path}' contains {actualCount} members, expected {expectedCount}.");
            }
        }

        private static async IAsyncEnumerable<AzureADUser> ReadLegacyDeltaAsync(
            ICollection<AzureADUser> membersToAdd,
            ICollection<AzureADUser> membersToRemove,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var member in membersToAdd)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return member;
            }

            foreach (var member in membersToRemove)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return member;
            }

            await Task.CompletedTask;
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
