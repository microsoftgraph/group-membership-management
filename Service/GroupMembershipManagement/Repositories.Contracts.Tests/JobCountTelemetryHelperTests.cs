// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Repositories.Contracts.Constants;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Repositories.Contracts.Tests
{
    [TestClass]
    public class JobCountTelemetryHelperTests
    {
        private sealed class CapturingChannel : ITelemetryChannel
        {
            public ConcurrentBag<ITelemetry> SentItems { get; } = new();
            public bool? DeveloperMode { get; set; }
            public string? EndpointAddress { get; set; }
            public void Dispose() { }
            public void Flush() { }
            public void Send(ITelemetry item) => SentItems.Add(item);
        }

        private static (TelemetryClient Client, CapturingChannel Channel) CreateClient()
        {
            var channel = new CapturingChannel();
            var configuration = new TelemetryConfiguration
            {
                ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000",
                TelemetryChannel = channel
            };
            return (new TelemetryClient(configuration), channel);
        }

        [TestMethod]
        public void TrackEventAndMetric_EmitsUnchangedEventAndMetricWithSameDimensions()
        {
            var (client, channel) = CreateClient();
            var properties = new Dictionary<string, string>
            {
                ["RunId"] = "run-id",
                ["Status"] = "Idle",
                ["ResultStatus"] = "Success"
            };

            JobCountTelemetryHelper.TrackEventAndMetric(client, TelemetryConstants.NumberOfJobsCompletedName, properties);
            client.Flush();

            var eventTelemetry = channel.SentItems.OfType<EventTelemetry>().Single();
            Assert.AreEqual(TelemetryConstants.NumberOfJobsCompletedName, eventTelemetry.Name);
            CollectionAssert.AreEquivalent(properties.Keys.ToList(), eventTelemetry.Properties.Keys.ToList());
            Assert.AreEqual(3, properties.Count, "Caller dictionary must not be mutated.");

            var metricTelemetry = channel.SentItems.OfType<MetricTelemetry>().Single();
            Assert.AreEqual(TelemetryConstants.NumberOfJobsCompletedName, metricTelemetry.Name);
            Assert.AreEqual(1, metricTelemetry.Sum);
            Assert.AreEqual(1, metricTelemetry.Count);
            Assert.AreEqual("run-id", metricTelemetry.Properties["RunId"]);
            Assert.AreEqual("Idle", metricTelemetry.Properties["Status"]);
            Assert.AreEqual("Success", metricTelemetry.Properties["ResultStatus"]);
            Assert.AreEqual(TelemetryConstants.RichMetricSchemaVersion,
                metricTelemetry.Properties[TelemetryConstants.TelemetrySchemaVersionPropertyName]);
            Assert.AreEqual(4, metricTelemetry.Properties.Count, "Metric must not add high-cardinality dimensions.");
        }

        [TestMethod]
        public void TrackEventAndMetric_NullProperties_EmitsMetricWithSchemaVersionOnly()
        {
            var (client, channel) = CreateClient();

            JobCountTelemetryHelper.TrackEventAndMetric(client, TelemetryConstants.NumberOfJobsStartedName, null);
            client.Flush();

            Assert.AreEqual(1, channel.SentItems.OfType<EventTelemetry>().Count());
            var metricTelemetry = channel.SentItems.OfType<MetricTelemetry>().Single();
            Assert.AreEqual(TelemetryConstants.NumberOfJobsStartedName, metricTelemetry.Name);
            Assert.AreEqual(1, metricTelemetry.Properties.Count);
            Assert.AreEqual(TelemetryConstants.RichMetricSchemaVersion,
                metricTelemetry.Properties[TelemetryConstants.TelemetrySchemaVersionPropertyName]);
        }

        [TestMethod]
        public void TrackEventAndMetric_NullPropertyValue_IsEmittedAsEmptyString()
        {
            var (client, channel) = CreateClient();
            var properties = new Dictionary<string, string> { ["RunId"] = null! };

            JobCountTelemetryHelper.TrackEventAndMetric(client, TelemetryConstants.NumberOfJobsStartedName, properties);
            client.Flush();

            var metricTelemetry = channel.SentItems.OfType<MetricTelemetry>().Single();
            Assert.AreEqual(string.Empty, metricTelemetry.Properties["RunId"]);
        }

        [TestMethod]
        public void TrackEventAndMetric_NullClient_Throws()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                JobCountTelemetryHelper.TrackEventAndMetric(null!, TelemetryConstants.NumberOfJobsStartedName, new Dictionary<string, string>()));
        }
    }
}
