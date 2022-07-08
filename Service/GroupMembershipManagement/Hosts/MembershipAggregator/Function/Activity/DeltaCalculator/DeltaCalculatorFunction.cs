// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Entities.ServiceBus;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Newtonsoft.Json;
using Repositories.Contracts;
using Services.Contracts;
using Services.Entities;
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
        public async Task<DeltaResponse> CalculateDeltaAsync([ActivityTrigger] DeltaCalculatorRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(DeltaCalculatorFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);

            GroupMembership sourceMembership;
            GroupMembership destinationMembership;

            if (request.ReadFromBlobs)
            {
                var sourceBlobResult = await _blobStorageRepository.DownloadFileAsync(request.SourceMembershipFilePath);
                var destinationBlobResult = await _blobStorageRepository.DownloadFileAsync(request.DestinationMembershipFilePath);

                await _blobStorageRepository.DeleteFileAsync(request.SourceMembershipFilePath);
                await _blobStorageRepository.DeleteFileAsync(request.DestinationMembershipFilePath);

                sourceMembership = JsonConvert.DeserializeObject<GroupMembership>(sourceBlobResult.Content.ToString());
                destinationMembership = JsonConvert.DeserializeObject<GroupMembership>(destinationBlobResult.Content.ToString());
            }
            else
            {
                sourceMembership = request.SourceGroupMembership;
                destinationMembership = request.DestinationGroupMembership;
            }

            var response = await _deltaCalculatorService.CalculateDifferenceAsync(sourceMembership, destinationMembership);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(DeltaCalculatorFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
            return response;
        }
    }
}
