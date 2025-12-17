// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.MessageSplitter;
using MessageSplitter.Contracts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using Repositories.Contracts;

namespace Services.Tests
{
    [TestClass]
    public class JobStatusUpdaterFunctionTests
    {
        [TestMethod]
        public async Task UpdateJobStatusAsync_LogsAndCallsService()
        {
            var loggingRepository = new Mock<ILoggingRepository>();
            var service = new Mock<IMessageSplitterService>();
            var function = new JobStatusUpdaterFunction(loggingRepository.Object, service.Object);

            var syncJob = new SyncJob { Id = Guid.NewGuid(), RunId = Guid.NewGuid() };
            var request = new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.Error };

            await function.UpdateJobStatusAsync(request);

            service.Verify(x => x.UpdateJobStatusAsync(syncJob.Id, SyncStatus.Error), Times.Once());
            loggingRepository.Verify(x => x.LogMessageAsync(It.IsAny<LogMessage>(), VerbosityLevel.DEBUG, It.IsAny<string>(), It.IsAny<string>()), Times.Exactly(2));
        }
    }
}
