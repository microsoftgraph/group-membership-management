// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using BusinessLogic.SyncJobUpdater;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.SyncJobHistory;
using Moq;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class SyncJobStatusServiceTests
    {
        private Mock<IDatabaseSyncJobsRepository> _databaseSyncJobsRepository;
        private Mock<ISyncJobHistoryRepository> _syncJobHistoryRepository;
        private SyncJobStatusService _service;
        private SyncJob _job;

        [TestInitialize]
        public void Setup()
        {
            _databaseSyncJobsRepository = new Mock<IDatabaseSyncJobsRepository>(MockBehavior.Strict);
            _syncJobHistoryRepository = new Mock<ISyncJobHistoryRepository>(MockBehavior.Strict);

            _service = new SyncJobStatusService(_databaseSyncJobsRepository.Object, _syncJobHistoryRepository.Object);

            _job = new SyncJob
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                LastRunTime = new DateTime(2025, 12, 1, 9, 30, 0, DateTimeKind.Utc)
            };
        }

        [TestMethod]
        public async Task UpdateJobStatusAsync_WhenStatusNullAndNoHistory_DoesNotPersistHistory()
        {
            // Arrange
            _databaseSyncJobsRepository
                .Setup(repo => repo.UpdateSyncJobStatusAsync(It.Is<IEnumerable<SyncJob>>(jobs => jobs.Count() == 1 && jobs.First() == _job), null))
                .Returns(Task.CompletedTask)
                .Verifiable();

            // Act
            await _service.UpdateJobStatusAsync(_job, null, null, "FunctionName");

            // Assert
            _databaseSyncJobsRepository.Verify();
            _syncJobHistoryRepository.Verify(repo => repo.GetByRunIdAsync(It.IsAny<Guid>()), Times.Never);
            _syncJobHistoryRepository.Verify(repo => repo.CreateAsync(It.IsAny<SyncJobHistory>()), Times.Never);
            _syncJobHistoryRepository.Verify(repo => repo.UpdateAsync(It.IsAny<SyncJobHistory>()), Times.Never);
        }

        [TestMethod]
        public async Task UpdateJobStatusAsync_WithStatus_CreatesHistoryRecord()
        {
            // Arrange
            var status = SyncStatus.InProgress;
            var functionName = "Updater";

            _databaseSyncJobsRepository
                .Setup(repo => repo.UpdateSyncJobStatusAsync(It.Is<IEnumerable<SyncJob>>(jobs => jobs.Count() == 1 && jobs.First() == _job), status))
                .Returns(Task.CompletedTask)
                .Verifiable();

            _syncJobHistoryRepository
                .Setup(repo => repo.GetByRunIdAsync(_job.RunId!.Value))
                .ReturnsAsync((SyncJobHistory)null!);

            _syncJobHistoryRepository
                .Setup(repo => repo.CreateAsync(It.Is<SyncJobHistory>(history =>
                    history.SyncJobId == _job.Id &&
                    history.RunId == _job.RunId &&
                    history.Status == status.ToString() &&
                    history.UpdatedByFunction == functionName &&
                    history.StartTime == _job.LastRunTime &&
                    history.CreatedAt != default &&
                    history.UpdatedAt != default)))
                .Returns(Task.CompletedTask)
                .Verifiable();

            // Act
            await _service.UpdateJobStatusAsync(_job, status, functionName: functionName);

            // Assert
            _databaseSyncJobsRepository.Verify();
            _syncJobHistoryRepository.Verify();
        }

        [TestMethod]
        public async Task CreateOrUpdateJobHistoryAsync_WithExistingHistory_UpdatesFields()
        {
            // Arrange
            var runId = Guid.NewGuid();
            var existingHistory = new SyncJobHistory
            {
                RunId = runId,
                SyncJobId = _job.Id,
                Status = "Old",
                UpdatedByFunction = "OldFunction"
            };

            var newStart = new DateTime(2025, 12, 1, 9, 0, 0, DateTimeKind.Utc);
            var newEnd = new DateTime(2025, 12, 1, 9, 45, 0, DateTimeKind.Utc);

            var historyUpdate = new SyncJobHistory
            {
                RunId = runId,
                StartTime = newStart,
                EndTime = newEnd,
                Status = SyncStatus.Idle.ToString(),
                UsersAdded = 5,
                UsersRemoved = 2,
                ThresholdViolations = 1,
                UpdatedByFunction = "Updater",
            };

            _syncJobHistoryRepository
                .Setup(repo => repo.GetByRunIdAsync(runId))
                .ReturnsAsync(existingHistory);

            _syncJobHistoryRepository
                .Setup(repo => repo.UpdateAsync(It.Is<SyncJobHistory>(history =>
                    history == existingHistory &&
                    history.Status == historyUpdate.Status &&
                    history.UsersAdded == historyUpdate.UsersAdded &&
                    history.UsersRemoved == historyUpdate.UsersRemoved &&
                    history.ThresholdViolations == historyUpdate.ThresholdViolations &&
                    history.UpdatedByFunction == historyUpdate.UpdatedByFunction &&
                    history.StartTime == newStart &&
                    history.EndTime == newEnd &&
                    history.Duration == (int)(newEnd - newStart).TotalSeconds &&
                    history.UpdatedAt != default)))
                .Returns(Task.CompletedTask)
                .Verifiable();

            // Act
            await _service.CreateOrUpdateJobHistoryAsync(historyUpdate);

            // Assert
            _syncJobHistoryRepository.Verify();
        }

        [TestMethod]
        public async Task CreateOrUpdateJobHistoryAsync_WithoutExistingHistory_ComputesDuration()
        {
            // Arrange
            var runId = Guid.NewGuid();
            var start = new DateTime(2025, 11, 30, 20, 0, 0, DateTimeKind.Utc);
            var end = new DateTime(2025, 11, 30, 20, 30, 0, DateTimeKind.Utc);

            var history = new SyncJobHistory
            {
                SyncJobId = _job.Id,
                RunId = runId,
                StartTime = start,
                EndTime = end,
                Status = SyncStatus.Error.ToString()
            };

            _syncJobHistoryRepository
                .Setup(repo => repo.GetByRunIdAsync(runId))
                .ReturnsAsync((SyncJobHistory)null!);

            _syncJobHistoryRepository
                .Setup(repo => repo.CreateAsync(It.Is<SyncJobHistory>(h =>
                    h == history &&
                    h.Duration == (int)(end - start).TotalSeconds)))
                .Returns(Task.CompletedTask)
                .Verifiable();

            // Act
            await _service.CreateOrUpdateJobHistoryAsync(history);

            // Assert
            _syncJobHistoryRepository.Verify();
        }
    }
}
