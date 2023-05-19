// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Models.Helpers;
using Models.ServiceBus;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Newtonsoft.Json;
using Repositories.Contracts;
using Services.Contracts;
using System;
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

        [FunctionName(nameof(DeltaCalculatorFunction))]
        public async Task<DeltaCalculatorResponse> CalculateDeltaAsync([ActivityTrigger] DeltaCalculatorRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(DeltaCalculatorFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);

            GroupMembership sourceMembership;
            GroupMembership destinationMembership;

            _deltaCalculatorService.RunId = request.RunId.GetValueOrDefault(Guid.Empty);

            if (request.ReadFromBlobs)
            {
                var sourceBlobResult = await _blobStorageRepository.DownloadFileAsync(request.SourceMembershipFilePath);
                var destinationBlobResult = await _blobStorageRepository.DownloadFileAsync(request.DestinationMembershipFilePath);

                await _blobStorageRepository.DeleteFileAsync(request.SourceMembershipFilePath);
                await _blobStorageRepository.DeleteFileAsync(request.DestinationMembershipFilePath);

                sourceMembership = JsonConvert.DeserializeObject<GroupMembership>(sourceBlobResult.Content);
                destinationMembership = JsonConvert.DeserializeObject<GroupMembership>(destinationBlobResult.Content);
            }
            else
            {
                sourceMembership = JsonConvert.DeserializeObject<GroupMembership>(TextCompressor.Decompress(request.SourceGroupMembership));
                destinationMembership = JsonConvert.DeserializeObject<GroupMembership>(TextCompressor.Decompress(request.DestinationGroupMembership));
            }

            var response = await _deltaCalculatorService.CalculateDifferenceAsync(sourceMembership, destinationMembership);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(DeltaCalculatorFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
            return new DeltaCalculatorResponse
            {
                MembersToAddCount = response.MembersToAdd?.Count ?? 0,
                MembersToRemoveCount = response.MembersToRemove?.Count ?? 0,
                MembershipDeltaStatus = response.MembershipDeltaStatus,
                CompressedMembersToAddJSON = TextCompressor.Compress(JsonConvert.SerializeObject(response.MembersToAdd)),
                CompressedMembersToRemoveJSON = TextCompressor.Compress(JsonConvert.SerializeObject(response.MembersToRemove)),
            };
        }
    }
}
