// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.SyncJobUpdater;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.ServiceBus;
using Models.SyncJobHistory;
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
    public class OrchestratorFunctionTests
    {
        private Mock<IDurableOrchestrationContext> _mockContext;
        private Mock<IDatabaseSyncJobsRepository> _mockDatabaseSyncJobsRepository;
        private Mock<ISyncJobHistoryRepository> _mockSyncJobHistoryRepository;
        private MockLoggingRepository _mockLogger;
        private OrchestratorFunction _orchestratorFunction;
        private SyncJobUpdaterService _syncJobUpdaterService;
        private SyncJobHistory _syncJobHistory;
        private const string GroupMembership = "GroupMembership";

        [TestInitialize]
        public void Setup()
        {
            _mockContext = new Mock<IDurableOrchestrationContext>();
            _mockLogger = new MockLoggingRepository();
            _mockDatabaseSyncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            _mockSyncJobHistoryRepository = new Mock<ISyncJobHistoryRepository>();

            _mockDatabaseSyncJobsRepository.Setup(x => x.UpdateSyncJobStatusAsync(It.IsAny<IEnumerable<SyncJob>>(), It.IsAny<SyncStatus?>()));
            _mockSyncJobHistoryRepository.Setup(x => x.GetByRunIdAsync(It.IsAny<Guid>())).ReturnsAsync(() => _syncJobHistory);
            _mockSyncJobHistoryRepository.Setup(x => x.CreateAsync(It.IsAny<SyncJobHistory>()));
            _mockSyncJobHistoryRepository.Setup(x => x.UpdateAsync(It.IsAny<SyncJobHistory>()));

            _syncJobUpdaterService = new SyncJobUpdaterService( 
                                                               _mockDatabaseSyncJobsRepository.Object,
                                                               _mockLogger,
                                                               _mockSyncJobHistoryRepository.Object);
            _orchestratorFunction = new OrchestratorFunction();
            _mockContext.Setup(c => c.CallActivityAsync(nameof(JobStatusUpdaterFunction), It.IsAny<JobStatusUpdateQueueMessage>()))
                        .Callback<string, object>(async (name, request) =>
                        {
                            var message = (JobStatusUpdateQueueMessage)request;
                            await CallJobStatusUpdaterFunction(message);
                        });

            
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

            await _orchestratorFunction.RunOrchestratorAsync(_mockContext.Object, null);
            _mockContext.Verify(context => context.CallActivityAsync(nameof(JobStatusUpdaterFunction), It.IsAny<JobStatusUpdateQueueMessage>()), Times.Once);
            _mockContext.Verify(context => context.CallActivityAsync(nameof(LoggerFunction), It.IsAny<LoggerRequest>()), Times.Exactly(2));
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

            await _orchestratorFunction.RunOrchestratorAsync(_mockContext.Object, null);
            _mockContext.Verify(context => context.CallActivityAsync(nameof(JobStatusUpdaterFunction), It.IsAny<JobStatusUpdateQueueMessage>()), Times.Once);
            _mockContext.Verify(context => context.CallActivityAsync(nameof(LoggerFunction), It.IsAny<LoggerRequest>()), Times.Exactly(2));
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
            
            var loggerRequests = new List<LoggerRequest>();
            _mockContext
                .Setup(context => context.CallActivityAsync(nameof(LoggerFunction), It.IsAny<LoggerRequest>()))
                .Callback<string, object>((name, request) => loggerRequests.Add((LoggerRequest)request))
                .Returns(Task.CompletedTask);

            _mockContext.Setup(c => c.GetInput<OrchestratorRequest>()).Returns(orchestratorRequest);

            await _orchestratorFunction.RunOrchestratorAsync(_mockContext.Object, null);
            _mockContext.Verify(context => context.CallActivityAsync(nameof(LoggerFunction), It.IsAny<LoggerRequest>()), Times.Exactly(2), "Expected LoggerFunction to be called exactly twice.");
            _mockContext.Verify(context => context.CallActivityAsync(nameof(JobStatusUpdaterFunction), It.IsAny<JobStatusUpdateQueueMessage>()), Times.Once);
            _mockDatabaseSyncJobsRepository.Verify(x => x.UpdateSyncJobStatusAsync(It.Is<IEnumerable<SyncJob>>(jobs => jobs.Any(j => j.Id == message.JobId)), message.NewStatus), Times.Once);
            _mockSyncJobHistoryRepository.Verify(x => x.CreateAsync(It.Is<SyncJobHistory>(history => history.SyncJobId == message.JobId && history.Status == message.NewStatus.ToString())), Times.Once);
        }

        private async Task CallJobStatusUpdaterFunction(JobStatusUpdateQueueMessage message)
        {
            var function = new JobStatusUpdaterFunction(
               _mockLogger,
               _syncJobUpdaterService);

            await function.UpdateJobStatusAsync(message);
        }

    }
}
