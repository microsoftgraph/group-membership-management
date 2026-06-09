// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.GroupOwnershipObtainer;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using Repositories.Contracts;

namespace Tests.Services
{
    [TestClass]
    public class FeatureFlagFunctionTests
    {
        [TestMethod]
        public async Task CheckFeatureFlagStateAsync_ReturnsFlagValue()
        {
            var featureFlagRepository = new Mock<IFeatureFlagRepository>();
            featureFlagRepository
                .Setup(x => x.IsFeatureFlagEnabledAsync(
                    It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<Guid?>()))
                .ReturnsAsync(true);

            var function = new FeatureFlagFunction(
                NullLogger<FeatureFlagFunction>.Instance,
                featureFlagRepository.Object);

            var request = new FeatureFlagRequest
            {
                SyncJob = new SyncJob { Id = Guid.NewGuid(), RunId = Guid.NewGuid() },
                CurrentPart = 1,
                TotalParts = 3,
                FeatureFlagName = "SomeFlag",
                RefreshAppConfigurationValues = false
            };

            var result = await function.CheckFeatureFlagStateAsync(request);

            Assert.IsTrue(result);
            featureFlagRepository.Verify(x => x.IsFeatureFlagEnabledAsync(
                "SomeFlag", false, request.SyncJob.RunId), Times.Once);
        }

        [TestMethod]
        public async Task CheckFeatureFlagStateAsync_FlagDisabled_ReturnsFalse()
        {
            var featureFlagRepository = new Mock<IFeatureFlagRepository>();
            featureFlagRepository
                .Setup(x => x.IsFeatureFlagEnabledAsync(
                    It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<Guid?>()))
                .ReturnsAsync(false);

            var function = new FeatureFlagFunction(
                NullLogger<FeatureFlagFunction>.Instance,
                featureFlagRepository.Object);

            var request = new FeatureFlagRequest
            {
                SyncJob = new SyncJob { Id = Guid.NewGuid(), RunId = Guid.NewGuid() },
                CurrentPart = 1,
                TotalParts = 1,
                FeatureFlagName = "Flag",
                RefreshAppConfigurationValues = true
            };

            var result = await function.CheckFeatureFlagStateAsync(request);

            Assert.IsFalse(result);
        }
    }
}
