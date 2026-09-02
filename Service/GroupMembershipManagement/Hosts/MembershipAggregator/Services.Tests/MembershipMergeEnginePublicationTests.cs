// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.MembershipAggregator.Helpers;
using MembershipAggregator.Services.Entities;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Entities;
using Models.ServiceBus;
using Moq;
using Repositories.Contracts;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public sealed class MembershipMergeEnginePublicationTests
    {
        [TestMethod]
        public async Task ExecutePublishesTheManifestAfterBothDeltaBlobs()
        {
            var store = new MembershipMergeTestStore();
            var added = ParseGuid(1);
            var excluded = ParseGuid(2);
            var removed = ParseGuid(3);
            var sourceGroup = ParseGuid(101);
            store.AddRaw(
                "included",
                Envelope(),
                User(added, sourceGroup),
                User(excluded, sourceGroup));
            await store.AddCompressedAsync(
                "excluded",
                Envelope(exclusionary: true),
                User(excluded, ParseGuid(102)));
            await store.AddCompressedAsync(
                "destination",
                Envelope(),
                User(removed));
            var request = Request(
                MembershipMergeInput.FromPath("included", MembershipMergeInputKind.Included),
                MembershipMergeInput.FromPath("excluded", MembershipMergeInputKind.Excluded),
                MembershipMergeInput.FromPath("destination", MembershipMergeInputKind.Destination));

            var result = await Engine(store).ExecuteAsync(request);

            var additions = await store.ReadMembersAsync(result.AdditionsPath);
            var removals = await store.ReadMembersAsync(result.RemovalsPath);
            var additionsEnvelope = await store.ReadEnvelopeAsync(result.AdditionsPath);
            Assert.AreEqual(1, additions.Count);
            Assert.AreEqual(added, additions[0].ObjectId);
            Assert.AreEqual(MembershipAction.Add, additions[0].MembershipAction);
            CollectionAssert.AreEqual(new[] { sourceGroup }, additions[0].SourceGroups);
            Assert.AreEqual(1, removals.Count);
            Assert.AreEqual(removed, removals[0].ObjectId);
            Assert.AreEqual(MembershipAction.Remove, removals[0].MembershipAction);
            Assert.AreEqual(Envelope().Destination.ObjectId, additionsEnvelope.Destination.ObjectId);
            Assert.AreEqual(Envelope().RunId, additionsEnvelope.RunId);
            Assert.AreEqual(Envelope().SyncJobId, additionsEnvelope.SyncJobId);
            Assert.AreEqual(
                Envelope().MembershipObtainerDryRunEnabled,
                additionsEnvelope.MembershipObtainerDryRunEnabled);
            Assert.AreEqual(Envelope().Query, additionsEnvelope.Query);
            Assert.AreEqual(result.ManifestPath, store.Operations.Last().Substring("manifest:".Length));

            var manifest = JsonSerializer.Deserialize<MembershipMergeManifest>(
                store.ReadText(result.ManifestPath));
            Assert.AreEqual(1, manifest.AddCount);
            Assert.AreEqual(1, manifest.RemoveCount);
            Assert.AreEqual(1, manifest.SourceMemberCount);
            Assert.AreEqual(1, manifest.DestinationMemberCount);
            Assert.AreEqual(MembershipDeltaStatus.Ok, manifest.Status);
        }

        [TestMethod]
        public async Task DestinationPropertiesSurviveSnapshotMergeAndRemovalPublication()
        {
            var store = new MembershipMergeTestStore();
            var sourcePath = "staged-source";
            var destinationPath = "staged-destination";
            var conversationMemberId = "MCMjMCMjNQ==";
            var removed = new AzureADTeamsUser
            {
                ObjectId = ParseGuid(500),
                ConversationMemberId = conversationMemberId
            };
            var inputs = Enumerable.Range(0, 16)
                .Select(index => MembershipMergeInput.FromMembers(
                    $"source-{index}",
                    MembershipMergeInputKind.Included,
                    new[] { User(ParseGuid(index + 1), ParseGuid(100 + index)) }))
                .Append(MembershipMergeInput.FromMembers(
                    "destination",
                    MembershipMergeInputKind.Destination,
                    new AzureADUser[] { removed }))
                .ToArray();
            var engine = Engine(store);

            await engine.StageSnapshotAsync(
                inputs,
                sourcePath,
                Envelope(),
                destinationPath,
                Envelope());
            var result = await engine.ExecuteAsync(Request(
                MembershipMergeInput.FromPath(sourcePath, MembershipMergeInputKind.Included),
                MembershipMergeInput.FromPath(destinationPath, MembershipMergeInputKind.Destination)));

            var stagedDestination = (await store.ReadMembersAsync(destinationPath)).Single();
            var removal = (await store.ReadMembersAsync(result.RemovalsPath)).Single();
            AssertConversationMemberId(stagedDestination, conversationMemberId);
            AssertConversationMemberId(removal, conversationMemberId);
            Assert.AreEqual(MembershipAction.Remove, removal.MembershipAction);
        }

        [TestMethod]
        public async Task OutOfOrderInputFailsBeforeAnythingIsPublished()
        {
            var store = new MembershipMergeTestStore();
            var request = Request(MembershipMergeInput.FromMembers(
                "out-of-order",
                MembershipMergeInputKind.Included,
                new[]
                {
                    User(ParseGuid(2), ParseGuid(101)),
                    User(ParseGuid(1), ParseGuid(101))
                }));

            await Assert.ThrowsExceptionAsync<InvalidDataException>(
                () => Engine(store).ExecuteAsync(request));

            Assert.IsFalse(store.Operations.Any(operation => operation.StartsWith("write:", StringComparison.Ordinal)));
            Assert.IsFalse(store.Operations.Any(operation => operation.StartsWith("manifest:", StringComparison.Ordinal)));
        }

        [TestMethod]
        public async Task OutOfOrderInputInALaterBatchPublishesNoIntermediate()
        {
            var store = new MembershipMergeTestStore();
            var inputs = Enumerable.Range(0, 16)
                .Select(index => MembershipMergeInput.FromMembers(
                    $"ordered-{index}",
                    MembershipMergeInputKind.Included,
                    new[] { User(ParseGuid(index + 1), ParseGuid(101)) }))
                .Append(MembershipMergeInput.FromMembers(
                    "out-of-order",
                    MembershipMergeInputKind.Included,
                    new[]
                    {
                        User(ParseGuid(100), ParseGuid(101)),
                        User(ParseGuid(99), ParseGuid(101))
                    }))
                .ToArray();

            await Assert.ThrowsExceptionAsync<InvalidDataException>(
                () => Engine(store).ExecuteAsync(Request(inputs)));

            Assert.IsFalse(store.Operations.Any(operation => operation.StartsWith("write:", StringComparison.Ordinal)));
            Assert.IsFalse(store.Operations.Any(operation => operation.StartsWith("manifest:", StringComparison.Ordinal)));
        }

        [TestMethod]
        public async Task FailedPublicationCanBeRetriedWithoutDuplicatesOrPartialCommit()
        {
            var store = new MembershipMergeTestStore();
            var failure = new IOException("transient output failure");
            store.WriteFailure = (path, attempt) =>
                path.Contains("removals", StringComparison.Ordinal) && attempt == 1
                    ? failure
                    : null;
            var request = Request(
                MembershipMergeInput.FromMembers(
                    "included",
                    MembershipMergeInputKind.Included,
                    new[] { User(ParseGuid(1), ParseGuid(101)) }),
                MembershipMergeInput.FromMembers(
                    "destination",
                    MembershipMergeInputKind.Destination,
                    new[] { User(ParseGuid(2)) }));

            var actual = await Assert.ThrowsExceptionAsync<IOException>(
                () => Engine(store).ExecuteAsync(request));

            Assert.AreSame(failure, actual);
            Assert.IsFalse(store.Operations.Any(operation => operation.StartsWith("manifest:", StringComparison.Ordinal)));
            Assert.IsFalse(store.ContainsMembership($"{request.AttemptPrefix}-additions.json"));
            Assert.IsFalse(store.ContainsMembership($"{request.AttemptPrefix}-removals.json"));

            var result = await Engine(store).ExecuteAsync(request);
            var additions = await store.ReadMembersAsync(result.AdditionsPath);
            var removals = await store.ReadMembersAsync(result.RemovalsPath);

            Assert.AreEqual(1, additions.Count);
            Assert.AreEqual(1, removals.Count);
            Assert.AreEqual(ParseGuid(1), additions[0].ObjectId);
            Assert.AreEqual(ParseGuid(2), removals[0].ObjectId);
            Assert.IsTrue(store.ContainsText(result.ManifestPath));
        }

        [TestMethod]
        public async Task RepeatingTheSameRequestProducesTheSameCommittedResult()
        {
            var store = new MembershipMergeTestStore();
            var request = Request(MembershipMergeInput.FromMembers(
                "included",
                MembershipMergeInputKind.Included,
                new[] { User(ParseGuid(1), ParseGuid(101)) }));

            var first = await Engine(store).ExecuteAsync(request);
            var firstManifest = store.ReadText(first.ManifestPath);
            var second = await Engine(store).ExecuteAsync(request);
            var secondManifest = store.ReadText(second.ManifestPath);

            Assert.AreEqual(first, second);
            Assert.AreEqual(firstManifest, secondManifest);
            Assert.AreEqual(1, (await store.ReadMembersAsync(second.AdditionsPath)).Count);
            Assert.AreEqual(0, (await store.ReadMembersAsync(second.RemovalsPath)).Count);
        }

        [TestMethod]
        public async Task SuccessfulReplayDoesNotRewriteCommittedOutput()
        {
            var store = new MembershipMergeTestStore();
            var request = Request(MembershipMergeInput.FromMembers(
                "included",
                MembershipMergeInputKind.Included,
                new[] { User(ParseGuid(1), ParseGuid(101)) }));
            var first = await Engine(store).ExecuteAsync(request);
            var operationsAfterCommit = store.Operations.ToArray();
            store.WriteFailure = (path, attempt) => new IOException("replay should not write");

            var replay = await Engine(store).ExecuteAsync(request);

            Assert.AreEqual(first, replay);
            CollectionAssert.AreEqual(operationsAfterCommit, store.Operations);
            Assert.AreEqual(ParseGuid(1), (await store.ReadMembersAsync(replay.AdditionsPath)).Single().ObjectId);
        }

        [TestMethod]
        public async Task FirstCommittedAttemptWinsAcrossDifferentAttempts()
        {
            var store = new MembershipMergeTestStore();
            var firstRequest = Request(
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                MembershipMergeInput.FromMembers(
                    "first",
                    MembershipMergeInputKind.Included,
                    new[] { User(ParseGuid(1), ParseGuid(101)) }));
            var secondRequest = Request(
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                MembershipMergeInput.FromMembers(
                    "second",
                    MembershipMergeInputKind.Included,
                    new[] { User(ParseGuid(2), ParseGuid(102)) }));

            var first = await Engine(store).ExecuteAsync(firstRequest);
            var second = await Engine(store).ExecuteAsync(secondRequest);

            Assert.AreEqual(first, second);
            Assert.AreEqual(ParseGuid(1), (await store.ReadMembersAsync(second.AdditionsPath)).Single().ObjectId);
            Assert.IsFalse(store.ContainsMembership(
                $"{secondRequest.AttemptPrefix}-additions.json"));
            Assert.IsFalse(store.ContainsMembership(
                $"{secondRequest.AttemptPrefix}-removals.json"));
        }

        [TestMethod]
        public async Task ConcurrentManifestLoserDeletesOnlyItsOwnArtifacts()
        {
            var firstStore = new MembershipMergeTestStore();
            var secondStore = new MembershipMergeTestStore();
            var bothInitialManifestReadsCompleted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var bothAttemptsReachedManifest = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var manifestPath = "merge-output/run-123-manifest.json";
            var initialManifestReads = 0;
            var manifestAttempts = 0;
            var manifestLock = new object();
            string committedManifest = null;
            var firstRequest = Request(
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                MembershipMergeInput.FromMembers(
                    "first",
                    MembershipMergeInputKind.Included,
                    new[] { User(ParseGuid(1), ParseGuid(101)) }));
            var secondRequest = Request(
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                MembershipMergeInput.FromMembers(
                    "second",
                    MembershipMergeInputKind.Included,
                    new[] { User(ParseGuid(2), ParseGuid(102)) }));

            async Task<BlobResult> DownloadManifestAsync()
            {
                lock (manifestLock)
                {
                    if (committedManifest != null)
                    {
                        return new BlobResult
                        {
                            BlobStatus = BlobStatus.Found,
                            Content = committedManifest
                        };
                    }
                }

                if (Interlocked.Increment(ref initialManifestReads) == 2)
                {
                    bothInitialManifestReadsCompleted.SetResult(true);
                }

                await bothInitialManifestReadsCompleted.Task.WaitAsync(
                    TimeSpan.FromSeconds(10));
                return new BlobResult { BlobStatus = BlobStatus.NotFound };
            }

            async Task<bool> TryCommitManifestAsync(
                string content,
                CancellationToken cancellationToken)
            {
                if (Interlocked.Increment(ref manifestAttempts) == 2)
                {
                    bothAttemptsReachedManifest.SetResult(true);
                }

                await bothAttemptsReachedManifest.Task.WaitAsync(
                    TimeSpan.FromSeconds(10),
                    cancellationToken);

                lock (manifestLock)
                {
                    if (committedManifest != null)
                    {
                        return false;
                    }

                    committedManifest = content;
                    return true;
                }
            }

            foreach (var store in new[] { firstStore, secondStore })
            {
                store.Repository
                    .Setup(repository => repository.DownloadFileAsync(manifestPath))
                    .Returns(DownloadManifestAsync);
                store.Repository
                    .Setup(repository => repository.UploadFileIfAbsentAsync(
                        manifestPath,
                        It.IsAny<string>(),
                        It.IsAny<CancellationToken>()))
                    .Returns((
                        string _,
                        string content,
                        CancellationToken cancellationToken) =>
                        TryCommitManifestAsync(content, cancellationToken));
            }

            var results = await Task.WhenAll(
                Engine(firstStore).ExecuteAsync(firstRequest),
                Engine(secondStore).ExecuteAsync(secondRequest));

            Assert.AreEqual(results[0], results[1]);
            Assert.AreEqual(2, manifestAttempts);

            var firstAttemptWon = results[0].AdditionsPath.StartsWith(
                firstRequest.AttemptPrefix,
                StringComparison.Ordinal);
            var winningStore = firstAttemptWon ? firstStore : secondStore;
            var losingStore = firstAttemptWon ? secondStore : firstStore;
            var losingRequest = firstAttemptWon ? secondRequest : firstRequest;
            Assert.IsTrue(winningStore.ContainsMembership(results[0].AdditionsPath));
            Assert.IsTrue(winningStore.ContainsMembership(results[0].RemovalsPath));
            var expectedDeletedArtifacts = new[]
            {
                $"{losingRequest.AttemptPrefix}-additions.json",
                $"{losingRequest.AttemptPrefix}-removals.json"
            };
            CollectionAssert.AreEquivalent(
                expectedDeletedArtifacts.Select(path => $"delete:{path}").ToArray(),
                losingStore.Operations
                    .Where(operation => operation.StartsWith("delete:", StringComparison.Ordinal))
                    .ToArray());
            Assert.IsFalse(losingStore.ContainsMembership(expectedDeletedArtifacts[0]));
            Assert.IsFalse(losingStore.ContainsMembership(expectedDeletedArtifacts[1]));

            var manifest = JsonSerializer.Deserialize<MembershipMergeManifest>(
                committedManifest);
            Assert.AreEqual(results[0].AdditionsPath, manifest.AdditionsPath);
            Assert.AreEqual(results[0].RemovalsPath, manifest.RemovalsPath);
        }

        [TestMethod]
        public async Task NoChangeDeltaReadsEachInputOnceAndPublishesEmptyArtifacts()
        {
            var store = new MembershipMergeTestStore();
            var member = User(ParseGuid(1), ParseGuid(101));
            await store.AddCompressedAsync("source", Envelope(), member);
            await store.AddCompressedAsync("destination", Envelope(), member);

            var result = await Engine(store).ExecuteAsync(Request(
                MembershipMergeInput.FromPath(
                    "source",
                    MembershipMergeInputKind.Included),
                MembershipMergeInput.FromPath(
                    "destination",
                    MembershipMergeInputKind.Destination)));

            Assert.AreEqual(MembershipDeltaStatus.NoChanges, result.Status);
            Assert.AreEqual(0, (await store.ReadMembersAsync(result.AdditionsPath)).Count);
            Assert.AreEqual(0, (await store.ReadMembersAsync(result.RemovalsPath)).Count);
            store.Repository.Verify(
                repository => repository.StreamMembershipAsync(
                    "source",
                    It.IsAny<Action<GroupMembership>>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            store.Repository.Verify(
                repository => repository.StreamMembershipAsync(
                    "destination",
                    It.IsAny<Action<GroupMembership>>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [TestMethod]
        public async Task AdditionOnlyDeltaReadsEachInputOnceAndPublishesEmptyRemoval()
        {
            var store = new MembershipMergeTestStore();
            await store.AddCompressedAsync(
                "source",
                Envelope(),
                User(ParseGuid(1), ParseGuid(101)));
            await store.AddCompressedAsync("destination", Envelope());

            var result = await Engine(store).ExecuteAsync(Request(
                MembershipMergeInput.FromPath(
                    "source",
                    MembershipMergeInputKind.Included),
                MembershipMergeInput.FromPath(
                    "destination",
                    MembershipMergeInputKind.Destination)));

            Assert.AreEqual(MembershipDeltaStatus.Ok, result.Status);
            Assert.AreEqual(1, (await store.ReadMembersAsync(result.AdditionsPath)).Count);
            Assert.AreEqual(0, (await store.ReadMembersAsync(result.RemovalsPath)).Count);
            store.Repository.Verify(
                repository => repository.StreamMembershipAsync(
                    "source",
                    It.IsAny<Action<GroupMembership>>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            store.Repository.Verify(
                repository => repository.StreamMembershipAsync(
                    "destination",
                    It.IsAny<Action<GroupMembership>>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [TestMethod]
        public async Task DeltaWithRemovalsReadsEachInputTwice()
        {
            var store = new MembershipMergeTestStore();
            await store.AddCompressedAsync("source", Envelope());
            await store.AddCompressedAsync(
                "destination",
                Envelope(),
                User(ParseGuid(1)));

            var result = await Engine(store).ExecuteAsync(Request(
                MembershipMergeInput.FromPath(
                    "source",
                    MembershipMergeInputKind.Included),
                MembershipMergeInput.FromPath(
                    "destination",
                    MembershipMergeInputKind.Destination)));

            Assert.AreEqual(1, (await store.ReadMembersAsync(result.RemovalsPath)).Count);
            store.Repository.Verify(
                repository => repository.StreamMembershipAsync(
                    "source",
                    It.IsAny<Action<GroupMembership>>(),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(2));
            store.Repository.Verify(
                repository => repository.StreamMembershipAsync(
                    "destination",
                    It.IsAny<Action<GroupMembership>>(),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }

        [TestMethod]
        public async Task FailedConcurrentAttemptDoesNotDeleteCommittedWinnerArtifacts()
        {
            var artifacts = new ConcurrentDictionary<string, bool>();
            var repository = new Mock<IBlobStorageRepository>();
            var failingAttemptReachedRemoval = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var winnerCommitted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var failure = new IOException("simulated concurrent attempt failure");
            var failingRequest = Request(
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                MembershipMergeInput.FromMembers(
                    "failing",
                    MembershipMergeInputKind.Included,
                    new[] { User(ParseGuid(1), ParseGuid(101)) }));
            var winningRequest = Request(
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                MembershipMergeInput.FromMembers(
                    "winning",
                    MembershipMergeInputKind.Included,
                    new[] { User(ParseGuid(2), ParseGuid(102)) }));
            string committedManifest = null;

            repository
                .Setup(currentRepository => currentRepository.DownloadFileAsync(
                    It.IsAny<string>()))
                .ReturnsAsync(new BlobResult { BlobStatus = BlobStatus.NotFound });
            repository
                .Setup(currentRepository => currentRepository.WriteMembershipAsync(
                    It.IsAny<string>(),
                    It.IsAny<GroupMembership>(),
                    It.IsAny<IAsyncEnumerable<AzureADUser>>(),
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(async (
                    string path,
                    GroupMembership _,
                    IAsyncEnumerable<AzureADUser> members,
                    Dictionary<string, string> _,
                    CancellationToken cancellationToken) =>
                {
                    await foreach (var member in members.WithCancellation(cancellationToken))
                    {
                    }

                    if (path == $"{failingRequest.AttemptPrefix}-removals.json")
                    {
                        failingAttemptReachedRemoval.SetResult(true);
                        await winnerCommitted.Task.WaitAsync(cancellationToken);
                        throw failure;
                    }

                    artifacts[path] = true;
                });
            repository
                .Setup(currentRepository => currentRepository.UploadFileIfAbsentAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Returns((
                    string _,
                    string content,
                    CancellationToken cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    committedManifest = content;
                    winnerCommitted.SetResult(true);
                    return Task.FromResult(true);
                });
            repository
                .Setup(currentRepository => currentRepository.DeleteFileAsync(
                    It.IsAny<string>()))
                .Returns((string path) =>
                {
                    artifacts.TryRemove(path, out _);
                    return Task.CompletedTask;
                });

            var engine = new MembershipMergeEngine(
                repository.Object,
                Options.Create(new MembershipMergeOptions()));
            var failingTask = engine.ExecuteAsync(failingRequest);
            await failingAttemptReachedRemoval.Task.WaitAsync(TimeSpan.FromSeconds(10));

            var winner = await engine.ExecuteAsync(winningRequest);
            var actual = await Assert.ThrowsExceptionAsync<IOException>(
                async () => await failingTask);

            Assert.AreSame(failure, actual);
            Assert.IsTrue(artifacts.ContainsKey(winner.AdditionsPath));
            Assert.IsTrue(artifacts.ContainsKey(winner.RemovalsPath));
            Assert.IsFalse(artifacts.ContainsKey(
                $"{failingRequest.AttemptPrefix}-additions.json"));
            Assert.IsFalse(artifacts.ContainsKey(
                $"{failingRequest.AttemptPrefix}-removals.json"));

            var manifest = JsonSerializer.Deserialize<MembershipMergeManifest>(
                committedManifest);
            Assert.AreEqual(winner.AdditionsPath, manifest.AdditionsPath);
            Assert.AreEqual(winner.RemovalsPath, manifest.RemovalsPath);
        }

        [TestMethod]
        public async Task InterruptedReadCanBeRetriedFromTheSameInput()
        {
            var store = new MembershipMergeTestStore();
            var failure = new IOException("transient read failure");
            store.AddRaw(
                "included",
                Envelope(),
                User(ParseGuid(1), ParseGuid(101)),
                User(ParseGuid(2), ParseGuid(101)));
            store.ReadFailure = (path, attempt, memberIndex) =>
                path == "included" && attempt == 1 && memberIndex == 1
                    ? failure
                    : null;
            var request = Request(
                MembershipMergeInput.FromPath("included", MembershipMergeInputKind.Included));

            var actual = await Assert.ThrowsExceptionAsync<IOException>(
                () => Engine(store).ExecuteAsync(request));

            Assert.AreSame(failure, actual);
            Assert.IsFalse(store.Operations.Any(operation => operation.StartsWith("manifest:", StringComparison.Ordinal)));

            var result = await Engine(store).ExecuteAsync(request);

            Assert.AreEqual(2, result.AddCount);
            Assert.AreEqual(2, (await store.ReadMembersAsync(result.AdditionsPath)).Count);
        }

        [TestMethod]
        public async Task FailedIntermediateWriteCanBeRetried()
        {
            var store = new MembershipMergeTestStore();
            var failure = new IOException("transient intermediate write failure");
            var hasFailed = false;
            store.WriteFailure = (path, attempt) =>
            {
                if (!path.Contains(".intermediate.", StringComparison.Ordinal) || hasFailed)
                {
                    return null;
                }

                hasFailed = true;
                return failure;
            };
            var inputs = Enumerable.Range(0, 17)
                .Select(index => MembershipMergeInput.FromMembers(
                    $"source-{index}",
                    MembershipMergeInputKind.Included,
                    new[] { User(ParseGuid(index + 1), ParseGuid(101 + index)) }))
                .ToArray();
            var request = Request(inputs);

            var actual = await Assert.ThrowsExceptionAsync<IOException>(
                () => Engine(store).ExecuteAsync(request));

            Assert.AreSame(failure, actual);
            Assert.IsFalse(store.Operations.Any(operation => operation.StartsWith("manifest:", StringComparison.Ordinal)));

            var result = await Engine(store).ExecuteAsync(request);

            Assert.AreEqual(17, result.AddCount);
            Assert.AreEqual(17, (await store.ReadMembersAsync(result.AdditionsPath)).Count);
        }

        [TestMethod]
        public async Task PublicationFailureWhenCleanupAlsoFailsPreservesPrimaryFailureAndAttemptsEveryArtifact()
        {
            var store = new MembershipMergeTestStore();
            var primaryFailure = new IOException("transient output failure");
            var cleanupFailure = new IOException("transient cleanup failure");
            var request = Request(
                MembershipMergeInput.FromMembers(
                    "included",
                    MembershipMergeInputKind.Included,
                    new[] { User(ParseGuid(1), ParseGuid(101)) }),
                MembershipMergeInput.FromMembers(
                    "destination",
                    MembershipMergeInputKind.Destination,
                    new[] { User(ParseGuid(2)) }));
            var deleteAttempts = new List<string>();
            store.WriteFailure = (path, attempt) =>
                path == $"{request.AttemptPrefix}-removals.json" && attempt == 1
                    ? primaryFailure
                    : null;
            store.Repository
                .Setup(repository => repository.DeleteFileAsync(It.IsAny<string>()))
                .Returns((string path) =>
                {
                    deleteAttempts.Add(path);
                    if (path == $"{request.AttemptPrefix}-additions.json")
                    {
                        throw cleanupFailure;
                    }

                    return Task.CompletedTask;
                });

            var actual = await Assert.ThrowsExceptionAsync<IOException>(
                () => Engine(store).ExecuteAsync(request));

            Assert.AreSame(primaryFailure, actual);
            Assert.AreSame(
                cleanupFailure,
                actual.Data["MembershipMergeCleanupFailure"]);
            CollectionAssert.AreEquivalent(
                new[]
                {
                    $"{request.AttemptPrefix}-additions.json",
                    $"{request.AttemptPrefix}-removals.json"
                },
                deleteAttempts);
        }

        [TestMethod]
        public async Task ChangedInputBetweenDeltaPassesPublishesNoResult()
        {
            var store = new MembershipMergeTestStore();
            var openCount = 0;
            var input = MembershipMergeInput.FromMemberSource(
                "changing",
                MembershipMergeInputKind.Destination,
                cancellationToken =>
                {
                    openCount++;
                    return EnumerateMembers(
                        openCount == 1
                            ? new[] { User(ParseGuid(1), ParseGuid(101)) }
                            : Array.Empty<AzureADUser>(),
                        cancellationToken);
                });
            var request = Request(input);

            var exception = await Assert.ThrowsExceptionAsync<InvalidDataException>(
                () => Engine(store).ExecuteAsync(request));

            StringAssert.Contains(exception.Message, "changed while writing delta outputs");
            Assert.IsFalse(store.ContainsMembership($"{request.AttemptPrefix}-additions.json"));
            Assert.IsFalse(store.ContainsMembership($"{request.AttemptPrefix}-removals.json"));
            Assert.IsFalse(store.ContainsText(
                $"{request.OutputPrefix}/{request.IdempotencyKey}-manifest.json"));
        }

        [TestMethod]
        public async Task CancellationPublishesNoOutput()
        {
            var store = new MembershipMergeTestStore();
            using var cancellation = new CancellationTokenSource();
            var input = MembershipMergeInput.FromMemberSource(
                "cancelling",
                MembershipMergeInputKind.Included,
                cancellationToken => CancellingMembers(cancellation, cancellationToken));
            var request = Request(input);

            await Assert.ThrowsExceptionAsync<OperationCanceledException>(
                () => Engine(store).ExecuteAsync(
                    request,
                    cancellationToken: cancellation.Token));

            Assert.IsFalse(store.Operations.Any(operation => operation.StartsWith("write:", StringComparison.Ordinal)));
            Assert.IsFalse(store.Operations.Any(operation => operation.StartsWith("manifest:", StringComparison.Ordinal)));
        }

        [TestMethod]
        public async Task CancellationAfterArtifactsCommitDeletesAttemptOutputs()
        {
            var store = new MembershipMergeTestStore();
            using var cancellation = new CancellationTokenSource();
            store.MembershipWriteCompleted = path =>
            {
                if (path.EndsWith("-removals.json", StringComparison.Ordinal))
                {
                    cancellation.Cancel();
                }
            };
            var request = Request(MembershipMergeInput.FromMembers(
                "included",
                MembershipMergeInputKind.Included,
                new[] { User(ParseGuid(1), ParseGuid(101)) }));
            var additionsPath = $"{request.AttemptPrefix}-additions.json";
            var removalsPath = $"{request.AttemptPrefix}-removals.json";
            var manifestPath = $"{request.OutputPrefix}/{request.IdempotencyKey}-manifest.json";

            await Assert.ThrowsExceptionAsync<OperationCanceledException>(
                () => Engine(store).ExecuteAsync(
                    request,
                    cancellationToken: cancellation.Token));

            Assert.AreEqual(
                2,
                store.Operations.Count(operation =>
                    operation.StartsWith("write:", StringComparison.Ordinal)));
            Assert.IsFalse(store.ContainsMembership(additionsPath));
            Assert.IsFalse(store.ContainsMembership(removalsPath));
            Assert.IsFalse(store.ContainsText(manifestPath));
            Assert.IsTrue(store.Operations.Contains($"delete:{additionsPath}"));
            Assert.IsTrue(store.Operations.Contains($"delete:{removalsPath}"));
            Assert.IsFalse(store.Operations.Any(operation =>
                operation.StartsWith("manifest:", StringComparison.Ordinal)));
        }

        [TestMethod]
        public async Task AmbiguousManifestFailureCanBeRetriedWithFreshAttempt()
        {
            var store = new MembershipMergeTestStore();
            var failure = new IOException("manifest response was lost");
            store.ManifestFailure = (path, attempt) => attempt == 1 ? failure : null;
            var input = MembershipMergeInput.FromMembers(
                "included",
                MembershipMergeInputKind.Included,
                new[] { User(ParseGuid(1), ParseGuid(101)) });
            var firstRequest = Request(
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                input);
            var retryRequest = Request(
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                input);

            var actual = await Assert.ThrowsExceptionAsync<IOException>(
                () => Engine(store).ExecuteAsync(firstRequest));

            Assert.AreSame(failure, actual);
            Assert.IsTrue(store.ContainsMembership($"{firstRequest.AttemptPrefix}-additions.json"));
            Assert.IsTrue(store.ContainsMembership($"{firstRequest.AttemptPrefix}-removals.json"));
            Assert.IsFalse(store.ContainsText(
                $"{firstRequest.OutputPrefix}/{firstRequest.IdempotencyKey}-manifest.json"));

            var result = await Engine(store).ExecuteAsync(retryRequest);

            Assert.AreEqual(1, result.AddCount);
            Assert.IsTrue(store.ContainsText(result.ManifestPath));
            Assert.AreEqual(
                $"{retryRequest.AttemptPrefix}-additions.json",
                result.AdditionsPath);
            Assert.AreEqual(
                $"{retryRequest.AttemptPrefix}-removals.json",
                result.RemovalsPath);
            Assert.IsTrue(store.ContainsMembership($"{firstRequest.AttemptPrefix}-additions.json"));
            Assert.IsTrue(store.ContainsMembership($"{firstRequest.AttemptPrefix}-removals.json"));
            Assert.AreEqual(4, store.MembershipPaths.Count);
        }

        [TestMethod]
        public async Task FreshAttemptRecoversACommittedManifestAfterItsResponseIsLost()
        {
            var store = new MembershipMergeTestStore();
            var failure = new IOException("manifest commit response was lost");
            store.ManifestFailureAfterCommit = (path, attempt) =>
                attempt == 1 ? failure : null;
            var input = MembershipMergeInput.FromMembers(
                "included",
                MembershipMergeInputKind.Included,
                new[] { User(ParseGuid(1), ParseGuid(101)) });
            var firstRequest = Request(
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                input);
            var retryRequest = Request(
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                input);

            var actual = await Assert.ThrowsExceptionAsync<IOException>(
                () => Engine(store).ExecuteAsync(firstRequest));

            Assert.AreSame(failure, actual);
            Assert.IsTrue(store.ContainsText(
                $"{firstRequest.OutputPrefix}/{firstRequest.IdempotencyKey}-manifest.json"));
            var operationsAfterCommit = store.Operations.ToArray();

            var result = await Engine(store).ExecuteAsync(retryRequest);

            CollectionAssert.AreEqual(operationsAfterCommit, store.Operations);
            Assert.AreEqual(1, result.AddCount);
            Assert.AreEqual(
                $"{firstRequest.AttemptPrefix}-additions.json",
                result.AdditionsPath);
            Assert.AreEqual(
                $"{firstRequest.AttemptPrefix}-removals.json",
                result.RemovalsPath);
            Assert.IsFalse(store.ContainsMembership($"{retryRequest.AttemptPrefix}-additions.json"));
            Assert.IsFalse(store.ContainsMembership($"{retryRequest.AttemptPrefix}-removals.json"));
            Assert.AreEqual(ParseGuid(1), (await store.ReadMembersAsync(result.AdditionsPath)).Single().ObjectId);
        }

        private static MembershipMergeEngine Engine(
            MembershipMergeTestStore store,
            MembershipMergeOptions options = null) =>
            new MembershipMergeEngine(
                store.Repository.Object,
                Options.Create(options ?? new MembershipMergeOptions()));

        private static MembershipMergeRequest Request(params MembershipMergeInput[] inputs) =>
            Request(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), inputs);

        private static MembershipMergeRequest Request(
            Guid attemptId,
            params MembershipMergeInput[] inputs) =>
            new MembershipMergeRequest(
                inputs,
                "merge-output",
                "run-123",
                attemptId,
                Envelope());

        private static async IAsyncEnumerable<AzureADUser> CancellingMembers(
            CancellationTokenSource cancellation,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return User(ParseGuid(1), ParseGuid(101));
            cancellation.Cancel();
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
        }

        private static async IAsyncEnumerable<AzureADUser> EnumerateMembers(
            IReadOnlyList<AzureADUser> members,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var member in members)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return member;
            }

            await Task.CompletedTask;
        }

        private static GroupMembership Envelope(bool exclusionary = false) => new GroupMembership
        {
            Destination = new AzureADGroup { ObjectId = ParseGuid(999) },
            RunId = ParseGuid(998),
            SyncJobId = ParseGuid(997),
            Exclusionary = exclusionary,
            MembershipObtainerDryRunEnabled = true,
            Query = "[{\"type\":\"GroupMembership\"}]",
            SourceMembers = new List<AzureADUser>()
        };

        private static AzureADUser User(Guid objectId, Guid sourceGroup = default) => new AzureADUser
        {
            ObjectId = objectId,
            SourceGroup = sourceGroup
        };

        private static void AssertConversationMemberId(
            AzureADUser user,
            string expectedConversationMemberId)
        {
            var teamsUser = new AzureADTeamsUser
            {
                ObjectId = user.ObjectId,
                Properties = user.Properties
            };

            Assert.AreEqual(expectedConversationMemberId, teamsUser.ConversationMemberId);
        }

        private static Guid ParseGuid(int value) =>
            Guid.Parse($"00000000-0000-0000-0000-{value:X12}");
    }
}
