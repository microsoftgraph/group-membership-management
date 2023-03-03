// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.ApplicationInsights;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.JobTrigger
{
    public class TelemetryTrackerFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly TelemetryClient _telemetryClient;

        public TelemetryTrackerFunction(ILoggingRepository loggingRepository, TelemetryClient telemetryClient)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
        }

        [FunctionName(nameof(TelemetryTrackerFunction))]
        public async Task TrackEventAsync([ActivityTrigger] TelemetryTrackerRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(TelemetryTrackerFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
            var jobsCompletedEvent = new Dictionary<string, string>
            {
                { "Status", request.JobStatus.ToString() },
                { "ResultStatus", request.ResultStatus.ToString() },
                { "RunId", request.RunId.ToString() }
            };
            _telemetryClient.TrackEvent("NumberOfJobsCompleted", jobsCompletedEvent);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(TelemetryTrackerFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
        }
    }
}
