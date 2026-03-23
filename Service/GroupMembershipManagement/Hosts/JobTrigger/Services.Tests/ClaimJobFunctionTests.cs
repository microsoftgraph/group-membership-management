// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.JobTrigger;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class ClaimJobFunctionTests
    {
        private Mock<IJobTriggerService> _jobTriggerService;
        private ClaimJobFunction _claimJobFunction;

        [TestInitialize]
        public void Setup()
        {
            _jobTriggerService = new Mock<IJobTriggerService>();
            _claimJobFunction = new ClaimJobFunction(
                NullLogger<ClaimJobFunction>.Instance,
                _jobTriggerService.Object);
        }

        [TestMethod]
        public async Task ClaimJobAsync_NullSyncJob_ReturnsFalse()
        {
            var request = new ClaimJobRequest
            {
                Status = SyncStatus.InProgress,
                SyncJob = null
            };

            var result = await _claimJobFunction.ClaimJobAsync(request);

            Assert.IsFalse(result);
            _jobTriggerService.Verify(
                s => s.TryClaimAndUpdateJobAsync(It.IsAny<SyncStatus>(), It.IsAny<SyncJob>()),
                Times.Never);
        }

        [TestMethod]
        public async Task ClaimJobAsync_ClaimSucceeds_ReturnsTrue()
        {
            var syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                Status = SyncStatus.Idle.ToString()
            };

            _jobTriggerService
                .Setup(s => s.TryClaimAndUpdateJobAsync(SyncStatus.InProgress, syncJob))
                .ReturnsAsync(true);

            var request = new ClaimJobRequest
            {
                Status = SyncStatus.InProgress,
                SyncJob = syncJob
            };

            var result = await _claimJobFunction.ClaimJobAsync(request);

            Assert.IsTrue(result);
            _jobTriggerService.Verify(
                s => s.TryClaimAndUpdateJobAsync(SyncStatus.InProgress, syncJob),
                Times.Once);
        }

        [TestMethod]
        public async Task ClaimJobAsync_ClaimFails_ReturnsFalse()
        {
            var syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                Status = SyncStatus.Idle.ToString()
            };

            _jobTriggerService
                .Setup(s => s.TryClaimAndUpdateJobAsync(SyncStatus.InProgress, syncJob))
                .ReturnsAsync(false);

            var request = new ClaimJobRequest
            {
                Status = SyncStatus.InProgress,
                SyncJob = syncJob
            };

            var result = await _claimJobFunction.ClaimJobAsync(request);

            Assert.IsFalse(result);
            _jobTriggerService.Verify(
                s => s.TryClaimAndUpdateJobAsync(SyncStatus.InProgress, syncJob),
                Times.Once);
        }

        [TestMethod]
        public async Task ClaimJobAsync_StuckInProgressStatus_PassesCorrectStatus()
        {
            var syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                Status = SyncStatus.InProgress.ToString()
            };

            _jobTriggerService
                .Setup(s => s.TryClaimAndUpdateJobAsync(SyncStatus.StuckInProgress, syncJob))
                .ReturnsAsync(true);

            var request = new ClaimJobRequest
            {
                Status = SyncStatus.StuckInProgress,
                SyncJob = syncJob
            };

            var result = await _claimJobFunction.ClaimJobAsync(request);

            Assert.IsTrue(result);
            _jobTriggerService.Verify(
                s => s.TryClaimAndUpdateJobAsync(SyncStatus.StuckInProgress, syncJob),
                Times.Once);
        }
    }
}
