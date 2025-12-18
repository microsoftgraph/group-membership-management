// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.ApplicationInsights;
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
        private readonly ILoggingRepository _loggingRepository;

        public GraphGroupMetricTracker(GraphServiceClient graphServiceClient,
                                       TelemetryClient telemetryClient,
                                       ILoggingRepository logger)
        {
            _graphServiceClient = graphServiceClient ?? throw new ArgumentNullException(nameof(graphServiceClient));
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
            _loggingRepository = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task TrackMetricsAsync(IDictionary<string, IEnumerable<string>> headers, QueryType queryType, Guid? runId)
        {
            if (queryType == QueryType.Delta || queryType == QueryType.DeltaLink)
            {
                const int deltaResourceUnitCost = 5;
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Resource unit cost of {Enum.GetName(typeof(QueryType), queryType)} - {deltaResourceUnitCost}", RunId = runId });
                TrackResourceUnitsUsedByTypeEvent(deltaResourceUnitCost, queryType, runId);
                _telemetryClient.GetMetric(TelemetryConstants.ResourceUnitsMetricName).TrackValue(deltaResourceUnitCost);
                return;
            }

            if (headers == null)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Resource unit cost of {Enum.GetName(typeof(QueryType), queryType)} is not available", RunId = runId });
                return;
            }

            var telemetryResult = await GraphTelemetryHelper.TrackResourceUnitsAsync(headers, queryType, runId, _loggingRepository, _telemetryClient);

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

        public async Task TrackRequestAsync(IDictionary<string, IEnumerable<string>> headers, Guid groupId, QueryType queryType, Guid? runId)
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

            await _loggingRepository.LogMessageAsync(
                new LogMessage
                {
                    Message = $"Group Id - {groupId}, QueryType - {queryType}, Request Id - {requestId}, Client Request Id - {clientRequestId}, Diagnostic - {diagnosticValue}, Date - {dateValue}",
                    RunId = runId
                });
        }
    }
}
