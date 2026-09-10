// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Repositories.Contracts;
using Models;
using Hosts.AgentReader.Services.Contracts;
using Hosts.AgentReader.Services.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Hosts.AgentReader.Services.Tests
{
    [TestClass]
    public class AgentReaderServiceTests
    {
        private const string AgentId = "00000000-0000-0000-0000-000000000001";
        private readonly Guid _runId = Guid.Parse("11111111-2222-4333-8444-555555555555");

        [DataTestMethod]
        [DataRow(false, null, "bad-id", nameof(AgentReadCounters.FilteredNoManager))]
        [DataRow(true, null, "bad-id", nameof(AgentReadCounters.FilteredManagerEmployeeIdAbsent))]
        [DataRow(true, "", "bad-id", nameof(AgentReadCounters.FilteredManagerEmployeeIdAbsent))]
        [DataRow(true, " \t ", "bad-id", nameof(AgentReadCounters.FilteredManagerEmployeeIdAbsent))]
        [DataRow(true, "abc", "bad-id", nameof(AgentReadCounters.FilteredManagerEmployeeIdNonNumeric))]
        [DataRow(true, "1.5", "bad-id", nameof(AgentReadCounters.FilteredManagerEmployeeIdNonNumeric))]
        [DataRow(true, "1e3", "bad-id", nameof(AgentReadCounters.FilteredManagerEmployeeIdNonNumeric))]
        [DataRow(true, "+", "bad-id", nameof(AgentReadCounters.FilteredManagerEmployeeIdNonNumeric))]
        [DataRow(true, "1,000", "bad-id", nameof(AgentReadCounters.FilteredManagerEmployeeIdNonNumeric))]
        [DataRow(true, "\u0661\u0662", "bad-id", nameof(AgentReadCounters.FilteredManagerEmployeeIdNonNumeric))]
        [DataRow(true, "2147483648", "bad-id", nameof(AgentReadCounters.FilteredManagerEmployeeIdOutOfRange))]
        [DataRow(true, "-2147483649", "bad-id", nameof(AgentReadCounters.FilteredManagerEmployeeIdOutOfRange))]
        [DataRow(true, "99999999999999999999999999999", "bad-id", nameof(AgentReadCounters.FilteredManagerEmployeeIdOutOfRange))]
        [DataRow(true, "123", "bad-id", nameof(AgentReadCounters.FilteredInvalidAgentObjectId))]
        [DataRow(true, "123", null, nameof(AgentReadCounters.FilteredInvalidAgentObjectId))]
        [DataRow(true, "123", "00000000-0000-0000-0000-000000000000", nameof(AgentReadCounters.FilteredInvalidAgentObjectId))]
        public async Task Filtering_UsesExactlyOneReasonInPriorityOrder(
            bool hasManager, string? managerIdentifier, string? agentId, string expectedReason)
        {
            var service = CreateService(new AgentSourceRecord(agentId, hasManager, managerIdentifier, null, null));

            var page = await service.ReadPageAsync(_runId, null, CancellationToken.None);

            Assert.AreEqual(0, page.Agents.Count);
            Assert.AreEqual(1L, page.Counters.TotalEnumerated);
            Assert.AreEqual(0L, page.Counters.FilterPassingAgents);
            Assert.AreEqual(1L, page.Counters.TotalFiltered);
            var reasonCount = expectedReason switch
            {
                nameof(AgentReadCounters.FilteredNoManager) => page.Counters.FilteredNoManager,
                nameof(AgentReadCounters.FilteredManagerEmployeeIdAbsent) => page.Counters.FilteredManagerEmployeeIdAbsent,
                nameof(AgentReadCounters.FilteredManagerEmployeeIdNonNumeric) => page.Counters.FilteredManagerEmployeeIdNonNumeric,
                nameof(AgentReadCounters.FilteredManagerEmployeeIdOutOfRange) => page.Counters.FilteredManagerEmployeeIdOutOfRange,
                nameof(AgentReadCounters.FilteredInvalidAgentObjectId) => page.Counters.FilteredInvalidAgentObjectId,
                _ => throw new AssertFailedException("Unrecognised expected reason.")
            };
            Assert.AreEqual(1L, reasonCount);
            Assert.AreEqual(0L, page.Counters.AccountEnabledDefaultApplied);
        }

        [DataTestMethod]
        [DataRow("-2147483648", int.MinValue)]
        [DataRow("2147483647", int.MaxValue)]
        [DataRow("0", 0)]
        [DataRow("-1", -1)]
        [DataRow("+123", 123)]
        [DataRow("00000000000000000000000123", 123)]
        [DataRow(" \t123\r\n", 123)]
        [DataRow("\u00a0123\u00a0", 123)]
        public async Task ManagerIdentifier_ParsesTheCompleteSignedInt32Range(string identifier, int expected)
        {
            var service = CreateService(new AgentSourceRecord(AgentId, true, identifier, null, true));

            var page = await service.ReadPageAsync(_runId, null, CancellationToken.None);

            Assert.AreEqual(1, page.Agents.Count);
            Assert.AreEqual(expected, page.Agents[0].ManagerId);
            Assert.AreEqual(0L, page.Counters.TotalFiltered);
            Assert.AreEqual(1L, page.Counters.FilterPassingAgents);
            Assert.AreEqual(0L, page.Counters.Inserted);
        }

        [TestMethod]
        public async Task AccountEnabled_PreservesDisabledAgentsAndDefaultsOnlyEligibleNulls()
        {
            var service = CreateService(
                new AgentSourceRecord(AgentId, true, "1", null, false),
                new AgentSourceRecord(Guid.NewGuid().ToString(), true, "2", null, true),
                new AgentSourceRecord(Guid.NewGuid().ToString(), true, "3", null, null),
                new AgentSourceRecord(Guid.NewGuid().ToString(), false, null, null, null));

            var page = await service.ReadPageAsync(_runId, null, CancellationToken.None);

            Assert.AreEqual(3, page.Agents.Count);
            Assert.IsFalse(page.Agents[0].AccountEnabled);
            Assert.IsTrue(page.Agents[1].AccountEnabled);
            Assert.IsTrue(page.Agents[2].AccountEnabled);
            Assert.AreEqual(1L, page.Counters.AccountEnabledDefaultApplied);
            Assert.AreEqual(1L, page.Counters.FilteredNoManager);
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow(" {Unnormalised Blueprint} ")]
        public async Task Blueprint_IsPreservedWithoutValidationOrNormalisation(string? blueprint)
        {
            var service = CreateService(new AgentSourceRecord(AgentId, true, "1", blueprint, true));

            var page = await service.ReadPageAsync(_runId, null, CancellationToken.None);

            Assert.AreEqual(blueprint, page.Agents[0].BlueprintId);
            Assert.AreEqual(0L, page.Counters.BlueprintIdTruncated);
        }

        [DataTestMethod]
        [DataRow(200, 0L)]
        [DataRow(201, 1L)]
        public async Task Blueprint_TruncatesOnlyAboveColumnWidth(int length, long expectedTruncations)
        {
            var service = CreateService(new AgentSourceRecord(AgentId, true, "1", new string('x', length), true));

            var page = await service.ReadPageAsync(_runId, null, CancellationToken.None);

            Assert.AreEqual(new string('x', 200), page.Agents[0].BlueprintId);
            Assert.AreEqual(expectedTruncations, page.Counters.BlueprintIdTruncated);
        }

        [TestMethod]
        public async Task ContinuationAndCancellation_ArePassedThroughUnchanged()
        {
            const string nextLink = "https://graph.microsoft.com/v1.0/users/microsoft.graph.agentUser?$skiptoken=a%2Bb%2Fc%3D";
            using var cancellation = new CancellationTokenSource();
            var graph = new Mock<IAgentGraphRepository>(MockBehavior.Strict);
            graph.Setup(repository => repository.ReadPageAsync(_runId, nextLink, cancellation.Token))
                .ReturnsAsync(new AgentSourcePage([], nextLink));
            var service = new AgentReaderService(
                graph.Object, Mock.Of<IAgentTableRepository>(), NullLogger<AgentReaderService>.Instance);

            var page = await service.ReadPageAsync(_runId, nextLink, cancellation.Token);

            Assert.AreEqual(nextLink, page.NextLink);
            Assert.AreEqual(0L, page.Counters.TotalEnumerated);
            graph.VerifyAll();
        }

        [TestMethod]
        public async Task GraphFailure_IsNotConvertedToAnEmptySuccess()
        {
            var graph = new Mock<IAgentGraphRepository>();
            graph.Setup(repository => repository.ReadPageAsync(_runId, null, CancellationToken.None))
                .ThrowsAsync(new IOException("Synthetic page failure."));
            var service = new AgentReaderService(
                graph.Object, Mock.Of<IAgentTableRepository>(), NullLogger<AgentReaderService>.Instance);

            await Assert.ThrowsExceptionAsync<IOException>(() => service.ReadPageAsync(_runId, null, CancellationToken.None));
        }

        [TestMethod]
        public async Task SqlOperations_UseTheSameRunIdAndCancellationAndPropagateFailure()
        {
            using var cancellation = new CancellationTokenSource();
            IReadOnlyList<AgentRecord> rows = [new(Guid.Parse(AgentId), 1, null, true)];
            var table = new Mock<IAgentTableRepository>(MockBehavior.Strict);
            table.Setup(repository => repository.ValidateTableAsync(_runId, cancellation.Token)).Returns(Task.CompletedTask);
            table.Setup(repository => repository.WriteAsync(_runId, rows, cancellation.Token)).ReturnsAsync(1);
            table.Setup(repository => repository.GetRowCountAsync(_runId, cancellation.Token)).ReturnsAsync(1L);
            var service = new AgentReaderService(
                Mock.Of<IAgentGraphRepository>(), table.Object, NullLogger<AgentReaderService>.Instance);

            await service.ValidateTableAsync(_runId, cancellation.Token);
            Assert.AreEqual(1, await service.WriteAsync(_runId, rows, cancellation.Token));
            Assert.AreEqual(1L, await service.GetRowCountAsync(_runId, cancellation.Token));
            table.VerifyAll();

            table.Setup(repository => repository.WriteAsync(_runId, rows, cancellation.Token))
                .ThrowsAsync(new IOException("Synthetic SQL failure."));
            await Assert.ThrowsExceptionAsync<IOException>(() => service.WriteAsync(_runId, rows, cancellation.Token));
        }

        private static AgentReaderService CreateService(params AgentSourceRecord[] agents)
        {
            var graph = new Mock<IAgentGraphRepository>();
            graph.Setup(repository => repository.ReadPageAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AgentSourcePage(agents, null));
            return new AgentReaderService(graph.Object, Mock.Of<IAgentTableRepository>(), NullLogger<AgentReaderService>.Instance);
        }
    }
}
