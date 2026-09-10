// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Repositories.Contracts;
using Models;
using Hosts.AgentReader.Services.Entities;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Hosts.AgentReader.Services.Tests
{
    [TestClass]
    public class OrchestratorTests
    {
        private const string NextLink = "https://graph.microsoft.com/v1.0/users/microsoft.graph.agentUser?$skiptoken=a%2Bb%3D";
        private readonly Guid _runId = Guid.Parse("11111111-2222-4333-8444-555555555555");

        [TestMethod]
        public async Task MultiplePages_ReconcileAllFiveReasonsAndOnlyCompleteAfterPersistedCountMatches()
        {
            var first = new AgentPage([NewAgent()], NextLink, new AgentReadCounters
            {
                TotalEnumerated = 3, FilterPassingAgents = 1,
                FilteredNoManager = 1, FilteredManagerEmployeeIdAbsent = 1,
                AccountEnabledDefaultApplied = 1
            });
            var second = new AgentPage([NewAgent()], null, new AgentReadCounters
            {
                TotalEnumerated = 4, FilterPassingAgents = 1,
                FilteredManagerEmployeeIdNonNumeric = 1, FilteredManagerEmployeeIdOutOfRange = 1,
                FilteredInvalidAgentObjectId = 1, BlueprintIdTruncated = 1
            });
            var fixture = new Fixture(_runId, 2, first, second);

            var result = await new OrchestratorFunction().RunAsync(fixture.Context.Object);

            Assert.AreEqual("Completed", result.Status);
            Assert.AreEqual(_runId, result.RunId);
            Assert.AreEqual(7L, result.Counters.TotalEnumerated);
            Assert.AreEqual(2L, result.Counters.Inserted);
            Assert.AreEqual(2L, result.Counters.FilterPassingAgents);
            Assert.AreEqual(5L, result.Counters.TotalFiltered);
            Assert.AreEqual(1L, result.Counters.AccountEnabledDefaultApplied);
            Assert.AreEqual(1L, result.Counters.BlueprintIdTruncated);
            CollectionAssert.AreEqual(
                new[] { "validate", "read", "write", "read", "write", "count" }, fixture.Calls);
            Assert.IsNull(fixture.Reads[0].NextLink);
            Assert.AreEqual(NextLink, fixture.Reads[1].NextLink);
            Assert.IsTrue(fixture.Reads.All(request => request.RunId == _runId));
            Assert.IsTrue(fixture.Writes.All(request => request.RunId == _runId));
            Assert.AreEqual("Completed", fixture.Logger.Summary["Status"]);
        }

        [TestMethod]
        public async Task EmptyTenant_CompletesEmptyOnlyAfterCheckingTheExistingTable()
        {
            var fixture = new Fixture(_runId, 0, new AgentPage([], null, new AgentReadCounters()));

            var result = await new OrchestratorFunction().RunAsync(fixture.Context.Object);

            Assert.AreEqual("CompletedEmpty", result.Status);
            Assert.AreEqual(0L, result.Counters.FilterPassingAgents);
            Assert.AreEqual(0L, result.Counters.Inserted);
            CollectionAssert.AreEqual(new[] { "validate", "read", "count" }, fixture.Calls);
            Assert.AreEqual("CompletedEmpty", fixture.Logger.Summary["Status"]);
        }

        [TestMethod]
        public async Task AllFilteredPages_AreExhaustedBeforeCompletedEmpty()
        {
            var fixture = new Fixture(_runId, 0,
                new AgentPage([], NextLink, new AgentReadCounters { TotalEnumerated = 1, FilteredNoManager = 1 }),
                new AgentPage([], null, new AgentReadCounters { TotalEnumerated = 1, FilteredManagerEmployeeIdAbsent = 1 }));

            var result = await new OrchestratorFunction().RunAsync(fixture.Context.Object);

            Assert.AreEqual("CompletedEmpty", result.Status);
            Assert.AreEqual(2L, result.Counters.TotalEnumerated);
            Assert.AreEqual(2L, result.Counters.TotalFiltered);
            Assert.AreEqual(0, fixture.Writes.Count);
            CollectionAssert.AreEqual(new[] { "validate", "read", "read", "count" }, fixture.Calls);
        }

        [DataTestMethod]
        [DataRow(0)]
        [DataRow(1)]
        public async Task MissingWrites_FailAndKeepEligibleCountObservable(int written)
        {
            var fixture = new Fixture(_runId, written, new AgentPage(
                [NewAgent(), NewAgent()], null, new AgentReadCounters { TotalEnumerated = 2, FilterPassingAgents = 2 }));
            fixture.Context.Setup(context => context.CallActivityAsync<int>(
                    nameof(AgentWriterFunction), It.IsAny<AgentWriteRequest>(), It.IsAny<TaskOptions>()))
                .ReturnsAsync(written);

            await Assert.ThrowsExceptionAsync<InvalidDataException>(() => new OrchestratorFunction().RunAsync(fixture.Context.Object));

            Assert.AreEqual("Failed", fixture.Logger.Summary["Status"]);
            Assert.AreEqual(2L, fixture.Logger.Summary["FilterPassingAgents"]);
            Assert.AreEqual((long)written, fixture.Logger.Summary["Inserted"]);
            Assert.IsTrue(fixture.Logger.FailureLogged);
        }

        [DataTestMethod]
        [DataRow(0L)]
        [DataRow(2L)]
        public async Task ActualTableCountMismatch_FailsEvenWhenWriterReportedSuccess(long persisted)
        {
            var fixture = new Fixture(_runId, persisted, EligiblePage(null));

            await Assert.ThrowsExceptionAsync<InvalidDataException>(() => new OrchestratorFunction().RunAsync(fixture.Context.Object));

            Assert.AreEqual("Failed", fixture.Logger.Summary["Status"]);
            Assert.AreEqual("count", fixture.Calls.Last());
        }

        [TestMethod]
        public async Task MissingTable_FailsBeforeGraphEnumeration()
        {
            var fixture = new Fixture(_runId, 0);
            fixture.Context.Setup(context => context.CallActivityAsync(
                    nameof(AgentTableFunctions.ValidateAgentTable), It.IsAny<AgentReaderRequest>(), It.IsAny<TaskOptions>()))
                .ThrowsAsync(new InvalidOperationException("Synthetic missing table."));

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => new OrchestratorFunction().RunAsync(fixture.Context.Object));

            Assert.AreEqual(0, fixture.Reads.Count);
            Assert.IsTrue(fixture.Logger.FailureLogged);
        }

        [TestMethod]
        public async Task FailedContinuation_IsNotConvertedToAPartialSuccess()
        {
            var fixture = new Fixture(_runId, 1);
            fixture.Context.SetupSequence(context => context.CallActivityAsync<AgentPage>(
                    nameof(AgentReaderFunction), It.IsAny<AgentPageRequest>(), It.IsAny<TaskOptions>()))
                .ReturnsAsync(EligiblePage(NextLink))
                .ThrowsAsync(new IOException("Synthetic second-page failure."));

            await Assert.ThrowsExceptionAsync<IOException>(() => new OrchestratorFunction().RunAsync(fixture.Context.Object));

            Assert.AreEqual("Failed", fixture.Logger.Summary["Status"]);
            Assert.AreEqual(1L, fixture.Logger.Summary["Inserted"]);
            Assert.AreEqual(1L, fixture.Logger.Summary["FilterPassingAgents"]);
        }

        [TestMethod]
        public async Task RepeatedContinuation_FailsRatherThanLoopingOrCompleting()
        {
            var fixture = new Fixture(_runId, 2, EligiblePage(NextLink), EligiblePage(NextLink));

            await Assert.ThrowsExceptionAsync<InvalidDataException>(() => new OrchestratorFunction().RunAsync(fixture.Context.Object));

            Assert.AreEqual(2, fixture.Reads.Count);
            Assert.AreEqual("Failed", fixture.Logger.Summary["Status"]);
        }

        [TestMethod]
        public async Task WriteException_PropagatesWithFilterPassingCount()
        {
            var fixture = new Fixture(_runId, 0, EligiblePage(null));
            fixture.Context.Setup(context => context.CallActivityAsync<int>(
                    nameof(AgentWriterFunction), It.IsAny<AgentWriteRequest>(), It.IsAny<TaskOptions>()))
                .ThrowsAsync(new IOException("Synthetic insert failure."));

            await Assert.ThrowsExceptionAsync<IOException>(() => new OrchestratorFunction().RunAsync(fixture.Context.Object));

            Assert.AreEqual("Failed", fixture.Logger.Summary["Status"]);
            Assert.AreEqual(1L, fixture.Logger.Summary["FilterPassingAgents"]);
        }

        [TestMethod]
        public async Task InconsistentPageCounters_FailBeforeWriting()
        {
            var fixture = new Fixture(_runId, 1,
                new AgentPage([NewAgent()], null, new AgentReadCounters { TotalEnumerated = 2, FilterPassingAgents = 1 }));

            await Assert.ThrowsExceptionAsync<InvalidDataException>(() => new OrchestratorFunction().RunAsync(fixture.Context.Object));

            Assert.AreEqual(0, fixture.Writes.Count);
        }

        [TestMethod]
        public async Task EmptyRunId_IsRejectedBeforeActivities()
        {
            var fixture = new Fixture(Guid.Empty, 0);

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => new OrchestratorFunction().RunAsync(fixture.Context.Object));

            Assert.AreEqual(0, fixture.Calls.Count);
        }

        private static AgentRecord NewAgent() => new(Guid.NewGuid(), 1, null, true);
        private static AgentPage EligiblePage(string? nextLink) =>
            new([NewAgent()], nextLink, new AgentReadCounters { TotalEnumerated = 1, FilterPassingAgents = 1 });

        private sealed class Fixture
        {
            public Mock<TaskOrchestrationContext> Context { get; } = new(MockBehavior.Strict);
            public CapturingLogger Logger { get; } = new();
            public List<string> Calls { get; } = [];
            public List<AgentPageRequest> Reads { get; } = [];
            public List<AgentWriteRequest> Writes { get; } = [];

            public Fixture(Guid runId, long persisted, params AgentPage[] pages)
            {
                var queue = new Queue<AgentPage>(pages);
                Context.Setup(context => context.GetInput<AgentReaderRequest>()).Returns(new AgentReaderRequest(runId));
                Context.Setup(context => context.CreateReplaySafeLogger("Hosts.AgentReader.OrchestratorFunction")).Returns(Logger);
                Context.Setup(context => context.CallActivityAsync(
                        nameof(AgentTableFunctions.ValidateAgentTable), It.IsAny<AgentReaderRequest>(), It.IsAny<TaskOptions>()))
                    .Callback(() => Calls.Add("validate")).Returns(Task.CompletedTask);
                Context.Setup(context => context.CallActivityAsync<AgentPage>(
                        nameof(AgentReaderFunction), It.IsAny<AgentPageRequest>(), It.IsAny<TaskOptions>()))
                    .Returns((TaskName name, object input, TaskOptions? options) =>
                    {
                        Calls.Add("read");
                        Reads.Add((AgentPageRequest)input);
                        return Task.FromResult(queue.Dequeue());
                    });
                Context.Setup(context => context.CallActivityAsync<int>(
                        nameof(AgentWriterFunction), It.IsAny<AgentWriteRequest>(), It.IsAny<TaskOptions>()))
                    .Returns((TaskName name, object input, TaskOptions? options) =>
                    {
                        Calls.Add("write");
                        var request = (AgentWriteRequest)input;
                        Writes.Add(request);
                        return Task.FromResult(request.Agents.Count);
                    });
                Context.Setup(context => context.CallActivityAsync<long>(
                        nameof(AgentTableFunctions.GetAgentRowCount), It.IsAny<AgentReaderRequest>(), It.IsAny<TaskOptions>()))
                    .Callback(() => Calls.Add("count")).ReturnsAsync(persisted);
            }
        }

        private sealed class CapturingLogger : ILogger
        {
            public Dictionary<string, object?> Summary { get; private set; } = [];
            public bool FailureLogged { get; private set; }
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (eventId.Id == 260011 && state is IEnumerable<KeyValuePair<string, object?>> properties)
                {
                    Summary = properties.ToDictionary(pair => pair.Key, pair => pair.Value);
                }
                if (eventId.Id == 260012)
                {
                    FailureLogged = true;
                }
            }
        }
    }
}
