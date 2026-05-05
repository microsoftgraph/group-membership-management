// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Repositories.Contracts.Constants;
using Repositories.Contracts.Helpers;
using System.Collections.Concurrent;

namespace Repositories.Contracts.Tests
{
    [TestClass]
    public class SyncCompleteTelemetryHelperTests
    {
        // Stand-in for a host's concrete SyncCompleteCustomEvent: implements the
        // interface AND has extra host-shaped properties. Used to verify the
        // helper still emits all concrete-type properties when the parameter is
        // declared as ISyncCompleteCustomEvent.
        private sealed class TestSyncCompleteCustomEvent : ISyncCompleteCustomEvent
        {
            public string Result { get; set; } = "N/A";
            public string Type { get; set; } = "N/A";
            public string IsDryRunEnabled { get; set; } = "N/A";
            public string SyncJobTimeElapsedSeconds { get; set; } = "N/A";

            public string TargetOfficeGroupId { get; set; } = "host-target";
            public string Identifier { get; set; } = "host-id";
            public string MembersAdded { get; set; } = "42";
        }

        private sealed class CapturingChannel : ITelemetryChannel
        {
            public ConcurrentBag<ITelemetry> SentItems { get; } = new();
            public bool? DeveloperMode { get; set; }
            public string? EndpointAddress { get; set; }
            public void Dispose() { }
            public void Flush() { }
            public void Send(ITelemetry item) => SentItems.Add(item);
        }

        private static TelemetryClient CreateClient(out CapturingChannel channel)
        {
            channel = new CapturingChannel();
            var config = new TelemetryConfiguration
            {
                ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000",
                TelemetryChannel = channel
            };
            return new TelemetryClient(config);
        }

        [TestMethod]
        public void TrackSyncCompleteEventAndMetric_EmitsAllConcretePropertiesInEvent()
        {
            var client = CreateClient(out var channel);
            ISyncCompleteCustomEvent evt = new TestSyncCompleteCustomEvent
            {
                Type = "GroupMembership",
                IsDryRunEnabled = "False"
            };
            var lastStart = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var nowUtc = new DateTime(2026, 1, 1, 12, 0, 30, DateTimeKind.Utc);
            var expectedElapsed = (nowUtc - lastStart).TotalSeconds.ToString();

            SyncCompleteTelemetryHelper.TrackSyncCompleteEventAndMetric(
                client, evt, nowUtc, lastStart, "Success");

            client.Flush();

            var eventTelemetry = channel.SentItems.OfType<EventTelemetry>().Single(t => t.Name == TelemetryConstants.SyncCompleteName);

            // Interface members
            Assert.AreEqual("Success", eventTelemetry.Properties["Result"]);
            Assert.AreEqual("GroupMembership", eventTelemetry.Properties["Type"]);
            Assert.AreEqual("False", eventTelemetry.Properties["IsDryRunEnabled"]);
            Assert.AreEqual(expectedElapsed, eventTelemetry.Properties["SyncJobTimeElapsedSeconds"]);

            // Host-specific properties — would be missing if reflection only saw
            // the interface (4 members) instead of the runtime concrete type.
            Assert.AreEqual("host-target", eventTelemetry.Properties["TargetOfficeGroupId"]);
            Assert.AreEqual("host-id", eventTelemetry.Properties["Identifier"]);
            Assert.AreEqual("42", eventTelemetry.Properties["MembersAdded"]);
        }

        [TestMethod]
        public void TrackSyncCompleteEventAndMetric_WritesBackResultAndElapsedToEntity()
        {
            var client = CreateClient(out _);
            var evt = new TestSyncCompleteCustomEvent { IsDryRunEnabled = "False" };
            var lastStart = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var nowUtc = new DateTime(2026, 1, 1, 12, 0, 45, DateTimeKind.Utc);
            var expectedElapsed = (nowUtc - lastStart).TotalSeconds.ToString();

            SyncCompleteTelemetryHelper.TrackSyncCompleteEventAndMetric(
                client, evt, nowUtc, lastStart, "PartialSuccess");

            Assert.AreEqual("PartialSuccess", evt.Result);
            Assert.AreEqual(expectedElapsed, evt.SyncJobTimeElapsedSeconds);
        }

        [TestMethod]
        public void TrackSyncCompleteEventAndMetric_EmitsMetricWithDimensions()
        {
            var client = CreateClient(out var channel);
            ISyncCompleteCustomEvent evt = new TestSyncCompleteCustomEvent
            {
                Type = "GroupMembership",
                IsDryRunEnabled = "False"
            };
            var lastStart = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var nowUtc = new DateTime(2026, 1, 1, 12, 0, 30, DateTimeKind.Utc);

            SyncCompleteTelemetryHelper.TrackSyncCompleteEventAndMetric(
                client, evt, nowUtc, lastStart, "Success");

            client.Flush();

            var metricTelemetry = channel.SentItems.OfType<MetricTelemetry>()
                .Single(t => t.Name == TelemetryConstants.SyncCompleteName);

            Assert.AreEqual(1, metricTelemetry.Sum);
            Assert.AreEqual(1, metricTelemetry.Count);
            Assert.AreEqual("Success", metricTelemetry.Properties[TelemetryConstants.ResultDimensionName]);
            Assert.AreEqual("GroupMembership", metricTelemetry.Properties[TelemetryConstants.TypeDimensionName]);
        }

        [TestMethod]
        public void TrackSyncCompleteEventAndMetric_DryRun_StillEmitsEventAndDoesNotThrow()
        {
            var client = CreateClient(out var channel);
            ISyncCompleteCustomEvent evt = new TestSyncCompleteCustomEvent { IsDryRunEnabled = "True" };
            var lastStart = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var nowUtc = new DateTime(2026, 1, 1, 12, 0, 10, DateTimeKind.Utc);

            SyncCompleteTelemetryHelper.TrackSyncCompleteEventAndMetric(
                client, evt, nowUtc, lastStart, "Success");

            client.Flush();

            // Event still emits on dry-run (existing behavior preserved).
            Assert.IsTrue(channel.SentItems.OfType<EventTelemetry>()
                .Any(t => t.Name == TelemetryConstants.SyncCompleteName));

            // Sampling-immune SyncComplete count metric is gated on dry-run so the
            // dashboard's DryRun==false filter doesn't need a new dimension.
            Assert.IsFalse(channel.SentItems.OfType<MetricTelemetry>()
                .Any(t => t.Name == TelemetryConstants.SyncCompleteName));
        }
    }
}
