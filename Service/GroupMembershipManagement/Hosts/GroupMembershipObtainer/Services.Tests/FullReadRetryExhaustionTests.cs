// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.GroupMembershipObtainer;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using Repositories.Contracts;
using Repositories.Contracts.DestinationResolution;
using Repositories.Contracts.InjectConfig;
using Repositories.Mocks;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Tests.Services.Infrastructure;

namespace Tests.Services
{
    /// <summary>
    /// FR-020 (amended by the owner, 2026-10-21): a transitive-members page has one retry owner, the shared retry
    /// policies in the Graph repository. A page that still fails after their configured attempts fails its reader
    /// activity: nothing is uploaded or logged for that call, the full read fails before any merge, nothing is
    /// published, and the job reaches TransientError. There is no Durable timer, throttle budget, or re-issued reader input.
    /// </summary>
    [TestClass]
    public sealed class FullReadRetryExhaustionTests
    {
        private const int ReaderEntryEventId = 150180;

        // Reader activities

        [TestMethod]
        public async Task MembersReader_RetriesExhausted_ThrowsAndUploadsNothingOrWritesNoReaderEntry()
        {
            var fixture = CreateReaderFixture();
            fixture.GraphRepository
                .Setup(repository => repository.GetFirstTransitiveMembersPageAsync(
                    fixture.SourceGroupId,
                    It.IsAny<IReadOnlyList<string>>()))
                .ThrowsAsync(new HttpRequestException("throttled", null, HttpStatusCode.TooManyRequests));
            var logger = new RecordingLogger<MembersReaderFunction>();
            var function = new MembersReaderFunction(logger, fixture.Storage, fixture.Calculator);

            var exception = await Assert.ThrowsExceptionAsync<HttpRequestException>(() => function.GetMembersAsync(new MembersReaderRequest
            {
                SyncJob = fixture.Job,
                GroupId = fixture.SourceGroupId,
                TargetGroupId = fixture.TargetGroupId,
                CurrentPart = 1,
                TotalParts = 2,
                ReadKind = GroupMembershipReadKind.Full
            }));

            Assert.AreEqual(HttpStatusCode.TooManyRequests, exception.StatusCode);
            Assert.AreEqual(0, fixture.Storage.Paths.Count, "A failed call must upload nothing.");
            Assert.AreEqual(0, logger.Entries.Count(entry => entry.EventId.Id == ReaderEntryEventId));
        }

        [DataTestMethod]
        [DataRow("users")]
        [DataRow("agents")]
        [DataRow("users,agents")]
        [DataRow(null)]
        public async Task MembersReader_ForwardsTheSelectionToTheRepository(string selection)
        {
            // A dropped selection would still compile (the parameter defaults to null) and would send the uncast read,
            // publishing agents in a people-only part. The repository must receive exactly the request's selection.
            var expected = selection?.Split(',').ToList();
            var fixture = CreateReaderFixture();
            fixture.GraphRepository
                .Setup(repository => repository.GetFirstTransitiveMembersPageAsync(
                    fixture.SourceGroupId,
                    It.IsAny<IReadOnlyList<string>>()))
                .ThrowsAsync(new HttpRequestException("stop after the call", null, HttpStatusCode.BadRequest));
            var function = new MembersReaderFunction(new RecordingLogger<MembersReaderFunction>(), fixture.Storage, fixture.Calculator);

            await Assert.ThrowsExceptionAsync<HttpRequestException>(() => function.GetMembersAsync(new MembersReaderRequest
            {
                SyncJob = fixture.Job,
                GroupId = fixture.SourceGroupId,
                TargetGroupId = fixture.TargetGroupId,
                CurrentPart = 1,
                TotalParts = 2,
                ReadKind = GroupMembershipReadKind.Full,
                IdentityTypes = expected
            }));

            fixture.GraphRepository.Verify(
                repository => repository.GetFirstTransitiveMembersPageAsync(
                    fixture.SourceGroupId,
                    It.Is<IReadOnlyList<string>>(types => SameSelection(types, expected))),
                Times.Once);
        }

