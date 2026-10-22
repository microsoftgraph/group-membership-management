// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary.Middleware.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Polly.Utilities;
using Repositories.Contracts;
using Repositories.Contracts.Constants;
using Repositories.Contracts.InjectConfig;
using Repositories.GraphGroups;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Services.Tests
{
    /// <summary>
    /// getByIds and transitive-members page requests have exactly one retry owner: the shared retry policies (the 429
    /// policy around the transient policy, with their configured attempts). These tests drive a real GraphServiceClient
    /// (with the SDK's default middleware) over a counting HTTP handler, so every physical send is observed, and verify
    /// that the SDK retry handler is disabled on every send. When the attempts are exhausted the request throws with
    /// the last status; there is no deadline and no hand-off to a Durable timer.
    /// </summary>
    [TestClass]
    [DoNotParallelize]
    public sealed class GraphGroupRepositoryRetryOwnerTests
    {
        private const string GraphApiBaseUrl = "https://graph.microsoft.com/v1.0";
        private static readonly DateTimeOffset Start = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

        private readonly List<TimeSpan> _delays = new();
        private DateTimeOffset _now;
        private Func<TimeSpan, CancellationToken, Task> _originalSleepAsync;
        private Func<DateTimeOffset> _originalUtcNow;

        [TestInitialize]
        public void SetUp()
        {
            _delays.Clear();
            _now = Start;
            _originalSleepAsync = SystemClock.SleepAsync;
            _originalUtcNow = SystemClock.DateTimeOffsetUtcNow;
            SystemClock.DateTimeOffsetUtcNow = () => _now;
            SystemClock.SleepAsync = (delay, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                _delays.Add(delay);
                _now += delay;
                return Task.CompletedTask;
            };
        }

        [TestCleanup]
        public void TearDown()
        {
            SystemClock.SleepAsync = _originalSleepAsync;
            SystemClock.DateTimeOffsetUtcNow = _originalUtcNow;
        }

        [TestMethod]
        public async Task GetByIds_ServerFailureThenSuccess_SendsOncePerPolicyAttemptWithSdkRetryDisabled()
        {
            var id = Guid.NewGuid();
            var handler = new CountingHandler(
                _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
                _ => Json($$"""{"value":[{"@odata.type":"#microsoft.graph.user","id":"{{id}}"}]}"""));
            using var fixture = new Fixture(handler);

            var result = await fixture.Repository.GetDirectoryObjectTypesAsync(new[] { id }, Guid.NewGuid(), CancellationToken.None);

            Assert.AreEqual("user", result[id]);
            Assert.AreEqual(2, handler.Requests.Count);
            AssertSdkRetryDisabled(handler);
            Assert.IsTrue(handler.Requests.All(r => r.Method == HttpMethod.Post));
            Assert.AreEqual(1, handler.Requests.Select(r => r.Uri.ToString()).Distinct().Count(), "The retried request changed its URL.");
            StringAssert.Contains(Uri.UnescapeDataString(handler.Requests[0].Uri.ToString()), "/directoryObjects/getByIds?$select=id");
            CollectionAssert.AreEqual(new[] { TimeSpan.FromSeconds(2) }, _delays);
        }

        [TestMethod]
        public async Task GetByIds_ShortThrottleThenSuccess_WaitsAndRecordsResourceUnitsOfEveryAttempt()
        {
            var id = Guid.NewGuid();
            var handler = new CountingHandler(
                _ => Throttled(TimeSpan.FromSeconds(5), resourceUnit: "1"),
                _ =>
                {
                    var response = Json($$"""{"value":[{"@odata.type":"#microsoft.graph.agentUser","id":"{{id}}"}]}""");
                    response.Headers.Add("x-ms-resource-unit", "2");
                    return response;
                });
            using var fixture = new Fixture(handler);

            var result = await fixture.Repository.GetDirectoryObjectTypesAsync(new[] { id }, Guid.NewGuid(), CancellationToken.None);

            Assert.AreEqual("agentUser", result[id]);
            Assert.AreEqual(2, handler.Requests.Count);
            AssertSdkRetryDisabled(handler);
            CollectionAssert.AreEqual(new[] { TimeSpan.FromSeconds(5) }, _delays);
            var resourceUnitEvents = fixture.Channel.Items
                .OfType<EventTelemetry>()
                .Where(item => item.Name == TelemetryConstants.ResourceUnitsEventName)
                .ToList();
            CollectionAssert.AreEqual(new[] { "1", "2" }, resourceUnitEvents.Select(e => e.Properties["ResourceUnitsUsed"]).ToArray());
            Assert.IsTrue(resourceUnitEvents.All(e =>
                e.Properties[TelemetryConstants.ResourceUnitSourceDimensionName] == TelemetryConstants.ResourceUnitSourceHeader));
        }

        [TestMethod]
        public async Task GetByIds_LongThrottle_IsWaitedOutByTheSharedPolicy()
        {
            var id = Guid.NewGuid();
            var handler = new CountingHandler(
                _ => Throttled(TimeSpan.FromSeconds(900), resourceUnit: "2"),
                _ => Json($$"""{"value":[{"@odata.type":"#microsoft.graph.user","id":"{{id}}"}]}"""));
            using var fixture = new Fixture(handler);

            var result = await fixture.Repository.GetDirectoryObjectTypesAsync(new[] { id }, null, CancellationToken.None);

            Assert.AreEqual("user", result[id]);
            Assert.AreEqual(2, handler.Requests.Count);
            AssertSdkRetryDisabled(handler);
            CollectionAssert.AreEqual(new[] { TimeSpan.FromSeconds(900) }, _delays, "Retry-After is honored as sent, with no deadline.");
        }

        [TestMethod]
        public async Task GetByIds_ThrottleAtAttemptExhaustion_ThrowsWithTheLastStatusAndRecordsEveryAttempt()
        {
            var handler = new CountingHandler(_ => Throttled(TimeSpan.FromSeconds(1), resourceUnit: "1"));
            using var fixture = new Fixture(handler, maxRetryAfterAttempts: 2);

            var exception = await Assert.ThrowsExceptionAsync<GraphRetriesExhaustedException>(
                () => fixture.Repository.GetDirectoryObjectTypesAsync(new[] { Guid.NewGuid() }, null, CancellationToken.None));

            Assert.AreEqual(HttpStatusCode.TooManyRequests, exception.StatusCode);
            Assert.AreEqual(3, handler.Requests.Count, "One send plus the two configured throttle retries.");
            AssertSdkRetryDisabled(handler);
            Assert.AreEqual(
                3,
                fixture.Channel.Items.OfType<EventTelemetry>().Count(e => e.Name == TelemetryConstants.ResourceUnitsEventName),
                "Every throttled attempt's resource units are recorded.");
            Assert.IsFalse(exception.Message.Contains("The request timed out", StringComparison.Ordinal),
                "The orchestrator reschedules (instead of failing) an exception whose message contains this text.");
        }

        [TestMethod]
        public async Task GetByIds_ServerFailureAtAttemptExhaustion_ThrowsWithTheLastStatus()
        {
            var handler = new CountingHandler(_ => new HttpResponseMessage(HttpStatusCode.GatewayTimeout));
            using var fixture = new Fixture(handler, maxExceptionHandlingAttempts: 2);

            var exception = await Assert.ThrowsExceptionAsync<GraphRetriesExhaustedException>(
                () => fixture.Repository.GetDirectoryObjectTypesAsync(new[] { Guid.NewGuid() }, null, CancellationToken.None));

            Assert.AreEqual(HttpStatusCode.GatewayTimeout, exception.StatusCode);
            Assert.AreEqual(3, handler.Requests.Count);
            CollectionAssert.AreEqual(new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4) }, _delays);
            Assert.IsFalse(exception.Message.Contains("The request timed out", StringComparison.Ordinal));
        }

        [TestMethod]
        public async Task GetByIds_Forbidden_IsNotRetried()
        {
            var handler = new CountingHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
            using var fixture = new Fixture(handler);

            var exception = await Assert.ThrowsExceptionAsync<HttpRequestException>(
                () => fixture.Repository.GetDirectoryObjectTypesAsync(new[] { Guid.NewGuid() }, null, CancellationToken.None));

            Assert.AreEqual(HttpStatusCode.Forbidden, exception.StatusCode);
            Assert.AreEqual(1, handler.Requests.Count);
        }

        [TestMethod]
        public async Task TransitiveFirstPage_BothTypes_ServerFailureIsRetriedOncePerAttempt()
        {
            var groupId = Guid.NewGuid();
            var memberId = Guid.NewGuid();
            var handler = new CountingHandler(
                _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
                _ => Json($$"""{"value":[{"@odata.type":"#microsoft.graph.user","id":"{{memberId}}"}]}"""));
            using var fixture = new Fixture(handler);

            var result = await fixture.Repository.GetFirstTransitiveMembersPageAsync(groupId);

            Assert.AreEqual(memberId, result.users.Single().ObjectId);
            Assert.AreEqual(2, handler.Requests.Count);
            AssertSdkRetryDisabled(handler);
            Assert.AreEqual(1, handler.Requests.Select(r => r.Uri.ToString()).Distinct().Count());
            StringAssert.Contains(Uri.UnescapeDataString(handler.Requests[0].Uri.ToString()), $"/groups/{groupId}/transitiveMembers?$top=999");
            Assert.IsTrue(handler.Requests.All(r => !r.Headers.Contains("ConsistencyLevel")));
            CollectionAssert.AreEqual(new[] { TimeSpan.FromSeconds(2) }, _delays);
        }

        [TestMethod]
        public async Task TransitiveFirstPage_UsersOnly_ShortThrottleIsWaitedOutWithUnchangedRequest()
        {
            var groupId = Guid.NewGuid();
            var handler = new CountingHandler(
                _ => Throttled(TimeSpan.FromSeconds(3)),
                _ => Json("""{"value":[]}"""));
            using var fixture = new Fixture(handler);

            await fixture.Repository.GetFirstTransitiveMembersPageAsync(groupId, new[] { "users" });

            Assert.AreEqual(2, handler.Requests.Count);
            AssertSdkRetryDisabled(handler);
            Assert.AreEqual(1, handler.Requests.Select(r => r.Uri.ToString()).Distinct().Count());
            StringAssert.Contains(Uri.UnescapeDataString(handler.Requests[0].Uri.ToString()), "/transitiveMembers/microsoft.graph.user?$filter=not isof('microsoft.graph.agentUser')");
            Assert.IsTrue(handler.Requests.All(r => r.Headers.GetValues("ConsistencyLevel").Single() == "eventual"));
            CollectionAssert.AreEqual(new[] { TimeSpan.FromSeconds(3) }, _delays);
        }

        [TestMethod]
        public async Task TransitiveNextPage_ServerFailureIsRetriedOnTheSameLink()
        {
            var groupId = Guid.NewGuid();
            var nextLink = $"{GraphApiBaseUrl}/groups/{groupId}/transitiveMembers?$skiptoken=page2";
            var handler = new CountingHandler(
                _ => new HttpResponseMessage(HttpStatusCode.BadGateway),
                _ => Json("""{"value":[]}"""));
            using var fixture = new Fixture(handler);

            var result = await fixture.Repository.GetNextTransitiveMembersPageAsync(groupId, nextLink, new[] { "agents" });

            Assert.IsNull(result.nextPageUrl);
            Assert.AreEqual(2, handler.Requests.Count);
            AssertSdkRetryDisabled(handler);
            Assert.IsTrue(handler.Requests.All(r => Uri.UnescapeDataString(r.Uri.ToString()) == Uri.UnescapeDataString(nextLink)));
        }

        [TestMethod]
        public async Task TransitivePage_ThrottleAtAttemptExhaustion_ThrowsWithTheLastStatus()
        {
            var handler = new CountingHandler(_ => Throttled(TimeSpan.FromSeconds(900)));
            using var fixture = new Fixture(handler, maxRetryAfterAttempts: 1);

            var exception = await Assert.ThrowsExceptionAsync<GraphRetriesExhaustedException>(
                () => fixture.Repository.GetFirstTransitiveMembersPageAsync(Guid.NewGuid(), new[] { "users" }));

            Assert.AreEqual(HttpStatusCode.TooManyRequests, exception.StatusCode);
            Assert.AreEqual(2, handler.Requests.Count, "One send plus the one configured throttle retry.");
            AssertSdkRetryDisabled(handler);
            CollectionAssert.AreEqual(new[] { TimeSpan.FromSeconds(900) }, _delays);
            Assert.IsFalse(exception.Message.Contains("The request timed out", StringComparison.Ordinal));
        }

        [DataTestMethod]
        [DataRow(null, DisplayName = "uncast (SDK GetAsync path)")]
        [DataRow("agents", DisplayName = "selected (RequestInformation path)")]
        public async Task TransitivePage_RecordsResourceUnitsOfEveryAttemptOnce(string selectedType)
        {
            var handler = new CountingHandler(
                _ => Throttled(TimeSpan.FromSeconds(3), resourceUnit: "1"),
                _ =>
                {
                    var response = Json("""{"value":[]}""");
                    response.Headers.Add("x-ms-resource-unit", "3");
                    return response;
                });
            using var fixture = new Fixture(handler);

            await fixture.Repository.GetFirstTransitiveMembersPageAsync(
                Guid.NewGuid(),
                selectedType == null ? null : new[] { selectedType });

            var resourceUnitEvents = fixture.Channel.Items
                .OfType<EventTelemetry>()
                .Where(item => item.Name == TelemetryConstants.ResourceUnitsEventName)
                .ToList();
            CollectionAssert.AreEqual(
                new[] { "1", "3" },
                resourceUnitEvents.Select(e => e.Properties["ResourceUnitsUsed"]).ToArray(),
                "Each physical attempt is recorded exactly once, including the throttled one.");
            Assert.IsTrue(resourceUnitEvents.All(e =>
                e.Properties[TelemetryConstants.ResourceUnitSourceDimensionName] == TelemetryConstants.ResourceUnitSourceHeader));
        }

        [TestMethod]
        public async Task TransitivePage_ThrownHttpRequestException_UsesOnlyThePolicyAttemptsAndNoTwoMinuteWait()
        {
            var handler = new CountingHandler(_ => throw new HttpRequestException("connection reset"));
            using var fixture = new Fixture(handler, maxExceptionHandlingAttempts: 2);

            await Assert.ThrowsExceptionAsync<GraphRetriesExhaustedException>(
                () => fixture.Repository.GetFirstTransitiveMembersPageAsync(Guid.NewGuid(), new[] { "users" }));

            Assert.AreEqual(3, handler.Requests.Count, "One send plus the policy's two transient retries.");
            CollectionAssert.AreEqual(new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4) }, _delays);
            Assert.IsTrue(_delays.All(delay => delay < TimeSpan.FromMinutes(2)));
        }

        [TestMethod]
        public async Task TransitivePage_ThrownServiceException_IsNotRetriedByASecondOwner()
        {
            var handler = new CountingHandler(_ => throw new ServiceException("transient"));
            using var fixture = new Fixture(handler);

            await Assert.ThrowsExceptionAsync<ServiceException>(
                () => fixture.Repository.GetFirstTransitiveMembersPageAsync(Guid.NewGuid()));

            Assert.AreEqual(1, handler.Requests.Count);
            Assert.AreEqual(0, _delays.Count, "The legacy 2-minute ServiceException retry still wraps the page request.");
        }

        [TestMethod]
        public async Task TransitivePage_NotFound_ThrowsWithoutRetry()
        {
            var handler = new CountingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
            using var fixture = new Fixture(handler);

            var exception = await Assert.ThrowsExceptionAsync<HttpRequestException>(
                () => fixture.Repository.GetFirstTransitiveMembersPageAsync(Guid.NewGuid(), new[] { "users" }));

            Assert.AreEqual(HttpStatusCode.NotFound, exception.StatusCode);
            Assert.AreEqual(1, handler.Requests.Count);
        }

        [TestMethod]
        public async Task GetByIds_CancellationDuringThrottleWait_Propagates()
        {
            using var cancellation = new CancellationTokenSource();
            SystemClock.SleepAsync = (_, token) =>
            {
                cancellation.Cancel();
                return Task.FromCanceled(token);
            };
            var handler = new CountingHandler(_ => Throttled(TimeSpan.FromSeconds(5)));
            using var fixture = new Fixture(handler);

            await Assert.ThrowsExceptionAsync<TaskCanceledException>(
                () => fixture.Repository.GetDirectoryObjectTypesAsync(new[] { Guid.NewGuid() }, null, cancellation.Token));

            Assert.AreEqual(1, handler.Requests.Count);
        }

        [TestMethod]
        public async Task TransitivePage_ThirdOfFivePagesStillThrottled_ThrowsAndReturnsNoPartialPages()
        {
            var groupId = Guid.NewGuid();
            var page2 = $"{GraphApiBaseUrl}/groups/{groupId}/transitiveMembers?$skiptoken=page2";
            var page3 = $"{GraphApiBaseUrl}/groups/{groupId}/transitiveMembers?$skiptoken=page3";
            var handler = new CountingHandler(
                _ => Json($$"""{"value":[{"@odata.type":"#microsoft.graph.user","id":"{{Guid.NewGuid()}}"}],"@odata.nextLink":"{{page2}}"}"""),
                _ => Json($$"""{"value":[{"@odata.type":"#microsoft.graph.user","id":"{{Guid.NewGuid()}}"}],"@odata.nextLink":"{{page3}}"}"""),
                _ => Throttled(TimeSpan.FromSeconds(1)));
            using var fixture = new Fixture(handler, maxRetryAfterAttempts: 1);

            var exception = await Assert.ThrowsExceptionAsync<GraphRetriesExhaustedException>(
                () => fixture.Repository.GetFirstTransitiveMembersPageAsync(groupId));

            // Pages 1 and 2 were read but never returned: the reader activity therefore uploads nothing for the call.
            Assert.AreEqual(HttpStatusCode.TooManyRequests, exception.StatusCode);
            Assert.AreEqual(4, handler.Requests.Count, "Pages 1 and 2, then page 3 plus its one configured throttle retry.");
            Assert.IsTrue(Uri.UnescapeDataString(handler.Requests[3].Uri.ToString()).Contains("skiptoken=page3"));
        }

        [TestMethod]
        public async Task GetByIds_HttpClientTimeoutThenSuccess_IsRetriedOnce()
        {
            var id = Guid.NewGuid();
            var handler = new CountingHandler(
                Hang,
                _ => Json($$"""{"value":[{"@odata.type":"#microsoft.graph.user","id":"{{id}}"}]}"""));
            using var fixture = new Fixture(handler, httpClientTimeout: TimeSpan.FromMilliseconds(200));

            var result = await fixture.Repository.GetDirectoryObjectTypesAsync(new[] { id }, null, CancellationToken.None);

            Assert.AreEqual("user", result[id]);
            Assert.AreEqual(2, handler.Requests.Count, "The original send plus one retry after the HttpClient timeout.");
            CollectionAssert.AreEqual(new[] { TimeSpan.FromSeconds(2) }, _delays);
        }

        [TestMethod]
        public async Task GetByIds_HttpClientTimeoutTwice_ThrowsTheTimeoutAfterTwoSends()
        {
            var handler = new CountingHandler(Hang);
            using var fixture = new Fixture(handler, httpClientTimeout: TimeSpan.FromMilliseconds(200));

            var exception = await Assert.ThrowsExceptionAsync<GraphRetriesExhaustedException>(
                () => fixture.Repository.GetDirectoryObjectTypesAsync(new[] { Guid.NewGuid() }, null, CancellationToken.None));

            Assert.IsInstanceOfType(exception.InnerException, typeof(TaskCanceledException), "The second HttpClient timeout is the inner exception.");
            Assert.IsInstanceOfType(exception.InnerException.InnerException, typeof(TimeoutException), "A real HttpClient timeout carries a TimeoutException.");
            Assert.AreEqual(2, handler.Requests.Count, "At most the original send plus one retry.");
        }

        [TestMethod]
        public async Task GetByIds_SecondHttpClientTimeoutAfterAServerRetry_IsRetryExhaustion()
        {
            var id = Guid.NewGuid();
            var handler = new CountingHandler(
                Hang,
                _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
                Hang,
                _ => Json($$"""{"value":[{"@odata.type":"#microsoft.graph.user","id":"{{id}}"}]}"""));
            using var fixture = new Fixture(handler, httpClientTimeout: TimeSpan.FromMilliseconds(200));

            var exception = await Assert.ThrowsExceptionAsync<GraphRetriesExhaustedException>(
                () => fixture.Repository.GetDirectoryObjectTypesAsync(new[] { id }, null, CancellationToken.None));

            Assert.IsInstanceOfType(exception.InnerException, typeof(TaskCanceledException), "The second HttpClient timeout ends the request.");
            Assert.AreEqual(3, handler.Requests.Count, "Timeout, 503, timeout: a 5xx retry in between does not reset the timeout count.");
        }

        [TestMethod]
        public async Task TransitivePage_HttpClientTimeoutTwice_ThrowsAfterTwoSends()
        {
            var handler = new CountingHandler(Hang);
            using var fixture = new Fixture(handler, httpClientTimeout: TimeSpan.FromMilliseconds(200));

            await Assert.ThrowsExceptionAsync<GraphRetriesExhaustedException>(
                () => fixture.Repository.GetFirstTransitiveMembersPageAsync(Guid.NewGuid(), new[] { "agents" }));

            Assert.AreEqual(2, handler.Requests.Count);
            AssertSdkRetryDisabled(handler);
        }

        [TestMethod]
        public async Task GetByIds_CallerCancellation_IsNotRetriedAsATimeout()
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            var handler = new CountingHandler(Hang);
            using var fixture = new Fixture(handler, httpClientTimeout: TimeSpan.FromSeconds(30));

            var exception = await Assert.ThrowsExceptionAsync<TaskCanceledException>(
                () => fixture.Repository.GetDirectoryObjectTypesAsync(new[] { Guid.NewGuid() }, null, cancellation.Token));

            Assert.IsNotInstanceOfType(exception.InnerException, typeof(TimeoutException));
            Assert.AreEqual(1, handler.Requests.Count, "Caller or host cancellation is never retried.");
        }

        private static void AssertSdkRetryDisabled(CountingHandler handler)
        {
            var limits = handler.Requests.Select(r => r.SdkRetryLimit).ToArray();
            Assert.IsTrue(
                limits.All(limit => limit == 0),
                $"The SDK retry handler was not disabled: [{string.Join(", ", limits.Select(l => l?.ToString() ?? "<unset>"))}]");
        }

        private static HttpResponseMessage Throttled(TimeSpan retryAfter, string resourceUnit = null)
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter);
            if (resourceUnit != null)
            {
                response.Headers.Add("x-ms-resource-unit", resourceUnit);
            }

            return response;
        }

        /// <summary>A script step that never answers, so the HttpClient timeout (or the caller's token) ends the send.</summary>
        private static HttpResponseMessage Hang(HttpRequestMessage request) => null;

        private static HttpResponseMessage Json(string content) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json"),
            };

        private sealed class Fixture : IDisposable
        {
            private readonly HttpClient _httpClient;
            private readonly GraphServiceClient _graphClient;
            private readonly TelemetryConfiguration _configuration;

            public Fixture(CountingHandler handler, int maxRetryAfterAttempts = 4, int maxExceptionHandlingAttempts = 2, TimeSpan? httpClientTimeout = null)
            {
                _httpClient = GraphClientFactory.Create(GraphClientFactory.CreateDefaultHandlers(), finalHandler: handler);
                if (httpClientTimeout.HasValue)
                {
                    _httpClient.Timeout = httpClientTimeout.Value;
                }

                _graphClient = new GraphServiceClient(_httpClient, new AnonymousAuthenticationProvider());
                _configuration = new TelemetryConfiguration { TelemetryChannel = Channel };
                var attempts = new Mock<IGraphServiceAttemptsValue>();
                attempts.SetupGet(value => value.MaxRetryAfterAttempts).Returns(maxRetryAfterAttempts);
                attempts.SetupGet(value => value.MaxExceptionHandlingAttempts).Returns(maxExceptionHandlingAttempts);
                Repository = new GraphGroupRepository(
                    _graphClient,
                    new TelemetryClient(_configuration),
                    new Repositories.RetryPolicyProvider.RetryPolicyProvider(
                        NullLogger<Repositories.RetryPolicyProvider.RetryPolicyProvider>.Instance,
                        attempts.Object),
                    NullLoggerFactory.Instance);
            }

            public CapturingChannel Channel { get; } = new();
            public GraphGroupRepository Repository { get; }

            public void Dispose()
            {
                _graphClient.Dispose();
                _httpClient.Dispose();
                _configuration.Dispose();
            }
        }

        private sealed class CountingHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage>[] _script;

            public CountingHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] script)
            {
                _script = script;
            }

            public List<RequestObservation> Requests { get; } = new();

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var optionKey = new HttpRequestOptionsKey<RetryHandlerOption>(typeof(RetryHandlerOption).FullName);
                var sdkRetryLimit = request.Options.TryGetValue(optionKey, out var retryOption)
                    ? (int?)retryOption.MaxRetry
                    : null;
                Requests.Add(new RequestObservation(request.Method, request.RequestUri, request.Headers, sdkRetryLimit));
                var step = _script[Math.Min(Requests.Count - 1, _script.Length - 1)];
                var response = step(request);
                if (response == null)
                {
                    // Hang until the HttpClient timeout or the caller's token cancels the send.
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }

                response.RequestMessage = request;
                return response;
            }
        }

        private sealed record RequestObservation(HttpMethod Method, Uri Uri, HttpRequestHeaders Headers, int? SdkRetryLimit);

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
