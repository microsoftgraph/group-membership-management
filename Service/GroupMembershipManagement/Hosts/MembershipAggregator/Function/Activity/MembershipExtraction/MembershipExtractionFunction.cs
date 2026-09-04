// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.MembershipAggregator.Helpers;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Models;
using Models.Helpers;
using Models.ServiceBus;
using Repositories.Contracts;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Hosts.MembershipAggregator
{
    public class MembershipExtractionFunction
    {
        private readonly ILogger<MembershipExtractionFunction> _logger;
        private readonly IBlobStorageRepository _blobStorageRepository;
        private readonly MembershipMergeEngine _mergeEngine;

        public MembershipExtractionFunction(
            ILogger<MembershipExtractionFunction> logger,
            IBlobStorageRepository blobStorageRepository,
            IOptions<MembershipMergeOptions> mergeOptions)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _blobStorageRepository = blobStorageRepository
                ?? throw new ArgumentNullException(nameof(blobStorageRepository));
            _mergeEngine = new MembershipMergeEngine(
                _blobStorageRepository,
                mergeOptions ?? throw new ArgumentNullException(nameof(mergeOptions)));
        }

        [Function(nameof(MembershipExtractionFunction))]
        public async Task<MembershipExtractionResponse> ExtractMembershipAsync(
            [ActivityTrigger] MembershipExtractionRequest request,
            CancellationToken cancellationToken = default)
        {
            using (_logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object>
            {
                ["CurrentPart"] = request.CurrentPart,
                ["TotalParts"] = request.TotalParts
            }))
            {
                _logger.ExtractingMembershipInfo(request.CompletedParts.Count);

                try
                {
                    var currentUtcDateTime = request.CurrentUtcDateTime == default
                        ? DateTime.UtcNow
                        : request.CurrentUtcDateTime;
                    var groupId = request.GroupId != Guid.Empty
                        ? request.GroupId
                        : request.SyncJob?.TargetOfficeGroupId ?? Guid.Empty;

                    if (groupId == Guid.Empty)
                    {
                        throw new InvalidOperationException(
                            "Unable to determine a valid group identifier for membership extraction output.");
                    }

                    var inputs = new List<MembershipMergeInput>();
                    GroupMembership firstSourceEnvelope = null;

                    foreach (var part in request.CompletedParts.Where(
                        part => !string.Equals(
                            part,
                            request.DestinationPart,
                            StringComparison.Ordinal)))
                    {
                        var envelope = await ReadEnvelopeAsync(
                            part,
                            isDestination: false,
                            cancellationToken);
                        firstSourceEnvelope ??= envelope;
                        inputs.Add(MembershipMergeInput.FromPath(
                            part,
                            envelope.Exclusionary
                                ? MembershipMergeInputKind.Excluded
                                : MembershipMergeInputKind.Included));
                    }

                    if (firstSourceEnvelope == null)
                    {
                        return new MembershipExtractionResponse
                        {
                            IsSuccessful = false,
                            ErrorMessage = "SourceMembership could not be extracted"
                        };
                    }

                    GroupMembership destinationEnvelope = null;
                    var destinationExpected =
                        !string.IsNullOrWhiteSpace(request.DestinationPart)
                        && request.CompletedParts.Contains(request.DestinationPart);
                    if (destinationExpected)
                    {
                        destinationEnvelope = await ReadEnvelopeAsync(
                            request.DestinationPart,
                            isDestination: true,
                            cancellationToken);
                        inputs.Add(MembershipMergeInput.FromPath(
                            request.DestinationPart,
                            MembershipMergeInputKind.Destination));
                    }

                    var attemptId = Guid.NewGuid();
                    var sourceFilePath = MembershipFilePathHelper.BuildFilePath(
                        request.SyncJob,
                        groupId,
                        $"SourceMembership-{attemptId:N}",
                        currentUtcDateTime);
                    var destinationFilePath = destinationEnvelope == null
                        ? null
                        : MembershipFilePathHelper.BuildFilePath(
                            request.SyncJob,
                            groupId,
                            $"DestinationMembership-{attemptId:N}",
                            currentUtcDateTime);

                    var snapshot = await _mergeEngine.StageSnapshotAsync(
                        inputs,
                        sourceFilePath,
                        firstSourceEnvelope,
                        destinationFilePath,
                        destinationEnvelope,
                        cancellationToken: cancellationToken);

                    _logger.MembershipExtractionSuccess(
                        snapshot.SourceMemberCount,
                        snapshot.DestinationMemberCount);

                    return new MembershipExtractionResponse
                    {
                        IsSuccessful = true,
                        SourceMembershipFilePath = sourceFilePath,
                        DestinationMembershipFilePath = destinationFilePath,
                        SourceMemberCount = snapshot.SourceMemberCount,
                        DestinationMemberCount = snapshot.DestinationMemberCount
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
                    _logger.MembershipExtractionError(ex, ex.Message);

                    return new MembershipExtractionResponse
                    {
                        IsSuccessful = false,
                        ErrorMessage = ex.Message
                    };
                }
            }
        }

        private async Task<GroupMembership> ReadEnvelopeAsync(
            string filePath,
            bool isDestination,
            CancellationToken cancellationToken)
        {
            GroupMembership envelope = null;

            try
            {
                await foreach (var _ in _blobStorageRepository.StreamMembershipAsync(
                    filePath,
                    details =>
                    {
                        envelope = details;
                    },
                    cancellationToken))
                {
                    if (envelope != null)
                    {
                        break;
                    }
                }
            }
            catch (FileNotFoundException)
            {
                throw new FileNotFoundException($"File {filePath} was not found");
            }
            catch (InvalidDataException ex)
                when (ex.Message.Contains("empty", StringComparison.OrdinalIgnoreCase))
            {
                var fileDescription = isDestination
                    ? $"destination file '{filePath}'"
                    : $"file '{filePath}'";
                throw new InvalidOperationException(
                    $"Content for {fileDescription} is null or empty",
                    ex);
            }
            catch (Exception ex) when (
                ex is FormatException
                || ex is InvalidDataException
                || ex is System.Text.Json.JsonException)
            {
                var fileDescription = isDestination
                    ? $"destination file '{filePath}'"
                    : $"file '{filePath}'";
                throw new InvalidOperationException(
                    $"Failed to deserialize JSON content from {fileDescription}: {ex.Message}",
                    ex);
            }

            return envelope
                ?? throw new InvalidDataException(
                    $"Membership file '{filePath}' does not contain a valid envelope.");
        }
    }
}
