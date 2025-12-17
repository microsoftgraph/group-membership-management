// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using MessageSplitter.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using Repositories.Contracts;

namespace Services.Tests
{
    [TestClass]
    public class MessageSplitterServiceTests
    {
        [TestMethod]
        public async Task UpdateJobStatusAsync_DoesNothing_WhenSyncJobNotFound()
        {
            var repo = new Mock<IDatabaseSyncJobsRepository>();
            repo.Setup(x => x.GetSyncJobAsync(It.IsAny<Guid>())).ReturnsAsync((SyncJob)null);

            var service = new MessageSplitterService(repo.Object);
            await service.UpdateJobStatusAsync(Guid.NewGuid(), SyncStatus.Idle);

            repo.Verify(x => x.UpdateSyncJobsAsync(It.IsAny<IEnumerable<SyncJob>>(), It.IsAny<SyncStatus>()), Times.Never());
        }

        [TestMethod]
        public async Task UpdateJobStatusAsync_UpdatesLastRunTime_WhenSyncJobFound()
        {
            var repo = new Mock<IDatabaseSyncJobsRepository>();
            var job = new SyncJob { Id = Guid.NewGuid(), LastRunTime = DateTime.UtcNow.AddDays(-1) };
            repo.Setup(x => x.GetSyncJobAsync(job.Id)).ReturnsAsync(job);
            repo.Setup(x => x.UpdateSyncJobsAsync(It.IsAny<IEnumerable<SyncJob>>(), It.IsAny<SyncStatus>())).Returns(Task.CompletedTask);

            var service = new MessageSplitterService(repo.Object);
            await service.UpdateJobStatusAsync(job.Id, SyncStatus.Error);

            repo.Verify(x => x.UpdateSyncJobsAsync(
                It.Is<IEnumerable<SyncJob>>(jobs => jobs.Single().Id == job.Id && jobs.Single().LastRunTime > DateTime.UtcNow.AddMinutes(-5)),
                SyncStatus.Error), Times.Once());
        }
    }
}
