// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.GroupMembershipObtainer;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using Repositories.Contracts;
using Repositories.Contracts.DestinationResolution;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Tests.Services.Infrastructure;

namespace Tests.Services
{
    [TestClass]
    public sealed class BeforeSyncCountTests
    {
        [TestMethod]
        public async Task SendUsersAsync_SelectedSourceIsDestination_DoesNotWriteBeforeSyncCount()
        {
            var fixture = await CreateFixtureAsync(new[] { "users" });

            await fixture.Function.SendUsersAsync(fixture.Request);

            fixture.StatusService.Verify(
                service => service.UpdateJobStatusAsync(
                    It.IsAny<SyncJob>(),
                    It.IsAny<SyncStatus?>(),
                    It.IsAny<Models.SyncJobHistory.SyncJobHistory>(),
                    It.IsAny<string>()),
                Times.Never);
        }

        [TestMethod]
        public async Task SendUsersAsync_DefaultDestinationRead_WritesFullBeforeSyncCount()
        {
            var fixture = await CreateFixtureAsync(identityTypes: null);

            await fixture.Function.SendUsersAsync(fixture.Request);

            fixture.StatusService.Verify(
                service => service.UpdateJobStatusAsync(
                    It.IsAny<SyncJob>(),
                    It.IsAny<SyncStatus?>(),
                    It.Is<Models.SyncJobHistory.SyncJobHistory>(
                        history => history.BeforeSyncUserCount == 1),
                    "GroupMembershipObtainer"),
                Times.Once);
        }

        private static async Task<BeforeSyncFixture> CreateFixtureAsync(
            IReadOnlyCollection<string> identityTypes)
        {
            var groupId = Guid.NewGuid();
            var runId = Guid.NewGuid();
            var job = new SyncJob
            {
                Id = Guid.NewGuid(),
                RunId = runId,
                TargetOfficeGroupId = groupId,
                Query = "[{\"type\":\"GroupMembership\"}]",
                Status = SyncStatus.InProgress.ToString()
            };
            var storage = new InMemoryBlobStorageRepository();
            await storage.UploadFileAsync(
                $"{groupId}/userUploads/{runId}_GroupMembership_1_page.json",
                JsonSerializer.Serialize(new[]
                {
                    new AzureADUser { ObjectId = Guid.NewGuid() }
                }));
            var database = new Mock<IDatabaseSyncJobsRepository>();
            database
                .Setup(repository => repository.GetSyncJobAsync(job.Id))
                .ReturnsAsync(job);
            var destinationResolver = new Mock<IDestinationResolver>();
            destinationResolver
                .Setup(resolver => resolver.ResolveAsync(
                    job,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ResolvedGroupDestination
                {
                    ObjectId = groupId
                });
            var dryRun = new Mock<IDryRunValue>();
            var statusService = new Mock<ISyncJobStatusService>();
            var calculator = new SGMembershipCalculator(
                Mock.Of<IGraphGroupRepository>(),
                storage,
                database.Object,
                destinationResolver.Object,
                Mock.Of<IServiceBusQueueRepository>(),
                Mock.Of<IDatabaseDestinationAttributesRepository>(),
                NullLogger<SGMembershipCalculator>.Instance,
                dryRun.Object,
                statusService.Object);
            var function = new TransitiveAndDeltaUsersSenderFunction(
                NullLogger<TransitiveAndDeltaUsersSenderFunction>.Instance,
                storage,
                calculator);
            return new BeforeSyncFixture(
                function,
                statusService,
                new TransitiveAndDeltaUsersSenderRequest
                {
                    SyncJob = job,
                    ObjectId = groupId,
                    GroupId = groupId,
                    CurrentPart = 1,
                    TotalParts = 2,
                    IdentityTypes = identityTypes == null
                        ? null
                        : new List<string>(identityTypes)
                });
        }

        private sealed record BeforeSyncFixture(
            TransitiveAndDeltaUsersSenderFunction Function,
            Mock<ISyncJobStatusService> StatusService,
            TransitiveAndDeltaUsersSenderRequest Request);
    }
}
