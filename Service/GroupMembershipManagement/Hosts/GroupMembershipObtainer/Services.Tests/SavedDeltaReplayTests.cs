// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.GroupMembershipObtainer;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Helpers;
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
    /// FR-019: replay-safe saved-delta processing. Covers the activity (no input deletion, write-once outputs,
    /// overlap rejection, FileTimestamp naming), the initial-path merge and uploaders, and the sub-orchestrator
    /// (finally deletion of the saved-delta inputs and one FileTimestamp per read attempt).
    /// </summary>
    [TestClass]
    public sealed class SavedDeltaReplayTests
    {
        private const int AlreadyWrittenEventId = 150149;
        private const int OverlapEventId = 150150;
        private static readonly DateTime FixedTimestamp =
            new DateTime(2024, 3, 5, 7, 9, 0, DateTimeKind.Utc);

        // ── ProcessCachedAndDeltaUsersFunction ──

        [TestMethod]
        public async Task ProcessCached_DoesNotDeleteSavedDeltaInputs()
        {
            var fixture = await CreateProcessFixtureAsync(identityTypes: null);

            var response = await fixture.Function.RunAsync(fixture.Request);

            Assert.IsTrue(response.CacheMatchesGroupCount);
            Assert.IsNotNull(fixture.Storage.GetContent(fixture.AddsPath), "The adds input was deleted.");
            Assert.IsNotNull(fixture.Storage.GetContent(fixture.RemovesPath), "The removes input was deleted.");
        }

        [TestMethod]
        public async Task ProcessCached_SequentialReplay_EqualCardinality_PublishesIdenticalBytes()
        {
            var fixture = await CreateProcessFixtureAsync(identityTypes: null, FixedTimestamp);

            var first = await fixture.Function.RunAsync(fixture.Request);
            var firstArtifacts = Capture(fixture, first);
            var second = await fixture.Function.RunAsync(fixture.Request);
            var secondArtifacts = Capture(fixture, second);

            Assert.AreEqual(first.MembershipFilePath, second.MembershipFilePath);
            Assert.AreEqual(first.CacheCount, second.CacheCount);
            CollectionAssert.AreEqual(firstArtifacts, secondArtifacts);
            CollectionAssert.AreEquivalent(
                new[] { fixture.RetainedMember, fixture.AddedMember },
                ReadMembership(firstArtifacts[0]).SourceMembers.Select(member => member.ObjectId).ToArray());
            Assert.AreEqual(1, WritesTo(fixture.Storage, first.MembershipFilePath));
            Assert.AreEqual(1, WritesTo(fixture.Storage, fixture.CachePath));
            Assert.AreEqual(1, WritesTo(fixture.Storage, fixture.TokenPath));
        }

        [TestMethod]
        public async Task ProcessCached_ConcurrentDuplicate_BothSucceedAndOneWriterPublishes()
        {
            var fixture = await CreateProcessFixtureAsync(identityTypes: null, FixedTimestamp, NullLogger<ProcessCachedAndDeltaUsersFunction>.Instance);
            var partPath = fixture.PartPath;
            var arrivals = 0;
            var bothArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Storage.BeforeWriteAsync = async path =>
            {
                if (string.Equals(path, partPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (Interlocked.Increment(ref arrivals) == 2)
                    {
                        bothArrived.TrySetResult();
                    }

                    await bothArrived.Task;
                }
            };

            var responses = await Task.WhenAll(
                fixture.Function.RunAsync(fixture.Request),
                fixture.Function.RunAsync(fixture.Request));

            Assert.AreEqual(2, arrivals, "Both executions must reach the part write before either commits.");
            Assert.IsTrue(responses.All(response => response.CacheMatchesGroupCount));
            Assert.AreEqual(responses[0].MembershipFilePath, responses[1].MembershipFilePath);
            Assert.AreEqual(1, WritesTo(fixture.Storage, partPath));
            Assert.AreEqual(1, WritesTo(fixture.Storage, fixture.CachePath));
            Assert.AreEqual(1, WritesTo(fixture.Storage, fixture.TokenPath));
            Assert.AreEqual(
                3,
                fixture.Storage.Operations.Count(operation => operation.StartsWith("write-rejected:", StringComparison.Ordinal)));
        }

        [TestMethod]
        public async Task ProcessCached_StragglerAfterInputDeletion_CannotOverwriteCacheOrToken()
        {
            var fixture = await CreateProcessFixtureAsync(identityTypes: null, FixedTimestamp);
            var first = await fixture.Function.RunAsync(fixture.Request);
            var published = Capture(fixture, first);

            // The sub-orchestrator deletes the inputs after the first completion; a straggling duplicate
            // then sees only the original cache, whose count still equals the group count.
            await fixture.Storage.DeleteFilesAsync(fixture.AddsPrefix);
            await fixture.Storage.DeleteFilesAsync(fixture.RemovesPrefix);
            var straggler = await fixture.Function.RunAsync(fixture.Request);

            Assert.IsTrue(straggler.CacheMatchesGroupCount, "The straggler models the equal-cardinality stale read.");
            CollectionAssert.AreEqual(published, Capture(fixture, first));
            Assert.AreEqual(1, WritesTo(fixture.Storage, fixture.CachePath));
            Assert.AreEqual(1, WritesTo(fixture.Storage, fixture.TokenPath));
            Assert.AreEqual(1, WritesTo(fixture.Storage, first.MembershipFilePath));
        }

        [TestMethod]
        public async Task ProcessCached_WriteOnceRejection_IsLoggedAtInformationAndTreatedAsSuccess()
        {
            var logger = new RecordingLogger<ProcessCachedAndDeltaUsersFunction>();
            var fixture = await CreateProcessFixtureAsync(identityTypes: null, FixedTimestamp, logger);
            await fixture.Storage.UploadFileAsync(fixture.PartPath, "existing-part");
            await fixture.Storage.UploadFileAsync(fixture.CachePath, "existing-cache");
            await fixture.Storage.UploadFileAsync(fixture.TokenPath, "existing-token");

            var response = await fixture.Function.RunAsync(fixture.Request);

            Assert.IsTrue(response.CacheMatchesGroupCount);
            Assert.AreEqual(fixture.PartPath, response.MembershipFilePath.TrimStart('/'));
            Assert.AreEqual("existing-part", fixture.Storage.GetContent(fixture.PartPath));
            Assert.AreEqual("existing-cache", fixture.Storage.GetContent(fixture.CachePath));
            Assert.AreEqual("existing-token", fixture.Storage.GetContent(fixture.TokenPath));
            var rejections = logger.Entries
                .Where(entry => entry.EventId.Id == AlreadyWrittenEventId)
                .ToList();
            Assert.AreEqual(3, rejections.Count);
            Assert.IsTrue(rejections.All(entry => entry.Level == LogLevel.Information));
            Assert.IsFalse(logger.Entries.Any(entry => entry.Level >= LogLevel.Warning));
        }

        [TestMethod]
        public async Task ProcessCached_CacheRejected_TokenWriteIsAlsoWriteOnce()
        {
            var fixture = await CreateProcessFixtureAsync(identityTypes: null, FixedTimestamp);
            await fixture.Storage.UploadFileAsync(fixture.CachePath, "winner-cache");
            await fixture.Storage.UploadFileAsync(fixture.TokenPath, "winner-token");

            await fixture.Function.RunAsync(fixture.Request);

            Assert.AreEqual("winner-cache", fixture.Storage.GetContent(fixture.CachePath));
            Assert.AreEqual(
                "winner-token",
                fixture.Storage.GetContent(fixture.TokenPath),
                "A losing writer paired its token with the winner's cache.");
        }

        [DataTestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public async Task ProcessCached_OverlappingAddAndRemove_ReportsMismatchAndPublishesNothing(bool withTimestamp)
        {
            var logger = new RecordingLogger<ProcessCachedAndDeltaUsersFunction>();
            var overlapping = Guid.NewGuid();
            var retained = Guid.NewGuid();
            var offsetting = Guid.NewGuid();
            var fixture = await CreateProcessFixtureAsync(
                identityTypes: null,
                withTimestamp ? FixedTimestamp : null,
                logger,
                cached: new[] { overlapping, retained },
                adds: new[] { overlapping, offsetting },
                removes: new[] { overlapping },
                expectedCount: 2);
            var pathsBefore = fixture.Storage.Paths.ToArray();

            var response = await fixture.Function.RunAsync(fixture.Request);

            Assert.IsFalse(response.CacheMatchesGroupCount);
            Assert.AreEqual(string.Empty, response.MembershipFilePath);
            Assert.IsTrue(string.IsNullOrEmpty(response.StagingPath));
            CollectionAssert.AreEquivalent(pathsBefore, fixture.Storage.Paths.ToArray(), "An artifact was published.");
            Assert.AreEqual(1, logger.Entries.Count(entry => entry.EventId.Id == OverlapEventId));
        }

        [TestMethod]
        public async Task ProcessCached_FileTimestamp_NamesPartCacheAndToken()
        {
            var fixture = await CreateProcessFixtureAsync(identityTypes: null, FixedTimestamp);

            var response = await fixture.Function.RunAsync(fixture.Request);

            Assert.AreEqual(
                $"{fixture.Request.TargetGroupId}/03052024-0709_{fixture.Request.SyncJob.RunId}_GroupMembership_1.json",
                response.MembershipFilePath);
            Assert.AreEqual($"cache/{fixture.Request.SourceGroupId}_03052024-0709.txt", fixture.CachePath);
            Assert.IsNotNull(fixture.Storage.GetContent(response.MembershipFilePath));
            Assert.IsNotNull(fixture.Storage.GetContent(fixture.CachePath));
            Assert.AreEqual("replacement-delta-token", fixture.Storage.GetContent(fixture.TokenPath));
        }

        [TestMethod]
        public async Task ProcessCached_OneTypeWithTimestamp_WritesStagingOnceAndReturnsTimestampedPartPath()
        {
            var fixture = await CreateProcessFixtureAsync(new[] { "users" }, FixedTimestamp, writeStaging: true);

            var first = await fixture.Function.RunAsync(fixture.Request);
            var staging = fixture.Storage.GetContent(first.StagingPath);
            await fixture.Storage.DeleteFilesAsync(fixture.AddsPrefix);
            await fixture.Storage.DeleteFilesAsync(fixture.RemovesPrefix);
            var straggler = await fixture.Function.RunAsync(fixture.Request);

            Assert.AreEqual(fixture.PartPath, first.MembershipFilePath);
            Assert.IsNull(fixture.Storage.GetContent(first.MembershipFilePath), "One-type parts are written by selection.");
            Assert.AreEqual(first.StagingPath, straggler.StagingPath);
            Assert.AreEqual(staging, fixture.Storage.GetContent(first.StagingPath));
            Assert.AreEqual(1, WritesTo(fixture.Storage, first.StagingPath));
        }

        [TestMethod]
        public async Task ProcessCached_BothTypesWriteOnceBytes_MatchLegacyOverwritingPath()
        {
            var sourceGroupId = Guid.NewGuid();
            var targetGroupId = Guid.NewGuid();
            var runId = Guid.NewGuid();
            var syncJobId = Guid.NewGuid();
            var retained = Guid.NewGuid();
            var removed = Guid.NewGuid();
            var added = Guid.NewGuid();
            var legacy = await CreateProcessFixtureAsync(
                identityTypes: null,
                fileTimestamp: null,
                cached: new[] { retained, removed },
                adds: new[] { added },
                removes: new[] { removed },
                sourceGroupId: sourceGroupId,
                targetGroupId: targetGroupId,
                runId: runId,
                syncJobId: syncJobId);
            var writeOnce = await CreateProcessFixtureAsync(
                identityTypes: null,
                FixedTimestamp,
                cached: new[] { retained, removed },
                adds: new[] { added },
                removes: new[] { removed },
                sourceGroupId: sourceGroupId,
                targetGroupId: targetGroupId,
                runId: runId,
                syncJobId: syncJobId);

            var legacyResponse = await legacy.Function.RunAsync(legacy.Request);
            var writeOnceResponse = await writeOnce.Function.RunAsync(writeOnce.Request);

            Assert.AreEqual(legacyResponse.CacheCount, writeOnceResponse.CacheCount);
            CollectionAssert.AreEqual(
                CaptureLatest(legacy.Storage, legacyResponse.MembershipFilePath, sourceGroupId),
                Capture(writeOnce, writeOnceResponse));
            CollectionAssert.AreEqual(
                legacy.Storage.GetBlobMetadataAsync(LatestCachePath(legacy.Storage, sourceGroupId)).Result.Metadata.ToArray(),
                writeOnce.Storage.GetBlobMetadataAsync(writeOnce.CachePath).Result.Metadata.ToArray());
        }

        // ── Initial-path merge and uploaders (SGMembershipCalculator) ──

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task TransitiveAndDeltaUsersSender_RerunAfterPageCleanup_KeepsBytesAndCount(bool writeStaging)
        {
            var fixture = await CreateSenderFixtureAsync(writeStaging);

            var first = await fixture.Sender.SendUsersAsync(fixture.Request);
            var outputPath = writeStaging
                ? GroupPartStoragePaths.BuildStagingPath(fixture.TargetGroupId, fixture.RunId, 1)
                : first.FilePath;
            var published = fixture.Storage.GetContent(outputPath);
            await fixture.Storage.DeleteBlobsAsync($"{fixture.TargetGroupId}/userUploads/{fixture.RunId}_GroupMembership_1_");
            var second = await fixture.Sender.SendUsersAsync(fixture.Request);

            Assert.AreEqual(2, first.MemberCount);
            Assert.AreEqual(first.MemberCount, second.MemberCount, "The rerun reported the empty listing's count.");
            Assert.AreEqual(first.FilePath, second.FilePath);
            Assert.AreEqual(published, fixture.Storage.GetContent(outputPath));
            Assert.AreEqual(2, ReadMembership(published).SourceMembers.Count);
            Assert.AreEqual(1, WritesTo(fixture.Storage, outputPath));
        }

        [TestMethod]
        public async Task TransitiveAndDeltaUsersSender_FullReadRerunInLaterMinute_LeavesExactlyOnePartFile()
        {
            var fixture = await CreateSenderFixtureAsync(writeStaging: false);
            fixture.Request.ReadKind = GroupMembershipReadKind.Full;

            var first = await fixture.Sender.SendUsersAsync(fixture.Request);
            // The re-run executes later on the wall clock but carries the same replay-safe FileTimestamp input.
            await Task.Delay(TimeSpan.FromMilliseconds(5));
            var second = await fixture.Sender.SendUsersAsync(fixture.Request);

            var partFiles = fixture.Storage.Paths
                .Where(path => path.EndsWith(
                    $"_{fixture.RunId}_GroupMembership_1.json",
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            Assert.AreEqual(1, partFiles.Length, string.Join(", ", partFiles));
            Assert.AreEqual(first.FilePath, second.FilePath);
            Assert.AreEqual(1, WritesTo(fixture.Storage, first.FilePath));
        }

        [TestMethod]
        public async Task TransitiveAndDeltaUsersSender_FileTimestamp_NamesPartFile()
        {
            var fixture = await CreateSenderFixtureAsync(writeStaging: false);

            var result = await fixture.Sender.SendUsersAsync(fixture.Request);

            Assert.AreEqual(
                $"/{fixture.TargetGroupId}/03052024-0709_{fixture.RunId}_GroupMembership_1.json",
                result.FilePath);
            Assert.IsNotNull(fixture.Storage.GetContent(result.FilePath));
        }

        [TestMethod]
        public async Task CacheAndDeltaLinkUploaders_FileTimestamp_WriteOnceWithTimestampNames()
        {
            var fixture = await CreateSenderFixtureAsync(writeStaging: false);
            var part = await fixture.Sender.SendUsersAsync(fixture.Request);
            var cacheUploader = new CacheUploaderFunction(
                NullLogger<CacheUploaderFunction>.Instance,
                fixture.Calculator);
            var deltaLinkUploader = new DeltaLinkUploaderFunction(
                NullLogger<DeltaLinkUploaderFunction>.Instance,
                fixture.Calculator);
            var cacheRequest = new CacheUploaderRequest
            {
                SyncJob = fixture.Request.SyncJob,
                ObjectId = fixture.SourceGroupId,
                CurrentPart = 1,
                TotalParts = 1,
                MembershipFileResult = part,
                FileTimestamp = FixedTimestamp
            };
            var deltaRequest = new DeltaLinkUploaderRequest
            {
                SyncJob = fixture.Request.SyncJob,
                ObjectId = fixture.SourceGroupId,
                CurrentPart = 1,
                TotalParts = 1,
                DeltaLink = "first-token",
                FileTimestamp = FixedTimestamp
            };
            var cachePath = $"cache/{fixture.SourceGroupId}_03052024-0709.txt";
            var tokenPath = $"cache/delta_{fixture.SourceGroupId}_03052024-0709.json";

            await cacheUploader.SendUsersAsync(cacheRequest);
            await deltaLinkUploader.SendDeltaLinkAsync(deltaRequest);
            var cache = fixture.Storage.GetContent(cachePath);
            await fixture.Storage.UploadFileAsync(part.FilePath, JsonSerializer.Serialize(new GroupMembership()));
            deltaRequest.DeltaLink = "straggler-token";
            await cacheUploader.SendUsersAsync(cacheRequest);
            await deltaLinkUploader.SendDeltaLinkAsync(deltaRequest);

            Assert.IsNotNull(cache);
            Assert.AreEqual(2, cache.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length);
            Assert.AreEqual(cache, fixture.Storage.GetContent(cachePath));
            Assert.AreEqual("first-token", fixture.Storage.GetContent(tokenPath));
            Assert.AreEqual("2", fixture.Storage.GetBlobMetadataAsync(cachePath).Result.Metadata["NumberOfUsers"]);
        }

        // ── DeleteBlobFunction ──

        [TestMethod]
        public async Task DeleteBlob_WithPrefixes_DeletesOnlyThosePrefixes()
        {
            var storage = new InMemoryBlobStorageRepository();
            var groupId = Guid.NewGuid();
            var runId = Guid.NewGuid();
            var adds = $"{groupId}/userUploads/deltaLink/adds/{runId}_GroupMembership_1_";
            var removes = $"{groupId}/userUploads/deltaLink/removes/{runId}_GroupMembership_1_";
            var pages = $"{groupId}/userUploads/{runId}_GroupMembership_1_";
            await storage.UploadFileAsync($"/{adds}a.json", "[]");
            await storage.UploadFileAsync($"/{removes}r.json", "[]");
            await storage.UploadFileAsync($"/{pages}p.json", "[]");
            var function = new DeleteBlobFunction(NullLogger<DeleteBlobFunction>.Instance, storage);

            await function.DeleteAsync(new DeleteBlobRequest
            {
                GroupId = groupId,
                CurrentPart = 1,
                TotalParts = 1,
                SyncJob = new SyncJob { RunId = runId },
                Prefixes = new List<string> { adds, removes }
            });

            CollectionAssert.AreEqual(new[] { $"{pages}p.json" }, storage.Paths.ToArray());

            await function.DeleteAsync(new DeleteBlobRequest
            {
                GroupId = groupId,
                CurrentPart = 1,
                TotalParts = 1,
                SyncJob = new SyncJob { RunId = runId }
            });

            Assert.AreEqual(0, storage.Paths.Count, "Without prefixes the legacy page-upload prefix is deleted.");
        }

        // ── SubOrchestratorFunction ──

        [TestMethod]
        public async Task SubOrchestrator_SavedDeltaMatch_DeletesInputsOnceAfterProcessing()
        {
            var harness = CreateSubOrchestratorHarness(cacheMatches: true);

            await harness.Function.RunSubOrchestratorAsync(harness.Context.Object);

            Assert.AreEqual(1, harness.DeleteRequests.Count);
            CollectionAssert.AreEqual(harness.ExpectedPrefixes, harness.DeleteRequests[0].Prefixes);
            Assert.IsTrue(
                harness.Sequence.IndexOf(nameof(DeleteBlobFunction)) >
                harness.Sequence.IndexOf(nameof(ProcessCachedAndDeltaUsersFunction)));
            Assert.AreEqual(Truncate(harness.Now), harness.ProcessRequests.Single().FileTimestamp);
        }

        [TestMethod]
        public async Task SubOrchestrator_ProcessCachedFailsWithTaskFailedException_FinallyDeletesInputs()
        {
            var harness = CreateSubOrchestratorHarness(cacheMatches: true, failProcess: true);

            var failure = await Assert.ThrowsExceptionAsync<TaskFailedException>(
                () => harness.Function.RunSubOrchestratorAsync(harness.Context.Object));

            Assert.AreEqual(nameof(ProcessCachedAndDeltaUsersFunction), failure.TaskName);
            Assert.AreEqual(1, harness.DeleteRequests.Count);
            CollectionAssert.AreEqual(harness.ExpectedPrefixes, harness.DeleteRequests[0].Prefixes);
        }

        [TestMethod]
        public async Task SubOrchestrator_DeltaLinkReaderFailsWithTaskFailedException_FinallyDeletesInputs()
        {
            var harness = CreateSubOrchestratorHarness(cacheMatches: true, failReader: true);

            await Assert.ThrowsExceptionAsync<TaskFailedException>(
                () => harness.Function.RunSubOrchestratorAsync(harness.Context.Object));

            Assert.AreEqual(1, harness.DeleteRequests.Count);
            CollectionAssert.AreEqual(harness.ExpectedPrefixes, harness.DeleteRequests[0].Prefixes);
        }

        [TestMethod]
        public async Task SubOrchestrator_InputDeletionFails_OriginalExceptionIsKept()
        {
            var harness = CreateSubOrchestratorHarness(cacheMatches: true, failProcess: true, failDelete: true);

            var failure = await Assert.ThrowsExceptionAsync<TaskFailedException>(
                () => harness.Function.RunSubOrchestratorAsync(harness.Context.Object));

            Assert.AreEqual(nameof(ProcessCachedAndDeltaUsersFunction), failure.TaskName);
        }

        [TestMethod]
        public async Task SubOrchestrator_InputDeletionFailsAfterSuccess_ReadStillCompletes()
        {
            var harness = CreateSubOrchestratorHarness(cacheMatches: true, failDelete: true);

            var response = await harness.Function.RunSubOrchestratorAsync(harness.Context.Object);

            Assert.AreEqual("delta-part.json", response.FilePath);
        }

        [TestMethod]
        public async Task SubOrchestrator_CacheMismatch_DeletesInputsAndGivesRebuildADistinctTimestamp()
        {
            var harness = CreateSubOrchestratorHarness(cacheMatches: false);

            await harness.Function.RunSubOrchestratorAsync(harness.Context.Object);

            Assert.AreEqual(1, harness.DeleteRequests.Count(request => request.Prefixes?.Count > 0));
            var processTimestamp = harness.ProcessRequests.Single().FileTimestamp;
            var senderTimestamp = harness.SenderRequests.Single().FileTimestamp;
            Assert.AreEqual(Truncate(harness.Now), processTimestamp);
            Assert.IsNotNull(senderTimestamp);
            Assert.IsTrue(senderTimestamp > processTimestamp, "Two read attempts in one minute shared a timestamp.");
            Assert.AreEqual(senderTimestamp, harness.CacheUploaderRequests.Single().FileTimestamp);
            Assert.AreEqual(senderTimestamp, harness.DeltaLinkUploaderRequests.Single().FileTimestamp);
        }

        [TestMethod]
        public async Task SubOrchestrator_LegacyJsonCache_ConversionAndProcessingGetDistinctTimestamps()
        {
            var harness = CreateSubOrchestratorHarness(cacheMatches: true, legacyJsonCache: true);

            await harness.Function.RunSubOrchestratorAsync(harness.Context.Object);

            var conversion = harness.ConverterRequests.Single().FileTimestamp;
            var processing = harness.ProcessRequests.Single().FileTimestamp;
            Assert.IsNotNull(conversion);
            Assert.IsTrue(processing > conversion, "The converted cache name could reject the updated cache.");
        }

        [TestMethod]
        public async Task SubOrchestrator_FullRead_PassesOneFileTimestampToTheMerge()
        {
            var harness = CreateSubOrchestratorHarness(cacheMatches: true, fullRead: true);

            await harness.Function.RunSubOrchestratorAsync(harness.Context.Object);

            Assert.AreEqual(Truncate(harness.Now), harness.SenderRequests.Single().FileTimestamp);
            Assert.IsFalse(harness.Sequence.Contains(nameof(ProcessCachedAndDeltaUsersFunction)));
            Assert.IsFalse(harness.DeleteRequests.Any(request => request.Prefixes?.Count > 0));
        }

        [TestMethod]
        public async Task SubOrchestrator_ReplayingTheSameHistory_ProducesIdenticalTimestamps()
        {
            var times = new[]
            {
                new DateTime(2024, 3, 5, 7, 9, 10, DateTimeKind.Utc),
                new DateTime(2024, 3, 5, 7, 9, 50, DateTimeKind.Utc),
                new DateTime(2024, 3, 5, 7, 12, 30, DateTimeKind.Utc)
            };
            var expected = new DateTime?[]
            {
                new DateTime(2024, 3, 5, 7, 9, 0, DateTimeKind.Utc),
                new DateTime(2024, 3, 5, 7, 10, 0, DateTimeKind.Utc),
                new DateTime(2024, 3, 5, 7, 12, 0, DateTimeKind.Utc)
            };
            var episodes = new List<DateTime?[]>();

            // Two invocations with fresh mocks model two replay episodes of the same history.
            for (var episode = 0; episode < 2; episode++)
            {
                var harness = CreateSubOrchestratorHarness(
                    cacheMatches: false,
                    legacyJsonCache: true,
                    times: times);
                await harness.Function.RunSubOrchestratorAsync(harness.Context.Object);
                episodes.Add(AttemptTimestamps(harness, 0));
            }

            CollectionAssert.AreEqual(expected, episodes[0]);
            CollectionAssert.AreEqual(episodes[0], episodes[1]);
        }

        [TestMethod]
        public async Task SubOrchestrator_SameContextReusedForTwoRuns_DoesNotLeakTimestampState()
        {
            var harness = CreateSubOrchestratorHarness(cacheMatches: false, legacyJsonCache: true);

            await harness.Function.RunSubOrchestratorAsync(harness.Context.Object);
            var first = AttemptTimestamps(harness, 0);
            await harness.Function.RunSubOrchestratorAsync(harness.Context.Object);
            var second = AttemptTimestamps(harness, 1);

            var minute = Truncate(harness.Now);
            CollectionAssert.AreEqual(
                new DateTime?[] { minute, minute.AddMinutes(1), minute.AddMinutes(2) },
                first);
            CollectionAssert.AreEqual(first, second, "Timestamp state leaked from the first run into the second.");
        }

        private static DateTime?[] AttemptTimestamps(SubOrchestratorHarness harness, int run) =>
            new[]
            {
                harness.ConverterRequests[run].FileTimestamp,
                harness.ProcessRequests[run].FileTimestamp,
                harness.SenderRequests[run].FileTimestamp
            };

        // ── Helpers ──

        private static async Task<ProcessFixture> CreateProcessFixtureAsync(
            IReadOnlyCollection<string> identityTypes,
            DateTime? fileTimestamp = null,
            ILogger<ProcessCachedAndDeltaUsersFunction> logger = null,
            IReadOnlyCollection<Guid> cached = null,
            IReadOnlyCollection<Guid> adds = null,
            IReadOnlyCollection<Guid> removes = null,
            int expectedCount = 2,
            bool writeStaging = false,
            Guid? sourceGroupId = null,
            Guid? targetGroupId = null,
            Guid? runId = null,
            Guid? syncJobId = null)
        {
            var retained = Guid.NewGuid();
            var removed = Guid.NewGuid();
            var added = Guid.NewGuid();
            cached ??= new[] { retained, removed };
            adds ??= new[] { added };
            removes ??= new[] { removed };
            var storage = new InMemoryBlobStorageRepository();
            var source = sourceGroupId ?? Guid.NewGuid();
            var target = targetGroupId ?? Guid.NewGuid();
            var run = runId ?? Guid.NewGuid();
            var originalCache = $"cache/original-{source}.txt";
            await storage.UploadFileAsync(
                originalCache,
                string.Join(Environment.NewLine, cached) + Environment.NewLine);
            var addsPrefix = $"{target}/userUploads/deltaLink/adds/{run}_GroupMembership_1_";
            var removesPrefix = $"{target}/userUploads/deltaLink/removes/{run}_GroupMembership_1_";
            var addsPath = $"{addsPrefix}page.json";
            var removesPath = $"{removesPrefix}page.json";
            await storage.UploadFileAsync(
                "/" + addsPath,
                JsonSerializer.Serialize(adds.Select(id => new AzureADUser { ObjectId = id }).ToArray()));
            await storage.UploadFileAsync(
                "/" + removesPath,
                JsonSerializer.Serialize(removes.Select(id => new AzureADUser { ObjectId = id }).ToArray()));
            var request = new ProcessCachedAndDeltaUsersRequest
            {
                SyncJob = new SyncJob
                {
                    Id = syncJobId ?? Guid.NewGuid(),
                    RunId = run,
                    Query = "[{\"type\":\"GroupMembership\"}]"
                },
                SourceGroupId = source,
                TargetGroupId = target,
                CacheFilePath = originalCache,
                DeltaUrl = "replacement-delta-token",
                CountOfUsersFromAADGroup = expectedCount,
                CurrentPart = 1,
                TotalParts = 1,
                IdentityTypes = identityTypes?.ToList(),
                WriteStaging = writeStaging,
                ReadKind = GroupMembershipReadKind.DeltaLink,
                FileTimestamp = fileTimestamp
            };
            var function = new ProcessCachedAndDeltaUsersFunction(
                logger ?? NullLogger<ProcessCachedAndDeltaUsersFunction>.Instance,
                CreateCalculator(storage),
                storage);
            var stamp = (fileTimestamp ?? FixedTimestamp).ToString("MMddyyyy-HHmm");
            return new ProcessFixture(
                function,
                storage,
                request,
                retained,
                added,
                addsPrefix,
                removesPrefix,
                addsPath,
                removesPath,
                $"{target}/{stamp}_{run}_GroupMembership_1.json",
                CacheFileNaming.BuildCacheFileName(source, fileTimestamp ?? FixedTimestamp),
                $"cache/delta_{source}_{stamp}.json");
        }

        private static async Task<SenderFixture> CreateSenderFixtureAsync(bool writeStaging)
        {
            var storage = new InMemoryBlobStorageRepository();
            var sourceGroupId = Guid.NewGuid();
            var targetGroupId = Guid.NewGuid();
            var runId = Guid.NewGuid();
            var job = new SyncJob
            {
                Id = Guid.NewGuid(),
                RunId = runId,
                TargetOfficeGroupId = targetGroupId,
                Query = "[{\"type\":\"GroupMembership\"}]"
            };
            await storage.UploadFileAsync(
                $"/{targetGroupId}/userUploads/{runId}_GroupMembership_1_{Guid.NewGuid()}.json",
                JsonSerializer.Serialize(new[]
                {
                    new AzureADUser { ObjectId = Guid.NewGuid() },
                    new AzureADUser { ObjectId = Guid.NewGuid() }
                }));
            var destinationResolver = new Mock<IDestinationResolver>();
            destinationResolver
                .Setup(resolver => resolver.ResolveAsync(job, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ResolvedGroupDestination { ObjectId = targetGroupId });
            var calculator = CreateCalculator(storage, destinationResolver.Object);
            var sender = new TransitiveAndDeltaUsersSenderFunction(
                NullLogger<TransitiveAndDeltaUsersSenderFunction>.Instance,
                storage,
                calculator);
            var request = new TransitiveAndDeltaUsersSenderRequest
            {
                SyncJob = job,
                ObjectId = sourceGroupId,
                GroupId = targetGroupId,
                CurrentPart = 1,
                TotalParts = 1,
                IdentityTypes = writeStaging ? new List<string> { "users" } : null,
                WriteStaging = writeStaging,
                ReadKind = GroupMembershipReadKind.DeltaInitial,
                FileTimestamp = FixedTimestamp
            };
            return new SenderFixture(storage, calculator, sender, request, sourceGroupId, targetGroupId, runId);
        }

        private static SGMembershipCalculator CreateCalculator(
            IBlobStorageRepository storage,
            IDestinationResolver destinationResolver = null) =>
            new SGMembershipCalculator(
                Mock.Of<IGraphGroupRepository>(),
                storage,
                Mock.Of<IDatabaseSyncJobsRepository>(),
                destinationResolver ?? Mock.Of<IDestinationResolver>(),
                Mock.Of<IServiceBusQueueRepository>(),
                Mock.Of<IDatabaseDestinationAttributesRepository>(),
                NullLogger<SGMembershipCalculator>.Instance,
                Mock.Of<IDryRunValue>(),
                Mock.Of<ISyncJobStatusService>());

        private static string[] Capture(ProcessFixture fixture, ProcessCachedAndDeltaUsersResponse response) =>
            new[]
            {
                fixture.Storage.GetContent(response.MembershipFilePath),
                fixture.Storage.GetContent(fixture.CachePath),
                fixture.Storage.GetContent(fixture.TokenPath)
            };

        private static string[] CaptureLatest(
            InMemoryBlobStorageRepository storage,
            string membershipPath,
            Guid sourceGroupId) =>
            new[]
            {
                storage.GetContent(membershipPath),
                storage.GetContent(LatestCachePath(storage, sourceGroupId)),
                storage.GetContent(storage.FindLatestFileAsync($"cache/delta_{sourceGroupId}_").Result.Path)
            };

        private static string LatestCachePath(InMemoryBlobStorageRepository storage, Guid sourceGroupId) =>
            storage.FindLatestFileAsync($"cache/{sourceGroupId}_").Result.Path;

        private static int WritesTo(InMemoryBlobStorageRepository storage, string path) =>
            storage.WriteHistory.Count(write =>
                string.Equals(write.Path, path.TrimStart('/'), StringComparison.OrdinalIgnoreCase));

        private static GroupMembership ReadMembership(string content) =>
            JsonSerializer.Deserialize<GroupMembership>(
                content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        private static DateTime Truncate(DateTime value) =>
            new DateTime(value.Ticks - value.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc);

        private static SubOrchestratorHarness CreateSubOrchestratorHarness(
            bool cacheMatches,
            bool failProcess = false,
            bool failReader = false,
            bool failDelete = false,
            bool legacyJsonCache = false,
            bool fullRead = false,
            IReadOnlyList<DateTime> times = null)
        {
            var request = new GroupMembershipRequest
            {
                SyncJob = new SyncJob
                {
                    Id = Guid.NewGuid(),
                    RunId = Guid.NewGuid(),
                    Query = "[{\"type\":\"GroupMembership\"}]"
                },
                SourceGroup = new AzureADGroup { ObjectId = Guid.NewGuid() },
                GroupId = Guid.NewGuid(),
                CurrentPart = 1,
                TotalParts = 1
            };
            var harness = new SubOrchestratorHarness
            {
                Now = new DateTime(2024, 3, 5, 7, 9, 41, DateTimeKind.Utc),
                ExpectedPrefixes = new List<string>
                {
                    $"{request.GroupId}/userUploads/deltaLink/adds/{request.SyncJob.RunId}_GroupMembership_1_",
                    $"{request.GroupId}/userUploads/deltaLink/removes/{request.SyncJob.RunId}_GroupMembership_1_"
                },
                Function = new SubOrchestratorFunction(
                    new FixedDeltaCachingConfig(),
                    new TelemetryClient(new TelemetryConfiguration()))
            };
            var context = new Mock<TaskOrchestrationContext>();
            harness.Context = context;
            context.Setup(c => c.GetInput<GroupMembershipRequest>()).Returns(request);
            context.Setup(c => c.CreateReplaySafeLogger(It.IsAny<string>())).Returns(NullLogger.Instance);
            context.SetupGet(c => c.IsReplaying).Returns(false);
            var timeReads = 0;
            context.SetupGet(c => c.CurrentUtcDateTime).Returns(() =>
                times == null ? harness.Now : times[Math.Min(timeReads++, times.Count - 1)]);
            SetupResult(harness, nameof(GroupValidatorFunction), true);
            SetupResult(harness, nameof(GetTransitiveGroupCountFunction), fullRead ? 1 : 0);
            SetupResult(harness, nameof(FileDownloaderFunction), TextCompressor.Compress("saved-delta-link"));
            SetupResult(harness, nameof(BlobCheckerFunction), new BlobResult
            {
                BlobStatus = BlobStatus.Found,
                Path = legacyJsonCache ? "cache/original.json" : "cache/original.txt"
            });
            SetupResult(harness, nameof(GetUserCountFunction), 2);
            SetupResult(harness, nameof(GetAgentUserCountFunction), 0);
            SetupResult(harness, nameof(DeltaUserReaderFunction), new DeltaUrls { DeltaUrl = "initial-token" });
            SetupResult(harness, nameof(TransitiveAndDeltaUsersSenderFunction), new GroupMembershipFileResult
            {
                FilePath = "initial-part.json",
                MemberCount = 2
            });
            context
                .Setup(c => c.CallActivityAsync<DeltaUrls>(
                    It.Is<TaskName>(name => name.Name == nameof(DeltaLinkUserReaderFunction)),
                    It.IsAny<object>(),
                    It.IsAny<TaskOptions>()))
                .Returns<TaskName, object, TaskOptions>((name, _, _) =>
                {
                    harness.Sequence.Add(name.Name);
                    return failReader
                        ? ActivityCallWrapper.InvokeAsync<DeltaUrls>(
                            name.Name,
                            () => Task.FromException<DeltaUrls>(new InvalidOperationException("Injected reader failure.")))
                        : Task.FromResult(new DeltaUrls { DeltaUrl = "replacement-token" });
                });
            context
                .Setup(c => c.CallActivityAsync<ProcessCachedAndDeltaUsersResponse>(
                    It.Is<TaskName>(name => name.Name == nameof(ProcessCachedAndDeltaUsersFunction)),
                    It.IsAny<object>(),
                    It.IsAny<TaskOptions>()))
                .Returns<TaskName, object, TaskOptions>((name, input, _) =>
                {
                    harness.Sequence.Add(name.Name);
                    harness.ProcessRequests.Add((ProcessCachedAndDeltaUsersRequest)input);
                    return failProcess
                        ? ActivityCallWrapper.InvokeAsync<ProcessCachedAndDeltaUsersResponse>(
                            name.Name,
                            () => Task.FromException<ProcessCachedAndDeltaUsersResponse>(
                                new InvalidOperationException("Injected processing failure.")))
                        : Task.FromResult(new ProcessCachedAndDeltaUsersResponse
                        {
                            CacheMatchesGroupCount = cacheMatches,
                            CacheCount = cacheMatches ? 2 : 3,
                            MembershipFilePath = cacheMatches ? "delta-part.json" : string.Empty
                        });
                });
            context
                .Setup(c => c.CallActivityAsync(
                    It.IsAny<TaskName>(),
                    It.IsAny<object>(),
                    It.IsAny<TaskOptions>()))
                .Returns<TaskName, object, TaskOptions>((name, input, _) =>
                {
                    harness.Sequence.Add(name.Name);
                    switch (input)
                    {
                        case DeleteBlobRequest delete:
                            harness.DeleteRequests.Add(delete);
                            if (failDelete && delete.Prefixes?.Count > 0)
                            {
                                return ActivityCallWrapper.InvokeAsync(
                                    name.Name,
                                    () => Task.FromException(new InvalidOperationException("Injected delete failure.")));
                            }

                            break;
                        case TransitiveAndDeltaUsersSenderRequest sender:
                            harness.SenderRequests.Add(sender);
                            break;
                        case CacheUploaderRequest cache:
                            harness.CacheUploaderRequests.Add(cache);
                            break;
                        case DeltaLinkUploaderRequest delta:
                            harness.DeltaLinkUploaderRequests.Add(delta);
                            break;
                        case CacheConverterRequest converter:
                            harness.ConverterRequests.Add(converter);
                            break;
                    }

                    return Task.CompletedTask;
                });
            return harness;
        }

        private static void SetupResult<T>(SubOrchestratorHarness harness, string activityName, T result)
        {
            harness.Context
                .Setup(c => c.CallActivityAsync<T>(
                    It.Is<TaskName>(name => name.Name == activityName),
                    It.IsAny<object>(),
                    It.IsAny<TaskOptions>()))
                .Returns<TaskName, object, TaskOptions>((name, input, _) =>
                {
                    harness.Sequence.Add(name.Name);
                    if (input is TransitiveAndDeltaUsersSenderRequest sender)
                    {
                        harness.SenderRequests.Add(sender);
                    }

                    return Task.FromResult(result);
                });
        }

        private sealed record ProcessFixture(
            ProcessCachedAndDeltaUsersFunction Function,
            InMemoryBlobStorageRepository Storage,
            ProcessCachedAndDeltaUsersRequest Request,
            Guid RetainedMember,
            Guid AddedMember,
            string AddsPrefix,
            string RemovesPrefix,
            string AddsPath,
            string RemovesPath,
            string PartPath,
            string CachePath,
            string TokenPath);

        private sealed record SenderFixture(
            InMemoryBlobStorageRepository Storage,
            SGMembershipCalculator Calculator,
            TransitiveAndDeltaUsersSenderFunction Sender,
            TransitiveAndDeltaUsersSenderRequest Request,
            Guid SourceGroupId,
            Guid TargetGroupId,
            Guid RunId);

        private sealed class SubOrchestratorHarness
        {
            public SubOrchestratorFunction Function { get; init; }
            public Mock<TaskOrchestrationContext> Context { get; set; }
            public DateTime Now { get; init; }
            public List<string> ExpectedPrefixes { get; init; }
            public List<string> Sequence { get; } = new();
            public List<DeleteBlobRequest> DeleteRequests { get; } = new();
            public List<ProcessCachedAndDeltaUsersRequest> ProcessRequests { get; } = new();
            public List<TransitiveAndDeltaUsersSenderRequest> SenderRequests { get; } = new();
            public List<CacheUploaderRequest> CacheUploaderRequests { get; } = new();
            public List<DeltaLinkUploaderRequest> DeltaLinkUploaderRequests { get; } = new();
            public List<CacheConverterRequest> ConverterRequests { get; } = new();
        }

        private sealed class FixedDeltaCachingConfig : IDeltaCachingConfig
        {
            public bool DeltaCacheEnabled => true;
        }
    }
}
