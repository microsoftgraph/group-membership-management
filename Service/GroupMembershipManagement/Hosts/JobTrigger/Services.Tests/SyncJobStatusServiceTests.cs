// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using BusinessLogic.SyncJobUpdater;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Repositories.Contracts;
using System;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class SyncJobStatusServiceTests
    {
        private Mock<IDatabaseSyncJobsRepository> _databaseSyncJobsRepository = null!;
        private Mock<ISyncJobHistoryRepository> _syncJobHistoryRepository = null!;
        private SyncJobStatusService _service = null!;

        [TestInitialize]
        public void Setup()
        {
            _databaseSyncJobsRepository = new Mock<IDatabaseSyncJobsRepository>(MockBehavior.Strict);
            _syncJobHistoryRepository = new Mock<ISyncJobHistoryRepository>(MockBehavior.Strict);
            _service = new SyncJobStatusService(_databaseSyncJobsRepository.Object, _syncJobHistoryRepository.Object);
        }

        [TestMethod]
        public async Task SaveAdfRunIdAsync_DelegatesToHistoryRepository()
        {
            // Arrange
            var runId = Guid.NewGuid();
            var adfRunId = Guid.NewGuid();

            _syncJobHistoryRepository
                .Setup(repo => repo.SaveAdfRunIdAsync(runId, adfRunId))
                .ReturnsAsync(1)
                .Verifiable();

            // Act
            var affectedRows = await _service.SaveAdfRunIdAsync(runId, adfRunId);

            // Assert — the service is a thin pass-through to the history repository and must not
            // touch the SyncJob row (only SyncJobHistory carries the ADF run id).
            Assert.AreEqual(1, affectedRows);
            _syncJobHistoryRepository.Verify();
            _databaseSyncJobsRepository.VerifyNoOtherCalls();
        }

        [TestMethod]
        public async Task SaveAdfRunIdAsync_WithNullAdfRunId_DelegatesToHistoryRepository()
        {
            // Arrange
            var runId = Guid.NewGuid();

            _syncJobHistoryRepository
                .Setup(repo => repo.SaveAdfRunIdAsync(runId, null))
                .ReturnsAsync(0)
                .Verifiable();

            // Act
            var affectedRows = await _service.SaveAdfRunIdAsync(runId, null);

            // Assert
            Assert.AreEqual(0, affectedRows);
            _syncJobHistoryRepository.Verify();
            _databaseSyncJobsRepository.VerifyNoOtherCalls();
        }
    }
}
