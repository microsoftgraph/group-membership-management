// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.GroupMembershipObtainer
{
    public class JobStatusUpdaterFunction
    {
        private readonly ILogger<JobStatusUpdaterFunction> _logger;
        private readonly SGMembershipCalculator _membershipCalculator;

        public JobStatusUpdaterFunction(ILogger<JobStatusUpdaterFunction> logger, SGMembershipCalculator membershipCalculator)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _membershipCalculator = membershipCalculator;
        }

        [Function(nameof(JobStatusUpdaterFunction))]
        public async Task UpdateJobStatusAsync([ActivityTrigger] JobStatusUpdaterRequest request)
        {
            if (request.SyncJob != null)
            {
                using (_logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object> { ["CurrentPart"] = request.CurrentPart, ["TotalParts"] = request.TotalParts }))
                {
                    _logger.FunctionStarted(nameof(JobStatusUpdaterFunction));
                    await _membershipCalculator.UpdateSyncJobStatusAsync(request.SyncJob, request.Status, request.BeforeSyncUserCount);
                    _logger.FunctionCompleted(nameof(JobStatusUpdaterFunction));
                }
            }
        }
    }
}