        [DataTestMethod]
        [DataRow("users")]
        [DataRow("agents")]
        [DataRow("users,agents")]
        [DataRow(null)]
        public async Task SubsequentMembersReader_ForwardsTheSelectionToTheRepository(string selection)
        {
            var expected = selection?.Split(',').ToList();
            var fixture = CreateReaderFixture();
            const string nextLink = "https://graph.microsoft.com/v1.0/groups/x/transitiveMembers?$skiptoken=page2";
            fixture.GraphRepository
                .Setup(repository => repository.GetNextTransitiveMembersPageAsync(
                    fixture.SourceGroupId,
                    nextLink,
                    It.IsAny<IReadOnlyList<string>>()))
                .ThrowsAsync(new HttpRequestException("stop after the call", null, HttpStatusCode.BadRequest));
            var function = new SubsequentMembersReaderFunction(new RecordingLogger<SubsequentMembersReaderFunction>(), fixture.Storage, fixture.Calculator);

            await Assert.ThrowsExceptionAsync<HttpRequestException>(() => function.GetMembersAsync(new SubsequentMembersReaderRequest
            {
                SyncJob = fixture.Job,
                GroupId = fixture.SourceGroupId,
                TargetGroupId = fixture.TargetGroupId,
                NextPageUrl = nextLink,
                CurrentPart = 1,
                TotalParts = 2,
                IdentityTypes = expected
            }));

            fixture.GraphRepository.Verify(
                repository => repository.GetNextTransitiveMembersPageAsync(
                    fixture.SourceGroupId,
                    nextLink,
                    It.Is<IReadOnlyList<string>>(types => SameSelection(types, expected))),
                Times.Once);
        }

        private static bool SameSelection(IReadOnlyList<string> actual, IReadOnlyList<string> expected) =>
            expected == null ? actual == null : actual != null && actual.SequenceEqual(expected);

        [TestMethod]
        public async Task SubsequentMembersReader_RetriesExhausted_ThrowsAndUploadsNothing()
        {
            var fixture = CreateReaderFixture();
            const string nextLink = "https://graph.microsoft.com/v1.0/groups/x/transitiveMembers?$skiptoken=page3";
            fixture.GraphRepository
                .Setup(repository => repository.GetNextTransitiveMembersPageAsync(
                    fixture.SourceGroupId,
                    nextLink,
                    It.IsAny<IReadOnlyList<string>>()))
                .ThrowsAsync(new HttpRequestException("unavailable", null, HttpStatusCode.ServiceUnavailable));
            var logger = new RecordingLogger<SubsequentMembersReaderFunction>();
            var function = new SubsequentMembersReaderFunction(logger, fixture.Storage, fixture.Calculator);

            await Assert.ThrowsExceptionAsync<HttpRequestException>(() => function.GetMembersAsync(new SubsequentMembersReaderRequest
            {
                SyncJob = fixture.Job,
                GroupId = fixture.SourceGroupId,
                TargetGroupId = fixture.TargetGroupId,
                CurrentPart = 1,
                TotalParts = 2,
                NextPageUrl = nextLink,
                IdentityTypes = new List<string> { "users" },
                ReadKind = GroupMembershipReadKind.Full
            }));

            Assert.AreEqual(0, fixture.Storage.Paths.Count, "A failed call must upload nothing.");
            Assert.AreEqual(0, logger.Entries.Count(entry => entry.EventId.Id == ReaderEntryEventId));
        }

        [TestMethod]
        public async Task MembersReader_CompletedCall_ReturnsTheNextLink()
        {
            var fixture = CreateReaderFixture();
            fixture.GraphRepository
                .Setup(repository => repository.GetFirstTransitiveMembersPageAsync(
                    fixture.SourceGroupId,
                    It.IsAny<IReadOnlyList<string>>()))
                .ReturnsAsync((new List<AzureADUser> { new AzureADUser { ObjectId = Guid.NewGuid() } }, new MemberReadCounts(), "link-2"));
            var function = new MembersReaderFunction(new RecordingLogger<MembersReaderFunction>(), fixture.Storage, fixture.Calculator);

            var nextLink = await function.GetMembersAsync(new MembersReaderRequest
            {
                SyncJob = fixture.Job,
                GroupId = fixture.SourceGroupId,
                TargetGroupId = fixture.TargetGroupId,
                CurrentPart = 1,
                TotalParts = 2,
                ReadKind = GroupMembershipReadKind.Full
            });

            Assert.AreEqual("link-2", nextLink);
            Assert.AreEqual(1, fixture.Storage.Paths.Count);
        }

        // Sub-orchestrator

