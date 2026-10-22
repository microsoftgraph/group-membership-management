// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.GroupMembershipObtainer;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.ServiceBus;
using Moq;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.Mocks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Tests.Services.Infrastructure;

namespace Tests.Services
{
    [TestClass]
    public sealed class SubOrchestratorIdentitySelectionTests
    {
        [TestMethod]
        public async Task RunSubOrchestratorAsync_DefaultFullRead_PreservesActivitySequence()
        {
            var harness = CreateHarness(identityTypes: null);
            SetupFullRead(harness, memberCount: 2);

            var response = await harness.Function.RunSubOrchestratorAsync(
                harness.Context.Object);

            Assert.AreEqual("full-part.json", response.FilePath);
            CollectionAssert.AreEqual(
                new[]
                {
                    nameof(GroupValidatorFunction),
                    nameof(GetTransitiveGroupCountFunction),
                    nameof(MembersReaderFunction),
                    nameof(TransitiveAndDeltaUsersSenderFunction),
                    nameof(DeleteBlobFunction),
                    nameof(GetAgentUserCountFunction)
                },
                harness.ActivitySequence);
            Assert.AreEqual(
                1,
                harness.Logger.Entries.Count(entry => entry.EventId.Id == 150202));
        }

        [TestMethod]
        public async Task RunSubOrchestratorAsync_OneTypeFullRead_PassesSelectionAndSkipsAgentTelemetry()
        {
            foreach (var selectedType in new[] { "users", "agents" })
            {
                var harness = CreateHarness(new[] { selectedType });
                MembersReaderRequest readerRequest = null;
                SetupFullRead(
                    harness,
                    memberCount: 1,
                    onReaderRequest: request => readerRequest = request);

                await harness.Function.RunSubOrchestratorAsync(
                    harness.Context.Object);

                Assert.IsNotNull(readerRequest);
                CollectionAssert.AreEqual(
                    new[] { selectedType },
                    readerRequest.IdentityTypes);
                Assert.AreEqual(
                    GroupMembershipReadKind.Full,
                    readerRequest.ReadKind);
                Assert.IsFalse(
                    harness.ActivitySequence.Contains(
                        nameof(GetAgentUserCountFunction)));
            }
        }

        [TestMethod]
        public async Task RunSubOrchestratorAsync_SelectedInitialDelta_SelectsAfterCacheStaging()
        {
            var person = new AzureADUser
            {
                ObjectId = Guid.NewGuid(),
                SourceGroup = Guid.NewGuid()
            };
            var agent = new AzureADUser
            {
                ObjectId = Guid.NewGuid(),
                SourceGroup = person.SourceGroup
            };
            var harness = CreateHarness(new[] { "users" }, transitiveGroupCount: 0);
            var selectionFunction = await SetupInitialDeltaRead(
                harness,
                new[] { person, agent },
                new Dictionary<Guid, string>
                {
                    [person.ObjectId] = "user",
                    [agent.ObjectId] = "agentUser"
                });
            CacheUploaderRequest cacheUploadRequest = null;
            GroupMembership cacheSourceMembership = null;
            harness.Context
                .Setup(context => context.CallActivityAsync(
                    It.Is<TaskName>(name =>
                        name.Name == nameof(CacheUploaderFunction)),
                    It.IsAny<CacheUploaderRequest>(),
                    It.IsAny<TaskOptions>()))
                .Returns<TaskName, CacheUploaderRequest, TaskOptions>(
                    (_, request, _) =>
                    {
                        harness.ActivitySequence.Add(
                            nameof(CacheUploaderFunction));
                        cacheUploadRequest = request;
                        cacheSourceMembership =
                            JsonSerializer.Deserialize<GroupMembership>(
                                harness.Storage.GetContent(
                                    request.MembershipFileResult.FilePath));
                        return Task.CompletedTask;
                    });

            var response = await harness.Function.RunSubOrchestratorAsync(
                harness.Context.Object);
            var membership = await ReadMembershipAsync(
                harness.Storage,
                response.FilePath);

            Assert.AreEqual(1, membership.SourceMembers.Count);
            Assert.AreEqual(person.ObjectId, membership.SourceMembers[0].ObjectId);
            Assert.IsTrue(
                harness.ActivitySequence.IndexOf(nameof(GroupPartSelectionPrepareFunction)) >
                harness.ActivitySequence.IndexOf(nameof(DeltaLinkUploaderFunction)));
            Assert.IsTrue(
                harness.ActivitySequence.IndexOf(nameof(GroupPartCleanupFunction)) >
                harness.ActivitySequence.IndexOf(nameof(GroupPartRecordTypesFunction)));
            Assert.IsTrue(
                harness.ActivitySequence.IndexOf(nameof(GroupPartRecordTypesFunction)) >
                harness.ActivitySequence.IndexOf(nameof(GroupPartSelectionFinalizeFunction)));
            Assert.IsFalse(
                harness.ActivitySequence.Contains(
                    nameof(GetAgentUserCountFunction)));
            Assert.IsNull(
                harness.Storage.GetContent(
                    GroupPartStoragePaths.BuildStagingPath(
                        harness.Request.GroupId,
                        harness.Request.SyncJob.RunId.GetValueOrDefault(),
                        harness.Request.CurrentPart)));
            Assert.AreEqual(
                1,
                selectionFunction.FinalizeLogger.Entries.Count(
                    entry => entry.EventId.Id == 150183));
            Assert.AreEqual(
                GroupPartStoragePaths.BuildStagingPath(
                    harness.Request.GroupId,
                    harness.Request.SyncJob.RunId.GetValueOrDefault(),
                    harness.Request.CurrentPart),
                cacheUploadRequest.MembershipFileResult.FilePath);
            Assert.AreEqual(2, cacheUploadRequest.MembershipFileResult.MemberCount);
            CollectionAssert.AreEquivalent(
                new[] { person.ObjectId, agent.ObjectId },
                cacheSourceMembership.SourceMembers
                    .Select(member => member.ObjectId)
                    .ToArray());
            Assert.AreEqual(
                1,
                harness.Logger.Entries.Count(entry => entry.EventId.Id == 150202));
        }

        [DataTestMethod]
        [DataRow(429)]
        [DataRow(503)]
        [DataRow(0, DisplayName = "transport failure")]
        public async Task RunSubOrchestratorAsync_LookupRetriesExhausted_FailsThePartWithoutFallback(int status)
        {
            var members = Enumerable.Range(0, 1001)
                .Select(_ => new AzureADUser { ObjectId = Guid.NewGuid() })
                .ToArray();
            var harness = CreateHarness(new[] { "users" }, transitiveGroupCount: 0);
            var pipeline = await SetupInitialDeltaRead(
                harness,
                members,
                members.ToDictionary(member => member.ObjectId, _ => "user"));
            pipeline.LookupOverride = (_, _, _) => Task.FromException<IReadOnlyDictionary<Guid, string>>(
                status == 0
                    ? new HttpRequestException("connection reset")
                    : new HttpRequestException("still failing", null, (HttpStatusCode)status));
            // Records a fallback full read if one (wrongly) starts; it does not replace the staged members.
            harness.Context
                .Setup(context => context.CallActivityAsync<string>(
                    It.Is<TaskName>(name => name.Name == nameof(MembersReaderFunction)),
                    It.IsAny<MembersReaderRequest>(),
                    It.IsAny<TaskOptions>()))
                .Returns(() =>
                {
                    harness.ActivitySequence.Add(nameof(MembersReaderFunction));
                    return Task.FromResult((string)null);
                });
            var stagingPath = GroupPartStoragePaths.BuildStagingPath(
                harness.Request.GroupId,
                harness.Request.SyncJob.RunId.GetValueOrDefault(),
                harness.Request.CurrentPart);

            await Assert.ThrowsExceptionAsync<GraphRetriesExhaustedException>(
                () => harness.Function.RunSubOrchestratorAsync(harness.Context.Object));

            Assert.AreEqual(1, pipeline.LookupCallCount, "The orchestrator does not retry a lookup the shared policies gave up on.");
            Assert.AreEqual(0, pipeline.TimerWaits.Count, "No Durable timer is used for retries.");
            Assert.IsFalse(harness.ActivitySequence.Contains(nameof(MembersReaderFunction)), "Retry exhaustion must not fall back to the full read.");
            Assert.AreEqual(0, harness.Logger.Entries.Count(entry => entry.EventId.Id == 150049), "Retry exhaustion is not a fallback.");
            Assert.IsNull(harness.Storage.GetContent(stagingPath), "The finally cleanup still removes this part's staging blob.");
        }

