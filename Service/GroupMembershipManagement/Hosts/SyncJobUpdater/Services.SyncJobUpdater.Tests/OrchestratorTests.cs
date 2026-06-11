// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BusinessLogic.SyncJobUpdater;
using Hosts.SyncJobUpdater;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.ServiceBus;
using Models.SyncJobHistory;
using Moq;
using Repositories.Contracts;

namespace Services.Tests
{
    [TestClass]
    public class OrchestratorFunctionTests
    {
        private Mock<TaskOrchestrationContext> _mockContext;
        private Mock<IDatabaseSyncJobsRepository> _mockDatabaseSyncJobsRepository;
        private Mock<ISyncJobHistoryRepository> _mockSyncJobHistoryRepository;
        private OrchestratorFunction _orchestratorFunction;
        private SyncJobUpdaterService _syncJobUpdaterService;
        private SyncJobHistory _syncJobHistory;
        private const string GroupMembership = "GroupMembership";

        [TestInitialize]
        public void Setup()
        {
            _mockContext = new Mock<TaskOrchestrationContext>();
            _mockDatabaseSyncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            _mockSyncJobHistoryRepository = new Mock<ISyncJobHistoryRepository>();

            _mockContext.Setup(c => c.CreateReplaySafeLogger(It.IsAny<string>()))
                .Returns(NullLogger.Instance);

            _mockDatabaseSyncJobsRepository.Setup(x => x.UpdateSyncJobStatusAsync(It.IsAny<IEnumerable<SyncJob>>(), It.IsAny<SyncStatus?>()));
            _mockSyncJobHistoryRepository.Setup(x => x.GetByRunIdAsync(It.IsAny<Guid>())).ReturnsAsync(() => _syncJobHistory);
            _mockSyncJobHistoryRepository.Setup(x => x.CreateAsync(It.IsAny<SyncJobHistory>()));
            _mockSyncJobHistoryRepository.Setup(x => x.UpdateAsync(It.IsAny<SyncJobHistory>()));

            var syncJobStatusService = new SyncJobStatusService(
                _mockDatabaseSyncJobsRepository.Object,
                _mockSyncJobHistoryRepository.Object);

            _syncJobUpdaterService = new SyncJobUpdaterService( 
                                                               _mockDatabaseSyncJobsRepository.Object,
                                                               NullLogger<SyncJobUpdaterService>.Instance,
                                                               syncJobStatusService);
                _orchestratorFunction = new OrchestratorFunction();
                _mockContext.Setup(c => c.CallActivityAsync(
                    It.Is<TaskName>(n => n.Name == nameof(JobStatusUpdaterFunction)),
                    It.IsAny<JobStatusUpdateQueueMessage>(),
                    It.IsAny<TaskOptions>()))
                .Returns((TaskName _, object payload, TaskOptions __) =>
                    CallJobStatusUpdaterFunction((JobStatusUpdateQueueMessage)payload));
        }

        [TestMethod]
        public async Task RunOrchestratorTest()
        {
            var syncJob = SampleDataHelper.CreateSampleSyncJobs(1, GroupMembership).First();
            var message = new JobStatusUpdateQueueMessage
            {
                JobId = syncJob.Id,
                RunId = syncJob.RunId.GetValueOrDefault(Guid.NewGuid()),
                NewStatus = SyncStatus.Idle,
                SyncJob = syncJob,
                UpdatedByFunction = "GraphUpdater"
            };
            
            var orchestratorRequest = new OrchestratorRequest
            {
                Message = message
            };

            _mockContext.Setup(c => c.GetInput<OrchestratorRequest>()).Returns(orchestratorRequest);

            await _orchestratorFunction.RunOrchestratorAsync(_mockContext.Object);
            _mockContext.Verify(context => context.CallActivityAsync(It.Is<TaskName>(n => n.Name == nameof(JobStatusUpdaterFunction)), It.IsAny<JobStatusUpdateQueueMessage>(), It.IsAny<TaskOptions>()), Times.Once);
            _mockDatabaseSyncJobsRepository.Verify(x => x.UpdateSyncJobStatusAsync(It.Is<IEnumerable<SyncJob>>(jobs => jobs.Any(j => j.Id == message.JobId)), message.NewStatus), Times.Once);
            _mockSyncJobHistoryRepository.Verify(x => x.CreateAsync(It.Is<SyncJobHistory>(history => history.SyncJobId == message.JobId && history.Status == message.NewStatus.ToString())), Times.Once);
        }

