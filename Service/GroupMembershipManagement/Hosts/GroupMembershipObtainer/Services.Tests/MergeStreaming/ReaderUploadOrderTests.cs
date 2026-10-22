// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.GroupMembershipObtainer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Helpers;
using Moq;
using Repositories.Contracts;
using Repositories.Contracts.DestinationResolution;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Tests.Services.Infrastructure;

namespace Tests.Services.MergeStreaming
{
    /// <summary>
    /// The page readers write their upload in canonical order (stable for equal identifiers) so the merge can
    /// stream; merging their uploads produces the bytes the materializing merge produced for the Graph order.
    /// </summary>
    [TestClass]
    public class ReaderUploadOrderTests
    {
        public static IEnumerable<object[]> Readers() =>
            new[] { "members", "subsequent-members", "delta", "subsequent-delta" }.Select(r => new object[] { r });

        [DataTestMethod]
        [DynamicData(nameof(Readers), DynamicDataSourceType.Method)]
        public async Task Reader_WritesItsUploadInCanonicalOrder_AndTheMergeStreamsIt(string reader)
        {
            var destinationId = Guid.NewGuid();
            var groupId = Guid.NewGuid();
            var job = new SyncJob { Id = Guid.NewGuid(), RunId = Guid.NewGuid(), Query = "[]" };
            var rng = new Random(707);
            var dup = NewId(rng);
            var pages = new[]
            {
                // Graph order, unsorted, with a member listed twice in one page and once in the next page.
                Enumerable.Range(0, 40).Select(i => new AzureADUser { ObjectId = NewId(rng), DisplayName = $"p0-{i}" })
                    .Append(new AzureADUser { ObjectId = dup, DisplayName = "p0-dup-first" })
                    .Append(new AzureADUser { ObjectId = NewId(rng), DisplayName = "p0-x" })
                    .Append(new AzureADUser { ObjectId = dup, DisplayName = "p0-dup-second" })
                    .ToList(),
                Enumerable.Range(0, 40).Select(i => new AzureADUser { ObjectId = NewId(rng), DisplayName = $"p1-{i}" })
                    .Append(new AzureADUser { ObjectId = dup, DisplayName = "p1-dup" })
                    .ToList(),
            };

            var fixture = CreateFixture();
            var counts = new MemberReadCounts();
            var written = new List<string>();
            for (var p = 0; p < pages.Length; p++)
            {
                var page = pages[p].Select(Clone).ToList();
                await RunReaderAsync(reader, fixture, page, counts, job, groupId, destinationId);
                var path = fixture.Storage.Paths.Single(x => !written.Contains(x));
                written.Add(path);

                var upload = JsonSerializer.Deserialize<List<AzureADUser>>(fixture.Storage.GetContent(path));
                var stableSorted = pages[p].OrderBy(m => m, CanonicalMemberComparer<AzureADUser>.Instance).ToList();
                CollectionAssert.AreEqual(
                    stableSorted.Select(m => m.DisplayName).ToList(),
                    upload.Select(m => m.DisplayName).ToList(),
                    $"{reader}: page {p} upload is not in stable canonical order.");
            }

            // Expected: the materializing merge, which visits the uploads in listing (name) order and each page in
            // Graph order, keeping the first occurrence of each identifier.
            var expectedOrder = new List<AzureADUser>();
            var seen = new HashSet<Guid>();
            foreach (var p in Enumerable.Range(0, pages.Length).OrderBy(i => written[i], StringComparer.Ordinal))
            {
                foreach (var member in pages[p].Where(member => seen.Add(member.ObjectId)))
                {
                    expectedOrder.Add(member);
                }
            }

            var logger = new RecordingLogger<ReaderUploadOrderTests>();
            fixture.Storage.MergeLogger = logger;
            var prefix = GroupPartStoragePaths.BuildMergePrefix(destinationId, job.RunId.Value, 1);
            var count = await fixture.Storage.MergeAndStreamUserBlobsAsync(
                prefix, "merged.json", new AzureADGroup { ObjectId = destinationId }, job.RunId.Value, job.Id, false, false, job.Query);

            Assert.AreEqual(0, logger.Entries.Count(e => e.Level == LogLevel.Warning), "Reader uploads must merge on the streaming path.");
            Assert.AreEqual(expectedOrder.Count, count);
            var merged = JsonSerializer.Deserialize<Models.ServiceBus.GroupMembership>(fixture.Storage.GetContent("merged.json")).SourceMembers;
            CollectionAssert.AreEqual(
                expectedOrder.OrderBy(m => m, CanonicalMemberComparer<AzureADUser>.Instance).Select(m => m.DisplayName).ToList(),
                merged.Select(m => m.DisplayName).ToList());
            var firstListed = string.CompareOrdinal(written[0], written[1]) < 0 ? "p0-dup-first" : "p1-dup";
            Assert.AreEqual(firstListed, merged.Single(m => m.ObjectId == dup).DisplayName);
        }

