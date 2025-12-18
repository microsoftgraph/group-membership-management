// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.ApplicationInsights;
using Models;
using Repositories.Contracts.Constants;
using Repositories.Contracts;
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
                                                                              ILoggingRepository loggingRepository,
                                                                              TelemetryClient telemetryClient)
        {
            if (response == null || loggingRepository is null || telemetryClient is null)
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

            return await TrackResourceUnitsAsync(headers, queryType, runId, loggingRepository, telemetryClient);
        }

        public static async Task<GraphTelemetryResult> TrackResourceUnitsAsync(IDictionary<string, IEnumerable<string>> headers,
                                                                              QueryType queryType,
                                                                              Guid? runId,
                                                                              ILoggingRepository loggingRepository,
                                                                              TelemetryClient telemetryClient)
        {
            if (headers == null || loggingRepository is null || telemetryClient is null)
            {
                return new GraphTelemetryResult();
            }

            if (!headers.TryGetValue(GraphResponseHeaders.ResourceUnit, out var resourceValues))
            {
                await loggingRepository.LogMessageAsync(new LogMessage
                {
                    RunId = runId,
                    Message = $"Resource unit cost of {Enum.GetName(typeof(QueryType), queryType)} is not available"
                });

                return new GraphTelemetryResult();
            }

            var ruu = ParseFirstInt(resourceValues);
            if (!ruu.HasValue)
            {
                await loggingRepository.LogMessageAsync(new LogMessage
                {
                    RunId = runId,
                    Message = $"Unable to parse resource unit cost of {Enum.GetName(typeof(QueryType), queryType)}"
                });

                return new GraphTelemetryResult();
            }

            await loggingRepository.LogMessageAsync(new LogMessage
            {
                RunId = runId,
                Message = $"Resource unit cost of {Enum.GetName(typeof(QueryType), queryType)} - {ruu.Value}"
            });

            TrackResourceUnitsUsedByTypeEvent(telemetryClient, ruu.Value, queryType, runId);
            telemetryClient.GetMetric(TelemetryConstants.ResourceUnitsMetricName).TrackValue(ruu.Value);

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
            if (telemetryClient is null)
            {
                return;
            }

            var ruuByTypeEvent = new Dictionary<string, string>
            {
                { "RunId", runId?.ToString() ?? string.Empty },
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
