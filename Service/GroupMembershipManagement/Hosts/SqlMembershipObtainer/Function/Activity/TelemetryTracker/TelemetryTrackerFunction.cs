// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.SqlMembershipObtainer;
using Microsoft.ApplicationInsights;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SqlMembershipObtainer
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
            using (_logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object>
            {
                ["CurrentPart"] = request.CurrentPart,
                ["TotalParts"] = request.TotalParts
            }))
            {
                _logger.FunctionStarted(nameof(TelemetryTrackerFunction));
                var jobsCompletedEvent = new Dictionary<string, string>
                {
                    { "Status", request.JobStatus.ToString() },
                    { "ResultStatus", request.ResultStatus.ToString() },
                    { "RunId", (request.SyncJob.RunId ?? Guid.Empty).ToString() }
                };
                _telemetryClient.TrackEvent("NumberOfJobsCompleted", jobsCompletedEvent);
                _logger.FunctionCompleted(nameof(TelemetryTrackerFunction));
            }
        }
    }
}