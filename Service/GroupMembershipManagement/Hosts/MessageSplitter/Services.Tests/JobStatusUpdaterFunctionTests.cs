// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.MessageSplitter;
using MessageSplitter.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;

namespace Services.Tests
{
    [TestClass]
    public class JobStatusUpdaterFunctionTests
    {
        [TestMethod]
        public async Task UpdateJobStatusAsync_LogsAndCallsService()
        {
            var service = new Mock<IMessageSplitterService>();
            var function = new JobStatusUpdaterFunction(NullLogger<JobStatusUpdaterFunction>.Instance, service.Object);

            var syncJob = new SyncJob { Id = Guid.NewGuid(), RunId = Guid.NewGuid() };
            var request = new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.Error };

            await function.UpdateJobStatusAsync(request);

            service.Verify(x => x.UpdateJobStatusAsync(syncJob.Id, SyncStatus.Error), Times.Once());
        }
    }
}
