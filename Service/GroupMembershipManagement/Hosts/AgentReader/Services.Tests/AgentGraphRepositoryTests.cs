// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Repositories.GraphAgents;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary.Middleware.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Polly.Utilities;
using Repositories.Contracts;
using Repositories.Contracts.Constants;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Hosts.AgentReader.Services.Tests
{
    [TestClass]
    [DoNotParallelize]
    public class AgentGraphRepositoryTests
    {
        private const string AgentId = "00000000-0000-0000-0000-000000000001";
        private const string NextLink =
            "https://graph.microsoft.com/v1.0/users/microsoft.graph.agentUser?$skiptoken=opaque%2Bpage%2Ftoken%3D";
        private readonly Guid _runId = Guid.Parse("11111111-2222-4333-8444-555555555555");
        private readonly List<TimeSpan> _delays = [];
        private Func<TimeSpan, CancellationToken, Task> _originalSleep = null!;

        [TestInitialize]
        public void SetUp()
        {
            _originalSleep = SystemClock.SleepAsync;
            SystemClock.SleepAsync = (delay, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                _delays.Add(delay);
                return Task.CompletedTask;
            };
        }

        [TestCleanup]
        public void TearDown() => SystemClock.SleepAsync = _originalSleep;

        [DataTestMethod]
        [DataRow(true, true)]
        [DataRow(true, false)]
        [DataRow(false, true)]
        [DataRow(false, false)]
        public async Task ExactCastRequest_PreservesBothSdkRepresentations(bool typedAgent, bool typedManager)
        {
            var agentAnnotation = typedAgent ? "\"@odata.type\":\"#microsoft.graph.agentUser\"," : "";
            var managerAnnotation = typedManager ? "\"@odata.type\":\"#microsoft.graph.user\"," : "";
            var payload = $$"""
                {"value":[{
                    {{agentAnnotation}}
                    "id":"{{AgentId}}",
                    "accountEnabled":false,
                    "agentIdentityBlueprintId":" {Unnormalised-BLUEPRINT} ",
                    "manager":{ {{managerAnnotation}} "onPremisesImmutableId":"1001","employeeId":"9999"}
                }]}
                """;
            using var fixture = new Fixture(Response(payload, resourceUnits: "2"));

            var page = await fixture.Repository.ReadPageAsync(_runId, null, CancellationToken.None);

            Assert.AreEqual(1, page.Agents.Count);
            Assert.AreEqual(AgentId, page.Agents[0].Id);
            Assert.IsTrue(page.Agents[0].HasManager);
            Assert.AreEqual("1001", page.Agents[0].ManagerIdentifier);
            Assert.AreEqual(false, page.Agents[0].AccountEnabled);
            Assert.AreEqual(" {Unnormalised-BLUEPRINT} ", page.Agents[0].BlueprintId);
            Assert.AreEqual(1, fixture.Requests.Count);
            var request = fixture.Requests[0];
            Assert.AreEqual(HttpMethod.Get, request.Method);
            Assert.AreEqual("/v1.0/users/microsoft.graph.agentUser", request.Uri.AbsolutePath);
            Assert.AreEqual(
                "?$select=id,accountEnabled,agentIdentityBlueprintId&$expand=manager($select=onPremisesImmutableId)",
                Uri.UnescapeDataString(request.Uri.Query));
            Assert.AreEqual(0, request.SdkRetries);
            var telemetry = fixture.Events.Single(item => item.Name == TelemetryConstants.ResourceUnitsEventName);
            Assert.AreEqual("AgentUser", telemetry.Properties["QueryType"]);
            Assert.AreEqual(_runId.ToString(), telemetry.Properties["RunId"]);
            Assert.AreEqual("2", telemetry.Properties["ResourceUnitsUsed"]);
        }

        [DataTestMethod]
        [DataRow("12345678-1234-4234-8234-123456789abc", false)]
        [DataRow("12345678-1234-4234-8234-123456789abc", true)]
        [DataRow("12345678-9ABC-4DEF-8ABC-1234567890AB", false)]
        [DataRow("12345678-9ABC-4DEF-8ABC-1234567890AB", true)]
        [DataRow("{12345678-9ABC-4DEF-8ABC-1234567890AB}", false)]
        [DataRow("{12345678-9ABC-4DEF-8ABC-1234567890AB}", true)]
        [DataRow(" 12345678-9ABC-4DEF-8ABC-1234567890AB ", false)]
        [DataRow(" 12345678-9ABC-4DEF-8ABC-1234567890AB ", true)]
        [DataRow("2026-09-11T12:34:56.789+05:30", false)]
        [DataRow("2026-09-11T12:34:56.789+05:30", true)]
        [DataRow("2026-09-11", false)]
        [DataRow("2026-09-11", true)]
        [DataRow("12:34:56.789", false)]
        [DataRow("12:34:56.789", true)]
        [DataRow("  opaque \"blueprint\" \\ value  ", false)]
        [DataRow("  opaque \"blueprint\" \\ value  ", true)]
        [DataRow("123", false)]
        [DataRow("123", true)]
        [DataRow("", false)]
        [DataRow("", true)]
        [DataRow(null, false)]
        [DataRow(null, true)]
        public async Task BlueprintId_JsonStringsReachEligibleRowsUnchanged(string? blueprintId, bool typedAgent)
        {
            var agentAnnotation = typedAgent ? "\"@odata.type\":\"#microsoft.graph.agentUser\"," : "";
            using var fixture = new Fixture(Response($$"""
                {"value":[{
                    {{agentAnnotation}}
                    "id":"{{AgentId}}",
                    "accountEnabled":false,
                    "agentIdentityBlueprintId":{{JsonSerializer.Serialize(blueprintId)}},
                    "manager":{"@odata.type":"#microsoft.graph.user","onPremisesImmutableId":"1001"}
                }]}
                """));
            var service = new AgentReaderService(
                fixture.Repository, new Mock<IAgentTableRepository>(MockBehavior.Strict).Object,
                NullLogger<AgentReaderService>.Instance);

            var page = await service.ReadPageAsync(_runId, null, CancellationToken.None);

            Assert.AreEqual(1, page.Agents.Count);
            Assert.AreEqual(Guid.Parse(AgentId), page.Agents[0].AgentObjectId);
            Assert.AreEqual(1001, page.Agents[0].ManagerId);
            Assert.AreEqual(blueprintId, page.Agents[0].BlueprintId);
            Assert.IsFalse(page.Agents[0].AccountEnabled);
            Assert.AreEqual(1L, page.Counters.TotalEnumerated);
            Assert.AreEqual(1L, page.Counters.FilterPassingAgents);
            Assert.AreEqual(0L, page.Counters.BlueprintIdTruncated);
            Assert.AreEqual(1, fixture.Requests.Count);
        }

        [DataTestMethod]
        [DataRow("12345678-9ABC-4DEF-8ABC-1234567890AB", false)]
        [DataRow("12345678-9ABC-4DEF-8ABC-1234567890AB", true)]
        [DataRow("2026-09-11T12:34:56.789+05:30", false)]
        [DataRow("2026-09-11T12:34:56.789+05:30", true)]
        public async Task ManagerIdentifier_NonNumericJsonStringsAreFiltered(string identifier, bool typedManager)
        {
            var managerAnnotation = typedManager ? "\"@odata.type\":\"#microsoft.graph.user\"," : "";
            using var fixture = new Fixture(Response($$"""
                {"value":[{
                    "id":"{{AgentId}}",
                    "manager":{ {{managerAnnotation}} "onPremisesImmutableId":{{JsonSerializer.Serialize(identifier)}}}
                }]}
                """));
            var service = new AgentReaderService(
                fixture.Repository, new Mock<IAgentTableRepository>(MockBehavior.Strict).Object,
                NullLogger<AgentReaderService>.Instance);

            var page = await service.ReadPageAsync(_runId, null, CancellationToken.None);

            Assert.AreEqual(0, page.Agents.Count);
            Assert.AreEqual(1L, page.Counters.TotalEnumerated);
            Assert.AreEqual(0L, page.Counters.FilterPassingAgents);
            Assert.AreEqual(1L, page.Counters.FilteredManagerEmployeeIdNonNumeric);
            Assert.AreEqual(1, fixture.Requests.Count);
        }

        [TestMethod]
        public async Task AdditionalStrings_RemainAssociatedWithTheirAgent()
        {
            const string blueprintId = "12345678-9ABC-4DEF-8ABC-1234567890AB";
            const string dateShapedBlueprintId = "2026-09-11T12:34:56.789+05:30";
            using var fixture = new Fixture(Response($$"""
                {"value":[
                    {
                        "id":"{{AgentId}}",
                        "agentIdentityBlueprintId":"{{blueprintId}}",
                        "manager":{"onPremisesImmutableId":"1001"}
                    },
                    {
                        "id":"00000000-0000-0000-0000-000000000002",
                        "manager":null
                    },
                    {
                        "id":"00000000-0000-0000-0000-000000000003",
                        "agentIdentityBlueprintId":"{{dateShapedBlueprintId}}",
                        "manager":{"@odata.type":"#microsoft.graph.user","onPremisesImmutableId":"1003"}
                    }
                ]}
                """));

            var page = await fixture.Repository.ReadPageAsync(_runId, null, CancellationToken.None);

            Assert.AreEqual(3, page.Agents.Count);
            CollectionAssert.AreEqual(
                new[] { AgentId, "00000000-0000-0000-0000-000000000002", "00000000-0000-0000-0000-000000000003" },
                page.Agents.Select(agent => agent.Id).ToArray());
            CollectionAssert.AreEqual(
                new string?[] { blueprintId, null, dateShapedBlueprintId },
                page.Agents.Select(agent => agent.BlueprintId).ToArray());
            CollectionAssert.AreEqual(
                new string?[] { "1001", null, "1003" },
                page.Agents.Select(agent => agent.ManagerIdentifier).ToArray());
        }

        [DataTestMethod]
        [DataRow("123")]
        [DataRow("1.5")]
        [DataRow("true")]
        [DataRow("{}")]
        [DataRow("[]")]
        public async Task BlueprintId_NonStringJsonValuesAreRejected(string jsonValue)
        {
            using var fixture = new Fixture(Response($$"""
                {"value":[{"id":"{{AgentId}}","agentIdentityBlueprintId":{{jsonValue}}}]}
                """));

            var exception = await Assert.ThrowsExceptionAsync<InvalidDataException>(
                () => fixture.Repository.ReadPageAsync(_runId, null, CancellationToken.None));

            Assert.AreEqual("Graph returned a non-string agentIdentityBlueprintId.", exception.Message);
        }

        [TestMethod]
        public async Task ContinuationLink_IsFollowedVerbatimWithoutManagerLookups()
        {
            using var fixture = new Fixture(
                Response($$"""{"value":[{"id":"{{AgentId}}"}],"@odata.nextLink":"{{NextLink}}"}"""),
                Response("""{"value":[]}"""));

            var first = await fixture.Repository.ReadPageAsync(_runId, null, CancellationToken.None);
            var second = await fixture.Repository.ReadPageAsync(_runId, first.NextLink, CancellationToken.None);

            Assert.AreEqual(NextLink, first.NextLink);
            Assert.IsNull(second.NextLink);
            Assert.AreEqual(2, fixture.Requests.Count);
            Assert.AreEqual(NextLink, fixture.Requests[1].Uri.AbsoluteUri);
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow(",\"manager\":null")]
        public async Task MissingManagerAndOptionalFields_AreNotInvented(string manager)
        {
            using var fixture = new Fixture(Response($$"""{"value":[{"id":"{{AgentId}}"{{manager}}}]}"""));

            var page = await fixture.Repository.ReadPageAsync(_runId, null, CancellationToken.None);

            Assert.IsFalse(page.Agents[0].HasManager);
            Assert.IsNull(page.Agents[0].ManagerIdentifier);
            Assert.IsNull(page.Agents[0].BlueprintId);
            Assert.IsNull(page.Agents[0].AccountEnabled);
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow("\"@odata.type\":\"#microsoft.graph.user\",")]
        public async Task EmployeeId_IsNeverUsedAsAFallback(string annotation)
        {
            using var fixture = new Fixture(Response($$"""
                {"value":[{"id":"{{AgentId}}","manager":{ {{annotation}} "employeeId":"9999"} }]}
                """));

            var page = await fixture.Repository.ReadPageAsync(_runId, null, CancellationToken.None);

            Assert.IsTrue(page.Agents[0].HasManager);
            Assert.IsNull(page.Agents[0].ManagerIdentifier);
            Assert.AreEqual(1, fixture.Requests.Count);
        }

        [DataTestMethod]
        [DataRow(null, 150)]
        [DataRow("7", 7)]
        public async Task Throttling_UsesSharedRetryPolicy(string? retryAfter, int seconds)
        {
            var throttled = Response("{}", HttpStatusCode.TooManyRequests);
            if (retryAfter != null)
            {
                throttled.Headers.Add("Retry-After", retryAfter);
            }
            using var fixture = new Fixture(throttled, Response("""{"value":[]}""", resourceUnits: "3"));

            await fixture.Repository.ReadPageAsync(_runId, null, CancellationToken.None);

            Assert.AreEqual(2, fixture.Requests.Count);
            CollectionAssert.AreEqual(new[] { TimeSpan.FromSeconds(seconds) }, _delays);
        }

        [TestMethod]
        public async Task ExhaustedThrottle_StillRecordsAllFailedAttemptsAndThrows()
        {
            using var fixture = new Fixture(
                Response("{}", HttpStatusCode.TooManyRequests, "2"),
                Response("{}", HttpStatusCode.TooManyRequests));

            var exception = await Assert.ThrowsExceptionAsync<HttpRequestException>(
                () => fixture.Repository.ReadPageAsync(_runId, null, CancellationToken.None));

            Assert.AreEqual(HttpStatusCode.TooManyRequests, exception.StatusCode);
            Assert.AreEqual(2, fixture.Requests.Count);
        }

        [TestMethod]
        public async Task ServerRetry_RetriesAfterServiceUnavailable()
        {
            using var fixture = new Fixture(
                Response("{}", HttpStatusCode.ServiceUnavailable, "4"),
                Response("""{"value":[]}""", resourceUnits: "0"));

            await fixture.Repository.ReadPageAsync(_runId, null, CancellationToken.None);

            Assert.AreEqual(2, fixture.Requests.Count);
            CollectionAssert.AreEqual(new[] { TimeSpan.FromSeconds(2) }, _delays);
        }

        [TestMethod]
        public async Task PermissionFailure_DoesNotReturnAnEmptyCollection()
        {
            using var fixture = new Fixture(Response("{}", HttpStatusCode.Forbidden, "1"));

            var exception = await Assert.ThrowsExceptionAsync<HttpRequestException>(
                () => fixture.Repository.ReadPageAsync(_runId, null, CancellationToken.None));

            Assert.AreEqual(HttpStatusCode.Forbidden, exception.StatusCode);
            Assert.AreEqual(1, fixture.Requests.Count);
        }

        [DataTestMethod]
        [DataRow("{}")]
        [DataRow("[]")]
        [DataRow("""{"value":null}""")]
        [DataRow("""{"value":{}}""")]
        [DataRow("""{"value":[null]}""")]
        [DataRow("""{"value":[123]}""")]
        [DataRow("""{"value":["not an agent"]}""")]
        [DataRow("""{"value":[[]]}""")]
        public async Task MalformedCollection_IsNotAnEmptySuccess(string json)
        {
            using var fixture = new Fixture(Response(json));

            await Assert.ThrowsExceptionAsync<InvalidDataException>(
                () => fixture.Repository.ReadPageAsync(_runId, null, CancellationToken.None));
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow("https://example.invalid/v1.0/users/microsoft.graph.agentUser?$skiptoken=a")]
        [DataRow("https://graph.microsoft.com/beta/users/microsoft.graph.agentUser?$skiptoken=a")]
        [DataRow("https://graph.microsoft.com/v1.0/users?$skiptoken=a")]
        [DataRow("http://graph.microsoft.com/v1.0/users/microsoft.graph.agentUser?$skiptoken=a")]
        public async Task InvalidContinuation_FailsWithoutFollowingIt(string nextLink)
        {
            using var fixture = new Fixture();

            await Assert.ThrowsExceptionAsync<InvalidDataException>(
                () => fixture.Repository.ReadPageAsync(_runId, nextLink, CancellationToken.None));

            Assert.AreEqual(0, fixture.Requests.Count);
        }

        [TestMethod]
        public async Task EmptyRunId_IsRejectedBeforeAnyGraphRequest()
        {
            using var fixture = new Fixture();

            await Assert.ThrowsExceptionAsync<ArgumentException>(
                () => fixture.Repository.ReadPageAsync(Guid.Empty, null, CancellationToken.None));

            Assert.AreEqual(0, fixture.Requests.Count);
        }

        [TestMethod]
        public async Task ConcurrentRuns_KeepTheirOwnRequestCorrelation()
        {
            var secondRun = Guid.NewGuid();
            using var fixture = new Fixture(
                Response("""{"value":[]}""", resourceUnits: "1"),
                Response("""{"value":[]}""", resourceUnits: "1"));

            await Task.WhenAll(
                fixture.Repository.ReadPageAsync(_runId, null, CancellationToken.None),
                fixture.Repository.ReadPageAsync(secondRun, null, CancellationToken.None));

            CollectionAssert.AreEquivalent(
                new[] { _runId.ToString(), secondRun.ToString() },
                fixture.Events.Where(item => item.Name == TelemetryConstants.ResourceUnitsEventName)
                    .Select(item => item.Properties["RunId"]).ToArray());
        }

        private static HttpResponseMessage Response(
            string json, HttpStatusCode status = HttpStatusCode.OK, string? resourceUnits = null)
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            if (resourceUnits != null)
            {
                response.Headers.Add("x-ms-resource-unit", resourceUnits);
            }
            return response;
        }

        private sealed class Fixture : IDisposable
        {
            private readonly CapturingChannel _channel = new();
            private readonly TelemetryConfiguration _configuration;
            private readonly HttpClient _httpClient;
            private readonly GraphServiceClient _client;
            private readonly FixtureHandler _handler;
            public AgentGraphRepository Repository { get; }
            public List<(HttpMethod Method, Uri Uri, int SdkRetries)> Requests => _handler.Requests;
            public EventTelemetry[] Events => _channel.Items.OfType<EventTelemetry>().ToArray();

            public Fixture(params HttpResponseMessage[] responses)
            {
                _configuration = new TelemetryConfiguration { TelemetryChannel = _channel };
                var telemetry = new TelemetryClient(_configuration);
                _handler = new FixtureHandler(responses);
                var handlers = GraphClientFactory.CreateDefaultHandlers();
                handlers.Add(new AgentGraphTelemetryHandler(NullLogger<AgentGraphTelemetryHandler>.Instance, telemetry));
                _httpClient = GraphClientFactory.Create(handlers, finalHandler: _handler);
                _client = new GraphServiceClient(_httpClient, new AnonymousAuthenticationProvider());
                var attempts = new Mock<IGraphServiceAttemptsValue>();
                attempts.SetupGet(value => value.MaxRetryAfterAttempts).Returns(1);
                attempts.SetupGet(value => value.MaxExceptionHandlingAttempts).Returns(1);
                var retry = new Repositories.RetryPolicyProvider.RetryPolicyProvider(
                    NullLogger<Repositories.RetryPolicyProvider.RetryPolicyProvider>.Instance, attempts.Object);
                Repository = new AgentGraphRepository(_client, retry, NullLogger<AgentGraphRepository>.Instance);
            }

            public void Dispose()
            {
                _client.Dispose();
                _httpClient.Dispose();
                _configuration.Dispose();
            }
        }

        private sealed class FixtureHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
        {
            private readonly ConcurrentQueue<HttpResponseMessage> _responses = new(responses);
            public List<(HttpMethod Method, Uri Uri, int SdkRetries)> Requests { get; } = [];

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Assert.IsNotNull(request.RequestUri);
                Assert.IsTrue(request.Options.TryGetValue(
                    new HttpRequestOptionsKey<RetryHandlerOption>(typeof(RetryHandlerOption).FullName!), out var option));
                Assert.IsNotNull(option);
                lock (Requests)
                {
                    Requests.Add((request.Method, request.RequestUri, option.MaxRetry));
                }
                if (!_responses.TryDequeue(out var response))
                {
                    throw new AssertFailedException("Unexpected extra physical Graph request.");
                }
                response.RequestMessage = request;
                return Task.FromResult(response);
            }
        }

        private sealed class CapturingChannel : ITelemetryChannel
        {
            public ConcurrentQueue<ITelemetry> Items { get; } = new();
            public bool? DeveloperMode { get; set; }
            public string EndpointAddress { get; set; } = string.Empty;
            public void Send(ITelemetry item) => Items.Enqueue(item);
            public void Flush() { }
            public void Dispose() { }
        }
    }
}
