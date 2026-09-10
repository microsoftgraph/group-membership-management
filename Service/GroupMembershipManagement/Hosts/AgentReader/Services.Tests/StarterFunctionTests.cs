// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Core.Serialization;
using DurableTask.Core.Entities.OperationFormat;
using Hosts.AgentReader;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RetryContext = Microsoft.Azure.Functions.Worker.RetryContext;

namespace Hosts.AgentReader.Services.Tests
{
    [TestClass]
    public class StarterFunctionTests
    {
        private static FakeHttpRequestData Request(string body)
        {
            return new FakeHttpRequestData(
                new FakeFunctionContext(),
                new Uri("http://localhost/api/StarterFunction"),
                new MemoryStream(Encoding.UTF8.GetBytes(body)));
        }

        private static async Task<(HttpResponseData Response, FakeDurableTaskClient Client)> RunAsync(
            string body, OrchestrationMetadata? existing = null)
        {
            var client = new FakeDurableTaskClient("test") { Existing = existing };
            var starter = new StarterFunction(NullLogger<StarterFunction>.Instance, TimeProvider.System);
            var response = await starter.RunAsync(Request(body), client);
            return (response, client);
        }

        [TestMethod]
        public async Task ValidRunIdSignalsTheStartEntityRatherThanSchedulingDirectly()
        {
            var runId = Guid.NewGuid();

            var (response, client) = await RunAsync($"{{\"RunId\":\"{runId}\"}}");

            Assert.AreEqual(1, client.ScheduledInstanceIds.Count);
            Assert.AreEqual(nameof(OrchestratorFunction), client.ScheduledNames[0]);
            Assert.AreEqual(new AgentReaderRequest(runId), client.ScheduledInputs[0]);
            Assert.AreEqual(0, client.DirectScheduleCount);
            Assert.AreEqual(AgentReaderInstance.GetStartEntityId(runId), client.Signals.Single().Id);
            Assert.AreEqual(nameof(AgentReaderStartEntity.Start), client.Signals.Single().Operation);
            Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);
        }

        [TestMethod]
        public async Task ResponseCarriesTheStatusQueryUriThePipelinePolls()
        {
            var (response, _) = await RunAsync($"{{\"RunId\":\"{Guid.NewGuid()}\"}}");

            response.Body.Position = 0;
            var body = await new StreamReader(response.Body).ReadToEndAsync();

            // Data Factory's Until/SetVariable stage reads this field to poll for runtimeStatus.
            // The worker emits it in PascalCase; Data Factory resolves activity output properties
            // case-insensitively, so the pipelines can read it as statusQueryGetUri.
            StringAssert.Contains(body, "StatusQueryGetUri");
        }

        [TestMethod]
        public async Task InstanceIdIsKeyedOnTheRunIdAlone()
        {
            var runId = Guid.NewGuid();

            var first = await RunAsync($"{{\"RunId\":\"{runId}\"}}");
            var second = await RunAsync($"{{\"RunId\":\"{runId}\"}}");

            // A per-invocation or per-attempt term would make these differ, and the duplicate guard
            // would never collide.
            Assert.AreEqual(first.Client.ScheduledInstanceIds[0], second.Client.ScheduledInstanceIds[0]);
            StringAssert.Contains(first.Client.ScheduledInstanceIds[0], runId.ToString());
            Assert.AreEqual(first.Client.Signals.Single().Id, second.Client.Signals.Single().Id);
        }

