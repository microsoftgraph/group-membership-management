// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.MembershipAggregator;
using Hosts.MembershipAggregator.Helpers;
using MembershipAggregator.Services.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Helpers;
using Models.ServiceBus;
using Moq;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Linq;
using System.Threading;
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
                deltaCalculatorService.Object,
                Options.Create(new MembershipMergeOptions()));

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
                service => service.CalculateDifferenceAsync(
                    It.IsAny<GroupMembership>(),
                    It.IsAny<MembershipDeltaSummary>()),
                Times.Never());
        }

        [TestMethod]
        public async Task CalculateDeltaAsync_StagedSnapshots_WritesDeltaByReference()
        {
            var store = new MembershipMergeTestStore();
            var groupId = ParseGuid(9001);
            var syncJob = new SyncJob
            {
                Id = ParseGuid(9002),
                RunId = ParseGuid(9003),
                TargetOfficeGroupId = groupId,
                MembershipType = MembershipTypes.GroupMembership.ToString()
            };
            var sourceEnvelope = new GroupMembership
            {
                Destination = new AzureADGroup { ObjectId = groupId },
                SyncJobId = syncJob.Id,
                RunId = syncJob.RunId.Value
            };
            var destinationEnvelope = new GroupMembership
            {
                Destination = new AzureADGroup { ObjectId = groupId },
                SyncJobId = syncJob.Id,
                RunId = syncJob.RunId.Value
            };
            await store.AddCompressedAsync(
                "/source",
                sourceEnvelope,
                User(ParseGuid(1), ParseGuid(101)),
                User(ParseGuid(3), ParseGuid(102)));
            await store.AddCompressedAsync(
                "/destination",
                destinationEnvelope,
                User(ParseGuid(2), Guid.Empty),
                User(ParseGuid(3), Guid.Empty));

            var deltaCalculatorService = new Mock<IDeltaCalculatorService>();
            deltaCalculatorService
                .Setup(service => service.CalculateDifferenceAsync(
                    It.IsAny<GroupMembership>(),
                    It.IsAny<MembershipDeltaSummary>()))
                .Returns((
                    GroupMembership _,
                    MembershipDeltaSummary summary) =>
                    Task.FromResult(new DeltaResponse
                    {
                        MembersToAddCount = summary.MembersToAddCount,
                        MembersToRemoveCount = summary.MembersToRemoveCount,
                        MembershipDeltaStatus = MembershipDeltaStatus.Ok
                    }));
            var logger = new Mock<ILogger<DeltaCalculatorFunction>>();
            logger
                .Setup(currentLogger => currentLogger.IsEnabled(LogLevel.Information))
                .Returns(true);
            var function = new DeltaCalculatorFunction(
                logger.Object,
                store.Repository.Object,
                deltaCalculatorService.Object,
                Options.Create(new MembershipMergeOptions()));

            var response = await function.CalculateDeltaAsync(new DeltaCalculatorRequest
            {
                SyncJob = syncJob,
                CurrentPart = 2,
                TotalParts = 2,
                DestinationMemberCount = 2,
                SourceGroupMembership = string.Empty,
                DestinationGroupMembership = string.Empty,
                ReadFromBlobs = true,
                SourceMembershipFilePath = "/source",
                DestinationMembershipFilePath = "/destination"
            });

            Assert.AreEqual(MembershipDeltaStatus.Ok, response.MembershipDeltaStatus);
            Assert.AreEqual(1, response.MembersToAddCount);
            Assert.AreEqual(1, response.MembersToRemoveCount);
            Assert.AreEqual(2, response.SourceMemberCount);
            Assert.AreEqual(2, response.DestinationMemberCount);
            Assert.IsTrue(store.ContainsMembership(response.MembersToAddFilePath));
            Assert.IsTrue(store.ContainsMembership(response.MembersToRemoveFilePath));
            Assert.IsTrue(store.ContainsText(response.DeltaManifestFilePath));
            Assert.AreEqual(true, response.UseStagedDeltaFiles);
            Assert.IsNull(response.CompressedMembersToAddJSON);
            Assert.IsNull(response.CompressedMembersToRemoveJSON);

            var addition = (await store.ReadMembersAsync(
                response.MembersToAddFilePath)).Single();
            Assert.AreEqual(ParseGuid(1), addition.ObjectId);
            Assert.AreEqual(MembershipAction.Add, addition.MembershipAction);
            CollectionAssert.AreEqual(
                new[] { ParseGuid(101) },
                addition.SourceGroups);

            var removal = (await store.ReadMembersAsync(
                response.MembersToRemoveFilePath)).Single();
            Assert.AreEqual(ParseGuid(2), removal.ObjectId);
            Assert.AreEqual(MembershipAction.Remove, removal.MembershipAction);

            store.Repository.Verify(
                repository => repository.DownloadFileAsync("/source"),
                Times.Never);
            store.Repository.Verify(
                repository => repository.DownloadFileAsync("/destination"),
                Times.Never);
            deltaCalculatorService.Verify(
                service => service.CalculateDifferenceAsync(
                    It.Is<GroupMembership>(membership =>
                        membership.SyncJobId == syncJob.Id),
                    It.Is<MembershipDeltaSummary>(summary =>
                        summary.SourceMemberCount == 2
                        && summary.DestinationMemberCount == 2
                        && summary.MembersToAddCount == 1
                        && summary.MembersToRemoveCount == 1)),
                Times.Once);
            logger.Verify(
                currentLogger => currentLogger.Log(
                    LogLevel.Information,
                    It.Is<EventId>(eventId => eventId.Id == 30105),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
            logger.Verify(
                currentLogger => currentLogger.Log(
                    LogLevel.Information,
                    It.Is<EventId>(eventId => eventId.Id == 30106),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        [TestMethod]
        public async Task CalculateDeltaAsync_NullDestination_UsesRequestGroupId()
        {
            var groupId = ParseGuid(6001);

            await AssertResolvedDestinationAsync(
                destination: null,
                requestGroupId: groupId,
                targetOfficeGroupId: Guid.Empty);
        }

        [TestMethod]
        public async Task CalculateDeltaAsync_EmptyDestinationId_UsesSyncJobGroupId()
        {
            var groupId = ParseGuid(6002);

            await AssertResolvedDestinationAsync(
                destination: new AzureADGroup(),
                requestGroupId: Guid.Empty,
                targetOfficeGroupId: groupId);
        }

        [TestMethod]
        public async Task CalculateDeltaAsync_RepeatedExecutionsUseDistinctAttemptPaths()
        {
            var groupId = ParseGuid(7001);
            var syncJob = new SyncJob
            {
                Id = ParseGuid(7002),
                RunId = ParseGuid(7003),
                TargetOfficeGroupId = groupId,
                MembershipType = MembershipTypes.GroupMembership.ToString()
            };
            var envelope = new GroupMembership
            {
                Destination = new AzureADGroup { ObjectId = groupId },
                SyncJobId = syncJob.Id,
                RunId = syncJob.RunId.Value
            };

            async Task<DeltaCalculatorResponse> ExecuteAsync()
            {
                var store = new MembershipMergeTestStore();
                await store.AddCompressedAsync(
                    "/source",
                    envelope,
                    User(ParseGuid(1), ParseGuid(101)));
                await store.AddCompressedAsync("/destination", envelope);
                var deltaCalculatorService = new Mock<IDeltaCalculatorService>();
                deltaCalculatorService
                    .Setup(service => service.CalculateDifferenceAsync(
                        It.IsAny<GroupMembership>(),
                        It.IsAny<MembershipDeltaSummary>()))
                    .Returns((
                        GroupMembership _,
                        MembershipDeltaSummary summary) =>
                        Task.FromResult(new DeltaResponse
                        {
                            MembersToAddCount = summary.MembersToAddCount,
                            MembersToRemoveCount = summary.MembersToRemoveCount,
                            MembershipDeltaStatus = MembershipDeltaStatus.Ok
                        }));
                var function = new DeltaCalculatorFunction(
                    NullLogger<DeltaCalculatorFunction>.Instance,
                    store.Repository.Object,
                    deltaCalculatorService.Object,
                    Options.Create(new MembershipMergeOptions()));

                return await function.CalculateDeltaAsync(new DeltaCalculatorRequest
                {
                    SyncJob = syncJob,
                    CurrentPart = 2,
                    TotalParts = 2,
                    SourceGroupMembership = string.Empty,
                    DestinationGroupMembership = string.Empty,
                    ReadFromBlobs = true,
                    SourceMembershipFilePath = "/source",
                    DestinationMembershipFilePath = "/destination"
                });
            }

            var first = await ExecuteAsync();
            var second = await ExecuteAsync();

            Assert.AreEqual(first.DeltaManifestFilePath, second.DeltaManifestFilePath);
            Assert.AreNotEqual(first.MembersToAddFilePath, second.MembersToAddFilePath);
            Assert.AreNotEqual(first.MembersToRemoveFilePath, second.MembersToRemoveFilePath);
        }

        [TestMethod]
        public async Task CalculateDeltaAsync_FailingDuplicatePreservesCommittedArtifacts()
        {
            var store = new MembershipMergeTestStore();
            var groupId = ParseGuid(7501);
            var syncJob = new SyncJob
            {
                Id = ParseGuid(7502),
                RunId = ParseGuid(7503),
                TargetOfficeGroupId = groupId,
                MembershipType = MembershipTypes.GroupMembership.ToString()
            };
            var envelope = new GroupMembership
            {
                Destination = new AzureADGroup { ObjectId = groupId },
                SyncJobId = syncJob.Id,
                RunId = syncJob.RunId.Value
            };
            await store.AddCompressedAsync(
                "/source",
                envelope,
                User(ParseGuid(1), ParseGuid(101)));
            await store.AddCompressedAsync(
                "/destination",
                envelope,
                User(ParseGuid(2), Guid.Empty));
            var request = new DeltaCalculatorRequest
            {
                SyncJob = syncJob,
                CurrentPart = 2,
                TotalParts = 2,
                SourceGroupMembership = string.Empty,
                DestinationGroupMembership = string.Empty,
                ReadFromBlobs = true,
                SourceMembershipFilePath = "/source",
                DestinationMembershipFilePath = "/destination"
            };
            var successfulService = new Mock<IDeltaCalculatorService>();
            successfulService
                .Setup(service => service.CalculateDifferenceAsync(
                    It.IsAny<GroupMembership>(),
                    It.IsAny<MembershipDeltaSummary>()))
                .Returns((
                    GroupMembership _,
                    MembershipDeltaSummary summary) =>
                    Task.FromResult(new DeltaResponse
                    {
                        MembersToAddCount = summary.MembersToAddCount,
                        MembersToRemoveCount = summary.MembersToRemoveCount,
                        MembershipDeltaStatus = MembershipDeltaStatus.Ok
                    }));
            var successfulFunction = new DeltaCalculatorFunction(
                NullLogger<DeltaCalculatorFunction>.Instance,
                store.Repository.Object,
                successfulService.Object,
                Options.Create(new MembershipMergeOptions()));
            var winner = await successfulFunction.CalculateDeltaAsync(request);
            var expectedFailure = new InvalidOperationException("Threshold calculation failed.");
            var failingService = new Mock<IDeltaCalculatorService>();
            failingService
                .Setup(service => service.CalculateDifferenceAsync(
                    It.IsAny<GroupMembership>(),
                    It.IsAny<MembershipDeltaSummary>()))
                .ThrowsAsync(expectedFailure);
            var failingFunction = new DeltaCalculatorFunction(
                NullLogger<DeltaCalculatorFunction>.Instance,
                store.Repository.Object,
                failingService.Object,
                Options.Create(new MembershipMergeOptions()));

            var actualFailure = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => failingFunction.CalculateDeltaAsync(request));

            Assert.AreSame(expectedFailure, actualFailure);
            Assert.IsTrue(store.ContainsMembership(winner.MembersToAddFilePath));
            Assert.IsTrue(store.ContainsMembership(winner.MembersToRemoveFilePath));
            Assert.IsTrue(store.ContainsText(winner.DeltaManifestFilePath));
            Assert.IsFalse(store.Operations.Any(operation =>
                operation.StartsWith("delete-prefix:", StringComparison.Ordinal)));

            var retry = await successfulFunction.CalculateDeltaAsync(request);
            Assert.AreEqual(winner.MembersToAddFilePath, retry.MembersToAddFilePath);
            Assert.AreEqual(winner.MembersToRemoveFilePath, retry.MembersToRemoveFilePath);
            Assert.AreEqual(winner.DeltaManifestFilePath, retry.DeltaManifestFilePath);
        }

        [TestMethod]
        public async Task CalculateDeltaAsync_WhenCancelled_PropagatesCancellation()
        {
            var store = new MembershipMergeTestStore();
            var groupId = ParseGuid(8001);
            var syncJob = new SyncJob
            {
                Id = ParseGuid(8002),
                RunId = ParseGuid(8003),
                TargetOfficeGroupId = groupId,
                MembershipType = MembershipTypes.GroupMembership.ToString()
            };
            var envelope = new GroupMembership
            {
                Destination = new AzureADGroup { ObjectId = groupId },
                SyncJobId = syncJob.Id,
                RunId = syncJob.RunId.Value
            };
            await store.AddCompressedAsync(
                "/source",
                envelope,
                User(ParseGuid(1), ParseGuid(101)));
            await store.AddCompressedAsync(
                "/destination",
                envelope,
                User(ParseGuid(2), Guid.Empty));
            var deltaCalculatorService = new Mock<IDeltaCalculatorService>();
            var function = new DeltaCalculatorFunction(
                NullLogger<DeltaCalculatorFunction>.Instance,
                store.Repository.Object,
                deltaCalculatorService.Object,
                Options.Create(new MembershipMergeOptions()));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            try
            {
                await function.CalculateDeltaAsync(
                    new DeltaCalculatorRequest
                    {
                        SyncJob = syncJob,
                        CurrentPart = 2,
                        TotalParts = 2,
                        SourceGroupMembership = string.Empty,
                        DestinationGroupMembership = string.Empty,
                        ReadFromBlobs = true,
                        SourceMembershipFilePath = "/source",
                        DestinationMembershipFilePath = "/destination"
                    },
                    cancellation.Token);
                Assert.Fail("Expected cancellation to propagate.");
            }
            catch (OperationCanceledException)
            {
            }

            deltaCalculatorService.Verify(
                service => service.CalculateDifferenceAsync(
                    It.IsAny<GroupMembership>(),
                    It.IsAny<MembershipDeltaSummary>()),
                Times.Never);
        }

        private static async Task AssertResolvedDestinationAsync(
            AzureADGroup destination,
            Guid requestGroupId,
            Guid targetOfficeGroupId)
        {
            var store = new MembershipMergeTestStore();
            var groupId = requestGroupId != Guid.Empty ? requestGroupId : targetOfficeGroupId;
            var syncJob = new SyncJob
            {
                Id = ParseGuid(6003),
                RunId = ParseGuid(6004),
                TargetOfficeGroupId = targetOfficeGroupId,
                MembershipType = MembershipTypes.GroupMembership.ToString()
            };
            var sourceEnvelope = new GroupMembership
            {
                Destination = destination,
                SyncJobId = syncJob.Id,
                RunId = syncJob.RunId.Value
            };
            var destinationEnvelope = new GroupMembership
            {
                Destination = new AzureADGroup { ObjectId = groupId },
                SyncJobId = syncJob.Id,
                RunId = syncJob.RunId.Value
            };
            await store.AddCompressedAsync("/source", sourceEnvelope, User(ParseGuid(1), ParseGuid(101)));
            await store.AddCompressedAsync("/destination", destinationEnvelope);

            var deltaCalculatorService = new Mock<IDeltaCalculatorService>();
            deltaCalculatorService
                .Setup(service => service.CalculateDifferenceAsync(
                    It.IsAny<GroupMembership>(),
                    It.IsAny<MembershipDeltaSummary>()))
                .Returns((
                    GroupMembership membership,
                    MembershipDeltaSummary summary) =>
                {
                    Assert.IsNotNull(membership.Destination);
                    Assert.AreEqual(groupId, membership.Destination.ObjectId);
                    return Task.FromResult(new DeltaResponse
                    {
                        MembersToAddCount = summary.MembersToAddCount,
                        MembersToRemoveCount = summary.MembersToRemoveCount,
                        MembershipDeltaStatus = MembershipDeltaStatus.Ok
                    });
                });
            var function = new DeltaCalculatorFunction(
                NullLogger<DeltaCalculatorFunction>.Instance,
                store.Repository.Object,
                deltaCalculatorService.Object,
                Options.Create(new MembershipMergeOptions()));

            var response = await function.CalculateDeltaAsync(new DeltaCalculatorRequest
            {
                SyncJob = syncJob,
                CurrentPart = 2,
                TotalParts = 2,
                GroupId = requestGroupId,
                SourceGroupMembership = string.Empty,
                DestinationGroupMembership = string.Empty,
                ReadFromBlobs = true,
                SourceMembershipFilePath = "/source",
                DestinationMembershipFilePath = "/destination"
            });

            Assert.AreEqual(MembershipDeltaStatus.Ok, response.MembershipDeltaStatus);
            Assert.AreEqual(1, response.MembersToAddCount);
            Assert.AreEqual(0, response.MembersToRemoveCount);
            deltaCalculatorService.Verify(
                service => service.CalculateDifferenceAsync(
                    It.IsAny<GroupMembership>(),
                    It.IsAny<MembershipDeltaSummary>()),
                Times.Once);
        }

        private static AzureADUser User(Guid objectId, Guid sourceGroup) =>
            new AzureADUser
            {
                ObjectId = objectId,
                SourceGroup = sourceGroup
            };

        private static Guid ParseGuid(long value) =>
            Guid.Parse($"00000000-0000-0000-0000-{value:x12}");
    }
}
