// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Core.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Security.Claims;
using System.Text;
using RetryContext = Microsoft.Azure.Functions.Worker.RetryContext;

namespace SqlDataChecker.Function.Tests
{
    [TestClass]
    public class StarterFunctionTests
    {
        private static async Task<(HttpResponseData Response, FakeDurableTaskClient Client)> RunAsync(string body)
        {
            var client = new FakeDurableTaskClient("test");
            var request = new FakeHttpRequestData(
                new FakeFunctionContext(),
                new Uri("http://localhost/api/StarterFunction"),
                new MemoryStream(Encoding.UTF8.GetBytes(body)));

            var response = await new StarterFunction(NullLogger<StarterFunction>.Instance).RunAsync(request, client);
            return (response, client);
        }

        [TestMethod]
        public async Task ValidRunId_SchedulesOrchestrationWithThatRunId()
        {
            var runId = Guid.NewGuid();

            var (response, client) = await RunAsync($"{{\"RunId\":\"{runId}\"}}");

            Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);
            Assert.AreEqual(1, client.ScheduledNames.Count);
            Assert.AreEqual(nameof(OrchestratorFunction), client.ScheduledNames[0]);
            Assert.AreEqual(runId, client.ScheduledInputs[0]);
        }

        [TestMethod]
        public async Task RunIdIsMatchedCaseInsensitivelyAndAcceptsAnyGuidFormat()
        {
            var runId = Guid.NewGuid();

            var (response, client) = await RunAsync($"{{\"runid\":\"{runId:N}\"}}");

            Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);
            Assert.AreEqual(runId, client.ScheduledInputs.Single());
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow("{}")]
        [DataRow("null")]
        [DataRow("not json")]
        [DataRow("[]")]
        [DataRow("{\"RunId\":null}")]
        [DataRow("{\"RunId\":\"\"}")]
        [DataRow("{\"RunId\":\"not-a-guid\"}")]
        [DataRow("{\"RunId\":\"00000000-0000-0000-0000-000000000000\"}")]
        public async Task MissingOrInvalidRunId_ReturnsBadRequestAndSchedulesNothing(string body)
        {
            var (response, client) = await RunAsync(body);

            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.AreEqual(0, client.ScheduledNames.Count);
        }
    }

    public class FakeDurableTaskClient : DurableTaskClient
    {
        public List<string> ScheduledNames { get; } = new();
        public List<object?> ScheduledInputs { get; } = new();

        public FakeDurableTaskClient(string name) : base(name)
        {
        }

        public override Task<string> ScheduleNewOrchestrationInstanceAsync(
            TaskName orchestratorName,
            object? input = null,
            StartOrchestrationOptions? options = null,
            CancellationToken cancellation = default)
        {
            ScheduledNames.Add(orchestratorName.Name);
            ScheduledInputs.Add(input);
            return Task.FromResult("instance-" + ScheduledNames.Count);
        }

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public override AsyncPageable<OrchestrationMetadata> GetAllInstancesAsync(OrchestrationQuery? filter = null)
            => throw new NotImplementedException();

        public override Task<OrchestrationMetadata?> GetInstancesAsync(string instanceId, bool getInputsAndOutputs = false, CancellationToken cancellation = default)
            => throw new NotImplementedException();

        public override Task RaiseEventAsync(string instanceId, string eventName, object? eventPayload = null, CancellationToken cancellation = default)
            => throw new NotImplementedException();

        public override Task ResumeInstanceAsync(string instanceId, string? reason = null, CancellationToken cancellation = default)
            => throw new NotImplementedException();

        public override Task SuspendInstanceAsync(string instanceId, string? reason = null, CancellationToken cancellation = default)
            => throw new NotImplementedException();

        public override Task<OrchestrationMetadata> WaitForInstanceCompletionAsync(string instanceId, bool getInputsAndOutputs = false, CancellationToken cancellation = default)
            => throw new NotImplementedException();

        public override Task<OrchestrationMetadata> WaitForInstanceStartAsync(string instanceId, bool getInputsAndOutputs = false, CancellationToken cancellation = default)
            => throw new NotImplementedException();
    }

    public class FakeHttpRequestData : HttpRequestData
    {
        public FakeHttpRequestData(FunctionContext functionContext, Uri url, Stream body) : base(functionContext)
        {
            Url = url;
            Body = body;
        }

        public override Stream Body { get; }
        public override HttpHeadersCollection Headers { get; } = new HttpHeadersCollection();
        public override IReadOnlyCollection<IHttpCookie> Cookies { get; } = Array.Empty<IHttpCookie>();
        public override Uri Url { get; }
        public override IEnumerable<ClaimsIdentity> Identities { get; } = Array.Empty<ClaimsIdentity>();
        public override string Method { get; } = "POST";

        public override HttpResponseData CreateResponse() => new FakeHttpResponseData(FunctionContext);
    }

    public class FakeHttpResponseData : HttpResponseData
    {
        public FakeHttpResponseData(FunctionContext functionContext) : base(functionContext)
        {
        }

        public override HttpStatusCode StatusCode { get; set; }
        public override HttpHeadersCollection Headers { get; set; } = new HttpHeadersCollection();
        public override Stream Body { get; set; } = new MemoryStream();
        public override HttpCookies Cookies => throw new NotImplementedException();
    }

    public class FakeFunctionContext : FunctionContext
    {
        public FakeFunctionContext()
        {
            var services = new ServiceCollection();
            services.AddSingleton<IOptions<WorkerOptions>>(
                Options.Create(new WorkerOptions { Serializer = new JsonObjectSerializer() }));
            InstanceServices = services.BuildServiceProvider();
        }

        public override string InvocationId => string.Empty;
        public override string FunctionId => string.Empty;
        public override TraceContext TraceContext => throw new NotImplementedException();
        public override BindingContext BindingContext => throw new NotImplementedException();
        public override RetryContext RetryContext => throw new NotImplementedException();
        public override IServiceProvider InstanceServices { get; set; }
        public override FunctionDefinition FunctionDefinition => throw new NotImplementedException();
        public override IDictionary<object, object> Items { get; set; } = new Dictionary<object, object>();
        public override IInvocationFeatures Features => throw new NotImplementedException();
    }
}
