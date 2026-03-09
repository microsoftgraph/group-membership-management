// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DIConcreteTypes.Tests
{
    [TestClass]
    public class TelemetryProcessorTests
    {
        [TestMethod]
        public void TelemetryProcessorSetsSamplingToHundredForTaggedTraceTelemetry()
        {
            var nextProcessor = new TestTelemetryProcessor();
            var processor = new TelemetryProcessor(nextProcessor);
            var telemetry = new TraceTelemetry("test trace");
            telemetry.Properties[TelemetryConstants.LogSourceProperty] = TelemetryConstants.LogSource;

            processor.Process(telemetry);

            Assert.AreSame(telemetry, nextProcessor.LastProcessedTelemetry);
            Assert.AreEqual(100, ((ISupportSampling)telemetry).SamplingPercentage);
        }

        [TestMethod]
        public void TelemetryProcessorDoesNotSetSamplingForUntaggedTraceTelemetry()
        {
            var nextProcessor = new TestTelemetryProcessor();
            var processor = new TelemetryProcessor(nextProcessor);
            var telemetry = new TraceTelemetry("test trace");

            processor.Process(telemetry);

            Assert.AreSame(telemetry, nextProcessor.LastProcessedTelemetry);
            Assert.IsNull(((ISupportSampling)telemetry).SamplingPercentage);
        }

        [TestMethod]
        public void TelemetryProcessorDoesNotSetSamplingForTaggedNonTraceTelemetry()
        {
            var nextProcessor = new TestTelemetryProcessor();
            var processor = new TelemetryProcessor(nextProcessor);
            var telemetry = new RequestTelemetry();
            telemetry.Properties[TelemetryConstants.LogSourceProperty] = TelemetryConstants.LogSource;

            processor.Process(telemetry);

            Assert.AreSame(telemetry, nextProcessor.LastProcessedTelemetry);
            Assert.IsNull(((ISupportSampling)telemetry).SamplingPercentage);
        }

        private sealed class TestTelemetryProcessor : ITelemetryProcessor
        {
            public ITelemetry? LastProcessedTelemetry { get; private set; }

            public void Process(ITelemetry item)
            {
                LastProcessedTelemetry = item;
            }
        }
    }
}