        private static async Task RunReaderAsync(
            string reader,
            Fixture fixture,
            List<AzureADUser> members,
            MemberReadCounts counts,
            SyncJob job,
            Guid groupId,
            Guid destinationId)
        {
            switch (reader)
            {
                case "members":
                    fixture.GraphRepository
                        .Setup(r => r.GetFirstTransitiveMembersPageAsync(
                            groupId, It.IsAny<IReadOnlyList<string>>()))
                        .ReturnsAsync((members, counts, (string)null));
                    await new MembersReaderFunction(NullLogger<MembersReaderFunction>.Instance, fixture.Storage, fixture.Calculator)
                        .GetMembersAsync(new MembersReaderRequest { SyncJob = job, GroupId = groupId, TargetGroupId = destinationId, CurrentPart = 1, TotalParts = 1 });
                    break;
                case "subsequent-members":
                    fixture.GraphRepository
                        .Setup(r => r.GetNextTransitiveMembersPageAsync(
                            groupId, "next", It.IsAny<IReadOnlyList<string>>()))
                        .ReturnsAsync((members, counts, (string)null));
                    await new SubsequentMembersReaderFunction(NullLogger<SubsequentMembersReaderFunction>.Instance, fixture.Storage, fixture.Calculator)
                        .GetMembersAsync(new SubsequentMembersReaderRequest { SyncJob = job, GroupId = groupId, TargetGroupId = destinationId, NextPageUrl = "next", CurrentPart = 1, TotalParts = 1 });
                    break;
                case "delta":
                    fixture.GraphRepository
                        .Setup(r => r.GetFirstDeltaUsersPageAsync(groupId, 5))
                        .ReturnsAsync((members, counts, (string)null, "delta"));
                    await new DeltaUserReaderFunction(NullLogger<DeltaUserReaderFunction>.Instance, fixture.Storage, fixture.Calculator)
                        .GetDeltaUsersAsync(new DeltaUserReaderRequest { SyncJob = job, ObjectId = groupId, TargetGroupId = destinationId, PageCount = 5, CurrentPart = 1, TotalParts = 1 });
                    break;
                case "subsequent-delta":
                    fixture.GraphRepository
                        .Setup(r => r.GetNextDeltaUsersPagesAsync(groupId, "next", 5))
                        .ReturnsAsync((members, counts, (string)null, "delta"));
                    await new SubsequentDeltaUserReaderFunction(NullLogger<SubsequentDeltaUserReaderFunction>.Instance, fixture.Storage, fixture.Calculator)
                        .GetSubsequentDeltaUsersAsync(new SubsequentDeltaUserReaderRequest { SyncJob = job, ObjectId = groupId, TargetGroupId = destinationId, NextPageUrl = "next", PageCount = 5, CurrentPart = 1, TotalParts = 1 });
                    break;
            }
        }

        private static AzureADUser Clone(AzureADUser user) => new() { ObjectId = user.ObjectId, DisplayName = user.DisplayName };

        private static Guid NewId(Random rng)
        {
            var bytes = new byte[16];
            rng.NextBytes(bytes);
            return new Guid(bytes);
        }

        private static Fixture CreateFixture()
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