        [TestMethod]
        public async Task RunSubOrchestratorAsync_LookupRetriesExhaustedInChildOrchestration_FailsThePart()
        {
            // 101 unknown segments: 100 in the sub-orchestration, 1 in a child orchestration whose lookup fails.
            var members = Enumerable.Range(0, 100_001)
                .Select(_ => new AzureADUser { ObjectId = Guid.NewGuid() })
                .ToArray();
            var harness = CreateHarness(new[] { "users" }, transitiveGroupCount: 0);
            var pipeline = await SetupInitialDeltaRead(
                harness,
                members,
                members.ToDictionary(member => member.ObjectId, _ => "user"));
            var calls = 0;
            pipeline.LookupOverride = (memberIds, _, _) =>
            {
                calls++;
                return calls == 101
                    ? Task.FromException<IReadOnlyDictionary<Guid, string>>(
                        new HttpRequestException("still failing", null, HttpStatusCode.TooManyRequests))
                    : Task.FromResult<IReadOnlyDictionary<Guid, string>>(
                        memberIds.ToDictionary(memberId => memberId, _ => "user"));
            };
            // Records a fallback full read if one (wrongly) starts; it does not replace the staged members.
            harness.Context
                .Setup(context => context.CallActivityAsync<string>(
                    It.Is<TaskName>(name => name.Name == nameof(MembersReaderFunction)),
                    It.IsAny<MembersReaderRequest>(),
                    It.IsAny<TaskOptions>()))
                .Returns(() =>
                {
                    harness.ActivitySequence.Add(nameof(MembersReaderFunction));
                    return Task.FromResult((string)null);
                });

            Exception failure = null;
            try
            {
                await harness.Function.RunSubOrchestratorAsync(harness.Context.Object);
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            var diagnostics = $"calls={calls}; children={pipeline.ChildOrchestrationActivityCounts.Count}; " +
                $"sequence={string.Join(",", harness.ActivitySequence)}; fallback={string.Join(" | ", harness.Logger.Entries.Where(entry => entry.EventId.Id == 150049).Select(entry => entry.Message))}";
            Assert.IsInstanceOfType(failure, typeof(GraphRetriesExhaustedException), diagnostics + "; failure=" + failure);
            Assert.AreEqual(1, pipeline.ChildOrchestrationActivityCounts.Count, diagnostics);
            Assert.AreEqual(101, calls, diagnostics);
            Assert.AreEqual(0, pipeline.TimerWaits.Count);
            Assert.IsFalse(harness.ActivitySequence.Contains(nameof(MembersReaderFunction)));
            Assert.AreEqual(0, harness.Logger.Entries.Count(entry => entry.EventId.Id == 150049));
        }

        [TestMethod]
        public async Task RunSubOrchestratorAsync_SelectedDeltaLink_SelectsOnlyAfterCacheMatch()
        {
            var person = new AzureADUser { ObjectId = Guid.NewGuid() };
            var agent = new AzureADUser { ObjectId = Guid.NewGuid() };
            var harness = CreateHarness(new[] { "agents" }, transitiveGroupCount: 0);
            SetupDeltaLinkRead(
                harness,
                new[] { person.ObjectId, agent.ObjectId },
                new Dictionary<Guid, string>
                {
                    [person.ObjectId] = "user",
                    [agent.ObjectId] = "agentUser"
                },
                cacheMatches: true);

            var response = await harness.Function.RunSubOrchestratorAsync(
                harness.Context.Object);
            var membership = await ReadMembershipAsync(
                harness.Storage,
                response.FilePath);

            Assert.AreEqual(1, membership.SourceMembers.Count);
            Assert.AreEqual(agent.ObjectId, membership.SourceMembers[0].ObjectId);
            Assert.AreEqual(harness.Request.SourceGroup.ObjectId, membership.SourceMembers[0].SourceGroup);
            Assert.IsTrue(
                harness.ActivitySequence.IndexOf(nameof(GroupPartSelectionPrepareFunction)) >
                harness.ActivitySequence.IndexOf(
                    nameof(ProcessCachedAndDeltaUsersFunction)));
        }

        [DataTestMethod]
        [DataRow(nameof(GroupPartSelectionPrepareFunction))]
        [DataRow(nameof(GroupPartSegmentClassifyFunction))]
        [DataRow(nameof(GroupPartSelectionFinalizeFunction))]
        public async Task RunSubOrchestratorAsync_SelectionStepHitsFunctionTimeout_FailsThePartWithoutFallback(string step)
        {
            var members = new[] { new AzureADUser { ObjectId = Guid.NewGuid() } };
            var harness = CreateHarness(new[] { "users" }, transitiveGroupCount: 0);
            var pipeline = await SetupInitialDeltaRead(
                harness,
                members,
                members.ToDictionary(member => member.ObjectId, _ => "user"));
            pipeline.FailBefore = (name, _) => name == step
                ? new Microsoft.Azure.WebJobs.Host.FunctionTimeoutException($"Timeout value of 00:20:00 was exceeded by function: Functions.{step}")
                : null;
            harness.Context
                .Setup(context => context.CallActivityAsync<string>(
                    It.Is<TaskName>(name => name.Name == nameof(MembersReaderFunction)),
                    It.IsAny<MembersReaderRequest>(),
                    It.IsAny<TaskOptions>()))
                .Returns(() =>
                {
                    harness.ActivitySequence.Add(nameof(MembersReaderFunction));
                    return Task.FromResult((string)null);
                });

            await Assert.ThrowsExceptionAsync<GraphRetriesExhaustedException>(
                () => harness.Function.RunSubOrchestratorAsync(harness.Context.Object));

            Assert.IsFalse(harness.ActivitySequence.Contains(nameof(MembersReaderFunction)), "A timed-out selection step must not fall back.");
            Assert.AreEqual(0, harness.Logger.Entries.Count(entry => entry.EventId.Id == 150049), "A function timeout is not a fallback.");
            Assert.IsTrue(harness.ActivitySequence.Contains(nameof(GroupPartCleanupFunction)), "The finally cleanup still runs.");
        }

        [TestMethod]
        public async Task RunSubOrchestratorAsync_ClassifyHitsFunctionTimeoutInChildOrchestration_FailsThePartWithoutFallback()
        {
            // 101 unknown segments: 100 in the sub-orchestration, 1 in a child orchestration whose classify call times out.
            // The parent sees a failed child without inner failure details, so only the child's result can carry it.
            var members = Enumerable.Range(0, 100_001)
                .Select(_ => new AzureADUser { ObjectId = Guid.NewGuid() })
                .ToArray();
            var harness = CreateHarness(new[] { "users" }, transitiveGroupCount: 0);
            var pipeline = await SetupInitialDeltaRead(
                harness,
                members,
                members.ToDictionary(member => member.ObjectId, _ => "user"));
            pipeline.ChildFailuresHideInnerFailure = true;
            pipeline.FailBefore = (name, request) =>
                name == nameof(GroupPartSegmentClassifyFunction) && ((GroupPartSelectionRequest)request).SegmentIndex == 100
                    ? new Microsoft.Azure.WebJobs.Host.FunctionTimeoutException(
                        $"Timeout value of 00:20:00 was exceeded by function: Functions.{nameof(GroupPartSegmentClassifyFunction)}")
                    : null;
            harness.Context
                .Setup(context => context.CallActivityAsync<string>(
                    It.Is<TaskName>(name => name.Name == nameof(MembersReaderFunction)),
                    It.IsAny<MembersReaderRequest>(),
                    It.IsAny<TaskOptions>()))
                .Returns(() =>
                {
                    harness.ActivitySequence.Add(nameof(MembersReaderFunction));
                    return Task.FromResult((string)null);
                });

            await Assert.ThrowsExceptionAsync<GraphRetriesExhaustedException>(
                () => harness.Function.RunSubOrchestratorAsync(harness.Context.Object));

            Assert.AreEqual(1, pipeline.ChildOrchestrationActivityCounts.Count, "The timed-out segment ran in a child orchestration.");
            Assert.IsFalse(harness.ActivitySequence.Contains(nameof(MembersReaderFunction)), "A timed-out classify call must not fall back.");
            Assert.AreEqual(0, harness.Logger.Entries.Count(entry => entry.EventId.Id == 150049), "A function timeout is not a fallback.");
            Assert.IsTrue(harness.ActivitySequence.Contains(nameof(GroupPartCleanupFunction)), "The finally cleanup still runs.");
        }

        [TestMethod]
        public async Task RunSubOrchestratorAsync_SavedDeltaLookupRetriesExhausted_FailsThePartWithoutFallback()
        {
            var member = new AzureADUser { ObjectId = Guid.NewGuid() };
            var harness = CreateHarness(new[] { "users" }, transitiveGroupCount: 0);
            SetupDeltaLinkRead(
                harness,
                new[] { member.ObjectId },
                typeMap: null,
                cacheMatches: true,
                lookupException: new HttpRequestException("still throttled", null, HttpStatusCode.TooManyRequests));
            harness.Context
                .Setup(context => context.CallActivityAsync<string>(
                    It.Is<TaskName>(name => name.Name == nameof(MembersReaderFunction)),
                    It.IsAny<MembersReaderRequest>(),
                    It.IsAny<TaskOptions>()))
                .Returns(() =>
                {
                    harness.ActivitySequence.Add(nameof(MembersReaderFunction));
                    return Task.FromResult((string)null);
                });

            await Assert.ThrowsExceptionAsync<GraphRetriesExhaustedException>(
                () => harness.Function.RunSubOrchestratorAsync(harness.Context.Object));

            Assert.IsFalse(harness.ActivitySequence.Contains(nameof(MembersReaderFunction)), "Retry exhaustion must not fall back from the saved-delta read.");
            Assert.AreEqual(0, harness.Logger.Entries.Count(entry => entry.EventId.Id == 150049), "Retry exhaustion is not a fallback.");
            Assert.IsTrue(harness.ActivitySequence.Contains(nameof(GroupPartCleanupFunction)), "The finally cleanup still runs.");
        }

        [DataTestMethod]
        [DataRow("users")]
        [DataRow("agents")]
        public async Task RunSubOrchestratorAsync_FallbackFullRead_CarriesThePartsSelection(string selection)
        {
            // The fallback full read must request only the selected type, or a people-only part would publish agents.
            var members = new[] { new AzureADUser { ObjectId = Guid.NewGuid() } };
            var harness = CreateHarness(new[] { selection }, transitiveGroupCount: 0);
            var pipeline = await SetupInitialDeltaRead(
                harness,
                members,
                members.ToDictionary(member => member.ObjectId, _ => "user"));
            pipeline.LookupOverride = (_, _, _) => Task.FromException<IReadOnlyDictionary<Guid, string>>(
                new InvalidOperationException("Graph returned an unreadable response."));
            var readerRequests = new List<MembersReaderRequest>();
            harness.Context
                .Setup(context => context.CallActivityAsync<string>(
                    It.Is<TaskName>(name => name.Name == nameof(MembersReaderFunction)),
                    It.IsAny<MembersReaderRequest>(),
                    It.IsAny<TaskOptions>()))
                .Returns<TaskName, object, TaskOptions>((_, input, _) =>
                {
                    readerRequests.Add((MembersReaderRequest)input);
                    return Task.FromException<string>(new InvalidOperationException("stop after the fallback read starts"));
                });

            try
            {
                await harness.Function.RunSubOrchestratorAsync(harness.Context.Object);
            }
            catch (Exception)
            {
                // The stubbed reader stops the run; only the request it received matters here.
            }

            Assert.AreEqual(1, harness.Logger.Entries.Count(entry => entry.EventId.Id == 150049), "The selection failure fell back.");
            Assert.AreEqual(1, readerRequests.Count, "The fallback full read started once.");
            CollectionAssert.AreEqual(new[] { selection }, readerRequests[0].IdentityTypes, "The fallback full read carries the part's selection.");
        }

        [TestMethod]
        public async Task RunSubOrchestratorAsync_FallbackFullReadRetriesExhausted_FailsThePartAndPublishesNothing()
        {
            var members = new[] { new AzureADUser { ObjectId = Guid.NewGuid() } };
            var harness = CreateHarness(new[] { "users" }, transitiveGroupCount: 0);
            var pipeline = await SetupInitialDeltaRead(
                harness,
                members,
                members.ToDictionary(member => member.ObjectId, _ => "user"));
            // An unreadable lookup response is a selection failure, so the part falls back to the selected full read.
            pipeline.LookupOverride = (_, _, _) => Task.FromException<IReadOnlyDictionary<Guid, string>>(
                new InvalidOperationException("Graph returned an unreadable response."));
            harness.Context
                .Setup(context => context.CallActivityAsync<string>(
                    It.Is<TaskName>(name => name.Name == nameof(MembersReaderFunction)),
                    It.IsAny<MembersReaderRequest>(),
                    It.IsAny<TaskOptions>()))
                .Returns(() =>
                {
                    harness.ActivitySequence.Add(nameof(MembersReaderFunction));
                    return ActivityCallWrapper.InvokeAsync<string>(
                        nameof(MembersReaderFunction),
                        () => Task.FromException<string>(new GraphRetriesExhaustedException(
                            "Graph still throttled after the configured retries.", null, HttpStatusCode.TooManyRequests)));
                });

            // The fallback read's exhaustion surfaces as the sub-orchestration's own failure, so the job reaches TransientError.
            var exception = await Assert.ThrowsExceptionAsync<GraphRetriesExhaustedException>(
                () => harness.Function.RunSubOrchestratorAsync(harness.Context.Object));

            Assert.IsInstanceOfType(exception.InnerException, typeof(TaskFailedException));
            Assert.AreEqual(nameof(MembersReaderFunction), ((TaskFailedException)exception.InnerException).TaskName);
            Assert.AreEqual(1, harness.Logger.Entries.Count(entry => entry.EventId.Id == 150049), "The selection failure fell back once.");
            Assert.AreEqual(
                1,
                harness.ActivitySequence.Count(name => name == nameof(TransitiveAndDeltaUsersSenderFunction)),
                "Only the initial read's staging merge ran; the failed fallback read published nothing.");
        }

        [TestMethod]
        public async Task RunSubOrchestratorAsync_SavedDeltaTypeLookupFails_FallsBackWithOnePartFile()
        {
            var member = new AzureADUser { ObjectId = Guid.NewGuid() };
            var harness = CreateHarness(
                new[] { "users" },
                transitiveGroupCount: 0);
            SetupDeltaLinkRead(
                harness,
                new[] { member.ObjectId },
                typeMap: null,
                cacheMatches: true,
                // An unreadable lookup response is a selection failure (fallback). An HttpRequestException would be
                // retry exhaustion, which fails the part instead (owner, 2026-10-21).
                lookupException: new InvalidOperationException(
                    "member-type lookup returned an unreadable response"));
            SetupFallbackFullReadAfterSavedDelta(harness, member);

            var response = await harness.Function.RunSubOrchestratorAsync(
                harness.Context.Object);
            var membership = await ReadMembershipAsync(
                harness.Storage,
                response.FilePath);
            var finalPartFiles = harness.Storage.Paths
                .Where(path => path.EndsWith(
                    ".json",
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();

            Assert.AreEqual("fallback-full.json", response.FilePath);
            CollectionAssert.AreEqual(
                new[] { member.ObjectId },
                membership.SourceMembers
                    .Select(sourceMember => sourceMember.ObjectId)
                    .ToArray());
            Assert.AreEqual(
                1,
                harness.Logger.Entries.Count(entry =>
                    entry.EventId.Id == 150049));
            Assert.IsTrue(
                harness.ActivitySequence.IndexOf(
                    nameof(GroupPartCleanupFunction)) <
                harness.ActivitySequence.IndexOf(
                    nameof(MembersReaderFunction)));
            CollectionAssert.AreEqual(
                new[] { "fallback-full.json" },
                finalPartFiles);
        }

        [TestMethod]
        public async Task RunSubOrchestratorAsync_ExplicitBothInitialDelta_UsesLegacyActivitySequence()
        {
            var harness = CreateHarness(
                new[] { "users", "agents" },
                transitiveGroupCount: 0);
            SetupBothInitialDeltaRead(
                harness,
                new[]
                {
                    new AzureADUser { ObjectId = Guid.NewGuid() },
                    new AzureADUser { ObjectId = Guid.NewGuid() }
                });

            await harness.Function.RunSubOrchestratorAsync(
                harness.Context.Object);

            Assert.IsFalse(
                harness.ActivitySequence.Contains(
                    nameof(GroupPartSelectionPrepareFunction)));
            Assert.IsFalse(
                harness.ActivitySequence.Contains(
                    nameof(GroupPartCleanupFunction)));
            CollectionAssert.AreEqual(
                new[]
                {
                    nameof(GroupValidatorFunction),
                    nameof(GetTransitiveGroupCountFunction),
                    nameof(FileDownloaderFunction),
                    nameof(BlobCheckerFunction),
                    nameof(DeltaUserReaderFunction),
                    nameof(TransitiveAndDeltaUsersSenderFunction),
                    nameof(DeleteBlobFunction),
                    nameof(CacheUploaderFunction),
                    nameof(DeltaLinkUploaderFunction),
                    nameof(GetAgentUserCountFunction)
                },
                harness.ActivitySequence);
        }

        [TestMethod]
        public async Task RunSubOrchestratorAsync_ExplicitBothDeltaLink_UsesNoSelectionActivity()
        {
            var memberId = Guid.NewGuid();
            var harness = CreateHarness(
                new[] { "agents", "users" },
                transitiveGroupCount: 0);
            SetupDeltaLinkRead(
                harness,
                new[] { memberId },
                new Dictionary<Guid, string>(),
                cacheMatches: true);

            var response = await harness.Function.RunSubOrchestratorAsync(
                harness.Context.Object);

            Assert.AreEqual(harness.PartFilePath, response.FilePath);
            Assert.IsFalse(
                harness.ActivitySequence.Contains(
                    nameof(GroupPartSelectionPrepareFunction)));
            Assert.IsTrue(
                harness.ActivitySequence.Contains(
                    nameof(GetAgentUserCountFunction)));
        }

        [TestMethod]
        public async Task RunSubOrchestratorAsync_CacheMismatch_CleansBeforeReplacementAndDoesNotSelectMissingStaging()
        {
            var person = new AzureADUser { ObjectId = Guid.NewGuid() };
            var harness = CreateHarness(new[] { "users" }, transitiveGroupCount: 0);
            SetupDeltaLinkRead(
                harness,
                new[] { person.ObjectId },
                new Dictionary<Guid, string> { [person.ObjectId] = "user" },
                cacheMatches: false);
            await ConfigureInitialReplacementRead(
                harness,
                new[] { person },
                new Dictionary<Guid, string> { [person.ObjectId] = "user" });

            await harness.Function.RunSubOrchestratorAsync(
                harness.Context.Object);

            var cleanupIndex = harness.ActivitySequence.IndexOf(
                nameof(GroupPartCleanupFunction));
            var initialReaderIndex = harness.ActivitySequence.IndexOf(
                nameof(DeltaUserReaderFunction));
            Assert.IsTrue(cleanupIndex >= 0 && cleanupIndex < initialReaderIndex);
            Assert.AreEqual(
                1,
                harness.ActivitySequence.Count(
                    activity => activity == nameof(GroupPartSelectionFinalizeFunction)),
                "The cache-mismatch result has no staging blob and must not be selected.");
        }

        [TestMethod]
        public async Task RunSubOrchestratorAsync_CacheMismatchFallback_LogsOneCompletionAfterReplacement()
        {
            var member = new AzureADUser { ObjectId = Guid.NewGuid() };
            var harness = CreateHarness(new[] { "users" }, transitiveGroupCount: 0);
            SetupDeltaLinkRead(
                harness,
                new[] { member.ObjectId },
                new Dictionary<Guid, string>(),
                cacheMatches: false);
            await ConfigureInitialReplacementRead(
                harness,
                new[] { member },
                typeMap: null,
                lookupException: new InvalidOperationException("lookup failed"));
            SetupFallbackFullRead(harness, member);

            var response = await harness.Function.RunSubOrchestratorAsync(
                harness.Context.Object);

            Assert.AreEqual("fallback-full.json", response.FilePath);
            Assert.AreEqual(
                1,
                harness.Logger.Entries.Count(entry => entry.EventId.Id == 150202));
            var completionEntry = harness.Logger.Entries.Single(
                entry => entry.EventId.Id == 150202);
            StringAssert.Contains(
                completionEntry.Message,
                "Read kind: Full");
            Assert.AreEqual(
                2,
                harness.ActivitySequence.Count(
                    activity => activity == nameof(FileDeleterFunction)));
            Assert.IsTrue(
                harness.ActivitySequence.LastIndexOf(
                    nameof(TransitiveAndDeltaUsersSenderFunction)) <
                harness.ActivitySequence.IndexOf(nameof(FileDeleterFunction)));
        }

        [TestMethod]
        public async Task RunSubOrchestratorAsync_MissingStaging_PrepareFailsAndFallsBack()
        {
            var member = new AzureADUser { ObjectId = Guid.NewGuid() };
            var harness = CreateHarness(new[] { "users" }, transitiveGroupCount: 0);
            await SetupInitialDeltaRead(
                harness,
                new[] { member },
                new Dictionary<Guid, string>
                {
                    [member.ObjectId] = "user"
                });
            SetupFallbackFullRead(harness, member, writeStagingOnFirstMerge: false);

            var response = await harness.Function.RunSubOrchestratorAsync(
                harness.Context.Object);

            Assert.AreEqual("fallback-full.json", response.FilePath);
            StringAssert.Contains(
                harness.Logger.Entries.Single(entry => entry.EventId.Id == 150049).Message,
                "identification failed");
            Assert.IsFalse(harness.ActivitySequence.Contains(nameof(GroupPartSelectionFinalizeFunction)));
            Assert.IsTrue(
                harness.ActivitySequence.IndexOf(nameof(GroupPartCleanupFunction)) <
                harness.ActivitySequence.IndexOf(nameof(MembersReaderFunction)));
        }

        [TestMethod]
        public async Task RunSubOrchestratorAsync_SelectionActivityFails_FallsBackWithIdentificationFailed()
        {
            var member = new AzureADUser { ObjectId = Guid.NewGuid() };
            var harness = CreateHarness(
                new[] { "users" },
                transitiveGroupCount: 0);
            var pipeline = await SetupInitialDeltaRead(
                harness,
                new[] { member },
                new Dictionary<Guid, string>
                {
                    [member.ObjectId] = "user"
                });
            pipeline.FailBefore = (name, _) => name == nameof(GroupPartSelectionFinalizeFunction)
                ? new InvalidOperationException("selection activity failed")
                : null;
            SetupFallbackFullRead(harness, member);

            var response = await harness.Function.RunSubOrchestratorAsync(
                harness.Context.Object);

            Assert.AreEqual("fallback-full.json", response.FilePath);
            StringAssert.Contains(
                harness.Logger.Entries.Single(entry => entry.EventId.Id == 150049).Message,
                "identification failed");
            Assert.IsTrue(
                harness.ActivitySequence.IndexOf(nameof(GroupPartCleanupFunction)) <
                harness.ActivitySequence.IndexOf(nameof(MembersReaderFunction)));
            Assert.AreEqual(
                1,
                harness.Logger.Entries.Count(entry => entry.EventId.Id == 150202));
        }

        [TestMethod]
        public async Task RunSubOrchestratorAsync_FallbackFullReadFails_PartFailsWithoutPartFile()
        {
            var member = new AzureADUser { ObjectId = Guid.NewGuid() };
            var harness = CreateHarness(new[] { "users" }, transitiveGroupCount: 0);
            await SetupInitialDeltaRead(
                harness,
                new[] { member },
                typeMap: null,
                lookupException: new InvalidOperationException("lookup failed"));
            harness.Context
                .Setup(context => context.CallActivityAsync<string>(
                    It.Is<TaskName>(name =>
                        name.Name == nameof(MembersReaderFunction)),
                    It.IsAny<MembersReaderRequest>(),
                    It.IsAny<TaskOptions>()))
                .Returns(() => ActivityCallWrapper.InvokeAsync<string>(
                    nameof(MembersReaderFunction),
                    () => Task.FromException<string>(new InvalidOperationException("full read failed"))));

            await Assert.ThrowsExceptionAsync<TaskFailedException>(
                async () => await harness.Function.RunSubOrchestratorAsync(
                    harness.Context.Object));

            Assert.AreEqual(0, harness.Logger.Entries.Count(entry => entry.EventId.Id == 150202));
            Assert.IsNull(harness.Storage.GetContent(harness.PartFilePath));
            Assert.IsFalse(harness.Storage.Paths.Any(path => path.Contains("_GroupMembershipSelection_")));
        }

        [TestMethod]
        public async Task RunSubOrchestratorAsync_CacheMismatchFallbackCleanupFails_DoesNotLogCompletion()
        {
            var member = new AzureADUser { ObjectId = Guid.NewGuid() };
            var harness = CreateHarness(new[] { "users" }, transitiveGroupCount: 0);
            SetupDeltaLinkRead(
                harness,
                new[] { member.ObjectId },
                new Dictionary<Guid, string>(),
                cacheMatches: false);
            await ConfigureInitialReplacementRead(
                harness,
                new[] { member },
                typeMap: null,
                lookupException: new InvalidOperationException("lookup failed"));
            SetupFallbackFullRead(harness, member);
            harness.Context
                .Setup(context => context.CallActivityAsync(
                    It.Is<TaskName>(name =>
                        name.Name == nameof(FileDeleterFunction)),
                    It.IsAny<FileDeleterRequest>(),
                    It.IsAny<TaskOptions>()))
                .ThrowsAsync(new InvalidOperationException("delete failed"));

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                async () => await harness.Function.RunSubOrchestratorAsync(
                    harness.Context.Object));

            Assert.AreEqual(
                0,
                harness.Logger.Entries.Count(entry => entry.EventId.Id == 150202));
        }

        [TestMethod]
        [DataRow(nameof(FileDeleterFunction), 1)]
        [DataRow(nameof(FileDeleterFunction), 2)]
        [DataRow(nameof(GetAgentUserCountFunction), 1)]
        public async Task RunSubOrchestratorAsync_CacheMismatchTrailingActivityFails_DoesNotLogCompletion(
            string failingActivity,
            int failingCall)
        {
            var member = new AzureADUser { ObjectId = Guid.NewGuid() };
            var harness = CreateHarness(identityTypes: null, transitiveGroupCount: 0);
            SetupBothInitialDeltaRead(harness, new[] { member });
            SetupDeltaLinkRead(
                harness,
                new[] { member.ObjectId },
                new Dictionary<Guid, string>(),
                cacheMatches: false);
            var callCount = 0;

            if (string.Equals(
                failingActivity,
                nameof(FileDeleterFunction),
                StringComparison.Ordinal))
            {
                harness.Context
                    .Setup(context => context.CallActivityAsync(
                        It.Is<TaskName>(name =>
                            name.Name == nameof(FileDeleterFunction)),
                        It.IsAny<FileDeleterRequest>(),
                        It.IsAny<TaskOptions>()))
                    .Returns(() =>
                    {
                        harness.ActivitySequence.Add(nameof(FileDeleterFunction));
                        callCount++;
                        return callCount == failingCall
                            ? Task.FromException(
                                new InvalidOperationException("delete failed"))
                            : Task.CompletedTask;
                    });
            }
            else
            {
                harness.Context
                    .Setup(context => context.CallActivityAsync<int?>(
                        It.Is<TaskName>(name =>
                            name.Name == nameof(GetAgentUserCountFunction)),
                        It.IsAny<GetAgentUserCountRequest>(),
                        It.IsAny<TaskOptions>()))
                    .Returns(() =>
                    {
                        harness.ActivitySequence.Add(
                            nameof(GetAgentUserCountFunction));
                        callCount++;
                        return callCount == failingCall
                            ? Task.FromException<int?>(
                                new InvalidOperationException("telemetry failed"))
                            : Task.FromResult<int?>(0);
                    });
            }

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                async () => await harness.Function.RunSubOrchestratorAsync(
                    harness.Context.Object));

            Assert.AreEqual(
                0,
                harness.Logger.Entries.Count(entry => entry.EventId.Id == 150202));
        }

        [TestMethod]
        public async Task RunSubOrchestratorAsync_IdentificationFailure_CleansAndFallsBackToSelectedFullRead()
        {
            var member = new AzureADUser { ObjectId = Guid.NewGuid() };
            var harness = CreateHarness(new[] { "users" }, transitiveGroupCount: 0);
            await SetupInitialDeltaRead(
                harness,
                new[] { member },
                typeMap: null,
                lookupException: new InvalidOperationException("lookup failed"));
            SetupFallbackFullRead(harness, member);

            var response = await harness.Function.RunSubOrchestratorAsync(
                harness.Context.Object);

            Assert.AreEqual("fallback-full.json", response.FilePath);
            Assert.IsTrue(
                harness.ActivitySequence.IndexOf(nameof(GroupPartCleanupFunction)) <
                harness.ActivitySequence.LastIndexOf(nameof(MembersReaderFunction)));
            Assert.AreEqual(
                1,
                harness.Logger.Entries.Count(entry => entry.EventId.Id == 150049));
            Assert.AreEqual(
                1,
                harness.Logger.Entries.Count(entry => entry.EventId.Id == 150202));
        }

        [TestMethod]
        public async Task RunSubOrchestratorAsync_PartsOneAndTen_CleanupLeavesOtherPartIntact()
        {
            var storage = new InMemoryBlobStorageRepository();
            var targetGroupId = Guid.NewGuid();
            var runId = Guid.NewGuid();
            var member = new AzureADUser { ObjectId = Guid.NewGuid() };
            var tenStaging = GroupPartStoragePaths.BuildStagingPath(targetGroupId, runId, 10);
            var tenFolder = GroupPartStoragePaths.BuildSelectionFolder(targetGroupId, runId, 10);
            var tenPage = GroupPartStoragePaths.BuildMergePrefix(targetGroupId, runId, 10) + "page.json";
            await storage.UploadFileAsync(tenStaging, "part-10-staging");
            await storage.UploadFileAsync(tenFolder + "manifest.json", "part-10-manifest");
            await storage.UploadFileAsync(tenPage, "[]");
            var partOne = CreateHarness(
                new[] { "users" },
                transitiveGroupCount: 0,
                storage: storage,
                targetGroupId: targetGroupId,
                runId: runId,
                currentPart: 1);
            await SetupInitialDeltaRead(
                partOne,
                new[] { member },
                new Dictionary<Guid, string> { [member.ObjectId] = "user" });

            await partOne.Function.RunSubOrchestratorAsync(partOne.Context.Object);

            Assert.AreEqual("part-10-staging", storage.GetContent(tenStaging));
            Assert.AreEqual("part-10-manifest", storage.GetContent(tenFolder + "manifest.json"));
            Assert.AreEqual("[]", storage.GetContent(tenPage));
            Assert.IsFalse(storage.Paths.Any(path => path.Contains("_GroupMembershipSelection_1/")));
            Assert.IsTrue(partOne.ActivitySequence.Contains(nameof(GroupPartCleanupFunction)));
        }

        [TestMethod]
        public async Task RunSubOrchestratorAsync_CleanupFails_ReturnsCompletedSelectedPart()
        {
            var member = new AzureADUser { ObjectId = Guid.NewGuid() };
            var harness = CreateHarness(new[] { "users" }, transitiveGroupCount: 0);
            await SetupInitialDeltaRead(
                harness,
                new[] { member },
                new Dictionary<Guid, string> { [member.ObjectId] = "user" });
            harness.Context
                .Setup(context => context.CallActivityAsync(
                    It.Is<TaskName>(name =>
                        name.Name == nameof(GroupPartCleanupFunction)),
                    It.IsAny<GroupPartCleanupRequest>(),
                    It.IsAny<TaskOptions>()))
                .Callback(() => harness.ActivitySequence.Add(
                    nameof(GroupPartCleanupFunction)))
                .ThrowsAsync(new InvalidOperationException("cleanup failed"));

            var response = await harness.Function.RunSubOrchestratorAsync(
                harness.Context.Object);

            Assert.AreEqual(harness.PartFilePath, response.FilePath);
            Assert.AreEqual(
                1,
                harness.Logger.Entries.Count(entry => entry.EventId.Id == 150048));
        }

        [TestMethod]
        public async Task RunSubOrchestratorAsync_SourceIsDestinationSelectedDelta_DoesNotWriteBeforeSyncCount()
        {
            var member = new AzureADUser { ObjectId = Guid.NewGuid() };
            var harness = CreateHarness(new[] { "users" }, transitiveGroupCount: 0);
            harness.Request.SourceGroup.ObjectId = harness.Request.GroupId;
            SetupDeltaLinkRead(
                harness,
                new[] { member.ObjectId },
                new Dictionary<Guid, string> { [member.ObjectId] = "user" },
                cacheMatches: true);

            await harness.Function.RunSubOrchestratorAsync(
                harness.Context.Object);

            harness.Context.Verify(
                context => context.CallActivityAsync(
                    It.Is<TaskName>(name =>
                        name.Name == nameof(JobStatusUpdaterFunction)),
                    It.IsAny<JobStatusUpdaterRequest>(),
                    It.IsAny<TaskOptions>()),
                Times.Never);
        }

        [TestMethod]
        public async Task RunSubOrchestratorAsync_NestedMembers_EachSelectionProducesExpectedPart()
        {
            var directSourceGroupId = Guid.NewGuid();
            var nestedSourceGroupId = Guid.NewGuid();
            var person = new AzureADUser
            {
                ObjectId = Guid.NewGuid(),
                SourceGroup = nestedSourceGroupId
            };
            var firstAgent = new AzureADUser
            {
                ObjectId = Guid.NewGuid(),
                SourceGroup = nestedSourceGroupId
            };
            var secondAgent = new AzureADUser
            {
                ObjectId = Guid.NewGuid(),
                SourceGroup = directSourceGroupId
            };
            var members = new[] { person, firstAgent, secondAgent };
            var typeMap = new Dictionary<Guid, string>
            {
                [person.ObjectId] = "user",
                [firstAgent.ObjectId] = "agentUser",
                [secondAgent.ObjectId] = "agentUser"
            };
            var cases = new[]
            {
                new
                {
                    IdentityTypes = (IReadOnlyCollection<string>)new[] { "users" },
                    Expected = new[] { person.ObjectId }
                },
                new
                {
                    IdentityTypes = (IReadOnlyCollection<string>)new[] { "agents" },
                    Expected = new[] { firstAgent.ObjectId, secondAgent.ObjectId }
                },
                new
                {
                    IdentityTypes = (IReadOnlyCollection<string>)new[] { "users", "agents" },
                    Expected = members.Select(member => member.ObjectId).ToArray()
                },
                new
                {
                    IdentityTypes = (IReadOnlyCollection<string>)null,
                    Expected = members.Select(member => member.ObjectId).ToArray()
                }
            };

            foreach (var testCase in cases)
            {
                var harness = CreateHarness(
                    testCase.IdentityTypes,
                    transitiveGroupCount: 0);
                harness.Request.SourceGroup.ObjectId = directSourceGroupId;
                if (testCase.IdentityTypes == null ||
                    testCase.IdentityTypes.Count == 2)
                {
                    SetupBothInitialDeltaRead(harness, members);
                }
                else
                {
                    await SetupInitialDeltaRead(harness, members, typeMap);
                }

                var response = await harness.Function.RunSubOrchestratorAsync(
                    harness.Context.Object);
                var membership = await ReadMembershipAsync(
                    harness.Storage,
                    response.FilePath);

                CollectionAssert.AreEquivalent(
                    testCase.Expected,
                    membership.SourceMembers
                        .Select(member => member.ObjectId)
                        .ToArray());
                foreach (var selectedMember in membership.SourceMembers)
                {
                    Assert.AreEqual(
                        members.Single(member =>
                            member.ObjectId == selectedMember.ObjectId).SourceGroup,
                        selectedMember.SourceGroup);
                }
            }
        }

        [TestMethod]
        public async Task RunSubOrchestratorAsync_ExclusionaryAgentsOnly_PreservesExclusionAndAgentMembers()
        {
            var person = new AzureADUser { ObjectId = Guid.NewGuid() };
            var agent = new AzureADUser { ObjectId = Guid.NewGuid() };
            var harness = CreateHarness(
                new[] { "agents" },
                transitiveGroupCount: 0);
            harness.Request.Exclusionary = true;
            await SetupInitialDeltaRead(
                harness,
                new[] { person, agent },
                new Dictionary<Guid, string>
                {
                    [person.ObjectId] = "user",
                    [agent.ObjectId] = "agentUser"
                });

            var response = await harness.Function.RunSubOrchestratorAsync(
                harness.Context.Object);
            var membership = await ReadMembershipAsync(
                harness.Storage,
                response.FilePath);

            Assert.IsTrue(membership.Exclusionary);
            CollectionAssert.AreEqual(
                new[] { agent.ObjectId },
                membership.SourceMembers
                    .Select(member => member.ObjectId)
                    .ToArray());
        }

        [TestMethod]
        public async Task RunSubOrchestratorAsync_SharedSourceSelectionChange_ReusesRecordedTypes()
        {
            var storage = new InMemoryBlobStorageRepository();
            var sourceGroupId = Guid.NewGuid();
            var person = new AzureADUser { ObjectId = Guid.NewGuid() };
            var agent = new AzureADUser { ObjectId = Guid.NewGuid() };
            var members = new[] { person, agent };
            var typeMap = new Dictionary<Guid, string>
            {
                [person.ObjectId] = "user",
                [agent.ObjectId] = "agentUser"
            };
            var agentsHarness = CreateHarness(
                new[] { "agents" },
                transitiveGroupCount: 0,
                storage: storage,
                sourceGroupId: sourceGroupId);
            await SetupInitialDeltaRead(agentsHarness, members, typeMap);

            var agentsResponse =
                await agentsHarness.Function.RunSubOrchestratorAsync(
                    agentsHarness.Context.Object);
            var agentsMembership = await ReadMembershipAsync(
                storage,
                agentsResponse.FilePath);

            var usersHarness = CreateHarness(
                new[] { "users" },
                transitiveGroupCount: 0,
                storage: storage,
                sourceGroupId: sourceGroupId);
            await SetupInitialDeltaRead(
                usersHarness,
                members,
                new Dictionary<Guid, string>());

            var usersResponse =
                await usersHarness.Function.RunSubOrchestratorAsync(
                    usersHarness.Context.Object);
            var usersMembership = await ReadMembershipAsync(
                storage,
                usersResponse.FilePath);

            CollectionAssert.AreEqual(
                new[] { agent.ObjectId },
                agentsMembership.SourceMembers
                    .Select(member => member.ObjectId)
                    .ToArray());
            CollectionAssert.AreEqual(
                new[] { person.ObjectId },
                usersMembership.SourceMembers
                    .Select(member => member.ObjectId)
                    .ToArray());
            Assert.AreEqual(0, usersHarness.SelectionHarness.LookupCallCount);
        }

        [TestMethod]
        public async Task RunSubOrchestratorAsync_TwoPartsShareSourceWithDifferentSelections_ProduceIndependentFiles()
        {
            var storage = new InMemoryBlobStorageRepository();
            var sourceGroupId = Guid.NewGuid();
            var targetGroupId = Guid.NewGuid();
            var syncJobId = Guid.NewGuid();
            var runId = Guid.NewGuid();
            var person = new AzureADUser { ObjectId = Guid.NewGuid() };
            var agent = new AzureADUser { ObjectId = Guid.NewGuid() };
            var members = new[] { person, agent };
            var typeMap = new Dictionary<Guid, string>
            {
                [person.ObjectId] = "user",
                [agent.ObjectId] = "agentUser"
            };
            var usersHarness = CreateHarness(
                new[] { "users" },
                transitiveGroupCount: 0,
                storage: storage,
                sourceGroupId: sourceGroupId,
                targetGroupId: targetGroupId,
                syncJobId: syncJobId,
                runId: runId,
                currentPart: 1);
            await SetupInitialDeltaRead(usersHarness, members, typeMap);
            var agentsHarness = CreateHarness(
                new[] { "agents" },
                transitiveGroupCount: 0,
                storage: storage,
                sourceGroupId: sourceGroupId,
                targetGroupId: targetGroupId,
                syncJobId: syncJobId,
                runId: runId,
                currentPart: 2);
            await SetupInitialDeltaRead(
                agentsHarness,
                members,
                new Dictionary<Guid, string>());

            var usersResponse =
                await usersHarness.Function.RunSubOrchestratorAsync(
                    usersHarness.Context.Object);
            var agentsResponse =
                await agentsHarness.Function.RunSubOrchestratorAsync(
                    agentsHarness.Context.Object);
            var usersMembership = await ReadMembershipAsync(
                storage,
                usersResponse.FilePath);
            var agentsMembership = await ReadMembershipAsync(
                storage,
                agentsResponse.FilePath);

            Assert.AreNotEqual(usersResponse.FilePath, agentsResponse.FilePath);
            CollectionAssert.AreEqual(
                new[] { person.ObjectId },
                usersMembership.SourceMembers
                    .Select(member => member.ObjectId)
                    .ToArray());
            CollectionAssert.AreEqual(
                new[] { agent.ObjectId },
                agentsMembership.SourceMembers
                    .Select(member => member.ObjectId)
                    .ToArray());
            Assert.AreEqual(0, agentsHarness.SelectionHarness.LookupCallCount);
        }

        [TestMethod]
        public async Task RunSubOrchestratorAsync_DeltaLinkAgentRemoval_RemovesAgentFromSelectedPart()
        {
            var storage = new InMemoryBlobStorageRepository();
            var sourceGroupId = Guid.NewGuid();
            var agentId = Guid.NewGuid();
            var firstHarness = CreateHarness(
                new[] { "agents" },
                transitiveGroupCount: 0,
                storage: storage,
                sourceGroupId: sourceGroupId);
            SetupDeltaLinkRead(
                firstHarness,
                new[] { agentId },
                new Dictionary<Guid, string> { [agentId] = "agentUser" },
                cacheMatches: true);

            var firstResponse =
                await firstHarness.Function.RunSubOrchestratorAsync(
                    firstHarness.Context.Object);
            var firstMembership = await ReadMembershipAsync(
                storage,
                firstResponse.FilePath);

            var secondHarness = CreateHarness(
                new[] { "agents" },
                transitiveGroupCount: 0,
                storage: storage,
                sourceGroupId: sourceGroupId);
            SetupDeltaLinkRead(
                secondHarness,
                Array.Empty<Guid>(),
                new Dictionary<Guid, string>(),
                cacheMatches: true);

            var secondResponse =
                await secondHarness.Function.RunSubOrchestratorAsync(
                    secondHarness.Context.Object);
            var secondMembership = await ReadMembershipAsync(
                storage,
                secondResponse.FilePath);

            CollectionAssert.AreEqual(
                new[] { agentId },
                firstMembership.SourceMembers
                    .Select(member => member.ObjectId)
                    .ToArray());
            Assert.AreEqual(0, secondMembership.SourceMembers.Count);
        }

        private static OrchestrationHarness CreateHarness(
            IReadOnlyCollection<string> identityTypes,
            int transitiveGroupCount = 1,
            InMemoryBlobStorageRepository storage = null,
            Guid? sourceGroupId = null,
            Guid? targetGroupId = null,
            Guid? syncJobId = null,
            Guid? runId = null,
            int currentPart = 1)
        {
            var effectiveRunId = runId ?? Guid.NewGuid();
            var effectiveTargetGroupId = targetGroupId ?? Guid.NewGuid();
            var request = new GroupMembershipRequest
            {
                SyncJob = new SyncJob
                {
                    Id = syncJobId ?? Guid.NewGuid(),
                    RunId = effectiveRunId,
                    Query = "[{\"type\":\"GroupMembership\"}]"
                },
                SourceGroup = new AzureADGroup
                {
                    ObjectId = sourceGroupId ?? Guid.NewGuid()
                },
                GroupId = effectiveTargetGroupId,
                CurrentPart = currentPart,
                TotalParts = 2,
                IdentityTypes = identityTypes?.ToList()
            };
            var context = new Mock<TaskOrchestrationContext>();
            var logger = new RecordingLogger<SubOrchestratorFunction>();
            storage ??= new InMemoryBlobStorageRepository();
            var sequence = new List<string>();
            context
                .Setup(orchestrationContext =>
                    orchestrationContext.GetInput<GroupMembershipRequest>())
                .Returns(request);
            context
                .Setup(orchestrationContext =>
                    orchestrationContext.CreateReplaySafeLogger(
                        It.IsAny<string>()))
                .Returns(logger);
            context
                .SetupGet(orchestrationContext =>
                    orchestrationContext.IsReplaying)
                .Returns(false);
            context
                .Setup(orchestrationContext =>
                    orchestrationContext.CallActivityAsync<bool>(
                        It.Is<TaskName>(name =>
                            name.Name == nameof(GroupValidatorFunction)),
                        It.IsAny<GroupValidatorRequest>(),
                        It.IsAny<TaskOptions>()))
                .Returns(() =>
                {
                    sequence.Add(nameof(GroupValidatorFunction));
                    return Task.FromResult(true);
                });
            context
                .Setup(orchestrationContext =>
                    orchestrationContext.CallActivityAsync<int>(
                        It.Is<TaskName>(name =>
                            name.Name == nameof(GetTransitiveGroupCountFunction)),
                        It.IsAny<GetTransitiveGroupCountRequest>(),
                        It.IsAny<TaskOptions>()))
                .Returns(() =>
                {
                    sequence.Add(nameof(GetTransitiveGroupCountFunction));
                    return Task.FromResult(transitiveGroupCount);
                });

            var deltaCachingConfig = new MockDeltaCachingConfig
            {
                DeltaCacheEnabled = true
            };
            return new OrchestrationHarness(
                context,
                logger,
                storage,
                request,
                sequence,
                new SubOrchestratorFunction(
                    deltaCachingConfig,
                    new TelemetryClient(new TelemetryConfiguration())),
                $"{effectiveTargetGroupId}/01012026-0000_{effectiveRunId}_GroupMembership_{currentPart}.json");
        }

        private static void SetupFullRead(
            OrchestrationHarness harness,
            int memberCount,
            Action<MembersReaderRequest> onReaderRequest = null)
        {
            harness.Context
                .Setup(context => context.CallActivityAsync<string>(
                    It.Is<TaskName>(name =>
                        name.Name == nameof(MembersReaderFunction)),
                    It.IsAny<MembersReaderRequest>(),
                    It.IsAny<TaskOptions>()))
                .Returns<TaskName, MembersReaderRequest, TaskOptions>(
                    (_, request, _) =>
                    {
                        harness.ActivitySequence.Add(
                            nameof(MembersReaderFunction));
                        onReaderRequest?.Invoke(request);
                        return Task.FromResult((string)null);
                    });
            harness.Context
                .Setup(context =>
                    context.CallActivityAsync<GroupMembershipFileResult>(
                        It.Is<TaskName>(name =>
                            name.Name ==
                            nameof(TransitiveAndDeltaUsersSenderFunction)),
                        It.IsAny<TransitiveAndDeltaUsersSenderRequest>(),
                        It.IsAny<TaskOptions>()))
                .Returns(() =>
                {
                    harness.ActivitySequence.Add(
                        nameof(TransitiveAndDeltaUsersSenderFunction));
                    return Task.FromResult(new GroupMembershipFileResult
                    {
                        FilePath = "full-part.json",
                        MemberCount = memberCount
                    });
                });
            SetupDeleteUploadActivity(harness);
            harness.Context
                .Setup(context => context.CallActivityAsync<int?>(
                    It.Is<TaskName>(name =>
                        name.Name == nameof(GetAgentUserCountFunction)),
                    It.IsAny<GetAgentUserCountRequest>(),
                    It.IsAny<TaskOptions>()))
                .Returns(() =>
                {
                    harness.ActivitySequence.Add(
                        nameof(GetAgentUserCountFunction));
                    return Task.FromResult<int?>(0);
                });
        }

        private static async Task<SelectionPipelineHarness> SetupInitialDeltaRead(
            OrchestrationHarness harness,
            IReadOnlyCollection<AzureADUser> members,
            IReadOnlyDictionary<Guid, string> typeMap,
            Exception lookupException = null)
        {
            harness.Context
                .Setup(context => context.CallActivityAsync<string>(
                    It.Is<TaskName>(name =>
                        name.Name == nameof(FileDownloaderFunction)),
                    It.IsAny<FileDownloaderRequest>(),
                    It.IsAny<TaskOptions>()))
                .Returns(() =>
                {
                    harness.ActivitySequence.Add(
                        nameof(FileDownloaderFunction));
                    return Task.FromResult(string.Empty);
                });
            harness.Context
                .Setup(context => context.CallActivityAsync<BlobResult>(
                    It.Is<TaskName>(name =>
                        name.Name == nameof(BlobCheckerFunction)),
                    It.IsAny<BlobCheckerRequest>(),
                    It.IsAny<TaskOptions>()))
                .Returns(() =>
                {
                    harness.ActivitySequence.Add(nameof(BlobCheckerFunction));
                    return Task.FromResult(new BlobResult
                    {
                        BlobStatus = BlobStatus.NotFound
                    });
                });
            await ConfigureInitialReplacementRead(
                harness,
                members,
                typeMap,
                lookupException);
            return harness.SelectionHarness;
        }

        private static void SetupBothInitialDeltaRead(
            OrchestrationHarness harness,
            IReadOnlyCollection<AzureADUser> members)
        {
            harness.Context
                .Setup(context => context.CallActivityAsync<string>(
                    It.Is<TaskName>(name =>
                        name.Name == nameof(FileDownloaderFunction)),
                    It.IsAny<FileDownloaderRequest>(),
                    It.IsAny<TaskOptions>()))
                .Returns(() =>
                {
                    harness.ActivitySequence.Add(
                        nameof(FileDownloaderFunction));
                    return Task.FromResult(string.Empty);
                });
            harness.Context
                .Setup(context => context.CallActivityAsync<BlobResult>(
                    It.Is<TaskName>(name =>
                        name.Name == nameof(BlobCheckerFunction)),
                    It.IsAny<BlobCheckerRequest>(),
                    It.IsAny<TaskOptions>()))
                .Returns(() =>
                {
                    harness.ActivitySequence.Add(nameof(BlobCheckerFunction));
                    return Task.FromResult(new BlobResult
                    {
                        BlobStatus = BlobStatus.NotFound
                    });
                });
            harness.Context
                .Setup(context => context.CallActivityAsync<DeltaUrls>(
                    It.Is<TaskName>(name =>
                        name.Name == nameof(DeltaUserReaderFunction)),
                    It.IsAny<DeltaUserReaderRequest>(),
                    It.IsAny<TaskOptions>()))
                .Returns(() =>
                {
                    harness.ActivitySequence.Add(
                        nameof(DeltaUserReaderFunction));
                    return Task.FromResult(new DeltaUrls
                    {
                        DeltaUrl = "new-delta"
                    });
                });
            harness.Context
                .Setup(context =>
                    context.CallActivityAsync<GroupMembershipFileResult>(
                        It.Is<TaskName>(name =>
                            name.Name ==
                            nameof(TransitiveAndDeltaUsersSenderFunction)),
                        It.IsAny<TransitiveAndDeltaUsersSenderRequest>(),
                        It.IsAny<TaskOptions>()))
                .Returns<TaskName, TransitiveAndDeltaUsersSenderRequest, TaskOptions>(
                    async (_, request, _) =>
                    {
                        harness.ActivitySequence.Add(
                            nameof(TransitiveAndDeltaUsersSenderFunction));
                        Assert.IsFalse(request.WriteStaging);
                        await harness.Storage.UploadFileAsync(
                            harness.PartFilePath,
                            JsonSerializer.Serialize(new GroupMembership
                            {
                                Destination = new AzureADGroup
                                {
                                    ObjectId = harness.Request.GroupId
                                },
                                RunId = harness.Request.SyncJob.RunId.GetValueOrDefault(),
                                SyncJobId = harness.Request.SyncJob.Id,
                                Exclusionary = harness.Request.Exclusionary,
                                Query = harness.Request.SyncJob.Query,
                                SourceMembers = members.ToList()
                            }));
                        return new GroupMembershipFileResult
                        {
                            FilePath = harness.PartFilePath,
                            MemberCount = members.Count
                        };
                    });
            SetupDeleteUploadActivity(harness);
            SetupNoResultActivity<CacheUploaderRequest>(
                harness,
                nameof(CacheUploaderFunction));
            SetupNoResultActivity<DeltaLinkUploaderRequest>(
                harness,
                nameof(DeltaLinkUploaderFunction));
            SetupAgentCountActivity(harness);
        }

        private static async Task ConfigureInitialReplacementRead(
            OrchestrationHarness harness,
            IReadOnlyCollection<AzureADUser> members,
            IReadOnlyDictionary<Guid, string> typeMap,
            Exception lookupException = null)
        {
            harness.Context
                .Setup(context => context.CallActivityAsync<DeltaUrls>(
                    It.Is<TaskName>(name =>
                        name.Name == nameof(DeltaUserReaderFunction)),
                    It.IsAny<DeltaUserReaderRequest>(),
                    It.IsAny<TaskOptions>()))
                .Returns(() =>
                {
                    harness.ActivitySequence.Add(
                        nameof(DeltaUserReaderFunction));
                    return Task.FromResult(new DeltaUrls
                    {
                        DeltaUrl = "new-delta"
                    });
                });
            harness.Context
                .Setup(context =>
                    context.CallActivityAsync<GroupMembershipFileResult>(
                        It.Is<TaskName>(name =>
                            name.Name ==
                            nameof(TransitiveAndDeltaUsersSenderFunction)),
                        It.IsAny<TransitiveAndDeltaUsersSenderRequest>(),
                        It.IsAny<TaskOptions>()))
                .Returns<TaskName, TransitiveAndDeltaUsersSenderRequest, TaskOptions>(
                    async (_, request, _) =>
                    {
                        harness.ActivitySequence.Add(
                            nameof(TransitiveAndDeltaUsersSenderFunction));
                        var stagingPath = GroupPartStoragePaths.BuildStagingPath(
                            harness.Request.GroupId,
                            harness.Request.SyncJob.RunId.GetValueOrDefault(),
                            harness.Request.CurrentPart);
                        await harness.Storage.UploadFileAsync(
                            stagingPath,
                            JsonSerializer.Serialize(new GroupMembership
                            {
                                Destination = new AzureADGroup
                                {
                                    ObjectId = harness.Request.GroupId
                                },
                                RunId = harness.Request.SyncJob.RunId.GetValueOrDefault(),
                                SyncJobId = harness.Request.SyncJob.Id,
                                Exclusionary = harness.Request.Exclusionary,
                                Query = harness.Request.SyncJob.Query,
                                SourceMembers = members.ToList()
                            }));
                        return new GroupMembershipFileResult
                        {
                            FilePath = harness.PartFilePath,
                            MemberCount = members.Count
                        };
                    });
            SetupDeleteUploadActivity(harness);
            SetupNoResultActivity<CacheUploaderRequest>(
                harness,
                nameof(CacheUploaderFunction));
            SetupNoResultActivity<DeltaLinkUploaderRequest>(
                harness,
                nameof(DeltaLinkUploaderFunction));
            ConfigureRealSelectionActivity(
                harness,
                typeMap,
                lookupException);
            ConfigureRealCleanupActivity(harness);
        }

        private static void SetupDeltaLinkRead(
            OrchestrationHarness harness,
            IReadOnlyCollection<Guid> memberIds,
            IReadOnlyDictionary<Guid, string> typeMap,
            bool cacheMatches,
            Exception lookupException = null)
        {
            harness.Context
                .Setup(context => context.CallActivityAsync<string>(
                    It.Is<TaskName>(name =>
                        name.Name == nameof(FileDownloaderFunction)),
                    It.IsAny<FileDownloaderRequest>(),
                    It.IsAny<TaskOptions>()))
                .Returns(() =>
                {
                    harness.ActivitySequence.Add(
                        nameof(FileDownloaderFunction));
                    return Task.FromResult(
                        Models.Helpers.TextCompressor.Compress("saved-delta"));
                });
            harness.Context
                .Setup(context => context.CallActivityAsync<BlobResult>(
                    It.Is<TaskName>(name =>
                        name.Name == nameof(BlobCheckerFunction)),
                    It.IsAny<BlobCheckerRequest>(),
                    It.IsAny<TaskOptions>()))
                .Returns(() =>
                {
                    harness.ActivitySequence.Add(nameof(BlobCheckerFunction));
                    return Task.FromResult(new BlobResult
                    {
                        BlobStatus = BlobStatus.Found,
                        Path = "cache/source"
                    });
                });
            harness.Context
                .Setup(context => context.CallActivityAsync<DeltaUrls>(
                    It.Is<TaskName>(name =>
                        name.Name == nameof(DeltaLinkUserReaderFunction)),
                    It.IsAny<DeltaLinkUserReaderRequest>(),
                    It.IsAny<TaskOptions>()))
                .Returns(() =>
                {
                    harness.ActivitySequence.Add(
                        nameof(DeltaLinkUserReaderFunction));
                    return Task.FromResult(new DeltaUrls
                    {
                        DeltaUrl = "new-delta"
                    });
                });
            harness.Context
                .Setup(context => context.CallActivityAsync<int>(
                    It.Is<TaskName>(name =>
                        name.Name == nameof(GetUserCountFunction)),
                    It.IsAny<GetUserCountRequest>(),
                    It.IsAny<TaskOptions>()))
                .Returns(() =>
                {
                    harness.ActivitySequence.Add(
                        nameof(GetUserCountFunction));
                    return Task.FromResult(memberIds.Count);
                });
            harness.Context
                .Setup(context =>
                    context.CallActivityAsync<ProcessCachedAndDeltaUsersResponse>(
                        It.Is<TaskName>(name =>
                            name.Name ==
                            nameof(ProcessCachedAndDeltaUsersFunction)),
                        It.IsAny<ProcessCachedAndDeltaUsersRequest>(),
                        It.IsAny<TaskOptions>()))
                .Returns<TaskName, ProcessCachedAndDeltaUsersRequest, TaskOptions>(
                    async (_, request, _) =>
                    {
                        harness.ActivitySequence.Add(
                            nameof(ProcessCachedAndDeltaUsersFunction));
                        var stagingPath = string.Empty;
                        if (cacheMatches && request.WriteStaging)
                        {
                            stagingPath =
                                GroupPartStoragePaths.BuildStagingPath(
                                    request.TargetGroupId,
                                    request.SyncJob.RunId.GetValueOrDefault(),
                                    request.CurrentPart);
                            await harness.Storage
                                .UploadGroupMembershipFromGuidsAsync(
                                    stagingPath,
                                    memberIds,
                                    new AzureADGroup
                                    {
                                        ObjectId = request.TargetGroupId
                                    },
                                    request.SyncJob.RunId.GetValueOrDefault(),
                                    request.SyncJob.Id,
                                    request.Exclusionary,
                                    request.SyncJob.IsDryRunEnabled,
                                    request.SyncJob.Query);
                        }

                        return new ProcessCachedAndDeltaUsersResponse
                        {
                            CacheMatchesGroupCount = cacheMatches,
                            CacheCount = memberIds.Count,
                            MembershipFilePath = harness.PartFilePath,
                            StagingPath = stagingPath
                        };
                    });
            ConfigureRealSelectionActivity(
                harness,
                typeMap,
                lookupException);
            ConfigureRealCleanupActivity(harness);
            SetupNoResultActivity<FileDeleterRequest>(
                harness,
                nameof(FileDeleterFunction));
            SetupAgentCountActivity(harness);
        }

        private static void ConfigureRealSelectionActivity(
            OrchestrationHarness harness,
            IReadOnlyDictionary<Guid, string> typeMap,
            Exception lookupException = null)
        {
            var pipeline = new SelectionPipelineHarness(harness.Storage);
            foreach (var entry in typeMap ?? new Dictionary<Guid, string>())
            {
                pipeline.DirectoryTypes[entry.Key] = entry.Value;
            }

            if (lookupException != null)
            {
                pipeline.LookupOverride = (_, _, _) =>
                    Task.FromException<IReadOnlyDictionary<Guid, string>>(lookupException);
            }

            pipeline.Wire(harness.Context, harness.ActivitySequence);
            harness.SelectionHarness = pipeline;
        }

        private static void ConfigureRealCleanupActivity(
            OrchestrationHarness harness)
        {
            // The pipeline harness wires the real cleanup activity.
        }

        private static void SetupFallbackFullRead(
            OrchestrationHarness harness,
            AzureADUser member,
            bool writeStagingOnFirstMerge = true)
        {
            harness.Context
                .Setup(context => context.CallActivityAsync<string>(
                    It.Is<TaskName>(name =>
                        name.Name == nameof(MembersReaderFunction)),
                    It.IsAny<MembersReaderRequest>(),
                    It.IsAny<TaskOptions>()))
                .Returns(() =>
                {
                    harness.ActivitySequence.Add(
                        nameof(MembersReaderFunction));
                    return Task.FromResult((string)null);
                });
            var senderCallCount = 0;
            harness.Context
                .Setup(context =>
                    context.CallActivityAsync<GroupMembershipFileResult>(
                        It.Is<TaskName>(name =>
                            name.Name ==
                            nameof(TransitiveAndDeltaUsersSenderFunction)),
                        It.IsAny<TransitiveAndDeltaUsersSenderRequest>(),
                        It.IsAny<TaskOptions>()))
                .Returns<TaskName, TransitiveAndDeltaUsersSenderRequest, TaskOptions>(
                    async (_, _, _) =>
                    {
                        harness.ActivitySequence.Add(
                            nameof(TransitiveAndDeltaUsersSenderFunction));
                        senderCallCount++;
                        if (senderCallCount == 1 && !writeStagingOnFirstMerge)
                        {
                            return new GroupMembershipFileResult
                            {
                                FilePath = harness.PartFilePath,
                                MemberCount = 1
                            };
                        }

                        if (senderCallCount == 1)
                        {
                            await harness.Storage.UploadFileAsync(
                                GroupPartStoragePaths.BuildStagingPath(
                                    harness.Request.GroupId,
                                    harness.Request.SyncJob.RunId.GetValueOrDefault(),
                                    harness.Request.CurrentPart),
                                JsonSerializer.Serialize(new GroupMembership
                                {
                                    Destination = new AzureADGroup
                                    {
                                        ObjectId = harness.Request.GroupId
                                    },
                                    RunId = harness.Request.SyncJob.RunId.GetValueOrDefault(),
                                    SyncJobId = harness.Request.SyncJob.Id,
                                    Query = harness.Request.SyncJob.Query,
                                    SourceMembers = new List<AzureADUser> { member }
                                }));
                            return new GroupMembershipFileResult
                            {
                                FilePath = harness.PartFilePath,
                                MemberCount = 1
                            };
                        }

                        await harness.Storage.UploadFileAsync(
                            "fallback-full.json",
                            JsonSerializer.Serialize(new GroupMembership
                            {
                                SourceMembers = new List<AzureADUser> { member }
                            }));
                        return new GroupMembershipFileResult
                        {
                            FilePath = "fallback-full.json",
                            MemberCount = 1
                        };
                    });
        }

        private static void SetupFallbackFullReadAfterSavedDelta(
            OrchestrationHarness harness,
            AzureADUser member)
        {
            harness.Context
                .Setup(context => context.CallActivityAsync<string>(
                    It.Is<TaskName>(name =>
                        name.Name == nameof(MembersReaderFunction)),
                    It.IsAny<MembersReaderRequest>(),
                    It.IsAny<TaskOptions>()))
                .Returns(() =>
                {
                    harness.ActivitySequence.Add(
                        nameof(MembersReaderFunction));
                    return Task.FromResult((string)null);
                });
            harness.Context
                .Setup(context =>
                    context.CallActivityAsync<GroupMembershipFileResult>(
                        It.Is<TaskName>(name =>
                            name.Name ==
                            nameof(TransitiveAndDeltaUsersSenderFunction)),
                        It.IsAny<TransitiveAndDeltaUsersSenderRequest>(),
                        It.IsAny<TaskOptions>()))
                .Returns<TaskName, TransitiveAndDeltaUsersSenderRequest, TaskOptions>(
                    async (_, _, _) =>
                    {
                        harness.ActivitySequence.Add(
                            nameof(TransitiveAndDeltaUsersSenderFunction));
                        await harness.Storage.UploadFileAsync(
                            "fallback-full.json",
                            JsonSerializer.Serialize(new GroupMembership
                            {
                                SourceMembers =
                                    new List<AzureADUser> { member }
                            }));
                        return new GroupMembershipFileResult
                        {
                            FilePath = "fallback-full.json",
                            MemberCount = 1
                        };
                    });
            SetupDeleteUploadActivity(harness);
        }

        private static void SetupDeleteUploadActivity(
            OrchestrationHarness harness) =>
            SetupNoResultActivity<DeleteBlobRequest>(
                harness,
                nameof(DeleteBlobFunction));

        private static void SetupAgentCountActivity(
            OrchestrationHarness harness)
        {
            harness.Context
                .Setup(context => context.CallActivityAsync<int?>(
                    It.Is<TaskName>(name =>
                        name.Name == nameof(GetAgentUserCountFunction)),
                    It.IsAny<GetAgentUserCountRequest>(),
                    It.IsAny<TaskOptions>()))
                .Returns(() =>
                {
                    harness.ActivitySequence.Add(
                        nameof(GetAgentUserCountFunction));
                    return Task.FromResult<int?>(0);
                });
        }

        private static void SetupNoResultActivity<TRequest>(
            OrchestrationHarness harness,
            string activityName)
        {
            harness.Context
                .Setup(context => context.CallActivityAsync(
                    It.Is<TaskName>(name => name.Name == activityName),
                    It.IsAny<TRequest>(),
                    It.IsAny<TaskOptions>()))
                .Returns(() =>
                {
                    harness.ActivitySequence.Add(activityName);
                    return Task.CompletedTask;
                });
        }

        private static async Task<GroupMembership> ReadMembershipAsync(
            InMemoryBlobStorageRepository storage,
            string path)
        {
            var blob = await storage.DownloadFileAsync(path);
            return JsonSerializer.Deserialize<GroupMembership>(blob.Content);
        }

        private sealed class OrchestrationHarness
        {
            public OrchestrationHarness(
                Mock<TaskOrchestrationContext> context,
                RecordingLogger<SubOrchestratorFunction> logger,
                InMemoryBlobStorageRepository storage,
                GroupMembershipRequest request,
                List<string> activitySequence,
                SubOrchestratorFunction function,
                string partFilePath)
            {
                Context = context;
                Logger = logger;
                Storage = storage;
                Request = request;
                ActivitySequence = activitySequence;
                Function = function;
                PartFilePath = partFilePath;
            }

            public Mock<TaskOrchestrationContext> Context { get; }
            public RecordingLogger<SubOrchestratorFunction> Logger { get; }
            public InMemoryBlobStorageRepository Storage { get; }
            public GroupMembershipRequest Request { get; }
            public List<string> ActivitySequence { get; }
            public SubOrchestratorFunction Function { get; }
            public string PartFilePath { get; }
            public SelectionPipelineHarness SelectionHarness { get; set; }
        }
    }
}
