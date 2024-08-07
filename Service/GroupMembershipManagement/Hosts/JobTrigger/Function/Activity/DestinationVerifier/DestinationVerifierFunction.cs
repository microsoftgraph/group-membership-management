// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Microsoft.ApplicationInsights;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.JobTrigger
{
    public class DestinationVerifierFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IJobTriggerService _jobTriggerService = null;
        private readonly TelemetryClient _telemetryClient = null;

        public DestinationVerifierFunction(ILoggingRepository loggingRepository, IJobTriggerService jobTriggerService, TelemetryClient telemetryClient)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _jobTriggerService = jobTriggerService ?? throw new ArgumentNullException(nameof(jobTriggerService));
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
        }

        [FunctionName(nameof(DestinationVerifierFunction))]
        public async Task<DestinationVerifierResult> VerifyDestinationAsync([ActivityTrigger] SyncJob syncJob)
        {
            var verifierResult = DestinationVerifierResult.NotFound;

            if (syncJob != null)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(DestinationVerifierFunction)} function started", RunId = syncJob.RunId }, VerbosityLevel.DEBUG);
                _jobTriggerService.RunId = syncJob.RunId ?? Guid.Empty;

                verifierResult = await _jobTriggerService.DestinationExistsAndGMMCanWriteToItAsync(syncJob);

                if (verifierResult == DestinationVerifierResult.Success)
                {
                    var endpoints = await _jobTriggerService.GetGroupEndpointsAsync(syncJob);
                    await _loggingRepository.LogMessageAsync(new LogMessage
                    {
                        RunId = syncJob.RunId,
                        Message = $"Linked services: {string.Join(",", endpoints)}"
                    });
                }

                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(DestinationVerifierFunction)} function completed", RunId = syncJob.RunId }, VerbosityLevel.DEBUG);
            }
            return verifierResult;
        }
    }
}