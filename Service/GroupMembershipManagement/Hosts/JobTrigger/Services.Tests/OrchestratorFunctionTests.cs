// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.JobTrigger;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using Repositories.Contracts;
using Repositories.Logging;
using Services.Contracts;
using System;
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
            var graphRepository = new Mock<IGraphGroupRepository>();
            var jobTriggerService = new Mock<IJobTriggerService>();
            var jobTriggerServiceInProgress = new Mock<IJobTriggerService>();
            var jobTriggerServiceStuckInProgress = new Mock<IJobTriggerService>();
            var context = new Mock<IDurableOrchestrationContext>();
            var syncJobs = SampleDataHelper.CreateSampleSyncJobs(10, "SecurityGroup");
            var loggerJobProperties = new Dictionary<Guid, LogProperties>();

            loggingRepository.SetupGet(x => x.SyncJobProperties).Returns(loggerJobProperties);

            jobTriggerService.Setup(x => x.GetSyncJobsSegmentAsync())
                                    .ReturnsAsync(() => syncJobs);

            context.Setup(x => x.CallActivityAsync<List<SyncJob>>(It.Is<string>(x => x == nameof(GetJobsSegmentedFunction)), It.IsAny<object>()))
                        .Returns(() => CallGetSyncJobsSegmentAsync(loggingRepository.Object, jobTriggerService.Object));

            context.Setup(x => x.CallSubOrchestratorAsync(It.Is<string>(x => x == nameof(SubOrchestratorFunction)), It.IsAny<SyncJob>()));

            var orchestrator = new OrchestratorFunction(loggingRepository.Object);
            await orchestrator.RunOrchestratorAsync(context.Object);

            Assert.IsTrue(syncJobs.All(x => x.RunId.HasValue));
            context.Verify(x => x.CallSubOrchestratorAsync(It.Is<string>(x => x == nameof(SubOrchestratorFunction)), It.IsAny<SyncJob>()),
                                Times.Exactly(syncJobs.Count));
        }


        [TestMethod]
        public async Task ZeroJobsRetrieved()
        {
            var loggingRepository = new Mock<ILoggingRepository>();
            var graphRepository = new Mock<IGraphGroupRepository>();
            var jobTriggerService = new Mock<IJobTriggerService>();
            var context = new Mock<IDurableOrchestrationContext>();
            var syncJobs = SampleDataHelper.CreateSampleSyncJobs(0, "SecurityGroup");
            var emptySyncJobsList = new List<SyncJob>();
            var loggerJobProperties = new Dictionary<Guid, LogProperties>();

            loggingRepository.SetupGet(x => x.SyncJobProperties).Returns(loggerJobProperties);

            jobTriggerService.Setup(x => x.GetSyncJobsSegmentAsync())
                                    .ReturnsAsync(() => syncJobs);

            context.Setup(x => x.CallActivityAsync<List<SyncJob>>(It.Is<string>(x => x == nameof(GetJobsSegmentedFunction)), It.IsAny<object>()))
                        .Returns(() => CallGetSyncJobsSegmentAsync(loggingRepository.Object, jobTriggerService.Object));

            context.Setup(x => x.CallSubOrchestratorAsync(It.Is<string>(x => x == nameof(SubOrchestratorFunction)), It.IsAny<SyncJob>()));
            var orchestrator = new OrchestratorFunction(loggingRepository.Object);
            await orchestrator.RunOrchestratorAsync(context.Object);

            context.Verify(x => x.CallSubOrchestratorAsync(nameof(SubOrchestratorFunction), It.IsAny<SyncJob>()),
                                Times.Exactly(syncJobs.Count));
        }


        [TestMethod]
        public async Task NoContinuationTokenRetrieved()
        {
            var loggingRepository = new Mock<ILoggingRepository>();
            var graphRepository = new Mock<IGraphGroupRepository>();
            var jobTriggerService = new Mock<IJobTriggerService>();
            var context = new Mock<IDurableOrchestrationContext>();
            var syncJobs = SampleDataHelper.CreateSampleSyncJobs(10, "SecurityGroup");
            var emptySyncJobsList = new List<SyncJob>();
            var loggerJobProperties = new Dictionary<Guid, LogProperties>();

            loggingRepository.SetupGet(x => x.SyncJobProperties).Returns(loggerJobProperties);

            jobTriggerService.Setup(x => x.GetSyncJobsSegmentAsync())
                                    .ReturnsAsync(() => syncJobs);

            context.Setup(x => x.CallActivityAsync<List<SyncJob>>(It.Is<string>(x => x == nameof(GetJobsSegmentedFunction)), It.IsAny<object>()))
                        .Returns(() => CallGetSyncJobsSegmentAsync(loggingRepository.Object, jobTriggerService.Object));

            context.Setup(x => x.CallSubOrchestratorAsync(It.Is<string>(x => x == nameof(SubOrchestratorFunction)), It.IsAny<SyncJob>()));
            var orchestrator = new OrchestratorFunction(loggingRepository.Object);
            await orchestrator.RunOrchestratorAsync(context.Object);

            context.Verify(x => x.CallSubOrchestratorAsync(nameof(SubOrchestratorFunction), It.IsAny<SyncJob>()),
                                Times.Exactly(syncJobs.Count));
        }

        [TestMethod]
        public async Task MultipleBatchesRetrieved()
        {
            var loggingRepository = new Mock<ILoggingRepository>();
            var graphRepository = new Mock<IGraphGroupRepository>();
            var jobTriggerService = new Mock<IJobTriggerService>();
            var context = new Mock<IDurableOrchestrationContext>();
            var syncJobs1 = SampleDataHelper.CreateSampleSyncJobs(10, "SecurityGroup");
            var syncJobs2 = SampleDataHelper.CreateSampleSyncJobs(10, "SecurityGroup");
            var emptySyncJobsList = new List<SyncJob>();
            var loggerJobProperties = new Dictionary<Guid, LogProperties>();

            loggingRepository.SetupGet(x => x.SyncJobProperties).Returns(loggerJobProperties);

            jobTriggerService.Setup(x => x.GetSyncJobsSegmentAsync())
                .ReturnsAsync(() => syncJobs1.Concat(syncJobs2).ToList());

            context.SetupSequence(x => x.CallActivityAsync<List<SyncJob>>(nameof(GetJobsSegmentedFunction), It.IsAny<object>()))
                        .ReturnsAsync(() =>
                            syncJobs1
                        )
                        .ReturnsAsync(() =>
                           syncJobs2
                        );

            context.Setup(x => x.CallActivityAsync<List<SyncJob>>(It.Is<string>(x => x == nameof(GetJobsSegmentedFunction)), It.IsAny<object>()))
                        .Returns(() => CallGetSyncJobsSegmentAsync(loggingRepository.Object, jobTriggerService.Object));

            context.Setup(x => x.CallSubOrchestratorAsync(It.Is<string>(x => x == nameof(SubOrchestratorFunction)), It.IsAny<SyncJob>()));
            var orchestrator = new OrchestratorFunction(loggingRepository.Object);
            await orchestrator.RunOrchestratorAsync(context.Object);

            context.Verify(x => x.CallSubOrchestratorAsync(nameof(SubOrchestratorFunction), It.IsAny<SyncJob>()),
                                Times.Exactly(syncJobs1.Count + syncJobs2.Count));
        }

        private async Task<List<SyncJob>> CallGetSyncJobsSegmentAsync(ILoggingRepository loggingRepository, IJobTriggerService jobTriggerService)
        {
            var getJobsSegmentedFunction = new GetJobsSegmentedFunction(jobTriggerService, loggingRepository);
            var getJobsSegmentedResponse = await getJobsSegmentedFunction.GetJobsToUpdateAsync(
                null);
            return getJobsSegmentedResponse;
        }
    }
}