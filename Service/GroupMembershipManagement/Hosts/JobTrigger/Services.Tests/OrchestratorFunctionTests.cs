// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.JobTrigger;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
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
            var jobTriggerService = new Mock<IJobTriggerService>();
            var context = new Mock<TaskOrchestrationContext>();
            var syncJobs = SampleDataHelper.CreateSampleSyncJobs(10, "GroupMembership");
            context.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(NullLogger.Instance);

			bool jobTriggerThresholdExceeded = false;
            int maxJobsAllowed = syncJobs.Count;
			jobTriggerService.Setup(x => x.GetSyncJobsAsync())
											.ReturnsAsync(syncJobs);
			context.Setup(x => x.CallActivityAsync<List<SyncJob>>(It.Is<TaskName>(x => x == nameof(GetJobsFunction)), It.IsAny<object>(), It.IsAny<TaskOptions>()))
                        .Returns(() => CallGetSyncJobsAsync(jobTriggerService.Object));

            context.Setup(x => x.CallSubOrchestratorAsync(It.Is<TaskName>(x => x == nameof(SubOrchestratorFunction)), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()));

            var orchestrator = new OrchestratorFunction();
            await orchestrator.RunOrchestratorAsync(context.Object);

            Assert.IsTrue(syncJobs.All(x => x.RunId.HasValue));
            context.Verify(x => x.CallSubOrchestratorAsync(It.Is<TaskName>(x => x == nameof(SubOrchestratorFunction)), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()),
                                Times.Exactly(syncJobs.Count));
        }


        [TestMethod]
        public async Task ZeroJobsRetrieved()
        {
            var jobTriggerService = new Mock<IJobTriggerService>();
            var context = new Mock<TaskOrchestrationContext>();
            var syncJobs = SampleDataHelper.CreateSampleSyncJobs(0, "GroupMembership");
            context.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(NullLogger.Instance);
			bool jobTriggerThresholdExceeded = false;
            int maxJobsAllowed = syncJobs.Count;
			jobTriggerService.Setup(x => x.GetSyncJobsAsync())
											.ReturnsAsync((syncJobs));
			context.Setup(x => x.CallActivityAsync<List<SyncJob>>(It.Is<TaskName>(x => x == nameof(GetJobsFunction)), It.IsAny<object>(), It.IsAny<TaskOptions>()))
                        .Returns(() => CallGetSyncJobsAsync(jobTriggerService.Object));

            context.Setup(x => x.CallSubOrchestratorAsync(It.Is<TaskName>(x => x == nameof(SubOrchestratorFunction)), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()));
            var orchestrator = new OrchestratorFunction();
            await orchestrator.RunOrchestratorAsync(context.Object);

            context.Verify(x => x.CallSubOrchestratorAsync(It.Is<TaskName>(x => x == nameof(SubOrchestratorFunction)), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()),
                                Times.Exactly(syncJobs.Count));
        }

		[TestMethod]
        public async Task NoContinuationTokenRetrieved()
        {
            var jobTriggerService = new Mock<IJobTriggerService>();
            var context = new Mock<TaskOrchestrationContext>();
            var syncJobs = SampleDataHelper.CreateSampleSyncJobs(10, "GroupMembership");
            context.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(NullLogger.Instance);

			bool jobTriggerThresholdExceeded = false;
            int maxJobsAllowed = syncJobs.Count;
			jobTriggerService.Setup(x => x.GetSyncJobsAsync())
											.ReturnsAsync((syncJobs));

			context.Setup(x => x.CallActivityAsync<List<SyncJob>>(It.Is<TaskName>(x => x == nameof(GetJobsFunction)), It.IsAny<object>(), It.IsAny<TaskOptions>()))
                        .Returns(() => CallGetSyncJobsAsync(jobTriggerService.Object));

            context.Setup(x => x.CallSubOrchestratorAsync(It.Is<TaskName>(x => x == nameof(SubOrchestratorFunction)), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()));
            var orchestrator = new OrchestratorFunction();
            await orchestrator.RunOrchestratorAsync(context.Object);

            context.Verify(x => x.CallSubOrchestratorAsync(It.Is<TaskName>(x => x == nameof(SubOrchestratorFunction)), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()),
                                Times.Exactly(syncJobs.Count));
        }

        [TestMethod]
        public async Task MultipleBatchesRetrieved()
        {
            var jobTriggerService = new Mock<IJobTriggerService>();
            var context = new Mock<TaskOrchestrationContext>();
            var syncJobs1 = SampleDataHelper.CreateSampleSyncJobs(10, "GroupMembership");
            var syncJobs2 = SampleDataHelper.CreateSampleSyncJobs(10, "GroupMembership");
            context.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(NullLogger.Instance);

			bool jobTriggerThresholdExceeded = false;
            int maxJobsAllowed = 2;
			jobTriggerService.Setup(x => x.GetSyncJobsAsync())
				                            .ReturnsAsync(() => (syncJobs1.Concat(syncJobs2).ToList() ));

			context.SetupSequence(x => x.CallActivityAsync<List<SyncJob>>(It.Is<TaskName>(x => x == nameof(GetJobsFunction)), It.IsAny<object>(), It.IsAny<TaskOptions>()))
                        .ReturnsAsync(() =>
                            syncJobs1
                        )
                        .ReturnsAsync(() =>
                           syncJobs2
                        );

            context.Setup(x => x.CallActivityAsync<List<SyncJob>>(It.Is<TaskName>(x => x == nameof(GetJobsFunction)), It.IsAny<object>(), It.IsAny<TaskOptions>()))
                        .Returns(() => CallGetSyncJobsAsync(jobTriggerService.Object));

            context.Setup(x => x.CallSubOrchestratorAsync(It.Is<TaskName>(x => x == nameof(SubOrchestratorFunction)), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()));
            var orchestrator = new OrchestratorFunction();
            await orchestrator.RunOrchestratorAsync(context.Object);

            context.Verify(x => x.CallSubOrchestratorAsync(It.Is<TaskName>(x => x == nameof(SubOrchestratorFunction)), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()),
                                Times.Exactly(syncJobs1.Count + syncJobs2.Count));
        }

        private async Task<List<SyncJob>> CallGetSyncJobsAsync(IJobTriggerService jobTriggerService)
        {
            var GetJobsFunction = new GetJobsFunction(jobTriggerService, NullLogger<GetJobsFunction>.Instance);
            var getJobsResponse = await GetJobsFunction.GetJobsToUpdateAsync(
                null);
            return getJobsResponse;
        }
    }
}
