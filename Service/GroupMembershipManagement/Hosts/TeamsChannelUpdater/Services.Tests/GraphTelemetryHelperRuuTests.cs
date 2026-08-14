// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Repositories.Contracts.Constants;
using Repositories.Contracts.Helpers;

namespace Services.Tests
{
    // Channel-path RUU records Graph-reported costs with a membership-type discriminator.
    [TestClass]
    public class GraphTelemetryHelperRuuTests
    {
        private sealed class CapturingChannel : ITelemetryChannel
        {
            public readonly List<ITelemetry> Sent = new();
            public bool? DeveloperMode { get; set; }
            public string EndpointAddress { get; set; } = "";
            public void Send(ITelemetry item) => Sent.Add(item);
            public void Flush() { }
            public void Dispose() { }
        }

        private static (TelemetryClient client, CapturingChannel channel) CreateClient()
        {
            var channel = new CapturingChannel();
            var config = new TelemetryConfiguration { TelemetryChannel = channel, ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000" };
            return (new TelemetryClient(config), channel);
        }

        private static IDictionary<string, IEnumerable<string>> HeadersWithResourceUnit(string value)
        {
            return new Dictionary<string, IEnumerable<string>>(StringComparer.OrdinalIgnoreCase)
            {
                { GraphResponseHeaders.ResourceUnit, new[] { value } }
            };
        }

        [TestMethod]
        public async Task RecordsGraphReportedResourceUnitWithDiscriminator()
        {
            var (client, channel) = CreateClient();

            var result = await GraphTelemetryHelper.TrackResourceUnitsAsync(
                HeadersWithResourceUnit("7"), QueryType.Delta, Guid.NewGuid(), NullLogger.Instance, client,
                GraphOperationType.Read, MembershipTypes.TeamsChannelMembership.ToString());

            Assert.AreEqual(7, result.ResourceUnitsUsed);
            var ruuEvent = channel.Sent.OfType<EventTelemetry>().Single(e => e.Name == TelemetryConstants.ResourceUnitsEventName);
            Assert.AreEqual("7", ruuEvent.Properties["ResourceUnitsUsed"]);
            Assert.AreEqual("TeamsChannelMembership", ruuEvent.Properties[TelemetryConstants.MembershipTypeDimensionName]);
        }

        [TestMethod]
        public async Task RecordsZeroWhenGraphChargesNone_NoFabricatedCost()
        {
            var (client, channel) = CreateClient();

            var result = await GraphTelemetryHelper.TrackResourceUnitsAsync(
                HeadersWithResourceUnit("0"), QueryType.Delta, Guid.NewGuid(), NullLogger.Instance, client,
                GraphOperationType.Read, MembershipTypes.TeamsChannelMembership.ToString());

            Assert.AreEqual(0, result.ResourceUnitsUsed);
            var ruuEvent = channel.Sent.OfType<EventTelemetry>().Single(e => e.Name == TelemetryConstants.ResourceUnitsEventName);
            Assert.AreEqual("0", ruuEvent.Properties["ResourceUnitsUsed"]);
        }

        [TestMethod]
        public async Task RecordsNothingWhenResourceUnitHeaderMissing()
        {
            var (client, channel) = CreateClient();

            var result = await GraphTelemetryHelper.TrackResourceUnitsAsync(
                new Dictionary<string, IEnumerable<string>>(), QueryType.Delta, Guid.NewGuid(), NullLogger.Instance, client,
                GraphOperationType.Read, MembershipTypes.TeamsChannelMembership.ToString());

            Assert.IsFalse(result.ResourceUnitsUsed.HasValue);
            Assert.IsFalse(channel.Sent.OfType<EventTelemetry>().Any(e => e.Name == TelemetryConstants.ResourceUnitsEventName));
        }
    }
}
