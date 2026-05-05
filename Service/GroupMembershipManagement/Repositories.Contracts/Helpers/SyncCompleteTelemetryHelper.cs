// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Models;
using Repositories.Contracts.Constants;
using System;
using System.Linq;
using System.Reflection;

namespace Repositories.Contracts.Helpers
{
    public static class SyncCompleteTelemetryHelper
    {
        // Single source of truth for SyncComplete telemetry: tracks job duration,
        // emits the rich SyncComplete custom event, and emits a sampling-immune
        // SyncComplete metric. Dry-runs skip the metric so it matches the
        // dashboard's existing DryRun==false filter without adding a dimension.
        public static void TrackSyncCompleteEventAndMetric(TelemetryClient telemetryClient,
                                                           ISyncCompleteCustomEvent syncCompleteEvent,
                                                           DateTime currentUtcDateTime,
                                                           DateTime lastSuccessfulStartTime,
                                                           string successStatus)
        {
            if (telemetryClient is null || syncCompleteEvent is null)
            {
                return;
            }

            var timeElapsedForJob = (currentUtcDateTime - lastSuccessfulStartTime).TotalSeconds;
            telemetryClient.TrackMetric(TelemetryConstants.SyncJobTimeElapsedSecondsMetricName, timeElapsedForJob);

            syncCompleteEvent.SyncJobTimeElapsedSeconds = timeElapsedForJob.ToString();
            syncCompleteEvent.Result = successStatus;

            var syncCompleteDict = syncCompleteEvent.GetType()
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .ToDictionary(prop => prop.Name, prop => (string)prop.GetValue(syncCompleteEvent, null));

            telemetryClient.TrackEvent(TelemetryConstants.SyncCompleteName, syncCompleteDict);

            if (bool.TryParse(syncCompleteEvent.IsDryRunEnabled, out var isDryRun) && isDryRun)
            {
                return;
            }

            // Send the count as a direct MetricTelemetry rather than via
            // TelemetryClient.GetMetric().TrackValue(). GetMetric pre-aggregates
            // in-process for ~60s before flushing; when the Functions host
            // recycles (which happens frequently under load), any unflushed
            // aggregation is lost. Direct MetricTelemetry goes through the same
            // telemetry channel as TrackEvent so it survives recycles, and metric
            // telemetry is not subject to ingestion sampling.
            var syncCompleteMetric = new MetricTelemetry
            {
                Name = TelemetryConstants.SyncCompleteName,
                Sum = 1,
                Count = 1
            };
            syncCompleteMetric.Properties[TelemetryConstants.ResultDimensionName] = successStatus ?? "Unknown";
            syncCompleteMetric.Properties[TelemetryConstants.TypeDimensionName] = syncCompleteEvent.Type ?? "Unknown";
            telemetryClient.TrackMetric(syncCompleteMetric);
        }
    }
}

