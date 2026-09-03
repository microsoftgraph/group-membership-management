// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.MembershipAggregator;
using Hosts.MembershipAggregator.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Helpers;
using Models.ServiceBus;
using Moq;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class MembershipExtractionStreamingTests
    {
        private static readonly JsonSerializerOptions _serializerOptions =
            new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
            };

        [TestMethod]
        public async Task ExtractMembershipAsync_MixedLayoutsAndEncodings_MatchesLegacyOracle()
        {
            var store = new MembershipMergeTestStore();
            var syncJob = CreateSyncJob();
            var firstSourceGroup = ParseGuid(101);
            var secondSourceGroup = ParseGuid(102);
            var excludedSourceGroup = ParseGuid(103);
            var memberA = ParseGuid(1);
            var memberC = ParseGuid(3);
            var memberD = ParseGuid(4);
            var memberE = ParseGuid(5);

            var firstEnvelope = CreateEnvelope(syncJob, exclusionary: false);
            firstEnvelope.Query = "first-source-query";
            firstEnvelope.ProjectedMemberCount = 42;
            var firstMembers = OrderedMembers(
                User(memberA, firstSourceGroup),
                User(memberC, firstSourceGroup));
            var secondEnvelope = CreateEnvelope(syncJob, exclusionary: false);
            var secondMembers = OrderedMembers(
                User(memberC, secondSourceGroup),
                User(memberE, secondSourceGroup));
            var excludedEnvelope = CreateEnvelope(syncJob, exclusionary: true);
            var excludedMembers = OrderedMembers(User(memberE, excludedSourceGroup));
            var destinationEnvelope = CreateEnvelope(syncJob, exclusionary: false);
            destinationEnvelope.Query = "destination-query";
            var destinationMembers = OrderedMembers(
                User(memberC, Guid.Empty),
                User(memberD, Guid.Empty));

            await store.AddCompressedAsync(
                "source-compressed",
                firstEnvelope,
                firstMembers.ToArray());
            store.AddRawContent(
                "source-released-layout",
                BuildReleasedLayout(secondEnvelope, secondMembers));
            await store.AddCompressedAsync(
                "source-excluded",
                excludedEnvelope,
                excludedMembers.ToArray());
            await store.AddCompressedAsync(
                "destination",
                destinationEnvelope,
                destinationMembers.ToArray());

            var function = new MembershipExtractionFunction(
                NullLogger<MembershipExtractionFunction>.Instance,
                store.Repository.Object,
                Options.Create(new MembershipMergeOptions()));
            var response = await function.ExtractMembershipAsync(
                CreateRequest(
                    syncJob,
                    "source-compressed",
                    "source-released-layout",
                    "source-excluded",
                    "destination"));

            Assert.IsTrue(response.IsSuccessful, response.ErrorMessage);
            Assert.AreEqual(2, response.SourceMemberCount);
            Assert.AreEqual(2, response.DestinationMemberCount);

            var expectedSource = LegacyExtractionOracle(
                (firstEnvelope, firstMembers),
                (secondEnvelope, secondMembers),
                (excludedEnvelope, excludedMembers));
            var actualSource = await store.ReadMembersAsync(
                response.SourceMembershipFilePath);
            AssertMembersEqual(expectedSource, actualSource);

            var actualDestination = await store.ReadMembersAsync(
                response.DestinationMembershipFilePath);
            CollectionAssert.AreEqual(
                destinationMembers.Select(member => member.ObjectId).ToArray(),
                actualDestination.Select(member => member.ObjectId).ToArray());

            var sourceOutputEnvelope = await store.ReadEnvelopeAsync(
                response.SourceMembershipFilePath);
            Assert.AreEqual(firstEnvelope.RunId, sourceOutputEnvelope.RunId);
            Assert.AreEqual(firstEnvelope.SyncJobId, sourceOutputEnvelope.SyncJobId);
            Assert.AreEqual(firstEnvelope.Query, sourceOutputEnvelope.Query);
            Assert.AreEqual(
                firstEnvelope.ProjectedMemberCount,
                sourceOutputEnvelope.ProjectedMemberCount);

            var destinationOutputEnvelope = await store.ReadEnvelopeAsync(
                response.DestinationMembershipFilePath);
            Assert.AreEqual(destinationEnvelope.Query, destinationOutputEnvelope.Query);
            Assert.IsTrue(
                response.SourceMembershipFilePath.Contains(
                    "SourceMembership-",
                    StringComparison.Ordinal));
            Assert.IsTrue(
                response.DestinationMembershipFilePath.Contains(
                    "DestinationMembership-",
                    StringComparison.Ordinal));
            store.Repository.Verify(
                repository => repository.DownloadFileAsync(It.IsAny<string>()),
                Times.Never);
            store.Repository.Verify(
                repository => repository.UploadFileAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<Dictionary<string, string>>()),
                Times.Never);
        }

        [TestMethod]
        public async Task ExtractMembershipAsync_WhenDestinationWriteFails_RemovesAttemptOutputs()
        {
            var store = new MembershipMergeTestStore
            {
                WriteFailure = (path, _) =>
                    path.Contains("DestinationMembership-", StringComparison.Ordinal)
                        ? new IOException("Simulated destination write failure.")
                        : null
            };
            var syncJob = CreateSyncJob();
            var sourceEnvelope = CreateEnvelope(syncJob, exclusionary: false);
            var destinationEnvelope = CreateEnvelope(syncJob, exclusionary: false);
            await store.AddCompressedAsync(
                "source",
                sourceEnvelope,
                User(ParseGuid(1), ParseGuid(101)));
            await store.AddCompressedAsync(
                "destination",
                destinationEnvelope,
                User(ParseGuid(2), Guid.Empty));

            var function = new MembershipExtractionFunction(
                NullLogger<MembershipExtractionFunction>.Instance,
                store.Repository.Object,
                Options.Create(new MembershipMergeOptions()));
            var response = await function.ExtractMembershipAsync(
                CreateRequest(syncJob, "source", "destination"));

            Assert.IsFalse(response.IsSuccessful);
            StringAssert.Contains(
                response.ErrorMessage,
                "Simulated destination write failure.");
            Assert.IsFalse(
                store.MembershipPaths.Any(path =>
                    path.Contains("SourceMembership-", StringComparison.Ordinal)
                    || path.Contains("DestinationMembership-", StringComparison.Ordinal)
                    || path.Contains(".intermediate.", StringComparison.Ordinal)));
            Assert.AreEqual(
                2,
                store.Operations.Count(operation =>
                    operation.StartsWith("delete:", StringComparison.Ordinal)));
        }

        [TestMethod]
        public async Task ExtractMembershipAsync_WithUnsortedSource_FailsBeforePublishing()
        {
            var store = new MembershipMergeTestStore();
            var syncJob = CreateSyncJob();
            var sourceEnvelope = CreateEnvelope(syncJob, exclusionary: false);
            store.AddRaw(
                "source",
                sourceEnvelope,
                User(ParseGuid(2), ParseGuid(101)),
                User(ParseGuid(1), ParseGuid(101)));

            var function = new MembershipExtractionFunction(
                NullLogger<MembershipExtractionFunction>.Instance,
                store.Repository.Object,
                Options.Create(new MembershipMergeOptions()));
            var response = await function.ExtractMembershipAsync(
                CreateRequest(syncJob, "source"));

            Assert.IsFalse(response.IsSuccessful);
            StringAssert.Contains(response.ErrorMessage, "canonical ObjectId order");
            Assert.IsFalse(
                store.MembershipPaths.Any(path =>
                    path.Contains("SourceMembership-", StringComparison.Ordinal)
                    || path.Contains(".intermediate.", StringComparison.Ordinal)));
        }

        [TestMethod]
        public async Task ExtractMembershipAsync_WhenCancelled_PropagatesCancellation()
        {
            var store = new MembershipMergeTestStore();
            var syncJob = CreateSyncJob();
            await store.AddCompressedAsync(
                "source",
                CreateEnvelope(syncJob, exclusionary: false),
                User(ParseGuid(1), ParseGuid(101)));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            var function = new MembershipExtractionFunction(
                NullLogger<MembershipExtractionFunction>.Instance,
                store.Repository.Object,
                Options.Create(new MembershipMergeOptions()));

            await Assert.ThrowsExceptionAsync<TaskCanceledException>(
                () => function.ExtractMembershipAsync(
                    CreateRequest(syncJob, "source"),
                    cancellation.Token));
        }

        [TestMethod]
        [Timeout(120000)]
        public async Task ExtractMembershipAsync_WithLargeHighFanInInput_BoundsOpenStreams()
        {
            const int sourcePartCount = 32;
            const int membersPerPart = 4000;
            var store = new MembershipMergeTestStore();
            var syncJob = CreateSyncJob();
            var completedParts = new List<string>(sourcePartCount);

            for (var part = 0; part < sourcePartCount; part++)
            {
                var path = $"source-{part:D2}";
                var sourceGroup = ParseGuid(1000 + part);
                var members = Enumerable
                    .Range(0, membersPerPart)
                    .Select(index => User(
                        ParseGuid(((long)part * membersPerPart) + index + 1),
                        sourceGroup))
                    .ToArray();
                store.AddRaw(
                    path,
                    CreateEnvelope(syncJob, exclusionary: false),
                    members);
                completedParts.Add(path);
            }

            var inputBytes = store.TotalMembershipBytes;
            Assert.IsTrue(
                inputBytes >= 10L * 1024 * 1024,
                $"Expected at least 10 MiB of input, but generated {inputBytes} bytes.");

            var function = new MembershipExtractionFunction(
                NullLogger<MembershipExtractionFunction>.Instance,
                store.Repository.Object,
                Options.Create(new MembershipMergeOptions()));
            var response = await function.ExtractMembershipAsync(
                CreateRequest(syncJob, completedParts.ToArray()));

            Assert.IsTrue(response.IsSuccessful, response.ErrorMessage);
            Assert.AreEqual(sourcePartCount * membersPerPart, response.SourceMemberCount);
            Assert.IsTrue(
                store.MaxActiveReaders <= 16,
                $"Observed {store.MaxActiveReaders} simultaneously open membership streams.");
        }

        private static MembershipExtractionRequest CreateRequest(
            SyncJob syncJob,
            params string[] completedParts)
        {
            var destinationPart = completedParts.FirstOrDefault(
                path => string.Equals(path, "destination", StringComparison.Ordinal));
            return new MembershipExtractionRequest
            {
                SyncJob = syncJob,
                GroupId = syncJob.TargetOfficeGroupId,
                CurrentPart = completedParts.Length,
                TotalParts = completedParts.Length,
                CompletedParts = completedParts.ToList(),
                DestinationPart = destinationPart,
                CurrentUtcDateTime = new DateTime(
                    2026,
                    8,
                    28,
                    12,
                    0,
                    0,
                    DateTimeKind.Utc)
            };
        }

        private static SyncJob CreateSyncJob() =>
            new SyncJob
            {
                Id = ParseGuid(9001),
                RunId = ParseGuid(9002),
                TargetOfficeGroupId = ParseGuid(9003)
            };

        private static GroupMembership CreateEnvelope(
            SyncJob syncJob,
            bool exclusionary) =>
            new GroupMembership
            {
                Destination = new AzureADGroup
                {
                    ObjectId = syncJob.TargetOfficeGroupId
                },
                RunId = syncJob.RunId.Value,
                SyncJobId = syncJob.Id,
                Exclusionary = exclusionary
            };

        private static AzureADUser User(Guid objectId, Guid sourceGroup) =>
            new AzureADUser
            {
                ObjectId = objectId,
                SourceGroup = sourceGroup
            };

        private static List<AzureADUser> OrderedMembers(
            params AzureADUser[] members) =>
            members
                .OrderBy(member => member.ObjectId, CanonicalObjectIdComparer.Instance)
                .ToList();

        private static List<AzureADUser> LegacyExtractionOracle(
            params (GroupMembership Envelope, List<AzureADUser> Members)[] inputs)
        {
            var toInclude = inputs
                .Where(input => !input.Envelope.Exclusionary)
                .SelectMany(input => input.Members)
                .ToList();
            var toExclude = inputs
                .Where(input => input.Envelope.Exclusionary)
                .SelectMany(input => input.Members)
                .ToList();
            var survivingObjectIds = new HashSet<Guid>(
                toInclude.Except(toExclude).Select(member => member.ObjectId));

            return inputs
                .SelectMany(input => input.Members)
                .GroupBy(member => member.ObjectId)
                .Where(group => survivingObjectIds.Contains(group.Key))
                .Select(group => new AzureADUser
                {
                    ObjectId = group.Key,
                    SourceGroups = group
                        .Select(member => member.SourceGroup)
                        .Distinct()
                        .OrderBy(
                            sourceGroup => sourceGroup,
                            CanonicalObjectIdComparer.Instance)
                        .ToList()
                })
                .OrderBy(member => member.ObjectId, CanonicalObjectIdComparer.Instance)
                .ToList();
        }

        private static void AssertMembersEqual(
            IReadOnlyList<AzureADUser> expected,
            IReadOnlyList<AzureADUser> actual)
        {
            Assert.AreEqual(expected.Count, actual.Count);

            for (var index = 0; index < expected.Count; index++)
            {
                Assert.AreEqual(expected[index].ObjectId, actual[index].ObjectId);
                CollectionAssert.AreEqual(
                    expected[index].SourceGroups,
                    actual[index].SourceGroups);
            }
        }

        private static string BuildReleasedLayout(
            GroupMembership envelope,
            IReadOnlyList<AzureADUser> members)
        {
            var destination = JsonSerializer.Serialize(
                envelope.Destination,
                _serializerOptions);
            var serializedMembers = JsonSerializer.Serialize(
                members,
                _serializerOptions);
            var query = JsonSerializer.Serialize(envelope.Query);

            return
                $"{{\"Destination\":{destination},\"SourceMembers\":{serializedMembers}," +
                $"\"RunId\":\"{envelope.RunId}\",\"SyncJobId\":\"{envelope.SyncJobId}\"," +
                $"\"Exclusionary\":{envelope.Exclusionary.ToString().ToLowerInvariant()}," +
                $"\"Query\":{query}}}";
        }

        private static Guid ParseGuid(long value) =>
            Guid.Parse($"00000000-0000-0000-0000-{value:x12}");
    }
}
