// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using BusinessLogic.SyncJobUpdater;
using MessageSplitter.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.SyncJobHistory;
using Moq;
using Repositories.Contracts;
using Services.Contracts;

namespace Services.Tests
{
    [TestClass]
    public class MessageSplitterServiceTests
    {
        [TestMethod]
        public async Task UpdateJobStatusAsync_DoesNothing_WhenSyncJobNotFound()
        {
            var repo = new Mock<IDatabaseSyncJobsRepository>();
            var syncJobStatusService = new Mock<ISyncJobStatusService>();
            repo.Setup(x => x.GetSyncJobAsync(It.IsAny<Guid>())).ReturnsAsync((SyncJob)null);

            var service = new MessageSplitterService(repo.Object, syncJobStatusService.Object);
            await service.UpdateJobStatusAsync(Guid.NewGuid(), SyncStatus.Idle);

            repo.Verify(x => x.UpdateSyncJobsAsync(It.IsAny<IEnumerable<SyncJob>>(), It.IsAny<SyncStatus>()), Times.Never());
        }

        [TestMethod]
        public async Task UpdateJobStatusAsync_UpdatesLastRunTime_WhenSyncJobFound()
        {
            var repo = new Mock<IDatabaseSyncJobsRepository>();
            var syncJobStatusService = new Mock<ISyncJobStatusService>();
            var job = new SyncJob { Id = Guid.NewGuid(), LastRunTime = DateTime.UtcNow.AddDays(-1) };
            repo.Setup(x => x.GetSyncJobAsync(job.Id)).ReturnsAsync(job);
            repo.Setup(x => x.UpdateSyncJobsAsync(It.IsAny<IEnumerable<SyncJob>>(), It.IsAny<SyncStatus>())).Returns(Task.CompletedTask);

            var service = new MessageSplitterService(repo.Object, syncJobStatusService.Object);
            await service.UpdateJobStatusAsync(job.Id, SyncStatus.Error);

            syncJobStatusService.Verify(x => x.UpdateJobStatusAsync(
                It.Is<SyncJob>(job => job.Id == job.Id && job.LastRunTime > DateTime.UtcNow.AddMinutes(-5)),
                SyncStatus.Error,
                It.IsAny<SyncJobHistory>(),
                "MessageSplitter"), Times.Once());
        }
    }
}
