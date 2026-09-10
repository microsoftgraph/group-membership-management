// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Polly.Utilities;
using Repositories.Contracts.InjectConfig;
using System.Net;
using System.Net.Http.Headers;

namespace Repositories.RetryPolicyProvider.Tests
{
    [TestClass]
    [DoNotParallelize]
    public class RetryPolicyProviderTests
    {
        private readonly List<TimeSpan> _delays = new List<TimeSpan>();
        private Func<TimeSpan, CancellationToken, Task> _originalSleepAsync = null!;

        [TestInitialize]
        public void SetUp()
        {
            _delays.Clear();
            _originalSleepAsync = SystemClock.SleepAsync;
            SystemClock.SleepAsync = (delay, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                Assert.IsTrue(delay >= TimeSpan.Zero, "The policy requested a negative delay.");
                _delays.Add(delay);
                return Task.CompletedTask;
            };
        }

        [TestCleanup]
        public void TearDown()
        {
            SystemClock.SleepAsync = _originalSleepAsync;
        }

        [DataTestMethod]
        [DataRow(null, 150)]
        [DataRow("120", 120)]
        [DataRow("0", 0)]
        public async Task RetryAfterPolicy_UsesDeltaSecondsOrFallback(string? header, int expectedSeconds)
        {
            using var throttled = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            using var success = new HttpResponseMessage(HttpStatusCode.OK);
            if (header != null)
            {
                throttled.Headers.Add("Retry-After", header);
            }

            var attempts = 0;
            var result = await CreateProvider().CreateRetryAfterPolicy(Guid.NewGuid())
                .ExecuteAsync(() => Task.FromResult(++attempts == 1 ? throttled : success));

            Assert.AreSame(success, result);
            Assert.AreEqual(2, attempts);
            if (expectedSeconds == 0)
            {
                Assert.IsTrue(_delays.All(delay => delay == TimeSpan.Zero));
            }
            else
            {
                CollectionAssert.AreEqual(new[] { TimeSpan.FromSeconds(expectedSeconds) }, _delays);
            }
        }

        [TestMethod]
        public async Task RetryAfterPolicy_UsesHttpDate()
        {
            using var throttled = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            using var success = new HttpResponseMessage(HttpStatusCode.OK);
            var retryAt = DateTimeOffset.UtcNow.AddMinutes(2);
            throttled.Headers.RetryAfter = new RetryConditionHeaderValue(retryAt);
            var attempts = 0;
            var before = DateTimeOffset.UtcNow;

            var result = await CreateProvider().CreateRetryAfterPolicy(null)
                .ExecuteAsync(() => Task.FromResult(++attempts == 1 ? throttled : success));

            var after = DateTimeOffset.UtcNow;
            Assert.AreSame(success, result);
            Assert.AreEqual(2, attempts);
            Assert.AreEqual(1, _delays.Count);
            Assert.IsTrue(_delays[0] >= retryAt - after);
            Assert.IsTrue(_delays[0] <= retryAt - before);
        }

        [DataTestMethod]
        [DataRow(0)]
        [DataRow(1)]
        [DataRow(4)]
        public async Task RetryAfterPolicy_StopsAtConfiguredLimit(int retryAttempts)
        {
            using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            var attempts = 0;

            var result = await CreateProvider(retryAttempts).CreateRetryAfterPolicy(null)
                .ExecuteAsync(() =>
                {
                    attempts++;
                    return Task.FromResult(response);
                });

            Assert.AreSame(response, result);
            Assert.AreEqual(retryAttempts + 1, attempts);
            CollectionAssert.AreEqual(
                Enumerable.Repeat(TimeSpan.FromSeconds(150), retryAttempts).ToArray(), _delays);
        }

