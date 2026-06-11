// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Repositories.Contracts.Helpers
{
    public static class GraphTelemetryHelper
    {
        public static async Task<GraphTelemetryResult> TrackResourceUnitsAsync(HttpResponseMessage response,
                                                                              QueryType queryType,
                                                                              Guid? runId,
                                                                              ILogger logger,
                                                                              TelemetryClient telemetryClient,
                                                                              GraphOperationType operationType = GraphOperationType.Read)
        {
            if (response == null || logger is null || telemetryClient is null)
            {
                return new GraphTelemetryResult();
            }

            var headers = new Dictionary<string, IEnumerable<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var header in response.Headers)
            {
                headers[header.Key] = header.Value;
            }

            if (response.Content != null)
            {
                foreach (var header in response.Content.Headers)
                {
                    headers[header.Key] = header.Value;
                }
            }

            return await TrackResourceUnitsAsync(headers, queryType, runId, logger, telemetryClient, operationType);
        }

        public static async Task<GraphTelemetryResult> TrackResourceUnitsAsync(IDictionary<string, IEnumerable<string>> headers,
                                                                              QueryType queryType,
                                                                              Guid? runId,
                                                                              ILogger logger,
                                                                              TelemetryClient telemetryClient,
                                                                              GraphOperationType operationType = GraphOperationType.Read)
        {
            var resolvedRunId = CorrelationActivity.ResolveRunId(runId);

            if (headers == null || logger is null || telemetryClient is null)
            {
                return new GraphTelemetryResult();
            }

            if (!headers.TryGetValue(GraphResponseHeaders.ResourceUnit, out var resourceValues))
            {
                logger.LogInformation("Resource unit cost of {QueryType} is not available", queryType);

                return new GraphTelemetryResult();
            }

            var ruu = ParseFirstInt(resourceValues);
            if (!ruu.HasValue)
            {
                logger.LogWarning("Unable to parse resource unit cost of {QueryType}", queryType);

                return new GraphTelemetryResult();
            }

            logger.LogInformation("Resource unit cost of {QueryType} is {ResourceUnitsUsed}", queryType, ruu.Value);

            TrackResourceUnitsUsedByTypeEvent(telemetryClient, ruu.Value, queryType, resolvedRunId);
            telemetryClient.GetMetric(TelemetryConstants.ResourceUnitsMetricName, "OperationType").TrackValue(ruu.Value, operationType.ToString());

            var telemetryResult = new GraphTelemetryResult
            {
                ResourceUnitsUsed = ruu
            };

            if (headers.TryGetValue(GraphResponseHeaders.ThrottlePercentage, out var throttleValues))
            {
                var throttle = ParseFirstDouble(throttleValues);
                if (throttle.HasValue)
                {
                    telemetryClient.GetMetric(TelemetryConstants.ThrottleLimitMetricName).TrackValue(throttle.Value);
                    telemetryResult.ThrottleLimitPercentage = throttle;
                }
            }

            return telemetryResult;
        }

        public static void TrackResourceUnitsUsedByTypeEvent(TelemetryClient telemetryClient,
                                                              int ruu,
                                                              QueryType queryType,
                                                              Guid? runId)
        {
            var resolvedRunId = CorrelationActivity.ResolveRunId(runId);

            if (telemetryClient is null)
            {
                return;
            }

            var ruuByTypeEvent = new Dictionary<string, string>
            {
                { "RunId", resolvedRunId?.ToString() ?? string.Empty },
                { "ResourceUnitsUsed", ruu.ToString() },
                { "QueryType", queryType.ToString() }
            };

            telemetryClient.TrackEvent(TelemetryConstants.ResourceUnitsEventName, ruuByTypeEvent);
        }

        private static int? ParseFirstInt(IEnumerable<string> values)
        {
            foreach (var value in values ?? Enumerable.Empty<string>())
            {
                if (int.TryParse(value, out var parsed))
                {
                    return parsed;
                }
            }

            return null;
        }

        private static double? ParseFirstDouble(IEnumerable<string> values)
        {
            foreach (var value in values ?? Enumerable.Empty<string>())
            {
                if (double.TryParse(value, out var parsed))
                {
                    return parsed;
                }
            }

            return null;
        }
    }

    public sealed class GraphTelemetryResult
    {
        public int? ResourceUnitsUsed { get; set; }
        public double? ThrottleLimitPercentage { get; set; }
    }
}
