// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using DurableTask.Core.Entities;
using DurableTask.Core.Entities.OperationFormat;
using Microsoft.DurableTask.Entities;
using Microsoft.DurableTask.Worker.Shims;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Hosts.AgentReader.Services.Tests
{
    [TestClass]
    public class AgentReaderStartEntityTests
    {
        [TestMethod]
        public async Task FirstStartCommitsTheMarkerAndTheOrchestrationAction()
        {
            var runId = Guid.NewGuid();

            var result = await ExecuteAsync(AgentReaderInstance.GetStartEntityId(runId));

            Assert.AreEqual("true", result.EntityState);
            Assert.IsNull(result.Results.Single().FailureDetails);
            var action = result.Actions.OfType<StartNewOrchestrationOperationAction>().Single();
            Assert.AreEqual(nameof(OrchestratorFunction), action.Name);
            Assert.AreEqual(AgentReaderInstance.GetId(runId), action.InstanceId);
            Assert.AreEqual(new AgentReaderRequest(runId), JsonSerializer.Deserialize<AgentReaderRequest>(action.Input));
        }

        [TestMethod]
        public async Task TwoStartsInOneBatchScheduleOnlyOnce()
        {
            var result = await ExecuteAsync(
                AgentReaderInstance.GetStartEntityId(Guid.NewGuid()), operationCount: 2);

            Assert.AreEqual(2, result.Results.Count);
            Assert.IsTrue(result.Results.All(operation => operation.FailureDetails == null));
            Assert.AreEqual(1, result.Actions.Count);
            Assert.AreEqual("true", result.EntityState);
        }

        [TestMethod]
        public async Task PersistedMarkerPreventsSchedulingInANewEntityExecution()
        {
            var id = AgentReaderInstance.GetStartEntityId(Guid.NewGuid());
            var first = await ExecuteAsync(id);

            var repeated = await ExecuteAsync(id, first.EntityState);

            Assert.AreEqual("true", repeated.EntityState);
            Assert.AreEqual(0, repeated.Actions.Count);
            Assert.IsNull(repeated.Results.Single().FailureDetails);
        }

        [TestMethod]
        public async Task DifferentRunIdsHaveIndependentMarkersAndInstances()
        {
            var firstRunId = Guid.NewGuid();
            var secondRunId = Guid.NewGuid();

            var first = await ExecuteAsync(AgentReaderInstance.GetStartEntityId(firstRunId));
            var second = await ExecuteAsync(AgentReaderInstance.GetStartEntityId(secondRunId));

            Assert.AreNotEqual(
                first.Actions.OfType<StartNewOrchestrationOperationAction>().Single().InstanceId,
                second.Actions.OfType<StartNewOrchestrationOperationAction>().Single().InstanceId);
        }

        [TestMethod]
        public async Task FailedOperationRollsBackBothTheMarkerAndTheQueuedStart()
        {
            var id = AgentReaderInstance.GetStartEntityId(Guid.NewGuid());

            var failed = await ExecuteAsync(id, logger: new ThrowAfterSchedulingLogger());

            Assert.IsNotNull(failed.Results.Single().FailureDetails);
            Assert.IsNull(failed.EntityState);
            Assert.AreEqual(0, failed.Actions.Count);

            var retried = await ExecuteAsync(id, failed.EntityState);

            Assert.IsNull(retried.Results.Single().FailureDetails);
            Assert.AreEqual("true", retried.EntityState);
            Assert.AreEqual(1, retried.Actions.Count);
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow("not-a-guid")]
        [DataRow("00000000-0000-0000-0000-000000000000")]
        [DataRow("0f8fad5bd9cb469fa16570867728950e")]
        [DataRow("0F8FAD5B-D9CB-469F-A165-70867728950E")]
        public async Task InvalidOrNonCanonicalEntityKeysCannotCreateAlternateGuards(string key)
        {
            var result = await ExecuteAsync(new EntityInstanceId(nameof(AgentReaderStartEntity), key));

            Assert.IsNotNull(result.Results.Single().FailureDetails);
            Assert.IsNull(result.EntityState);
            Assert.AreEqual(0, result.Actions.Count);
        }

        internal static Task<EntityBatchResult> ExecuteAsync(
            EntityInstanceId id,
            string? state = null,
            int operationCount = 1,
            ILogger<AgentReaderStartEntity>? logger = null)
        {
            var entity = new AgentReaderStartEntity(logger ?? NullLogger<AgentReaderStartEntity>.Instance);
            var shim = DurableTaskShimFactory.Default.CreateEntity(
                nameof(AgentReaderStartEntity), entity, new EntityId(id.Name, id.Key));
            return shim.ExecuteOperationBatchAsync(new EntityBatchRequest
            {
                InstanceId = id.ToString(),
                EntityState = state,
                Operations = Enumerable.Range(0, operationCount)
                    .Select(_ => new OperationRequest { Operation = nameof(AgentReaderStartEntity.Start) })
                    .ToList()
            });
        }

        private sealed class ThrowAfterSchedulingLogger : ILogger<AgentReaderStartEntity>
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
                NullLogger<AgentReaderStartEntity>.Instance.BeginScope(state);

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (eventId.Id == 260022)
                {
                    throw new InvalidOperationException("Injected failure after the start action was queued.");
                }
            }
        }
    }
}
