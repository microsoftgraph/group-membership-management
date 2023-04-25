// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.ApplicationInsights;
using Microsoft.Graph;
using Models;
using Newtonsoft.Json;
using Repositories.Contracts;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Repositories.GraphGroups
{
    internal class GraphGroupMetricTracker
    {
        private const string ResourceUnitHeader = "x-ms-resource-unit";
        private const string ThrottlePercentageHeader = "x-ms-throttle-limit-percentage";
        private const string ThrottleInfoHeader = "x-ms-throttle-information";
        private const string ThrottleScopeHeader = "x-ms-throttle-scope";

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

        public async Task TrackMetricsAsync(IDictionary<string, object> additionalData, QueryType queryType, Guid? runId)
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

            // some replies just don't have the response headers
            // i suspect those either aren't throttled the same way or it's a different kind of call
            if (!additionalData.TryGetValue("responseHeaders", out var headers))
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Resource unit cost of {Enum.GetName(typeof(QueryType), queryType)} is not available", RunId = runId });
                return;
            }

            // see https://github.com/microsoftgraph/msgraph-sdk-dotnet/blob/dev/docs/headers.md#reading-response-headers
            var responseHeaders = JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(headers.ToString());

            if (!responseHeaders.TryGetValue(ResourceUnitHeader, out var resourceValues))
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Resource unit cost of {Enum.GetName(typeof(QueryType), queryType)} is not available", RunId = runId });
                return;
            }

            ruu = ParseFirst<int>(resourceValues, int.TryParse);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Resource unit cost of {Enum.GetName(typeof(QueryType), queryType)} - {ruu}", RunId = runId });
            TrackResourceUnitsUsedByTypeEvent(ruu, queryType, runId);
            _telemetryClient.GetMetric(nameof(Services.Entities.Metric.ResourceUnitsUsed)).TrackValue(ruu);

            if (responseHeaders.TryGetValue(ThrottlePercentageHeader, out var throttleValues))
                _telemetryClient.GetMetric(nameof(Services.Entities.Metric.ThrottleLimitPercentage)).TrackValue(ParseFirst<double>(throttleValues, double.TryParse));
        }

        private void TrackResourceUnitsUsedByTypeEvent(int ruu, QueryType queryType, Guid? runId)
        {
            var ruuByTypeEvent = new Dictionary<string, string>
                    {
                        { "RunId", runId.ToString() },
                        { "ResourceUnitsUsed", ruu.ToString() },
                        { "QueryType", queryType.ToString() }
                    };

            _telemetryClient.TrackEvent("ResourceUnitsUsedByType", ruuByTypeEvent);
        }

        delegate bool TryParseFunction<T>(string str, out T parsed);
        private static T ParseFirst<T>(IEnumerable<string> toParse, TryParseFunction<T> tryParse)
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
