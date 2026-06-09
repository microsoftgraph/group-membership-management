// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Repositories.Contracts.Helpers;
using System;
using System.Threading.Tasks;
using TeamsChannelMembershipObtainer.Service.Contracts;

namespace Hosts.TeamsChannelMembershipObtainer
{
    public class JobStatusUpdaterFunction
    {
        private readonly ILogger<JobStatusUpdaterFunction> _logger;
        private readonly ITeamsChannelService _teamsChannelService;

        public JobStatusUpdaterFunction(ILogger<JobStatusUpdaterFunction> logger, ITeamsChannelService teamsChannelService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _teamsChannelService = teamsChannelService ?? throw new ArgumentNullException(nameof(teamsChannelService));
        }

        [Function(nameof(JobStatusUpdaterFunction))]
        public async Task UpdateJobStatusAsync([ActivityTrigger] JobStatusUpdaterRequest request)
        {
            using var scope = _logger.BeginSyncJobScope(request.SyncJob);

            _logger.FunctionStarted(nameof(JobStatusUpdaterFunction));
            await _teamsChannelService.UpdateSyncJobStatusAsync(request.SyncJob, request.Status);
            _logger.FunctionCompleted(nameof(JobStatusUpdaterFunction));
        }
    }
}
