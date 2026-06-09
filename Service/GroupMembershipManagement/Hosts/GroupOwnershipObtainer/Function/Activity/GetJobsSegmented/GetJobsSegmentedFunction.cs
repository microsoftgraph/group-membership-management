// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts.Helpers;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.GroupOwnershipObtainer
{
    public class GetJobsSegmentedFunction
    {
        private readonly ILogger<GetJobsSegmentedFunction> _logger;
        private readonly IGroupOwnershipObtainerService _groupOwnershipObtainerService;

        public GetJobsSegmentedFunction(ILogger<GetJobsSegmentedFunction> logger, IGroupOwnershipObtainerService groupOwnershipObtainerService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _groupOwnershipObtainerService = groupOwnershipObtainerService ?? throw new ArgumentNullException(nameof(groupOwnershipObtainerService));
        }

        [Function(nameof(GetJobsSegmentedFunction))]
        public async Task<List<SyncJob>> GetJobsAsync([ActivityTrigger] GetJobsSegmentedRequest request)
        {
            using var scope = _logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object>
            {
                ["CurrentPart"] = request.CurrentPart,
                ["TotalParts"] = request.TotalParts
            });
            _logger.FunctionStarted(nameof(GetJobsSegmentedFunction));
            var responsePage = await _groupOwnershipObtainerService.GetSyncJobsSegmentAsync();
            _logger.FunctionCompleted(nameof(GetJobsSegmentedFunction));
            _logger.SegmentedJobsCount(nameof(GetJobsSegmentedFunction), responsePage.Count);

            return responsePage;
        }
    }
}
