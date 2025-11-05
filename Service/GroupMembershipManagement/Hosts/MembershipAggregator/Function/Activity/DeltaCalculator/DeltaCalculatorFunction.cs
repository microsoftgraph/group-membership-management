// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.WebJobs;
using Models;
using Models.Helpers;
using Models.ServiceBus;
using Repositories.Contracts;
using Services.Contracts;
using Services.Entities;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace Hosts.MembershipAggregator
{
    public class DeltaCalculatorFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IBlobStorageRepository _blobStorageRepository;
        private readonly IDeltaCalculatorService _deltaCalculatorService;

        public DeltaCalculatorFunction(
            ILoggingRepository loggingRepository,
            IBlobStorageRepository blobStorageRepository,
            IDeltaCalculatorService deltaCalculatorService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
            _deltaCalculatorService = deltaCalculatorService ?? throw new ArgumentNullException(nameof(deltaCalculatorService));
            _blobStorageRepository = blobStorageRepository;
        }

        [Function(nameof(DeltaCalculatorFunction))]
        public async Task<DeltaCalculatorResponse> CalculateDeltaAsync([ActivityTrigger] DeltaCalculatorRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(DeltaCalculatorFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);

            GroupMembership sourceMembership;
            GroupMembership destinationMembership;

            _deltaCalculatorService.RunId = request.RunId;

            if (request.ReadFromBlobs)
            {
                var sourceBlobResult = await _blobStorageRepository.DownloadFileAsync(request.SourceMembershipFilePath);
                await _loggingRepository.LogMessageAsync(
                    new LogMessage
                    {
                        Message = $"Source blob download result: {sourceBlobResult.BlobStatus} for path {request.SourceMembershipFilePath}",
                        RunId = request.RunId
                    },
                    VerbosityLevel.DEBUG
                );
                var destinationBlobResult = await _blobStorageRepository.DownloadFileAsync(request.DestinationMembershipFilePath);

                await _loggingRepository.LogMessageAsync(
                    new LogMessage
                    {
                        Message = $"Destination blob download result: {destinationBlobResult.BlobStatus} for path {request.DestinationMembershipFilePath}",
                        RunId = request.RunId
                    },
                    VerbosityLevel.DEBUG
                );

                if (sourceBlobResult.BlobStatus == BlobStatus.NotFound)
                {
                    await _loggingRepository.LogMessageAsync(
                        new LogMessage
                        {
                            Message = "SourceMembership blob not found",
                            RunId = request.RunId
                        },
                        VerbosityLevel.DEBUG
                    );

                    return new DeltaCalculatorResponse
                    {
                        MembershipDeltaStatus = MembershipDeltaStatus.Error
                    };
                }

                if (destinationBlobResult.BlobStatus == BlobStatus.NotFound)
                {
                    await _loggingRepository.LogMessageAsync(
                        new LogMessage
                        {
                            Message = "DestinationMembership blob not found",
                            RunId = request.RunId
                        },
                        VerbosityLevel.DEBUG
                    );

                    return new DeltaCalculatorResponse
                    {
                        MembershipDeltaStatus = MembershipDeltaStatus.Error
                    };
                }

                sourceMembership = JsonSerializer.Deserialize<GroupMembership>(sourceBlobResult.Content);
                destinationMembership = JsonSerializer.Deserialize<GroupMembership>(destinationBlobResult.Content);
            }
            else
            {
                sourceMembership = JsonSerializer.Deserialize<GroupMembership>(TextCompressor.Decompress(request.SourceGroupMembership));
                destinationMembership = JsonSerializer.Deserialize<GroupMembership>(TextCompressor.Decompress(request.DestinationGroupMembership));
            }

            var response = await _deltaCalculatorService.CalculateDifferenceAsync(sourceMembership, destinationMembership);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(DeltaCalculatorFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
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
}
