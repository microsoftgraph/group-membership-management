// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.ApplicationInsights;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.JobTrigger
{
    public class GroupVerifierFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IJobTriggerService _jobTriggerService = null;
        private readonly TelemetryClient _telemetryClient = null;

        public GroupVerifierFunction(ILoggingRepository loggingRepository, IJobTriggerService jobTriggerService, TelemetryClient telemetryClient)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _jobTriggerService = jobTriggerService ?? throw new ArgumentNullException(nameof(jobTriggerService));
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
        }

        [FunctionName(nameof(GroupVerifierFunction))]
        public async Task<bool> VerifyGroupAsync([ActivityTrigger] SyncJob syncJob)
        {
            var canWriteToGroup = false;
            if (syncJob != null)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupVerifierFunction)} function started", RunId = syncJob.RunId }, VerbosityLevel.DEBUG);
                _jobTriggerService.RunId = syncJob.RunId ?? Guid.Empty;
                canWriteToGroup = await _jobTriggerService.GroupExistsAndGMMCanWriteToGroupAsync(syncJob);

                if (canWriteToGroup)
                {
                    var endpoints = await _jobTriggerService.GetGroupEndpointsAsync(syncJob.TargetOfficeGroupId);
                    endpoints.ForEach(x =>
                    {
                        var metric = _telemetryClient.GetMetric("Endpoints", "EndPointName", "GroupId");
                        metric.TrackValue(1, x, syncJob.TargetOfficeGroupId.ToString());
                    });
                }

                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupVerifierFunction)} function completed", RunId = syncJob.RunId }, VerbosityLevel.DEBUG);
            }
            return canWriteToGroup;
        }
    }
}