        [DataTestMethod]
        [DataRow("Pending")]
        [DataRow("Running")]
        [DataRow("Completed")]
        [DataRow("Failed")]
        [DataRow("Terminated")]
        [DataRow("Suspended")]
        public async Task ExistingInstanceIsNeverRestarted(string runtimeStatus)
        {
            var runId = Guid.NewGuid();
            var existing = new OrchestrationMetadata(nameof(OrchestratorFunction), "AgentReader-" + runId)
            {
                RuntimeStatus = Enum.Parse<OrchestrationRuntimeStatus>(runtimeStatus)
            };

            var (response, client) = await RunAsync($"{{\"RunId\":\"{runId}\"}}", existing);

            // Durable restarts a terminal instance when it is scheduled again. Re-running the load
            // against an already-populated table would duplicate rows (FR-012).
            Assert.AreEqual(0, client.ScheduledInstanceIds.Count);
            Assert.AreEqual(0, client.Signals.Count);
            Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);
        }

        [TestMethod]
        public async Task ResubmittingTheSameRunStartsOneLoadOnly()
        {
            var runId = Guid.NewGuid();
            var body = $"{{\"RunId\":\"{runId}\"}}";
            var client = new FakeDurableTaskClient("test");
            var starter = new StarterFunction(NullLogger<StarterFunction>.Instance, TimeProvider.System);

            await starter.RunAsync(Request(body), client);

            // The first start is what a re-delivery of the same pipeline run would then observe.
            client.Existing = new OrchestrationMetadata(nameof(OrchestratorFunction), client.ScheduledInstanceIds[0])
            {
                RuntimeStatus = OrchestrationRuntimeStatus.Running
            };

            await starter.RunAsync(Request(body), client);

            Assert.AreEqual(1, client.ScheduledInstanceIds.Count);
            Assert.AreEqual(1, client.Signals.Count);
        }

        [TestMethod]
        public async Task ConcurrentStaleLookupsCannotRestartACompletedInstance()
        {
            var runId = Guid.NewGuid();
            var body = $"{{\"RunId\":\"{runId}\"}}";
            var bothLookups = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var probeCount = 0;
            var client = new FakeDurableTaskClient("test")
            {
                ScheduledStatus = OrchestrationRuntimeStatus.Completed
            };
            client.OnGetInstance = async (id, cancellation) =>
            {
                if (Interlocked.Increment(ref probeCount) <= 2)
                {
                    if (Volatile.Read(ref probeCount) == 2)
                    {
                        bothLookups.TrySetResult();
                    }
                    await bothLookups.Task.WaitAsync(cancellation);
                    return null;
                }
                return client.GetRecordedInstance(id);
            };
            var starter = new StarterFunction(NullLogger<StarterFunction>.Instance, TimeProvider.System);

            var responses = await Task.WhenAll(
                starter.RunAsync(Request(body), client),
                starter.RunAsync(Request(body), client)).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.AreEqual(2, client.Signals.Count, "Both requests must have observed the stale missing instance.");
            Assert.AreEqual(1, client.ScheduledInstanceIds.Count);
            Assert.AreEqual(0, client.DirectScheduleCount);
            Assert.IsTrue(responses.All(response => response.StatusCode == HttpStatusCode.Accepted));
            Assert.AreEqual(
                await GetStatusUriAsync(responses[0]),
                await GetStatusUriAsync(responses[1]));
        }

        [TestMethod]
        public async Task EquivalentRunIdFormatsUseTheSameGuard()
        {
            var runId = Guid.NewGuid();
            var first = await RunAsync($"{{\"RunId\":\"{runId:N}\"}}");
            var second = await RunAsync($"{{\"RunId\":\"{runId.ToString("D").ToUpperInvariant()}\"}}");

            Assert.AreEqual(first.Client.Signals.Single().Id, second.Client.Signals.Single().Id);
            Assert.AreEqual(first.Client.ScheduledInstanceIds.Single(), second.Client.ScheduledInstanceIds.Single());
        }

        [TestMethod]
        public async Task StarterWaitsUntilThePollingInstanceExistsIncludingPending()
        {
            var runId = Guid.NewGuid();
            var clock = new ControlledTimeProvider();
            var client = new FakeDurableTaskClient("test") { OnSignal = (_, _, _) => Task.CompletedTask };
            var starter = new StarterFunction(NullLogger<StarterFunction>.Instance, clock);

            var pendingResponse = starter.RunAsync(Request($"{{\"RunId\":\"{runId}\"}}"), client);
            await clock.PollCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.IsFalse(pendingResponse.IsCompleted, "Enqueueing an entity signal does not confirm the polling instance.");
            client.Existing = new OrchestrationMetadata(nameof(OrchestratorFunction), AgentReaderInstance.GetId(runId))
            {
                RuntimeStatus = OrchestrationRuntimeStatus.Pending
            };
            clock.FirePoll();
            var response = await pendingResponse.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);
            StringAssert.Contains(await GetStatusUriAsync(response), AgentReaderInstance.GetId(runId));
        }

        [TestMethod]
        public async Task UnconfirmedStartFailsRatherThanReturningAnUnusablePollingUri()
        {
            var clock = new ControlledTimeProvider();
            var client = new FakeDurableTaskClient("test") { OnSignal = (_, _, _) => Task.CompletedTask };
            var starter = new StarterFunction(NullLogger<StarterFunction>.Instance, clock);

            var pendingResponse = starter.RunAsync(Request($"{{\"RunId\":\"{Guid.NewGuid()}\"}}"), client);
            await clock.PollCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
            clock.FireDeadline();
            var response = await pendingResponse.WaitAsync(TimeSpan.FromSeconds(5));
            var body = await ReadBodyAsync(response);

            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            StringAssert.Contains(body, "AgentReaderStartNotConfirmed");
            Assert.IsFalse(body.Contains("StatusQueryGetUri", StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual(0, client.DirectScheduleCount);
        }

        [TestMethod]
        public async Task RetainedMarkerDoesNotRestartAnInstanceWhoseHistoryWasPurged()
        {
            var runId = Guid.NewGuid();
            var client = new FakeDurableTaskClient("test");
            await new StarterFunction(NullLogger<StarterFunction>.Instance, TimeProvider.System)
                .RunAsync(Request($"{{\"RunId\":\"{runId}\"}}"), client);
            client.RemoveRecordedInstance(AgentReaderInstance.GetId(runId));
            var clock = new ControlledTimeProvider();

            var pendingResponse = new StarterFunction(NullLogger<StarterFunction>.Instance, clock)
                .RunAsync(Request($"{{\"RunId\":\"{runId}\"}}"), client);
            await clock.PollCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
            clock.FireDeadline();
            var response = await pendingResponse.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.AreEqual(1, client.ScheduledInstanceIds.Count);
            Assert.AreEqual(2, client.Signals.Count);
        }

        [TestMethod]
        public async Task CallerCancellationIsNotReportedAsAStartTimeout()
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var client = new FakeDurableTaskClient("test");
            var starter = new StarterFunction(NullLogger<StarterFunction>.Instance, TimeProvider.System);

            await Assert.ThrowsExceptionAsync<TaskCanceledException>(() =>
                starter.RunAsync(Request($"{{\"RunId\":\"{Guid.NewGuid()}\"}}"), client, cancellation.Token));

            Assert.AreEqual(0, client.Signals.Count);
        }

        [TestMethod]
        public async Task EntityEnqueueFailuresPropagateWithoutADirectStartFallback()
        {
            var client = new FakeDurableTaskClient("test")
            {
                OnSignal = (_, _, _) => throw new IOException("Injected entity enqueue failure.")
            };
            var starter = new StarterFunction(NullLogger<StarterFunction>.Instance, TimeProvider.System);

            await Assert.ThrowsExceptionAsync<IOException>(() =>
                starter.RunAsync(Request($"{{\"RunId\":\"{Guid.NewGuid()}\"}}"), client));

            Assert.AreEqual(0, client.ScheduledInstanceIds.Count);
            Assert.AreEqual(0, client.DirectScheduleCount);
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow("   ")]
        [DataRow("not json")]
        [DataRow("[{\"RunId\":\"0f8fad5b-d9cb-469f-a165-70867728950e\"}]")]
        [DataRow("{}")]
        [DataRow("{\"RunId\":null}")]
        [DataRow("{\"RunId\":\"\"}")]
        [DataRow("{\"RunId\":\"not-a-guid\"}")]
        [DataRow("{\"RunId\":\"00000000-0000-0000-0000-000000000000\"}")]
        [DataRow("{\"RunId\":12345}")]
        public async Task UnusableRunIdIsRejectedRatherThanDefaulted(string body)
        {
            var (response, client) = await RunAsync(body);

            // A defaulted identifier would populate the wrong run's table.
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.AreEqual(0, client.ScheduledInstanceIds.Count);
            Assert.AreEqual(0, client.Signals.Count);
        }

        private static async Task<string> ReadBodyAsync(HttpResponseData response)
        {
            response.Body.Position = 0;
            return await new StreamReader(response.Body, leaveOpen: true).ReadToEndAsync();
        }

        private static async Task<string> GetStatusUriAsync(HttpResponseData response)
        {
            using var json = JsonDocument.Parse(await ReadBodyAsync(response));
            return json.RootElement.GetProperty("StatusQueryGetUri").GetString()!;
        }

        private sealed class ControlledTimeProvider : TimeProvider
        {
            private readonly List<ControlledTimer> _timers = new();
            public TaskCompletionSource PollCreated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            {
                var timer = new ControlledTimer(callback, state, dueTime);
                lock (_timers)
                {
                    _timers.Add(timer);
                }
                if (dueTime < TimeSpan.FromSeconds(1))
                {
                    PollCreated.TrySetResult();
                }
                return timer;
            }

            public void FirePoll() => Fire(timer => timer.DueTime < TimeSpan.FromSeconds(1));
            public void FireDeadline() => Fire(timer => timer.DueTime >= TimeSpan.FromSeconds(1));

            private void Fire(Func<ControlledTimer, bool> predicate)
            {
                ControlledTimer timer;
                lock (_timers)
                {
                    timer = _timers.First(candidate => !candidate.Disposed && predicate(candidate));
                }
                timer.Fire();
            }

            private sealed class ControlledTimer(TimerCallback callback, object? state, TimeSpan dueTime) : ITimer
            {
                public TimeSpan DueTime { get; private set; } = dueTime;
                public bool Disposed { get; private set; }
                public void Fire() => callback(state);
                public void Dispose() => Disposed = true;
                public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
                public bool Change(TimeSpan nextDueTime, TimeSpan period)
                {
                    DueTime = nextDueTime;
                    return !Disposed;
                }
            }
        }
    }

    public class FakeDurableTaskClient : DurableTaskClient
    {
        private readonly Mock<DurableEntityClient> _entities;
        private readonly SemaphoreSlim _entityOperations = new(1, 1);
        private readonly Dictionary<EntityInstanceId, string?> _entityStates = new();
        private readonly ConcurrentDictionary<string, OrchestrationMetadata> _instances = new();

        public FakeDurableTaskClient(string name) : base(name)
        {
            _entities = new Mock<DurableEntityClient>(name) { CallBase = true };
            _entities.Setup(client => client.SignalEntityAsync(
                It.IsAny<EntityInstanceId>(), It.IsAny<string>(), It.IsAny<object>(),
                It.IsAny<SignalEntityOptions>(), It.IsAny<CancellationToken>()))
                .Returns<EntityInstanceId, string, object?, SignalEntityOptions?, CancellationToken>(
                    async (id, operation, _, _, cancellation) =>
                    {
                        cancellation.ThrowIfCancellationRequested();
                        Signals.Enqueue((id, operation));
                        if (OnSignal != null)
                        {
                            await OnSignal(id, operation, cancellation);
                        }
                        else
                        {
                            await ProcessEntityStartAsync(id, cancellation);
                        }
                    });
        }

        public OrchestrationMetadata? Existing { get; set; }
        public OrchestrationRuntimeStatus ScheduledStatus { get; set; } = OrchestrationRuntimeStatus.Pending;
        public Func<string, CancellationToken, Task<OrchestrationMetadata?>>? OnGetInstance { get; set; }
        public Func<EntityInstanceId, string, CancellationToken, Task>? OnSignal { get; set; }
        public ConcurrentQueue<(EntityInstanceId Id, string Operation)> Signals { get; } = new();
        public override DurableEntityClient Entities => _entities.Object;
        public List<string> ScheduledInstanceIds { get; } = new();
        public List<string> ScheduledNames { get; } = new();
        public List<object> ScheduledInputs { get; } = new();
        public int DirectScheduleCount { get; private set; }

        public override Task<OrchestrationMetadata?> GetInstanceAsync(
            string instanceId, bool getInputsAndOutputs = false, CancellationToken cancellation = default)
        {
            cancellation.ThrowIfCancellationRequested();
            return OnGetInstance?.Invoke(instanceId, cancellation)
                ?? Task.FromResult(GetRecordedInstance(instanceId));
        }

        public OrchestrationMetadata? GetRecordedInstance(string id) =>
            Existing ?? (_instances.TryGetValue(id, out var instance) ? instance : null);

        public void RemoveRecordedInstance(string id) => _instances.TryRemove(id, out _);

        private async Task ProcessEntityStartAsync(EntityInstanceId id, CancellationToken cancellation)
        {
            await _entityOperations.WaitAsync(cancellation);
            try
            {
                _entityStates.TryGetValue(id, out var state);
                var result = await AgentReaderStartEntityTests.ExecuteAsync(id, state);
                Assert.IsTrue(result.Results.All(operation => operation.FailureDetails == null));
                _entityStates[id] = result.EntityState;
                foreach (var action in result.Actions.OfType<StartNewOrchestrationOperationAction>())
                {
                    ScheduledNames.Add(action.Name);
                    ScheduledInputs.Add(JsonSerializer.Deserialize<AgentReaderRequest>(action.Input)!);
                    ScheduledInstanceIds.Add(action.InstanceId);
                    _instances[action.InstanceId] = new OrchestrationMetadata(action.Name, action.InstanceId)
                    {
                        RuntimeStatus = ScheduledStatus
                    };
                }
            }
            finally
            {
                _entityOperations.Release();
            }
        }

        public override Task<string> ScheduleNewOrchestrationInstanceAsync(
            TaskName orchestratorName, object input = null, StartOrchestrationOptions options = null,
            CancellationToken cancellation = default)
        {
            DirectScheduleCount++;
            ScheduledNames.Add(orchestratorName.Name);
            ScheduledInputs.Add(input);
            ScheduledInstanceIds.Add(options?.InstanceId);
            return Task.FromResult(options?.InstanceId);
        }

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public override AsyncPageable<OrchestrationMetadata> GetAllInstancesAsync(OrchestrationQuery filter = null) => throw new NotImplementedException();
        public override Task<OrchestrationMetadata> GetInstancesAsync(string instanceId, bool getInputsAndOutputs = false, CancellationToken cancellation = default) => throw new NotImplementedException();
        public override Task RaiseEventAsync(string instanceId, string eventName, object eventPayload = null, CancellationToken cancellation = default) => throw new NotImplementedException();
        public override Task ResumeInstanceAsync(string instanceId, string reason = null, CancellationToken cancellation = default) => throw new NotImplementedException();
        public override Task SuspendInstanceAsync(string instanceId, string reason = null, CancellationToken cancellation = default) => throw new NotImplementedException();
        public override Task<OrchestrationMetadata> WaitForInstanceCompletionAsync(string instanceId, bool getInputsAndOutputs = false, CancellationToken cancellation = default) => throw new NotImplementedException();
        public override Task<OrchestrationMetadata> WaitForInstanceStartAsync(string instanceId, bool getInputsAndOutputs = false, CancellationToken cancellation = default) => throw new NotImplementedException();
    }

    public class FakeHttpRequestData : HttpRequestData
    {
        private readonly Stream _body;

        public FakeHttpRequestData(FunctionContext functionContext, Uri url, Stream body = null) : base(functionContext)
        {
            Url = url;
            _body = body ?? new MemoryStream();
        }

        public override Stream Body => _body;
        public override HttpHeadersCollection Headers { get; } = new HttpHeadersCollection();
        public override IReadOnlyCollection<IHttpCookie> Cookies { get; }
        public override Uri Url { get; }
        public override IEnumerable<ClaimsIdentity> Identities { get; }
        public override string Method => "POST";
        public override HttpResponseData CreateResponse() => new FakeHttpResponseData(FunctionContext);
    }

    public class FakeHttpResponseData : HttpResponseData
    {
        public FakeHttpResponseData(FunctionContext functionContext) : base(functionContext) { }

        public override HttpStatusCode StatusCode { get; set; }
        public override HttpHeadersCollection Headers { get; set; } = new HttpHeadersCollection();
        public override Stream Body { get; set; } = new MemoryStream();
        public override HttpCookies Cookies { get; }
    }

    public class FakeFunctionContext : FunctionContext
    {
        public FakeFunctionContext()
        {
            InstanceServices = CreateDefaultServiceProvider();
        }

        public override string InvocationId { get; }
        public override string FunctionId { get; }
        public override TraceContext TraceContext { get; }
        public override BindingContext BindingContext { get; }
        public override RetryContext RetryContext { get; }
        public override IServiceProvider InstanceServices { get; set; }
        public override FunctionDefinition FunctionDefinition { get; }
        public override IDictionary<object, object> Items { get; set; } = new Dictionary<object, object>();
        public override IInvocationFeatures Features { get; }

        private static IServiceProvider CreateDefaultServiceProvider()
        {
            var services = new ServiceCollection();
            // Matches the deployed worker, which emits the check-status payload in PascalCase; this
            // was read back off the AgentReader activity output of a real ADF run. Injecting Web
            // defaults here rendered camelCase and hid that mismatch. The pipelines still read
            // `statusQueryGetUri` (azureDataFactoryPipelines.bicep:2383, :2886) because Data Factory
            // resolves activity output properties case-insensitively.
            var options = new System.Text.Json.JsonSerializerOptions();
            services.AddSingleton(options);
            services.AddSingleton<IOptions<WorkerOptions>>(
                Options.Create(new WorkerOptions { Serializer = new JsonObjectSerializer(options) }));
            return services.BuildServiceProvider();
        }
    }
}
