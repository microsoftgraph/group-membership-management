// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.ApplicationInsights;
using Microsoft.Graph;
using Models;
using Repositories.Contracts;
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
            int ruu = 0;

            if (queryType == QueryType.Delta || queryType == QueryType.DeltaLink)
            {
                ruu = 5;
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Resource unit cost of {Enum.GetName(typeof(QueryType), queryType)} - {ruu}", RunId = runId });
                TrackResourceUnitsUsedByTypeEvent(ruu, queryType, runId);
                _telemetryClient.GetMetric(nameof(Services.Entities.Metric.ResourceUnitsUsed)).TrackValue(ruu);
                return;
            }

            if (headers == null || !headers.TryGetValue(GraphResponseHeader.ResourceUnitHeader, out var resourceValues))
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Resource unit cost of {Enum.GetName(typeof(QueryType), queryType)} is not available", RunId = runId });
                return;
            }

            ruu = ParseFirst<int>(resourceValues, int.TryParse);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Resource unit cost of {Enum.GetName(typeof(QueryType), queryType)} - {ruu}", RunId = runId });
            TrackResourceUnitsUsedByTypeEvent(ruu, queryType, runId);
            _telemetryClient.GetMetric(nameof(Services.Entities.Metric.ResourceUnitsUsed)).TrackValue(ruu);

            if (headers.TryGetValue(GraphResponseHeader.ThrottlePercentageHeader, out var throttleValues))
                _telemetryClient.GetMetric(nameof(Services.Entities.Metric.ThrottleLimitPercentage)).TrackValue(ParseFirst<double>(throttleValues, double.TryParse));
        }

        public Metric GetMetric(string metric)
        {
            return _telemetryClient.GetMetric(metric);
        }

        public void TrackResourceUnitsUsedByTypeEvent(int ruu, QueryType queryType, Guid? runId)
        {
            var ruuByTypeEvent = new Dictionary<string, string>
                    {
                        { "RunId", runId.ToString() },
                        { "ResourceUnitsUsed", ruu.ToString() },
                        { "QueryType", queryType.ToString() }
                    };

            _telemetryClient.TrackEvent("ResourceUnitsUsedByType", ruuByTypeEvent);
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

            await _loggingRepository.LogMessageAsync(
                new LogMessage
                {
                    Message = $"Request Id - {requestId}, Client Request Id - {clientRequestId}, Diagnostic - {diagnosticValue}, Date - {dateValue}",
                    RunId = runId
                });
        }

        public delegate bool TryParseFunction<T>(string str, out T parsed);
        public static T ParseFirst<T>(IEnumerable<string> toParse, TryParseFunction<T> tryParse)
        {
            foreach (var str in toParse)
            {
                if (tryParse(str, out var parsed))
                {
                    return parsed;
                }
            }

            return default;
        }
    }
}
