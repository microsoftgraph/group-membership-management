// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.GroupMembershipObtainer;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.ServiceBus;
using Repositories.BlobStorage;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Tests.Services.FailureModel;
using Tests.Services.Infrastructure;
using static Tests.Services.FailureModel.SelectionPipelineAcceptanceTests;

namespace Tests.Services
{
    /// <summary>
    /// Activity-level tests of the bounded selection pipeline (quickstart V6 items 3, 4, 7, 9 and 10). They replace the
    /// tests of the removed time-budgeted <c>GroupPartSelectionFunction</c>.
    /// </summary>
    [TestClass]
    public sealed class GroupPartSelectionPipelineTests
    {
        // ------------------------------------------------------------ prepare (V6.3)

        [DataTestMethod]
        [DataRow(0)]
        [DataRow(1)]
        [DataRow(999)]
        [DataRow(1000)]
        [DataRow(1001)]
        [DataRow(2500)]
        public async Task Prepare_SnapshotCoversEveryStagingMemberOnceAscendingWithRecordedTypeOrUnknown(int memberCount)
        {
            var storage = new InMemoryBlobStorageRepository();
            var pipeline = new SelectionPipelineHarness(storage);
            var sourceGroupId = Guid.NewGuid();
            var members = Enumerable.Range(0, memberCount).Select(_ => Member(sourceGroupId)).ToList();
            // Record types for every third member; every seventh recorded as an agent user.
            var recorded = new Dictionary<Guid, string>();
            for (var i = 0; i < members.Count; i += 3)
            {
                recorded[members[i].ObjectId] = i % 7 == 0 ? "agentUser" : "user";
            }

            await storage.UploadFileAsync(
                GroupPartStoragePaths.BuildRecordedTypesPath(sourceGroupId),
                "{\"schema\":1}\n" + string.Concat(recorded.Select(entry => TypeLine(entry.Key, entry.Value))) + "not json\n{\"id\":\"x\"}\n");
            var request = await PipelineRequestAsync(storage, GroupMembershipReadKind.DeltaInitial, new[] { "users" }, members, sourceGroupId);

            var result = await pipeline.Prepare.RunAsync(request, CancellationToken.None);

            Assert.AreEqual(SelectionOutcomeStatus.Completed, result.Outcome.Status);
            Assert.AreEqual(memberCount, result.StagingCount);
            Assert.AreEqual((memberCount + 999) / 1000, result.SegmentCount);
            var snapshot = new List<(Guid Id, string Type)>();
            var expectedUnknown = new List<int>();
            for (var segment = 0; segment < result.SegmentCount; segment++)
            {
                var content = storage.GetContent(GroupPartStoragePaths.BuildSegmentPath(
                    request.TargetGroupId, RunId(request), request.CurrentPart, result.ExecutionId, segment));
                var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => (Guid.Parse(line.Split(',')[0]), line.Split(',')[1]))
                    .ToList();
                Assert.IsTrue(lines.Count <= 1000);
                if (lines.Any(line => line.Item2 == "unknown"))
                {
                    expectedUnknown.Add(segment);
                }

                snapshot.AddRange(lines);
            }

            CollectionAssert.AreEqual(members.Select(member => member.ObjectId).OrderBy(id => id).ToList(), snapshot.Select(entry => entry.Id).ToList());
            foreach (var (id, type) in snapshot)
            {
                Assert.AreEqual(recorded.TryGetValue(id, out var known) ? known : "unknown", type);
            }

            CollectionAssert.AreEqual(expectedUnknown, result.UnknownSegments);
        }

        [TestMethod]
        public async Task Prepare_UnreadableRecord_EveryMemberUnknown()
        {
            var storage = new InMemoryBlobStorageRepository();
            var pipeline = new SelectionPipelineHarness(storage);
            var sourceGroupId = Guid.NewGuid();
            var members = Enumerable.Range(0, 1500).Select(_ => Member(sourceGroupId)).ToList();
            var recordPath = GroupPartStoragePaths.BuildRecordedTypesPath(sourceGroupId);
            await storage.UploadFileAsync(recordPath, "{\"schema\":1}\n" + string.Concat(members.Select(member => TypeLine(member.ObjectId, "user"))));
            storage.ReadFailurePaths.Add(recordPath);
            var request = await PipelineRequestAsync(storage, GroupMembershipReadKind.DeltaInitial, new[] { "users" }, members, sourceGroupId);

            var result = await pipeline.Prepare.RunAsync(request, CancellationToken.None);

            CollectionAssert.AreEqual(new[] { 0, 1 }, result.UnknownSegments);
            Assert.IsTrue(pipeline.PrepareLogger.Entries.Any(entry => entry.EventId.Id == 150185));
        }

