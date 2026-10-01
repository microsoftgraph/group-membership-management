// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace WebApi.Tests
{
    /// <summary>
    /// Minimal ITelemetryChannel that captures telemetry items in memory so tests
    /// can assert which Track* calls were made. TelemetryClient is a concrete class
    /// that cannot be mocked, so emission is observed through the channel instead.
    /// </summary>
    internal sealed class CapturingTelemetryChannel : ITelemetryChannel
    {
        private readonly List<EventTelemetry> _events = new();
        private readonly List<MetricTelemetry> _metrics = new();

        public bool? DeveloperMode { get; set; }
        public string EndpointAddress { get; set; } = string.Empty;

        public void Send(ITelemetry item)
        {
            if (item is EventTelemetry eventTelemetry)
            {
                _events.Add(eventTelemetry);
            }
            else if (item is MetricTelemetry metricTelemetry)
            {
                _metrics.Add(metricTelemetry);
            }
        }

        public IReadOnlyList<EventTelemetry> GetEvents(string name) =>
            _events.Where(e => e.Name == name).ToList();

        public IReadOnlyList<MetricTelemetry> GetMetrics(string name) =>
            _metrics.Where(m => m.Name == name).ToList();

        public TelemetryClient CreateClient() =>
            new TelemetryClient(new TelemetryConfiguration
            {
                TelemetryChannel = this,
                ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000"
            });

        public void Flush() { }
        public void Dispose() { }
    }
}
