// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.ApplicationInsights;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.JobTrigger
{
    public class TelemetryTrackerFunction
    {
        private readonly ILogger<TelemetryTrackerFunction> _logger;
        private readonly TelemetryClient _telemetryClient;

        public TelemetryTrackerFunction(ILogger<TelemetryTrackerFunction> logger, TelemetryClient telemetryClient)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
        }

        [Function(nameof(TelemetryTrackerFunction))]
        public async Task TrackEventAsync([ActivityTrigger] TelemetryTrackerRequest request)
        {
            using var activity = CorrelationActivity.StartRunIdActivity(nameof(TelemetryTrackerFunction), request.RunId);
            using (_logger.BeginRunIdScope(request.RunId))
            {
                _logger.ActivityFunctionStarted(nameof(TelemetryTrackerFunction));
                var jobsCompletedEvent = new Dictionary<string, string>
                {
                    { "Status", request.JobStatus.ToString() },
                    { "ResultStatus", request.ResultStatus.ToString() },
                    { "RunId", request.RunId.ToString() }
                };

                const string eventName = "NumberOfJobsCompleted";
                _telemetryClient.TrackEvent(eventName, jobsCompletedEvent);
                _logger.TrackedTelemetryEvent(eventName);
                _logger.ActivityFunctionCompleted(nameof(TelemetryTrackerFunction));
            }
        }
    }
}
