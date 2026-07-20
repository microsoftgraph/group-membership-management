// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.MembershipAggregator;
using MembershipAggregator.Services.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.ServiceBus;
using Moq;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class DeltaCalculatorFunctionTests
    {
        [TestMethod]
        public async Task CalculateDeltaAsync_MissingDestinationPath_ReturnsErrorWithoutBlobAccess()
        {
            var blobStorageRepository = new Mock<IBlobStorageRepository>();
            var deltaCalculatorService = new Mock<IDeltaCalculatorService>();
            var function = new DeltaCalculatorFunction(
                NullLogger<DeltaCalculatorFunction>.Instance,
                blobStorageRepository.Object,
                deltaCalculatorService.Object);

            var response = await function.CalculateDeltaAsync(new DeltaCalculatorRequest
            {
                SyncJob = new SyncJob
                {
                    Id = Guid.NewGuid(),
                    RunId = Guid.NewGuid(),
                    MembershipType = "GroupMembership"
                },
                CurrentPart = 2,
                TotalParts = 2,
                SourceGroupMembership = string.Empty,
                DestinationGroupMembership = string.Empty,
                ReadFromBlobs = true,
                SourceMembershipFilePath = "/source-membership.json",
                DestinationMembershipFilePath = null
            });

            Assert.AreEqual(MembershipDeltaStatus.Error, response.MembershipDeltaStatus);
            blobStorageRepository.Verify(
                repository => repository.DownloadFileAsync(It.IsAny<string>()),
                Times.Never());
            deltaCalculatorService.Verify(
                service => service.CalculateDifferenceAsync(It.IsAny<GroupMembership>(), It.IsAny<GroupMembership>()),
                Times.Never());
        }
    }
}
