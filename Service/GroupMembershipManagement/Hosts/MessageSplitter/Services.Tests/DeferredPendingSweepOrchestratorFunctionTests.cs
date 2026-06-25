// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.MessageSplitter;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class DeferredPendingSweepOrchestratorFunctionTests
    {
        private Mock<TaskOrchestrationContext> _context;
        private RunLimiterSettings _settings;

        [TestInitialize]
        public void Setup()
        {
            _context = new Mock<TaskOrchestrationContext> { DefaultValue = DefaultValue.Mock };
            _context.Setup(x => x.GetInput<DeferredPendingSweepRequest>())
                    .Returns(new DeferredPendingSweepRequest("large"));
            _context.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>()))
                    .Returns(NullLogger.Instance);
            _context.Setup(x => x.CurrentUtcDateTime).Returns(DateTime.UtcNow);

            _settings = new RunLimiterSettings
            {
                IsEnabled = true,
                MaxInFlightMessages = 3,
                LeaseTimeoutMinutes = 15,
                HeartbeatIntervalMinutes = 5,
                MaxPendingAgeMinutes = 0
            };
        }

        [TestMethod]
        public async Task RunAsync_EmptyLane_ReturnsEarly()
        {
            // Arrange — empty lane
            _context.Setup(x => x.GetInput<DeferredPendingSweepRequest>())
                    .Returns(new DeferredPendingSweepRequest(""));

            var sut = new DeferredPendingSweepOrchestratorFunction(_settings);

            // Act
            await sut.RunAsync(_context.Object);

            // Assert — no entity calls
            _context.Verify(x => x.Entities.CallEntityAsync<int>(
                It.IsAny<EntityInstanceId>(),
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CallEntityOptions>()), Times.Never());
        }

        [TestMethod]
        public async Task RunAsync_BelowCapacity_DoesNotErrorAnyJob()
        {
            // In a capacity-free window the sweep does not transition any job to Error. It only kicks
            // the drain so waiting entries get dispatched once a slot frees.
            SetupPrune(prunedLeases: 0);
            SetupGetState(activeLeaseCount: 0);
            SetupDrainSubOrchestrator();

            var sut = new DeferredPendingSweepOrchestratorFunction(_settings);

            await sut.RunAsync(_context.Object);

            // No job is transitioned to Error by the sweep.
            _context.Verify(x => x.CallActivityAsync(
                nameof(JobStatusUpdaterFunction),
                It.IsAny<object>(),
                It.IsAny<TaskOptions>()), Times.Never());

            // The drain is still kicked so waiting entries get dispatched once capacity allows.
            _context.Verify(x => x.CallSubOrchestratorAsync(
                nameof(DeferredPendingDrainOrchestrator),
                It.IsAny<DeferredPendingDrainRequest>(),
                It.IsAny<TaskOptions>()), Times.Once());
        }

        [TestMethod]
        public async Task RunAsync_AtCapacity_TransitionsNoJobToError()
        {
            // In a capacity-saturated window the sweep removes nothing and Errors nothing —
            // unchanged behavior — while still kicking the drain.
            SetupPrune(prunedLeases: 0);
            SetupGetState(activeLeaseCount: 3);
            SetupDrainSubOrchestrator();

            var sut = new DeferredPendingSweepOrchestratorFunction(_settings);

            await sut.RunAsync(_context.Object);

            _context.Verify(x => x.CallActivityAsync(
                nameof(JobStatusUpdaterFunction),
                It.IsAny<object>(),
                It.IsAny<TaskOptions>()), Times.Never());

            _context.Verify(x => x.CallSubOrchestratorAsync(
                nameof(DeferredPendingDrainOrchestrator),
                It.IsAny<DeferredPendingDrainRequest>(),
                It.IsAny<TaskOptions>()), Times.Once());
        }

        #region Helpers

        private void SetupPrune(int prunedLeases)
        {
            _context.Setup(x => x.Entities.CallEntityAsync<int>(
                    It.IsAny<EntityInstanceId>(),
                    nameof(RunLimiter.Prune),
                    It.IsAny<object>(),
                    It.IsAny<CallEntityOptions>()))
                   .ReturnsAsync(prunedLeases);
        }

        private void SetupGetState(int activeLeaseCount)
        {
            var state = new RunLimiterState();
            for (int i = 0; i < activeLeaseCount; i++)
            {
                state.Leases[Guid.NewGuid().ToString()] = DateTimeOffset.UtcNow.AddMinutes(10);
            }

            _context.Setup(x => x.Entities.CallEntityAsync<RunLimiterState>(
                    It.IsAny<EntityInstanceId>(),
                    nameof(RunLimiter.GetState),
                    It.IsAny<object>(),
                    It.IsAny<CallEntityOptions>()))
                   .ReturnsAsync(state);
        }

        private void SetupDrainSubOrchestrator()
        {
            _context.Setup(x => x.CallSubOrchestratorAsync(
                    nameof(DeferredPendingDrainOrchestrator),
                    It.IsAny<DeferredPendingDrainRequest>(),
                    It.IsAny<TaskOptions>()))
                   .Returns(Task.CompletedTask);
        }

        #endregion
    }
}
