// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using BusinessLogic.SyncJobUpdater;
using Hosts.MembershipAggregator;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.SyncJobHistory;
using Moq;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class JobStatusUpdaterFunctionTests
    {
        private Mock<IDatabaseSyncJobsRepository> _syncJobRepository;
        private Mock<ISyncJobHistoryRepository> _historyRepository;
        private JobStatusUpdaterFunction _function;
        private SyncJob _syncJob;

        [TestInitialize]
        public void Setup()
        {
            _syncJobRepository = new Mock<IDatabaseSyncJobsRepository>(MockBehavior.Strict);
            _historyRepository = new Mock<ISyncJobHistoryRepository>(MockBehavior.Strict);
            ISyncJobStatusService syncJobStatusService = new SyncJobStatusService(_syncJobRepository.Object, _historyRepository.Object);

            _function = new JobStatusUpdaterFunction(
                NullLogger<JobStatusUpdaterFunction>.Instance,
                _syncJobRepository.Object,
                syncJobStatusService);

            _syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                Period = 24,
                Status = SyncStatus.InProgress.ToString()
            };

            _syncJobRepository
                .Setup(repo => repo.GetSyncJobAsync(_syncJob.Id))
                .ReturnsAsync(_syncJob);

            _syncJobRepository
                .Setup(repo => repo.UpdateSyncJobStatusAsync(
                    It.Is<IEnumerable<SyncJob>>(jobs => jobs.Count() == 1 && jobs.First() == _syncJob),
                    It.IsAny<SyncStatus?>()))
                .Returns(Task.CompletedTask);
        }

        [TestMethod]
        public async Task UpdateJobStatusAsync_WhenThresholdExceededAndProposedCountsProvided_WritesCountsToHistoryAsync()
        {
            _historyRepository
                .Setup(repo => repo.GetByRunIdAsync(_syncJob.RunId!.Value))
                .ReturnsAsync((SyncJobHistory)null!);

            _historyRepository
                .Setup(repo => repo.CreateAsync(It.Is<SyncJobHistory>(history =>
                    history.Status == SyncStatus.ThresholdExceeded.ToString() &&
                    history.UsersAdded == 31 &&
                    history.UsersRemoved == 4)))
                .Returns(Task.CompletedTask)
                .Verifiable();

            await _function.UpdateJobStatusAsync(CreateRequest(SyncStatus.ThresholdExceeded, proposedUsersAdded: 31, proposedUsersRemoved: 4));

            _historyRepository.Verify();
        }

        [TestMethod]
        public async Task UpdateJobStatusAsync_WhenStatusIsIdleAndProposedCountsNull_PreservesExistingHistoryCountsAsync()
        {
            var existingHistory = new SyncJobHistory
            {
                SyncJobId = _syncJob.Id,
                RunId = _syncJob.RunId!.Value,
                Status = SyncStatus.ThresholdExceeded.ToString(),
                UsersAdded = 12,
                UsersRemoved = 5
            };

            _historyRepository
                .Setup(repo => repo.GetByRunIdAsync(_syncJob.RunId!.Value))
                .ReturnsAsync(existingHistory);

            _historyRepository
                .Setup(repo => repo.UpdateAsync(It.Is<SyncJobHistory>(history =>
                    history == existingHistory &&
                    history.Status == SyncStatus.Idle.ToString() &&
                    history.UsersAdded == 12 &&
                    history.UsersRemoved == 5)))
                .Returns(Task.CompletedTask)
                .Verifiable();

            await _function.UpdateJobStatusAsync(CreateRequest(SyncStatus.Idle));

            _historyRepository.Verify();
        }

        private JobStatusUpdaterRequest CreateRequest(SyncStatus status, int? proposedUsersAdded = null, int? proposedUsersRemoved = null) => new()
        {
            SyncJob = _syncJob,
            CurrentPart = 1,
            TotalParts = 1,
            Status = status,
            IsDryRun = false,
            IsNoOpSync = false,
            ProposedUsersAdded = proposedUsersAdded,
            ProposedUsersRemoved = proposedUsersRemoved
        };
    }
}
