// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph;
using Microsoft.Graph.Groups.Delta;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Serialization.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using Polly;
using Repositories.Contracts;
using Repositories.Contracts.Constants;
using Repositories.Contracts.Helpers;
using Repositories.GraphGroups;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Services.Tests
{
    /// <summary>
    /// The Header-vs-Estimated resource-unit source is recorded only on the group-membership path. Every other caller of the
    /// shared helper (Teams, Mail, GraphUsers, GraphAgents) must emit exactly the pre-existing telemetry.
    /// </summary>
    [TestClass]
    public sealed class ResourceUnitSourceTelemetryTests
    {
        private const string GraphApiBaseUrl = "https://graph.microsoft.com/v1.0";

        private static readonly string[] SharedMetricDimensions =
        {
            TelemetryConstants.OperationTypeDimensionName,
            TelemetryConstants.QueryTypeDimensionName,
            TelemetryConstants.MembershipTypeDimensionName,
        };

        private static readonly string[] SharedEventProperties =
        {
            "RunId",
            "ResourceUnitsUsed",
            TelemetryConstants.QueryTypeDimensionName,
            TelemetryConstants.MembershipTypeDimensionName,
        };

        [DataTestMethod]
        [DataRow("TeamsChannelMembership", "TeamsChannelMembership")]
        [DataRow(null, "Unknown")]
        public async Task ExistingCaller_WithoutSource_EmitsUnchangedEventAndMetric(string membershipType, string expectedMembershipType)
        {
            using var fixture = new TelemetryFixture();
            var runId = Guid.NewGuid();

            // Exactly the call shape the Teams channel tracker (and the other shared callers) use today.
            var result = await GraphTelemetryHelper.TrackResourceUnitsAsync(
                Headers("7"), QueryType.Delta, runId, NullLogger.Instance, fixture.Client,
                GraphOperationType.Read, membershipType);

            Assert.AreEqual(7, result.ResourceUnitsUsed);
            var ruuEvent = fixture.Events().Single(e => e.Name == TelemetryConstants.ResourceUnitsEventName);
            CollectionAssert.AreEquivalent(SharedEventProperties, ruuEvent.Properties.Keys.ToArray());
            Assert.AreEqual(runId.ToString(), ruuEvent.Properties["RunId"]);
            Assert.AreEqual("7", ruuEvent.Properties["ResourceUnitsUsed"]);
            Assert.AreEqual("Delta", ruuEvent.Properties[TelemetryConstants.QueryTypeDimensionName]);
            Assert.AreEqual(expectedMembershipType, ruuEvent.Properties[TelemetryConstants.MembershipTypeDimensionName]);

            var metric = fixture.Metrics().Single(m => m.Name == TelemetryConstants.ResourceUnitsMetricName);
            CollectionAssert.AreEquivalent(SharedMetricDimensions, DimensionNames(metric));
            Assert.AreEqual("Read", metric.Properties[TelemetryConstants.OperationTypeDimensionName]);
            Assert.AreEqual("Delta", metric.Properties[TelemetryConstants.QueryTypeDimensionName]);
            Assert.AreEqual(expectedMembershipType, metric.Properties[TelemetryConstants.MembershipTypeDimensionName]);
            Assert.AreEqual(7, metric.Sum);
            Assert.IsFalse(fixture.Metrics().Any(m => m.Name == TelemetryConstants.ResourceUnitsBySourceMetricName));
        }

        [TestMethod]
        public void ExistingEventCaller_WithoutSource_EmitsUnchangedProperties()
        {
            using var fixture = new TelemetryFixture();

            GraphTelemetryHelper.TrackResourceUnitsUsedByTypeEvent(fixture.Client, 3, QueryType.Other, Guid.NewGuid(), "GroupOwnership");

            var ruuEvent = fixture.Events().Single(e => e.Name == TelemetryConstants.ResourceUnitsEventName);
            CollectionAssert.AreEquivalent(SharedEventProperties, ruuEvent.Properties.Keys.ToArray());
        }

        [TestMethod]
        public async Task OptInSource_AddsEventPropertyButKeepsSharedMetricIdentity()
        {
            using var fixture = new TelemetryFixture();

            await GraphTelemetryHelper.TrackResourceUnitsAsync(
                Headers("2"), QueryType.Other, Guid.NewGuid(), NullLogger.Instance, fixture.Client,
                GraphOperationType.Read, MembershipTypes.GroupMembership.ToString(), TelemetryConstants.ResourceUnitSourceHeader);

            var ruuEvent = fixture.Events().Single(e => e.Name == TelemetryConstants.ResourceUnitsEventName);
            Assert.AreEqual(TelemetryConstants.ResourceUnitSourceHeader, ruuEvent.Properties[TelemetryConstants.ResourceUnitSourceDimensionName]);
            var metric = fixture.Metrics().Single(m => m.Name == TelemetryConstants.ResourceUnitsMetricName);
            CollectionAssert.AreEquivalent(SharedMetricDimensions, DimensionNames(metric));
        }

        [TestMethod]
        public async Task GroupRepository_GetByIds_RecordsHeaderSource()
        {
            using var fixture = new TelemetryFixture();
            var id = Guid.NewGuid();
            var requestAdapter = CreateRequestAdapter();
            requestAdapter
                .Setup(adapter => adapter.SendNoContentAsync(
                    It.IsAny<RequestInformation>(),
                    It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestInformation, Dictionary<string, ParsableFactory<IParsable>>, CancellationToken>(
                    (request, _, _) =>
                    {
                        var response = Json($$"""{"value":[{"@odata.type":"#microsoft.graph.user","id":"{{id}}"}]}""");
                        response.Headers.Add("x-ms-resource-unit", "2");
                        SetNativeResponse(request, response);
                    })
                .Returns(Task.CompletedTask);
            var repository = CreateRepository(requestAdapter.Object, fixture.Client);

            await repository.GetDirectoryObjectTypesAsync(new[] { id }, Guid.NewGuid(), CancellationToken.None);

            var ruuEvent = fixture.Events().Single(e => e.Name == TelemetryConstants.ResourceUnitsEventName);
            Assert.AreEqual(TelemetryConstants.ResourceUnitSourceHeader, ruuEvent.Properties[TelemetryConstants.ResourceUnitSourceDimensionName]);
            var sharedMetric = fixture.Metrics().Single(m => m.Name == TelemetryConstants.ResourceUnitsMetricName);
            CollectionAssert.AreEquivalent(SharedMetricDimensions, DimensionNames(sharedMetric));
            var bySource = fixture.Metrics().Single(m => m.Name == TelemetryConstants.ResourceUnitsBySourceMetricName);
            CollectionAssert.AreEquivalent(
                new[] { TelemetryConstants.QueryTypeDimensionName, TelemetryConstants.ResourceUnitSourceDimensionName },
                DimensionNames(bySource));
            Assert.AreEqual(TelemetryConstants.ResourceUnitSourceHeader, bySource.Properties[TelemetryConstants.ResourceUnitSourceDimensionName]);
            Assert.AreEqual(2, bySource.Sum);
        }

        [TestMethod]
        public async Task GroupRepository_DeltaRead_RecordsEstimatedSource()
        {
            using var fixture = new TelemetryFixture();
            var groupId = Guid.NewGuid();
            var requestAdapter = CreateRequestAdapter();
            requestAdapter
                .Setup(adapter => adapter.SendAsync(
                    It.IsAny<RequestInformation>(),
                    It.IsAny<ParsableFactory<DeltaResponse>>(),
                    It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestInformation, ParsableFactory<DeltaResponse>, Dictionary<string, ParsableFactory<IParsable>>, CancellationToken>(
                    (request, _, _, _) => SetNativeResponse(
                        request,
                        Json($$"""{"value":[{"id":"{{groupId}}","members@delta":[]}],"@odata.deltaLink":"{{GraphApiBaseUrl}}/groups/delta?$deltatoken=done"}""")))
                .ReturnsAsync((DeltaResponse)null);
            var repository = CreateRepository(requestAdapter.Object, fixture.Client);

            await repository.GetFirstDeltaUsersPageAsync(groupId, 1);

            var ruuEvent = fixture.Events().First(e => e.Name == TelemetryConstants.ResourceUnitsEventName);
            Assert.AreEqual(TelemetryConstants.ResourceUnitSourceEstimated, ruuEvent.Properties[TelemetryConstants.ResourceUnitSourceDimensionName]);
            Assert.AreEqual("5", ruuEvent.Properties["ResourceUnitsUsed"]);
            var sharedMetric = fixture.Metrics().Single(m => m.Name == TelemetryConstants.ResourceUnitsMetricName);
            CollectionAssert.AreEquivalent(SharedMetricDimensions, DimensionNames(sharedMetric));
            var bySource = fixture.Metrics().Single(m => m.Name == TelemetryConstants.ResourceUnitsBySourceMetricName);
            Assert.AreEqual(TelemetryConstants.ResourceUnitSourceEstimated, bySource.Properties[TelemetryConstants.ResourceUnitSourceDimensionName]);
        }

        private static string[] DimensionNames(MetricTelemetry metric) =>
            metric.Properties.Keys.Where(key => !key.StartsWith("_MS.", StringComparison.Ordinal)).ToArray();

        private static IDictionary<string, IEnumerable<string>> Headers(string resourceUnit) =>
            new Dictionary<string, IEnumerable<string>>(StringComparer.OrdinalIgnoreCase)
            {
                { GraphResponseHeaders.ResourceUnit, new[] { resourceUnit } },
            };

        private static GraphGroupRepository CreateRepository(IRequestAdapter requestAdapter, TelemetryClient telemetryClient) =>
            new(
                new GraphServiceClient(requestAdapter, GraphApiBaseUrl),
                telemetryClient,
                new NoOpRetryPolicyProvider(),
                NullLoggerFactory.Instance);

        private static Mock<IRequestAdapter> CreateRequestAdapter()
        {
            var requestAdapter = new Mock<IRequestAdapter>();
            requestAdapter.SetupProperty(adapter => adapter.BaseUrl, GraphApiBaseUrl);
            requestAdapter
                .SetupGet(adapter => adapter.SerializationWriterFactory)
                .Returns(new JsonSerializationWriterFactory());
            return requestAdapter;
        }

        private static void SetNativeResponse(RequestInformation request, HttpResponseMessage response)
        {
            var option = request.RequestOptions.OfType<ResponseHandlerOption>().Single();
            ((NativeResponseHandler)option.ResponseHandler).Value = response;
        }

        private static HttpResponseMessage Json(string content) =>
            new(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, "application/json") };

        private sealed class NoOpRetryPolicyProvider : IRetryPolicyProvider
        {
            public AsyncPolicy<HttpResponseMessage> CreateRetryAfterPolicy(Guid? runId) => Policy.NoOpAsync<HttpResponseMessage>();
            public AsyncPolicy<HttpResponseMessage> CreateExceptionHandlingPolicy(Guid? runId) => Policy.NoOpAsync<HttpResponseMessage>();
        }

        private sealed class TelemetryFixture : IDisposable
        {
            private readonly CapturingChannel _channel = new();
            private readonly TelemetryConfiguration _configuration;

            public TelemetryFixture()
            {
                _configuration = new TelemetryConfiguration
                {
                    TelemetryChannel = _channel,
                    ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000",
                };
                Client = new TelemetryClient(_configuration);
            }

            public TelemetryClient Client { get; }

            public IReadOnlyList<EventTelemetry> Events() => _channel.Items.OfType<EventTelemetry>().ToList();

            public IReadOnlyList<MetricTelemetry> Metrics()
            {
                Client.Flush();
                return _channel.Items.OfType<MetricTelemetry>().ToList();
            }

            public void Dispose() => _configuration.Dispose();
        }

        private sealed class CapturingChannel : ITelemetryChannel
        {
            public ConcurrentQueue<ITelemetry> Items { get; } = new();
            public bool? DeveloperMode { get; set; }
            public string EndpointAddress { get; set; } = string.Empty;
            public void Send(ITelemetry item) => Items.Enqueue(item);
            public void Flush()
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
