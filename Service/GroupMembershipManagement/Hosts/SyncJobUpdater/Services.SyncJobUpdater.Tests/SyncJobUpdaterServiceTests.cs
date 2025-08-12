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

        [TestMethod]
        public async Task UpdateSyncJobStatusWithMessage_PreservesAllJobPropertiesIncludingDestination()
        {
            var jobId = Guid.NewGuid();
            var runId = Guid.NewGuid();
            var testGroupId = Guid.NewGuid();
            var testDestination = $"[{{\"type\":\"GroupMembership\",\"value\":{{\"objectId\":\"{testGroupId}\"}}}}]";
            
            var syncJob = new SyncJob 
            { 
                Id = jobId, 
                RunId = runId, 
                Period = 24, 
                IgnoreThresholdOnce = true,
                Destination = testDestination,
                Query = "[{\"type\":\"GroupMembership\",\"sources\":[{\"type\":\"SecurityGroup\",\"id\":\"source-group-id\"}]}]",
                MembershipType = "GroupMembership",
                Requestor = "test-requestor@example.com"
            };

            var message = new JobStatusUpdateQueueMessage
            {
                JobId = jobId,
                RunId = runId,
                NewStatus = SyncStatus.InProgress,
                SyncJob = CloneJobWithAllProperties(syncJob),
                ThresholdViolations = 2,
                UpdatedByFunction = "JobTrigger"
            };

            await _syncJobUpdaterService.UpdateSyncJobStatusAsync(message);

            _mockDatabaseSyncJobsRepository.Verify(repo => repo.UpdateSyncJobStatusAsync(
                It.Is<SyncJob[]>(jobs => 
                    jobs.Length == 1 && 
                    jobs[0].Destination == testDestination &&
                    jobs[0].Query == syncJob.Query &&
                    jobs[0].MembershipType == "GroupMembership" &&
                    jobs[0].Requestor == "test-requestor@example.com" &&
                    jobs[0].ThresholdViolations == 2), 
                SyncStatus.InProgress), Times.Once);

            // Verify that all properties are preserved in the updated job
            Assert.AreEqual(testDestination, _updatedJob.Destination);
            Assert.AreEqual(syncJob.Query, _updatedJob.Query);
            Assert.AreEqual("GroupMembership", _updatedJob.MembershipType);
            Assert.AreEqual("test-requestor@example.com", _updatedJob.Requestor);
            Assert.AreEqual(2, _updatedJob.ThresholdViolations);
            Assert.AreEqual("InProgress", _updatedJob.Status);
        }

        [TestMethod]
        public async Task UpdateSyncJobStatusWithMessage_HandlesTeamsChannelDestination()
        {
            var jobId = Guid.NewGuid();
            var runId = Guid.NewGuid();
            var testGroupId = Guid.NewGuid();
            var testChannelId = "test-channel-id";
            var testDestination = $"[{{\"type\":\"TeamsChannelMembership\",\"value\":{{\"objectId\":\"{testGroupId}\",\"channelId\":\"{testChannelId}\"}}}}]";
            
            var syncJob = new SyncJob 
            { 
                Id = jobId, 
                RunId = runId, 
                Period = 12, 
                Destination = testDestination,
                MembershipType = "TeamsChannelMembership"
            };

            var message = new JobStatusUpdateQueueMessage
            {
                JobId = jobId,
                RunId = runId,
                NewStatus = SyncStatus.Idle,
                SyncJob = CloneJobWithAllProperties(syncJob),
                UpdatedByFunction = "GraphUpdater"
            };

            await _syncJobUpdaterService.UpdateSyncJobStatusAsync(message);

            // Verify that Teams channel destination is properly handled
            Assert.AreEqual(testDestination, _updatedJob.Destination);
            Assert.AreEqual("TeamsChannelMembership", _updatedJob.MembershipType);
            Assert.AreEqual("Idle", _updatedJob.Status);
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

        private SyncJob CloneJobWithAllProperties(SyncJob job)
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
                IsDryRunEnabled = job.IsDryRunEnabled,
                Destination = job.Destination,
                Query = job.Query,
                MembershipType = job.MembershipType,
                Requestor = job.Requestor,
                ThresholdViolations = job.ThresholdViolations,
                ThresholdPercentageForAdditions = job.ThresholdPercentageForAdditions,
                ThresholdPercentageForRemovals = job.ThresholdPercentageForRemovals,
                IgnoreThresholdOnce = job.IgnoreThresholdOnce
            };
        }
    }
}