        [TestMethod]
        public async Task Prepare_MissingStaging_IsAnError()
        {
            var storage = new InMemoryBlobStorageRepository();
            var pipeline = new SelectionPipelineHarness(storage);
            var request = await PipelineRequestAsync(storage, GroupMembershipReadKind.DeltaInitial, new[] { "users" }, new[] { Member(Guid.NewGuid()) }, Guid.NewGuid());
            await storage.DeleteFileAsync(request.StagingPath);

            await Assert.ThrowsExceptionAsync<FileNotFoundException>(() => pipeline.Prepare.RunAsync(request, CancellationToken.None));
            Assert.IsNull(storage.GetContent(ManifestPath(request)));
        }

        [TestMethod]
        public async Task Prepare_UnreadableStaging_IsAnErrorWithoutManifest()
        {
            var storage = new InMemoryBlobStorageRepository();
            var pipeline = new SelectionPipelineHarness(storage);
            var request = await PipelineRequestAsync(storage, GroupMembershipReadKind.DeltaInitial, new[] { "users" }, new[] { Member(Guid.NewGuid()) }, Guid.NewGuid());
            await storage.UploadFileAsync(request.StagingPath, "{not membership");

            Exception exception = null;
            try
            {
                await pipeline.Prepare.RunAsync(request, CancellationToken.None);
            }
            catch (Exception thrown)
            {
                exception = thrown;
            }

            Assert.IsInstanceOfType(exception, typeof(JsonException));
            Assert.IsNull(storage.GetContent(ManifestPath(request)));
        }