        [DataTestMethod]
        [Timeout(30000)]
        [DataRow(null, DisplayName = "un-cast (both types)")]
        [DataRow("users", DisplayName = "users only")]
        [DataRow("agents", DisplayName = "agents only")]
        public async Task FullRead_PageFailsAfterRetries_FailsThePartWithoutTimerOrPublishing(string selectedType)
        {
            var identityTypes = selectedType == null ? null : new[] { selectedType };
            var harness = CreateOrchestrationHarness(identityTypes);
            harness.FirstPage = _ => Task.FromResult("link-2");
            harness.NextPage = request => request.NextPageUrl == "link-2"
                ? Task.FromResult("link-3")
                : Task.FromException<string>(new GraphRetriesExhaustedException(
                    "The transitive-members request failed with status 429 (TooManyRequests). The configured retries are exhausted.",
                    null,
                    HttpStatusCode.TooManyRequests));

            var exception = await Assert.ThrowsExceptionAsync<GraphRetriesExhaustedException>(
                () => harness.Function.RunSubOrchestratorAsync(harness.Context.Object));

            Assert.IsInstanceOfType(exception.InnerException, typeof(TaskFailedException), "The reader's failure is kept as the cause.");
            Assert.AreEqual(nameof(SubsequentMembersReaderFunction), ((TaskFailedException)exception.InnerException).TaskName);
            CollectionAssert.AreEqual(
                new[] { "link-2", "link-3" },
                harness.NextPageRequests.Select(request => request.NextPageUrl).ToArray(),
                "The failed page is not re-issued by the orchestrator.");
            Assert.AreEqual(0, harness.TimerCalls, "No Durable timer is used for retries.");
            Assert.AreEqual(0, harness.SenderCalls, "A failed full read publishes nothing.");
        }

