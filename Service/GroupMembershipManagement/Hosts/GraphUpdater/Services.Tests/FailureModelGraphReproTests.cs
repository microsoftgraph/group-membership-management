// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph;
using Microsoft.Graph.Groups.Delta;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Http.HttpClientLibrary.Middleware.Options;
using Microsoft.Kiota.Serialization.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Polly;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.GraphGroups;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public sealed class FailureModelGraphReproTests
    {
        private const string GraphApiBaseUrl = "https://graph.microsoft.com/v1.0";

        /// <summary>
        /// I5: a delta removal must be returned even when Graph omits its type or labels it directoryObject.
        /// Finding 18. GraphGroupDeltaReader.cs:265-289 requires a user-family type before emitting @removed.
        /// Expected failing assertion: the typeless or generic removal is present in usersToRemove.
        /// </summary>
        [DataTestMethod]
        [DataRow("")]
        [DataRow("#microsoft.graph.directoryObject")]
        [TestCategory("FailureModelRepro")]
        public async Task I5_DeltaRemoval_AppliesWithoutUserTypeAnnotation(
            string odataType)
        {
            var groupId = Guid.NewGuid();
            var removedMemberId = Guid.NewGuid();
            var removedMemberJson = string.IsNullOrEmpty(odataType)
                ? $"{{\"id\":\"{removedMemberId}\",\"@removed\":{{\"reason\":\"changed\"}}}}"
                : $"{{\"@odata.type\":\"{odataType}\",\"id\":\"{removedMemberId}\",\"@removed\":{{\"reason\":\"changed\"}}}}";
            var requestAdapter = CreateRequestAdapter();
            requestAdapter
                .Setup(adapter => adapter.SendAsync(
                    It.IsAny<RequestInformation>(),
                    It.IsAny<ParsableFactory<DeltaResponse>>(),
                    It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestInformation, ParsableFactory<DeltaResponse>, Dictionary<string, ParsableFactory<IParsable>>, CancellationToken>(
                    (request, _, _, _) =>
                    {
                        SetNativeResponse(
                            request,
                            CreateJsonResponse(
                                $$"""
                                {
                                  "value": [{
                                    "id": "{{groupId}}",
                                    "members@delta": [
                                      {{removedMemberJson}}
                                    ]
                                  }],
                                  "@odata.deltaLink": "{{GraphApiBaseUrl}}/groups/delta?$deltatoken=done"
                                }
                                """));
                    })
                .ReturnsAsync((DeltaResponse)null);
            var repository = CreateRepository(requestAdapter.Object);

            var result = await repository.GetFirstDeltaLinkUsersPageAsync(
                groupId,
                $"{GraphApiBaseUrl}/groups/delta?$deltatoken=start",
                1);

            CollectionAssert.Contains(
                result.usersToRemove.Select(user => user.ObjectId).ToList(),
                removedMemberId,
                "The removal was discarded because its type annotation was absent or generic.");
        }

        /// <summary>
        /// I8 (amended by the owner, 2026-10-21): getByIds has exactly one retry owner, the shared RetryPolicyProvider,
        /// with the SDK retry handler disabled. A long Retry-After (delta or date) is waited out by that policy inside
        /// the activity; there is no story deadline and no hand-off to a Durable timer. Host cancellation ends the wait.
        /// Assertions: one physical send before the wait, MaxRetry is zero, and cancellation propagates promptly.
        /// </summary>
        [TestMethod]
        [DataRow("delta")]
        [DataRow("date")]
        [TestCategory("FailureModelRepro")]
        public async Task I8_GetByIds_LongRetryAfter_IsWaitedOutByTheSingleRetryOwner(
            string retryAfterForm)
        {
            var expectedRetryAtUtc = DateTimeOffset.UtcNow.AddSeconds(900);
            var handler = new LongThrottleHandler(
                retryAfterForm,
                expectedRetryAtUtc);
            var handlers = GraphClientFactory.CreateDefaultHandlers();
            using var httpClient = GraphClientFactory.Create(
                handlers,
                finalHandler: handler);
            using var graphClient = new GraphServiceClient(
                httpClient,
                new AnonymousAuthenticationProvider());
            var attempts = new Mock<IGraphServiceAttemptsValue>();
            attempts.SetupGet(value => value.MaxRetryAfterAttempts).Returns(1);
            attempts.SetupGet(value => value.MaxExceptionHandlingAttempts).Returns(0);
            var retryPolicy = new Repositories.RetryPolicyProvider.RetryPolicyProvider(
                NullLogger<Repositories.RetryPolicyProvider.RetryPolicyProvider>.Instance,
                attempts.Object);
            var repository = new GraphGroupRepository(
                graphClient,
                new TelemetryClient(new TelemetryConfiguration()),
                retryPolicy,
                NullLoggerFactory.Instance);
            using var hostCancellation = new CancellationTokenSource(
                TimeSpan.FromMilliseconds(300));
            var stopwatch = Stopwatch.StartNew();
            Exception failure = null;

            try
            {
                await repository.GetDirectoryObjectTypesAsync(
                    new[] { Guid.NewGuid() },
                    Guid.NewGuid(),
                    hostCancellation.Token);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            stopwatch.Stop();

            var failures = new List<string>();
            if (stopwatch.Elapsed >= TimeSpan.FromSeconds(2))
            {
                failures.Add($"the call blocked for {stopwatch.Elapsed}");
            }

            if (handler.Requests.Count != 1)
            {
                failures.Add($"expected one physical send before the policy's wait but observed {handler.Requests.Count}");
            }

            var sdkRetryLimits = handler.Requests
                .Select(request => request.SdkRetryLimit)
                .ToArray();
            if (sdkRetryLimits.Any(limit => limit != 0))
            {
                failures.Add(
                    $"the getByIds request did not disable SDK retries: [{string.Join(", ", sdkRetryLimits.Select(limit => limit?.ToString() ?? "<unset>"))}]");
            }

            if (failure is not OperationCanceledException)
            {
                failures.Add(
                    $"expected host cancellation to end the Retry-After wait but received {failure?.GetType().FullName ?? "no exception"}");
            }

            Assert.AreEqual(0, failures.Count, string.Join("; ", failures));
        }

        /// <summary>
        /// I9: a selected full-read fallback must retry a later-page 5xx response with the shared transient policy.
        /// Finding 22. GraphGroupMembershipReader.cs:534-567 throws HttpRequestException, which the base retry
        /// policy does not handle.
        /// Expected failing assertion: page three succeeds after one retry and the method returns normally.
        /// </summary>
        [TestMethod]
        [TestCategory("FailureModelRepro")]
        public async Task I9_SelectedFullRead_RetriesLaterPageServerFailure()
        {
            var groupId = Guid.NewGuid();
            var firstNextLink =
                $"{GraphApiBaseUrl}/groups/{groupId}/transitiveMembers?$skiptoken=page2";
            var secondNextLink =
                $"{GraphApiBaseUrl}/groups/{groupId}/transitiveMembers?$skiptoken=page3";
            var requestCount = 0;
            var requestAdapter = CreateRequestAdapter();
            requestAdapter
                .Setup(adapter => adapter.SendAsync(
                    It.IsAny<RequestInformation>(),
                    It.IsAny<ParsableFactory<UserCollectionResponse>>(),
                    It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestInformation, ParsableFactory<UserCollectionResponse>, Dictionary<string, ParsableFactory<IParsable>>, CancellationToken>(
                    (request, _, _, _) =>
                    {
                        requestCount++;
                        var response = requestCount switch
                        {
                            1 => CreateJsonResponse(
                                $$"""{"value":[],"@odata.nextLink":"{{firstNextLink}}"}"""),
                            2 => CreateJsonResponse(
                                $$"""{"value":[],"@odata.nextLink":"{{secondNextLink}}"}"""),
                            3 => new HttpResponseMessage(
                                HttpStatusCode.BadGateway),
                            _ => CreateJsonResponse("""{"value":[]}""")
                        };
                        SetNativeResponse(request, response);
                    })
                .ReturnsAsync((UserCollectionResponse)null);
            var attempts = new Mock<IGraphServiceAttemptsValue>();
            attempts.SetupGet(value => value.MaxRetryAfterAttempts).Returns(4);
            attempts.SetupGet(value => value.MaxExceptionHandlingAttempts).Returns(1);
            var repository = CreateRepository(
                requestAdapter.Object,
                new Repositories.RetryPolicyProvider.RetryPolicyProvider(
                    NullLogger<Repositories.RetryPolicyProvider.RetryPolicyProvider>.Instance,
                    attempts.Object));
            Exception failure = null;

            try
            {
                await repository.GetFirstTransitiveMembersPageAsync(
                    groupId,
                    new[] { "users" });
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            Assert.IsNull(
                failure,
                $"The third page was not retried. HTTP sends: {requestCount}. Failure: {failure}");
            Assert.AreEqual(4, requestCount);
        }

        private static GraphGroupRepository CreateRepository(
            IRequestAdapter requestAdapter,
            IRetryPolicyProvider retryPolicyProvider = null,
            ILoggerFactory loggerFactory = null)
        {
            return new GraphGroupRepository(
                new GraphServiceClient(requestAdapter, GraphApiBaseUrl),
                new TelemetryClient(new TelemetryConfiguration()),
                retryPolicyProvider ?? NoOpRetryPolicyProvider.Instance,
                loggerFactory);
        }

        private static Mock<IRequestAdapter> CreateRequestAdapter()
        {
            var requestAdapter = new Mock<IRequestAdapter>();
            requestAdapter.SetupProperty(
                adapter => adapter.BaseUrl,
                GraphApiBaseUrl);
            requestAdapter
                .SetupGet(adapter => adapter.SerializationWriterFactory)
                .Returns(new JsonSerializationWriterFactory());
            return requestAdapter;
        }

        private static void SetNativeResponse(
            RequestInformation request,
            HttpResponseMessage response)
        {
            var responseHandlerOption = request.RequestOptions
                .OfType<ResponseHandlerOption>()
                .Single();
            var nativeResponseHandler =
                (NativeResponseHandler)responseHandlerOption.ResponseHandler;
            nativeResponseHandler.Value = response;
        }

        private static HttpResponseMessage CreateJsonResponse(string content) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    content,
                    Encoding.UTF8,
                    "application/json")
            };

        private sealed class NoOpRetryPolicyProvider : IRetryPolicyProvider
        {
            public static NoOpRetryPolicyProvider Instance { get; } = new();

            public AsyncPolicy<HttpResponseMessage> CreateRetryAfterPolicy(
                Guid? runId) =>
                Policy.NoOpAsync<HttpResponseMessage>();

            public AsyncPolicy<HttpResponseMessage> CreateExceptionHandlingPolicy(
                Guid? runId) =>
                Policy.NoOpAsync<HttpResponseMessage>();
        }

        private sealed class LongThrottleHandler : HttpMessageHandler
        {
            private readonly string _retryAfterForm;
            private readonly DateTimeOffset _retryAtUtc;

            public LongThrottleHandler(
                string retryAfterForm,
                DateTimeOffset retryAtUtc)
            {
                _retryAfterForm = retryAfterForm;
                _retryAtUtc = retryAtUtc;
            }

            public List<RequestObservation> Requests { get; } = new();

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var optionKey = new HttpRequestOptionsKey<RetryHandlerOption>(
                    typeof(RetryHandlerOption).FullName);
                var sdkRetryLimit = request.Options.TryGetValue(
                    optionKey,
                    out var retryOption)
                    ? (int?)retryOption.MaxRetry
                    : null;
                Requests.Add(new RequestObservation(
                    request.RequestUri,
                    sdkRetryLimit));

                var response = new HttpResponseMessage(
                    HttpStatusCode.TooManyRequests)
                {
                    RequestMessage = request
                };
                response.Headers.RetryAfter = string.Equals(
                    _retryAfterForm,
                    "date",
                    StringComparison.Ordinal)
                    ? new RetryConditionHeaderValue(_retryAtUtc)
                    : new RetryConditionHeaderValue(TimeSpan.FromSeconds(900));

                return Task.FromResult(response);
            }
        }

        private sealed record RequestObservation(
            Uri RequestUri,
            int? SdkRetryLimit);
    }
}