        [TestMethod]
        public async Task Prepare_HostCancellation_Propagates()
        {
            var storage = new InMemoryBlobStorageRepository();
            var pipeline = new SelectionPipelineHarness(storage);
            var request = await PipelineRequestAsync(storage, GroupMembershipReadKind.DeltaInitial, new[] { "users" }, new[] { Member(Guid.NewGuid()) }, Guid.NewGuid());
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();

            await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => pipeline.Prepare.RunAsync(request, cancelled.Token));
        }

        [TestMethod]
        public async Task Prepare_OneTypeSelectionRequired()
        {
            var storage = new InMemoryBlobStorageRepository();
            var pipeline = new SelectionPipelineHarness(storage);
            var request = await PipelineRequestAsync(storage, GroupMembershipReadKind.DeltaInitial, new[] { "users", "agents" }, new[] { Member(Guid.NewGuid()) }, Guid.NewGuid());

            await Assert.ThrowsExceptionAsync<ArgumentException>(() => pipeline.Prepare.RunAsync(request, CancellationToken.None));
        }

        // ------------------------------------------------------------ classify (V6.4)

        [TestMethod]
        public async Task Classify_ReceiptIsWriteOnce_DuplicateReusesWinner()
        {
            var storage = new InMemoryBlobStorageRepository();
            var pipeline = new SelectionPipelineHarness(storage);
            var member = Member(Guid.NewGuid());
            pipeline.DirectoryTypes[member.ObjectId] = "agentUser";
            var request = await PipelineRequestAsync(storage, GroupMembershipReadKind.DeltaInitial, new[] { "agents" }, new[] { member, Member(Guid.NewGuid()) }, Guid.NewGuid());
            var prepare = await pipeline.Prepare.RunAsync(request, CancellationToken.None);
            var receiptPath = ReceiptPath(request, prepare.ExecutionId, 0);
            // A concurrent duplicate already wrote the receipt.
            await storage.UploadFileAsync(receiptPath, TypeLine(member.ObjectId, "agentUser") + TypeLine(Guid.NewGuid(), "notIdentified"));
            var before = storage.GetBytes(receiptPath);

            var result = await pipeline.Classify.RunAsync(Segment(request, 0), CancellationToken.None);

            Assert.AreEqual(SelectionOutcomeStatus.Completed, result.Outcome.Status);
            Assert.AreEqual(1, result.IdentifiedCount);
            Assert.AreEqual(1, result.NotIdentifiedCount);
            Assert.AreEqual(0, pipeline.LookupCallCount, "An existing receipt must be reused without a lookup.");
            CollectionAssert.AreEqual(before, storage.GetBytes(receiptPath));
            Assert.AreEqual(0, pipeline.ClassifyLogger.Entries.Count(entry => entry.EventId.Id == 150184), "A reused receipt writes no segment entry.");
        }

        [TestMethod]
        public async Task Classify_ConcurrentWriterWins_LoserReturnsWinnersReceipt()
        {
            var storage = new InMemoryBlobStorageRepository();
            var pipeline = new SelectionPipelineHarness(storage);
            var member = Member(Guid.NewGuid());
            pipeline.DirectoryTypes[member.ObjectId] = "user";
            var request = await PipelineRequestAsync(storage, GroupMembershipReadKind.DeltaInitial, new[] { "users" }, new[] { member }, Guid.NewGuid());
            var prepare = await pipeline.Prepare.RunAsync(request, CancellationToken.None);
            var receiptPath = ReceiptPath(request, prepare.ExecutionId, 0);
            storage.BeforeConditionalWriteAsync = async path =>
            {
                if (path == receiptPath)
                {
                    storage.BeforeConditionalWriteAsync = null;
                    await storage.UploadFileAsync(receiptPath, TypeLine(member.ObjectId, "user"));
                }
            };

            var result = await pipeline.Classify.RunAsync(Segment(request, 0), CancellationToken.None);

            Assert.AreEqual(SelectionOutcomeStatus.Completed, result.Outcome.Status);
            Assert.AreEqual(1, result.IdentifiedCount);
            Assert.AreEqual(TypeLine(member.ObjectId, "user"), storage.GetContent(receiptPath));
        }

        [DataTestMethod]
        [DataRow(HttpStatusCode.Forbidden)]
        [DataRow(HttpStatusCode.Unauthorized)]
        public async Task Classify_Refused_ReturnsLookupUnavailableWithoutAnyMarker(HttpStatusCode status)
        {
            var storage = new InMemoryBlobStorageRepository();
            var pipeline = new SelectionPipelineHarness(storage);
            pipeline.LookupOverride = (_, _, _) => Task.FromException<IReadOnlyDictionary<Guid, string>>(new HttpRequestException("refused", null, status));
            var request = await PipelineRequestAsync(storage, GroupMembershipReadKind.DeltaInitial, new[] { "users" }, new[] { Member(Guid.NewGuid()) }, Guid.NewGuid());
            var prepare = await pipeline.Prepare.RunAsync(request, CancellationToken.None);

            var result = await pipeline.Classify.RunAsync(Segment(request, 0), CancellationToken.None);

            Assert.AreEqual(SelectionOutcome.LookupUnavailable((int)status), result.Outcome);
            Assert.IsNull(storage.GetContent(ReceiptPath(request, prepare.ExecutionId, 0)));
            Assert.IsFalse(storage.Paths.Any(path => path.StartsWith("capabilities/", StringComparison.Ordinal)),
                "A refusal falls back for this run only; no capability marker is written (owner, 2026-10-21).");
        }

        [DataTestMethod]
        [DataRow(429)]
        [DataRow(500)]
        [DataRow(502)]
        [DataRow(503)]
        [DataRow(504)]
        [DataRow(0, DisplayName = "transport failure")]
        [DataRow(-1, DisplayName = "HttpClient timeout after its retry")]
        public async Task Classify_RetriesExhausted_ReturnsRetriesExhaustedWithoutReceipt(int status)
        {
            var storage = new InMemoryBlobStorageRepository();
            var pipeline = new SelectionPipelineHarness(storage);
            Exception failure = status switch
            {
                0 => new HttpRequestException("connection reset"),
                -1 => new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.", new TimeoutException()),
                _ => new HttpRequestException("still failing", null, (HttpStatusCode)status),
            };
            pipeline.LookupOverride = (_, _, _) => Task.FromException<IReadOnlyDictionary<Guid, string>>(failure);
            var request = await PipelineRequestAsync(storage, GroupMembershipReadKind.DeltaInitial, new[] { "users" }, new[] { Member(Guid.NewGuid()) }, Guid.NewGuid());
            var prepare = await pipeline.Prepare.RunAsync(request, CancellationToken.None);

            var result = await pipeline.Classify.RunAsync(Segment(request, 0), CancellationToken.None);

            Assert.AreEqual(SelectionOutcomeStatus.RetriesExhausted, result.Outcome.Status, result.Outcome.ToString());
            Assert.IsNull(storage.GetContent(ReceiptPath(request, prepare.ExecutionId, 0)));
            Assert.IsTrue(pipeline.ClassifyLogger.Entries.Any(entry => entry.EventId.Id == 150188));
        }

        [TestMethod]
        public async Task Classify_PassesTheHostCancellationTokenToTheLookup()
        {
            var storage = new InMemoryBlobStorageRepository();
            var pipeline = new SelectionPipelineHarness(storage);
            CancellationToken seen = default;
            var member = Member(Guid.NewGuid());
            pipeline.LookupOverride = (ids, _, token) =>
            {
                seen = token;
                return Task.FromResult<IReadOnlyDictionary<Guid, string>>(ids.ToDictionary(id => id, _ => "user"));
            };
            var request = await PipelineRequestAsync(storage, GroupMembershipReadKind.DeltaInitial, new[] { "users" }, new[] { member }, Guid.NewGuid());
            await pipeline.Prepare.RunAsync(request, CancellationToken.None);
            using var host = new CancellationTokenSource();

            await pipeline.Classify.RunAsync(Segment(request, 0), host.Token);

            Assert.AreEqual(host.Token, seen, "The lookup runs under the host's token alone; there is no activity deadline.");
        }

        [DataTestMethod]
        [DataRow("server-error")]
        [DataRow("malformed")]
        [DataRow("unsupported-type")]
        [DataRow("all-omitted")]
        public async Task Classify_OtherFailures_ReturnFailedWithoutReceipt(string failure)
        {
            var storage = new InMemoryBlobStorageRepository();
            var pipeline = new SelectionPipelineHarness(storage);
            pipeline.LookupOverride = (ids, _, _) => failure switch
            {
                "server-error" => Task.FromException<IReadOnlyDictionary<Guid, string>>(new HttpRequestException("bad request", null, HttpStatusCode.BadRequest)),
                "malformed" => Task.FromException<IReadOnlyDictionary<Guid, string>>(new InvalidOperationException("unreadable response")),
                "unsupported-type" => Task.FromResult<IReadOnlyDictionary<Guid, string>>(ids.ToDictionary(id => id, _ => "#microsoft.graph.servicePrincipal")),
                _ => Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>()),
            };
            var request = await PipelineRequestAsync(storage, GroupMembershipReadKind.DeltaInitial, new[] { "users" }, new[] { Member(Guid.NewGuid()), Member(Guid.NewGuid()) }, Guid.NewGuid());
            var prepare = await pipeline.Prepare.RunAsync(request, CancellationToken.None);

            var result = await pipeline.Classify.RunAsync(Segment(request, 0), CancellationToken.None);

            Assert.AreEqual(SelectionOutcomeStatus.Failed, result.Outcome.Status);
            if (failure == "all-omitted")
            {
                Assert.AreEqual("lookup returned none", result.Outcome.Reason);
            }

            if (failure == "server-error")
            {
                Assert.IsTrue(pipeline.ClassifyLogger.Entries.Any(entry => entry.EventId.Id == 150188 && entry.Message.Contains("400")));
            }

            Assert.IsNull(storage.GetContent(ReceiptPath(request, prepare.ExecutionId, 0)));
        }

        [TestMethod]
        public async Task Classify_SingleIdentifierOmitted_IsNotIdentifiedNotFailure()
        {
            var storage = new InMemoryBlobStorageRepository();
            var pipeline = new SelectionPipelineHarness(storage);
            var request = await PipelineRequestAsync(storage, GroupMembershipReadKind.DeltaInitial, new[] { "users" }, new[] { Member(Guid.NewGuid()) }, Guid.NewGuid());
            await pipeline.Prepare.RunAsync(request, CancellationToken.None);

            var result = await pipeline.Classify.RunAsync(Segment(request, 0), CancellationToken.None);

            Assert.AreEqual(SelectionOutcomeStatus.Completed, result.Outcome.Status);
            Assert.AreEqual(1, result.NotIdentifiedCount);
            var entry = pipeline.ClassifyLogger.Entries.Single(log => log.EventId.Id == 150184);
            StringAssert.Contains(entry.Message, "Classified segment 0/1");
        }

        [TestMethod]
        public async Task Classify_HostCancellation_Propagates()
        {
            var storage = new InMemoryBlobStorageRepository();
            var pipeline = new SelectionPipelineHarness(storage);
            using var host = new CancellationTokenSource();
            pipeline.LookupOverride = (_, _, token) =>
            {
                host.Cancel();
                token.ThrowIfCancellationRequested();
                return Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
            };
            var request = await PipelineRequestAsync(storage, GroupMembershipReadKind.DeltaInitial, new[] { "users" }, new[] { Member(Guid.NewGuid()) }, Guid.NewGuid());
            await pipeline.Prepare.RunAsync(request, CancellationToken.None);

            await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => pipeline.Classify.RunAsync(Segment(request, 0), host.Token));
        }

        // ------------------------------------------------------------ finalize (V6.7)

        [DataTestMethod]
        [DataRow("delete")]
        [DataRow("corrupt")]
        [DataRow("contradict")]
        public async Task Finalize_ClassifiesOnlyFromSnapshot_SharedRecordChangesDoNotMatter(string change)
        {
            var storage = new InMemoryBlobStorageRepository();
            var pipeline = new SelectionPipelineHarness(storage);
            var sourceGroupId = Guid.NewGuid();
            var person = Member(sourceGroupId);
            var agent = Member(sourceGroupId);
            var recordPath = GroupPartStoragePaths.BuildRecordedTypesPath(sourceGroupId);
            await storage.UploadFileAsync(recordPath, "{\"schema\":1}\n" + TypeLine(person.ObjectId, "user") + TypeLine(agent.ObjectId, "agentUser"));
            var request = await PipelineRequestAsync(storage, GroupMembershipReadKind.DeltaInitial, new[] { "users" }, new[] { person, agent }, sourceGroupId);
            await RunUntilFinalizeAsync(pipeline, request);
            switch (change)
            {
                case "delete": await storage.DeleteFileAsync(recordPath); break;
                case "corrupt": await storage.UploadFileAsync(recordPath, "garbage"); break;
                default: await storage.UploadFileAsync(recordPath, TypeLine(person.ObjectId, "agentUser") + TypeLine(agent.ObjectId, "user")); break;
            }

            var result = await pipeline.Finalize.RunAsync(request, CancellationToken.None);

            CollectionAssert.AreEqual(new[] { person.ObjectId }, await ReadIdsAsync(storage, result.FilePath));
            Assert.AreEqual(1, result.UserCount);
            Assert.AreEqual(0, pipeline.LookupCallCount);
        }

        [TestMethod]
        public async Task Finalize_StagingMemberMissingFromSnapshot_FailsUnclassified()
        {
            var storage = new InMemoryBlobStorageRepository();
            var pipeline = new SelectionPipelineHarness(storage);
            var member = Member(Guid.NewGuid());
            pipeline.DirectoryTypes[member.ObjectId] = "user";
            var request = await PipelineRequestAsync(storage, GroupMembershipReadKind.DeltaInitial, new[] { "users" }, new[] { member }, Guid.NewGuid());
            await RunUntilFinalizeAsync(pipeline, request);
            // Simulate a staging blob that differs from the snapshot (never happens with write-once staging).
            await SelectionPipelineHarness.WriteInitialStagingAsync(storage, request.StagingPath, request.TargetGroupId, request.SyncJob, new[] { member, Member(Guid.NewGuid()) });

            var result = await pipeline.Finalize.RunAsync(request, CancellationToken.None);

            Assert.AreEqual(SelectionOutcome.Failed("unclassified member"), result.Outcome);
            Assert.IsNull(storage.GetContent(request.PartFilePath));
        }

        [TestMethod]
        public async Task Finalize_UnknownMemberWithoutReceipt_FailsUnclassified()
        {
            var storage = new InMemoryBlobStorageRepository();
            var pipeline = new SelectionPipelineHarness(storage);
            var request = await PipelineRequestAsync(storage, GroupMembershipReadKind.DeltaInitial, new[] { "users" }, new[] { Member(Guid.NewGuid()) }, Guid.NewGuid());
            await pipeline.Prepare.RunAsync(request, CancellationToken.None);

            var result = await pipeline.Finalize.RunAsync(request, CancellationToken.None);

            Assert.AreEqual(SelectionOutcome.Failed("unclassified member"), result.Outcome);
        }

        [TestMethod]
        public async Task Finalize_InitialUsersOnly_PreservesSelectedRecordsAndEnvelope()
        {
            var storage = new InMemoryBlobStorageRepository();
            var pipeline = new SelectionPipelineHarness(storage);
            var sourceGroupId = Guid.NewGuid();
            var person = new AzureADUser { ObjectId = Guid.NewGuid(), SourceGroup = sourceGroupId, Mail = "p@contoso.com", DisplayName = "Person", SourceGroups = new List<Guid> { sourceGroupId } };
            var agent = new AzureADUser { ObjectId = Guid.NewGuid(), SourceGroup = sourceGroupId };
            pipeline.DirectoryTypes[person.ObjectId] = "user";
            pipeline.DirectoryTypes[agent.ObjectId] = "agentUser";
            var request = await PipelineRequestAsync(storage, GroupMembershipReadKind.DeltaInitial, new[] { "users" }, new[] { person, agent }, sourceGroupId, exclusionary: true);

            var result = await RunPipelineAsync(pipeline, request);

            var membership = JsonSerializer.Deserialize<GroupMembership>(storage.GetContent(result.FilePath));
            Assert.IsTrue(membership.Exclusionary);
            Assert.AreEqual(request.TargetGroupId, membership.Destination.ObjectId);
            Assert.AreEqual(1, membership.SourceMembers.Count);
            Assert.AreEqual(JsonSerializer.Serialize(person), JsonSerializer.Serialize(membership.SourceMembers[0]));
            var entry = pipeline.FinalizeLogger.Entries.Single(log => log.EventId.Id == 150183);
            StringAssert.Contains(entry.Message, "Selected 1 members");
            StringAssert.Contains(entry.Message, "agents excluded 1");
            StringAssert.Contains(entry.Message, "looked up 2 in 1 segments");
            StringAssert.Contains(entry.Message, "Read kind: DeltaInitial; selection: users");
        }

        [TestMethod]
        public async Task Finalize_InitialOutput_IsByteIdenticalToTodaysSerializer()
        {
            var storage = new InMemoryBlobStorageRepository();
            var pipeline = new SelectionPipelineHarness(storage);
            var sourceGroupId = Guid.NewGuid();
            var members = Enumerable.Range(0, 50).Select(_ => new AzureADUser { ObjectId = Guid.NewGuid(), SourceGroup = sourceGroupId, UserPrincipalName = "u@x" }).ToList();
            members.ForEach(member => pipeline.DirectoryTypes[member.ObjectId] = "agentUser");
            var request = await PipelineRequestAsync(storage, GroupMembershipReadKind.DeltaInitial, new[] { "agents" }, members, sourceGroupId);
            var staging = JsonSerializer.Deserialize<GroupMembership>(storage.GetContent(request.StagingPath));

            var result = await RunPipelineAsync(pipeline, request);

            staging.SourceMembers = staging.SourceMembers.OrderBy(member => member.ObjectId).ToList();
            Assert.AreEqual(JsonSerializer.Serialize(staging), storage.GetContent(result.FilePath));
        }

        [TestMethod]
        public async Task Finalize_DeltaLinkAgentsOnly_UsesIdentifierFormatWithSourceGroup()
        {
            var storage = new InMemoryBlobStorageRepository();
            var pipeline = new SelectionPipelineHarness(storage);
            var sourceGroupId = Guid.NewGuid();
            var person = Member(sourceGroupId);
            var agent = Member(sourceGroupId);
            pipeline.DirectoryTypes[person.ObjectId] = "user";
            pipeline.DirectoryTypes[agent.ObjectId] = "agentUser";
            var request = await PipelineRequestAsync(storage, GroupMembershipReadKind.DeltaLink, new[] { "agents" }, new[] { person, agent }, sourceGroupId);

            var result = await RunPipelineAsync(pipeline, request);

            using var document = JsonDocument.Parse(storage.GetContent(result.FilePath));
            var members = document.RootElement.GetProperty("SourceMembers").EnumerateArray().ToList();
            Assert.AreEqual(1, members.Count);
            Assert.AreEqual(agent.ObjectId, members[0].GetProperty("ObjectId").GetGuid());
            Assert.AreEqual(sourceGroupId, members[0].GetProperty("SourceGroup").GetGuid());
            Assert.AreEqual(2, members[0].EnumerateObject().Count());
        }

        [TestMethod]
        public async Task Finalize_PartBytes_AreReadableByRunAttributionExtractor()
        {
            foreach (var readKind in new[] { GroupMembershipReadKind.DeltaInitial, GroupMembershipReadKind.DeltaLink })
            {
                var storage = new InMemoryBlobStorageRepository();
                var pipeline = new SelectionPipelineHarness(storage);
                var members = Enumerable.Range(0, 5).Select(_ => Member(Guid.NewGuid())).ToList();
                members.ForEach(member => pipeline.DirectoryTypes[member.ObjectId] = "user");
                var request = await PipelineRequestAsync(storage, readKind, new[] { "users" }, members, Guid.NewGuid());

                var result = await RunPipelineAsync(pipeline, request);

                var extracted = await storage.ExtractGroupMembershipSourceMembersAsync(result.FilePath);
                CollectionAssert.AreEquivalent(members.Select(member => member.ObjectId).ToList(), extracted.ToList());
            }
        }

        [TestMethod]
        public async Task Finalize_LogsNotIdentifiedIdsInBatchesOfHundred()
        {
            var storage = new InMemoryBlobStorageRepository();
            var pipeline = new SelectionPipelineHarness(storage);
            var members = Enumerable.Range(0, 250).Select(_ => Member(Guid.NewGuid())).ToList();
            pipeline.DirectoryTypes[members[0].ObjectId] = "user";
            var request = await PipelineRequestAsync(storage, GroupMembershipReadKind.DeltaInitial, new[] { "users" }, members, Guid.NewGuid());

            var result = await RunPipelineAsync(pipeline, request);

            Assert.AreEqual(249, result.NotIdentifiedCount);
            var entries = pipeline.FinalizeLogger.Entries.Where(entry => entry.EventId.Id == 150187).ToList();
            Assert.AreEqual(3, entries.Count);
            Assert.IsTrue(entries.All(entry => entry.Level == LogLevel.Warning));
            StringAssert.Contains(entries[0].Message, "(1/3)");
            var logged = entries.SelectMany(entry => ((string)entry.State["MemberIds"]).Split(',')).Select(Guid.Parse).ToList();
            CollectionAssert.AreEquivalent(members.Skip(1).Select(member => member.ObjectId).ToList(), logged);
        }

        [TestMethod]
        public async Task Finalize_LoggingFailures_DoNotChangeResults()
        {
            var storage = new InMemoryBlobStorageRepository();
            var pipeline = new SelectionPipelineHarness(storage);
            var members = Enumerable.Range(0, 3).Select(_ => Member(Guid.NewGuid())).ToList();
            pipeline.DirectoryTypes[members[0].ObjectId] = "user";
            var request = await PipelineRequestAsync(storage, GroupMembershipReadKind.DeltaInitial, new[] { "users" }, members, Guid.NewGuid());
            var prepare = await pipeline.Prepare.RunAsync(request, CancellationToken.None);
            var classify = new GroupPartSegmentClassifyFunction(storage, pipeline.GraphRepository.Object, new ThrowingLogger<GroupPartSegmentClassifyFunction>(150184));
            var finalize = new GroupPartSelectionFinalizeFunction(storage, new ThrowingLogger<GroupPartSelectionFinalizeFunction>(150183, 150187));

            var segment = await classify.RunAsync(Segment(request, 0), CancellationToken.None);
            var result = await finalize.RunAsync(request, CancellationToken.None);

            Assert.AreEqual(SelectionOutcomeStatus.Completed, segment.Outcome.Status);
            Assert.AreEqual(SelectionOutcomeStatus.Completed, result.Outcome.Status);
            Assert.AreEqual(1, result.MemberCount);
            Assert.AreEqual(prepare.ExecutionId, prepare.ExecutionId);
        }

        [TestMethod]
        public async Task Finalize_PartWriteFails_Throws()
        {
            var storage = new InMemoryBlobStorageRepository();
            var pipeline = new SelectionPipelineHarness(storage);
            var member = Member(Guid.NewGuid());
            pipeline.DirectoryTypes[member.ObjectId] = "user";
            var request = await PipelineRequestAsync(storage, GroupMembershipReadKind.DeltaLink, new[] { "users" }, new[] { member }, Guid.NewGuid());
            await RunUntilFinalizeAsync(pipeline, request);
            storage.WriteFailurePaths.Add(request.PartFilePath);

            await Assert.ThrowsExceptionAsync<IOException>(() => pipeline.Finalize.RunAsync(request, CancellationToken.None));
        }

        // ------------------------------------------------------------ record types (V6.9)

        [TestMethod]
        public async Task RecordTypes_PrunesToStagingSet_KeepsKnownTypes_DropsDisagreement()
        {
            var storage = new InMemoryBlobStorageRepository();
            var pipeline = new SelectionPipelineHarness(storage);
            var sourceGroupId = Guid.NewGuid();
            var kept = Member(sourceGroupId);
            var departed = Guid.NewGuid();
            var looked = Member(sourceGroupId);
            var recordPath = GroupPartStoragePaths.BuildRecordedTypesPath(sourceGroupId);
            await storage.UploadFileAsync(recordPath, "{\"schema\":1}\n" + TypeLine(kept.ObjectId, "user") + TypeLine(departed, "agentUser") + "garbage\n");
            pipeline.DirectoryTypes[looked.ObjectId] = "agentUser";
            var request = await PipelineRequestAsync(storage, GroupMembershipReadKind.DeltaInitial, new[] { "users" }, new[] { kept, looked }, sourceGroupId);
            await RunUntilFinalizeAsync(pipeline, request);

            var result = await pipeline.RecordTypes.RunAsync(request, CancellationToken.None);

            Assert.IsTrue(result.Written);
            var expected = new[] { (kept.ObjectId, "user"), (looked.ObjectId, "agentUser") }.OrderBy(entry => entry.Item1);
            Assert.AreEqual("{\"schema\":1}\n" + string.Concat(expected.Select(entry => TypeLine(entry.Item1, entry.Item2))), storage.GetContent(recordPath));

            // A later receipt that disagrees with the recorded type drops the identifier for re-lookup.
            var conflicting = await PipelineRequestAsync(storage, GroupMembershipReadKind.DeltaInitial, new[] { "users" }, new[] { kept, looked }, sourceGroupId);
            var conflictPipeline = new SelectionPipelineHarness(storage);
            await storage.DeleteFileAsync(recordPath);
            conflictPipeline.DirectoryTypes[kept.ObjectId] = "agentUser";
            conflictPipeline.DirectoryTypes[looked.ObjectId] = "agentUser";
            await RunUntilFinalizeAsync(conflictPipeline, conflicting);
            await storage.UploadFileAsync(recordPath, "{\"schema\":1}\n" + TypeLine(kept.ObjectId, "user") + TypeLine(looked.ObjectId, "agentUser"));
            await conflictPipeline.RecordTypes.RunAsync(conflicting, CancellationToken.None);
            Assert.AreEqual("{\"schema\":1}\n" + TypeLine(looked.ObjectId, "agentUser"), storage.GetContent(recordPath));
        }

        [TestMethod]
        public async Task RecordTypes_NeverWritesNotIdentified_AndFailureIsCaught()
        {
            var storage = new InMemoryBlobStorageRepository();
            var pipeline = new SelectionPipelineHarness(storage);
            var sourceGroupId = Guid.NewGuid();
            var omitted = Member(sourceGroupId);
            var request = await PipelineRequestAsync(storage, GroupMembershipReadKind.DeltaInitial, new[] { "users" }, new[] { omitted }, sourceGroupId);
            await RunUntilFinalizeAsync(pipeline, request);
            var recordPath = GroupPartStoragePaths.BuildRecordedTypesPath(sourceGroupId);

            var result = await pipeline.RecordTypes.RunAsync(request, CancellationToken.None);
            Assert.IsFalse((storage.GetContent(recordPath) ?? string.Empty).Contains(omitted.ObjectId.ToString()));

            storage.ReadFailurePaths.Add(recordPath);
            var failed = await pipeline.RecordTypes.RunAsync(request, CancellationToken.None);
            Assert.IsTrue(failed.Failed);
            Assert.IsTrue(pipeline.RecordTypesLogger.Entries.Any(entry => entry.EventId.Id == 150186));
            Assert.IsFalse(result.Failed);
        }

        // ------------------------------------------------------------ cleanup (V6.10)

        [TestMethod]
        public async Task Cleanup_DeletesOnlyThisPartsBlobs_AndAbandonedPartAndPagesBeforeReplacement()
        {
            var storage = new InMemoryBlobStorageRepository();
            var pipeline = new SelectionPipelineHarness(storage);
            var target = Guid.NewGuid();
            var run = Guid.NewGuid();
            var keep = new[]
            {
                GroupPartStoragePaths.BuildStagingPath(target, run, 10),
                GroupPartStoragePaths.BuildSelectionFolder(target, run, 10) + "manifest.json",
                GroupPartStoragePaths.BuildMergePrefix(target, run, 10) + "page.json",
                GroupPartStoragePaths.BuildSelectionFolder(target, Guid.NewGuid(), 1) + "manifest.json",
                $"{target}/10202026-1200_{run}_GroupMembership_10.json",
                $"{target}/10202026-1201_{run}_GroupMembership_1.json"
            };
            var abandoned = $"{target}/10202026-1200_{run}_GroupMembership_1.json";
            var remove = new[]
            {
                GroupPartStoragePaths.BuildStagingPath(target, run, 1),
                GroupPartStoragePaths.BuildSelectionFolder(target, run, 1) + "manifest.json",
                GroupPartStoragePaths.BuildSelectionFolder(target, run, 1) + Guid.NewGuid() + "/segment_00000.txt",
                GroupPartStoragePaths.BuildMergePrefix(target, run, 1) + "page.json",
                abandoned
            };
            foreach (var path in keep.Concat(remove))
            {
                await storage.UploadFileAsync(path, "x");
            }

            await pipeline.Cleanup.RunAsync(new GroupPartCleanupRequest
            {
                SyncJob = new SyncJob { Id = Guid.NewGuid(), RunId = run },
                CurrentPart = 1,
                TotalParts = 10,
                TargetGroupId = target,
                AbandonedPartFilePath = abandoned,
                BeforeReplacementRead = true
            });

            CollectionAssert.AreEquivalent(keep, storage.Paths.ToList());
        }

        [TestMethod]
        public async Task Cleanup_WithoutReplacement_KeepsMergePages()
        {
            var storage = new InMemoryBlobStorageRepository();
            var pipeline = new SelectionPipelineHarness(storage);
            var target = Guid.NewGuid();
            var run = Guid.NewGuid();
            var page = GroupPartStoragePaths.BuildMergePrefix(target, run, 1) + "page.json";
            await storage.UploadFileAsync(page, "x");
            await storage.UploadFileAsync(GroupPartStoragePaths.BuildStagingPath(target, run, 1), "x");

            await pipeline.Cleanup.RunAsync(new GroupPartCleanupRequest
            {
                SyncJob = new SyncJob { Id = Guid.NewGuid(), RunId = run },
                CurrentPart = 1,
                TotalParts = 1,
                TargetGroupId = target
            });

            CollectionAssert.AreEqual(new[] { page }, storage.Paths.ToList());
        }

        // ------------------------------------------------------------ production repository members

        [TestMethod]
        public async Task ProductionStreamWriter_MatchesSerializer_AndSourceGroupWriterReadsBack()
        {
            var envelope = new GroupMembership
            {
                Destination = new AzureADGroup { ObjectId = Guid.NewGuid() },
                RunId = Guid.NewGuid(),
                SyncJobId = Guid.NewGuid(),
                Exclusionary = true,
                Query = "[]"
            };
            var members = Enumerable.Range(0, 3).Select(_ => new AzureADUser { ObjectId = Guid.NewGuid(), Mail = "m" }).ToList();
            using var stream = new MemoryStream();
            var count = await BlobStorageRepository.WriteGroupMembershipStreamAsync(stream, envelope, members.AsTestAsyncStream(), CancellationToken.None);
            envelope.SourceMembers = members;
            Assert.AreEqual(3, count);
            Assert.AreEqual(JsonSerializer.Serialize(envelope), System.Text.Encoding.UTF8.GetString(stream.ToArray()));

            var sourceGroupId = Guid.NewGuid();
            using var identifiers = new MemoryStream();
            await BlobStorageRepository.WriteGroupMembershipFromGuidsAsync(identifiers, members.Select(member => member.ObjectId).ToList(), envelope.Destination, envelope.RunId, envelope.SyncJobId, true, false, "[]", CancellationToken.None, sourceGroupId);
            identifiers.Position = 0;
            var read = new List<AzureADUser>();
            await foreach (var member in MembershipStream.ReadAsync(identifiers, null, CancellationToken.None))
            {
                read.Add(member);
            }

            Assert.IsTrue(read.All(member => member.SourceGroup == sourceGroupId));
            CollectionAssert.AreEquivalent(members.Select(member => member.ObjectId).ToList(), read.Select(member => member.ObjectId).ToList());
        }
    }

    internal static class AsyncEnumerableTestExtensions
    {
        public static async IAsyncEnumerable<T> AsTestAsyncStream<T>(this IEnumerable<T> source)
        {
            foreach (var item in source)
            {
                await Task.Yield();
                yield return item;
            }
        }
    }
}
