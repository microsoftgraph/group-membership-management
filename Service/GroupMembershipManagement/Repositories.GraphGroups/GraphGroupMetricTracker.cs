// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Models;
using Repositories.Contracts;
using Repositories.Contracts.Constants;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Repositories.GraphGroups
{
    internal class GraphGroupMetricTracker
    {
        private readonly GraphServiceClient _graphServiceClient;
        private readonly TelemetryClient _telemetryClient;
        private readonly ILogger<GraphGroupMetricTracker> _logger;

        public GraphGroupMetricTracker(GraphServiceClient graphServiceClient,
                                       TelemetryClient telemetryClient,
                                       ILogger<GraphGroupMetricTracker> logger)
        {
            _graphServiceClient = graphServiceClient ?? throw new ArgumentNullException(nameof(graphServiceClient));
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task TrackMetricsAsync(IDictionary<string, IEnumerable<string>> headers, QueryType queryType, Guid? runId, GraphOperationType operationType = GraphOperationType.Read)
        {
            if (queryType == QueryType.Delta || queryType == QueryType.DeltaLink)
            {
                const int deltaResourceUnitCost = 5;
                _logger.LogInformationWithRunId(runId, $"Resource unit cost of {Enum.GetName(typeof(QueryType), queryType)} - {deltaResourceUnitCost}");
                TrackResourceUnitsUsedByTypeEvent(deltaResourceUnitCost, queryType, runId);
                _telemetryClient.GetMetric(TelemetryConstants.ResourceUnitsMetricName, "OperationType", "QueryType")
                                .TrackValue(deltaResourceUnitCost, operationType.ToString(), queryType.ToString());
                return;
            }

            if (headers == null)
            {
                _logger.LogInformationWithRunId(runId, $"Resource unit cost of {Enum.GetName(typeof(QueryType), queryType)} is not available");
                return;
            }

            var telemetryResult = await GraphTelemetryHelper.TrackResourceUnitsAsync(headers, queryType, runId, _logger, _telemetryClient, operationType);

            // Resource unit/throttle metrics already tracked within GraphTelemetryHelper.
        }

        public Metric GetMetric(string metric)
        {
            return _telemetryClient.GetMetric(metric);
        }

        public void TrackResourceUnitsUsedByTypeEvent(int ruu, QueryType queryType, Guid? runId)
        {
            GraphTelemetryHelper.TrackResourceUnitsUsedByTypeEvent(_telemetryClient, ruu, queryType, runId);
        }

        public Task TrackRequestAsync(IDictionary<string, IEnumerable<string>> headers, Guid groupId, QueryType queryType, Guid? runId)
        {
            string requestId = string.Empty;
            string clientRequestId = string.Empty;
            string diagnosticValue = string.Empty;
            string dateValue = string.Empty;

            if (headers.TryGetValue("request-id", out var request))
            {
                requestId = request.FirstOrDefault();
            }

            if (headers.TryGetValue("client-request-id", out var clientRequest))
            {
                clientRequestId = clientRequest.FirstOrDefault();
            }

            if (headers.TryGetValue("x-ms-ags-diagnostic", out var diagnostic))
            {
                diagnosticValue = diagnostic.FirstOrDefault();
            }

            if (headers.TryGetValue("Date", out var date))
            {
                dateValue = date.FirstOrDefault();
            }

            _logger.LogInformationWithRunId(runId, $"Group Id - {groupId}, QueryType - {queryType}, Request Id - {requestId}, Client Request Id - {clientRequestId}, Diagnostic - {diagnosticValue}, Date - {dateValue}");
            return Task.CompletedTask;
        }
    }
}
