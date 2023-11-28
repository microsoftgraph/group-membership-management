// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.MembershipAggregator;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using Polly;
using Repositories.Contracts;
using Repositories.Logging;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class GroupNameReaderFunctionTests
    {
        private Mock<ILoggingRepository> _mockLoggingRepository;
        private Mock<IGraphAPIService> _mockGraphAPIService;
        private GroupNameReaderFunction _groupNameReaderFunction;

        [TestInitialize]
        public void SetUp()
        {
            _mockLoggingRepository = new Mock<ILoggingRepository>();
            _mockGraphAPIService = new Mock<IGraphAPIService>();
            _groupNameReaderFunction = new GroupNameReaderFunction(_mockLoggingRepository.Object, _mockGraphAPIService.Object);
        }

        [TestMethod]
        public async Task GetGroupNameAsync_ShouldReturnCorrectGroupName()
        {
            var expectedGroupName = "Test Group Name";
            var syncJob = new SyncJob { TargetOfficeGroupId = Guid.NewGuid(), RunId = Guid.NewGuid() };
            _mockGraphAPIService.Setup(service => service.GetGroupNameAsync(syncJob.TargetOfficeGroupId))
                                .ReturnsAsync(expectedGroupName);

            var result = await _groupNameReaderFunction.GetGroupNameAsync(syncJob);

            Assert.IsNotNull(result);
            Assert.AreEqual(expectedGroupName, result.Name);
            _mockLoggingRepository.Verify(log => log.LogMessageAsync(It.Is<LogMessage>(m => m.Message.Contains("function started")), VerbosityLevel.DEBUG, It.IsAny<string>(), It.IsAny<string>()), Times.Once());
			_mockLoggingRepository.Verify(log => log.LogMessageAsync(It.Is<LogMessage>(m => m.Message.Contains("function completed")), VerbosityLevel.DEBUG, It.IsAny<string>(), It.IsAny<string>()), Times.Once());
		}
	}
}