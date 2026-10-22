// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Http.HttpClientLibrary.Middleware.Options;
using Microsoft.Kiota.Serialization.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Polly;
using Repositories.Contracts;
using Repositories.GraphGroups;
using System;
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
    /// I15 / FR-020: <c>GetAgentUserCountAsync</c> returns null, never a fabricated zero, when the count cannot be read,
    /// and logs a warning that mentions the agent count.
    /// </summary>
    [TestClass]
    public sealed class GraphGroupAgentCountTests
    {
        private const string GraphApiBaseUrl = "https://graph.microsoft.com/v1.0";

        [TestMethod]
        public async Task GetAgentUserCountAsync_RequestThrows_ReturnsNullAndWarns()
        {
            var fixture = CreateFixture(_ => throw new HttpRequestException("network down"));

            var count = await fixture.Repository.GetAgentUserCountAsync(Guid.NewGuid());

            Assert.IsNull(count);
            fixture.AssertOneAgentCountWarning();
        }

        [DataTestMethod]
        [DataRow(HttpStatusCode.InternalServerError)]
        [DataRow(HttpStatusCode.BadRequest)]
        [DataRow(HttpStatusCode.Forbidden)]
        public async Task GetAgentUserCountAsync_NonSuccessStatus_ReturnsNullAndWarns(HttpStatusCode status)
        {
            var fixture = CreateFixture(_ => Response(status, "{}"));

            var count = await fixture.Repository.GetAgentUserCountAsync(Guid.NewGuid());

            Assert.IsNull(count);
            fixture.AssertOneAgentCountWarning();
        }

        [DataTestMethod]
        [DataRow("{\"value\":[]}", DisplayName = "missing @odata.count")]
        [DataRow("{\"@odata.count\":\"5\",\"value\":[]}", DisplayName = "string count")]
        [DataRow("{\"@odata.count\":-1,\"value\":[]}", DisplayName = "negative count")]
        [DataRow("{\"@odata.count\":1.5,\"value\":[]}", DisplayName = "fractional count")]
        [DataRow("[]", DisplayName = "array body")]
        public async Task GetAgentUserCountAsync_MissingOrInvalidCount_ReturnsNullAndWarns(string body)
        {
            var fixture = CreateFixture(_ => Response(HttpStatusCode.OK, body));

            var count = await fixture.Repository.GetAgentUserCountAsync(Guid.NewGuid());

            Assert.IsNull(count);
            fixture.AssertOneAgentCountWarning();
        }

        [TestMethod]
        public async Task GetAgentUserCountAsync_NoResponse_ReturnsNullAndWarns()
        {
            var fixture = CreateFixture(_ => null);

            var count = await fixture.Repository.GetAgentUserCountAsync(Guid.NewGuid());

            Assert.IsNull(count);
            fixture.AssertOneAgentCountWarning();
        }

        [DataTestMethod]
        [DataRow(0)]
        [DataRow(42)]
        public async Task GetAgentUserCountAsync_MeasuredCount_ReturnsValueWithoutWarning(int measured)
        {
            RequestInformation sent = null;
            var fixture = CreateFixture(request =>
            {
                sent = request;
                return Response(HttpStatusCode.OK, $"{{\"@odata.count\":{measured},\"value\":[]}}");
            });
            var groupId = Guid.NewGuid();

            var count = await fixture.Repository.GetAgentUserCountAsync(groupId);

            Assert.AreEqual(measured, count);
            Assert.AreEqual(0, fixture.Warnings.Count);
            // The request shape is unchanged.
            Assert.AreEqual(
                $"{GraphApiBaseUrl}/groups/{groupId}/transitiveMembers?$count=true&$top=1&$filter=isof('microsoft.graph.agentUser')",
                sent.UrlTemplate);
            Assert.IsTrue(sent.Headers.TryGetValue("ConsistencyLevel", out var consistency));
            CollectionAssert.AreEqual(new[] { "eventual" }, consistency.ToArray());
        }

        private static HttpResponseMessage Response(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        private static Fixture CreateFixture(Func<RequestInformation, HttpResponseMessage> respond)
        {
            var requestAdapter = new Mock<IRequestAdapter>();
            requestAdapter.SetupProperty(adapter => adapter.BaseUrl, GraphApiBaseUrl);
            requestAdapter
                .SetupGet(adapter => adapter.SerializationWriterFactory)
                .Returns(new JsonSerializationWriterFactory());
            requestAdapter
                .Setup(adapter => adapter.SendPrimitiveAsync<int?>(
                    It.IsAny<RequestInformation>(),
                    It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestInformation, Dictionary<string, ParsableFactory<IParsable>>, CancellationToken>(
                    (requestInformation, _, _) =>
                    {
                        var handler = (NativeResponseHandler)requestInformation
                            .RequestOptions
                            .OfType<ResponseHandlerOption>()
                            .Single()
                            .ResponseHandler;
                        handler.Value = respond(requestInformation);
                    })
                .ReturnsAsync((int?)null);
            var retryPolicyProvider = new Mock<IRetryPolicyProvider>();
            retryPolicyProvider
                .Setup(provider => provider.CreateRetryAfterPolicy(It.IsAny<Guid?>()))
                .Returns(Policy.NoOpAsync<HttpResponseMessage>());
            retryPolicyProvider
                .Setup(provider => provider.CreateExceptionHandlingPolicy(It.IsAny<Guid?>()))
                .Returns(Policy.NoOpAsync<HttpResponseMessage>());
            var warnings = new List<string>();
            var logger = new Mock<ILogger>();
            logger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
            logger
                .Setup(l => l.Log(
                    It.IsAny<LogLevel>(),
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()))
                .Callback(new InvocationAction(invocation =>
                {
                    var level = (LogLevel)invocation.Arguments[0];
                    if (level == LogLevel.Warning)
                    {
                        warnings.Add(invocation.Arguments[2]?.ToString() ?? string.Empty);
                    }
                }));
            var loggerFactory = new Mock<ILoggerFactory>();
            loggerFactory.Setup(factory => factory.CreateLogger(It.IsAny<string>())).Returns(logger.Object);
            var repository = new GraphGroupRepository(
                new GraphServiceClient(requestAdapter.Object, GraphApiBaseUrl),
                new TelemetryClient(new TelemetryConfiguration()),
                retryPolicyProvider.Object,
                loggerFactory.Object);
            return new Fixture(repository, warnings);
        }

        private sealed record Fixture(GraphGroupRepository Repository, List<string> Warnings)
        {
            public void AssertOneAgentCountWarning()
            {
                Assert.AreEqual(1, Warnings.Count, string.Join(" | ", Warnings));
                StringAssert.Contains(Warnings[0].ToLowerInvariant(), "agent count");
            }
        }
    }
}
