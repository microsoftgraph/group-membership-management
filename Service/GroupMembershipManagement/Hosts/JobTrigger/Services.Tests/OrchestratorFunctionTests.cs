// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Hosts.JobTrigger;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class OrchestratorFunctionTests
    {
        [TestMethod]
        public async Task ValidOrchestratorRunAsync()
        {
            var loggingRepository = new Mock<ILoggingRepository>();
            var graphRespository = new Mock<IGraphGroupRepository>();
            var jobTriggerService = new Mock<IJobTriggerService>();
            var jobTriggerServiceInProgress = new Mock<IJobTriggerService>();
            var jobTriggerServiceStuckInProgress = new Mock<IJobTriggerService>();
            var context = new Mock<IDurableOrchestrationContext>();
            var syncJobs = SampleDataHelper.CreateSampleSyncJobs(10, "SecurityGroup");
            var loggerJobProperties = new Dictionary<Guid, LogProperties>();

            loggingRepository.SetupGet(x => x.SyncJobProperties).Returns(loggerJobProperties);
            jobTriggerService.Setup(x => x.GetSyncJobsAsync(SyncStatus.Idle)).ReturnsAsync(syncJobs);
            jobTriggerServiceInProgress.Setup(x => x.GetSyncJobsAsync(SyncStatus.InProgress)).ReturnsAsync(new List<SyncJob>());
            jobTriggerServiceStuckInProgress.Setup(x => x.GetSyncJobsAsync(SyncStatus.StuckInProgress)).ReturnsAsync(new List<SyncJob>());

            context.Setup(x => x.CallActivityAsync<List<SyncJob>>(It.Is<string>(x => x == nameof(SyncJobsReaderFunction)), SyncStatus.Idle))
                        .Returns(() => CallSyncJobsReaderFunctionAsync(loggingRepository.Object, jobTriggerService.Object, SyncStatus.Idle));
            context.Setup(x => x.CallActivityAsync<List<SyncJob>>(It.Is<string>(x => x == nameof(SyncJobsReaderFunction)), SyncStatus.InProgress))
                        .Returns(() => CallSyncJobsReaderFunctionAsync(loggingRepository.Object, jobTriggerServiceInProgress.Object, SyncStatus.InProgress));
            context.Setup(x => x.CallActivityAsync<List<SyncJob>>(It.Is<string>(x => x == nameof(SyncJobsReaderFunction)), SyncStatus.StuckInProgress))
                        .Returns(() => CallSyncJobsReaderFunctionAsync(loggingRepository.Object, jobTriggerServiceStuckInProgress.Object, SyncStatus.StuckInProgress));
            context.Setup(x => x.CallSubOrchestratorAsync(It.Is<string>(x => x == nameof(SubOrchestratorFunction)), It.IsAny<SyncJob>()));

            var orchestrator = new OrchestratorFunction(loggingRepository.Object);
            await orchestrator.RunOrchestratorAsync(context.Object);

            Assert.IsTrue(syncJobs.All(x => x.RunId.HasValue));
            context.Verify(x => x.CallSubOrchestratorAsync(It.Is<string>(x => x == nameof(SubOrchestratorFunction)), It.IsAny<SyncJob>()),
                                Times.Exactly(syncJobs.Count));
        }

        private async Task<List<SyncJob>> CallSyncJobsReaderFunctionAsync(ILoggingRepository loggingRepository, IJobTriggerService jobTriggerService, SyncStatus status)
        {
            var syncJobsReaderFunction = new SyncJobsReaderFunction(loggingRepository, jobTriggerService);
            var jobs = await syncJobsReaderFunction.GetSyncJobsAsync(status);
            return jobs;
        }
    }
}
