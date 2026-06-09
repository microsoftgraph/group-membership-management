// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Repositories.Contracts.Helpers;
using Services;
using System.Threading.Tasks;

namespace Hosts.PlaceMembershipObtainer
{
    public class JobStatusUpdaterFunction
    {
        private readonly ILogger<JobStatusUpdaterFunction> _logger;
        private readonly PlaceMembershipObtainerService _membershipProviderService;

        public JobStatusUpdaterFunction(ILogger<JobStatusUpdaterFunction> logger, PlaceMembershipObtainerService membershipProviderService)
        {
            _logger = logger;
            _membershipProviderService = membershipProviderService;
        }

        [Function(nameof(JobStatusUpdaterFunction))]
        public async Task UpdateJobStatusAsync([ActivityTrigger] JobStatusUpdaterRequest request)
        {
            if (request.SyncJob != null)
            {
                using (_logger.BeginSyncJobScope(request.SyncJob))
                {
                    _logger.FunctionStarted(nameof(JobStatusUpdaterFunction));
                    await _membershipProviderService.UpdateSyncJobStatusAsync(request.SyncJob, request.Status);
                    _logger.FunctionCompleted(nameof(JobStatusUpdaterFunction));
                }
            }
        }
    }
}