        [DataTestMethod]
        [DataRow(HttpStatusCode.OK)]
        [DataRow(HttpStatusCode.BadRequest)]
        [DataRow(HttpStatusCode.Unauthorized)]
        [DataRow(HttpStatusCode.Forbidden)]
        [DataRow(HttpStatusCode.NotFound)]
        [DataRow(HttpStatusCode.RequestTimeout)]
        [DataRow(HttpStatusCode.InternalServerError)]
        [DataRow(HttpStatusCode.BadGateway)]
        [DataRow(HttpStatusCode.ServiceUnavailable)]
        [DataRow(HttpStatusCode.GatewayTimeout)]
        public async Task RetryAfterPolicy_DoesNotRetryOtherStatuses(HttpStatusCode statusCode)
        {
            using var response = new HttpResponseMessage(statusCode);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(120));
            var attempts = 0;

            var result = await CreateProvider().CreateRetryAfterPolicy(null)
                .ExecuteAsync(() =>
                {
                    attempts++;
                    return Task.FromResult(response);
                });

            Assert.AreSame(response, result);
            Assert.AreEqual(1, attempts);
            Assert.AreEqual(0, _delays.Count);
        }

        [TestMethod]
        public async Task RetryAfterPolicy_DoesNotHandleHttpRequestExceptions()
        {
            var exception = new HttpRequestException("Test request failure.");
            var attempts = 0;

            var thrown = await Assert.ThrowsExceptionAsync<HttpRequestException>(() =>
                CreateProvider().CreateRetryAfterPolicy(null).ExecuteAsync(() =>
                {
                    attempts++;
                    return Task.FromException<HttpResponseMessage>(exception);
                }));

            Assert.AreSame(exception, thrown);
            Assert.AreEqual(1, attempts);
            Assert.AreEqual(0, _delays.Count);
        }

        [DataTestMethod]
        [DataRow(HttpStatusCode.InternalServerError)]
        [DataRow(HttpStatusCode.BadGateway)]
        [DataRow(HttpStatusCode.ServiceUnavailable)]
        [DataRow(HttpStatusCode.GatewayTimeout)]
        public async Task ExceptionPolicy_PreservesTransientStatusRetries(HttpStatusCode statusCode)
        {
            using var response = new HttpResponseMessage(statusCode);
            var attempts = 0;

            var result = await CreateProvider().CreateExceptionHandlingPolicy(null)
                .ExecuteAsync(() =>
                {
                    attempts++;
                    return Task.FromResult(response);
                });

            Assert.AreSame(response, result);
            Assert.AreEqual(3, attempts);
            CollectionAssert.AreEqual(new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4) }, _delays);
        }

        [TestMethod]
        public async Task ExceptionPolicy_PreservesHttpRequestExceptionRetries()
        {
            var exception = new HttpRequestException("Test request failure.");
            var attempts = 0;

            var thrown = await Assert.ThrowsExceptionAsync<HttpRequestException>(() =>
                CreateProvider().CreateExceptionHandlingPolicy(null).ExecuteAsync(() =>
                {
                    attempts++;
                    return Task.FromException<HttpResponseMessage>(exception);
                }));

            Assert.AreSame(exception, thrown);
            Assert.AreEqual(3, attempts);
            CollectionAssert.AreEqual(new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4) }, _delays);
        }

        [TestMethod]
        public async Task WrappedPolicies_PreserveThrottleAndTransientErrorHandling()
        {
            using var throttled = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            using var unavailable = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            using var success = new HttpResponseMessage(HttpStatusCode.OK);
            throttled.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(120));
            var responses = new Queue<HttpResponseMessage>(new[] { throttled, unavailable, success });
            var provider = CreateProvider();
            var policy = provider.CreateRetryAfterPolicy(null)
                .WrapAsync(provider.CreateExceptionHandlingPolicy(null));

            var result = await policy.ExecuteAsync(() => Task.FromResult(responses.Dequeue()));

            Assert.AreSame(success, result);
            Assert.AreEqual(0, responses.Count);
            CollectionAssert.AreEqual(new[] { TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(2) }, _delays);
        }

        private static RetryPolicyProvider CreateProvider(int retryAttempts = 4)
        {
            var attempts = Mock.Of<IGraphServiceAttemptsValue>(value =>
                value.MaxRetryAfterAttempts == retryAttempts && value.MaxExceptionHandlingAttempts == 2);
            return new RetryPolicyProvider(NullLogger<RetryPolicyProvider>.Instance, attempts);
        }
    }
}