        [DataTestMethod]
        [Timeout(30000)]
        [DataRow("function timeout")]
        [DataRow("not found")]
        public async Task FullRead_PageFailure_IsTransientOnlyForExhaustionOrTimeout(string kind)
        {
            var harness = CreateOrchestrationHarness(new[] { "agents" });
            harness.FirstPage = _ => kind == "function timeout"
                ? Task.FromException<string>(new Microsoft.Azure.WebJobs.Host.FunctionTimeoutException(
                    "Timeout value of 00:20:00 was exceeded by function: Functions.MembersReaderFunction"))
                : Task.FromException<string>(new HttpRequestException("not found", null, HttpStatusCode.NotFound));

            Exception failure = null;
            try
            {
                await harness.Function.RunSubOrchestratorAsync(harness.Context.Object);
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            if (kind == "function timeout")
            {
                Assert.IsInstanceOfType(failure, typeof(GraphRetriesExhaustedException), "A reader cut short by the function timeout is transient.");
            }
            else
            {
                Assert.IsInstanceOfType(failure, typeof(TaskFailedException), "Other reader failures keep today's failure (Error).");
                Assert.IsNotInstanceOfType(failure, typeof(GraphRetriesExhaustedException));
            }

            Assert.AreEqual(0, harness.SenderCalls, "Nothing is published.");
        }

        [TestMethod]
        [Timeout(30000)]
        public async Task FullRead_EveryPageCompletes_PublishesOnce()
        {
            var harness = CreateOrchestrationHarness(new[] { "agents" });
            harness.FirstPage = _ => Task.FromResult("link-2");
            harness.NextPage = request => Task.FromResult(request.NextPageUrl == "link-2" ? "link-3" : null);

            var response = await harness.Function.RunSubOrchestratorAsync(harness.Context.Object);

            Assert.AreEqual("part.json", response.FilePath);
            CollectionAssert.AreEqual(
                new[] { "link-2", "link-3" },
                harness.NextPageRequests.Select(request => request.NextPageUrl).ToArray());
            Assert.AreEqual(0, harness.TimerCalls);
            Assert.AreEqual(1, harness.SenderCalls);
        }

        private static ReaderFixture CreateReaderFixture()
        {
            var targetGroupId = Guid.NewGuid();
            var graphRepository = new Mock<IGraphGroupRepository>();
            var storage = new InMemoryBlobStorageRepository();
            var destinationResolver = new Mock<IDestinationResolver>();
            destinationResolver
                .Setup(resolver => resolver.ResolveAsync(It.IsAny<SyncJob>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ResolvedGroupDestination { ObjectId = targetGroupId });
            var calculator = new SGMembershipCalculator(
                graphRepository.Object,
                storage,
                Mock.Of<IDatabaseSyncJobsRepository>(),
                destinationResolver.Object,
                Mock.Of<IServiceBusQueueRepository>(),
                Mock.Of<IDatabaseDestinationAttributesRepository>(),
                NullLogger<SGMembershipCalculator>.Instance,
                Mock.Of<IDryRunValue>(),
                Mock.Of<ISyncJobStatusService>());
            return new ReaderFixture(
                graphRepository,
                storage,
                calculator,
                new SyncJob
                {
                    Id = Guid.NewGuid(),
                    RunId = Guid.NewGuid(),
                    Query = "[{\"type\":\"GroupMembership\"}]"
                },
                Guid.NewGuid(),
                targetGroupId);
        }

        private static OrchestrationHarness CreateOrchestrationHarness(IReadOnlyCollection<string> identityTypes)
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
                TotalParts = 2,
                IdentityTypes = identityTypes?.ToList(),
                IsDestinationPart = false
            };
            var harness = new OrchestrationHarness();
            var context = harness.Context;
            context.Setup(c => c.GetInput<GroupMembershipRequest>()).Returns(request);
            context.Setup(c => c.CreateReplaySafeLogger(It.IsAny<string>())).Returns(harness.Logger);
            context.SetupGet(c => c.IsReplaying).Returns(false);
            context.SetupGet(c => c.CurrentUtcDateTime).Returns(new DateTime(2026, 10, 21, 8, 0, 0, DateTimeKind.Utc));
            context
                .Setup(c => c.CallActivityAsync<bool>(
                    It.Is<TaskName>(name => name.Name == nameof(GroupValidatorFunction)),
                    It.IsAny<object>(),
                    It.IsAny<TaskOptions>()))
                .ReturnsAsync(true);
            context
                .Setup(c => c.CallActivityAsync<int>(
                    It.Is<TaskName>(name => name.Name == nameof(GetTransitiveGroupCountFunction)),
                    It.IsAny<object>(),
                    It.IsAny<TaskOptions>()))
                .ReturnsAsync(0);
            context
                .Setup(c => c.CallActivityAsync<string>(
                    It.Is<TaskName>(name => name.Name == nameof(MembersReaderFunction)),
                    It.IsAny<object>(),
                    It.IsAny<TaskOptions>()))
                .Returns<TaskName, object, TaskOptions>((_, input, _) =>
                    ActivityCallWrapper.InvokeAsync(nameof(MembersReaderFunction), () => harness.FirstPage((MembersReaderRequest)input)));
            context
                .Setup(c => c.CallActivityAsync<string>(
                    It.Is<TaskName>(name => name.Name == nameof(SubsequentMembersReaderFunction)),
                    It.IsAny<object>(),
                    It.IsAny<TaskOptions>()))
                .Returns<TaskName, object, TaskOptions>((_, input, _) =>
                {
                    var readerRequest = (SubsequentMembersReaderRequest)input;
                    harness.NextPageRequests.Add(readerRequest);
                    return ActivityCallWrapper.InvokeAsync(nameof(SubsequentMembersReaderFunction), () => harness.NextPage(readerRequest));
                });
            context
                .Setup(c => c.CreateTimer(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .Returns<DateTime, CancellationToken>((_, _) =>
                {
                    harness.TimerCalls++;
                    return Task.CompletedTask;
                });
            context
                .Setup(c => c.CallActivityAsync<GroupMembershipFileResult>(
                    It.Is<TaskName>(name => name.Name == nameof(TransitiveAndDeltaUsersSenderFunction)),
                    It.IsAny<object>(),
                    It.IsAny<TaskOptions>()))
                .Returns(() =>
                {
                    harness.SenderCalls++;
                    return Task.FromResult(new GroupMembershipFileResult { FilePath = "part.json", MemberCount = 1 });
                });
            context
                .Setup(c => c.CallActivityAsync<int?>(
                    It.Is<TaskName>(name => name.Name == nameof(GetAgentUserCountFunction)),
                    It.IsAny<object>(),
                    It.IsAny<TaskOptions>()))
                .ReturnsAsync(2);
            return harness;
        }

        private sealed record ReaderFixture(
            Mock<IGraphGroupRepository> GraphRepository,
            InMemoryBlobStorageRepository Storage,
            SGMembershipCalculator Calculator,
            SyncJob Job,
            Guid SourceGroupId,
            Guid TargetGroupId);

        private sealed class OrchestrationHarness
        {
            public Mock<TaskOrchestrationContext> Context { get; } = new();
            public RecordingLogger<SubOrchestratorFunction> Logger { get; } = new();
            public SubOrchestratorFunction Function { get; } = new SubOrchestratorFunction(
                new MockDeltaCachingConfig { DeltaCacheEnabled = false },
                new TelemetryClient(new TelemetryConfiguration()));
            public List<SubsequentMembersReaderRequest> NextPageRequests { get; } = new();
            public int SenderCalls { get; set; }
            public int TimerCalls { get; set; }
            public Func<MembersReaderRequest, Task<string>> FirstPage { get; set; } = _ => Task.FromResult<string>(null);
            public Func<SubsequentMembersReaderRequest, Task<string>> NextPage { get; set; } = _ => Task.FromResult<string>(null);
        }
    }
}
