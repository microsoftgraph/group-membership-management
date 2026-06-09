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
        public async Task ClaimJobAsync_NullSyncJob_ReturnsNull()
        {
            var request = new ClaimJobRequest
            {
                Status = SyncStatus.InProgress,
                SyncJob = null
            };

            var result = await _claimJobFunction.ClaimJobAsync(request);

            Assert.IsNull(result);
            _jobTriggerService.Verify(
                s => s.TryClaimAndUpdateJobAsync(It.IsAny<SyncStatus>(), It.IsAny<SyncJob>()),
                Times.Never);
        }

        [TestMethod]
        public async Task ClaimJobAsync_ClaimSucceeds_ReturnsClaimedJob()
        {
            var syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                Status = SyncStatus.Idle.ToString()
            };

            var claimedJob = new SyncJob
            {
                Id = syncJob.Id,
                RunId = syncJob.RunId,
                Status = SyncStatus.InProgress.ToString()
            };

            _jobTriggerService
                .Setup(s => s.TryClaimAndUpdateJobAsync(SyncStatus.InProgress, syncJob))
                .ReturnsAsync(claimedJob);

            var request = new ClaimJobRequest
            {
                Status = SyncStatus.InProgress,
                SyncJob = syncJob
            };

            var result = await _claimJobFunction.ClaimJobAsync(request);

            Assert.IsNotNull(result);
            Assert.AreEqual(SyncStatus.InProgress.ToString(), result.Status);
            _jobTriggerService.Verify(
                s => s.TryClaimAndUpdateJobAsync(SyncStatus.InProgress, syncJob),
                Times.Once);
        }

        [TestMethod]
        public async Task ClaimJobAsync_ClaimFails_ReturnsNull()
        {
            var syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                Status = SyncStatus.Idle.ToString()
            };

            _jobTriggerService
                .Setup(s => s.TryClaimAndUpdateJobAsync(SyncStatus.InProgress, syncJob))
                .ReturnsAsync((SyncJob)null);

            var request = new ClaimJobRequest
            {
                Status = SyncStatus.InProgress,
                SyncJob = syncJob
            };

            var result = await _claimJobFunction.ClaimJobAsync(request);

            Assert.IsNull(result);
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

            var claimedJob = new SyncJob
            {
                Id = syncJob.Id,
                RunId = syncJob.RunId,
                Status = SyncStatus.StuckInProgress.ToString()
            };

            _jobTriggerService
                .Setup(s => s.TryClaimAndUpdateJobAsync(SyncStatus.StuckInProgress, syncJob))
                .ReturnsAsync(claimedJob);

            var request = new ClaimJobRequest
            {
                Status = SyncStatus.StuckInProgress,
                SyncJob = syncJob
            };

            var result = await _claimJobFunction.ClaimJobAsync(request);

            Assert.IsNotNull(result);
            _jobTriggerService.Verify(
                s => s.TryClaimAndUpdateJobAsync(SyncStatus.StuckInProgress, syncJob),
                Times.Once);
        }
    }
}
