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

namespace Hosts.TeamsChannelMembershipObtainer
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
        public Task TrackEventAsync([ActivityTrigger] TelemetryTrackerRequest request)
        {
            using var scope = _logger.BeginRunIdScope(request.RunId.GetValueOrDefault(Guid.Empty));

            _logger.FunctionStarted(nameof(TelemetryTrackerFunction));

            var jobsCompletedEvent = new Dictionary<string, string>
            {
                { "Status", request.JobStatus.ToString() },
                { "ResultStatus", request.ResultStatus.ToString() },
                { "RunId", request.RunId.ToString() }
            };
            _telemetryClient.TrackEvent("NumberOfJobsCompleted", jobsCompletedEvent);

            _logger.FunctionCompleted(nameof(TelemetryTrackerFunction));

            return Task.CompletedTask;
        }
    }
}