// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.MembershipAggregator.Helpers;
using MembershipAggregator.Services.Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Models;
using Models.Helpers;
using Models.ServiceBus;
using Repositories.Contracts;
using Repositories.Contracts.Helpers;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Hosts.MembershipAggregator
{
    public class DeltaCalculatorFunction
    {
        private readonly ILogger<DeltaCalculatorFunction> _logger;
        private readonly IBlobStorageRepository _blobStorageRepository;
        private readonly IDeltaCalculatorService _deltaCalculatorService;
        private readonly MembershipMergeEngine _mergeEngine;

        public DeltaCalculatorFunction(
            ILogger<DeltaCalculatorFunction> logger,
            IBlobStorageRepository blobStorageRepository,
            IDeltaCalculatorService deltaCalculatorService,
            IOptions<MembershipMergeOptions> mergeOptions)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
            _deltaCalculatorService = deltaCalculatorService ?? throw new ArgumentNullException(nameof(deltaCalculatorService));
            _mergeEngine = new MembershipMergeEngine(_blobStorageRepository, mergeOptions ?? throw new ArgumentNullException(nameof(mergeOptions)));
        }

        [Function(nameof(DeltaCalculatorFunction))]
        public async Task<DeltaCalculatorResponse> CalculateDeltaAsync(
            [ActivityTrigger] DeltaCalculatorRequest request,
            CancellationToken cancellationToken = default)
        {
            using (_logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object>
            {
                ["CurrentPart"] = request.CurrentPart,
                ["TotalParts"] = request.TotalParts
            }))
            {
                _logger.FunctionStarted(nameof(DeltaCalculatorFunction));

                GroupMembership sourceMembership;
                IReadOnlyList<MembershipMergeInput> inputs;
                var destinationMemberCount = request.DestinationMemberCount;

                if (request.ReadFromBlobs)
                {
                    if (string.IsNullOrWhiteSpace(request.SourceMembershipFilePath))
                    {
                        _logger.SourceBlobNotFound();
                        return new DeltaCalculatorResponse
                        {
                            MembershipDeltaStatus = MembershipDeltaStatus.Error
                        };
                    }

                    if (string.IsNullOrWhiteSpace(request.DestinationMembershipFilePath))
                    {
                        _logger.DestinationBlobNotFound();
                        return new DeltaCalculatorResponse
                        {
                            MembershipDeltaStatus = MembershipDeltaStatus.Error
                        };
                    }

                    try
                    {
                        sourceMembership = await ReadEnvelopeAsync(request.SourceMembershipFilePath, cancellationToken);
                    }
                    catch (FileNotFoundException)
                    {
                        _logger.SourceBlobNotFound();
                        return new DeltaCalculatorResponse
                        {
                            MembershipDeltaStatus = MembershipDeltaStatus.Error
                        };
                    }

                    try
                    {
                        await ReadEnvelopeAsync(request.DestinationMembershipFilePath, cancellationToken);
                    }
                    catch (FileNotFoundException)
                    {
                        _logger.DestinationBlobNotFound();
                        return new DeltaCalculatorResponse
                        {
                            MembershipDeltaStatus = MembershipDeltaStatus.Error
                        };
                    }

                    _logger.SourceBlobDownloadResult(BlobStatus.Found.ToString(), request.SourceMembershipFilePath);
                    _logger.DestinationBlobDownloadResult(BlobStatus.Found.ToString(), request.DestinationMembershipFilePath);
                    inputs = new[]
                    {
                        MembershipMergeInput.FromPath(request.SourceMembershipFilePath, MembershipMergeInputKind.Included),
                        MembershipMergeInput.FromPath(request.DestinationMembershipFilePath, MembershipMergeInputKind.Destination)
                    };
                }
                else
                {
                    sourceMembership = JsonSerializer.Deserialize<GroupMembership>(TextCompressor.Decompress(request.SourceGroupMembership));
                    var destinationMembership = JsonSerializer.Deserialize<GroupMembership>(TextCompressor.Decompress(request.DestinationGroupMembership));
                    if (sourceMembership == null || destinationMembership == null)
                    {
                        throw new InvalidDataException("Legacy membership input could not be deserialized.");
                    }
                    destinationMemberCount = destinationMembership.SourceMembers?.Count ?? 0;

                    inputs = new[]
                    {
                        MembershipMergeInput.FromMembers("legacy-source", MembershipMergeInputKind.Included, sourceMembership.SourceMembers ?? new List<AzureADUser>()),
                        MembershipMergeInput.FromMembers("legacy-destination", MembershipMergeInputKind.Destination, destinationMembership.SourceMembers ?? new List<AzureADUser>())
                    };
                }

                var requestedGroupId = request.GroupId != Guid.Empty
                    ? request.GroupId
                    : request.SyncJob.TargetOfficeGroupId;
                var sourceGroupId = sourceMembership.Destination?.ObjectId ?? Guid.Empty;
                if (requestedGroupId != Guid.Empty
                    && sourceGroupId != Guid.Empty
                    && sourceGroupId != requestedGroupId)
                {
                    throw new InvalidDataException(
                        $"The source membership destination '{sourceGroupId}' does not match the requested destination '{requestedGroupId}'.");
                }

                var groupId = requestedGroupId != Guid.Empty ? requestedGroupId : sourceGroupId;
                if (groupId == Guid.Empty)
                {
                    throw new InvalidDataException("The source membership does not identify a destination group.");
                }
                if (sourceMembership.Destination == null)
                {
                    sourceMembership.Destination = new AzureADGroup { ObjectId = groupId };
                }
                else if (sourceMembership.Destination.ObjectId == Guid.Empty)
                {
                    sourceMembership.Destination.ObjectId = groupId;
                }

                var runId = sourceMembership.RunId != Guid.Empty
                    ? sourceMembership.RunId
                    : request.SyncJob.RunId ?? Guid.Empty;
                if (runId == Guid.Empty)
                {
                    throw new InvalidDataException("The source membership does not identify a sync run.");
                }

                var mergeRequest = new MembershipMergeRequest(
                    inputs,
                    $"/{groupId}/delta",
                    $"{request.SyncJob.Id:N}-{runId:N}",
                    Guid.NewGuid(),
                    sourceMembership);
                var fromTo = $"to {sourceMembership.Destination}";
                _logger.CalculatingMembershipDifference(fromTo, destinationMemberCount);
                var stopwatch = Stopwatch.StartNew();
                var mergeResult = await _mergeEngine.ExecuteAsync(mergeRequest, cancellationToken: cancellationToken);
                stopwatch.Stop();
                _logger.CalculatedMembershipDifference(fromTo, stopwatch.Elapsed.TotalSeconds, mergeResult.AddCount, mergeResult.RemoveCount);
                var response = await _deltaCalculatorService.CalculateDifferenceAsync(
                    sourceMembership,
                    new MembershipDeltaSummary(
                        mergeResult.SourceMemberCount,
                        mergeResult.DestinationMemberCount,
                        mergeResult.AddCount,
                        mergeResult.RemoveCount));

                _logger.FunctionCompleted(nameof(DeltaCalculatorFunction));
                return new DeltaCalculatorResponse
                {
                    MembersToAddCount = response.MembersToAddCount,
                    MembersToRemoveCount = response.MembersToRemoveCount,
                    SourceMemberCount = mergeResult.SourceMemberCount,
                    DestinationMemberCount = mergeResult.DestinationMemberCount,
                    MembershipDeltaStatus = response.MembershipDeltaStatus,
                    MembersToAddFilePath = mergeResult.AdditionsPath,
                    MembersToRemoveFilePath = mergeResult.RemovalsPath,
                    DeltaManifestFilePath = mergeResult.ManifestPath,
                    UseStagedDeltaFiles = true
                };
            }
        }

        private async Task<GroupMembership> ReadEnvelopeAsync(
            string filePath,
            CancellationToken cancellationToken)
        {
            GroupMembership envelope = null;

            await foreach (var _ in _blobStorageRepository.StreamMembershipAsync(filePath, details => envelope = details, cancellationToken))
            {
                if (envelope != null)
                {
                    break;
                }
            }

            return envelope ?? throw new InvalidDataException($"Membership file '{filePath}' does not contain a valid envelope.");
        }
    }
}
