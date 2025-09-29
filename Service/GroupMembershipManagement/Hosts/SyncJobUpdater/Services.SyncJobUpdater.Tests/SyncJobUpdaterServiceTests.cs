// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.SyncJobUpdater;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.ServiceBus;
using Moq;
using Repositories.Contracts;
using Services.SyncJobUpdater.Tests.Mocks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class SyncJobUpdaterServiceTests
    {
        private Mock<IDatabaseSyncJobsRepository> _mockDatabaseSyncJobsRepository;
        private Mock<ISyncJobHistoryRepository> _mockSyncJobHistoryRepository;
        private MockLoggingRepository _mockLoggingRepository;
        private SyncJobUpdaterService _syncJobUpdaterService;
        private SyncJob _updatedJob = null;
        private DateTime _currentDateTime;

        [TestInitialize]
        public void Setup()
        {
            _currentDateTime = DateTime.UtcNow;

            _mockDatabaseSyncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            _mockSyncJobHistoryRepository = new Mock<ISyncJobHistoryRepository>();
            _mockLoggingRepository = new MockLoggingRepository();
            _syncJobUpdaterService = new SyncJobUpdaterService(
                _mockDatabaseSyncJobsRepository.Object, 
                _mockLoggingRepository,
                _mockSyncJobHistoryRepository.Object);

            _mockDatabaseSyncJobsRepository.Setup(x => x.UpdateSyncJobStatusAsync(It.IsAny<IEnumerable<SyncJob>>(), It.IsAny<SyncStatus?>()))
                .Callback<IEnumerable<SyncJob>, SyncStatus?>((jobs, status) => 
                {
                    _updatedJob = jobs.First();
                    _updatedJob.Status = status?.ToString();
                });
        }

        [TestMethod]
        public async Task UpdateSyncJobStatusWithMessage()
        {
            var jobId = Guid.NewGuid();
            var runId = Guid.NewGuid();
            var syncJob = new SyncJob { Id = jobId, RunId = runId, Period = 24, IgnoreThresholdOnce = true };
            var message = new JobStatusUpdateQueueMessage
            {
                JobId = jobId,
                RunId = runId,
                NewStatus = SyncStatus.Idle,
                SyncJob = CloneJob(syncJob),
                ThresholdViolations = 0,
                UpdatedByFunction = "TestFunction"
            };

            await _syncJobUpdaterService.UpdateSyncJobStatusAsync(message);

            _mockDatabaseSyncJobsRepository.Verify(repo => repo.UpdateSyncJobStatusAsync(It.Is<SyncJob[]>(jobs => jobs.Length == 1), SyncStatus.Idle), Times.Once);
            _mockSyncJobHistoryRepository.Verify(repo => repo.GetByRunIdAsync(runId), Times.Once);

            Assert.AreNotEqual(default, _updatedJob.LastRunTime);
            Assert.AreEqual(message.NewStatus.ToString(), _updatedJob.Status);
            Assert.AreEqual(_updatedJob.LastRunTime, _updatedJob.LastSuccessfulRunTime);
            Assert.IsTrue(syncJob.LastRunTime < _updatedJob.LastRunTime);
            Assert.IsTrue(syncJob.LastSuccessfulRunTime < _updatedJob.LastSuccessfulRunTime);
            Assert.IsTrue(_updatedJob.ScheduledDate >= _currentDateTime.AddHours(24));
            Assert.IsFalse(_updatedJob.IgnoreThresholdOnce);
        }

        [TestMethod]
        public async Task UpdateSyncJobStatusWithMessage_PreservesExistingHistoryValues()
        {
            var jobId = Guid.NewGuid();
            var runId = Guid.NewGuid();
            var syncJob = new SyncJob { Id = jobId, RunId = runId, Period = 24, IgnoreThresholdOnce = true };
            var existingHistory = new Models.SyncJobHistory.SyncJobHistory
            {
                Id = Guid.NewGuid(),
                SyncJobId = jobId,
                RunId = runId,
                StartTime = DateTime.UtcNow.AddMinutes(-30),
                UsersAdded = 10,
                UsersRemoved = 5,
                ThresholdViolations = 1,
                UpdatedByFunction = "OriginalFunction"
            };

            var message = new JobStatusUpdateQueueMessage
            {
                JobId = jobId,
                RunId = runId,
                NewStatus = SyncStatus.Idle,
                SyncJob = CloneJob(syncJob),
                JobEndTime = DateTime.UtcNow,
                UpdatedByFunction = "NewFunction"
                // Note: UsersAddedCount, UsersRemovedCount, ThresholdViolations are not provided
            };

            _mockSyncJobHistoryRepository.Setup(repo => repo.GetByRunIdAsync(runId))
                .ReturnsAsync(existingHistory);

            await _syncJobUpdaterService.UpdateSyncJobStatusAsync(message);

            _mockSyncJobHistoryRepository.Verify(repo => repo.UpdateAsync(It.Is<Models.SyncJobHistory.SyncJobHistory>(h => 
                h.UsersAdded == 10 && // Preserved
                h.UsersRemoved == 5 && // Preserved
                h.ThresholdViolations == 1 && // Preserved
                h.UpdatedByFunction == "NewFunction" && // Updated
                h.EndTime == message.JobEndTime && // Updated
                h.Status == "Idle" // Updated
            )), Times.Once);

            Assert.AreNotEqual(default, _updatedJob.LastRunTime);
            Assert.AreEqual(message.NewStatus.ToString(), _updatedJob.Status);
            Assert.AreEqual(_updatedJob.LastRunTime, _updatedJob.LastSuccessfulRunTime);
            Assert.IsTrue(syncJob.LastRunTime < _updatedJob.LastRunTime);
            Assert.IsTrue(syncJob.LastSuccessfulRunTime < _updatedJob.LastSuccessfulRunTime);
            Assert.IsTrue(_updatedJob.ScheduledDate >= _currentDateTime.AddHours(24));
            Assert.IsFalse(_updatedJob.IgnoreThresholdOnce);
        }

        [TestMethod]
        public async Task UpdateSyncJobStatusWithMessage_CreatesNewHistoryEntry()
        {
            var jobId = Guid.NewGuid();
            var runId = Guid.NewGuid();
            var syncJob = new SyncJob { Id = jobId, RunId = runId, Period = 24, IgnoreThresholdOnce = true };
            var message = new JobStatusUpdateQueueMessage
            {
                JobId = jobId,
                RunId = runId,
                NewStatus = SyncStatus.InProgress,
                SyncJob = CloneJob(syncJob),
                JobStartTime = DateTime.UtcNow.AddMinutes(-5),
                UsersAddedCount = 15,
                UsersRemovedCount = 3,
                ThresholdViolations = 0,
                UpdatedByFunction = "TestFunction"
            };

            _mockSyncJobHistoryRepository.Setup(repo => repo.GetByRunIdAsync(runId))
                .ReturnsAsync((Models.SyncJobHistory.SyncJobHistory)null);

            await _syncJobUpdaterService.UpdateSyncJobStatusAsync(message);

            _mockSyncJobHistoryRepository.Verify(repo => repo.CreateAsync(It.Is<Models.SyncJobHistory.SyncJobHistory>(h => 
                h.SyncJobId == jobId &&
                h.RunId == runId &&
                h.Status == "InProgress" &&
                h.UsersAdded == 15 &&
                h.UsersRemoved == 3 &&
                h.ThresholdViolations == 0 &&
                h.UpdatedByFunction == "TestFunction"
            )), Times.Once);

            Assert.AreNotEqual(default, _updatedJob.LastRunTime);
            Assert.AreEqual(message.NewStatus.ToString(), _updatedJob.Status);
            Assert.IsTrue(syncJob.LastRunTime < _updatedJob.LastRunTime);
            Assert.IsTrue(syncJob.LastSuccessfulRunTime == _updatedJob.LastSuccessfulRunTime);
            Assert.IsFalse(_updatedJob.IgnoreThresholdOnce);
        }

        private SyncJob CloneJob(SyncJob job)
        {
            return new SyncJob
            {
                Id = job.Id,
                RunId = job.RunId,
                Period = job.Period,
                Status = job.Status,
                LastRunTime = job.LastRunTime,
                LastSuccessfulRunTime = job.LastSuccessfulRunTime,
                ScheduledDate = job.ScheduledDate,
                DryRunTimeStamp = job.DryRunTimeStamp,
                IsDryRunEnabled = job.IsDryRunEnabled
            };
        }
    }
}