// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using MembershipAggregator.Services.Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models;
using Models.Helpers;
using Models.ServiceBus;
using Repositories.Contracts;
using Repositories.Contracts.Helpers;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace Hosts.MembershipAggregator
{
    public class DeltaCalculatorFunction
    {
        private readonly ILogger<DeltaCalculatorFunction> _logger;
        private readonly IBlobStorageRepository _blobStorageRepository;
        private readonly IDeltaCalculatorService _deltaCalculatorService;

        public DeltaCalculatorFunction(
            ILogger<DeltaCalculatorFunction> logger,
            IBlobStorageRepository blobStorageRepository,
            IDeltaCalculatorService deltaCalculatorService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
            _deltaCalculatorService = deltaCalculatorService ?? throw new ArgumentNullException(nameof(deltaCalculatorService));
        }

        [Function(nameof(DeltaCalculatorFunction))]
        public async Task<DeltaCalculatorResponse> CalculateDeltaAsync([ActivityTrigger] DeltaCalculatorRequest request)
        {
            using (_logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object>
            {
                ["CurrentPart"] = request.CurrentPart,
                ["TotalParts"] = request.TotalParts
            }))
            {
                _logger.FunctionStarted(nameof(DeltaCalculatorFunction));

                GroupMembership sourceMembership;
                GroupMembership destinationMembership;

                if (request.ReadFromBlobs)
                {
                    var sourceBlobResult = await _blobStorageRepository.DownloadFileAsync(request.SourceMembershipFilePath);
                    _logger.SourceBlobDownloadResult(sourceBlobResult.BlobStatus.ToString(), request.SourceMembershipFilePath);

                    var destinationBlobResult = await _blobStorageRepository.DownloadFileAsync(request.DestinationMembershipFilePath);
                    _logger.DestinationBlobDownloadResult(destinationBlobResult.BlobStatus.ToString(), request.DestinationMembershipFilePath);

                    if (sourceBlobResult.BlobStatus == BlobStatus.NotFound)
                    {
                        _logger.SourceBlobNotFound();
                        return new DeltaCalculatorResponse
                        {
                            MembershipDeltaStatus = MembershipDeltaStatus.Error
                        };
                    }

                    if (destinationBlobResult.BlobStatus == BlobStatus.NotFound)
                    {
                        _logger.DestinationBlobNotFound();
                        return new DeltaCalculatorResponse
                        {
                            MembershipDeltaStatus = MembershipDeltaStatus.Error
                        };
                    }

                    var sourceJson = TryDecompress(sourceBlobResult.Content);
                    var destinationJson = TryDecompress(destinationBlobResult.Content);

                    sourceMembership = JsonSerializer.Deserialize<GroupMembership>(sourceJson);
                    destinationMembership = JsonSerializer.Deserialize<GroupMembership>(destinationJson);
                }
                else
                {
                    sourceMembership = JsonSerializer.Deserialize<GroupMembership>(TextCompressor.Decompress(request.SourceGroupMembership));
                    destinationMembership = JsonSerializer.Deserialize<GroupMembership>(TextCompressor.Decompress(request.DestinationGroupMembership));
                }

                var response = await _deltaCalculatorService.CalculateDifferenceAsync(sourceMembership, destinationMembership);

                _logger.FunctionCompleted(nameof(DeltaCalculatorFunction));
                return new DeltaCalculatorResponse
                {
                    MembersToAddCount = response.MembersToAdd?.Count ?? 0,
                    MembersToRemoveCount = response.MembersToRemove?.Count ?? 0,
                    MembershipDeltaStatus = response.MembershipDeltaStatus,
                    CompressedMembersToAddJSON = TextCompressor.Compress(JsonSerializer.Serialize(response.MembersToAdd)),
                    CompressedMembersToRemoveJSON = TextCompressor.Compress(JsonSerializer.Serialize(response.MembersToRemove)),
                };
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
    }
}
