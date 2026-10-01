// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Repositories.Contracts.Constants;
using System;
using System.Collections.Generic;

namespace Repositories.Contracts.Helpers
{
    public static class JobCountTelemetryHelper
    {
        public static void TrackEventAndMetric(
            TelemetryClient telemetryClient,
            string telemetryName,
            IDictionary<string, string> properties)
        {
            if (telemetryClient == null) throw new ArgumentNullException(nameof(telemetryClient));
            properties ??= new Dictionary<string, string>();

            telemetryClient.TrackEvent(telemetryName, properties);

            // Metrics are not subject to adaptive sampling; dimensions are kept to the
            // existing event properties so cardinality does not grow.
            var metric = new MetricTelemetry
            {
                Name = telemetryName,
                Sum = 1,
                Count = 1
            };

            foreach (var property in properties)
            {
                metric.Properties[property.Key] = property.Value ?? string.Empty;
            }

            metric.Properties[TelemetryConstants.TelemetrySchemaVersionPropertyName] =
                TelemetryConstants.RichMetricSchemaVersion;

            telemetryClient.TrackMetric(metric);
        }
    }
}
