// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph;
using Models;
using Repositories.Contracts.Constants;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Repositories.GraphGroups
{
    internal class TeamsChannelMetricTracker
    {
        private readonly GraphServiceClient _graphServiceClient;
        private readonly TelemetryClient _telemetryClient;
        private readonly ILogger<TeamsChannelMetricTracker> _teamsChannelMetricTrackerLogger;

        public TeamsChannelMetricTracker(GraphServiceClient graphServiceClient,
                                         TelemetryClient telemetryClient,
                                         ILogger<TeamsChannelMetricTracker> teamsChannelMetricTrackerLogger)
        {
            _graphServiceClient = graphServiceClient ?? throw new ArgumentNullException(nameof(graphServiceClient));
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
            _teamsChannelMetricTrackerLogger = teamsChannelMetricTrackerLogger ?? throw new ArgumentNullException(nameof(teamsChannelMetricTrackerLogger));
        }

        public async Task TrackMetricsAsync(IDictionary<string, IEnumerable<string>> headers, QueryType queryType, Guid? runId, GraphOperationType operationType = GraphOperationType.Read)
        {
            if (queryType == QueryType.Delta || queryType == QueryType.DeltaLink)
            {
                const int deltaResourceUnitCost = 5;
                _teamsChannelMetricTrackerLogger.LogInformationWithRunId(runId, $"Resource unit cost of {Enum.GetName(typeof(QueryType), queryType)} - {deltaResourceUnitCost}");
                GraphTelemetryHelper.TrackResourceUnitsUsedByTypeEvent(_telemetryClient, deltaResourceUnitCost, queryType, runId);
                _telemetryClient.GetMetric(TelemetryConstants.ResourceUnitsMetricName, "OperationType").TrackValue(deltaResourceUnitCost, operationType.ToString());
                return;
            }

            if (headers == null)
            {
                _teamsChannelMetricTrackerLogger.LogInformationWithRunId(runId, $"Resource unit cost of {Enum.GetName(typeof(QueryType), queryType)} is not available");
                return;
            }

            var telemetryResult = await GraphTelemetryHelper.TrackResourceUnitsAsync(headers, queryType, runId, _teamsChannelMetricTrackerLogger, _telemetryClient, operationType);

            // Telemetry values already recorded via GraphTelemetryHelper.
        }

        public Microsoft.ApplicationInsights.Metric GetMetric(string metric)
        {
            return _telemetryClient.GetMetric(metric);
        }

        public async Task TrackRequestAsync(IDictionary<string, IEnumerable<string>> headers, Guid? runId)
        {
            string requestId = "";
            string clientRequestId = "";
            string diagnosticValue = "";
            string dateValue = "";

            if (headers.TryGetValue("request-id", out var request))
                requestId = request.FirstOrDefault();

            if (headers.TryGetValue("client-request-id", out var clientRequest))
                clientRequestId = clientRequest.FirstOrDefault();

            if (headers.TryGetValue("x-ms-ags-diagnostic", out var diagnostic))
                diagnosticValue = diagnostic.FirstOrDefault();

            if (headers.TryGetValue("Date", out var date))
                dateValue = date.FirstOrDefault();

            _teamsChannelMetricTrackerLogger.LogInformationWithRunId(runId, $"Request Id - {requestId}, Client Request Id - {clientRequestId}, Diagnostic - {diagnosticValue}, Date - {dateValue}");

            await Task.CompletedTask;
        }

    }
}
