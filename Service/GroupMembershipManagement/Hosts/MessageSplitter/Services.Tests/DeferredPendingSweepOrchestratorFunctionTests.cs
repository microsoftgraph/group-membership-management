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
        public async Task RunAsync_AtCapacity_SkipsPruningIndexItems()
        {
            // Arrange — 3 active leases, max is 3
            SetupPrune(prunedLeases: 0);
            SetupGetState(activeLeaseCount: 3);
            SetupDrainSubOrchestrator();

            var sut = new DeferredPendingSweepOrchestratorFunction(_settings);

            // Act
            await sut.RunAsync(_context.Object);

            // Assert — should NOT call PruneOlderThanMinutes on the index entity
            _context.Verify(x => x.Entities.CallEntityAsync<List<DeferredPendingItem>>(
                It.IsAny<EntityInstanceId>(),
                nameof(DeferredPendingIndexEntity.PruneOlderThanMinutes),
                It.IsAny<object>(),
                It.IsAny<CallEntityOptions>()), Times.Never());

            // Should still kick drain
            _context.Verify(x => x.CallSubOrchestratorAsync(
                nameof(DeferredPendingDrainOrchestrator),
                It.IsAny<DeferredPendingDrainRequest>(),
                It.IsAny<TaskOptions>()), Times.Once());
        }

        [TestMethod]
        public async Task RunAsync_BelowCapacity_PrunesOldItems()
        {
            // Arrange — 1 active lease, max is 3 → capacity available
            var staleItem = new DeferredPendingItem(42, Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-90), Guid.NewGuid());
            SetupPrune(prunedLeases: 0);
            SetupGetState(activeLeaseCount: 1);
            SetupPruneIndex(new List<DeferredPendingItem> { staleItem });
            SetupJobStatusUpdater();
            SetupDrainSubOrchestrator();

            var sut = new DeferredPendingSweepOrchestratorFunction(_settings);

            // Act
            await sut.RunAsync(_context.Object);

            // Assert — should call PruneOlderThanMinutes
            _context.Verify(x => x.Entities.CallEntityAsync<List<DeferredPendingItem>>(
                It.IsAny<EntityInstanceId>(),
                nameof(DeferredPendingIndexEntity.PruneOlderThanMinutes),
                It.IsAny<object>(),
                It.IsAny<CallEntityOptions>()), Times.Once());

            // Should set pruned item to Error
            _context.Verify(x => x.CallActivityAsync(
                nameof(JobStatusUpdaterFunction),
                It.IsAny<object>(),
                It.IsAny<TaskOptions>()), Times.Once());
        }

        [TestMethod]
        public async Task RunAsync_BelowCapacity_NothingOld_PrunesNothing()
        {
            // Arrange — capacity available but no old items
            SetupPrune(prunedLeases: 0);
            SetupGetState(activeLeaseCount: 0);
            SetupPruneIndex(new List<DeferredPendingItem>());
            SetupDrainSubOrchestrator();

            var sut = new DeferredPendingSweepOrchestratorFunction(_settings);

            // Act
            await sut.RunAsync(_context.Object);

            // Assert — should call PruneOlderThanMinutes but no JobStatusUpdater calls
            _context.Verify(x => x.Entities.CallEntityAsync<List<DeferredPendingItem>>(
                It.IsAny<EntityInstanceId>(),
                nameof(DeferredPendingIndexEntity.PruneOlderThanMinutes),
                It.IsAny<object>(),
                It.IsAny<CallEntityOptions>()), Times.Once());

            _context.Verify(x => x.CallActivityAsync(
                nameof(JobStatusUpdaterFunction),
                It.IsAny<object>(),
                It.IsAny<TaskOptions>()), Times.Never());
        }

        [TestMethod]
        public async Task RunAsync_CustomMaxPendingAgeMinutes_UsesConfiguredValue()
        {
            // Arrange — set custom age
            _settings.MaxPendingAgeMinutes = 120;
            SetupPrune(prunedLeases: 0);
            SetupGetState(activeLeaseCount: 0);
            object capturedRequest = null;
            _context.Setup(x => x.Entities.CallEntityAsync<List<DeferredPendingItem>>(
                    It.IsAny<EntityInstanceId>(),
                    nameof(DeferredPendingIndexEntity.PruneOlderThanMinutes),
                    It.IsAny<object>(),
                    It.IsAny<CallEntityOptions>()))
                   .Callback<EntityInstanceId, string, object, CallEntityOptions>((_, _, input, _) => capturedRequest = input)
                   .ReturnsAsync(new List<DeferredPendingItem>());
            SetupDrainSubOrchestrator();

            var sut = new DeferredPendingSweepOrchestratorFunction(_settings);

            // Act
            await sut.RunAsync(_context.Object);

            // Assert — verify MaxAgeMinutes = 120
            Assert.IsNotNull(capturedRequest);
            var pruneReq = (PruneOlderThanMinutesRequest)capturedRequest;
            Assert.AreEqual(120, pruneReq.MaxAgeMinutes);
        }

        [TestMethod]
        public async Task RunAsync_DefaultMaxPendingAgeMinutes_FallsBackTo60()
        {
            // Arrange — MaxPendingAgeMinutes = 0 (default/unset)
            _settings.MaxPendingAgeMinutes = 0;
            SetupPrune(prunedLeases: 0);
            SetupGetState(activeLeaseCount: 0);
            object capturedRequest = null;
            _context.Setup(x => x.Entities.CallEntityAsync<List<DeferredPendingItem>>(
                    It.IsAny<EntityInstanceId>(),
                    nameof(DeferredPendingIndexEntity.PruneOlderThanMinutes),
                    It.IsAny<object>(),
                    It.IsAny<CallEntityOptions>()))
                   .Callback<EntityInstanceId, string, object, CallEntityOptions>((_, _, input, _) => capturedRequest = input)
                   .ReturnsAsync(new List<DeferredPendingItem>());
            SetupDrainSubOrchestrator();

            var sut = new DeferredPendingSweepOrchestratorFunction(_settings);

            // Act
            await sut.RunAsync(_context.Object);

            // Assert — verify MaxAgeMinutes falls back to 60
            Assert.IsNotNull(capturedRequest);
            var pruneReq = (PruneOlderThanMinutesRequest)capturedRequest;
            Assert.AreEqual(60, pruneReq.MaxAgeMinutes);
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
        public async Task RunAsync_AfterPruningStaleLeases_FreesCapacity_PrunesItems()
        {
            // Arrange — 3 leases but Prune removes 2 expired → only 1 active remains
            SetupPrune(prunedLeases: 2);
            SetupGetState(activeLeaseCount: 1);
            SetupPruneIndex(new List<DeferredPendingItem>
            {
                new DeferredPendingItem(10, Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-70), Guid.NewGuid()),
                new DeferredPendingItem(11, Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-65), Guid.NewGuid())
            });
            SetupJobStatusUpdater();
            SetupDrainSubOrchestrator();

            var sut = new DeferredPendingSweepOrchestratorFunction(_settings);

            // Act
            await sut.RunAsync(_context.Object);

            // Assert — should prune items since capacity freed after lease expiry
            _context.Verify(x => x.CallActivityAsync(
                nameof(JobStatusUpdaterFunction),
                It.IsAny<object>(),
                It.IsAny<TaskOptions>()), Times.Exactly(2));
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

        private void SetupPruneIndex(List<DeferredPendingItem> items)
        {
            _context.Setup(x => x.Entities.CallEntityAsync<List<DeferredPendingItem>>(
                    It.IsAny<EntityInstanceId>(),
                    nameof(DeferredPendingIndexEntity.PruneOlderThanMinutes),
                    It.IsAny<object>(),
                    It.IsAny<CallEntityOptions>()))
                   .ReturnsAsync(items);
        }

        private void SetupJobStatusUpdater()
        {
            _context.Setup(x => x.CallActivityAsync(
                    nameof(JobStatusUpdaterFunction),
                    It.IsAny<object>(),
                    It.IsAny<TaskOptions>()))
                   .Returns(Task.CompletedTask);
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
