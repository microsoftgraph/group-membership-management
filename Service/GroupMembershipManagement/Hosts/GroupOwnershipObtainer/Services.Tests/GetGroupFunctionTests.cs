// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.GroupOwnershipObtainer;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using Services.Contracts;

namespace Tests.Services
{
    [TestClass]
    public class GetGroupFunctionTests
    {
        [TestMethod]
        public async Task GetGroupAsync_ReturnsGroupIdFromService()
        {
            var expectedGroupId = Guid.NewGuid();
            var groupOwnershipObtainerService = new Mock<IGroupOwnershipObtainerService>();
            groupOwnershipObtainerService
                .Setup(x => x.GetGroupIdAsync(It.IsAny<SyncJob>()))
                .ReturnsAsync(expectedGroupId);

            var function = new GetGroupFunction(
                NullLogger<GetGroupFunction>.Instance,
                groupOwnershipObtainerService.Object);

            var request = new GetGroupRequest
            {
                SyncJob = new SyncJob { Id = Guid.NewGuid(), RunId = Guid.NewGuid() },
                CurrentPart = 1,
                TotalParts = 2
            };

            var result = await function.GetGroupAsync(request);

            Assert.AreEqual(expectedGroupId, result);
            groupOwnershipObtainerService.Verify(x => x.GetGroupIdAsync(request.SyncJob), Times.Once);
        }
    }
}
