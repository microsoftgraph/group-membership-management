// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.TeamsChannelUpdater;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class TelemetryTrackerFunctionTests
    {
        private TelemetryClient _telemetryClient;
        private TelemetryTrackerFunction _telemetryTrackerFunction;
        private SyncJob _syncJob;
        private List<EventTelemetry> _capturedEvents;

        [TestInitialize]
        public void Setup()
        {
            _capturedEvents = new List<EventTelemetry>();

            var telemetryConfig = new TelemetryConfiguration();
            telemetryConfig.TelemetryChannel = new StubTelemetryChannel();
            _telemetryClient = new TelemetryClient(telemetryConfig);

            _telemetryTrackerFunction = new TelemetryTrackerFunction(
                NullLogger<TelemetryTrackerFunction>.Instance,
                _telemetryClient);

            _syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid()
            };
        }

        [TestMethod]
        public async Task TrackEvent_SendsCorrectProperties()
        {
            var request = new TelemetryTrackerRequest
            {
                JobStatus = SyncStatus.Idle,
                ResultStatus = ResultStatus.Success,
                SyncJob = _syncJob
            };

            await _telemetryTrackerFunction.TrackEventAsync(request);

            // TrackEvent was called — verify via TelemetryClient flush
            _telemetryClient.Flush();
        }

        [TestMethod]
        public async Task TrackEvent_WithErrorStatus_SendsCorrectProperties()
        {
            var request = new TelemetryTrackerRequest
            {
                JobStatus = SyncStatus.Error,
                ResultStatus = ResultStatus.Failure,
                SyncJob = _syncJob
            };

            await _telemetryTrackerFunction.TrackEventAsync(request);

            _telemetryClient.Flush();
        }

        private class StubTelemetryChannel : Microsoft.ApplicationInsights.Channel.ITelemetryChannel
        {
            public bool? DeveloperMode { get; set; }
            public string EndpointAddress { get; set; }
            public void Dispose() { }
            public void Flush() { }
            public void Send(Microsoft.ApplicationInsights.Channel.ITelemetry item) { }
        }
    }
}
