// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.MembershipAggregator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class GroupNameReaderFunctionTests
    {
        private Mock<IGraphAPIService> _mockGraphAPIService;
        private GroupNameReaderFunction _groupNameReaderFunction;

        [TestInitialize]
        public void SetUp()
        {
            _mockGraphAPIService = new Mock<IGraphAPIService>();
            _groupNameReaderFunction = new GroupNameReaderFunction(NullLogger<GroupNameReaderFunction>.Instance, _mockGraphAPIService.Object);
        }

        [TestMethod]
        public async Task GetGroupNameAsync_ShouldReturnCorrectGroupName()
        {
            var expectedGroupName = "Test Group Name";
            var syncJob = new SyncJob { RunId = Guid.NewGuid() };
            var groupNameReaderRequest = new GroupNameReaderRequest { SyncJob = syncJob, CurrentPart = 1, TotalParts = 1, GroupId = Guid.NewGuid() };
            _mockGraphAPIService.Setup(service => service.GetGroupNameAsync(groupNameReaderRequest.GroupId))
                                .ReturnsAsync(expectedGroupName);

            var result = await _groupNameReaderFunction.GetGroupNameAsync(groupNameReaderRequest);

            Assert.IsNotNull(result);
            Assert.AreEqual(expectedGroupName, result.Name);
        }
    }
}