// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.GroupMembershipObtainer;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.ServiceBus;
using Moq;
using Repositories.Contracts;
using Repositories.Contracts.DestinationResolution;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Tests.Services.Infrastructure;

namespace Tests.Services
{
    /// <summary>
    /// FR-010 owner decision (I14): every reader sets <c>SourceGroup</c> unless the request is the job's destination
    /// part, including a source part whose group equals the destination. Requests recorded before
    /// <c>IsDestinationPart</c> existed keep the legacy group comparison.
    /// </summary>
    [TestClass]
    public sealed class ReaderSourceGroupTests
    {
        public static IEnumerable<object[]> Cases()
        {
            // reader, source equals destination, IsDestinationPart, SourceGroup expected
            foreach (var reader in new[] { "members", "subsequent-members", "delta", "subsequent-delta" })
            {
                yield return new object[] { reader, true, false, true };
                yield return new object[] { reader, true, true, false };
                yield return new object[] { reader, false, false, true };
                yield return new object[] { reader, true, null, false };
                yield return new object[] { reader, false, null, true };
            }
        }

        [DataTestMethod]
        [DynamicData(nameof(Cases), DynamicDataSourceType.Method)]
        public async Task Reader_SetsSourceGroupUnlessDestinationPart(
            string reader,
            bool sourceEqualsDestination,
            bool? isDestinationPart,
            bool expectSourceGroup)
        {
            var destinationId = Guid.NewGuid();
            var groupId = sourceEqualsDestination ? destinationId : Guid.NewGuid();
            var fixture = CreateFixture(groupId);
            var members = new List<AzureADUser>
            {
                new() { ObjectId = Guid.NewGuid() },
                new() { ObjectId = Guid.NewGuid() }
            };
            var counts = new MemberReadCounts { MemberCount = members.Count };
            var job = new SyncJob { Id = Guid.NewGuid(), RunId = Guid.NewGuid(), Query = "[]" };

            switch (reader)
            {
                case "members":
                    fixture.GraphRepository
                        .Setup(r => r.GetFirstTransitiveMembersPageAsync(
                            groupId, It.IsAny<IReadOnlyList<string>>()))
                        .ReturnsAsync((members, counts, (string)null));
                    await new MembersReaderFunction(NullLogger<MembersReaderFunction>.Instance, fixture.Storage, fixture.Calculator)
                        .GetMembersAsync(new MembersReaderRequest
                        {
                            SyncJob = job,
                            GroupId = groupId,
                            TargetGroupId = destinationId,
                            CurrentPart = 1,
                            TotalParts = 2,
                            IsDestinationPart = isDestinationPart
                        });
                    break;
                case "subsequent-members":
                    fixture.GraphRepository
                        .Setup(r => r.GetNextTransitiveMembersPageAsync(
                            groupId, "next", It.IsAny<IReadOnlyList<string>>()))
                        .ReturnsAsync((members, counts, (string)null));
                    await new SubsequentMembersReaderFunction(NullLogger<SubsequentMembersReaderFunction>.Instance, fixture.Storage, fixture.Calculator)
                        .GetMembersAsync(new SubsequentMembersReaderRequest
                        {
                            SyncJob = job,
                            GroupId = groupId,
                            TargetGroupId = destinationId,
                            NextPageUrl = "next",
                            CurrentPart = 1,
                            TotalParts = 2,
                            IsDestinationPart = isDestinationPart
                        });
                    break;
                case "delta":
                    fixture.GraphRepository
                        .Setup(r => r.GetFirstDeltaUsersPageAsync(groupId, 5))
                        .ReturnsAsync((members, counts, (string)null, "delta"));
                    await new DeltaUserReaderFunction(NullLogger<DeltaUserReaderFunction>.Instance, fixture.Storage, fixture.Calculator)
                        .GetDeltaUsersAsync(new DeltaUserReaderRequest
                        {
                            SyncJob = job,
                            ObjectId = groupId,
                            TargetGroupId = destinationId,
                            PageCount = 5,
                            CurrentPart = 1,
                            TotalParts = 2,
                            IsDestinationPart = isDestinationPart
                        });
                    break;
                case "subsequent-delta":
                    fixture.GraphRepository
                        .Setup(r => r.GetNextDeltaUsersPagesAsync(groupId, "next", 5))
                        .ReturnsAsync((members, counts, (string)null, "delta"));
                    await new SubsequentDeltaUserReaderFunction(NullLogger<SubsequentDeltaUserReaderFunction>.Instance, fixture.Storage, fixture.Calculator)
                        .GetSubsequentDeltaUsersAsync(new SubsequentDeltaUserReaderRequest
                        {
                            SyncJob = job,
                            ObjectId = groupId,
                            TargetGroupId = destinationId,
                            NextPageUrl = "next",
                            PageCount = 5,
                            CurrentPart = 1,
                            TotalParts = 2,
                            IsDestinationPart = isDestinationPart
                        });
                    break;
            }

            var path = fixture.Storage.Paths.Single();
            var written = JsonSerializer.Deserialize<List<AzureADUser>>(fixture.Storage.GetContent(path));
            Assert.AreEqual(2, written.Count);
            var expected = expectSourceGroup ? groupId : Guid.Empty;
            Assert.IsTrue(
                written.All(member => member.SourceGroup == expected),
                $"Expected SourceGroup {expected} on every member; got {string.Join(", ", written.Select(m => m.SourceGroup))}.");
        }