        [TestMethod]
        public async Task UpdateExistingRunHistoryTest()
        {
            var syncJob = SampleDataHelper.CreateSampleSyncJobs(1, GroupMembership).First();
            var message = new JobStatusUpdateQueueMessage
            {
                JobId = syncJob.Id,
                RunId = syncJob.RunId.GetValueOrDefault(Guid.NewGuid()),
                NewStatus = SyncStatus.Error,
                SyncJob = syncJob,
                UpdatedByFunction = "GraphUpdater"
            };

            _syncJobHistory = new SyncJobHistory
            {
                SyncJobId = message.JobId,
                RunId = message.RunId,
                StartTime = message.JobStartTime,
                EndTime = message.JobEndTime,
                Status = SyncStatus.InProgress.ToString(),
                UsersAdded = message.UsersAddedCount,
                UsersRemoved = message.UsersRemovedCount,
                ThresholdViolations = message.ThresholdViolations,
                UpdatedByFunction = message.UpdatedByFunction
            };

            var orchestratorRequest = new OrchestratorRequest
            {
                Message = message
            };

            _mockContext.Setup(c => c.GetInput<OrchestratorRequest>()).Returns(orchestratorRequest);

            await _orchestratorFunction.RunOrchestratorAsync(_mockContext.Object);
            _mockContext.Verify(context => context.CallActivityAsync(It.Is<TaskName>(n => n.Name == nameof(JobStatusUpdaterFunction)), It.IsAny<JobStatusUpdateQueueMessage>(), It.IsAny<TaskOptions>()), Times.Once);
            _mockDatabaseSyncJobsRepository.Verify(x => x.UpdateSyncJobStatusAsync(It.Is<IEnumerable<SyncJob>>(jobs => jobs.Any(j => j.Id == message.JobId)), message.NewStatus), Times.Once);
            _mockSyncJobHistoryRepository.Verify(x => x.UpdateAsync(It.Is<SyncJobHistory>(history => history.SyncJobId == message.JobId && history.Status == message.NewStatus.ToString())), Times.Once);
        }

        [TestMethod]
        public async Task RunOrchestratorTestWithErrorStatus()
        {
            var syncJob = SampleDataHelper.CreateSampleSyncJobs(1, GroupMembership).First();
            var message = new JobStatusUpdateQueueMessage
            {
                JobId = syncJob.Id,
                RunId = syncJob.RunId.GetValueOrDefault(Guid.NewGuid()),
                NewStatus = SyncStatus.Error,
                SyncJob = syncJob,
                UpdatedByFunction = nameof(StarterFunction)
            };
            
            var orchestratorRequest = new OrchestratorRequest
            {
                Message = message
            };

            _mockContext.Setup(c => c.GetInput<OrchestratorRequest>()).Returns(orchestratorRequest);

            await _orchestratorFunction.RunOrchestratorAsync(_mockContext.Object);
            _mockContext.Verify(context => context.CallActivityAsync(It.Is<TaskName>(n => n.Name == nameof(JobStatusUpdaterFunction)), It.IsAny<JobStatusUpdateQueueMessage>(), It.IsAny<TaskOptions>()), Times.Once);
            _mockDatabaseSyncJobsRepository.Verify(x => x.UpdateSyncJobStatusAsync(It.Is<IEnumerable<SyncJob>>(jobs => jobs.Any(j => j.Id == message.JobId)), message.NewStatus), Times.Once);
            _mockSyncJobHistoryRepository.Verify(x => x.CreateAsync(It.Is<SyncJobHistory>(history => history.SyncJobId == message.JobId && history.Status == message.NewStatus.ToString())), Times.Once);
        }

        private async Task CallJobStatusUpdaterFunction(JobStatusUpdateQueueMessage message)
        {
            var function = new JobStatusUpdaterFunction(
               NullLogger<JobStatusUpdaterFunction>.Instance,
               _syncJobUpdaterService);

            await function.UpdateJobStatusAsync(message);
        }

    }
}
