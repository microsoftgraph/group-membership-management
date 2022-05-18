// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
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
        private readonly IDeltaCalculatorService _deltaCalculatorService;

        public DeltaCalculatorFunction(ILoggingRepository loggingRepository, IDeltaCalculatorService deltaCalculatorService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _deltaCalculatorService = deltaCalculatorService ?? throw new ArgumentNullException(nameof(deltaCalculatorService));
        }

        [FunctionName(nameof(DeltaCalculatorFunction))]
        public async Task<DeltaResponse> CalculateDeltaAsync([ActivityTrigger] DeltaCalculatorRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(DeltaCalculatorFunction)} function started", RunId = request.SourceGroupMembership.RunId });
            var response = await _deltaCalculatorService.CalculateDifferenceAsync(request.SourceGroupMembership, request.DestinationGroupMembership);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(DeltaCalculatorFunction)} function completed", RunId = request.SourceGroupMembership.RunId });
            return response;
        }
    }
}
