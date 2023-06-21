// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Microsoft.Azure.WebJobs;
using Repositories.Contracts;
using System;
using System.Threading.Tasks;
using TeamsChannel.Service.Contracts;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;

namespace Hosts.TeamsChannelMembershipObtainer
{
    public class JobStatusUpdaterFunction
    {
        private readonly ITeamsChannelService _teamsChannelService;
        private readonly ILoggingRepository _loggingRepository;

        public JobStatusUpdaterFunction(ILoggingRepository loggingRepository, ITeamsChannelService teamsChannelService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _teamsChannelService = teamsChannelService ?? throw new ArgumentNullException(nameof(teamsChannelService));
        }

        [FunctionName(nameof(JobStatusUpdaterFunction))]
        public async Task UpdateJobStatusAsync([ActivityTrigger] JobStatusUpdaterRequest request)
        {
            var runId = request.SyncJob.RunId.GetValueOrDefault(Guid.Empty);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(JobStatusUpdaterFunction)} function started", RunId = runId }, VerbosityLevel.DEBUG);

            await _teamsChannelService.UpdateSyncJobStatusAsync(request.SyncJob, request.Status);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(JobStatusUpdaterFunction)} function completed", RunId = runId }, VerbosityLevel.DEBUG);
        }
    }
}