        [DataTestMethod]
        [DataRow(true, true)]
        [DataRow(true, false)]
        [DataRow(false, true)]
        [DataRow(false, false)]
        public async Task SubOrchestrator_PassesIsDestinationPartToEveryReaderRequest(bool fullRead, bool isDestinationPart)
        {
            var destinationId = Guid.NewGuid();
            var request = new GroupMembershipRequest
            {
                SyncJob = new SyncJob { Id = Guid.NewGuid(), RunId = Guid.NewGuid(), Query = "[]" },
                SourceGroup = new AzureADGroup { ObjectId = destinationId },
                GroupId = destinationId,
                CurrentPart = 1,
                TotalParts = 2,
                IsDestinationPart = isDestinationPart
            };
            var captured = new List<bool?>();
            var context = new Mock<Microsoft.DurableTask.TaskOrchestrationContext>();
            context.Setup(c => c.GetInput<GroupMembershipRequest>()).Returns(request);
            context.Setup(c => c.CreateReplaySafeLogger(It.IsAny<string>())).Returns(NullLogger.Instance);
            context.SetupGet(c => c.CurrentUtcDateTime).Returns(new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc));
            context.Setup(c => c.CallActivityAsync<bool>(It.Is<Microsoft.DurableTask.TaskName>(n => n.Name == nameof(GroupValidatorFunction)), It.IsAny<object>(), It.IsAny<Microsoft.DurableTask.TaskOptions>()))
                .ReturnsAsync(true);
            context.Setup(c => c.CallActivityAsync<int>(It.Is<Microsoft.DurableTask.TaskName>(n => n.Name == nameof(GetTransitiveGroupCountFunction)), It.IsAny<object>(), It.IsAny<Microsoft.DurableTask.TaskOptions>()))
                .ReturnsAsync(0);
            context.Setup(c => c.CallActivityAsync<string>(It.Is<Microsoft.DurableTask.TaskName>(n => n.Name == nameof(FileDownloaderFunction)), It.IsAny<object>(), It.IsAny<Microsoft.DurableTask.TaskOptions>()))
                .ReturnsAsync((string)null);
            context.Setup(c => c.CallActivityAsync<BlobResult>(It.Is<Microsoft.DurableTask.TaskName>(n => n.Name == nameof(BlobCheckerFunction)), It.IsAny<object>(), It.IsAny<Microsoft.DurableTask.TaskOptions>()))
                .ReturnsAsync(new BlobResult { BlobStatus = BlobStatus.NotFound });
            context.Setup(c => c.CallActivityAsync<string>(It.Is<Microsoft.DurableTask.TaskName>(n => n.Name == nameof(MembersReaderFunction)), It.IsAny<object>(), It.IsAny<Microsoft.DurableTask.TaskOptions>()))
                .Returns<Microsoft.DurableTask.TaskName, object, Microsoft.DurableTask.TaskOptions>((_, input, _) =>
                {
                    captured.Add(((MembersReaderRequest)input).IsDestinationPart);
                    return Task.FromResult("next");
                });
            context.Setup(c => c.CallActivityAsync<string>(It.Is<Microsoft.DurableTask.TaskName>(n => n.Name == nameof(SubsequentMembersReaderFunction)), It.IsAny<object>(), It.IsAny<Microsoft.DurableTask.TaskOptions>()))
                .Returns<Microsoft.DurableTask.TaskName, object, Microsoft.DurableTask.TaskOptions>((_, input, _) =>
                {
                    captured.Add(((SubsequentMembersReaderRequest)input).IsDestinationPart);
                    return Task.FromResult((string)null);
                });
            context.Setup(c => c.CallActivityAsync<DeltaUrls>(It.Is<Microsoft.DurableTask.TaskName>(n => n.Name == nameof(DeltaUserReaderFunction)), It.IsAny<object>(), It.IsAny<Microsoft.DurableTask.TaskOptions>()))
                .Returns<Microsoft.DurableTask.TaskName, object, Microsoft.DurableTask.TaskOptions>((_, input, _) =>
                {
                    captured.Add(((DeltaUserReaderRequest)input).IsDestinationPart);
                    return Task.FromResult(new DeltaUrls { NextPageUrl = "next" });
                });
            context.Setup(c => c.CallActivityAsync<DeltaUrls>(It.Is<Microsoft.DurableTask.TaskName>(n => n.Name == nameof(SubsequentDeltaUserReaderFunction)), It.IsAny<object>(), It.IsAny<Microsoft.DurableTask.TaskOptions>()))
                .Returns<Microsoft.DurableTask.TaskName, object, Microsoft.DurableTask.TaskOptions>((_, input, _) =>
                {
                    captured.Add(((SubsequentDeltaUserReaderRequest)input).IsDestinationPart);
                    return Task.FromResult(new DeltaUrls { DeltaUrl = "delta" });
                });
            context.Setup(c => c.CallActivityAsync<GroupMembershipFileResult>(It.Is<Microsoft.DurableTask.TaskName>(n => n.Name == nameof(TransitiveAndDeltaUsersSenderFunction)), It.IsAny<object>(), It.IsAny<Microsoft.DurableTask.TaskOptions>()))
                .ReturnsAsync(new GroupMembershipFileResult { FilePath = "part.json", MemberCount = 1 });
            var function = new SubOrchestratorFunction(
                new Repositories.Mocks.MockDeltaCachingConfig { DeltaCacheEnabled = !fullRead },
                new Microsoft.ApplicationInsights.TelemetryClient(new Microsoft.ApplicationInsights.Extensibility.TelemetryConfiguration()));

            await function.RunSubOrchestratorAsync(context.Object);

            Assert.AreEqual(2, captured.Count, "Expected the first and the subsequent reader requests.");
            Assert.IsTrue(captured.All(value => value == isDestinationPart));
        }

        private static Fixture CreateFixture(Guid groupId)
        {
            var graphRepository = new Mock<IGraphGroupRepository>();
            var storage = new InMemoryBlobStorageRepository();
            var calculator = new SGMembershipCalculator(
                graphRepository.Object,
                storage,
                Mock.Of<IDatabaseSyncJobsRepository>(),
                Mock.Of<IDestinationResolver>(),
                Mock.Of<IServiceBusQueueRepository>(),
                Mock.Of<IDatabaseDestinationAttributesRepository>(),
                NullLogger<SGMembershipCalculator>.Instance,
                Mock.Of<IDryRunValue>(),
                Mock.Of<ISyncJobStatusService>());
            return new Fixture(graphRepository, storage, calculator);
        }

        private sealed record Fixture(
            Mock<IGraphGroupRepository> GraphRepository,
            InMemoryBlobStorageRepository Storage,
            SGMembershipCalculator Calculator);
    }
}
