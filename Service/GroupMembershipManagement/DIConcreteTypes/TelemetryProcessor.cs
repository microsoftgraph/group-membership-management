// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace DIConcreteTypes
{
    public sealed class TelemetryProcessor : ITelemetryProcessor
    {
        private readonly ITelemetryProcessor _next;

        public TelemetryProcessor(ITelemetryProcessor next)
        {
            _next = next;
        }

        public void Process(ITelemetry item)
        {
            if (item is TraceTelemetry traceTelemetry
                && item is ISupportSampling samplingTelemetry
                && traceTelemetry.Properties.TryGetValue(TelemetryConstants.LogSourceProperty, out var logSource)
                && logSource == TelemetryConstants.LogSource)
            {
                samplingTelemetry.SamplingPercentage = 100;
            }

            _next.Process(item);
        }
    }
}
