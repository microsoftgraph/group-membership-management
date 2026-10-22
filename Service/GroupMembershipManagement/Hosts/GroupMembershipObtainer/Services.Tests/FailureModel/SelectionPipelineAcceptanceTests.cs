// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.GroupMembershipObtainer;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.ServiceBus;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Tests.Services.Infrastructure;

namespace Tests.Services.FailureModel
{
    /// <summary>
    /// Failure-model acceptance tests for the bounded one-type selection pipeline (storage contract revision 4).
    /// Each test replaces a pre-amendment repro test that targeted the removed time-budgeted selection activity, or
    /// covers a new invariant row. All run the real activities against the in-memory blob double (V6.12).
    /// </summary>
    [TestClass]
    public sealed class SelectionPipelineAcceptanceTests
    {
        private const string Category = "FailureModelAcceptance";

        // ---------------------------------------------------------------- I1

        /// <summary>
        /// I1 (replaces repro I1_OmittedLookupIds_AreLoggedIndividuallyAndRetried): omitted getByIds identifiers are
        /// excluded, logged by object ID, left unrecorded, and looked up again on the next run; the part succeeds.
        /// </summary>
        [TestMethod]
        [TestCategory(Category)]
        public async Task I1_OmittedLookupIds_AreLoggedIndividuallyAndRetried()
        {
            var storage = new InMemoryBlobStorageRepository();
            var pipeline = new SelectionPipelineHarness(storage);
            var sourceGroupId = Guid.NewGuid();
            var identified = Member(sourceGroupId);
            var firstOmitted = Member(sourceGroupId);
            var secondOmitted = Member(sourceGroupId);
            pipeline.DirectoryTypes[identified.ObjectId] = "user";
            var first = await PipelineRequestAsync(storage, GroupMembershipReadKind.DeltaInitial, new[] { "users" },
                new[] { identified, firstOmitted, secondOmitted }, sourceGroupId);

            var firstResult = await RunPipelineAsync(pipeline, first);

            Assert.AreEqual(SelectionOutcomeStatus.Completed, firstResult.Outcome.Status);
            CollectionAssert.AreEqual(new[] { identified.ObjectId }, await ReadIdsAsync(storage, firstResult.FilePath));
            Assert.AreEqual(2, firstResult.NotIdentifiedCount);
            var record = storage.GetContent(GroupPartStoragePaths.BuildRecordedTypesPath(sourceGroupId)) ?? string.Empty;
            StringAssert.Contains(record, identified.ObjectId.ToString());
            Assert.IsFalse(record.Contains(firstOmitted.ObjectId.ToString(), StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(record.Contains(secondOmitted.ObjectId.ToString(), StringComparison.OrdinalIgnoreCase));
            var warning = pipeline.FinalizeLogger.Entries.SingleOrDefault(entry =>
                entry.Level == LogLevel.Warning && entry.EventId.Id == 150187);
            Assert.IsNotNull(warning, "The omitted object IDs were not logged.");
            StringAssert.Contains(warning.Message, firstOmitted.ObjectId.ToString());
            StringAssert.Contains(warning.Message, secondOmitted.ObjectId.ToString());

            pipeline.LookupBatches.Clear();
            pipeline.DirectoryTypes[firstOmitted.ObjectId] = "user";
            pipeline.DirectoryTypes[secondOmitted.ObjectId] = "user";
            var second = await PipelineRequestAsync(storage, GroupMembershipReadKind.DeltaInitial, new[] { "users" },
                new[] { identified, firstOmitted, secondOmitted }, sourceGroupId);
            var secondResult = await RunPipelineAsync(pipeline, second);

            Assert.AreEqual(1, pipeline.LookupBatches.Count);
            CollectionAssert.AreEquivalent(new[] { firstOmitted.ObjectId, secondOmitted.ObjectId }, pipeline.LookupBatches[0]);
            Assert.AreEqual(3, secondResult.MemberCount);
        }

        /// <summary>
        /// I1, owner D3 exception: a lookup batch of 2 or more identifiers that returns none of them is a lookup failure
        /// (<c>lookup returned none</c>); the part falls back to the selected full read.
        /// </summary>
        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        [TestCategory(Category)]
        public async Task I1_AllOmittedSegment_FallsBackToSelectedFullRead(bool savedDeltaLink)
        {
            var scenario = new SelectionOrchestrationScenario(new[] { "users" }, savedDeltaLink);
            scenario.AddMember("user", knownToGraph: false);
            scenario.AddMember("agentUser", knownToGraph: false);

            var response = await scenario.RunAsync();

            var fallback = scenario.FallbackEntries.Single();
            StringAssert.Contains(fallback.Message, "identification failed");
            CollectionAssert.AreEquivalent(scenario.SelectedTrueMembers.ToList(), await scenario.ReadPartMemberIdsAsync(response.FilePath));
            Assert.AreEqual(1, scenario.PartFiles.Count);
            Assert.AreEqual(0, scenario.SelectionArtifacts.Count);
            Assert.IsTrue(
                scenario.Pipeline.ClassifyLogger.Entries.Any(entry => entry.Message.Contains("lookup returned none", StringComparison.Ordinal)),
                "Classify did not report the all-omitted response as 'lookup returned none'.");
        }

        // ---------------------------------------------------------------- I2

        /// <summary>
        /// I2: re-running prepare, classify and finalize with the original input after their writes succeeded yields the
        /// same manifest, receipts and part bytes as running each once.
        /// </summary>
        [DataTestMethod]
        [DataRow((int)GroupMembershipReadKind.DeltaInitial)]
        [DataRow((int)GroupMembershipReadKind.DeltaLink)]
        [TestCategory(Category)]
        public async Task I2_SelectionActivitiesReRunAfterWrites_PublishIdenticalBytes(int readKindValue)
        {
            var readKind = (GroupMembershipReadKind)readKindValue;
            var storage = new InMemoryBlobStorageRepository();
            var pipeline = new SelectionPipelineHarness(storage);
            var members = Enumerable.Range(0, 2500).Select(_ => Member(Guid.NewGuid())).ToList();
            foreach (var member in members)
            {
                pipeline.DirectoryTypes[member.ObjectId] = member.ObjectId.GetHashCode() % 3 == 0 ? "agentUser" : "user";
            }

            var request = await PipelineRequestAsync(storage, readKind, new[] { "agents" }, members, Guid.NewGuid());

            var prepare = await pipeline.Prepare.RunAsync(request, CancellationToken.None);
            var manifest = storage.GetBytes(ManifestPath(request));
            var prepareAgain = await pipeline.Prepare.RunAsync(request, CancellationToken.None);
            CollectionAssert.AreEqual(manifest, storage.GetBytes(ManifestPath(request)));
            Assert.AreEqual(prepare.ExecutionId, prepareAgain.ExecutionId);

            var receipts = new Dictionary<int, byte[]>();
            foreach (var segment in prepare.UnknownSegments)
            {
                await pipeline.Classify.RunAsync(Segment(request, segment), CancellationToken.None);
                receipts[segment] = storage.GetBytes(ReceiptPath(request, prepare.ExecutionId, segment));
                var again = await pipeline.Classify.RunAsync(Segment(request, segment), CancellationToken.None);
                Assert.AreEqual(SelectionOutcomeStatus.Completed, again.Outcome.Status);
                CollectionAssert.AreEqual(receipts[segment], storage.GetBytes(ReceiptPath(request, prepare.ExecutionId, segment)));
            }

            Assert.AreEqual(prepare.UnknownSegments.Count, pipeline.LookupCallCount, "A re-run classify call repeated its lookup.");

            var finalized = await pipeline.Finalize.RunAsync(request, CancellationToken.None);
            var part = storage.GetBytes(request.PartFilePath);
            var finalizedAgain = await pipeline.Finalize.RunAsync(request, CancellationToken.None);

            CollectionAssert.AreEqual(part, storage.GetBytes(request.PartFilePath));
            Assert.AreEqual(finalized.MemberCount, finalizedAgain.MemberCount);
            Assert.AreEqual(finalized.UserCount, finalizedAgain.UserCount);
            Assert.AreEqual(finalized.AgentCount, finalizedAgain.AgentCount);
            Assert.AreEqual(SelectionOutcomeStatus.Completed, finalizedAgain.Outcome.Status);
        }

        /// <summary>
        /// I2: two concurrent prepare executions that read different recorded types produce one manifest; classify and
        /// finalize use only the winner's segments; the loser's segments are removed by cleanup.
        /// </summary>
        [TestMethod]
        [TestCategory(Category)]
        public async Task I2_ConcurrentPrepareDuplicates_OneManifestWinnerSnapshotOnly()
        {
            var storage = new InMemoryBlobStorageRepository();
            var pipeline = new SelectionPipelineHarness(storage);
            var sourceGroupId = Guid.NewGuid();
            var person = Member(sourceGroupId);
            var agent = Member(sourceGroupId);
            pipeline.DirectoryTypes[person.ObjectId] = "user";
            pipeline.DirectoryTypes[agent.ObjectId] = "agentUser";
            var request = await PipelineRequestAsync(storage, GroupMembershipReadKind.DeltaInitial, new[] { "users" },
                new[] { person, agent }, sourceGroupId);
            var recordPath = GroupPartStoragePaths.BuildRecordedTypesPath(sourceGroupId);
            await storage.UploadFileAsync(recordPath, "{\"schema\":1}\n" + TypeLine(person.ObjectId, "user") + TypeLine(agent.ObjectId, "agentUser"));

            var releaseManifests = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var waiting = 0;
            storage.BeforeConditionalWriteAsync = async path =>
            {
                if (path.EndsWith("manifest.json", StringComparison.Ordinal))
                {
                    if (Interlocked.Increment(ref waiting) == 2)
                    {
                        releaseManifests.TrySetResult();
                    }

                    await releaseManifests.Task;
                }
            };
            var first = pipeline.Prepare.RunAsync(request, CancellationToken.None);
            // The second execution reads the record after it was deleted: every member is unknown for it.
            while (Volatile.Read(ref waiting) == 0)
            {
                await Task.Delay(5);
            }

            await storage.DeleteFileAsync(recordPath);
            var second = pipeline.Prepare.RunAsync(request, CancellationToken.None);
            var results = await Task.WhenAll(first, second);
            storage.BeforeConditionalWriteAsync = null;

            Assert.AreEqual(results[0].ExecutionId, results[1].ExecutionId, "Both executions must return the winner's manifest.");
            var executionFolders = storage.Paths
                .Where(path => path.StartsWith(GroupPartStoragePaths.BuildSelectionFolder(request.TargetGroupId, RunId(request), request.CurrentPart), StringComparison.Ordinal))
                .Select(path => path.Split('/')[3])
                .Where(name => name != "manifest.json")
                .Distinct()
                .ToList();
            Assert.AreEqual(2, executionFolders.Count, "Each execution writes its own segments.");

            foreach (var segment in results[0].UnknownSegments)
            {
                await pipeline.Classify.RunAsync(Segment(request, segment), CancellationToken.None);
            }

            var finalized = await pipeline.Finalize.RunAsync(request, CancellationToken.None);
            CollectionAssert.AreEqual(new[] { person.ObjectId }, await ReadIdsAsync(storage, finalized.FilePath));
            Assert.AreEqual(results[0].UnknownSegments.Count, pipeline.LookupCallCount, "Classify did not follow the winner's snapshot.");

            await pipeline.Cleanup.RunAsync(CleanupRequest(request));
            Assert.IsFalse(storage.Paths.Any(path => path.Contains("_GroupMembershipSelection_", StringComparison.Ordinal)));
        }

        /// <summary>
        /// I2: a finalize writer interrupted after staging its content and before the commit leaves no part file; its
        /// re-run publishes the full bytes.
        /// </summary>
        [TestMethod]
        [TestCategory(Category)]
        public async Task I2_FinalizeInterruptedBeforeCommit_ReRunPublishesFullBytes()
        {
            var storage = new InMemoryBlobStorageRepository();
            var pipeline = new SelectionPipelineHarness(storage);
            var members = Enumerable.Range(0, 30).Select(_ => Member(Guid.NewGuid())).ToList();
            members.ForEach(member => pipeline.DirectoryTypes[member.ObjectId] = "user");
            var request = await PipelineRequestAsync(storage, GroupMembershipReadKind.DeltaInitial, new[] { "users" }, members, Guid.NewGuid());
            await RunUntilFinalizeAsync(pipeline, request);
            storage.WriteFailurePaths.Add(request.PartFilePath);

            await Assert.ThrowsExceptionAsync<IOException>(() => pipeline.Finalize.RunAsync(request, CancellationToken.None));
            Assert.IsNull(storage.GetContent(request.PartFilePath));

            storage.WriteFailurePaths.Clear();
            var result = await pipeline.Finalize.RunAsync(request, CancellationToken.None);

            Assert.AreEqual(SelectionOutcomeStatus.Completed, result.Outcome.Status);
            var expected = JsonSerializer.Serialize(new GroupMembership
            {
                Destination = new AzureADGroup { ObjectId = request.TargetGroupId },
                RunId = RunId(request),
                SyncJobId = request.SyncJob.Id,
                Query = request.SyncJob.Query,
                SourceMembers = members.OrderBy(member => member.ObjectId).ToList()
            });
            Assert.AreEqual(expected, storage.GetContent(request.PartFilePath));
        }

        // ---------------------------------------------------------------- I6

        /// <summary>
        /// I6 (replaces repro I6_WrappedReaderFailure_CleansStoryArtifacts): when a reader fails through Durable's
        /// wrapper after the staging blob and selection folder exist, cleanup still deletes them.
        /// </summary>
        [DataTestMethod]
        [DataRow("initial-delta")]
        [DataRow("delta-link")]
        [TestCategory(Category)]
        public async Task I6_WrappedReaderFailure_CleansStoryArtifacts(string readPath)
        {
            var savedDeltaLink = readPath == "delta-link";
            var scenario = new SelectionOrchestrationScenario(new[] { "users" }, savedDeltaLink);
            scenario.AddMember("user");
            await scenario.Storage.UploadFileAsync(scenario.StagingPath, "stale-staging");
            await scenario.Storage.UploadFileAsync(scenario.SelectionFolder + "manifest.json", "stale-manifest");
            scenario.FailReadActivityOnCall[savedDeltaLink ? nameof(DeltaLinkUserReaderFunction) : nameof(DeltaUserReaderFunction)] = 1;

            await Assert.ThrowsExceptionAsync<TaskFailedException>(() => scenario.RunAsync());

            Assert.IsNull(scenario.Storage.GetContent(scenario.StagingPath), "Wrapped activity failure left staging state.");
            Assert.AreEqual(0, scenario.SelectionArtifacts.Count, "Wrapped activity failure left the selection folder.");
        }

        /// <summary>
        /// I6 and V6.6: a <see cref="TaskFailedException"/> from each selection activity falls back with
        /// <c>identification failed</c>; cleanup runs; exactly one part file exists and it equals a direct selected full
        /// read. The replacement read never merges the abandoned read's unfiltered pages.
        /// </summary>
        [DataTestMethod]
        [DataRow(nameof(GroupPartSelectionPrepareFunction), false, false)]
        [DataRow(nameof(GroupPartSegmentClassifyFunction), false, false)]
        [DataRow(nameof(GroupPartSelectionFinalizeFunction), false, false)]
        [DataRow(nameof(GroupPartSelectionPrepareFunction), true, true)]
        [DataRow(nameof(GroupPartSegmentClassifyFunction), true, true)]
        [DataRow(nameof(GroupPartSelectionFinalizeFunction), true, true)]
        [DataRow(nameof(GroupPartSelectionPrepareFunction), false, true)]
        [TestCategory(Category)]
        public async Task I6_SelectionActivityTaskFailure_CleansAndFallsBackWithoutAbandonedPages(
            string failingActivity,
            bool failAfterWrites,
            bool savedDeltaLink)
        {
            var scenario = new SelectionOrchestrationScenario(new[] { "users" }, savedDeltaLink)
            {
                IncrementalReaderUploadsPage = true
            };
            scenario.AddMember("user");
            scenario.AddMember("agentUser", knownToGraph: true);
            Func<string, object, Exception> inject = (name, _) =>
                name == failingActivity ? new InvalidOperationException($"Injected {name} failure.") : null;
            if (failAfterWrites)
            {
                scenario.Pipeline.FailAfter = inject;
            }
            else
            {
                scenario.Pipeline.FailBefore = inject;
            }

            var response = await scenario.RunAsync();

            StringAssert.Contains(scenario.FallbackEntries.Single().Message, "identification failed");
            Assert.AreEqual(0, scenario.SelectionArtifacts.Count, "Cleanup did not remove staging and the selection folder.");
            Assert.AreEqual(1, scenario.PartFiles.Count, string.Join(", ", scenario.PartFiles));
            Assert.AreEqual(response.FilePath, scenario.PartFiles.Single());
            CollectionAssert.AreEquivalent(scenario.SelectedTrueMembers.ToList(), await scenario.ReadPartMemberIdsAsync(response.FilePath));
            Assert.AreEqual(1, scenario.MergedPageCounts.Last(), "The replacement read merged abandoned page uploads.");
            Assert.AreEqual(1, scenario.CompletionEntries.Count());
            StringAssert.Contains(scenario.CompletionEntries.Single().Message, "Read kind: Full");
        }

        // ---------------------------------------------------------------- I7

        /// <summary>
        /// I7 (amended 2026-10-21), structural guard: the scenario test above cannot see a deadline that uses real time,
        /// so this test fails if one is reintroduced. No type in the GMO function assembly or Repositories.Contracts models a deadline or a throttle
        /// budget, and no selection activity except record types (which needs a clock for its 7-day refresh rule) takes
        /// a clock or a deadline setting.
        /// </summary>
        [TestMethod]
        [TestCategory(Category)]
        public void I7_NoDeadlineOrThrottleBudgetTypes_AndSelectionActivitiesTakeNoClock()
        {
            var assemblies = new[]
            {
                typeof(GroupPartSelectionPrepareFunction).Assembly,
                typeof(Repositories.Contracts.IRetryPolicyProvider).Assembly,
            };
            var forbidden = new[] { "Deadline", "ThrottleBudget", "GraphThrottled", "LookupCapability", "PageReadResult" };
            var offenders = assemblies
                .SelectMany(assembly => assembly.GetTypes())
                .Select(type => type.FullName ?? type.Name)
                .Where(name => forbidden.Any(token => name.Contains(token, StringComparison.Ordinal)))
                .ToList();
            Assert.AreEqual(0, offenders.Count, "Removed deadline/throttle types are back: " + string.Join(", ", offenders));

            var clockFree = new[]
            {
                typeof(GroupPartSelectionPrepareFunction),
                typeof(GroupPartSegmentClassifyFunction),
                typeof(GroupPartSelectionFinalizeFunction),
                typeof(GroupPartCleanupFunction),
            };
            foreach (var activity in clockFree)
            {
                var parameters = activity.GetConstructors()
                    .SelectMany(constructor => constructor.GetParameters())
                    .Select(parameter => parameter.ParameterType)
                    .ToList();
                Assert.IsFalse(parameters.Contains(typeof(TimeProvider)), $"{activity.Name} takes a clock again; an activity deadline may be back.");
            }

            Assert.IsFalse(
                Enum.GetNames(typeof(SelectionOutcomeStatus)).Contains("Throttled"),
                "The Durable-timer throttle outcome is back.");
        }

        /// <summary>
        /// I7 (amended by the owner, 2026-10-21): the selection activities have no story deadline; GMO's function
        /// timeout is the only bound. With a fake clock that passes 9 minutes inside prepare, classify, finalize or
        /// record types, the activity still completes and the part publishes its selected members without a fallback.
        /// </summary>
        [DataTestMethod]
        [DataRow(nameof(GroupPartSelectionPrepareFunction))]
        [DataRow(nameof(GroupPartSegmentClassifyFunction))]
        [DataRow(nameof(GroupPartSelectionFinalizeFunction))]
        [DataRow(nameof(GroupPartRecordTypesFunction))]
        [TestCategory(Category)]
        public async Task I7_SlowActivity_HasNoStoryDeadlineAndCompletes(string slowActivity)
        {
            var scenario = new SelectionOrchestrationScenario(new[] { "agents" }, savedDeltaLink: false);
            scenario.AddMember("user");
            scenario.AddMember("agentUser");
            var storage = scenario.Storage;
            var clock = scenario.Pipeline.Clock;
            var advanced = false;
            var reads = new Dictionary<string, int>();
            void Pass()
            {
                if (!advanced)
                {
                    advanced = true;
                    clock.Advance(TimeSpan.FromMinutes(9));
                }
            }

            storage.BeforeWriteAsync = path =>
            {
                if (slowActivity == nameof(GroupPartSelectionPrepareFunction) && path.EndsWith("segment_00000.txt", StringComparison.Ordinal))
                {
                    Pass();
                }

                return Task.CompletedTask;
            };
            storage.BeforeReadAsync = path =>
            {
                reads[path] = reads.TryGetValue(path, out var count) ? count + 1 : 1;
                var hit = slowActivity switch
                {
                    nameof(GroupPartSegmentClassifyFunction) => path.EndsWith("segment_00000.txt", StringComparison.Ordinal),
                    // Prepare reads staging first; finalize reads it next.
                    nameof(GroupPartSelectionFinalizeFunction) => path == scenario.StagingPath && reads[path] == 2,
                    // Prepare reads the record first; record types reads it next.
                    nameof(GroupPartRecordTypesFunction) => path.StartsWith("memberTypes/", StringComparison.Ordinal) && reads[path] == 2,
                    _ => false,
                };
                if (hit)
                {
                    Pass();
                }

                return Task.CompletedTask;
            };

            var response = await scenario.RunAsync();

            Assert.IsTrue(advanced, "The slow storage hook did not run.");
            Assert.AreEqual(0, scenario.FallbackEntries.Count(), string.Join(Environment.NewLine, scenario.FallbackEntries.Select(entry => entry.Message)));
            Assert.IsFalse(scenario.Pipeline.RecordTypesLogger.Entries.Any(entry => entry.EventId.Id == 150186), "Record types failed.");
            CollectionAssert.AreEquivalent(scenario.SelectedTrueMembers.ToList(), await scenario.ReadPartMemberIdsAsync(response.FilePath));
            Assert.AreEqual(1, scenario.PartFiles.Count);
            Assert.AreEqual(1, scenario.CompletionEntries.Count());
        }

        /// <summary>
        /// I7 and revision 4: once the part file's commit succeeded, finalize is <c>Completed</c> even if time passes
        /// while the commit is in flight; a re-run after the commit is Completed too and no second part file appears.
        /// </summary>
        [TestMethod]
        [TestCategory(Category)]
        public async Task I7_FinalizeAfterCommit_StaysCompleted()
        {
            var storage = new InMemoryBlobStorageRepository();
            var pipeline = new SelectionPipelineHarness(storage);
            var member = Member(Guid.NewGuid());
            pipeline.DirectoryTypes[member.ObjectId] = "user";
            var request = await PipelineRequestAsync(storage, GroupMembershipReadKind.DeltaInitial, new[] { "users" }, new[] { member }, Guid.NewGuid());
            await RunUntilFinalizeAsync(pipeline, request);
            // Time passes while the commit request is in flight: the commit still lands.
            var commitObserved = false;
            storage.BeforeConditionalWriteAsync = path =>
            {
                if (path == request.PartFilePath)
                {
                    commitObserved = true;
                    pipeline.Clock.Advance(TimeSpan.FromMinutes(9));
                }

                return Task.CompletedTask;
            };

            var first = await pipeline.Finalize.RunAsync(request, CancellationToken.None);
            pipeline.Clock.Advance(TimeSpan.FromMinutes(30));
            var rerun = await pipeline.Finalize.RunAsync(request, CancellationToken.None);

            Assert.IsTrue(commitObserved);
            Assert.AreEqual(SelectionOutcomeStatus.Completed, first.Outcome.Status);
            Assert.AreEqual(SelectionOutcomeStatus.Completed, rerun.Outcome.Status, "A re-run after the commit must not turn into Failed.");
            Assert.AreEqual(1, storage.Paths.Count(path => path == request.PartFilePath));
        }

        // ---------------------------------------------------------------- I10

        /// <summary>
        /// I10 (amended by the owner, 2026-10-21): a 401/403 from getByIds falls back to the selected full read for
        /// this run only, with reason <c>lookup forbidden</c>. No capability marker is written or read, so the next
        /// one-type read runs the incremental read and probes getByIds again.
        /// </summary>
        [TestMethod]
        [TestCategory(Category)]
        public async Task I10_ForbiddenLookup_FallsBackForThisRunAndProbesAgainNextRun()
        {
            var storage = new InMemoryBlobStorageRepository();
            var pipeline = new SelectionPipelineHarness(storage);
            pipeline.LookupOverride = (_, _, _) =>
                Task.FromException<IReadOnlyDictionary<Guid, string>>(new HttpRequestException("Forbidden.", null, HttpStatusCode.Forbidden));
            var sourceGroupId = Guid.NewGuid();
            var first = new SelectionOrchestrationScenario(new[] { "users" }, savedDeltaLink: true, storage, pipeline, sourceGroupId);
            first.AddMember("user");
            first.AddMember("agentUser");

            var firstResponse = await first.RunAsync();

            var second = new SelectionOrchestrationScenario(new[] { "users" }, savedDeltaLink: true, storage, pipeline, sourceGroupId);
            second.Members.AddRange(first.Members);
            foreach (var entry in first.TrueTypes)
            {
                second.TrueTypes[entry.Key] = entry.Value;
            }

            var secondResponse = await second.RunAsync();

            Assert.AreEqual(2, pipeline.LookupCallCount, "Each run probes getByIds; nothing remembers the refusal.");
            Assert.IsFalse(storage.Paths.Any(path => path.StartsWith("capabilities/", StringComparison.Ordinal)), "No capability marker is written.");
            foreach (var scenario in new[] { first, second })
            {
                StringAssert.Contains(scenario.FallbackEntries.Single().Message, "lookup forbidden");
                StringAssert.Contains(scenario.FallbackEntries.Single().Message, "Abandoned read kind: DeltaLink");
                Assert.IsTrue(scenario.Sequence.Contains(nameof(DeltaLinkUserReaderFunction)), "The read skipped the incremental read.");
            }

            CollectionAssert.AreEquivalent(first.SelectedTrueMembers.ToList(), await first.ReadPartMemberIdsAsync(firstResponse.FilePath));
            CollectionAssert.AreEquivalent(second.SelectedTrueMembers.ToList(), await second.ReadPartMemberIdsAsync(secondResponse.FilePath));
        }

        // ---------------------------------------------------------------- I11

        /// <summary>
        /// I11 (replaces repro I11_ConcurrentTypeFactMerges_PreserveBothWriters): three concurrent record-types merges
        /// with ETag conflicts keep every writer's facts within the 5-attempt bound. The writers read the same source
        /// group, so each staging set holds every member (pruning to the staging set is documented, R5).
        /// </summary>
        [TestMethod]
        [TestCategory(Category)]
        public async Task I11_ConcurrentTypeFactMerges_PreserveAllWriters()
        {
            var storage = new InMemoryBlobStorageRepository();
            var sourceGroupId = Guid.NewGuid();
            var members = Enumerable.Range(0, 3).Select(_ => Member(sourceGroupId)).ToArray();
            var requests = new List<GroupPartSelectionRequest>();
            var pipelines = new List<SelectionPipelineHarness>();
            for (var part = 1; part <= 3; part++)
            {
                var pipeline = new SelectionPipelineHarness(storage);
                // Each writer identifies only its own member; the others are omitted by its lookup.
                pipeline.DirectoryTypes[members[part - 1].ObjectId] = "user";
                var request = await PipelineRequestAsync(storage, GroupMembershipReadKind.DeltaInitial, new[] { "users" }, members, sourceGroupId, currentPart: part);
                await RunUntilFinalizeAsync(pipeline, request);
                requests.Add(request);
                pipelines.Add(pipeline);
            }

            var recordPath = GroupPartStoragePaths.BuildRecordedTypesPath(sourceGroupId);
            var atBarrier = 0;
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            storage.BeforeConditionalWriteAsync = async path =>
            {
                if (path == recordPath && Interlocked.Increment(ref atBarrier) <= 3)
                {
                    if (Volatile.Read(ref atBarrier) == 3)
                    {
                        release.TrySetResult();
                    }

                    await release.Task;
                }
            };

            var results = await Task.WhenAll(requests.Select((request, i) => pipelines[i].RecordTypes.RunAsync(request, CancellationToken.None)));

            var record = storage.GetContent(recordPath);
            foreach (var member in members)
            {
                StringAssert.Contains(record, member.ObjectId.ToString(), $"A writer's fact was lost:{Environment.NewLine}{record}");
            }

            Assert.IsTrue(results.All(result => !result.Exhausted && result.Attempts <= GroupPartRecordTypesFunction.MaximumAttempts));
            Assert.IsTrue(results.Any(result => result.Attempts > 1), "The test did not produce an ETag conflict.");
            Assert.IsTrue(record.StartsWith("{\"schema\":1}\n", StringComparison.Ordinal));
            Assert.IsFalse(record.Contains("notIdentified", StringComparison.Ordinal));
        }

        /// <summary>I11: an unchanged record whose LastModified is more than 7 days old is rewritten (refreshed).</summary>
        [TestMethod]
        [TestCategory(Category)]
        public async Task I11_UnchangedRecordOlderThanSevenDays_IsRefreshed()
        {
            var storage = new InMemoryBlobStorageRepository();
            var pipeline = new SelectionPipelineHarness(storage);
            var sourceGroupId = Guid.NewGuid();
            var member = Member(sourceGroupId);
            var recordPath = GroupPartStoragePaths.BuildRecordedTypesPath(sourceGroupId);
            await storage.UploadFileAsync(recordPath, "{\"schema\":1}\n" + TypeLine(member.ObjectId, "user"));
            var request = await PipelineRequestAsync(storage, GroupMembershipReadKind.DeltaInitial, new[] { "users" }, new[] { member }, sourceGroupId);
            await RunUntilFinalizeAsync(pipeline, request);

            var fresh = await pipeline.RecordTypes.RunAsync(request, CancellationToken.None);
            Assert.IsFalse(fresh.Written, "A fresh unchanged record must not be rewritten.");

            pipeline.Clock.Advance(TimeSpan.FromDays(8));
            var before = storage.GetETag(recordPath);
            var stale = await pipeline.RecordTypes.RunAsync(request, CancellationToken.None);

            Assert.IsTrue(stale.Written);
            Assert.AreNotEqual(before, storage.GetETag(recordPath));
            StringAssert.Contains(storage.GetContent(recordPath), member.ObjectId.ToString());
        }

        /// <summary>I11: exhausting the 5 conditional attempts is logged and never fails the part.</summary>
        [TestMethod]
        [TestCategory(Category)]
        public async Task I11_MergeExhaustion_IsLoggedAndDoesNotFail()
        {
            var storage = new InMemoryBlobStorageRepository();
            var pipeline = new SelectionPipelineHarness(storage);
            var sourceGroupId = Guid.NewGuid();
            var member = Member(sourceGroupId);
            pipeline.DirectoryTypes[member.ObjectId] = "user";
            var request = await PipelineRequestAsync(storage, GroupMembershipReadKind.DeltaInitial, new[] { "users" }, new[] { member }, sourceGroupId);
            await RunUntilFinalizeAsync(pipeline, request);
            storage.InjectPreconditionFailure(GroupPartStoragePaths.BuildRecordedTypesPath(sourceGroupId), 412, 10);

            var result = await pipeline.RecordTypes.RunAsync(request, CancellationToken.None);

            Assert.IsTrue(result.Exhausted);
            Assert.AreEqual(GroupPartRecordTypesFunction.MaximumAttempts, result.Attempts);
            Assert.IsTrue(pipeline.RecordTypesLogger.Entries.Any(entry => entry.EventId.Id == 150189));
        }

        // ---------------------------------------------------------------- I12

        /// <summary>
        /// I12 (replaces repro I12_ProgressWrites_AreBoundedByNewState): at 100,000 and 700,000 staging members, prepare
        /// and finalize hold only compact per-member arrays plus one segment, and every selection blob is written once
        /// (no per-continuation rewrite). Live heap is sampled during the segment writes and before the part commit.
        /// </summary>
        [DataTestMethod]
        [DataRow(100_000)]
        [DataRow(700_000)]
        [TestCategory(Category)]
        public async Task I12_CompactArraysAndSingleWritePerBlob_AtEnvelope(int memberCount)
        {
            var storage = new InMemoryBlobStorageRepository();
            var pipeline = new SelectionPipelineHarness(storage);
            var sourceGroupId = Guid.NewGuid();
            var ids = new Guid[memberCount];
            for (var i = 0; i < memberCount; i++)
            {
                ids[i] = Guid.NewGuid();
            }

            // Sorted as the snapshot is, so the 1,000 unrecorded members form segment 0.
            Array.Sort(ids);

            // Every member is recorded except one segment's worth, so one lookup runs.
            var record = new StringBuilder("{\"schema\":1}\n", memberCount * 64);
            for (var i = 1000; i < memberCount; i++)
            {
                record.Append(TypeLine(ids[i], i % 5 == 0 ? "agentUser" : "user"));
            }

            for (var i = 0; i < 1000; i++)
            {
                pipeline.DirectoryTypes[ids[i]] = "user";
            }

            await storage.UploadFileAsync(GroupPartStoragePaths.BuildRecordedTypesPath(sourceGroupId), record.ToString());
            record = null;
            var request = await PipelineRequestAsync(storage, GroupMembershipReadKind.DeltaLink, new[] { "users" }, ids.Select(id => new AzureADUser { ObjectId = id }).ToList(), sourceGroupId);
            ids = null;

            var baseline = GC.GetTotalMemory(forceFullCollection: true);
            long prepareLive = 0;
            storage.BeforeWriteAsync = path =>
            {
                if (path.EndsWith("segment_00050.txt", StringComparison.Ordinal))
                {
                    prepareLive = GC.GetTotalMemory(forceFullCollection: true) - baseline;
                }

                return Task.CompletedTask;
            };
            storage.WriteHistory.Clear();
            var prepare = await pipeline.Prepare.RunAsync(request, CancellationToken.None);
            storage.BeforeWriteAsync = null;

            Assert.AreEqual(memberCount, prepare.StagingCount);
            Assert.AreEqual((memberCount + 999) / 1000, prepare.SegmentCount);
            CollectionAssert.AreEqual(new[] { 0 }, prepare.UnknownSegments);
            var bound = memberCount * 48L + 16 * 1024 * 1024;
            Assert.IsTrue(prepareLive < bound, $"Prepare held {prepareLive:N0} live bytes for {memberCount:N0} members (bound {bound:N0}).");

            foreach (var segment in prepare.UnknownSegments)
            {
                await pipeline.Classify.RunAsync(Segment(request, segment), CancellationToken.None);
            }

            // The stored segments and receipts are blob content, not finalize's working set: rebase.
            baseline = GC.GetTotalMemory(forceFullCollection: true);
            long finalizeLive = 0;
            storage.BeforePartWriterAsync = path =>
            {
                if (path == request.PartFilePath)
                {
                    finalizeLive = GC.GetTotalMemory(forceFullCollection: true) - baseline;
                }

                return Task.CompletedTask;
            };
            var finalized = await pipeline.Finalize.RunAsync(request, CancellationToken.None);

            Assert.AreEqual(SelectionOutcomeStatus.Completed, finalized.Outcome.Status);
            Assert.AreEqual(1000 + (memberCount - 1000) - (memberCount - 1000) / 5, finalized.MemberCount);
            // Sampled when finalize hands the selected identifiers to the writer: snapshot arrays plus the selected
            // identifiers (about 33 bytes per member), before the double buffers the part it stores.
            var finalizeBound = memberCount * 48L + 16 * 1024 * 1024;
            Assert.IsTrue(finalizeLive > 0, "The finalize memory sample did not run.");
            Assert.IsTrue(finalizeLive < finalizeBound, $"Finalize held {finalizeLive:N0} live bytes (bound {finalizeBound:N0}).");

            var selectionWrites = storage.WriteHistory
                .Where(write => write.Path.Contains("_GroupMembershipSelection_", StringComparison.Ordinal))
                .GroupBy(write => write.Path)
                .ToList();
            Assert.IsTrue(selectionWrites.All(group => group.Count() == 1), "A selection blob was rewritten.");
            var selectionBytes = selectionWrites.Sum(group => group.Single().ByteCount);
            Assert.IsTrue(selectionBytes < memberCount * 60L, $"Selection artifacts wrote {selectionBytes:N0} bytes.");
        }

        /// <summary>
        /// I12: orchestration history is bounded by the 100-segment limit: a read with 201 unknown segments classifies
        /// 100 in the sub-orchestration and the rest in child orchestrations of at most 100 segments each.
        /// </summary>
        [TestMethod]
        [TestCategory(Category)]
        public async Task I12_History_BoundedByHundredSegmentChildren()
        {
            var scenario = new SelectionOrchestrationScenario(new[] { "users" }, savedDeltaLink: true);
            for (var i = 0; i < 200_001; i++)
            {
                scenario.AddMember(i % 7 == 0 ? "agentUser" : "user");
            }

            var response = await scenario.RunAsync();

            Assert.AreEqual(0, scenario.FallbackEntries.Count());
            Assert.AreEqual(100, scenario.Sequence.Count(name => name == nameof(GroupPartSegmentClassifyFunction)));
            Assert.AreEqual(2, scenario.Pipeline.ChildOrchestrationActivityCounts.Count);
            Assert.IsTrue(scenario.Pipeline.ChildOrchestrationActivityCounts.All(count => count <= 100));
            Assert.AreEqual(201, scenario.Pipeline.LookupCallCount);
            Assert.AreEqual(scenario.SelectedTrueMembers.Count, (await scenario.ReadPartMemberIdsAsync(response.FilePath)).Count);
        }

        // ---------------------------------------------------------------- I14

        /// <summary>
        /// I14 (replaces repro I14_DeltaLinkSelection_PreservesSourceGroup): the one-type saved-delta part keeps
        /// <c>SourceGroup</c> on every member (source-group overload of the identifier writer).
        /// </summary>
        [TestMethod]
        [TestCategory(Category)]
        public async Task I14_DeltaLinkSelection_PreservesSourceGroup()
        {
            var storage = new InMemoryBlobStorageRepository();
            var pipeline = new SelectionPipelineHarness(storage);
            var sourceGroupId = Guid.NewGuid();
            var members = new[] { Member(sourceGroupId), Member(sourceGroupId) };
            pipeline.DirectoryTypes[members[0].ObjectId] = "user";
            pipeline.DirectoryTypes[members[1].ObjectId] = "user";
            var request = await PipelineRequestAsync(storage, GroupMembershipReadKind.DeltaLink, new[] { "users" }, members, sourceGroupId);

            var result = await RunPipelineAsync(pipeline, request);

            var read = new List<AzureADUser>();
            await foreach (var member in storage.StreamMembershipAsync(result.FilePath))
            {
                read.Add(member);
            }

            Assert.AreEqual(2, read.Count);
            Assert.IsTrue(read.All(member => member.SourceGroup == sourceGroupId), "The saved-delta writer erased source attribution.");
        }

        // ---------------------------------------------------------------- helpers

        internal static AzureADUser Member(Guid sourceGroupId) => new() { ObjectId = Guid.NewGuid(), SourceGroup = sourceGroupId };

        internal static string TypeLine(Guid id, string type) => $"{{\"id\":\"{id}\",\"type\":\"{type}\"}}\n";

        internal static Guid RunId(GroupPartSelectionRequest request) => request.SyncJob.RunId.GetValueOrDefault();

        internal static string ManifestPath(GroupPartSelectionRequest request) =>
            GroupPartStoragePaths.BuildManifestPath(request.TargetGroupId, RunId(request), request.CurrentPart);

        internal static string ReceiptPath(GroupPartSelectionRequest request, Guid executionId, int segment) =>
            GroupPartStoragePaths.BuildReceiptPath(request.TargetGroupId, RunId(request), request.CurrentPart, executionId, segment);

        internal static GroupPartSelectionRequest Segment(GroupPartSelectionRequest request, int segment)
        {
            var copy = JsonSerializer.Deserialize<GroupPartSelectionRequest>(JsonSerializer.Serialize(request));
            copy.SegmentIndex = segment;
            return copy;
        }

        internal static GroupPartCleanupRequest CleanupRequest(GroupPartSelectionRequest request) => new()
        {
            SyncJob = request.SyncJob,
            CurrentPart = request.CurrentPart,
            TotalParts = request.TotalParts,
            TargetGroupId = request.TargetGroupId
        };

        internal static async Task<GroupPartSelectionRequest> PipelineRequestAsync(
            InMemoryBlobStorageRepository storage,
            GroupMembershipReadKind readKind,
            IReadOnlyCollection<string> identityTypes,
            IReadOnlyCollection<AzureADUser> members,
            Guid sourceGroupId,
            int currentPart = 1,
            Guid? targetGroupId = null,
            Guid? runId = null,
            bool exclusionary = false)
        {
            var target = targetGroupId ?? Guid.NewGuid();
            var run = runId ?? Guid.NewGuid();
            var request = new GroupPartSelectionRequest
            {
                SyncJob = new SyncJob { Id = Guid.NewGuid(), RunId = run, Query = "[{\"type\":\"GroupMembership\"}]" },
                CurrentPart = currentPart,
                TotalParts = Math.Max(currentPart, 2),
                SourceGroupId = sourceGroupId,
                TargetGroupId = target,
                IdentityTypes = identityTypes?.ToList(),
                ReadKind = readKind,
                StagingPath = GroupPartStoragePaths.BuildStagingPath(target, run, currentPart),
                PartFilePath = $"{target}/10202026-1200_{run}_GroupMembership_{currentPart}.json"
            };
            if (readKind == GroupMembershipReadKind.DeltaLink)
            {
                await SelectionPipelineHarness.WriteDeltaLinkStagingAsync(storage, request.StagingPath, target, request.SyncJob,
                    members.Select(member => member.ObjectId).ToList(), exclusionary);
            }
            else
            {
                await SelectionPipelineHarness.WriteInitialStagingAsync(storage, request.StagingPath, target, request.SyncJob, members, exclusionary);
            }

            return request;
        }

        internal static async Task RunUntilFinalizeAsync(SelectionPipelineHarness pipeline, GroupPartSelectionRequest request)
        {
            var prepare = await pipeline.Prepare.RunAsync(request, CancellationToken.None);
            Assert.AreEqual(SelectionOutcomeStatus.Completed, prepare.Outcome.Status, prepare.Outcome.ToString());
            foreach (var segment in prepare.UnknownSegments)
            {
                var classified = await pipeline.Classify.RunAsync(Segment(request, segment), CancellationToken.None);
                Assert.AreEqual(SelectionOutcomeStatus.Completed, classified.Outcome.Status, classified.Outcome.ToString());
            }
        }

        internal static async Task<GroupPartSelectionResult> RunPipelineAsync(SelectionPipelineHarness pipeline, GroupPartSelectionRequest request)
        {
            await RunUntilFinalizeAsync(pipeline, request);
            var result = await pipeline.Finalize.RunAsync(request, CancellationToken.None);
            await pipeline.RecordTypes.RunAsync(request, CancellationToken.None);
            return result;
        }

        internal static async Task<List<Guid>> ReadIdsAsync(InMemoryBlobStorageRepository storage, string path)
        {
            var ids = new List<Guid>();
            await foreach (var member in storage.StreamMembershipAsync(path))
            {
                ids.Add(member.ObjectId);
            }

            return ids;
        }

        // ---------------------------------------------------------------- I2 (slice H): write-once publication

        /// <summary>
        /// I2 (slice H): finalize A commits the saved-delta part; a duplicate finalize A' of the same read starts and is
        /// interrupted mid-write (the double interrupts any overwriting stream after its open and any write-once commit
        /// before it lands). The published part still holds A's full bytes: the saved-delta part is written write-once,
        /// never through <c>OpenWriteAsync(overwrite: true)</c>, which commits an empty blob first.
        /// </summary>
        [TestMethod]
        [TestCategory(Category)]
        public Task I2_DeltaLinkFinalizeStraggler_CannotTruncateCommittedPart() =>
            AssertFinalizeStragglerCannotTruncateAsync(GroupMembershipReadKind.DeltaLink);

        /// <summary>I2 (slice H): the same straggler guarantee for the initial-delta part (streamed write-once).</summary>
        [TestMethod]
        [TestCategory(Category)]
        public Task I2_DeltaInitialFinalizeStraggler_CannotTruncateCommittedPart() =>
            AssertFinalizeStragglerCannotTruncateAsync(GroupMembershipReadKind.DeltaInitial);

        /// <summary>
        /// I2 (slice H): every publication on the one-type paths (initial delta and saved delta) and on the both-types
        /// saved-delta path goes through a write-once API: the staging blob, the selection manifest and receipts, the part
        /// file, the cache and the delta link. The real activities run against the double, which records the API of
        /// every write.
        /// </summary>
        [TestMethod]
        [TestCategory(Category)]
        public async Task I2_OneTypeAndSavedDeltaPublications_UseWriteOnceApis()
        {
            var storage = new InMemoryBlobStorageRepository();
            var pipeline = new SelectionPipelineHarness(storage);
            var fileTimestamp = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
            var publications = new List<InMemoryBlobStorageRepository.WriteCall>();

            // 1. One-type initial delta: merge into staging, selection, cache and delta link.
            {
                var sourceGroupId = Guid.NewGuid();
                var targetGroupId = Guid.NewGuid();
                var syncJob = new SyncJob { Id = Guid.NewGuid(), RunId = Guid.NewGuid(), Query = "[{\"type\":\"GroupMembership\"}]", TargetOfficeGroupId = targetGroupId };
                var runId = syncJob.RunId.GetValueOrDefault();
                var members = Enumerable.Range(0, 20).Select(_ => Member(sourceGroupId)).ToList();
                members.ForEach(member => pipeline.DirectoryTypes[member.ObjectId] = member.ObjectId.GetHashCode() % 2 == 0 ? "user" : "agentUser");
                await storage.UploadFileAsync(
                    GroupPartStoragePaths.BuildMergePrefix(targetGroupId, runId, 1) + Guid.NewGuid() + ".json",
                    JsonSerializer.Serialize(members));
                storage.WriteCalls.Clear();

                var calculator = CreateWriteOnceCalculator(storage, targetGroupId);
                var merge = new TransitiveAndDeltaUsersSenderFunction(
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<TransitiveAndDeltaUsersSenderFunction>.Instance,
                    storage,
                    calculator);
                var merged = await merge.SendUsersAsync(new TransitiveAndDeltaUsersSenderRequest
                {
                    SyncJob = syncJob,
                    ObjectId = sourceGroupId,
                    GroupId = targetGroupId,
                    CurrentPart = 1,
                    TotalParts = 2,
                    IdentityTypes = new List<string> { "users" },
                    WriteStaging = true,
                    ReadKind = GroupMembershipReadKind.DeltaInitial,
                    FileTimestamp = fileTimestamp
                });
                var request = SelectionRequest(syncJob, sourceGroupId, targetGroupId, GroupMembershipReadKind.DeltaInitial, merged.FilePath);
                var result = await RunPipelineAsync(pipeline, request);
                Assert.AreEqual(SelectionOutcomeStatus.Completed, result.Outcome.Status, result.Outcome.ToString());

                await new CacheUploaderFunction(
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<CacheUploaderFunction>.Instance,
                    calculator).SendUsersAsync(new CacheUploaderRequest
                    {
                        ObjectId = sourceGroupId,
                        MembershipFileResult = new GroupMembershipFileResult { FilePath = request.StagingPath },
                        CurrentPart = 1,
                        TotalParts = 2,
                        SyncJob = syncJob,
                        FileTimestamp = fileTimestamp
                    });
                await new DeltaLinkUploaderFunction(
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<DeltaLinkUploaderFunction>.Instance,
                    calculator).SendDeltaLinkAsync(new DeltaLinkUploaderRequest
                    {
                        ObjectId = sourceGroupId,
                        DeltaLink = "new-delta",
                        CurrentPart = 1,
                        TotalParts = 2,
                        SyncJob = syncJob,
                        FileTimestamp = fileTimestamp
                    });
                publications.AddRange(storage.WriteCalls);
            }

            // 2. One-type saved delta: saved-delta processing publishes staging, cache and delta link; then selection.
            // 3. Both-types saved delta: saved-delta processing publishes the part, cache and delta link.
            foreach (var oneType in new[] { true, false })
            {
                var sourceGroupId = Guid.NewGuid();
                var targetGroupId = Guid.NewGuid();
                var syncJob = new SyncJob { Id = Guid.NewGuid(), RunId = Guid.NewGuid(), Query = "[{\"type\":\"GroupMembership\"}]", TargetOfficeGroupId = targetGroupId };
                var cached = Enumerable.Range(0, 12).Select(_ => Guid.NewGuid()).ToList();
                cached.ForEach(id => pipeline.DirectoryTypes[id] = id.GetHashCode() % 2 == 0 ? "user" : "agentUser");
                var cachePath = $"cache/original-{sourceGroupId}.txt";
                await storage.UploadFileAsync(cachePath, string.Join(Environment.NewLine, cached));
                storage.WriteCalls.Clear();

                var process = new ProcessCachedAndDeltaUsersFunction(
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<ProcessCachedAndDeltaUsersFunction>.Instance,
                    CreateWriteOnceCalculator(storage, targetGroupId),
                    storage);
                var processed = await process.RunAsync(new ProcessCachedAndDeltaUsersRequest
                {
                    SyncJob = syncJob,
                    SourceGroupId = sourceGroupId,
                    TargetGroupId = targetGroupId,
                    CacheFilePath = cachePath,
                    DeltaUrl = "replacement-delta",
                    CountOfUsersFromAADGroup = cached.Count,
                    CurrentPart = 1,
                    TotalParts = 2,
                    IdentityTypes = oneType ? new List<string> { "agents" } : null,
                    WriteStaging = oneType,
                    ReadKind = GroupMembershipReadKind.DeltaLink,
                    FileTimestamp = fileTimestamp
                });
                Assert.IsTrue(processed.CacheMatchesGroupCount);
                if (oneType)
                {
                    var request = SelectionRequest(syncJob, sourceGroupId, targetGroupId, GroupMembershipReadKind.DeltaLink, processed.MembershipFilePath);
                    var result = await RunPipelineAsync(pipeline, request);
                    Assert.AreEqual(SelectionOutcomeStatus.Completed, result.Outcome.Status, result.Outcome.ToString());
                }

                publications.AddRange(storage.WriteCalls);
            }

            var classified = publications
                .Select(call => (Kind: PublicationKind(call.Path), Call: call))
                .Where(entry => entry.Kind != null)
                .ToList();
            var overwriting = classified.Where(entry => !entry.Call.WriteOnce).Select(entry => $"{entry.Kind}: {entry.Call.Api}({entry.Call.Path})").ToList();
            Assert.AreEqual(0, overwriting.Count, "Publications written through a non-write-once API:" + Environment.NewLine + string.Join(Environment.NewLine, overwriting));
            foreach (var kind in new[] { "staging", "manifest", "receipt", "part", "cache", "delta link" })
            {
                Assert.IsTrue(classified.Any(entry => entry.Kind == kind), $"No {kind} publication was observed; the check would be vacuous.");
            }

            Assert.AreEqual(3, classified.Count(entry => entry.Kind == "part"), "Expected one part file per read.");
        }

        private static async Task AssertFinalizeStragglerCannotTruncateAsync(GroupMembershipReadKind readKind)
        {
            var storage = new InMemoryBlobStorageRepository();
            var pipeline = new SelectionPipelineHarness(storage);
            var sourceGroupId = Guid.NewGuid();
            var members = Enumerable.Range(0, 40).Select(_ => Member(sourceGroupId)).ToList();
            members.ForEach(member => pipeline.DirectoryTypes[member.ObjectId] = member.ObjectId.GetHashCode() % 3 == 0 ? "agentUser" : "user");
            var request = await PipelineRequestAsync(storage, readKind, new[] { "users" }, members, sourceGroupId);
            await RunUntilFinalizeAsync(pipeline, request);

            // Finalize A commits the part.
            var first = await pipeline.Finalize.RunAsync(request, CancellationToken.None);
            Assert.AreEqual(SelectionOutcomeStatus.Completed, first.Outcome.Status, first.Outcome.ToString());
            var committed = storage.GetBytes(request.PartFilePath);
            Assert.IsNotNull(committed);
            Assert.IsTrue(committed.Length > 0);
            Assert.AreEqual(members.Count(member => pipeline.DirectoryTypes[member.ObjectId] == "user"), (await ReadIdsAsync(storage, request.PartFilePath)).Count);

            // Duplicate finalize A' of the same read is interrupted in the middle of its part write, whichever API it uses.
            var interruptions = 0;
            Task Interrupt(string path)
            {
                if (path == request.PartFilePath)
                {
                    Interlocked.Increment(ref interruptions);
                    throw new IOException("Injected interruption of the duplicate finalize's part write.");
                }

                return Task.CompletedTask;
            }

            storage.DuringOverwriteAsync = Interrupt;
            storage.BeforeConditionalWriteAsync = Interrupt;
            Exception duplicateFailure = null;
            try
            {
                await pipeline.Finalize.RunAsync(request, CancellationToken.None);
            }
            catch (IOException exception)
            {
                duplicateFailure = exception;
            }
            finally
            {
                storage.DuringOverwriteAsync = null;
                storage.BeforeConditionalWriteAsync = null;
            }

            Assert.AreEqual(1, interruptions, "The duplicate finalize's part write was not interrupted.");
            Assert.IsNotNull(duplicateFailure, "The interrupted duplicate finalize should surface the interruption.");
            CollectionAssert.AreEqual(committed, storage.GetBytes(request.PartFilePath), "A straggling duplicate finalize truncated or replaced the committed part.");

            // A later re-run still finds the committed part and reports it as done.
            var rerun = await pipeline.Finalize.RunAsync(request, CancellationToken.None);
            Assert.AreEqual(SelectionOutcomeStatus.Completed, rerun.Outcome.Status);
            CollectionAssert.AreEqual(committed, storage.GetBytes(request.PartFilePath));
        }

        private static GroupPartSelectionRequest SelectionRequest(
            SyncJob syncJob,
            Guid sourceGroupId,
            Guid targetGroupId,
            GroupMembershipReadKind readKind,
            string partFilePath)
        {
            var runId = syncJob.RunId.GetValueOrDefault();
            return new GroupPartSelectionRequest
            {
                SyncJob = syncJob,
                CurrentPart = 1,
                TotalParts = 2,
                SourceGroupId = sourceGroupId,
                TargetGroupId = targetGroupId,
                IdentityTypes = new List<string> { "agents" },
                ReadKind = readKind,
                StagingPath = GroupPartStoragePaths.BuildStagingPath(targetGroupId, runId, 1),
                PartFilePath = partFilePath.TrimStart('/')
            };
        }

        private static SGMembershipCalculator CreateWriteOnceCalculator(IBlobStorageRepository storage, Guid targetGroupId)
        {
            var destinationResolver = new Moq.Mock<IDestinationResolver>();
            destinationResolver
                .Setup(resolver => resolver.ResolveAsync(Moq.It.IsAny<SyncJob>(), Moq.It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<Repositories.Contracts.DestinationResolution.ResolvedDestination>(new Repositories.Contracts.DestinationResolution.ResolvedGroupDestination { ObjectId = targetGroupId }));
            return new SGMembershipCalculator(
                Moq.Mock.Of<IGraphGroupRepository>(),
                storage,
                Moq.Mock.Of<IDatabaseSyncJobsRepository>(),
                destinationResolver.Object,
                Moq.Mock.Of<IServiceBusQueueRepository>(),
                Moq.Mock.Of<IDatabaseDestinationAttributesRepository>(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SGMembershipCalculator>.Instance,
                Moq.Mock.Of<Repositories.Contracts.InjectConfig.IDryRunValue>(),
                Moq.Mock.Of<global::Services.Contracts.ISyncJobStatusService>());
        }

        /// <summary>The kind of a published blob, or null for blobs that are not publications (inputs, segments, records).</summary>
        private static string PublicationKind(string path)
        {
            if (path.Contains("_GroupMembershipStaging_", StringComparison.Ordinal))
            {
                return "staging";
            }

            if (path.Contains("_GroupMembershipSelection_", StringComparison.Ordinal))
            {
                return path.EndsWith("/manifest.json", StringComparison.Ordinal) ? "manifest"
                    : path.Contains("/receipt_", StringComparison.Ordinal) ? "receipt"
                    : null;
            }

            if (path.StartsWith("cache/delta_", StringComparison.Ordinal))
            {
                return "delta link";
            }

            if (path.StartsWith("cache/", StringComparison.Ordinal))
            {
                return path.StartsWith("cache/original-", StringComparison.Ordinal) ? null : "cache";
            }

            return System.Text.RegularExpressions.Regex.IsMatch(path, @"^[0-9a-f\-]{36}/\d{8}-\d{4}_[0-9a-f\-]{36}_GroupMembership_\d+\.json$")
                ? "part"
                : null;
        }

        // ---------------------------------------------------------------- I1 (slice I): cleanup before a replacement read

        /// <summary>
        /// I1 (slice I): an abandoned one-type delta read left an unfiltered page under the merge prefix (a straggling
        /// duplicate page reader uploaded it after upload cleanup). Before the replacement read, cleanup must remove it
        /// and confirm the prefix is empty. When the merge-prefix delete throws, the activity fails, or the
        /// prefix is not empty afterwards, the replacement read is not started and the part fails, publishing nothing:
        /// the replacement read's merge (same run and part prefix) would otherwise publish the agent user in a
        /// people-only part.
        /// </summary>
        [DataTestMethod]
        [DataRow("throws")]
        [DataRow("straggler re-upload")]
        [TestCategory(Category)]
        public async Task I1_ReplacementReadCleanupIncomplete_FailsPartWithoutPublishing(string fault)
        {
            var scenario = new SelectionOrchestrationScenario(new[] { "users" }, savedDeltaLink: false);
            scenario.AddMember("user");
            scenario.AddMember("agentUser");
            var agent = scenario.Members.Last().ObjectId;
            var stragglerPath = scenario.MergePrefix + "straggler-" + Guid.NewGuid() + ".json";
            var reuploaded = false;
            switch (fault)
            {
                case "throws":
                    scenario.Storage.DeleteFailurePaths.Add(stragglerPath);
                    break;
                default:
                    // The duplicate page reader uploads again right after cleanup deleted its first page.
                    scenario.Storage.AfterListedDeleteAsync = async path =>
                    {
                        if (!reuploaded && path == stragglerPath)
                        {
                            reuploaded = true;
                            await scenario.Storage.UploadFileAsync(
                                scenario.MergePrefix + "straggler-again-" + Guid.NewGuid() + ".json",
                                JsonSerializer.Serialize(scenario.Members));
                        }
                    };
                    break;
            }

            // The selection fails, so the part falls back to a replacement (selected full) read.
            scenario.Pipeline.FailBefore = (name, _) =>
            {
                if (name != nameof(GroupPartSelectionPrepareFunction))
                {
                    return null;
                }

                UploadAbandonedPage(scenario, stragglerPath);
                return new InvalidOperationException("Injected prepare failure.");
            };

            var failure = await RecordFailureAsync(() => scenario.RunAsync());

            var published = new List<Guid>();
            foreach (var partFile in scenario.PartFiles)
            {
                published.AddRange(await scenario.ReadPartMemberIdsAsync(partFile));
            }

            CollectionAssert.DoesNotContain(published, agent, "A wrong-type (agent) member was published in a people-only part.");
            Assert.AreEqual(0, scenario.PartFiles.Count, "A part file was published although cleanup before the replacement read was incomplete: " + string.Join(", ", scenario.PartFiles));
            Assert.IsNotNull(failure, "The part did not fail although cleanup before the replacement read was incomplete.");
            Assert.IsInstanceOfType(failure, typeof(InvalidOperationException), failure.ToString());
            Assert.IsFalse(scenario.Sequence.Contains(nameof(MembersReaderFunction)), "The replacement read started.");
            var error = scenario.Logger.Entries.SingleOrDefault(entry => entry.EventId.Id == 150050);
            Assert.IsNotNull(error, "The refused replacement read was not logged.");
            Assert.AreEqual(LogLevel.Error, error.Level);
            Assert.AreEqual(0, scenario.CompletionEntries.Count());
            if (fault == "straggler re-upload")
            {
                Assert.IsTrue(reuploaded, "The straggler did not re-upload.");
            }
        }

        /// <summary>
        /// I1 (slice I), happy path: the abandoned page is removed and the prefix confirmed empty, so the part falls
        /// back and publishes exactly the selected full read.
        /// </summary>
        [TestMethod]
        [TestCategory(Category)]
        public async Task I1_ReplacementReadCleanupCompletes_FallsBackAndPublishesSelectedFullRead()
        {
            var scenario = new SelectionOrchestrationScenario(new[] { "users" }, savedDeltaLink: false);
            scenario.AddMember("user");
            scenario.AddMember("agentUser");
            var stragglerPath = scenario.MergePrefix + "straggler-" + Guid.NewGuid() + ".json";
            scenario.Pipeline.FailBefore = (name, _) =>
            {
                if (name != nameof(GroupPartSelectionPrepareFunction))
                {
                    return null;
                }

                UploadAbandonedPage(scenario, stragglerPath);
                return new InvalidOperationException("Injected prepare failure.");
            };

            var response = await scenario.RunAsync();

            StringAssert.Contains(scenario.FallbackEntries.Single().Message, "identification failed");
            Assert.AreEqual(1, scenario.PartFiles.Count, string.Join(", ", scenario.PartFiles));
            Assert.AreEqual(response.FilePath, scenario.PartFiles.Single());
            CollectionAssert.AreEquivalent(scenario.SelectedTrueMembers.ToList(), await scenario.ReadPartMemberIdsAsync(response.FilePath));
            Assert.AreEqual(1, scenario.MergedPageCounts.Last(), "The replacement read merged the abandoned page.");
            Assert.AreEqual(0, scenario.Logger.Entries.Count(entry => entry.EventId.Id == 150050));
            StringAssert.Contains(scenario.CompletionEntries.Single().Message, "Read kind: Full");
        }

        /// <summary>
        /// I1 (slice I): the final cleanup (not before a replacement read) stays best-effort. When it throws or the
        /// activity fails, a completed selected part is still returned.
        /// </summary>
        [DataTestMethod]
        [DataRow("throws")]
        [DataRow("activity failure")]
        [TestCategory(Category)]
        public async Task I1_FinalCleanupFailure_DoesNotFailCompletedPart(string fault)
        {
            var scenario = new SelectionOrchestrationScenario(new[] { "users" }, savedDeltaLink: false);
            scenario.AddMember("user");
            scenario.AddMember("agentUser");
            switch (fault)
            {
                case "throws":
                    scenario.Storage.DeleteFailurePaths.Add(scenario.StagingPath);
                    break;
                default:
                    scenario.Pipeline.FailBefore = (name, _) =>
                        name == nameof(GroupPartCleanupFunction) ? new InvalidOperationException("Injected cleanup failure.") : null;
                    break;
            }

            var response = await scenario.RunAsync();

            Assert.AreEqual(0, scenario.FallbackEntries.Count());
            Assert.AreEqual(1, scenario.PartFiles.Count, string.Join(", ", scenario.PartFiles));
            Assert.AreEqual(response.FilePath, scenario.PartFiles.Single());
            CollectionAssert.AreEquivalent(scenario.SelectedTrueMembers.ToList(), await scenario.ReadPartMemberIdsAsync(response.FilePath));
            Assert.AreEqual(1, scenario.CompletionEntries.Count());
            Assert.AreEqual(0, scenario.Logger.Entries.Count(entry => entry.EventId.Id == 150050));
        }

        private static void UploadAbandonedPage(SelectionOrchestrationScenario scenario, string path) =>
            scenario.Storage.UploadFileAsync(path, JsonSerializer.Serialize(scenario.Members)).GetAwaiter().GetResult();

        private static async Task<Exception> RecordFailureAsync(Func<Task> action)
        {
            try
            {
                await action();
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }
    }
}
