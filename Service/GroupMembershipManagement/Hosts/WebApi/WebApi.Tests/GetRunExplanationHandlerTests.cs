// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging.Abstractions;
using Models;
using Models.AdfRunHistory;
using Models.SyncJobChange;
using Moq;
using Repositories.Contracts;
using Repositories.Contracts.DestinationResolution;
using Services;
using Services.Contracts;
using Services.Messages.Requests;
using Services.WebApi.Contracts;
using SqlMembershipObtainer.Entities;
using System.Net;
using System.Text.Json;
using System.Threading;

namespace WebApi.Tests
{
    [TestClass]
    public class GetRunExplanationHandlerTests
    {
        private Mock<IDatabaseSyncJobsRepository> _mockSyncJobRepository = null!;
        private Mock<ISyncJobHistoryRepository> _mockSyncJobHistoryRepository = null!;
        private Mock<ISyncJobChangeRepository> _mockSyncJobChangeRepository = null!;
        private Mock<IGraphGroupRepository> _mockGraphGroupRepository = null!;
        private Mock<IBlobStorageRepository> _mockBlobStorageRepository = null!;
        private Mock<ISqlMembershipRepository> _mockSqlMembershipRepository = null!;
        private Mock<IAdfRunHistoryRepository> _mockAdfRunHistoryRepository = null!;
        private Mock<IOpenAIService> _mockOpenAIService = null!;
        private Mock<IDestinationResolver> _mockDestinationResolver = null!;
        private GetRunExplanationHandler _handler = null!;

        private Guid _syncJobId;
        private Guid _targetGroupId;
        private Guid _runId;
        private const string TestUserIdentity = "test-user-id";

        [TestInitialize]
        public void Initialize()
        {
            _mockSyncJobRepository = new Mock<IDatabaseSyncJobsRepository>();
            _mockSyncJobHistoryRepository = new Mock<ISyncJobHistoryRepository>();
            _mockSyncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            _mockGraphGroupRepository = new Mock<IGraphGroupRepository>();
            _mockBlobStorageRepository = new Mock<IBlobStorageRepository>();
            _mockSqlMembershipRepository = new Mock<ISqlMembershipRepository>();
            _mockAdfRunHistoryRepository = new Mock<IAdfRunHistoryRepository>();
            _mockOpenAIService = new Mock<IOpenAIService>();
            _mockDestinationResolver = new Mock<IDestinationResolver>();

            _handler = new GetRunExplanationHandler(
                NullLogger<GetRunExplanationHandler>.Instance,
                _mockSyncJobRepository.Object,
                _mockSyncJobHistoryRepository.Object,
                _mockSyncJobChangeRepository.Object,
                _mockGraphGroupRepository.Object,
                _mockBlobStorageRepository.Object,
                _mockSqlMembershipRepository.Object,
                _mockAdfRunHistoryRepository.Object,
                _mockOpenAIService.Object,
                _mockDestinationResolver.Object);

            _syncJobId = Guid.NewGuid();
            _targetGroupId = Guid.NewGuid();
            _runId = Guid.NewGuid();

            _mockSyncJobRepository
                .Setup(x => x.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(new SyncJob
                {
                    Id = _syncJobId,
                    TargetOfficeGroupId = _targetGroupId,
                    Query = "[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Building = 'B40'\"}}]"
                });

            _mockSyncJobHistoryRepository
                .Setup(x => x.GetByRunIdAsync(_runId))
                .ReturnsAsync(new global::Models.SyncJobHistory.SyncJobHistory
                {
                    SyncJobId = _syncJobId,
                    RunId = _runId,
                    Status = "Idle",
                    StartTime = DateTime.UtcNow.AddMinutes(-10),
                    EndTime = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    UsersAdded = 0,
                    UsersRemoved = 0,
                    BeforeSyncUserCount = 100,
                    AfterSyncUserCount = 100,
                });

            _mockGraphGroupRepository
                .Setup(x => x.IsEmailRecipientOwnerOfGroupAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<bool>()))
                .ReturnsAsync(true);

            _mockSyncJobHistoryRepository
                .Setup(x => x.GetBySyncJobIdAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync(new List<global::Models.SyncJobHistory.SyncJobHistory>());

            _mockSyncJobChangeRepository
                .Setup(x => x.GetRecentConfigChangesBySyncJobIdAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<int>()))
                .ReturnsAsync(new List<SyncJobChange>());

            _mockSyncJobChangeRepository
                .Setup(x => x.GetRecentIgnoreThresholdOnceEventsAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<int>()))
                .ReturnsAsync(new List<SyncJobChange>());

            _mockBlobStorageRepository
                .Setup(x => x.FindAggregatedFileByRunIdAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new BlobResult { BlobStatus = BlobStatus.NotFound });

            _mockBlobStorageRepository
                .Setup(x => x.FindPartFilesByRunIdAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new Dictionary<string, BlobResult>());

            _mockOpenAIService
                .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync("This sync completed successfully.");
        }

        private GetRunExplanationRequest BuildRequest(bool hasAiRole = true) =>
            new GetRunExplanationRequest(_syncJobId, _runId, TestUserIdentity, hasAiRole);

        // ---------------- ExecuteCoreAsync orchestration tests ----------------

        [TestMethod]
        public async Task ExecuteAsync_JobNotFound_ReturnsNotFound()
        {
            _mockSyncJobRepository.Setup(x => x.GetSyncJobAsync(_syncJobId)).ReturnsAsync((SyncJob?)null);

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        }

        [TestMethod]
        public async Task ExecuteAsync_NotAiRoleAndNotOwner_ReturnsForbidden()
        {
            _mockGraphGroupRepository
                .Setup(x => x.IsEmailRecipientOwnerOfGroupAsync(TestUserIdentity, _targetGroupId, It.IsAny<bool>()))
                .ReturnsAsync(false);

            var response = await _handler.ExecuteAsync(BuildRequest(hasAiRole: false));

            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [TestMethod]
        public async Task ExecuteAsync_NotAiRoleButIsOwner_Proceeds()
        {
            _mockGraphGroupRepository
                .Setup(x => x.IsEmailRecipientOwnerOfGroupAsync(TestUserIdentity, _targetGroupId, It.IsAny<bool>()))
                .ReturnsAsync(true);

            var response = await _handler.ExecuteAsync(BuildRequest(hasAiRole: false));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        }

        [TestMethod]
        public async Task ExecuteAsync_UseBoundary_GroupDestinationResolved_OwnershipCheckUsesResolvedObjectId()
        {
            // T035/US2: when the shared boundary resolves a group destination whose ObjectId differs from the
            // legacy TargetOfficeGroupId (e.g. because the resolver reads live Group navigation), the ownership
            // check and blob key must use the resolver's identity, not the stale scalar — proving genuine reroute.
            var resolvedObjectId = Guid.NewGuid();
            _mockDestinationResolver
                .Setup(x => x.ResolveAsync(It.IsAny<SyncJob>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ResolvedGroupDestination { SyncJobId = _syncJobId, ObjectId = resolvedObjectId });
            _mockGraphGroupRepository
                .Setup(x => x.IsEmailRecipientOwnerOfGroupAsync(TestUserIdentity, resolvedObjectId, It.IsAny<bool>()))
                .ReturnsAsync(true);

            var response = await _handler.ExecuteAsync(BuildRequest(hasAiRole: false));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            _mockGraphGroupRepository.Verify(
                x => x.IsEmailRecipientOwnerOfGroupAsync(TestUserIdentity, resolvedObjectId, It.IsAny<bool>()),
                Times.Once);
            _mockGraphGroupRepository.Verify(
                x => x.IsEmailRecipientOwnerOfGroupAsync(TestUserIdentity, _targetGroupId, It.IsAny<bool>()),
                Times.Never);
        }

        [TestMethod]
        public async Task ExecuteAsync_UseBoundary_TeamsChannelDestinationResolved_OwnershipCheckUsesTeamObjectId_NotChannelId()
        {
            // T036/US2: Teams-channel reroutes must key ownership/blob lookups off ResolvedTeamsChannelDestination.TeamObjectId
            // (the historical TargetOfficeGroupId semantics) — never collapse to a group-only identity that drops ChannelId,
            // and never substitute the ChannelId in place of the team identity.
            var resolvedTeamObjectId = Guid.NewGuid();
            _mockDestinationResolver
                .Setup(x => x.ResolveAsync(It.IsAny<SyncJob>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ResolvedTeamsChannelDestination
                {
                    SyncJobId = _syncJobId,
                    TeamObjectId = resolvedTeamObjectId,
                    ChannelId = "19:channel123@thread.tacv2"
                });
            _mockGraphGroupRepository
                .Setup(x => x.IsEmailRecipientOwnerOfGroupAsync(TestUserIdentity, resolvedTeamObjectId, It.IsAny<bool>()))
                .ReturnsAsync(true);

            var response = await _handler.ExecuteAsync(BuildRequest(hasAiRole: false));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            _mockGraphGroupRepository.Verify(
                x => x.IsEmailRecipientOwnerOfGroupAsync(TestUserIdentity, resolvedTeamObjectId, It.IsAny<bool>()),
                Times.Once);
        }

        [TestMethod]
        public async Task ExecuteAsync_RunHistoryNotFound_ReturnsNotFound()
        {
            _mockSyncJobHistoryRepository.Setup(x => x.GetByRunIdAsync(_runId))
                .ReturnsAsync((global::Models.SyncJobHistory.SyncJobHistory?)null);

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        }

        [TestMethod]
        public async Task ExecuteAsync_RunBelongsToDifferentSyncJob_ReturnsNotFound()
        {
            // Security: caller authorized for _syncJobId but provides a runId that belongs to a different sync job.
            var differentSyncJobId = Guid.NewGuid();
            _mockSyncJobHistoryRepository.Setup(x => x.GetByRunIdAsync(_runId))
                .ReturnsAsync(new global::Models.SyncJobHistory.SyncJobHistory
                {
                    SyncJobId = differentSyncJobId,  // NOT _syncJobId
                    RunId = _runId,
                    Status = "Idle",
                    UpdatedAt = DateTime.UtcNow,
                    UsersAdded = 5,
                    UsersRemoved = 3,
                });

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
            _mockOpenAIService.Verify(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [TestMethod]
        public async Task ExecuteAsync_SkipPathA_NoDeltaNoChangeIdleStatus_ReturnsNoMembershipChanges()
        {
            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(GetRunExplanationHandler.NoMembershipChanges, response.Explanation);
            _mockOpenAIService.Verify(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [TestMethod]
        public async Task ExecuteAsync_NoChangesAfterMembershipAggregatorIdle_ClearsEarlierConfigurationChange()
        {
            var now = DateTime.UtcNow;
            var sourceGroupId = Guid.NewGuid();
            var currentQuery =
                $"[{{\"type\":\"GroupMembership\",\"source\":\"{sourceGroupId}\",\"identityTypes\":[\"agents\"]}}]";
            var previousQuery =
                $"[{{\"type\":\"GroupMembership\",\"source\":\"{sourceGroupId}\",\"identityTypes\":[\"users\"]}}]";
            var currentChange = ChangeWithSerializedQuery(
                now.AddMinutes(-90),
                currentQuery);
            var previousChange = ChangeWithSerializedQuery(
                now.AddHours(-3),
                previousQuery);
            var changes = new[] { currentChange, previousChange };

            _mockSyncJobRepository
                .Setup(repository => repository.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(new SyncJob
                {
                    Id = _syncJobId,
                    TargetOfficeGroupId = _targetGroupId,
                    Query = currentQuery
                });
            _mockSyncJobHistoryRepository
                .Setup(repository => repository.GetByRunIdAsync(_runId))
                .ReturnsAsync(new global::Models.SyncJobHistory.SyncJobHistory
                {
                    SyncJobId = _syncJobId,
                    RunId = _runId,
                    Status = "Idle",
                    StartTime = now.AddMinutes(-10),
                    EndTime = now,
                    UpdatedAt = now,
                    UsersAdded = 0,
                    UsersRemoved = 0
                });
            _mockSyncJobHistoryRepository
                .Setup(repository => repository.GetBySyncJobIdAsync(
                    _syncJobId,
                    It.IsAny<int>(),
                    It.IsAny<int>()))
                .ReturnsAsync(new List<global::Models.SyncJobHistory.SyncJobHistory>
                {
                    new()
                    {
                        SyncJobId = _syncJobId,
                        RunId = Guid.NewGuid(),
                        Status = "Idle",
                        UpdatedByFunction = "MembershipAggregator",
                        StartTime = now.AddMinutes(-65),
                        EndTime = now.AddMinutes(-60),
                        UpdatedAt = now.AddMinutes(-60)
                    },
                    new()
                    {
                        SyncJobId = _syncJobId,
                        RunId = Guid.NewGuid(),
                        Status = "Idle",
                        UpdatedByFunction = "GraphUpdater",
                        StartTime = now.AddMinutes(-130),
                        EndTime = now.AddMinutes(-125),
                        UpdatedAt = now.AddMinutes(-125)
                    }
                });
            _mockSyncJobChangeRepository
                .Setup(repository => repository.GetRecentConfigChangesBySyncJobIdAsync(
                    _syncJobId,
                    It.IsAny<DateTime>(),
                    It.IsAny<int>()))
                .ReturnsAsync((
                    Guid _,
                    DateTime asOf,
                    int count) =>
                {
                    var matchingChanges = changes
                        .Where(change => change.ChangeTime <= asOf)
                        .OrderByDescending(change => change.ChangeTime);
                    return count > 0
                        ? matchingChanges.Take(count).ToList()
                        : matchingChanges.ToList();
                });

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(
                GetRunExplanationHandler.NoMembershipChanges,
                response.Explanation);
            _mockOpenAIService.Verify(
                service => service.GetCompletionAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>()),
                Times.Never);
        }

        [TestMethod]
        public async Task ExecuteAsync_MembershipAggregatorDryRun_DoesNotAdvanceAppliedBoundary()
        {
            var now = DateTime.UtcNow;
            var sourceGroupId = Guid.NewGuid();
            var addedUser = Guid.NewGuid();
            var currentQuery =
                $"[{{\"type\":\"GroupMembership\",\"source\":\"{sourceGroupId}\",\"identityTypes\":[\"agents\"]}}]";
            var previousQuery =
                $"[{{\"type\":\"GroupMembership\",\"source\":\"{sourceGroupId}\",\"identityTypes\":[\"users\"]}}]";
            var changes = new[]
            {
                new SyncJobChange
                {
                    Id = Guid.NewGuid(),
                    ChangeTime = now.AddMinutes(-10),
                    ChangeReason = SyncJobChangeReason.Update.ToString(),
                    ChangeDetails = JsonSerializer.Serialize(new SyncJob
                    {
                        Query = currentQuery,
                        IsDryRunEnabled = false
                    })
                },
                new SyncJobChange
                {
                    Id = Guid.NewGuid(),
                    ChangeTime = now.AddMinutes(-90),
                    ChangeReason = SyncJobChangeReason.Update.ToString(),
                    ChangeDetails = JsonSerializer.Serialize(new SyncJob
                    {
                        Query = currentQuery,
                        IsDryRunEnabled = true
                    })
                },
                ChangeWithSerializedQuery(now.AddHours(-3), previousQuery)
            };

            _mockSyncJobRepository
                .Setup(repository => repository.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(new SyncJob
                {
                    Id = _syncJobId,
                    TargetOfficeGroupId = _targetGroupId,
                    Query = currentQuery
                });
            _mockSyncJobHistoryRepository
                .Setup(repository => repository.GetByRunIdAsync(_runId))
                .ReturnsAsync(new global::Models.SyncJobHistory.SyncJobHistory
                {
                    SyncJobId = _syncJobId,
                    RunId = _runId,
                    Status = "Idle",
                    StartTime = now.AddMinutes(-5),
                    EndTime = now,
                    UpdatedAt = now,
                    UsersAdded = 1,
                    UsersRemoved = 0
                });
            _mockSyncJobHistoryRepository
                .Setup(repository => repository.GetBySyncJobIdAsync(
                    _syncJobId,
                    It.IsAny<int>(),
                    It.IsAny<int>()))
                .ReturnsAsync(new List<global::Models.SyncJobHistory.SyncJobHistory>
                {
                    new()
                    {
                        SyncJobId = _syncJobId,
                        RunId = Guid.NewGuid(),
                        Status = "Idle",
                        UpdatedByFunction = "MembershipAggregator",
                        StartTime = now.AddMinutes(-65),
                        EndTime = now.AddMinutes(-60),
                        UpdatedAt = now.AddMinutes(-60)
                    },
                    new()
                    {
                        SyncJobId = _syncJobId,
                        RunId = Guid.NewGuid(),
                        Status = "Idle",
                        UpdatedByFunction = "GraphUpdater",
                        StartTime = now.AddMinutes(-130),
                        EndTime = now.AddMinutes(-125),
                        UpdatedAt = now.AddMinutes(-125)
                    }
                });
            _mockSyncJobChangeRepository
                .Setup(repository => repository.GetRecentConfigChangesBySyncJobIdAsync(
                    _syncJobId,
                    It.IsAny<DateTime>(),
                    It.IsAny<int>()))
                .ReturnsAsync((
                    Guid _,
                    DateTime asOf,
                    int count) =>
                {
                    var matchingChanges = changes
                        .Where(change => change.ChangeTime <= asOf)
                        .OrderByDescending(change => change.ChangeTime);
                    return count > 0
                        ? matchingChanges.Take(count).ToList()
                        : matchingChanges.ToList();
                });
            SetupAggregatedMembers(MembershipAction.Add, addedUser);
            string? capturedPrompt = null;
            _mockOpenAIService
                .Setup(service => service.GetCompletionAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>()))
                .Callback<string, string>((_, prompt) => capturedPrompt = prompt)
                .ReturnsAsync("This sync reflects the configured identity selection.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedPrompt);
            StringAssert.Contains(capturedPrompt, "Previous configuration:");
            StringAssert.Contains(
                capturedPrompt,
                "Identity types changed from only people to only agent users");
        }

        [TestMethod]
        public async Task ExecuteAsync_ScheduledNowOnlyHistory_WithItoActivation_PreservesItoExplanation()
        {
            var now = DateTime.UtcNow;
            var query =
                "[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Building = 'B40'\"}}]";
            var addedUser = Guid.NewGuid();

            _mockSyncJobRepository
                .Setup(repository => repository.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(new SyncJob
                {
                    Id = _syncJobId,
                    TargetOfficeGroupId = _targetGroupId,
                    Query = query
                });
            _mockSyncJobHistoryRepository
                .Setup(repository => repository.GetByRunIdAsync(_runId))
                .ReturnsAsync(new global::Models.SyncJobHistory.SyncJobHistory
                {
                    SyncJobId = _syncJobId,
                    RunId = _runId,
                    Status = "Idle",
                    StartTime = now.AddMinutes(-5),
                    EndTime = now,
                    UpdatedAt = now,
                    UsersAdded = 1,
                    UsersRemoved = 0
                });
            _mockSyncJobHistoryRepository
                .Setup(repository => repository.GetBySyncJobIdAsync(
                    _syncJobId,
                    It.IsAny<int>(),
                    It.IsAny<int>()))
                .ReturnsAsync(new List<global::Models.SyncJobHistory.SyncJobHistory>
                {
                    new()
                    {
                        SyncJobId = _syncJobId,
                        RunId = Guid.NewGuid(),
                        Status = SyncStatus.ThresholdExceeded.ToString(),
                        UpdatedAt = now.AddHours(-1)
                    }
                });
            _mockSyncJobChangeRepository
                .Setup(repository => repository.GetRecentConfigChangesBySyncJobIdAsync(
                    _syncJobId,
                    It.IsAny<DateTime>(),
                    It.IsAny<int>()))
                .ReturnsAsync(new List<SyncJobChange>
                {
                    new()
                    {
                        Id = Guid.NewGuid(),
                        ChangeTime = now.AddMinutes(-10),
                        ChangeReason = SyncJobChangeReason.ScheduledNow.ToString(),
                        ChangedByDisplayName = "Configuration Owner",
                        ChangeDetails = JsonSerializer.Serialize(new { query })
                    }
                });
            _mockSyncJobChangeRepository
                .Setup(repository => repository.GetRecentIgnoreThresholdOnceEventsAsync(
                    _syncJobId,
                    It.IsAny<DateTime>(),
                    It.IsAny<int>()))
                .ReturnsAsync(new List<SyncJobChange>
                {
                    new()
                    {
                        Id = Guid.NewGuid(),
                        SyncJobId = _syncJobId,
                        ChangeTime = now.AddMinutes(-15),
                        ChangeReason = SyncJobChangeReason.IgnoreThresholdOnce.ToString(),
                        ChangedByDisplayName = "Configuration Owner"
                    }
                });
            SetupAggregatedMembers(MembershipAction.Add, addedUser);
            string? capturedPrompt = null;
            _mockOpenAIService
                .Setup(service => service.GetCompletionAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>()))
                .Callback<string, string>((_, prompt) => capturedPrompt = prompt)
                .ReturnsAsync("This sync reflects the configured membership rule.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedPrompt);
            StringAssert.Contains(capturedPrompt, "No configuration changes found.");
            Assert.IsFalse(capturedPrompt.Contains("Last configuration change"));
            Assert.IsFalse(capturedPrompt.Contains("Initial configuration"));
            StringAssert.Contains(capturedPrompt, "IgnoreThresholdOnce activated");
        }

        [TestMethod]
        public async Task ExecuteAsync_GroupOnlySourceWithAddsAndCompletedStatus_CallsOpenAI()
        {
            // Regression: previously "Skip Path B" (deleted) bailed with fallback text; now per-part blob attribution runs and OpenAI is called.
            _mockSyncJobHistoryRepository.Setup(x => x.GetByRunIdAsync(_runId))
                .ReturnsAsync(new global::Models.SyncJobHistory.SyncJobHistory
                {
                    SyncJobId = _syncJobId,
                    RunId = _runId,
                    Status = "Idle",
                    UpdatedAt = DateTime.UtcNow,
                    UsersAdded = 5,
                    UsersRemoved = 0,
                });

            _mockSyncJobRepository.Setup(x => x.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(new SyncJob
                {
                    Id = _syncJobId,
                    TargetOfficeGroupId = _targetGroupId,
                    Query = "[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-0000-0000-000000000001\"}]"
                });

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            _mockOpenAIService.Verify(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        [TestMethod]
        public async Task ExecuteAsync_ThresholdBlockedByStatus_CallsOpenAI()
        {
            _mockSyncJobHistoryRepository.Setup(x => x.GetByRunIdAsync(_runId))
                .ReturnsAsync(new global::Models.SyncJobHistory.SyncJobHistory
                {
                    SyncJobId = _syncJobId,
                    RunId = _runId,
                    Status = "ThresholdExceeded",
                    UpdatedAt = DateTime.UtcNow,
                    UsersAdded = 0,
                    UsersRemoved = 0,
                });

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("This sync completed successfully.", response.Explanation);
            _mockOpenAIService.Verify(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        /// <summary>
        /// FR-005a — after the threshold-violation counter column is dropped, blocked-run detection is
        /// derived from Status alone. A run persisted as ThresholdExceeded must still be detected
        /// as blocked and routed to OpenAI (this is the path all post-immediate-disable blocked
        /// runs take).
        /// </summary>
        [TestMethod]
        public async Task ExecuteAsync_ThresholdExceededStatus_CallsOpenAI()
        {
            string? capturedSystemPrompt = null;
            _mockSyncJobHistoryRepository.Setup(x => x.GetByRunIdAsync(_runId))
                .ReturnsAsync(new global::Models.SyncJobHistory.SyncJobHistory
                {
                    SyncJobId = _syncJobId,
                    RunId = _runId,
                    Status = "ThresholdExceeded",
                    UpdatedAt = DateTime.UtcNow,
                    UsersAdded = 0,
                    UsersRemoved = 0
                });

            _mockSyncJobHistoryRepository.Setup(x => x.GetBySyncJobIdAsync(_syncJobId, It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync(new List<global::Models.SyncJobHistory.SyncJobHistory>
                {
                    new() { RunId = Guid.NewGuid(), UpdatedAt = DateTime.UtcNow.AddHours(-1) }
                });
            _mockOpenAIService
                .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((systemPrompt, _) =>
                    capturedSystemPrompt = systemPrompt)
                .ReturnsAsync("This sync completed successfully.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedSystemPrompt);
            StringAssert.Contains(
                capturedSystemPrompt,
                "stopped contributing through the people-only rule");
            Assert.IsFalse(
                capturedSystemPrompt.Contains(
                    "users leaving the source group",
                    StringComparison.OrdinalIgnoreCase));
            _mockOpenAIService.Verify(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        /// <summary>
        /// FR-005a / D-007 — accepted, irreversible narration loss. Pre-immediate-disable early
        /// violations were persisted as Status=Idle with NULL/zero adds-removes, and the only signal
        /// they were threshold-blocked was the threshold-violation counter delta. With the counter
        /// gone, such a row is no longer detectable as blocked and falls through Skip Path A.
        ///
        /// This asserts the accepted degradation explicitly so it is a deliberate, tested outcome
        /// rather than a silent regression.
        /// </summary>
        [TestMethod]
        public async Task ExecuteAsync_PreFeatureIdleRowFormerlyBlockedByCounter_NoLongerDetectedAsBlocked()
        {
            _mockSyncJobHistoryRepository.Setup(x => x.GetByRunIdAsync(_runId))
                .ReturnsAsync(new global::Models.SyncJobHistory.SyncJobHistory
                {
                    SyncJobId = _syncJobId,
                    RunId = _runId,
                    Status = "Idle",
                    UpdatedAt = DateTime.UtcNow,
                    UsersAdded = 0,
                    UsersRemoved = 0,
                });

            _mockSyncJobHistoryRepository.Setup(x => x.GetBySyncJobIdAsync(_syncJobId, It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync(new List<global::Models.SyncJobHistory.SyncJobHistory>
                {
                    new() { RunId = Guid.NewGuid(), UpdatedAt = DateTime.UtcNow.AddHours(-1) }
                });

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(GetRunExplanationHandler.NoMembershipChanges, response.Explanation);
            _mockOpenAIService.Verify(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [TestMethod]
        public async Task ExecuteAsync_InformativeStatus_CallsOpenAI()
        {
            _mockSyncJobHistoryRepository.Setup(x => x.GetByRunIdAsync(_runId))
                .ReturnsAsync(new global::Models.SyncJobHistory.SyncJobHistory
                {
                    SyncJobId = _syncJobId,
                    RunId = _runId,
                    Status = "MembershipDataNotFound",
                    UpdatedAt = DateTime.UtcNow,
                    UsersAdded = 0,
                    UsersRemoved = 0,
                });

            var response = await _handler.ExecuteAsync(BuildRequest());

            _mockOpenAIService.Verify(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        }

        [TestMethod]
        public async Task ExecuteAsync_HasUsersAddedButNoConfigChange_CallsOpenAI()
        {
            _mockSyncJobHistoryRepository.Setup(x => x.GetByRunIdAsync(_runId))
                .ReturnsAsync(new global::Models.SyncJobHistory.SyncJobHistory
                {
                    SyncJobId = _syncJobId,
                    RunId = _runId,
                    Status = "Idle",
                    UpdatedAt = DateTime.UtcNow,
                    UsersAdded = 5,
                    UsersRemoved = 0,
                });

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            _mockOpenAIService.Verify(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        [TestMethod]
        public async Task ExecuteAsync_OpenAiRateLimited_ReturnsFallbackExplanation()
        {
            _mockSyncJobHistoryRepository.Setup(x => x.GetByRunIdAsync(_runId))
                .ReturnsAsync(new global::Models.SyncJobHistory.SyncJobHistory
                {
                    SyncJobId = _syncJobId,
                    RunId = _runId,
                    Status = "Idle",
                    UpdatedAt = DateTime.UtcNow,
                    UsersAdded = 5,
                    UsersRemoved = 0,
                });

            _mockOpenAIService
                .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("Failed with HTTP 429"));

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(GetRunExplanationHandler.FallbackExplanation, response.Explanation);
        }

        [TestMethod]
        public async Task ExecuteAsync_OpenAiEmptyResponse_ReturnsFallbackExplanation()
        {
            _mockSyncJobHistoryRepository.Setup(x => x.GetByRunIdAsync(_runId))
                .ReturnsAsync(new global::Models.SyncJobHistory.SyncJobHistory
                {
                    SyncJobId = _syncJobId,
                    RunId = _runId,
                    Status = "Idle",
                    UpdatedAt = DateTime.UtcNow,
                    UsersAdded = 5,
                    UsersRemoved = 0,
                });

            _mockOpenAIService
                .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync("");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(GetRunExplanationHandler.FallbackExplanation, response.Explanation);
        }

        [TestMethod]
        public async Task ExecuteAsync_OpenAiGenericException_ReturnsInternalServerError()
        {
            _mockSyncJobHistoryRepository.Setup(x => x.GetByRunIdAsync(_runId))
                .ReturnsAsync(new global::Models.SyncJobHistory.SyncJobHistory
                {
                    SyncJobId = _syncJobId,
                    RunId = _runId,
                    Status = "Idle",
                    UpdatedAt = DateTime.UtcNow,
                    UsersAdded = 5,
                    UsersRemoved = 0,
                });

            _mockOpenAIService
                .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("something else broke"));

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode);
            // Generic exceptions leave Explanation as the default empty string (no fallback text on 500).
            Assert.AreEqual(string.Empty, response.Explanation);
        }

        [TestMethod]
        public async Task ExecuteAsync_ConfigChangeInWindow_CallsOpenAI()
        {
            var changeTime = DateTime.UtcNow.AddMinutes(-5);
            _mockSyncJobChangeRepository
                .Setup(x => x.GetRecentConfigChangesBySyncJobIdAsync(_syncJobId, It.IsAny<DateTime>(), It.IsAny<int>()))
                .ReturnsAsync(new List<SyncJobChange>
                {
                    new()
                    {
                        Id = Guid.NewGuid(),
                        SyncJobId = _syncJobId,
                        ChangeReason = SyncJobChangeReason.Update.ToString(),
                        ChangeTime = changeTime,
                        ChangeDetails = "{\"query\":\"[{\\\"type\\\":\\\"SqlMembership\\\",\\\"source\\\":{\\\"filter\\\":\\\"Building = 'B40'\\\"}}]\"}"
                    }
                });

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            _mockOpenAIService.Verify(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        // ---------------- IsOpenAiRateLimited (private static) — covered transitively above ----------------

        // ---------------- CapAt150 ----------------

        [TestMethod]
        public void CapAt150_BothEmpty_ReturnsEmpty()
        {
            var (a, r) = GetRunExplanationHandler.CapAt150(new List<Guid>(), new List<Guid>());
            Assert.AreEqual(0, a.Count);
            Assert.AreEqual(0, r.Count);
        }

        [TestMethod]
        public void CapAt150_UnderCap_ReturnsAll()
        {
            var added = Enumerable.Range(0, 50).Select(_ => Guid.NewGuid()).ToList();
            var removed = Enumerable.Range(0, 50).Select(_ => Guid.NewGuid()).ToList();
            var (a, r) = GetRunExplanationHandler.CapAt150(added, removed);
            Assert.AreEqual(50, a.Count);
            Assert.AreEqual(50, r.Count);
        }

        [TestMethod]
        public void CapAt150_ExactlyAtCap_ReturnsAll()
        {
            var added = Enumerable.Range(0, 75).Select(_ => Guid.NewGuid()).ToList();
            var removed = Enumerable.Range(0, 75).Select(_ => Guid.NewGuid()).ToList();
            var (a, r) = GetRunExplanationHandler.CapAt150(added, removed);
            Assert.AreEqual(150, a.Count + r.Count);
        }

        [TestMethod]
        public void CapAt150_OverCap_BothSides_ReturnsCapped()
        {
            var added = Enumerable.Range(0, 100).Select(_ => Guid.NewGuid()).ToList();
            var removed = Enumerable.Range(0, 100).Select(_ => Guid.NewGuid()).ToList();
            var (a, r) = GetRunExplanationHandler.CapAt150(added, removed);
            Assert.IsTrue(a.Count + r.Count <= 150);
            Assert.IsTrue(a.Count > 0 && r.Count > 0);
        }

        [TestMethod]
        public void CapAt150_OnlyAdded_ReturnsCapped()
        {
            var added = Enumerable.Range(0, 300).Select(_ => Guid.NewGuid()).ToList();
            var (a, r) = GetRunExplanationHandler.CapAt150(added, new List<Guid>());
            Assert.IsTrue(a.Count <= 150);
            Assert.AreEqual(0, r.Count);
        }

        [TestMethod]
        public void CapAt150_OnlyRemoved_ReturnsCapped()
        {
            var removed = Enumerable.Range(0, 300).Select(_ => Guid.NewGuid()).ToList();
            var (a, r) = GetRunExplanationHandler.CapAt150(new List<Guid>(), removed);
            Assert.IsTrue(r.Count <= 150);
            Assert.AreEqual(0, a.Count);
        }

        // ---------------- BucketAttribution ----------------

        [TestMethod]
        public void BucketAttribution_ZeroTotal_ReturnsNone()
        {
            Assert.AreEqual("none", GetRunExplanationHandler.BucketAttribution(0, 0));
            Assert.AreEqual("none", GetRunExplanationHandler.BucketAttribution(5, 0));
        }

        [TestMethod]
        public void BucketAttribution_ZeroMatched_ReturnsNone()
        {
            Assert.AreEqual("none", GetRunExplanationHandler.BucketAttribution(0, 100));
        }

        [TestMethod]
        public void BucketAttribution_All_ReturnsAll()
        {
            Assert.AreEqual("all", GetRunExplanationHandler.BucketAttribution(100, 100));
            Assert.AreEqual("all", GetRunExplanationHandler.BucketAttribution(150, 100)); // over-count still "all"
        }

        [TestMethod]
        public void BucketAttribution_AlmostAll_ReturnsAlmostAll()
        {
            Assert.AreEqual("almost all", GetRunExplanationHandler.BucketAttribution(90, 100));
            Assert.AreEqual("almost all", GetRunExplanationHandler.BucketAttribution(85, 100));
        }

        [TestMethod]
        public void BucketAttribution_Most_ReturnsMost()
        {
            Assert.AreEqual("most", GetRunExplanationHandler.BucketAttribution(70, 100));
            Assert.AreEqual("most", GetRunExplanationHandler.BucketAttribution(60, 100));
        }

        [TestMethod]
        public void BucketAttribution_AboutHalf_ReturnsAboutHalf()
        {
            Assert.AreEqual("about half", GetRunExplanationHandler.BucketAttribution(50, 100));
            Assert.AreEqual("about half", GetRunExplanationHandler.BucketAttribution(40, 100));
        }

        [TestMethod]
        public void BucketAttribution_Some_ReturnsSome()
        {
            Assert.AreEqual("some", GetRunExplanationHandler.BucketAttribution(30, 100));
            Assert.AreEqual("some", GetRunExplanationHandler.BucketAttribution(15, 100));
        }

        [TestMethod]
        public void BucketAttribution_AFew_ReturnsAFew()
        {
            Assert.AreEqual("a few", GetRunExplanationHandler.BucketAttribution(5, 100));
            Assert.AreEqual("a few", GetRunExplanationHandler.BucketAttribution(1, 100));
        }

        // ---------------- ParseQueryParts ----------------

        [TestMethod]
        public void ParseQueryParts_Null_ReturnsNull()
        {
            Assert.IsNull(GetRunExplanationHandler.ParseQueryParts(null));
        }

        [TestMethod]
        public void ParseQueryParts_Empty_ReturnsNull()
        {
            Assert.IsNull(GetRunExplanationHandler.ParseQueryParts(""));
        }

        [TestMethod]
        public void ParseQueryParts_MalformedJson_ReturnsNull()
        {
            Assert.IsNull(GetRunExplanationHandler.ParseQueryParts("{not json"));
        }

        [TestMethod]
        public void ParseQueryParts_SqlMembershipFilterOnly_Parses()
        {
            var parts = GetRunExplanationHandler.ParseQueryParts(
                "[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"EmployeeType = 'FTE'\"}}]");
            Assert.IsNotNull(parts);
            Assert.AreEqual(1, parts!.Count);
            Assert.AreEqual("SqlMembership", parts[0].Type);
            Assert.AreEqual("EmployeeType = 'FTE'", parts[0].Filter);
            Assert.IsNull(parts[0].ManagerId);
            Assert.IsNull(parts[0].ManagerDepth);
            Assert.IsFalse(parts[0].Exclusionary);
        }

        [TestMethod]
        public void ParseQueryParts_SqlMembershipWithManagerUnbounded_Parses()
        {
            var parts = GetRunExplanationHandler.ParseQueryParts(
                "[{\"type\":\"SqlMembership\",\"source\":{\"manager\":{\"id\":42},\"filter\":\"EmployeeType = 'FTE'\"}}]");
            Assert.IsNotNull(parts);
            Assert.AreEqual("42", parts![0].ManagerId);
            Assert.IsNull(parts[0].ManagerDepth);
            Assert.AreEqual("EmployeeType = 'FTE'", parts[0].Filter);
        }

        [TestMethod]
        public void ParseQueryParts_SqlMembershipWithManagerDepth_Parses()
        {
            var parts = GetRunExplanationHandler.ParseQueryParts(
                "[{\"type\":\"SqlMembership\",\"source\":{\"manager\":{\"id\":42,\"depth\":2},\"filter\":\"EmployeeType = 'FTE'\"}}]");
            Assert.IsNotNull(parts);
            Assert.AreEqual("42", parts![0].ManagerId);
            Assert.AreEqual(2, parts[0].ManagerDepth);
        }

        [TestMethod]
        public void ParseQueryParts_GroupMembership_Parses()
        {
            var guid = "88fb73f2-0000-0000-0000-000000000000";
            var parts = GetRunExplanationHandler.ParseQueryParts(
                $"[{{\"type\":\"GroupMembership\",\"source\":\"{guid}\"}}]");
            Assert.IsNotNull(parts);
            Assert.AreEqual("GroupMembership", parts![0].Type);
            Assert.AreEqual(guid, parts[0].Source);
        }

        [TestMethod]
        public void ParseQueryParts_ExclusionaryPart_ParsesFlag()
        {
            var parts = GetRunExplanationHandler.ParseQueryParts(
                "[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"X = 1\"},\"exclusionary\":true}]");
            Assert.IsNotNull(parts);
            Assert.IsTrue(parts![0].Exclusionary);
        }

        [TestMethod]
        public void ParseQueryParts_ManagerIdAsString_Parses()
        {
            var parts = GetRunExplanationHandler.ParseQueryParts(
                "[{\"type\":\"SqlMembership\",\"source\":{\"manager\":{\"id\":\"42\",\"depth\":\"3\"},\"filter\":\"X = 1\"}}]");
            Assert.IsNotNull(parts);
            Assert.AreEqual("42", parts![0].ManagerId);
            Assert.AreEqual(3, parts[0].ManagerDepth);
        }

        [TestMethod]
        public void ParseQueryParts_MultipleParts_IndexesAssigned()
        {
            var parts = GetRunExplanationHandler.ParseQueryParts(
                "[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"A = 1\"}},{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-0000-0000-000000000001\"}]");
            Assert.IsNotNull(parts);
            Assert.AreEqual(2, parts!.Count);
            Assert.AreEqual(0, parts[0].Index);
            Assert.AreEqual(1, parts[1].Index);
        }

        [TestMethod]
        public void ParseQueryParts_NestedFilterOverridesTopLevel_UsesNested()
        {
            var parts = GetRunExplanationHandler.ParseQueryParts(
                "[{\"type\":\"SqlMembership\",\"filter\":\"topLevel\",\"source\":{\"filter\":\"nested\"}}]");
            Assert.IsNotNull(parts);
            Assert.AreEqual("nested", parts![0].Filter);
        }

        [TestMethod]
        public void ParseQueryParts_QualifiedFilterAndSelection_ArePreserved()
        {
            const string filter =
                "users.EmployeeType_Code = 'FTE' AND agents.BlueprintId = 'approved'";
            var parts = GetRunExplanationHandler.ParseQueryParts(
                "[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"" +
                filter +
                "\"},\"identityTypes\":[\"agents\",\"users\"]}]");

            Assert.IsNotNull(parts);
            Assert.AreEqual(filter, parts![0].Filter);
            Assert.AreEqual(IdentityTypeSet.All, parts[0].IdentityTypes);
        }

        [TestMethod]
        public void ParseQueryParts_OmittedSelection_DefaultsToUsersOnly()
        {
            var parts = GetRunExplanationHandler.ParseQueryParts(
                "[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Department = 'Sales'\"}}]");

            Assert.IsNotNull(parts);
            Assert.AreEqual(IdentityTypeSet.UsersOnly, parts![0].IdentityTypes);
        }

        [TestMethod]
        public void ParseQueryParts_GroupSelectionAndDefault_ArePreserved()
        {
            var explicitParts = GetRunExplanationHandler.ParseQueryParts(
                "[{\"type\":\"GroupMembership\",\"source\":\"11111111-1111-4111-8111-111111111111\",\"identityTypes\":[\"agents\"]}]");
            var omittedParts = GetRunExplanationHandler.ParseQueryParts(
                "[{\"type\":\"GroupMembership\",\"source\":\"11111111-1111-4111-8111-111111111111\"}]");

            Assert.IsNotNull(explicitParts);
            Assert.AreEqual(
                IdentityTypeSet.Of(IdentityType.Agents),
                explicitParts![0].IdentityTypes);
            Assert.IsNotNull(omittedParts);
            Assert.AreEqual(
                IdentityTypeSet.GroupDefault,
                omittedParts![0].IdentityTypes);
        }

        [TestMethod]
        public void ParseQueryParts_InvalidGroupSelection_IsUndetermined()
        {
            var parts = GetRunExplanationHandler.ParseQueryParts(
                "[{\"type\":\"GroupMembership\",\"source\":\"11111111-1111-4111-8111-111111111111\",\"identityTypes\":[\"users\",\"users\"]}]");

            Assert.IsNotNull(parts);
            Assert.IsNull(parts![0].IdentityTypes);
        }

        [TestMethod]
        public void ParseQueryParts_MisplacedSelection_IsUndetermined()
        {
            var parts = GetRunExplanationHandler.ParseQueryParts(
                "[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Department = 'Sales'\",\"identityTypes\":[\"agents\"]}}]");

            Assert.IsNotNull(parts);
            Assert.IsNull(parts![0].IdentityTypes);
        }

        [TestMethod]
        public void ParseQueryParts_SelectionKeyWithDifferentLetterCase_IsUndetermined()
        {
            var parts = GetRunExplanationHandler.ParseQueryParts(
                "[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Department = 'Sales'\"},\"IdentityTypes\":[\"agents\"]}]");

            Assert.IsNotNull(parts);
            Assert.IsNull(parts![0].IdentityTypes);
        }

        // ---------------- ParseMembershipDelta ----------------

        [TestMethod]
        public void ParseMembershipDelta_Empty_ReturnsEmpty()
        {
            var (a, r) = GetRunExplanationHandler.ParseMembershipDelta("");
            Assert.AreEqual(0, a.Count);
            Assert.AreEqual(0, r.Count);
        }

        [TestMethod]
        public void ParseMembershipDelta_Malformed_ReturnsEmpty()
        {
            var (a, r) = GetRunExplanationHandler.ParseMembershipDelta("not json at all");
            Assert.AreEqual(0, a.Count);
            Assert.AreEqual(0, r.Count);
        }

        [TestMethod]
        public void ParseMembershipDelta_AddsAndRemoves_ParsesBoth()
        {
            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();
            var json = $"{{\"SourceMembers\":[{{\"ObjectId\":\"{id1}\",\"MembershipAction\":\"Add\"}},{{\"ObjectId\":\"{id2}\",\"MembershipAction\":\"Remove\"}}]}}";
            var (a, r) = GetRunExplanationHandler.ParseMembershipDelta(json);
            Assert.AreEqual(1, a.Count);
            Assert.AreEqual(id1, a[0]);
            Assert.AreEqual(1, r.Count);
            Assert.AreEqual(id2, r[0]);
        }

        [TestMethod]
        public void ParseMembershipDelta_NumericMembershipAction_Parses()
        {
            var id1 = Guid.NewGuid();
            // MembershipAction.Add = 1 (None=0, Add=1, Remove=2)
            var json = $"{{\"sourceMembers\":[{{\"objectId\":\"{id1}\",\"membershipAction\":1}}]}}";
            var (a, r) = GetRunExplanationHandler.ParseMembershipDelta(json);
            Assert.AreEqual(1, a.Count);
            Assert.AreEqual(0, r.Count);
        }

        [TestMethod]
        public void ParseMembershipDelta_MissingObjectId_Skipped()
        {
            var json = "{\"SourceMembers\":[{\"MembershipAction\":\"Add\"}]}";
            var (a, r) = GetRunExplanationHandler.ParseMembershipDelta(json);
            Assert.AreEqual(0, a.Count);
            Assert.AreEqual(0, r.Count);
        }

        // ---------------- CollectGroupSourceGuids ----------------

        [TestMethod]
        public void CollectGroupSourceGuids_Empty_ReturnsEmpty()
        {
            var result = GetRunExplanationHandler.CollectGroupSourceGuids();
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void CollectGroupSourceGuids_NullList_ReturnsEmpty()
        {
            var result = GetRunExplanationHandler.CollectGroupSourceGuids((IReadOnlyList<GetRunExplanationHandler.QueryPartInfo>?)null);
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void CollectGroupSourceGuids_MixedPartTypes_OnlyGroupFamilyKept()
        {
            var g1 = Guid.NewGuid();
            var parts = new List<GetRunExplanationHandler.QueryPartInfo>
            {
                new() { Type = "GroupMembership", Source = g1.ToString() },
                new() { Type = "SqlMembership", Source = Guid.NewGuid().ToString() },
                new() { Type = "PlaceMembership", Source = Guid.NewGuid().ToString() },
            };
            var result = GetRunExplanationHandler.CollectGroupSourceGuids(parts);
            Assert.AreEqual(1, result.Count);
            Assert.IsTrue(result.Contains(g1));
        }

        [TestMethod]
        public void CollectGroupSourceGuids_DuplicateGuids_Deduplicated()
        {
            var g1 = Guid.NewGuid();
            var parts = new List<GetRunExplanationHandler.QueryPartInfo>
            {
                new() { Type = "GroupMembership", Source = g1.ToString() },
                new() { Type = "GroupOwnership", Source = g1.ToString() },
            };
            var result = GetRunExplanationHandler.CollectGroupSourceGuids(parts);
            Assert.AreEqual(1, result.Count);
        }

        [TestMethod]
        public void CollectGroupSourceGuids_NonGuidSource_Skipped()
        {
            var parts = new List<GetRunExplanationHandler.QueryPartInfo>
            {
                new() { Type = "GroupMembership", Source = "not-a-guid" },
            };
            var result = GetRunExplanationHandler.CollectGroupSourceGuids(parts);
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void CollectGroupSourceGuids_EmptyGuid_Skipped()
        {
            var parts = new List<GetRunExplanationHandler.QueryPartInfo>
            {
                new() { Type = "GroupMembership", Source = Guid.Empty.ToString() },
            };
            var result = GetRunExplanationHandler.CollectGroupSourceGuids(parts);
            Assert.AreEqual(0, result.Count);
        }

        // ---------------- CollectManagerIds ----------------

        [TestMethod]
        public void CollectManagerIds_Empty_ReturnsEmpty()
        {
            var result = GetRunExplanationHandler.CollectManagerIds();
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void CollectManagerIds_ValidIds_Kept()
        {
            var parts = new List<GetRunExplanationHandler.QueryPartInfo>
            {
                new() { Type = "SqlMembership", ManagerId = "42" },
                new() { Type = "SqlMembership", ManagerId = "100" },
            };
            var result = GetRunExplanationHandler.CollectManagerIds(parts);
            Assert.AreEqual(2, result.Count);
            Assert.IsTrue(result.Contains(42));
            Assert.IsTrue(result.Contains(100));
        }

        [TestMethod]
        public void CollectManagerIds_ZeroAndNegative_Skipped()
        {
            var parts = new List<GetRunExplanationHandler.QueryPartInfo>
            {
                new() { Type = "SqlMembership", ManagerId = "0" },
                new() { Type = "SqlMembership", ManagerId = "-5" },
            };
            var result = GetRunExplanationHandler.CollectManagerIds(parts);
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void CollectManagerIds_NonNumeric_Skipped()
        {
            var parts = new List<GetRunExplanationHandler.QueryPartInfo>
            {
                new() { Type = "SqlMembership", ManagerId = "abc" },
            };
            var result = GetRunExplanationHandler.CollectManagerIds(parts);
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void CollectManagerIds_Duplicates_Deduplicated()
        {
            var parts = new List<GetRunExplanationHandler.QueryPartInfo>
            {
                new() { Type = "SqlMembership", ManagerId = "42" },
                new() { Type = "SqlMembership", ManagerId = "42" },
            };
            var result = GetRunExplanationHandler.CollectManagerIds(parts);
            Assert.AreEqual(1, result.Count);
        }

        // ---------------- ClassifyJobSourceKind ----------------

        [TestMethod]
        public void ClassifyJobSourceKind_NullList_ReturnsNone()
        {
            Assert.AreEqual(GetRunExplanationHandler.JobSourceKind.None,
                GetRunExplanationHandler.ClassifyJobSourceKind(null));
        }

        [TestMethod]
        public void ClassifyJobSourceKind_EmptyList_ReturnsNone()
        {
            Assert.AreEqual(GetRunExplanationHandler.JobSourceKind.None,
                GetRunExplanationHandler.ClassifyJobSourceKind(new List<GetRunExplanationHandler.QueryPartInfo>()));
        }

        [TestMethod]
        public void ClassifyJobSourceKind_SqlOnly_ReturnsHrData()
        {
            var parts = new List<GetRunExplanationHandler.QueryPartInfo>
            {
                new() { Type = "SqlMembership" },
            };
            Assert.AreEqual(GetRunExplanationHandler.JobSourceKind.HrData,
                GetRunExplanationHandler.ClassifyJobSourceKind(parts));
        }

        [TestMethod]
        public void ClassifyJobSourceKind_GroupMembershipOnly_ReturnsGroup()
        {
            var parts = new List<GetRunExplanationHandler.QueryPartInfo>
            {
                new() { Type = "GroupMembership" },
            };
            Assert.AreEqual(GetRunExplanationHandler.JobSourceKind.Group,
                GetRunExplanationHandler.ClassifyJobSourceKind(parts));
        }

        [TestMethod]
        public void ClassifyJobSourceKind_GroupOwnershipOnly_ReturnsGroup()
        {
            var parts = new List<GetRunExplanationHandler.QueryPartInfo>
            {
                new() { Type = "GroupOwnership" },
            };
            Assert.AreEqual(GetRunExplanationHandler.JobSourceKind.Group,
                GetRunExplanationHandler.ClassifyJobSourceKind(parts));
        }

        [TestMethod]
        public void ClassifyJobSourceKind_TeamsChannelOnly_ReturnsGroup()
        {
            var parts = new List<GetRunExplanationHandler.QueryPartInfo>
            {
                new() { Type = "TeamsChannelMembership" },
            };
            Assert.AreEqual(GetRunExplanationHandler.JobSourceKind.Group,
                GetRunExplanationHandler.ClassifyJobSourceKind(parts));
        }

        [TestMethod]
        public void ClassifyJobSourceKind_SqlAndGroup_ReturnsMixed()
        {
            var parts = new List<GetRunExplanationHandler.QueryPartInfo>
            {
                new() { Type = "SqlMembership" },
                new() { Type = "GroupMembership" },
            };
            Assert.AreEqual(GetRunExplanationHandler.JobSourceKind.Mixed,
                GetRunExplanationHandler.ClassifyJobSourceKind(parts));
        }

        [TestMethod]
        public void ClassifyJobSourceKind_PlaceMembershipOnly_ReturnsNone()
        {
            // PlaceMembership is neither an HR filter nor a group-referencing source, so it maps to the generic fallback.
            var parts = new List<GetRunExplanationHandler.QueryPartInfo>
            {
                new() { Type = "PlaceMembership" },
            };
            Assert.AreEqual(GetRunExplanationHandler.JobSourceKind.None,
                GetRunExplanationHandler.ClassifyJobSourceKind(parts));
        }

        // ---------------- UpstreamFallbackPhrase ----------------

        [TestMethod]
        public void UpstreamFallbackPhrase_HrData_UsesHrWording()
        {
            var result = InvokeStaticPrivate<string>(typeof(GetRunExplanationHandler), "UpstreamFallbackPhrase",
                new object?[] { GetRunExplanationHandler.JobSourceKind.HrData });
            Assert.AreEqual("upstream HR data", result);
        }

        [TestMethod]
        public void UpstreamFallbackPhrase_Group_UsesGroupWording()
        {
            var result = InvokeStaticPrivate<string>(typeof(GetRunExplanationHandler), "UpstreamFallbackPhrase",
                new object?[] { GetRunExplanationHandler.JobSourceKind.Group });
            Assert.AreEqual("upstream source group memberships", result);
        }

        [TestMethod]
        public void UpstreamFallbackPhrase_Mixed_UsesGenericWording()
        {
            var result = InvokeStaticPrivate<string>(typeof(GetRunExplanationHandler), "UpstreamFallbackPhrase",
                new object?[] { GetRunExplanationHandler.JobSourceKind.Mixed });
            Assert.AreEqual("upstream membership sources", result);
        }

        [TestMethod]
        public void UpstreamFallbackPhrase_None_UsesGenericWording()
        {
            var result = InvokeStaticPrivate<string>(typeof(GetRunExplanationHandler), "UpstreamFallbackPhrase",
                new object?[] { GetRunExplanationHandler.JobSourceKind.None });
            Assert.AreEqual("upstream membership sources", result);
        }

        // ---------------- Job source kind line injected into the run prompt ----------------

        [TestMethod]
        public async Task ExecuteAsync_HrDataJob_PromptIncludesHrSourceKindAndPhrase()
        {
            _mockSyncJobHistoryRepository.Setup(x => x.GetByRunIdAsync(_runId))
                .ReturnsAsync(new global::Models.SyncJobHistory.SyncJobHistory
                {
                    SyncJobId = _syncJobId,
                    RunId = _runId,
                    Status = "Idle",
                    UpdatedAt = DateTime.UtcNow,
                    StartTime = DateTime.UtcNow.AddMinutes(-5),
                    EndTime = DateTime.UtcNow,
                    UsersAdded = 2,
                    UsersRemoved = 1,
                    BeforeSyncUserCount = 100,
                    AfterSyncUserCount = 101,
                });

            var blob = new BlobResult { BlobStatus = BlobStatus.Found, Path = "test/path.json" };
            _mockBlobStorageRepository.Setup(x => x.FindAggregatedFileByRunIdAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(blob);
            var fakeJson = $"{{\"SourceMembers\":[" +
                $"{{\"ObjectId\":\"{Guid.NewGuid()}\",\"MembershipAction\":1}}," +
                $"{{\"ObjectId\":\"{Guid.NewGuid()}\",\"MembershipAction\":2}}]}}";
            _mockBlobStorageRepository.Setup(x => x.DownloadFileAsync(It.IsAny<string>()))
                .ReturnsAsync(new BlobResult { BlobStatus = BlobStatus.Found, Content = fakeJson });

            string? capturedPrompt = null;
            _mockOpenAIService.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((_, prompt) => capturedPrompt = prompt)
                .ReturnsAsync("This sync reflects changes in upstream HR data.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedPrompt);
            // The default test job's Query is a single SqlMembership part -> HrData.
            StringAssert.Contains(capturedPrompt!, "Job source kind: HrData");
            StringAssert.Contains(capturedPrompt!, "upstream HR data");
            // The HR-data prompt must never direct the model to the group-source phrasing.
            Assert.IsFalse(capturedPrompt!.Contains("upstream source group memberships"),
                "HR-data run prompt must not offer the group-source fallback phrasing.");
        }

        [TestMethod]
        public async Task ExecuteAsync_GroupJob_PromptIncludesGroupSourceKindAndPhrase()
        {
            _mockSyncJobRepository.Setup(x => x.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(new SyncJob
                {
                    Id = _syncJobId,
                    TargetOfficeGroupId = _targetGroupId,
                    Query = "[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-0000-0000-000000000001\"}]"
                });
            _mockSyncJobHistoryRepository.Setup(x => x.GetByRunIdAsync(_runId))
                .ReturnsAsync(new global::Models.SyncJobHistory.SyncJobHistory
                {
                    SyncJobId = _syncJobId,
                    RunId = _runId,
                    Status = "Idle",
                    UpdatedAt = DateTime.UtcNow,
                    StartTime = DateTime.UtcNow.AddMinutes(-5),
                    EndTime = DateTime.UtcNow,
                    UsersAdded = 2,
                    UsersRemoved = 1,
                    BeforeSyncUserCount = 100,
                    AfterSyncUserCount = 101,
                });

            var blob = new BlobResult { BlobStatus = BlobStatus.Found, Path = "test/path.json" };
            _mockBlobStorageRepository.Setup(x => x.FindAggregatedFileByRunIdAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(blob);
            var fakeJson = $"{{\"SourceMembers\":[" +
                $"{{\"ObjectId\":\"{Guid.NewGuid()}\",\"MembershipAction\":1}}," +
                $"{{\"ObjectId\":\"{Guid.NewGuid()}\",\"MembershipAction\":2}}]}}";
            _mockBlobStorageRepository.Setup(x => x.DownloadFileAsync(It.IsAny<string>()))
                .ReturnsAsync(new BlobResult { BlobStatus = BlobStatus.Found, Content = fakeJson });

            string? capturedPrompt = null;
            _mockOpenAIService.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((_, prompt) => capturedPrompt = prompt)
                .ReturnsAsync("This sync reflects changes in upstream source group memberships.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedPrompt);
            StringAssert.Contains(capturedPrompt!, "Job source kind: Group");
            StringAssert.Contains(capturedPrompt!, "upstream source group memberships");
        }

        // ---------------- FormatGroupRef ----------------

        [TestMethod]
        public void FormatGroupRef_NullSource_ReturnsEmpty()
        {
            Assert.AreEqual(string.Empty, GetRunExplanationHandler.FormatGroupRef(null, null));
        }

        [TestMethod]
        public void FormatGroupRef_EmptySource_ReturnsEmpty()
        {
            Assert.AreEqual(string.Empty, GetRunExplanationHandler.FormatGroupRef("", null));
        }

        [TestMethod]
        public void FormatGroupRef_NonGuidSource_ReturnsAsIs()
        {
            Assert.AreEqual("some-non-guid", GetRunExplanationHandler.FormatGroupRef("some-non-guid", null));
        }

        [TestMethod]
        public void FormatGroupRef_GuidWithoutCache_ReturnsGuid()
        {
            var g = Guid.NewGuid().ToString();
            Assert.AreEqual(g, GetRunExplanationHandler.FormatGroupRef(g, null));
        }

        [TestMethod]
        public void FormatGroupRef_GuidWithCache_ReturnsNameAndGuid()
        {
            var g = Guid.NewGuid();
            var cache = new Dictionary<Guid, string> { { g, "Engineering FTEs" } };
            var result = GetRunExplanationHandler.FormatGroupRef(g.ToString(), cache);
            StringAssert.Contains(result, "Engineering FTEs");
            StringAssert.Contains(result, g.ToString());
        }

        // ---------------- BuildGroupNameReferenceTable ----------------

        [TestMethod]
        public void BuildGroupNameReferenceTable_Null_ReturnsEmpty()
        {
            Assert.AreEqual(string.Empty, GetRunExplanationHandler.BuildGroupNameReferenceTable(null));
        }

        [TestMethod]
        public void BuildGroupNameReferenceTable_Empty_ReturnsEmpty()
        {
            Assert.AreEqual(string.Empty, GetRunExplanationHandler.BuildGroupNameReferenceTable(new Dictionary<Guid, string>()));
        }

        [TestMethod]
        public void BuildGroupNameReferenceTable_HasEntries_ReturnsTable()
        {
            var g = Guid.NewGuid();
            var cache = new Dictionary<Guid, string> { { g, "Team A" } };
            var result = GetRunExplanationHandler.BuildGroupNameReferenceTable(cache);
            StringAssert.Contains(result, "Source group display names");
            StringAssert.Contains(result, g.ToString());
            StringAssert.Contains(result, "Team A");
        }

        // ---------------- FormatManagerScopeWithName ----------------

        [TestMethod]
        public void FormatManagerScopeWithName_FilterOnly_ReturnsBase()
        {
            var part = new GetRunExplanationHandler.QueryPartInfo { ManagerId = null };
            var result = GetRunExplanationHandler.FormatManagerScopeWithName(part, null);
            StringAssert.Contains(result, "filter-only");
        }

        [TestMethod]
        public void FormatManagerScopeWithName_UnboundedNoCache_ReturnsIdBased()
        {
            var part = new GetRunExplanationHandler.QueryPartInfo { ManagerId = "42" };
            var result = GetRunExplanationHandler.FormatManagerScopeWithName(part, null);
            StringAssert.Contains(result, "id=42");
            StringAssert.Contains(result, "unbounded depth");
        }

        [TestMethod]
        public void FormatManagerScopeWithName_UnboundedWithCache_UsesName()
        {
            var part = new GetRunExplanationHandler.QueryPartInfo { ManagerId = "42" };
            var cache = new Dictionary<int, string> { { 42, "Alice Smith" } };
            var result = GetRunExplanationHandler.FormatManagerScopeWithName(part, cache);
            StringAssert.Contains(result, "Alice Smith");
        }

        [TestMethod]
        public void FormatManagerScopeWithName_DepthCappedNoCache_ReturnsIdBased()
        {
            var part = new GetRunExplanationHandler.QueryPartInfo { ManagerId = "42", ManagerDepth = 2 };
            var result = GetRunExplanationHandler.FormatManagerScopeWithName(part, null);
            StringAssert.Contains(result, "id=42");
            StringAssert.Contains(result, "depth<=2");
        }

        [TestMethod]
        public void FormatManagerScopeWithName_DepthCappedWithCache_UsesName()
        {
            var part = new GetRunExplanationHandler.QueryPartInfo { ManagerId = "42", ManagerDepth = 2 };
            var cache = new Dictionary<int, string> { { 42, "Alice Smith" } };
            var result = GetRunExplanationHandler.FormatManagerScopeWithName(part, cache);
            StringAssert.Contains(result, "Alice Smith");
            StringAssert.Contains(result, "depth<=2");
        }

        [TestMethod]
        public void FormatManagerScopeWithName_NonNumericIdIgnoresCache_ReturnsBase()
        {
            var part = new GetRunExplanationHandler.QueryPartInfo { ManagerId = "not-a-number" };
            var cache = new Dictionary<int, string> { { 42, "Alice Smith" } };
            var result = GetRunExplanationHandler.FormatManagerScopeWithName(part, cache);
            Assert.IsFalse(result.Contains("Alice Smith"));
        }

        // ---------------- BuildManagerNameReferenceTable ----------------

        [TestMethod]
        public void BuildManagerNameReferenceTable_Null_ReturnsEmpty()
        {
            Assert.AreEqual(string.Empty, GetRunExplanationHandler.BuildManagerNameReferenceTable(null));
        }

        [TestMethod]
        public void BuildManagerNameReferenceTable_Empty_ReturnsEmpty()
        {
            Assert.AreEqual(string.Empty, GetRunExplanationHandler.BuildManagerNameReferenceTable(new Dictionary<int, string>()));
        }

        [TestMethod]
        public void BuildManagerNameReferenceTable_HasEntries_ReturnsTable()
        {
            var cache = new Dictionary<int, string> { { 42, "Alice Smith" } };
            var result = GetRunExplanationHandler.BuildManagerNameReferenceTable(cache);
            StringAssert.Contains(result, "Manager display names");
            StringAssert.Contains(result, "42");
            StringAssert.Contains(result, "Alice Smith");
        }

        // ---------------- FormatSqlPartLabel ----------------

        [TestMethod]
        public void FormatSqlPartLabel_FilterOnly_UsesFilterOnlyLabel()
        {
            var part = new GetRunExplanationHandler.QueryPartInfo { Index = 0, ManagerId = null };
            var result = GetRunExplanationHandler.FormatSqlPartLabel(part, null);
            StringAssert.Contains(result, "filter-only");
        }

        [TestMethod]
        public void FormatSqlPartLabel_WithManagerScope_IncludesScope()
        {
            var part = new GetRunExplanationHandler.QueryPartInfo { Index = 2, ManagerId = "42" };
            var result = GetRunExplanationHandler.FormatSqlPartLabel(part, null);
            StringAssert.Contains(result, "#3"); // Index + 1
            StringAssert.Contains(result, "id=42");
        }

        // ---------------- FormatGroupFamilyPartLabel ----------------

        [TestMethod]
        public void FormatGroupFamilyPartLabel_ResolvedName_UsesName()
        {
            var g = Guid.NewGuid();
            var part = new GetRunExplanationHandler.QueryPartInfo
            {
                Index = 0,
                Type = "GroupMembership",
                Source = g.ToString()
            };
            var cache = new Dictionary<Guid, string> { { g, "Team A" } };
            var result = GetRunExplanationHandler.FormatGroupFamilyPartLabel(part, cache);
            StringAssert.Contains(result, "Team A");
            StringAssert.Contains(result, "GroupMembership");
        }

        [TestMethod]
        public void FormatGroupFamilyPartLabel_NoCache_FallsBackToGuid()
        {
            var g = Guid.NewGuid();
            var part = new GetRunExplanationHandler.QueryPartInfo
            {
                Index = 0,
                Type = "GroupMembership",
                Source = g.ToString()
            };
            var result = GetRunExplanationHandler.FormatGroupFamilyPartLabel(part, null);
            StringAssert.Contains(result, g.ToString());
        }

        // ---------------- QueryPartInfo.FormatManagerScope ----------------

        [TestMethod]
        public void QueryPartInfo_FormatManagerScope_NoManager()
        {
            var part = new GetRunExplanationHandler.QueryPartInfo { ManagerId = null };
            StringAssert.Contains(part.FormatManagerScope(), "filter-only");
        }

        [TestMethod]
        public void QueryPartInfo_FormatManagerScope_Unbounded()
        {
            var part = new GetRunExplanationHandler.QueryPartInfo { ManagerId = "42" };
            var s = part.FormatManagerScope();
            StringAssert.Contains(s, "id=42");
            StringAssert.Contains(s, "unbounded depth");
        }

        [TestMethod]
        public void QueryPartInfo_FormatManagerScope_DepthCapped()
        {
            var part = new GetRunExplanationHandler.QueryPartInfo { ManagerId = "42", ManagerDepth = 2 };
            var s = part.FormatManagerScope();
            StringAssert.Contains(s, "id=42");
            StringAssert.Contains(s, "depth<=2");
        }

        // ---------------- ExtractAttributeNamesFromSqlFilter ----------------

        [TestMethod]
        public void ExtractAttributeNamesFromSqlFilter_SingleEquals_ExtractsAttribute()
        {
            var set = new HashSet<string>();
            GetRunExplanationHandler.ExtractAttributeNamesFromSqlFilter("EmployeeType = 'FTE'", set);
            Assert.IsTrue(set.Contains("EmployeeType"));
        }

        [TestMethod]
        public void ExtractAttributeNamesFromSqlFilter_AndCombination_ExtractsAll()
        {
            // The parser checks "=" before ">=" so uses = for both operands.
            var set = new HashSet<string>();
            GetRunExplanationHandler.ExtractAttributeNamesFromSqlFilter("EmployeeType = 'FTE' AND Dept = 'HR'", set);
            Assert.IsTrue(set.Contains("EmployeeType"));
            Assert.IsTrue(set.Contains("Dept"));
        }

        [TestMethod]
        public void ExtractAttributeNamesFromSqlFilter_InOperator_ExtractsAttribute()
        {
            var set = new HashSet<string>();
            GetRunExplanationHandler.ExtractAttributeNamesFromSqlFilter("Status IN ('A', 'B')", set);
            Assert.IsTrue(set.Contains("Status"));
        }

        [TestMethod]
        public void ExtractAttributeNamesFromSqlFilter_OrCombination_ExtractsAll()
        {
            var set = new HashSet<string>();
            GetRunExplanationHandler.ExtractAttributeNamesFromSqlFilter("A = 1 OR B = 2", set);
            Assert.IsTrue(set.Contains("A"));
            Assert.IsTrue(set.Contains("B"));
        }

        // ---------------- NormalizeQuery ----------------

        [TestMethod]
        public void NormalizeQuery_Null_ReturnsNull()
        {
            var result = InvokeStaticPrivate<string?>(typeof(GetRunExplanationHandler), "NormalizeQuery", new object?[] { null });
            Assert.IsNull(result);
        }

        [TestMethod]
        public void NormalizeQuery_EmptyString_ReturnsNull()
        {
            // Empty is whitespace, so NormalizeQuery returns null.
            var result = InvokeStaticPrivate<string?>(typeof(GetRunExplanationHandler), "NormalizeQuery", new object?[] { "" });
            Assert.IsNull(result);
        }

        [TestMethod]
        public void NormalizeQuery_WithWhitespace_Normalizes()
        {
            var result = InvokeStaticPrivate<string?>(typeof(GetRunExplanationHandler), "NormalizeQuery", new object?[] { "  hello   world  " });
            Assert.IsNotNull(result);
            Assert.IsFalse(result!.StartsWith(" "));
            Assert.IsFalse(result.EndsWith(" "));
        }

        // ---------------- ExtractQueryFromChangeDetails ----------------

        [TestMethod]
        public void ExtractQueryFromChangeDetails_Null_ReturnsNull()
        {
            var result = InvokeStaticPrivate<string?>(typeof(GetRunExplanationHandler), "ExtractQueryFromChangeDetails", new object?[] { null });
            Assert.IsNull(result);
        }

        [TestMethod]
        public void ExtractQueryFromChangeDetails_Empty_ReturnsNull()
        {
            var result = InvokeStaticPrivate<string?>(typeof(GetRunExplanationHandler), "ExtractQueryFromChangeDetails", new object?[] { "" });
            Assert.IsNull(result);
        }

        [TestMethod]
        public void ExtractQueryFromChangeDetails_MalformedJson_ReturnsNull()
        {
            var result = InvokeStaticPrivate<string?>(typeof(GetRunExplanationHandler), "ExtractQueryFromChangeDetails", new object?[] { "not json" });
            Assert.IsNull(result);
        }

        [TestMethod]
        public void ExtractQueryFromChangeDetails_CamelCaseQuery_ReturnsValue()
        {
            var result = InvokeStaticPrivate<string?>(typeof(GetRunExplanationHandler), "ExtractQueryFromChangeDetails",
                new object?[] { "{\"query\":\"[{\\\"type\\\":\\\"SqlMembership\\\"}]\"}" });
            Assert.IsNotNull(result);
            StringAssert.Contains(result!, "SqlMembership");
        }

        [TestMethod]
        public void ExtractQueryFromChangeDetails_PascalCaseQuery_ReturnsValue()
        {
            var result = InvokeStaticPrivate<string?>(typeof(GetRunExplanationHandler), "ExtractQueryFromChangeDetails",
                new object?[] { "{\"Query\":\"[{\\\"type\\\":\\\"SqlMembership\\\"}]\"}" });
            Assert.IsNotNull(result);
            StringAssert.Contains(result!, "SqlMembership");
        }

        [TestMethod]
        public void ExtractQueryFromChangeDetails_MissingQueryKey_ReturnsNull()
        {
            var result = InvokeStaticPrivate<string?>(typeof(GetRunExplanationHandler), "ExtractQueryFromChangeDetails",
                new object?[] { "{\"other\":\"value\"}" });
            Assert.IsNull(result);
        }

        // ---------------- ExecuteAsync — full pipeline with realistic mocks ----------------
        // Each of these tests exercises hundreds of untested lines by driving BuildRunPrompt,
        // BuildConfigurationDiff, ComputeAddsAttributionAsync, and the name resolvers.

        [TestMethod]
        public async Task ExecuteAsync_FullPipeline_WithSqlPartAndDelta_CallsOpenAI()
        {
            _mockSyncJobHistoryRepository.Setup(x => x.GetByRunIdAsync(_runId))
                .ReturnsAsync(new global::Models.SyncJobHistory.SyncJobHistory
                {
                    SyncJobId = _syncJobId,
                    RunId = _runId,
                    Status = "Idle",
                    UpdatedAt = DateTime.UtcNow,
                    StartTime = DateTime.UtcNow.AddMinutes(-5),
                    EndTime = DateTime.UtcNow,
                    UsersAdded = 3,
                    UsersRemoved = 1,
                    BeforeSyncUserCount = 100,
                    AfterSyncUserCount = 102,
                });

            // Provide a matching blob so ReadMembershipDeltaAsync + ParseMembershipDelta + TryDecompress fire.
            var blob = new BlobResult { BlobStatus = BlobStatus.Found, Path = "test/path.json" };
            _mockBlobStorageRepository.Setup(x => x.FindAggregatedFileByRunIdAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(blob);
            var fakeJson = $"{{\"SourceMembers\":[" +
                $"{{\"ObjectId\":\"{Guid.NewGuid()}\",\"MembershipAction\":1}}," +
                $"{{\"ObjectId\":\"{Guid.NewGuid()}\",\"MembershipAction\":1}}," +
                $"{{\"ObjectId\":\"{Guid.NewGuid()}\",\"MembershipAction\":1}}," +
                $"{{\"ObjectId\":\"{Guid.NewGuid()}\",\"MembershipAction\":2}}]}}";
            _mockBlobStorageRepository.Setup(x => x.DownloadFileAsync(It.IsAny<string>()))
                .ReturnsAsync(new BlobResult { BlobStatus = BlobStatus.Found, Content = fakeJson });

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            _mockOpenAIService.Verify(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        [TestMethod]
        public async Task ExecuteAsync_FullPipeline_WithConfigChangeInWindow_CallsOpenAIWithDiff()
        {
            var previousRunTime = DateTime.UtcNow.AddHours(-1);
            var configChangeTime = DateTime.UtcNow.AddMinutes(-30);

            _mockSyncJobHistoryRepository.Setup(x => x.GetByRunIdAsync(_runId))
                .ReturnsAsync(new global::Models.SyncJobHistory.SyncJobHistory
                {
                    SyncJobId = _syncJobId,
                    RunId = _runId,
                    Status = "Idle",
                    UpdatedAt = DateTime.UtcNow,
                    StartTime = DateTime.UtcNow.AddMinutes(-5),
                    EndTime = DateTime.UtcNow,
                    UsersAdded = 5,
                    UsersRemoved = 0,
                });

            _mockSyncJobHistoryRepository.Setup(x => x.GetBySyncJobIdAsync(_syncJobId, It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync(new List<global::Models.SyncJobHistory.SyncJobHistory>
                {
                    new() { RunId = Guid.NewGuid(), UpdatedAt = previousRunTime }
                });

            _mockSyncJobChangeRepository
                .Setup(x => x.GetRecentConfigChangesBySyncJobIdAsync(_syncJobId, It.IsAny<DateTime>(), It.IsAny<int>()))
                .ReturnsAsync(new List<SyncJobChange>
                {
                    new()
                    {
                        Id = Guid.NewGuid(),
                        SyncJobId = _syncJobId,
                        ChangeReason = SyncJobChangeReason.Update.ToString(),
                        ChangeTime = configChangeTime,
                        ChangedByDisplayName = "admin@contoso.com",
                        ChangeDetails = "{\"query\":\"[{\\\"type\\\":\\\"SqlMembership\\\",\\\"source\\\":{\\\"filter\\\":\\\"EmployeeType = 'FTE'\\\"}}]\"}"
                    },
                    new()
                    {
                        Id = Guid.NewGuid(),
                        SyncJobId = _syncJobId,
                        ChangeReason = SyncJobChangeReason.Onboarding.ToString(),
                        ChangeTime = DateTime.UtcNow.AddDays(-30),
                        ChangeDetails = "{\"query\":\"[{\\\"type\\\":\\\"SqlMembership\\\",\\\"source\\\":{\\\"filter\\\":\\\"Building = 'B40'\\\"}}]\"}"
                    }
                });

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            _mockOpenAIService.Verify(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        [TestMethod]
        public async Task ExecuteAsync_FullPipeline_WithGroupMembershipPart_ResolvesNames()
        {
            var groupGuid = Guid.NewGuid();
            _mockSyncJobRepository.Setup(x => x.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(new SyncJob
                {
                    Id = _syncJobId,
                    TargetOfficeGroupId = _targetGroupId,
                    Query = "[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"EmployeeType = 'FTE'\"}}," +
                            "{\"type\":\"GroupMembership\",\"source\":\"" + groupGuid + "\"}]"
                });

            _mockSyncJobHistoryRepository.Setup(x => x.GetByRunIdAsync(_runId))
                .ReturnsAsync(new global::Models.SyncJobHistory.SyncJobHistory
                {
                    SyncJobId = _syncJobId,
                    RunId = _runId,
                    Status = "Idle",
                    UpdatedAt = DateTime.UtcNow,
                    UsersAdded = 2,
                    UsersRemoved = 0,
                });

            _mockGraphGroupRepository
                .Setup(x => x.GetGroupNamesAsync(It.IsAny<List<Guid>>()))
                .ReturnsAsync(new Dictionary<Guid, string> { { groupGuid, "Engineering FTEs" } });

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            _mockOpenAIService.Verify(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
            _mockGraphGroupRepository.Verify(x => x.GetGroupNamesAsync(It.IsAny<List<Guid>>()), Times.AtLeastOnce);
        }

        [TestMethod]
        public async Task ExecuteAsync_FullPipeline_WithManagerScope_ResolvesManagerNames()
        {
            _mockSyncJobRepository.Setup(x => x.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(new SyncJob
                {
                    Id = _syncJobId,
                    TargetOfficeGroupId = _targetGroupId,
                    Query = "[{\"type\":\"SqlMembership\",\"source\":{\"manager\":{\"id\":42},\"filter\":\"EmployeeType = 'FTE'\"}}]"
                });

            _mockSyncJobHistoryRepository.Setup(x => x.GetByRunIdAsync(_runId))
                .ReturnsAsync(new global::Models.SyncJobHistory.SyncJobHistory
                {
                    SyncJobId = _syncJobId,
                    RunId = _runId,
                    Status = "Idle",
                    UpdatedAt = DateTime.UtcNow,
                    UsersAdded = 3,
                    UsersRemoved = 0,
                    AdfRunId = Guid.NewGuid()
                });

            _mockSqlMembershipRepository
                .Setup(x => x.CheckIfTableExistsAsync(It.IsAny<string>()))
                .ReturnsAsync(true);

            _mockSqlMembershipRepository
                .Setup(x => x.GetOrgLeaderAsync(42, It.IsAny<string>()))
                .ReturnsAsync((0, "00000000-0000-0000-0000-000000000042"));

            _mockGraphGroupRepository
                .Setup(x => x.GetUserByUpnOrIdAsync(It.IsAny<string>(), It.IsAny<bool>()))
                .ReturnsAsync(new AzureADUser { ObjectId = Guid.Parse("00000000-0000-0000-0000-000000000042"), DisplayName = "Alice Smith" });

            _mockAdfRunHistoryRepository
                .Setup(x => x.GetLatestAdfRunAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AdfRunHistory { AdfRunId = Guid.NewGuid() });

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            _mockOpenAIService.Verify(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        [TestMethod]
        public async Task ExecuteAsync_FullPipeline_WithItoEventInWindow_CallsOpenAI()
        {
            _mockSyncJobHistoryRepository.Setup(x => x.GetByRunIdAsync(_runId))
                .ReturnsAsync(new global::Models.SyncJobHistory.SyncJobHistory
                {
                    SyncJobId = _syncJobId,
                    RunId = _runId,
                    Status = "Idle",
                    UpdatedAt = DateTime.UtcNow,
                    UsersAdded = 9,
                    UsersRemoved = 0,
                });

            _mockSyncJobChangeRepository
                .Setup(x => x.GetRecentIgnoreThresholdOnceEventsAsync(_syncJobId, It.IsAny<DateTime>(), It.IsAny<int>()))
                .ReturnsAsync(new List<SyncJobChange>
                {
                    new()
                    {
                        Id = Guid.NewGuid(),
                        SyncJobId = _syncJobId,
                        ChangeReason = SyncJobChangeReason.IgnoreThresholdOnce.ToString(),
                        ChangeTime = DateTime.UtcNow.AddMinutes(-15),
                        ChangedByDisplayName = "owner@contoso.com"
                    }
                });

            _mockSyncJobHistoryRepository.Setup(x => x.GetBySyncJobIdAsync(_syncJobId, It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync(new List<global::Models.SyncJobHistory.SyncJobHistory>
                {
                    new()
                    {
                        RunId = Guid.NewGuid(),
                        UpdatedAt = DateTime.UtcNow.AddHours(-1),
                        Status = "ThresholdExceeded",
                    }
                });

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            _mockOpenAIService.Verify(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        [TestMethod]
        public async Task ExecuteAsync_FullPipeline_WithHrCrossSnapshotDiff_ExercisesHrDiff()
        {
            var currentAdfRunId = Guid.NewGuid();
            var previousAdfRunId = Guid.NewGuid();

            _mockSyncJobHistoryRepository.Setup(x => x.GetByRunIdAsync(_runId))
                .ReturnsAsync(new global::Models.SyncJobHistory.SyncJobHistory
                {
                    SyncJobId = _syncJobId,
                    RunId = _runId,
                    Status = "Idle",
                    UpdatedAt = DateTime.UtcNow,
                    UsersAdded = 2,
                    UsersRemoved = 1,
                    AdfRunId = currentAdfRunId
                });

            _mockSyncJobHistoryRepository.Setup(x => x.GetBySyncJobIdAsync(_syncJobId, It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync(new List<global::Models.SyncJobHistory.SyncJobHistory>
                {
                    new()
                    {
                        RunId = Guid.NewGuid(),
                        UpdatedAt = DateTime.UtcNow.AddHours(-1),
                        AdfRunId = previousAdfRunId,
                    }
                });

            _mockBlobStorageRepository.Setup(x => x.FindAggregatedFileByRunIdAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new BlobResult { BlobStatus = BlobStatus.Found, Path = "test/path.json" });
            var fakeJson = $"{{\"SourceMembers\":[" +
                $"{{\"ObjectId\":\"{Guid.NewGuid()}\",\"MembershipAction\":1}}," +
                $"{{\"ObjectId\":\"{Guid.NewGuid()}\",\"MembershipAction\":2}}]}}";
            _mockBlobStorageRepository.Setup(x => x.DownloadFileAsync(It.IsAny<string>()))
                .ReturnsAsync(new BlobResult { BlobStatus = BlobStatus.Found, Content = fakeJson });

            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(It.IsAny<string>()))
                .ReturnsAsync(true);
            _mockSqlMembershipRepository.Setup(x => x.GetUserAttributesBatchAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<string>()))
                .ReturnsAsync(new Dictionary<string, Dictionary<string, string>>());

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            _mockOpenAIService.Verify(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        [TestMethod]
        public async Task ExecuteAsync_RemovesAttribution_AgentsOnlyWithValidEmptyCurrentResult_AttributesRemoval()
        {
            var currentAdfRunId = Guid.NewGuid();
            var previousAdfRunId = Guid.NewGuid();
            var previousRunId = Guid.NewGuid();
            var currentTable = currentAdfRunId.ToString().Replace("-", string.Empty);
            var previousTable = previousAdfRunId.ToString().Replace("-", string.Empty);
            var removedAgent = Guid.NewGuid();
            string? capturedPrompt = null;

            _mockSyncJobRepository.Setup(x => x.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(new SyncJob
                {
                    Id = _syncJobId,
                    TargetOfficeGroupId = _targetGroupId,
                    Query = "[{\"type\":\"SqlMembership\",\"identityTypes\":[\"agents\"],\"source\":{\"filter\":\"agents.BlueprintId = 'approved'\"}}]"
                });
            SetupCurrentRunAndPreviousRun(currentAdfRunId, previousAdfRunId, previousRunId, usersRemoved: 1);
            SetupAggregatedRemovedUsers(removedAgent);
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(currentTable)).ReturnsAsync(true);
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(previousTable)).ReturnsAsync(true);
            SetupSqlEvaluation(
                "agents.BlueprintId = 'approved'",
                currentTable,
                IdentityTypeSet.Of(IdentityType.Agents));
            SetupSqlEvaluation(
                "agents.BlueprintId = 'approved'",
                previousTable,
                IdentityTypeSet.Of(IdentityType.Agents),
                agentObjectIds: new[] { removedAgent });
            _mockSqlMembershipRepository.Setup(x => x.GetUserAttributesBatchAsync(
                    It.IsAny<IEnumerable<string>>(),
                    currentTable))
                .ReturnsAsync(new Dictionary<string, Dictionary<string, string>>());
            _mockOpenAIService.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((_, prompt) => capturedPrompt = prompt)
                .ReturnsAsync("This sync completed successfully.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedPrompt);
            StringAssert.Contains(capturedPrompt!, "no longer match this rule");
            Assert.IsFalse(capturedPrompt!.Contains("no attributable source found"));
        }

        [TestMethod]
        public async Task ExecuteAsync_RemovesAttribution_UsersAndAgents_CombinesSelectedIdentities()
        {
            var currentAdfRunId = Guid.NewGuid();
            var previousAdfRunId = Guid.NewGuid();
            var previousRunId = Guid.NewGuid();
            var currentTable = currentAdfRunId.ToString().Replace("-", string.Empty);
            var previousTable = previousAdfRunId.ToString().Replace("-", string.Empty);
            var removedUser = Guid.NewGuid();
            var removedAgent = Guid.NewGuid();
            string? capturedPrompt = null;

            _mockSyncJobRepository.Setup(x => x.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(new SyncJob
                {
                    Id = _syncJobId,
                    TargetOfficeGroupId = _targetGroupId,
                    Query = "[{\"type\":\"SqlMembership\",\"identityTypes\":[\"users\",\"agents\"],\"source\":{\"filter\":\"Department = 'Engineering'\"}}]"
                });
            SetupCurrentRunAndPreviousRun(currentAdfRunId, previousAdfRunId, previousRunId, usersRemoved: 2);
            SetupAggregatedRemovedUsers(removedUser, removedAgent);
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(currentTable)).ReturnsAsync(true);
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(previousTable)).ReturnsAsync(true);
            SetupSqlEvaluation(
                "Department = 'Engineering'",
                currentTable,
                IdentityTypeSet.All);
            SetupSqlEvaluation(
                "Department = 'Engineering'",
                previousTable,
                IdentityTypeSet.All,
                userObjectIds: new[] { removedUser },
                agentObjectIds: new[] { removedAgent });
            _mockSqlMembershipRepository.Setup(x => x.GetUserAttributesBatchAsync(
                    It.IsAny<IEnumerable<string>>(),
                    currentTable))
                .ReturnsAsync(new Dictionary<string, Dictionary<string, string>>());
            _mockOpenAIService.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((_, prompt) => capturedPrompt = prompt)
                .ReturnsAsync("This sync completed successfully.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedPrompt);
            StringAssert.Contains(capturedPrompt!, "all of the removed users no longer match this rule");
            Assert.IsFalse(capturedPrompt!.Contains("no attributable source found"));
        }

        [TestMethod]
        public async Task ExecuteAsync_AddsAttribution_UsersAndAgentsSharingAnId_CountsThatIdOnce()
        {
            var currentAdfRunId = Guid.NewGuid();
            var previousAdfRunId = Guid.NewGuid();
            var previousRunId = Guid.NewGuid();
            var currentTable = currentAdfRunId.ToString().Replace("-", string.Empty);
            var sharedIdentity = Guid.NewGuid();
            var addedOutsideRule = Guid.NewGuid();
            string? capturedPrompt = null;

            _mockSyncJobRepository.Setup(x => x.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(new SyncJob
                {
                    Id = _syncJobId,
                    TargetOfficeGroupId = _targetGroupId,
                    Query = "[{\"type\":\"SqlMembership\",\"identityTypes\":[\"users\",\"agents\"],\"source\":{\"filter\":\"Department = 'Engineering'\"}}]"
                });
            SetupCurrentRunAndPreviousRun(currentAdfRunId, previousAdfRunId, previousRunId, usersRemoved: 0, usersAdded: 2);
            SetupAggregatedMembers(MembershipAction.Add, sharedIdentity, addedOutsideRule);
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(currentTable)).ReturnsAsync(true);
            SetupSqlEvaluation(
                "Department = 'Engineering'",
                currentTable,
                IdentityTypeSet.All,
                userObjectIds: new[] { sharedIdentity },
                agentObjectIds: new[] { sharedIdentity });
            _mockSqlMembershipRepository.Setup(x => x.GetUserAttributesBatchAsync(
                    It.IsAny<IEnumerable<string>>(),
                    currentTable))
                .ReturnsAsync(new Dictionary<string, Dictionary<string, string>>());
            _mockOpenAIService.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((_, prompt) => capturedPrompt = prompt)
                .ReturnsAsync("This sync completed successfully.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedPrompt);
            StringAssert.Contains(capturedPrompt!, "about half of the added users match this rule");
            Assert.IsFalse(capturedPrompt!.Contains("all of the added users match this rule"));
        }

        [TestMethod]
        public async Task ExecuteAsync_RemovesAttribution_MixedQualifiedFilter_PreservesCompleteFilter()
        {
            var currentAdfRunId = Guid.NewGuid();
            var previousAdfRunId = Guid.NewGuid();
            var previousRunId = Guid.NewGuid();
            var currentTable = currentAdfRunId.ToString().Replace("-", string.Empty);
            var previousTable = previousAdfRunId.ToString().Replace("-", string.Empty);
            var removedAgent = Guid.NewGuid();
            const string filter = "users.Department = 'Engineering' AND agents.BlueprintId = 'approved'";
            string? capturedPrompt = null;

            _mockSyncJobRepository.Setup(x => x.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(new SyncJob
                {
                    Id = _syncJobId,
                    TargetOfficeGroupId = _targetGroupId,
                    Query = $"[{{\"type\":\"SqlMembership\",\"identityTypes\":[\"users\",\"agents\"],\"source\":{{\"filter\":\"{filter}\"}}}}]"
                });
            SetupCurrentRunAndPreviousRun(currentAdfRunId, previousAdfRunId, previousRunId, usersRemoved: 1);
            SetupAggregatedRemovedUsers(removedAgent);
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(currentTable)).ReturnsAsync(true);
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(previousTable)).ReturnsAsync(true);
            SetupSqlEvaluation(
                filter,
                currentTable,
                IdentityTypeSet.All);
            SetupSqlEvaluation(
                filter,
                previousTable,
                IdentityTypeSet.All,
                agentObjectIds: new[] { removedAgent });
            _mockSqlMembershipRepository.Setup(x => x.GetUserAttributesBatchAsync(
                    It.IsAny<IEnumerable<string>>(),
                    currentTable))
                .ReturnsAsync(new Dictionary<string, Dictionary<string, string>>());
            _mockOpenAIService.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((_, prompt) => capturedPrompt = prompt)
                .ReturnsAsync("This sync completed successfully.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedPrompt);
            StringAssert.Contains(capturedPrompt!, "no longer match this rule");
            Assert.IsFalse(capturedPrompt!.Contains("no attributable source found"));
        }

        [TestMethod]
        public async Task ExecuteAsync_RemovesAttribution_RequiredAgentsInputUnavailable_SkipsSqlAttribution()
        {
            var currentAdfRunId = Guid.NewGuid();
            var previousAdfRunId = Guid.NewGuid();
            var previousRunId = Guid.NewGuid();
            var currentTable = currentAdfRunId.ToString().Replace("-", string.Empty);
            var previousTable = previousAdfRunId.ToString().Replace("-", string.Empty);
            var removedAgent = Guid.NewGuid();
            const string filter = "agents.BlueprintId = 'approved'";
            string? capturedPrompt = null;

            _mockSyncJobRepository.Setup(x => x.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(new SyncJob
                {
                    Id = _syncJobId,
                    TargetOfficeGroupId = _targetGroupId,
                    Query = "[{\"type\":\"SqlMembership\",\"identityTypes\":[\"agents\"],\"source\":{\"filter\":\"agents.BlueprintId = 'approved'\"}}]"
                });
            SetupCurrentRunAndPreviousRun(currentAdfRunId, previousAdfRunId, previousRunId, usersRemoved: 1);
            SetupAggregatedRemovedUsers(removedAgent);
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(currentTable)).ReturnsAsync(true);
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(previousTable)).ReturnsAsync(true);
            _mockSqlMembershipRepository.Setup(x => x.EvaluateMembershipAsync(
                    It.Is<SqlMembershipEvaluationRequest>(request =>
                        MatchesSqlEvaluationRequest(
                            request,
                            filter,
                            currentTable,
                            IdentityTypeSet.Of(IdentityType.Agents)))))
                .ThrowsAsync(new InvalidOperationException("Required agents input is unavailable."));
            SetupSqlEvaluation(
                filter,
                previousTable,
                IdentityTypeSet.Of(IdentityType.Agents),
                agentObjectIds: new[] { removedAgent });
            _mockSqlMembershipRepository.Setup(x => x.GetUserAttributesBatchAsync(
                    It.IsAny<IEnumerable<string>>(),
                    currentTable))
                .ReturnsAsync(new Dictionary<string, Dictionary<string, string>>());
            _mockOpenAIService.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((_, prompt) => capturedPrompt = prompt)
                .ReturnsAsync("This sync completed successfully.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedPrompt);
            StringAssert.Contains(capturedPrompt!, "no attributable source found");
            Assert.IsFalse(capturedPrompt!.Contains("no longer match this rule"));
        }

        [TestMethod]
        public async Task ExecuteAsync_RemovesAttribution_WithSqlAndGroupDiffs_AddsPromptSection()
        {
            var currentAdfRunId = Guid.NewGuid();
            var previousAdfRunId = Guid.NewGuid();
            var previousRunId = Guid.NewGuid();
            var currentTable = currentAdfRunId.ToString().Replace("-", string.Empty);
            var previousTable = previousAdfRunId.ToString().Replace("-", string.Empty);
            var sqlDropped = Guid.NewGuid();
            var sqlNewlyExcluded = Guid.NewGuid();
            var groupLeft = Guid.NewGuid();
            var groupNewlyExcluded = Guid.NewGuid();
            var inclusionaryGroup = Guid.NewGuid();
            var exclusionaryGroup = Guid.NewGuid();
            var managerObjectId = Guid.NewGuid();
            var query = $"[" +
                "{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Department = 'Engineering'\"}}," +
                "{\"type\":\"SqlMembership\",\"source\":{\"manager\":{\"id\":42,\"depth\":2},\"filter\":\"EmployeeType = 'Vendor'\"},\"exclusionary\":true}," +
                $"{{\"type\":\"GroupMembership\",\"source\":\"{inclusionaryGroup}\"}}," +
                $"{{\"type\":\"GroupMembership\",\"source\":\"{exclusionaryGroup}\",\"exclusionary\":true}}" +
                "]";
            string? capturedPrompt = null;

            _mockSyncJobRepository.Setup(x => x.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(new SyncJob
                {
                    Id = _syncJobId,
                    TargetOfficeGroupId = _targetGroupId,
                    Query = query
                });

            SetupCurrentRunAndPreviousRun(currentAdfRunId, previousAdfRunId, previousRunId, usersRemoved: 4);
            SetupAggregatedRemovedUsers(sqlDropped, sqlNewlyExcluded, groupLeft, groupNewlyExcluded);
            _mockSyncJobChangeRepository
                .Setup(repository =>
                    repository.GetRecentConfigChangesBySyncJobIdAsync(
                        _syncJobId,
                        It.IsAny<DateTime>(),
                        It.IsAny<int>()))
                .ReturnsAsync(new List<SyncJobChange>
                {
                    ChangeWithSerializedQuery(
                        DateTime.UtcNow.AddMinutes(-10),
                        query),
                    ChangeWithSerializedQuery(
                        DateTime.UtcNow.AddMinutes(-70),
                        query)
                });

            _mockBlobStorageRepository.Setup(x => x.FindPartFilesByRunIdAsync(_targetGroupId.ToString(), _runId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult>
                {
                    ["GroupMembership_3"] = new() { BlobStatus = BlobStatus.Found, Path = "current/group-inclusion.json" },
                    ["GroupMembership_4"] = new() { BlobStatus = BlobStatus.Found, Path = "current/group-exclusion.json" }
                });
            _mockBlobStorageRepository.Setup(x => x.FindPartFilesByRunIdAsync(_targetGroupId.ToString(), previousRunId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult>
                {
                    ["GroupMembership_3"] = new() { BlobStatus = BlobStatus.Found, Path = "previous/group-inclusion.json" },
                    ["GroupMembership_4"] = new() { BlobStatus = BlobStatus.Found, Path = "previous/group-exclusion.json" }
                });
            _mockBlobStorageRepository.Setup(x => x.ExtractGroupMembershipSourceMembersAsync("current/group-inclusion.json"))
                .ReturnsAsync(new HashSet<Guid>());
            _mockBlobStorageRepository.Setup(x => x.ExtractGroupMembershipSourceMembersAsync("previous/group-inclusion.json"))
                .ReturnsAsync(new HashSet<Guid> { groupLeft });
            _mockBlobStorageRepository.Setup(x => x.ExtractGroupMembershipSourceMembersAsync("current/group-exclusion.json"))
                .ReturnsAsync(new HashSet<Guid> { groupNewlyExcluded });
            _mockBlobStorageRepository.Setup(x => x.ExtractGroupMembershipSourceMembersAsync("previous/group-exclusion.json"))
                .ReturnsAsync(new HashSet<Guid>());

            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(currentTable)).ReturnsAsync(true);
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(previousTable)).ReturnsAsync(true);
            SetupSqlEvaluation(
                "Department = 'Engineering'",
                currentTable,
                IdentityTypeSet.UsersOnly);
            SetupSqlEvaluation(
                "Department = 'Engineering'",
                previousTable,
                IdentityTypeSet.UsersOnly,
                userObjectIds: new[] { sqlDropped });
            SetupSqlEvaluation(
                "EmployeeType = 'Vendor'",
                currentTable,
                IdentityTypeSet.UsersOnly,
                managerEmployeeId: 42,
                depth: 2,
                userObjectIds: new[] { sqlNewlyExcluded });
            SetupSqlEvaluation(
                "EmployeeType = 'Vendor'",
                previousTable,
                IdentityTypeSet.UsersOnly,
                managerEmployeeId: 42,
                depth: 2);
            _mockSqlMembershipRepository.Setup(x => x.GetUserAttributesBatchAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<string>()))
                .ReturnsAsync(new Dictionary<string, Dictionary<string, string>>());
            _mockSqlMembershipRepository.Setup(x => x.GetOrgLeaderAsync(42, currentTable))
                .ReturnsAsync((42, managerObjectId.ToString()));
            _mockGraphGroupRepository.Setup(x => x.GetUserByUpnOrIdAsync(managerObjectId.ToString(), false))
                .ReturnsAsync(new AzureADUser { ObjectId = managerObjectId, DisplayName = "Vendor Manager" });
            _mockGraphGroupRepository.Setup(x => x.GetGroupNamesAsync(It.IsAny<List<Guid>>()))
                .ReturnsAsync(new Dictionary<Guid, string>
                {
                    [inclusionaryGroup] = "Engineering Source",
                    [exclusionaryGroup] = "Excluded Source"
                });
            _mockOpenAIService.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((_, prompt) => capturedPrompt = prompt)
                .ReturnsAsync("This sync completed successfully.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            _mockOpenAIService.Verify(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
            Assert.IsNotNull(capturedPrompt);
            StringAssert.Contains(capturedPrompt!, "Per-part attribution for removed users");
            StringAssert.Contains(capturedPrompt!, "no longer match this rule");
            StringAssert.Contains(capturedPrompt!, "newly excluded by this rule");
            StringAssert.Contains(capturedPrompt!, "stopped contributing through this rule");
            StringAssert.Contains(capturedPrompt!, "now match this exclusionary rule");
        }

        [TestMethod]
        public async Task ExecuteAsync_RemovesAttribution_WhenPreviousAdfTableMissing_SkipsSqlAttribution()
        {
            var currentAdfRunId = Guid.NewGuid();
            var previousAdfRunId = Guid.NewGuid();
            var previousRunId = Guid.NewGuid();
            var currentTable = currentAdfRunId.ToString().Replace("-", string.Empty);
            var previousTable = previousAdfRunId.ToString().Replace("-", string.Empty);
            var removedUser = Guid.NewGuid();
            string? capturedPrompt = null;

            SetupCurrentRunAndPreviousRun(currentAdfRunId, previousAdfRunId, previousRunId, usersRemoved: 1);
            SetupAggregatedRemovedUsers(removedUser);
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(currentTable)).ReturnsAsync(true);
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(previousTable)).ReturnsAsync(false);
            _mockSqlMembershipRepository.Setup(x => x.GetUserAttributesBatchAsync(It.IsAny<IEnumerable<string>>(), currentTable))
                .ReturnsAsync(new Dictionary<string, Dictionary<string, string>>());
            _mockOpenAIService.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((_, prompt) => capturedPrompt = prompt)
                .ReturnsAsync("This sync completed successfully.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            _mockOpenAIService.Verify(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
            Assert.IsNotNull(capturedPrompt);
            // Section IS present but as a no-signal marker (no attributable source found) — enforces the anti-hallucination hard rule.
            StringAssert.Contains(capturedPrompt!, "Per-part attribution for removed users");
            StringAssert.Contains(capturedPrompt!, "no attributable source found");
            _mockSqlMembershipRepository.Verify(
                x => x.EvaluateMembershipAsync(It.IsAny<SqlMembershipEvaluationRequest>()),
                Times.Never);
        }

        [TestMethod]
        public async Task ExecuteAsync_RemovesWalkBack_WhenPreviousAdfTablePruned_AttributesFromPreviousSourceBlob()
        {
            // Reported removes hedge (class B): previous run's ADF snapshot table is pruned so the single-step SQL diff cannot run, but its source blob still exists and the walk-back reads it to attribute the removals.
            var currentAdfRunId = Guid.NewGuid();
            var previousAdfRunId = Guid.NewGuid();
            var previousRunId = Guid.NewGuid();
            var currentTable = currentAdfRunId.ToString().Replace("-", string.Empty);
            var previousTable = previousAdfRunId.ToString().Replace("-", string.Empty);
            var removedUser = Guid.NewGuid();
            string? capturedPrompt = null;

            SetupCurrentRunAndPreviousRun(currentAdfRunId, previousAdfRunId, previousRunId, usersRemoved: 1);
            SetupAggregatedRemovedUsers(removedUser);
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(currentTable)).ReturnsAsync(true);
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(previousTable)).ReturnsAsync(false); // pruned
            _mockSqlMembershipRepository.Setup(x => x.GetUserAttributesBatchAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<string>()))
                .ReturnsAsync(new Dictionary<string, Dictionary<string, string>>());

            // Previous run's SqlMembership_1 source blob still present (~30d retention) and contains the removed user.
            _mockBlobStorageRepository.Setup(x => x.FindPartFilesByRunIdAsync(_targetGroupId.ToString(), previousRunId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult>
                {
                    ["SqlMembership_1"] = new() { BlobStatus = BlobStatus.Found, Path = "prev/sql1.json" }
                });
            _mockBlobStorageRepository.Setup(x => x.ExtractGroupMembershipSourceMembersAsync("prev/sql1.json"))
                .ReturnsAsync(new HashSet<Guid> { removedUser });

            _mockOpenAIService.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((_, prompt) => capturedPrompt = prompt)
                .ReturnsAsync("This sync completed successfully.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedPrompt);
            StringAssert.Contains(capturedPrompt!, "Per-part attribution for removed users");
            StringAssert.Contains(capturedPrompt!, "no longer match this rule");
            StringAssert.Contains(capturedPrompt!, "per prior-run source history");
            Assert.IsFalse(capturedPrompt!.Contains("no attributable source found"),
                "Walk-back should have replaced the no-signal hedge marker with a real attribution.");
            // The walk-back reads blobs — it must NOT re-query the (pruned) ADF tables.
            _mockSqlMembershipRepository.Verify(
                x => x.EvaluateMembershipAsync(It.IsAny<SqlMembershipEvaluationRequest>()),
                Times.Never);
        }

        [TestMethod]
        public async Task ExecuteAsync_RemovesWalkBack_MultiRunBlip_AttributesFromOlderRunGroupBlob()
        {
            // Reported removes hedge (class D, multi-run blip): the user is absent from the immediately-previous run's source but was present two runs back, so the walk-back keeps going and attributes to the older run.
            var previousRunId = Guid.NewGuid();
            var olderRunId = Guid.NewGuid();
            var inclusionaryGroup = Guid.NewGuid();
            var removedUser = Guid.NewGuid();
            var now = DateTime.UtcNow;
            string? capturedPrompt = null;

            _mockSyncJobRepository.Setup(x => x.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(new SyncJob
                {
                    Id = _syncJobId,
                    TargetOfficeGroupId = _targetGroupId,
                    Query = $"[{{\"type\":\"GroupMembership\",\"source\":\"{inclusionaryGroup}\"}}]"
                });

            SetupCurrentRunAndPreviousRun(Guid.NewGuid(), Guid.NewGuid(), previousRunId, usersRemoved: 1);
            SetupAggregatedRemovedUsers(removedUser);

            // Override history so the walk-back sees TWO prior runs (previous at -1h, older at -2h).
            _mockSyncJobHistoryRepository.Setup(x => x.GetBySyncJobIdAsync(_syncJobId, It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync(new List<global::Models.SyncJobHistory.SyncJobHistory>
                {
                    new() { SyncJobId = _syncJobId, RunId = previousRunId, Status = "Idle", UpdatedAt = now.AddHours(-1), UsersRemoved = 0 },
                    new() { SyncJobId = _syncJobId, RunId = olderRunId, Status = "Idle", UpdatedAt = now.AddHours(-2), UsersRemoved = 0 }
                });

            // Current + previous source blobs do NOT contain the user -> single-step finds nothing -> no-signal marker.
            _mockBlobStorageRepository.Setup(x => x.FindPartFilesByRunIdAsync(_targetGroupId.ToString(), _runId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult> { ["GroupMembership_1"] = new() { BlobStatus = BlobStatus.Found, Path = "cur/g1.json" } });
            _mockBlobStorageRepository.Setup(x => x.FindPartFilesByRunIdAsync(_targetGroupId.ToString(), previousRunId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult> { ["GroupMembership_1"] = new() { BlobStatus = BlobStatus.Found, Path = "prev/g1.json" } });
            _mockBlobStorageRepository.Setup(x => x.FindPartFilesByRunIdAsync(_targetGroupId.ToString(), olderRunId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult> { ["GroupMembership_1"] = new() { BlobStatus = BlobStatus.Found, Path = "old/g1.json" } });
            _mockBlobStorageRepository.Setup(x => x.ExtractGroupMembershipSourceMembersAsync("cur/g1.json")).ReturnsAsync(new HashSet<Guid>());
            _mockBlobStorageRepository.Setup(x => x.ExtractGroupMembershipSourceMembersAsync("prev/g1.json")).ReturnsAsync(new HashSet<Guid>());
            _mockBlobStorageRepository.Setup(x => x.ExtractGroupMembershipSourceMembersAsync("old/g1.json")).ReturnsAsync(new HashSet<Guid> { removedUser });

            _mockGraphGroupRepository.Setup(x => x.GetGroupNamesAsync(It.IsAny<List<Guid>>()))
                .ReturnsAsync(new Dictionary<Guid, string> { [inclusionaryGroup] = "Engineering Source" });
            _mockOpenAIService.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((_, prompt) => capturedPrompt = prompt)
                .ReturnsAsync("This sync completed successfully.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedPrompt);
            StringAssert.Contains(capturedPrompt!, "Per-part attribution for removed users");
            StringAssert.Contains(capturedPrompt!, "stopped contributing through this rule");
            StringAssert.Contains(capturedPrompt!, "per prior-run source history");
            Assert.IsFalse(capturedPrompt!.Contains("no attributable source found"),
                "Walk-back should have resolved the blip from the older run's source blob.");
        }

        [TestMethod]
        public async Task ExecuteAsync_RemovesWalkBack_SuppressedWhenConfigChangeInWindow()
        {
            // Guard: a query change in the window suppresses the walk-back (part-index/blob-tag mapping may no longer hold), so config-driven removals stay with the config-diff passes instead.
            var currentAdfRunId = Guid.NewGuid();
            var previousAdfRunId = Guid.NewGuid();
            var previousRunId = Guid.NewGuid();
            var currentTable = currentAdfRunId.ToString().Replace("-", string.Empty);
            var previousTable = previousAdfRunId.ToString().Replace("-", string.Empty);
            var removedUser = Guid.NewGuid();
            string? capturedPrompt = null;

            SetupCurrentRunAndPreviousRun(currentAdfRunId, previousAdfRunId, previousRunId, usersRemoved: 1);
            SetupAggregatedRemovedUsers(removedUser);
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(currentTable)).ReturnsAsync(true);
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(previousTable)).ReturnsAsync(false); // single-step yields no-signal marker
            _mockSqlMembershipRepository.Setup(x => x.GetUserAttributesBatchAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<string>()))
                .ReturnsAsync(new Dictionary<string, Dictionary<string, string>>());

            // A config change AFTER the previous run puts us inside a config-change window -> walk-back must not run.
            _mockSyncJobChangeRepository.Setup(x => x.GetRecentConfigChangesBySyncJobIdAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<int>()))
                .ReturnsAsync(new List<SyncJobChange> { new() { SyncJobId = _syncJobId, ChangeTime = DateTime.UtcNow, ChangeDetails = null } });

            // The previous SQL blob WOULD attribute the user if the walk-back ran — it must be ignored while suppressed.
            _mockBlobStorageRepository.Setup(x => x.FindPartFilesByRunIdAsync(_targetGroupId.ToString(), previousRunId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult> { ["SqlMembership_1"] = new() { BlobStatus = BlobStatus.Found, Path = "prev/sql1.json" } });
            _mockBlobStorageRepository.Setup(x => x.ExtractGroupMembershipSourceMembersAsync("prev/sql1.json"))
                .ReturnsAsync(new HashSet<Guid> { removedUser });

            _mockOpenAIService.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((_, prompt) => capturedPrompt = prompt)
                .ReturnsAsync("This sync completed successfully.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedPrompt);
            StringAssert.Contains(capturedPrompt!, "no attributable source found");
            Assert.IsFalse(capturedPrompt!.Contains("per prior-run source history"),
                "Walk-back must be suppressed while a config change is in the window.");
        }

        [TestMethod]
        public async Task ExecuteAsync_RemovesWalkBack_WhenPreviousAdfRunIdNull_AttributesFromPreviousSourceBlob()
        {
            // Reported hedge where the CURRENT ADF table exists yet the cause could not be determined: the PREVIOUS run has no AdfRunId so the single-step SQL diff cannot run, but the RunId-keyed source blob still lets the walk-back attribute the removals.
            var currentAdfRunId = Guid.NewGuid();
            var previousRunId = Guid.NewGuid();
            var currentTable = currentAdfRunId.ToString().Replace("-", string.Empty);
            var removedUser = Guid.NewGuid();
            var now = DateTime.UtcNow;
            string? capturedPrompt = null;

            SetupCurrentRunAndPreviousRun(currentAdfRunId, Guid.NewGuid(), previousRunId, usersRemoved: 1);
            SetupAggregatedRemovedUsers(removedUser);

            // Override the previous-run row so its AdfRunId is NULL (the actual root cause of the single-step hedge).
            _mockSyncJobHistoryRepository.Setup(x => x.GetBySyncJobIdAsync(_syncJobId, It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync(new List<global::Models.SyncJobHistory.SyncJobHistory>
                {
                    new() { SyncJobId = _syncJobId, RunId = previousRunId, Status = "Idle", UpdatedAt = now.AddHours(-1), UsersRemoved = 0, AdfRunId = null }
                });

            // The current ADF table exists, proving the hedge was not caused by the current table being pruned.
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(currentTable)).ReturnsAsync(true);
            _mockSqlMembershipRepository.Setup(x => x.GetUserAttributesBatchAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<string>()))
                .ReturnsAsync(new Dictionary<string, Dictionary<string, string>>());

            // The previous run's SqlMembership_1 source blob is still present and contains the removed user.
            _mockBlobStorageRepository.Setup(x => x.FindPartFilesByRunIdAsync(_targetGroupId.ToString(), previousRunId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult> { ["SqlMembership_1"] = new() { BlobStatus = BlobStatus.Found, Path = "prev/sql1.json" } });
            _mockBlobStorageRepository.Setup(x => x.ExtractGroupMembershipSourceMembersAsync("prev/sql1.json"))
                .ReturnsAsync(new HashSet<Guid> { removedUser });

            _mockOpenAIService.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((_, prompt) => capturedPrompt = prompt)
                .ReturnsAsync("This sync completed successfully.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedPrompt);
            StringAssert.Contains(capturedPrompt!, "Per-part attribution for removed users");
            StringAssert.Contains(capturedPrompt!, "no longer match this rule");
            StringAssert.Contains(capturedPrompt!, "per prior-run source history");
            Assert.IsFalse(capturedPrompt!.Contains("no attributable source found"),
                "Walk-back should attribute despite a null previous AdfRunId, since blobs are keyed by RunId not by ADF table.");
            _mockSqlMembershipRepository.Verify(
                x => x.EvaluateMembershipAsync(It.IsAny<SqlMembershipEvaluationRequest>()),
                Times.Never);
        }

        [TestMethod]
        public async Task ExecuteAsync_RemovesWalkBack_ThresholdBlockedProposedRemoves_AttributesFromPreviousSourceBlob()
        {
            // Reported threshold-blocked run whose proposed removals could not be determined: nothing was applied, but the walk-back still reads the previous source blob and attributes the proposed removals so the owner sees why.
            var currentAdfRunId = Guid.NewGuid();
            var previousAdfRunId = Guid.NewGuid();
            var previousRunId = Guid.NewGuid();
            var currentTable = currentAdfRunId.ToString().Replace("-", string.Empty);
            var previousTable = previousAdfRunId.ToString().Replace("-", string.Empty);
            var removedUser = Guid.NewGuid();
            var now = DateTime.UtcNow;
            string? capturedPrompt = null;

            SetupCurrentRunAndPreviousRun(currentAdfRunId, previousAdfRunId, previousRunId, usersRemoved: 1);
            SetupAggregatedRemovedUsers(removedUser);

            // Mark THIS run threshold-blocked (Status = ThresholdExceeded) so it takes the blocked path.
            _mockSyncJobHistoryRepository.Setup(x => x.GetByRunIdAsync(_runId))
                .ReturnsAsync(new global::Models.SyncJobHistory.SyncJobHistory
                {
                    SyncJobId = _syncJobId,
                    RunId = _runId,
                    Status = "ThresholdExceeded",
                    UpdatedAt = now,
                    StartTime = now.AddMinutes(-5),
                    EndTime = now,
                    UsersAdded = 0,
                    UsersRemoved = 1,
                    BeforeSyncUserCount = 100,
                    AfterSyncUserCount = 100,
                    AdfRunId = currentAdfRunId
                });

            // Previous ADF table pruned so the single-step SQL diff hedges, forcing the durable walk-back path.
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(currentTable)).ReturnsAsync(true);
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(previousTable)).ReturnsAsync(false);
            _mockSqlMembershipRepository.Setup(x => x.GetUserAttributesBatchAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<string>()))
                .ReturnsAsync(new Dictionary<string, Dictionary<string, string>>());

            // The previous run's SqlMembership_1 source blob still holds the proposed-removed user.
            _mockBlobStorageRepository.Setup(x => x.FindPartFilesByRunIdAsync(_targetGroupId.ToString(), previousRunId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult> { ["SqlMembership_1"] = new() { BlobStatus = BlobStatus.Found, Path = "prev/sql1.json" } });
            _mockBlobStorageRepository.Setup(x => x.ExtractGroupMembershipSourceMembersAsync("prev/sql1.json"))
                .ReturnsAsync(new HashSet<Guid> { removedUser });

            _mockOpenAIService.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((_, prompt) => capturedPrompt = prompt)
                .ReturnsAsync("This sync proposed changes that were blocked because the change exceeded the configured threshold.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedPrompt);
            StringAssert.Contains(capturedPrompt!, "Per-part attribution for removed users");
            StringAssert.Contains(capturedPrompt!, "no longer match this rule");
            StringAssert.Contains(capturedPrompt!, "per prior-run source history");
            Assert.IsFalse(capturedPrompt!.Contains("no attributable source found"),
                "Walk-back should attribute the proposed removals on a threshold-blocked run.");
        }

        [TestMethod]
        public async Task ExecuteAsync_RemovesWalkBack_ManagerChainSqlPart_AttributesFromPreviousSourceBlob()
        {
            // Management-chain query shape (manager root plus an HR filter) from the reported hedges: previous ADF table pruned, and the walk-back reads the previous run's source blob to attribute the removed manager-chain members.
            var currentAdfRunId = Guid.NewGuid();
            var previousAdfRunId = Guid.NewGuid();
            var previousRunId = Guid.NewGuid();
            var currentTable = currentAdfRunId.ToString().Replace("-", string.Empty);
            var previousTable = previousAdfRunId.ToString().Replace("-", string.Empty);
            var removedUser = Guid.NewGuid();
            string? capturedPrompt = null;

            // Synthetic manager-rooted SQL query (no real ids): management chain under id 4242 filtered to EmployeeType FTE.
            _mockSyncJobRepository.Setup(x => x.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(new SyncJob
                {
                    Id = _syncJobId,
                    TargetOfficeGroupId = _targetGroupId,
                    Query = "[{\"type\":\"SqlMembership\",\"source\":{\"manager\":{\"id\":4242},\"filter\":\"EmployeeType = 'FTE'\"}}]"
                });

            SetupCurrentRunAndPreviousRun(currentAdfRunId, previousAdfRunId, previousRunId, usersRemoved: 1);
            SetupAggregatedRemovedUsers(removedUser);

            // Previous ADF table pruned so the single-step SQL diff hedges, forcing the walk-back path.
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(currentTable)).ReturnsAsync(true);
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(previousTable)).ReturnsAsync(false);
            _mockSqlMembershipRepository.Setup(x => x.GetUserAttributesBatchAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<string>()))
                .ReturnsAsync(new Dictionary<string, Dictionary<string, string>>());

            // The previous run's SqlMembership_1 source blob still contains the removed manager-chain member.
            _mockBlobStorageRepository.Setup(x => x.FindPartFilesByRunIdAsync(_targetGroupId.ToString(), previousRunId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult> { ["SqlMembership_1"] = new() { BlobStatus = BlobStatus.Found, Path = "prev/sql1.json" } });
            _mockBlobStorageRepository.Setup(x => x.ExtractGroupMembershipSourceMembersAsync("prev/sql1.json"))
                .ReturnsAsync(new HashSet<Guid> { removedUser });

            _mockOpenAIService.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((_, prompt) => capturedPrompt = prompt)
                .ReturnsAsync("This sync completed successfully.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedPrompt);
            StringAssert.Contains(capturedPrompt!, "Per-part attribution for removed users");
            StringAssert.Contains(capturedPrompt!, "no longer match this rule");
            StringAssert.Contains(capturedPrompt!, "per prior-run source history");
            Assert.IsFalse(capturedPrompt!.Contains("no attributable source found"),
                "Walk-back should attribute the removed manager-chain members from the previous run's source blob.");
        }

        [TestMethod]
        public async Task ExecuteAsync_RemovesCurrentState_WhenExactSnapshotGoneAndUserAbsentFromLatestRule_EmitsCurrentStateColor()
        {
            // Phase 3: exact-run ADF snapshot is gone so removal-time attribution hedges; the removed user is absent from the inclusionary SQL rule in the LATEST table, so we add present-tense current-state color.
            var currentAdfRunId = Guid.NewGuid();
            var previousAdfRunId = Guid.NewGuid();
            var previousRunId = Guid.NewGuid();
            var currentTable = currentAdfRunId.ToString().Replace("-", string.Empty);
            var previousTable = previousAdfRunId.ToString().Replace("-", string.Empty);
            var latestRunId = Guid.NewGuid();
            var latestTable = latestRunId.ToString().Replace("-", string.Empty);
            var removedUser = Guid.NewGuid();
            string? capturedPrompt = null;

            SetupCurrentRunAndPreviousRun(currentAdfRunId, previousAdfRunId, previousRunId, usersRemoved: 1);
            SetupAggregatedRemovedUsers(removedUser);
            // Exact-run and previous ADF tables pruned so single-step and walk-back hedge, making Phase 3 eligible.
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(currentTable)).ReturnsAsync(false);
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(previousTable)).ReturnsAsync(false);
            // Latest ADF table is available and the removed user does NOT match the rule today.
            _mockAdfRunHistoryRepository.Setup(x => x.GetLatestAdfRunAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(new AdfRunHistory { AdfRunId = latestRunId });
            SetupSqlEvaluation(
                "Building = 'B40'",
                latestTable,
                IdentityTypeSet.UsersOnly,
                userObjectIds: new[] { Guid.NewGuid() });
            _mockOpenAIService.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((_, prompt) => capturedPrompt = prompt)
                .ReturnsAsync("This sync completed successfully.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedPrompt);
            StringAssert.Contains(capturedPrompt!, "Per-part attribution for removed users");
            StringAssert.Contains(capturedPrompt!, "no longer match this rule in the latest HR data (current state, not the removal-time snapshot)");
            Assert.IsFalse(capturedPrompt!.Contains("no attributable source found"),
                "Phase 3 current-state color should replace the no-signal hedge marker.");
        }

        [TestMethod]
        public async Task ExecuteAsync_RemovesCurrentState_WhenRemovedUserStillMatchesLatestRule_SuppressesColorAndKeepsHedge()
        {
            // Phase 3 contradiction guard (Scenario 5): the removed user STILL matches the inclusionary rule in the latest table, so we must NOT claim they no longer match and the hedge marker is preserved.
            var currentAdfRunId = Guid.NewGuid();
            var previousAdfRunId = Guid.NewGuid();
            var previousRunId = Guid.NewGuid();
            var currentTable = currentAdfRunId.ToString().Replace("-", string.Empty);
            var previousTable = previousAdfRunId.ToString().Replace("-", string.Empty);
            var latestRunId = Guid.NewGuid();
            var latestTable = latestRunId.ToString().Replace("-", string.Empty);
            var removedUser = Guid.NewGuid();
            string? capturedPrompt = null;

            SetupCurrentRunAndPreviousRun(currentAdfRunId, previousAdfRunId, previousRunId, usersRemoved: 1);
            SetupAggregatedRemovedUsers(removedUser);
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(currentTable)).ReturnsAsync(false);
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(previousTable)).ReturnsAsync(false);
            _mockAdfRunHistoryRepository.Setup(x => x.GetLatestAdfRunAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(new AdfRunHistory { AdfRunId = latestRunId });
            // The removed user is still returned by the rule against the latest table, so the guard fires.
            SetupSqlEvaluation(
                "Building = 'B40'",
                latestTable,
                IdentityTypeSet.UsersOnly,
                userObjectIds: new[] { removedUser });
            _mockOpenAIService.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((_, prompt) => capturedPrompt = prompt)
                .ReturnsAsync("This sync completed successfully.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedPrompt);
            Assert.IsFalse(capturedPrompt!.Contains("in the latest HR data (current state, not the removal-time snapshot)"),
                "Contradiction guard must suppress current-state color when the removed user still matches the latest rule.");
            StringAssert.Contains(capturedPrompt!, "no attributable source found");
        }

        [TestMethod]
        public async Task ExecuteAsync_RemovesCurrentState_ManagerChainPart_EmitsCurrentStateColorFromLatestTable()
        {
            // Phase 3 evaluates the whole manager-rooted part against the latest table, so a user no longer in the chain is colored even if they still match the filter attribute.
            var currentAdfRunId = Guid.NewGuid();
            var previousAdfRunId = Guid.NewGuid();
            var previousRunId = Guid.NewGuid();
            var currentTable = currentAdfRunId.ToString().Replace("-", string.Empty);
            var previousTable = previousAdfRunId.ToString().Replace("-", string.Empty);
            var latestRunId = Guid.NewGuid();
            var latestTable = latestRunId.ToString().Replace("-", string.Empty);
            var removedUser = Guid.NewGuid();
            string? capturedPrompt = null;

            // Synthetic manager-rooted SQL query (no real ids): management chain under id 42 filtered to EmployeeType FTE.
            _mockSyncJobRepository.Setup(x => x.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(new SyncJob
                {
                    Id = _syncJobId,
                    TargetOfficeGroupId = _targetGroupId,
                    Query = "[{\"type\":\"SqlMembership\",\"source\":{\"manager\":{\"id\":42,\"depth\":2},\"filter\":\"EmployeeType = 'FTE'\"}}]"
                });
            SetupCurrentRunAndPreviousRun(currentAdfRunId, previousAdfRunId, previousRunId, usersRemoved: 1);
            SetupAggregatedRemovedUsers(removedUser);
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(currentTable)).ReturnsAsync(false);
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(previousTable)).ReturnsAsync(false);
            _mockAdfRunHistoryRepository.Setup(x => x.GetLatestAdfRunAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(new AdfRunHistory { AdfRunId = latestRunId });
            // The removed user is no longer in the management chain under 42 in the latest table.
            SetupSqlEvaluation(
                "EmployeeType = 'FTE'",
                latestTable,
                IdentityTypeSet.UsersOnly,
                managerEmployeeId: 42,
                depth: 2,
                userObjectIds: new[] { Guid.NewGuid() });
            _mockOpenAIService.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((_, prompt) => capturedPrompt = prompt)
                .ReturnsAsync("This sync completed successfully.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedPrompt);
            StringAssert.Contains(capturedPrompt!, "no longer match this rule in the latest HR data (current state, not the removal-time snapshot)");
        }

        [TestMethod]
        public async Task ExecuteAsync_RemovesCurrentState_WhenExactSnapshotStillExists_DoesNotAddCurrentStateColor()
        {
            // Phase 3 is skipped when the exact-run snapshot still exists: that snapshot is the removal-time truth and the single-step diff owns the reason, so no current-state color is added.
            var currentAdfRunId = Guid.NewGuid();
            var previousAdfRunId = Guid.NewGuid();
            var previousRunId = Guid.NewGuid();
            var currentTable = currentAdfRunId.ToString().Replace("-", string.Empty);
            var previousTable = previousAdfRunId.ToString().Replace("-", string.Empty);
            var latestRunId = Guid.NewGuid();
            var latestTable = latestRunId.ToString().Replace("-", string.Empty);
            var removedUser = Guid.NewGuid();
            string? capturedPrompt = null;

            SetupCurrentRunAndPreviousRun(currentAdfRunId, previousAdfRunId, previousRunId, usersRemoved: 1);
            SetupAggregatedRemovedUsers(removedUser);
            // Both exact-run and previous snapshots present so the single-step diff attributes at removal time.
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(currentTable)).ReturnsAsync(true);
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(previousTable)).ReturnsAsync(true);
            SetupSqlEvaluation(
                "Building = 'B40'",
                currentTable,
                IdentityTypeSet.UsersOnly);
            SetupSqlEvaluation(
                "Building = 'B40'",
                previousTable,
                IdentityTypeSet.UsersOnly,
                userObjectIds: new[] { removedUser });
            _mockAdfRunHistoryRepository.Setup(x => x.GetLatestAdfRunAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(new AdfRunHistory { AdfRunId = latestRunId });
            // If Phase 3 wrongly ran, this latest-table match would let it emit color; the guard must prevent that.
            SetupSqlEvaluation(
                "Building = 'B40'",
                latestTable,
                IdentityTypeSet.UsersOnly,
                userObjectIds: new[] { Guid.NewGuid() });
            _mockOpenAIService.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((_, prompt) => capturedPrompt = prompt)
                .ReturnsAsync("This sync completed successfully.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedPrompt);
            StringAssert.Contains(capturedPrompt!, "no longer match this rule");
            Assert.IsFalse(capturedPrompt!.Contains("in the latest HR data (current state, not the removal-time snapshot)"),
                "Phase 3 must be skipped when the exact-run snapshot still exists.");
        }

        [TestMethod]
        public async Task ExecuteAsync_RemovesCurrentState_WhenNoLatestAdfTable_SkipsColorAndKeepsHedge()
        {
            // Phase 3 is skipped when there is no latest ADF run id to evaluate against, so the hedge marker is preserved and no latest-table read is attempted.
            var currentAdfRunId = Guid.NewGuid();
            var previousAdfRunId = Guid.NewGuid();
            var previousRunId = Guid.NewGuid();
            var currentTable = currentAdfRunId.ToString().Replace("-", string.Empty);
            var previousTable = previousAdfRunId.ToString().Replace("-", string.Empty);
            var removedUser = Guid.NewGuid();
            string? capturedPrompt = null;

            SetupCurrentRunAndPreviousRun(currentAdfRunId, previousAdfRunId, previousRunId, usersRemoved: 1);
            SetupAggregatedRemovedUsers(removedUser);
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(currentTable)).ReturnsAsync(false);
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(previousTable)).ReturnsAsync(false);
            // No latest ADF run id available so Phase 3 bails before any latest-table read.
            _mockAdfRunHistoryRepository.Setup(x => x.GetLatestAdfRunAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync((AdfRunHistory?)null);
            _mockOpenAIService.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((_, prompt) => capturedPrompt = prompt)
                .ReturnsAsync("This sync completed successfully.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedPrompt);
            Assert.IsFalse(capturedPrompt!.Contains("in the latest HR data (current state, not the removal-time snapshot)"),
                "Phase 3 must be skipped when there is no latest ADF table.");
            StringAssert.Contains(capturedPrompt!, "no attributable source found");
            _mockSqlMembershipRepository.Verify(
                x => x.EvaluateMembershipAsync(It.IsAny<SqlMembershipEvaluationRequest>()),
                Times.Never);
        }

        // ---- Phase 3 confirms the residual removes hedges (exact snapshot gone AND walk-back blobs gone) are now explained by the latest-ADF current-state fallback; no real job/run/ADF ids or names are used ----

        [TestMethod]
        public async Task ExecuteAsync_RemovesCurrentState_ThresholdBlocked_ExactSnapshotGoneAndBlobsGone_LatestAdfFallbackExplainsHedge()
        {
            // Mirrors the reported threshold-blocked run whose proposed removals "could not be determined": here the exact-run snapshot is pruned AND the prior source blobs are gone, so only the latest-ADF current-state fallback can explain the proposed removals.
            var currentAdfRunId = Guid.NewGuid();
            var previousAdfRunId = Guid.NewGuid();
            var previousRunId = Guid.NewGuid();
            var currentTable = currentAdfRunId.ToString().Replace("-", string.Empty);
            var previousTable = previousAdfRunId.ToString().Replace("-", string.Empty);
            var latestRunId = Guid.NewGuid();
            var latestTable = latestRunId.ToString().Replace("-", string.Empty);
            var removedUser = Guid.NewGuid();
            var now = DateTime.UtcNow;
            string? capturedPrompt = null;

            SetupCurrentRunAndPreviousRun(currentAdfRunId, previousAdfRunId, previousRunId, usersRemoved: 1);
            SetupAggregatedRemovedUsers(removedUser);
            // Mark THIS run threshold-blocked so it takes the blocked path with proposed (not applied) removals.
            _mockSyncJobHistoryRepository.Setup(x => x.GetByRunIdAsync(_runId))
                .ReturnsAsync(new global::Models.SyncJobHistory.SyncJobHistory
                {
                    SyncJobId = _syncJobId,
                    RunId = _runId,
                    Status = "ThresholdExceeded",
                    UpdatedAt = now,
                    StartTime = now.AddMinutes(-5),
                    EndTime = now,
                    UsersAdded = 0,
                    UsersRemoved = 1,
                    BeforeSyncUserCount = 100,
                    AfterSyncUserCount = 100,
                    AdfRunId = currentAdfRunId
                });
            // Exact-run snapshot pruned (Phase 3 eligible) and the previous snapshot pruned too (single-step hedges).
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(currentTable)).ReturnsAsync(false);
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(previousTable)).ReturnsAsync(false);
            // Prior source blobs are gone so the walk-back also finds nothing.
            _mockBlobStorageRepository.Setup(x => x.FindPartFilesByRunIdAsync(_targetGroupId.ToString(), previousRunId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult>());
            // Latest ADF table is available and the proposed-removed user no longer matches the rule today.
            _mockAdfRunHistoryRepository.Setup(x => x.GetLatestAdfRunAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(new AdfRunHistory { AdfRunId = latestRunId });
            SetupSqlEvaluation(
                "Building = 'B40'",
                latestTable,
                IdentityTypeSet.UsersOnly,
                userObjectIds: new[] { Guid.NewGuid() });
            _mockOpenAIService.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((_, prompt) => capturedPrompt = prompt)
                .ReturnsAsync("This sync proposed changes that were blocked because the change exceeded the configured threshold.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedPrompt);
            StringAssert.Contains(capturedPrompt!, "no longer match this rule in the latest HR data (current state, not the removal-time snapshot)");
            Assert.IsFalse(capturedPrompt!.Contains("no attributable source found"),
                "The latest-ADF current-state fallback should replace the threshold-blocked no-signal hedge.");
        }

        [TestMethod]
        public async Task ExecuteAsync_RemovesCurrentState_NullCurrentAdfRunIdAndBlobsGone_LatestAdfFallbackExplainsHedge()
        {
            // Mirrors the reported hedge caused by a missing AdfRunId: this run has NO AdfRunId (no exact-run snapshot) and the prior source blobs are gone, so the latest-ADF current-state fallback is the only remaining signal.
            var previousAdfRunId = Guid.NewGuid();
            var previousRunId = Guid.NewGuid();
            var latestRunId = Guid.NewGuid();
            var latestTable = latestRunId.ToString().Replace("-", string.Empty);
            var removedUser = Guid.NewGuid();
            var now = DateTime.UtcNow;
            string? capturedPrompt = null;

            SetupCurrentRunAndPreviousRun(Guid.NewGuid(), previousAdfRunId, previousRunId, usersRemoved: 1);
            SetupAggregatedRemovedUsers(removedUser);
            // Override the current run so its AdfRunId is NULL (root cause of the single-step hedge).
            _mockSyncJobHistoryRepository.Setup(x => x.GetByRunIdAsync(_runId))
                .ReturnsAsync(new global::Models.SyncJobHistory.SyncJobHistory
                {
                    SyncJobId = _syncJobId,
                    RunId = _runId,
                    Status = "Idle",
                    UpdatedAt = now,
                    StartTime = now.AddMinutes(-5),
                    EndTime = now,
                    UsersAdded = 0,
                    UsersRemoved = 1,
                    BeforeSyncUserCount = 100,
                    AfterSyncUserCount = 99,
                    AdfRunId = null
                });
            // Prior source blobs are gone so the walk-back finds nothing.
            _mockBlobStorageRepository.Setup(x => x.FindPartFilesByRunIdAsync(_targetGroupId.ToString(), previousRunId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult>());
            // Latest ADF table is available and the removed user no longer matches the rule today.
            _mockAdfRunHistoryRepository.Setup(x => x.GetLatestAdfRunAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(new AdfRunHistory { AdfRunId = latestRunId });
            SetupSqlEvaluation(
                "Building = 'B40'",
                latestTable,
                IdentityTypeSet.UsersOnly,
                userObjectIds: new[] { Guid.NewGuid() });
            _mockOpenAIService.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((_, prompt) => capturedPrompt = prompt)
                .ReturnsAsync("This sync completed successfully.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedPrompt);
            StringAssert.Contains(capturedPrompt!, "no longer match this rule in the latest HR data (current state, not the removal-time snapshot)");
            Assert.IsFalse(capturedPrompt!.Contains("no attributable source found"),
                "The latest-ADF current-state fallback should explain removals even when the current run has a null AdfRunId.");
        }

        [TestMethod]
        public async Task ExecuteAsync_RemovesCurrentState_ManagerChainHedge_DoesNotOverClaimWhenUserStillMatchesLatestChain()
        {
            // Safety mirror for the management-chain shape: even under the hedge conditions, a proposed-removed user who STILL matches the whole rule (manager chain + filter) in the latest table must NOT be claimed as no-longer-matching, so the fallback stays silent and the hedge marker is preserved.
            var previousAdfRunId = Guid.NewGuid();
            var previousRunId = Guid.NewGuid();
            var latestRunId = Guid.NewGuid();
            var latestTable = latestRunId.ToString().Replace("-", string.Empty);
            var removedUser = Guid.NewGuid();
            var now = DateTime.UtcNow;
            string? capturedPrompt = null;

            // Synthetic management-chain query (no real ids): chain under id 42 filtered to EmployeeType FTE.
            _mockSyncJobRepository.Setup(x => x.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(new SyncJob
                {
                    Id = _syncJobId,
                    TargetOfficeGroupId = _targetGroupId,
                    Query = "[{\"type\":\"SqlMembership\",\"source\":{\"manager\":{\"id\":42,\"depth\":2},\"filter\":\"EmployeeType = 'FTE'\"}}]"
                });
            SetupCurrentRunAndPreviousRun(Guid.NewGuid(), previousAdfRunId, previousRunId, usersRemoved: 1);
            SetupAggregatedRemovedUsers(removedUser);
            // No exact-run snapshot (null AdfRunId) and blobs gone, so only the fallback could speak.
            _mockSyncJobHistoryRepository.Setup(x => x.GetByRunIdAsync(_runId))
                .ReturnsAsync(new global::Models.SyncJobHistory.SyncJobHistory
                {
                    SyncJobId = _syncJobId,
                    RunId = _runId,
                    Status = "Idle",
                    UpdatedAt = now,
                    StartTime = now.AddMinutes(-5),
                    EndTime = now,
                    UsersAdded = 0,
                    UsersRemoved = 1,
                    BeforeSyncUserCount = 100,
                    AfterSyncUserCount = 99,
                    AdfRunId = null
                });
            _mockBlobStorageRepository.Setup(x => x.FindPartFilesByRunIdAsync(_targetGroupId.ToString(), previousRunId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult>());
            _mockAdfRunHistoryRepository.Setup(x => x.GetLatestAdfRunAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(new AdfRunHistory { AdfRunId = latestRunId });
            // The proposed-removed user is STILL in the management chain today, so the contradiction guard must fire.
            SetupSqlEvaluation(
                "EmployeeType = 'FTE'",
                latestTable,
                IdentityTypeSet.UsersOnly,
                managerEmployeeId: 42,
                depth: 2,
                userObjectIds: new[] { removedUser });
            _mockOpenAIService.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((_, prompt) => capturedPrompt = prompt)
                .ReturnsAsync("This sync completed successfully.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedPrompt);
            Assert.IsFalse(capturedPrompt!.Contains("in the latest HR data (current state, not the removal-time snapshot)"),
                "The fallback must never claim a still-matching user no longer matches the rule.");
            StringAssert.Contains(capturedPrompt!, "no attributable source found");
        }

        // ---- Phase 4 confirms the residual hedges (all attribution layers empty) are steered to a descriptive rule-based explanation instead of "could not be determined"; no real job/run/ADF ids or names are used ----

        [TestMethod]
        public async Task ExecuteAsync_RemovesDescriptiveFallback_ThresholdBlocked_AllLayersEmpty_SteersToRuleNotHedge()
        {
            // Mirrors the reported threshold-blocked run whose removals "could not be determined": exact snapshot pruned, previous snapshot pruned, walk-back blobs gone, and no latest ADF table, so every attribution layer is empty and only the descriptive rule fallback remains.
            var currentAdfRunId = Guid.NewGuid();
            var previousAdfRunId = Guid.NewGuid();
            var previousRunId = Guid.NewGuid();
            var currentTable = currentAdfRunId.ToString().Replace("-", string.Empty);
            var previousTable = previousAdfRunId.ToString().Replace("-", string.Empty);
            var removedUser = Guid.NewGuid();
            var now = DateTime.UtcNow;
            string? capturedPrompt = null;

            SetupCurrentRunAndPreviousRun(currentAdfRunId, previousAdfRunId, previousRunId, usersRemoved: 1);
            SetupAggregatedRemovedUsers(removedUser);
            // Mark THIS run threshold-blocked so it takes the blocked path with proposed (not applied) removals.
            _mockSyncJobHistoryRepository.Setup(x => x.GetByRunIdAsync(_runId))
                .ReturnsAsync(new global::Models.SyncJobHistory.SyncJobHistory
                {
                    SyncJobId = _syncJobId,
                    RunId = _runId,
                    Status = "ThresholdExceeded",
                    UpdatedAt = now,
                    StartTime = now.AddMinutes(-5),
                    EndTime = now,
                    UsersAdded = 0,
                    UsersRemoved = 1,
                    BeforeSyncUserCount = 100,
                    AfterSyncUserCount = 100,
                    AdfRunId = currentAdfRunId
                });
            // Both ADF snapshots pruned so the single-step SQL diff hedges; part blobs default empty so the walk-back finds nothing; GetMostRecentSucceededRunIdAsync left unset so Phase 3 is skipped.
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(currentTable)).ReturnsAsync(false);
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(previousTable)).ReturnsAsync(false);
            _mockSqlMembershipRepository.Setup(x => x.GetUserAttributesBatchAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<string>()))
                .ReturnsAsync(new Dictionary<string, Dictionary<string, string>>());
            _mockOpenAIService.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((_, prompt) => capturedPrompt = prompt)
                .ReturnsAsync("This sync proposed changes that were blocked because the change exceeded the configured threshold.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedPrompt);
            // Every layer came up empty, so the no-signal marker is still present.
            StringAssert.Contains(capturedPrompt!, "no attributable source found");
            // Phase 4: the marker's HARD RULE now steers to a descriptive rule restatement and forbids the old hedge.
            StringAssert.Contains(capturedPrompt!, "descriptive membership-rule fallback");
            StringAssert.Contains(capturedPrompt!, "no longer match its membership rule");
            StringAssert.Contains(capturedPrompt!, "NEVER say the reason could not be determined");
            // The always-available plain-English rule glossary the descriptive fallback depends on is present.
            StringAssert.Contains(capturedPrompt!, "Membership rules as of this run:");
            // Phase 4 must not weaken the anti-hallucination guard.
            StringAssert.Contains(capturedPrompt!, "do NOT name any specific source group");
        }

        [TestMethod]
        public async Task ExecuteAsync_AddsDescriptiveFallback_NoPerPartAttribution_SteersToRuleNotHedge()
        {
            // Mirrors the reported adds-only hedge ("added N users ... could not be determined for these additions"): the run added a user but no current per-part blob attributes it, so the adds marker fires and only the descriptive rule fallback remains.
            var currentAdfRunId = Guid.NewGuid();
            var addedUser = Guid.NewGuid();
            var now = DateTime.UtcNow;
            string? capturedPrompt = null;

            _mockSyncJobHistoryRepository.Setup(x => x.GetByRunIdAsync(_runId))
                .ReturnsAsync(new global::Models.SyncJobHistory.SyncJobHistory
                {
                    SyncJobId = _syncJobId,
                    RunId = _runId,
                    Status = "Idle",
                    UpdatedAt = now,
                    StartTime = now.AddMinutes(-5),
                    EndTime = now,
                    UsersAdded = 1,
                    UsersRemoved = 0,
                    BeforeSyncUserCount = 100,
                    AfterSyncUserCount = 101,
                    AdfRunId = currentAdfRunId
                });
            // Aggregated blob has one added user; part blobs default empty so no source attributes the add.
            _mockBlobStorageRepository.Setup(x => x.FindAggregatedFileByRunIdAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new BlobResult { BlobStatus = BlobStatus.Found, Path = "test/adds.json" });
            _mockBlobStorageRepository.Setup(x => x.DownloadFileAsync("test/adds.json"))
                .ReturnsAsync(new BlobResult { BlobStatus = BlobStatus.Found, Content = $"{{\"SourceMembers\":[{{\"ObjectId\":\"{addedUser}\",\"MembershipAction\":1}}]}}" });
            _mockOpenAIService.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((_, prompt) => capturedPrompt = prompt)
                .ReturnsAsync("This sync added members.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedPrompt);
            // The adds no-signal marker is present because no per-part source attributed the addition.
            StringAssert.Contains(capturedPrompt!, "no attributable source found");
            // Phase 4: the adds marker now steers to a descriptive rule restatement and forbids the old hedge.
            StringAssert.Contains(capturedPrompt!, "these users were added because they match the group's membership rule");
            StringAssert.Contains(capturedPrompt!, "NEVER say the reason could not be determined");
            StringAssert.Contains(capturedPrompt!, "Membership rules as of this run:");
            // Anti-hallucination guard for adds must survive.
            StringAssert.Contains(capturedPrompt!, "do NOT infer one by process of elimination");
        }

        [TestMethod]
        public async Task ExecuteAsync_AddsAttribution_ReadableGroupBlobWithZeroOverlap_UsesNoAttributableSourceGuard()
        {
            var addedUser = Guid.NewGuid();
            var unrelatedUser = Guid.NewGuid();
            var sourceGroupId = Guid.NewGuid();
            var now = DateTime.UtcNow;
            string? capturedPrompt = null;

            _mockSyncJobRepository.Setup(x => x.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(new SyncJob
                {
                    Id = _syncJobId,
                    TargetOfficeGroupId = _targetGroupId,
                    Query = $"[{{\"type\":\"GroupMembership\",\"source\":\"{sourceGroupId}\"}}]"
                });
            _mockSyncJobHistoryRepository.Setup(x => x.GetByRunIdAsync(_runId))
                .ReturnsAsync(new global::Models.SyncJobHistory.SyncJobHistory
                {
                    SyncJobId = _syncJobId,
                    RunId = _runId,
                    Status = "Idle",
                    UpdatedAt = now,
                    StartTime = now.AddMinutes(-5),
                    EndTime = now,
                    UsersAdded = 1,
                    UsersRemoved = 0,
                    BeforeSyncUserCount = 100,
                    AfterSyncUserCount = 101
                });
            SetupAggregatedMembers(MembershipAction.Add, addedUser);
            _mockBlobStorageRepository.Setup(x =>
                    x.FindPartFilesByRunIdAsync(
                        _targetGroupId.ToString(),
                        _runId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult>
                {
                    ["GroupMembership_1"] = new()
                    {
                        BlobStatus = BlobStatus.Found,
                        Path = "current/unrelated.json"
                    }
                });
            _mockBlobStorageRepository.Setup(x =>
                    x.ExtractGroupMembershipSourceMembersAsync(
                        "current/unrelated.json"))
                .ReturnsAsync(new HashSet<Guid> { unrelatedUser });
            _mockGraphGroupRepository.Setup(x => x.GetGroupNamesAsync(It.IsAny<List<Guid>>()))
                .ReturnsAsync(new Dictionary<Guid, string>
                {
                    [sourceGroupId] = "Unrelated Source"
                });
            _mockOpenAIService.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((_, prompt) => capturedPrompt = prompt)
                .ReturnsAsync("This sync added members.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedPrompt);
            StringAssert.Contains(capturedPrompt!, "no attributable source found");
            StringAssert.Contains(capturedPrompt, "HARD RULE");
            Assert.IsFalse(capturedPrompt.Contains("none of the added users matched this rule"));
        }

        [TestMethod]
        public async Task ExecuteAsync_RemovesDescriptiveFallback_ManagerChainRule_RetainedHistoryExhausted_DescribesRuleInPlainEnglish()
        {
            // Mirrors the management-chain hedge where retained history is exhausted: no exact snapshot (null AdfRunId), walk-back blobs gone, no latest ADF table, so the descriptive fallback must restate the manager-chain rule the glossary already carries.
            var previousAdfRunId = Guid.NewGuid();
            var previousRunId = Guid.NewGuid();
            var removedUser = Guid.NewGuid();
            var now = DateTime.UtcNow;
            string? capturedPrompt = null;

            // Synthetic management-chain query (no real ids): chain under id 55 filtered to EmployeeType FTE.
            _mockSyncJobRepository.Setup(x => x.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(new SyncJob
                {
                    Id = _syncJobId,
                    TargetOfficeGroupId = _targetGroupId,
                    Query = "[{\"type\":\"SqlMembership\",\"source\":{\"manager\":{\"id\":55,\"depth\":2},\"filter\":\"EmployeeType = 'FTE'\"}}]"
                });
            SetupCurrentRunAndPreviousRun(Guid.NewGuid(), previousAdfRunId, previousRunId, usersRemoved: 1);
            SetupAggregatedRemovedUsers(removedUser);
            // Null current AdfRunId (no exact snapshot); part blobs default empty (walk-back finds nothing); GetMostRecentSucceededRunIdAsync unset (Phase 3 skipped).
            _mockSyncJobHistoryRepository.Setup(x => x.GetByRunIdAsync(_runId))
                .ReturnsAsync(new global::Models.SyncJobHistory.SyncJobHistory
                {
                    SyncJobId = _syncJobId,
                    RunId = _runId,
                    Status = "Idle",
                    UpdatedAt = now,
                    StartTime = now.AddMinutes(-5),
                    EndTime = now,
                    UsersAdded = 0,
                    UsersRemoved = 1,
                    BeforeSyncUserCount = 100,
                    AfterSyncUserCount = 99,
                    AdfRunId = null
                });
            _mockOpenAIService.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((_, prompt) => capturedPrompt = prompt)
                .ReturnsAsync("This sync removed members.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedPrompt);
            StringAssert.Contains(capturedPrompt!, "no attributable source found");
            StringAssert.Contains(capturedPrompt!, "no longer match its membership rule");
            StringAssert.Contains(capturedPrompt!, "NEVER say the reason could not be determined");
            // The glossary describes the manager-chain rule in plain English, which is what the descriptive fallback restates.
            StringAssert.Contains(capturedPrompt!, "management chain rooted at");
        }

        // ---- Phase 4, end-to-end: each reported hedge shape (all attribution layers empty) now yields an owner-visible descriptive explanation, never the FallbackExplanation hedge; no real job/run/ADF ids or names are used ----

        [TestMethod]
        public async Task ExecuteAsync_RemovesDescriptiveFallback_NullAdfRunId_AllLayersEmpty_SteersToRuleNotHedge()
        {
            // Mirrors the reported hedge whose root cause was a missing AdfRunId: this run has NO AdfRunId (no exact snapshot), the walk-back blobs are gone, and there is no latest ADF table, so every attribution layer is empty and only the descriptive rule fallback remains.
            var previousAdfRunId = Guid.NewGuid();
            var previousRunId = Guid.NewGuid();
            var removedUser = Guid.NewGuid();
            var now = DateTime.UtcNow;
            string? capturedPrompt = null;

            SetupCurrentRunAndPreviousRun(Guid.NewGuid(), previousAdfRunId, previousRunId, usersRemoved: 1);
            SetupAggregatedRemovedUsers(removedUser);
            // Null current AdfRunId (no exact snapshot); part blobs default empty (walk-back finds nothing); GetMostRecentSucceededRunIdAsync unset (Phase 3 skipped).
            _mockSyncJobHistoryRepository.Setup(x => x.GetByRunIdAsync(_runId))
                .ReturnsAsync(new global::Models.SyncJobHistory.SyncJobHistory
                {
                    SyncJobId = _syncJobId,
                    RunId = _runId,
                    Status = "Idle",
                    UpdatedAt = now,
                    StartTime = now.AddMinutes(-5),
                    EndTime = now,
                    UsersAdded = 0,
                    UsersRemoved = 1,
                    BeforeSyncUserCount = 100,
                    AfterSyncUserCount = 99,
                    AdfRunId = null
                });
            _mockOpenAIService.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((_, prompt) => capturedPrompt = prompt)
                .ReturnsAsync("This sync removed members.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedPrompt);
            StringAssert.Contains(capturedPrompt!, "no attributable source found");
            StringAssert.Contains(capturedPrompt!, "no longer match its membership rule");
            StringAssert.Contains(capturedPrompt!, "NEVER say the reason could not be determined");
            StringAssert.Contains(capturedPrompt!, "Membership rules as of this run:");
        }

        [TestMethod]
        public async Task ExecuteAsync_ThresholdBlockedRemoves_AllLayersEmpty_ResponseIsDescriptiveNotHedge()
        {
            // End-to-end proof for the reported threshold-blocked example: with every attribution layer empty, once the model follows the new descriptive instruction the owner sees a rule-based explanation and NEVER the old "could not be determined" hedge.
            var currentAdfRunId = Guid.NewGuid();
            var previousAdfRunId = Guid.NewGuid();
            var previousRunId = Guid.NewGuid();
            var currentTable = currentAdfRunId.ToString().Replace("-", string.Empty);
            var previousTable = previousAdfRunId.ToString().Replace("-", string.Empty);
            var removedUser = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var descriptive = "This sync's proposed removals are people who were in the group but no longer match its membership rule.";
            string? capturedPrompt = null;

            SetupCurrentRunAndPreviousRun(currentAdfRunId, previousAdfRunId, previousRunId, usersRemoved: 1);
            SetupAggregatedRemovedUsers(removedUser);
            _mockSyncJobHistoryRepository.Setup(x => x.GetByRunIdAsync(_runId))
                .ReturnsAsync(new global::Models.SyncJobHistory.SyncJobHistory
                {
                    SyncJobId = _syncJobId,
                    RunId = _runId,
                    Status = "ThresholdExceeded",
                    UpdatedAt = now,
                    StartTime = now.AddMinutes(-5),
                    EndTime = now,
                    UsersAdded = 0,
                    UsersRemoved = 1,
                    BeforeSyncUserCount = 100,
                    AfterSyncUserCount = 100,
                    AdfRunId = currentAdfRunId
                });
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(currentTable)).ReturnsAsync(false);
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(previousTable)).ReturnsAsync(false);
            _mockSqlMembershipRepository.Setup(x => x.GetUserAttributesBatchAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<string>()))
                .ReturnsAsync(new Dictionary<string, Dictionary<string, string>>());
            // Model complies with the new instruction and returns a descriptive rule-based explanation.
            _mockOpenAIService.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((_, prompt) => capturedPrompt = prompt)
                .ReturnsAsync(descriptive);

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            // The owner-visible explanation is the descriptive one, never the old hedge.
            Assert.AreEqual(descriptive, response.Explanation);
            Assert.AreNotEqual(GetRunExplanationHandler.FallbackExplanation, response.Explanation);
            Assert.IsFalse(response.Explanation!.Contains("could not be determined"),
                "The reported threshold-blocked removals hedge must no longer be surfaced.");
            // The prompt drove it: the no-signal marker forbids the hedge and requires the descriptive rule restatement.
            Assert.IsNotNull(capturedPrompt);
            StringAssert.Contains(capturedPrompt!, "NEVER say the reason could not be determined");
        }

        [TestMethod]
        public async Task ExecuteAsync_AddsOnly_NoAttribution_ResponseIsDescriptiveNotHedge()
        {
            // End-to-end proof for the reported adds-only example ("added N users ... could not be determined for these additions"): with no per-part attribution, the model follows the descriptive instruction and the owner sees a rule-based explanation instead of the hedge.
            var currentAdfRunId = Guid.NewGuid();
            var addedUser = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var descriptive = "The added users are new people who match the group's membership rule.";
            string? capturedPrompt = null;

            _mockSyncJobHistoryRepository.Setup(x => x.GetByRunIdAsync(_runId))
                .ReturnsAsync(new global::Models.SyncJobHistory.SyncJobHistory
                {
                    SyncJobId = _syncJobId,
                    RunId = _runId,
                    Status = "Idle",
                    UpdatedAt = now,
                    StartTime = now.AddMinutes(-5),
                    EndTime = now,
                    UsersAdded = 1,
                    UsersRemoved = 0,
                    BeforeSyncUserCount = 100,
                    AfterSyncUserCount = 101,
                    AdfRunId = currentAdfRunId
                });
            // Aggregated blob has one added user; part blobs default empty so no source attributes the add.
            _mockBlobStorageRepository.Setup(x => x.FindAggregatedFileByRunIdAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new BlobResult { BlobStatus = BlobStatus.Found, Path = "test/adds.json" });
            _mockBlobStorageRepository.Setup(x => x.DownloadFileAsync("test/adds.json"))
                .ReturnsAsync(new BlobResult { BlobStatus = BlobStatus.Found, Content = $"{{\"SourceMembers\":[{{\"ObjectId\":\"{addedUser}\",\"MembershipAction\":1}}]}}" });
            _mockOpenAIService.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((_, prompt) => capturedPrompt = prompt)
                .ReturnsAsync(descriptive);

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(descriptive, response.Explanation);
            Assert.AreNotEqual(GetRunExplanationHandler.FallbackExplanation, response.Explanation);
            Assert.IsFalse(response.Explanation!.Contains("could not be determined"),
                "The reported adds-only hedge must no longer be surfaced.");
            // The prompt drove it: the adds marker requires the descriptive rule restatement.
            Assert.IsNotNull(capturedPrompt);
            StringAssert.Contains(capturedPrompt!, "these users were added because they match the group's membership rule");
        }

        // ---- PR-comment #1: the never-hedge behavior must NOT depend on model compliance. When the model DEFIES the prompt and ships the hedge (exact or a per-add/remove variant) on an established run that moved members, a deterministic override replaces it with a rule-based explanation. No real job/run/ADF ids or names are used. ----

        [TestMethod]
        public async Task ExecuteAsync_NonInitialThresholdBlockedRemoves_ModelShipsHedgeVariant_OverrideReplacesWithRuleText()
        {
            // Mirrors the reported LP/PROD symptom exactly: an established, threshold-blocked run where the model ignores the never-hedge prompt and returns the per-removal hedge VARIANT (not the exact FallbackExplanation constant). The deterministic override must catch the variant and surface the rule instead.
            var currentAdfRunId = Guid.NewGuid();
            var previousAdfRunId = Guid.NewGuid();
            var previousRunId = Guid.NewGuid();
            var removedUser = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var hedgeVariant = "This sync proposed changes that were blocked because the change exceeded the configured threshold. The specific reason for the removals could not be determined from the available data.";

            SetupCurrentRunAndPreviousRun(currentAdfRunId, previousAdfRunId, previousRunId, usersRemoved: 7);
            SetupAggregatedRemovedUsers(removedUser);
            _mockSyncJobHistoryRepository.Setup(x => x.GetByRunIdAsync(_runId))
                .ReturnsAsync(new global::Models.SyncJobHistory.SyncJobHistory
                {
                    SyncJobId = _syncJobId,
                    RunId = _runId,
                    Status = "ThresholdExceeded",
                    UpdatedAt = now,
                    StartTime = now.AddMinutes(-5),
                    EndTime = now,
                    UsersAdded = 0,
                    UsersRemoved = 7,
                    BeforeSyncUserCount = 100,
                    AfterSyncUserCount = 100,
                    AdfRunId = currentAdfRunId
                });
            _mockOpenAIService.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(hedgeVariant);

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreNotEqual(hedgeVariant, response.Explanation);
            Assert.AreNotEqual(GetRunExplanationHandler.FallbackExplanation, response.Explanation);
            Assert.IsFalse(response.Explanation!.Contains("could not be determined", StringComparison.OrdinalIgnoreCase),
                "The variant hedge must be replaced, not surfaced.");
            StringAssert.Contains(response.Explanation!, "no longer match");
            StringAssert.Contains(response.Explanation!, "threshold");
            // The resolved rule detail from the glossary must flow into the deterministic text.
            StringAssert.Contains(response.Explanation!, "Building is \"B40\"");
        }

        [TestMethod]
        public async Task ExecuteAsync_NonInitialThresholdBlockedRemoves_ModelShipsExactFallback_OverrideReplacesWithRuleText()
        {
            // Same established threshold-blocked scenario, but the model returns the EXACT FallbackExplanation constant; the override must catch that shape too.
            var currentAdfRunId = Guid.NewGuid();
            var previousAdfRunId = Guid.NewGuid();
            var previousRunId = Guid.NewGuid();
            var removedUser = Guid.NewGuid();
            var now = DateTime.UtcNow;

            SetupCurrentRunAndPreviousRun(currentAdfRunId, previousAdfRunId, previousRunId, usersRemoved: 4);
            SetupAggregatedRemovedUsers(removedUser);
            _mockSyncJobHistoryRepository.Setup(x => x.GetByRunIdAsync(_runId))
                .ReturnsAsync(new global::Models.SyncJobHistory.SyncJobHistory
                {
                    SyncJobId = _syncJobId,
                    RunId = _runId,
                    Status = "ThresholdExceeded",
                    UpdatedAt = now,
                    StartTime = now.AddMinutes(-5),
                    EndTime = now,
                    UsersAdded = 0,
                    UsersRemoved = 4,
                    BeforeSyncUserCount = 100,
                    AfterSyncUserCount = 100,
                    AdfRunId = currentAdfRunId
                });
            _mockOpenAIService.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(GetRunExplanationHandler.FallbackExplanation);

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreNotEqual(GetRunExplanationHandler.FallbackExplanation, response.Explanation);
            Assert.IsFalse(response.Explanation!.Contains("could not be determined", StringComparison.OrdinalIgnoreCase));
            StringAssert.Contains(response.Explanation!, "no longer match");
            StringAssert.Contains(response.Explanation!, "threshold");
        }

        [TestMethod]
        public async Task ExecuteAsync_NonInitialAddsOnly_ModelShipsHedge_OverrideReplacesWithRuleText()
        {
            // Established adds-only run where the model ships the per-addition hedge; override must describe the rule-based addition.
            var currentAdfRunId = Guid.NewGuid();
            var previousAdfRunId = Guid.NewGuid();
            var previousRunId = Guid.NewGuid();
            var addedUser = Guid.NewGuid();
            var now = DateTime.UtcNow;

            SetupCurrentRunAndPreviousRun(currentAdfRunId, previousAdfRunId, previousRunId, usersRemoved: 0);
            _mockSyncJobHistoryRepository.Setup(x => x.GetByRunIdAsync(_runId))
                .ReturnsAsync(new global::Models.SyncJobHistory.SyncJobHistory
                {
                    SyncJobId = _syncJobId,
                    RunId = _runId,
                    Status = "Idle",
                    UpdatedAt = now,
                    StartTime = now.AddMinutes(-5),
                    EndTime = now,
                    UsersAdded = 3,
                    UsersRemoved = 0,
                    BeforeSyncUserCount = 100,
                    AfterSyncUserCount = 103,
                    AdfRunId = currentAdfRunId
                });
            _mockBlobStorageRepository.Setup(x => x.FindAggregatedFileByRunIdAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new BlobResult { BlobStatus = BlobStatus.Found, Path = "test/adds.json" });
            _mockBlobStorageRepository.Setup(x => x.DownloadFileAsync("test/adds.json"))
                .ReturnsAsync(new BlobResult { BlobStatus = BlobStatus.Found, Content = $"{{\"SourceMembers\":[{{\"ObjectId\":\"{addedUser}\",\"MembershipAction\":1}}]}}" });
            _mockOpenAIService.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync("Added members, but the specific reason could not be determined from the available data.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsFalse(response.Explanation!.Contains("could not be determined", StringComparison.OrdinalIgnoreCase));
            StringAssert.Contains(response.Explanation!, "added members who newly match");
            StringAssert.Contains(response.Explanation!, "Building is \"B40\"");
        }

        [TestMethod]
        public async Task ExecuteAsync_NonInitialMixedAddsAndRemoves_ModelShipsHedge_OverrideReplacesWithRuleText()
        {
            // Established mixed run (both adds and removes) where the model hedges; override must describe both directions.
            var currentAdfRunId = Guid.NewGuid();
            var previousAdfRunId = Guid.NewGuid();
            var previousRunId = Guid.NewGuid();
            var addedUser = Guid.NewGuid();
            var removedUser = Guid.NewGuid();
            var now = DateTime.UtcNow;

            SetupCurrentRunAndPreviousRun(currentAdfRunId, previousAdfRunId, previousRunId, usersRemoved: 2);
            _mockSyncJobHistoryRepository.Setup(x => x.GetByRunIdAsync(_runId))
                .ReturnsAsync(new global::Models.SyncJobHistory.SyncJobHistory
                {
                    SyncJobId = _syncJobId,
                    RunId = _runId,
                    Status = "Idle",
                    UpdatedAt = now,
                    StartTime = now.AddMinutes(-5),
                    EndTime = now,
                    UsersAdded = 2,
                    UsersRemoved = 2,
                    BeforeSyncUserCount = 100,
                    AfterSyncUserCount = 100,
                    AdfRunId = currentAdfRunId
                });
            _mockBlobStorageRepository.Setup(x => x.FindAggregatedFileByRunIdAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new BlobResult { BlobStatus = BlobStatus.Found, Path = "test/mixed.json" });
            _mockBlobStorageRepository.Setup(x => x.DownloadFileAsync("test/mixed.json"))
                .ReturnsAsync(new BlobResult { BlobStatus = BlobStatus.Found, Content = $"{{\"SourceMembers\":[{{\"ObjectId\":\"{addedUser}\",\"MembershipAction\":1}},{{\"ObjectId\":\"{removedUser}\",\"MembershipAction\":2}}]}}" });
            _mockOpenAIService.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync("The specific reason could not be determined from the available data.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsFalse(response.Explanation!.Contains("could not be determined", StringComparison.OrdinalIgnoreCase));
            StringAssert.Contains(response.Explanation!, "added members who newly match");
            StringAssert.Contains(response.Explanation!, "removed members who were in the group but no longer match");
        }

        [TestMethod]
        public async Task ExecuteAsync_NonInitialRemoves_ModelReturnsGoodExplanation_OverrideDoesNotFire()
        {
            // Guard against false positives: when the model returns a good, non-hedge explanation, the deterministic override must NOT rewrite it.
            var currentAdfRunId = Guid.NewGuid();
            var previousAdfRunId = Guid.NewGuid();
            var previousRunId = Guid.NewGuid();
            var removedUser = Guid.NewGuid();
            var good = "GMM removed 3 people who transferred out of the engineering organization.";

            SetupCurrentRunAndPreviousRun(currentAdfRunId, previousAdfRunId, previousRunId, usersRemoved: 3);
            SetupAggregatedRemovedUsers(removedUser);
            _mockOpenAIService.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(good);

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(good, response.Explanation);
        }

        [TestMethod]
        public async Task ExecuteAsync_NonInitialThresholdBlockedRemoves_UnresolvedCodeFilter_OverrideNeverSurfacesUnavailableSentinel()
        {
            // When the _Code filter can't be resolved to a description (mappings unavailable at explanation time), the glossary carries an internal "description is unavailable" sentinel. The override must drop that detail and fall back to the plain rule phrasing — never hedge, never leak the sentinel.
            var currentAdfRunId = Guid.NewGuid();
            var previousAdfRunId = Guid.NewGuid();
            var previousRunId = Guid.NewGuid();
            var removedUser = Guid.NewGuid();
            var now = DateTime.UtcNow;

            _mockSyncJobRepository.Setup(x => x.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(new SyncJob
                {
                    Id = _syncJobId,
                    TargetOfficeGroupId = _targetGroupId,
                    Query = "[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Company_Code = '8210'\"}}]"
                });
            SetupCurrentRunAndPreviousRun(currentAdfRunId, previousAdfRunId, previousRunId, usersRemoved: 5);
            SetupAggregatedRemovedUsers(removedUser);
            _mockSyncJobHistoryRepository.Setup(x => x.GetByRunIdAsync(_runId))
                .ReturnsAsync(new global::Models.SyncJobHistory.SyncJobHistory
                {
                    SyncJobId = _syncJobId,
                    RunId = _runId,
                    Status = "ThresholdExceeded",
                    UpdatedAt = now,
                    StartTime = now.AddMinutes(-5),
                    EndTime = now,
                    UsersAdded = 0,
                    UsersRemoved = 5,
                    BeforeSyncUserCount = 100,
                    AfterSyncUserCount = 100,
                    AdfRunId = currentAdfRunId
                });
            // No GetMostRecentSucceededRunIdAsync / GetAttributeMappingsAsync mock => the _Code cannot be resolved.
            _mockOpenAIService.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(GetRunExplanationHandler.FallbackExplanation);

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsFalse(response.Explanation!.Contains("could not be determined", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(response.Explanation!.Contains("description is unavailable", StringComparison.OrdinalIgnoreCase),
                "The internal unavailable-description sentinel must never reach the owner.");
            StringAssert.Contains(response.Explanation!, "the group's configured membership rules");
            StringAssert.Contains(response.Explanation!, "no longer match");
        }

        // ---- Direct unit tests for the deterministic never-hedge helpers ----

        [TestMethod]
        public void IsHedgeExplanation_DetectsExactVariantBlankAndNonHedge()
        {
            var type = typeof(GetRunExplanationHandler);
            Assert.IsTrue(InvokeStaticPrivate<bool>(type, "IsHedgeExplanation", new object?[] { GetRunExplanationHandler.FallbackExplanation }),
                "Exact FallbackExplanation must be detected as a hedge.");
            Assert.IsTrue(InvokeStaticPrivate<bool>(type, "IsHedgeExplanation", new object?[] { "Blocked by threshold. The specific reason for the removals could not be determined from the available data." }),
                "The per-removal variant must be detected as a hedge.");
            Assert.IsFalse(InvokeStaticPrivate<bool>(type, "IsHedgeExplanation", new object?[] { "GMM removed people who no longer match the rule." }),
                "A descriptive explanation must not be treated as a hedge.");
            Assert.IsFalse(InvokeStaticPrivate<bool>(type, "IsHedgeExplanation", new object?[] { "   " }),
                "Blank is an AI soft-failure, not a hedge (the honest fallback is preserved).");
            Assert.IsFalse(InvokeStaticPrivate<bool>(type, "IsHedgeExplanation", new object?[] { null }),
                "Null must not be treated as a hedge.");
        }

        [TestMethod]
        public void BuildNonInitialRuleExplanation_ProducesDescriptiveRuleTextForEachDirection()
        {
            var type = typeof(GetRunExplanationHandler);
            var rules = "- Rule 1: Includes employees where Building is 'B40'.";

            var blockedRemoves = InvokeStaticPrivate<string>(type, "BuildNonInitialRuleExplanation", new object?[] { 0, 3, true, rules })!;
            StringAssert.Contains(blockedRemoves, "threshold");
            StringAssert.Contains(blockedRemoves, "no longer match");
            StringAssert.Contains(blockedRemoves, "Building is 'B40'");
            Assert.IsFalse(blockedRemoves.Contains("could not be determined", StringComparison.OrdinalIgnoreCase));

            var addsOnly = InvokeStaticPrivate<string>(type, "BuildNonInitialRuleExplanation", new object?[] { 5, 0, false, rules })!;
            StringAssert.Contains(addsOnly, "added members who newly match");

            var mixed = InvokeStaticPrivate<string>(type, "BuildNonInitialRuleExplanation", new object?[] { 2, 3, false, rules })!;
            StringAssert.Contains(mixed, "added members who newly match");
            StringAssert.Contains(mixed, "removed members who were in the group but no longer match");
        }

        [TestMethod]
        public void BuildNonInitialRuleExplanation_DropsUnavailableSentinelAndUsesPlainRulePhrasing()
        {
            var type = typeof(GetRunExplanationHandler);
            var unresolved = "- Rule 1: Includes employees where Company is a configured value whose description is unavailable.";

            var text = InvokeStaticPrivate<string>(type, "BuildNonInitialRuleExplanation", new object?[] { 0, 4, true, unresolved })!;

            Assert.IsFalse(text.Contains("description is unavailable", StringComparison.OrdinalIgnoreCase),
                "The internal unavailable-description sentinel must be dropped.");
            StringAssert.Contains(text, "the group's configured membership rules");
            StringAssert.Contains(text, "no longer match");
        }

        [TestMethod]
        public void ComputeWalkBackFloor_AllIdenticalResubmits_UsesOldestKnownCurrentLayout()
        {
            var now = DateTime.UtcNow;
            var oldestKnownCurrentLayout = now.AddHours(-10);
            var changes = new List<SyncJobChange>
            {
                ChangeWithQuery(now.AddHours(-1), "Q"),
                ChangeWithQuery(oldestKnownCurrentLayout, "Q"),
            };

            var floor = InvokeStaticPrivate<DateTime>(typeof(GetRunExplanationHandler), "ComputeWalkBackFloor", new object?[] { changes, "Q" });

            Assert.AreEqual(oldestKnownCurrentLayout, floor);
        }

        [TestMethod]
        public void ComputeWalkBackFloor_IdenticalResubmitThenDifferentQuery_UsesStartOfKnownCurrentLayout()
        {
            var now = DateTime.UtcNow;
            var latestResubmit = now.AddHours(-1);
            var oldestKnownCurrentLayout = now.AddHours(-5);
            var changes = new List<SyncJobChange>
            {
                ChangeWithQuery(latestResubmit, "Q"),
                ChangeWithQuery(oldestKnownCurrentLayout, "Q"),
                ChangeWithQuery(now.AddHours(-30), "Q_OLD"),
            };

            var floor = InvokeStaticPrivate<DateTime>(typeof(GetRunExplanationHandler), "ComputeWalkBackFloor", new object?[] { changes, "Q" });

            Assert.AreEqual(oldestKnownCurrentLayout, floor);
            Assert.AreNotEqual(latestResubmit, floor);
        }

        [TestMethod]
        public void ComputeWalkBackFloor_MostRecentChangeIsDifferentQuery_FloorsAtThatChange()
        {
            var now = DateTime.UtcNow;
            var diffChange = now.AddHours(-2);
            var changes = new List<SyncJobChange> { ChangeWithQuery(diffChange, "Q_OLD") };

            var floor = InvokeStaticPrivate<DateTime>(typeof(GetRunExplanationHandler), "ComputeWalkBackFloor", new object?[] { changes, "Q" });

            Assert.AreEqual(diffChange, floor);
        }

        [TestMethod]
        public void ComputeWalkBackFloor_NullOrEmpty_ReturnsMinValue()
        {
            Assert.AreEqual(DateTime.MinValue,
                InvokeStaticPrivate<DateTime>(typeof(GetRunExplanationHandler), "ComputeWalkBackFloor", new object?[] { null, "Q" }));
            Assert.AreEqual(DateTime.MinValue,
                InvokeStaticPrivate<DateTime>(typeof(GetRunExplanationHandler), "ComputeWalkBackFloor", new object?[] { new List<SyncJobChange>(), "Q" }));
        }

        [TestMethod]
        public void ComputeWalkBackFloor_SkipsStatusOnlyChangesWithoutQuery()
        {
            // A status-only change (e.g. an approval) carries no query snapshot, so it must not be treated as a query boundary that truncates the walk-back.
            var now = DateTime.UtcNow;
            var statusOnly = now.AddHours(-1);
            var realQueryChange = now.AddHours(-20);
            var changes = new List<SyncJobChange>
            {
                ChangeWithQuery(statusOnly, null),
                ChangeWithQuery(realQueryChange, "Q_OLD"),
            };

            var floor = InvokeStaticPrivate<DateTime>(typeof(GetRunExplanationHandler), "ComputeWalkBackFloor", new object?[] { changes, "Q" });

            Assert.AreEqual(realQueryChange, floor, "A status-only change without a query snapshot must not truncate the walk-back.");
            Assert.AreNotEqual(statusOnly, floor);
        }

        private const string SqlPartBeforeMigration =
            "[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"EmployeeType = 'FTE'\"},\"exclusionary\":true}]";
        private const string SqlPartWrittenByMigration =
            "[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"EmployeeType = 'FTE'\"},\"exclusionary\":true,\"identityTypes\":[\"users\"]}]";
        private const string SqlPartSavedByUiAfterMigration =
            "[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"EmployeeType = 'FTE'\"},\"identityTypes\":[\"users\"],\"exclusionary\":true}]";

        private static readonly Type[] _explanationHandlerTypes =
        {
            typeof(GetRunExplanationHandler),
            typeof(GetSyncExplanationHandler),
        };

        [TestMethod]
        public void NormalizeQuery_UsersOnlyDefaultAndPropertyOrder_DoNotMakeSnapshotsDiffer()
        {
            foreach (var handlerType in _explanationHandlerTypes)
            {
                var beforeMigration = NormalizeWith(handlerType, SqlPartBeforeMigration);

                Assert.AreEqual(beforeMigration, NormalizeWith(handlerType, SqlPartWrittenByMigration),
                    $"{handlerType.Name}: the migration's explicit users-only selection is the same configuration.");
                Assert.AreEqual(beforeMigration, NormalizeWith(handlerType, SqlPartSavedByUiAfterMigration),
                    $"{handlerType.Name}: the UI's property order is the same configuration.");
            }
        }

        [TestMethod]
        public void NormalizeQuery_SelectionOrder_DoesNotMakeSnapshotsDiffer()
        {
            const string usersThenAgents = "[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"A = 1\"},\"identityTypes\":[\"users\",\"agents\"]}]";
            const string agentsThenUsers = "[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"A = 1\"},\"identityTypes\":[\"agents\",\"users\"]}]";

            foreach (var handlerType in _explanationHandlerTypes)
            {
                Assert.AreEqual(NormalizeWith(handlerType, usersThenAgents), NormalizeWith(handlerType, agentsThenUsers),
                    $"{handlerType.Name}: the same identity types in another order are the same configuration.");
            }
        }

        [TestMethod]
        public void NormalizeQuery_InvalidSelection_IsKeptAsWritten()
        {
            const string validSelection = "[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"A = 1\"},\"identityTypes\":[\"users\"]}]";
            var invalidSelections = new[]
            {
                "[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"A = 1\"},\"identityTypes\":[\"users\",\"users\"]}]",
                "[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"A = 1\"},\"identityTypes\":[\"Users\"]}]",
                "[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"A = 1\"},\"identityTypes\":\"users\"}]",
                "[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"A = 1\"},\"identityTypes\":null}]",
            };

            foreach (var handlerType in _explanationHandlerTypes)
            {
                var valid = NormalizeWith(handlerType, validSelection);
                foreach (var invalidSelection in invalidSelections)
                {
                    Assert.AreNotEqual(valid, NormalizeWith(handlerType, invalidSelection),
                        $"{handlerType.Name}: {invalidSelection}");
                }
            }
        }

        [TestMethod]
        public void NormalizeQuery_RealRuleAndSelectionChanges_StillMakeSnapshotsDiffer()
        {
            const string omittedSelection = "[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"A = 1\"}},{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"B = 1\"}}]";
            var changedConfigurations = new[]
            {
                "[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"A = 1\"},\"identityTypes\":[\"agents\"]},{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"B = 1\"}}]",
                "[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"A = 1\"},\"identityTypes\":[\"users\",\"agents\"]},{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"B = 1\"}}]",
                "[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"A = 2\"}},{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"B = 1\"}}]",
                "[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"A = 1\"},\"exclusionary\":true},{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"B = 1\"}}]",
                "[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"B = 1\"}},{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"A = 1\"}}]",
            };

            foreach (var handlerType in _explanationHandlerTypes)
            {
                var original = NormalizeWith(handlerType, omittedSelection);
                foreach (var changedConfiguration in changedConfigurations)
                {
                    Assert.AreNotEqual(original, NormalizeWith(handlerType, changedConfiguration),
                        $"{handlerType.Name}: {changedConfiguration}");
                }
            }
        }

        [TestMethod]
        public void NormalizeQuery_GroupDefaultAndOrder_DoNotMakeSnapshotsDiffer()
        {
            const string groupPart = "[{\"type\":\"GroupMembership\",\"source\":\"11111111-1111-1111-1111-111111111111\"}]";
            const string usersThenAgents = "[{\"type\":\"GroupMembership\",\"source\":\"11111111-1111-1111-1111-111111111111\",\"identityTypes\":[\"users\",\"agents\"]}]";
            const string agentsThenUsers = "[{\"type\":\"GroupMembership\",\"source\":\"11111111-1111-1111-1111-111111111111\",\"identityTypes\":[\"agents\",\"users\"]}]";

            foreach (var handlerType in _explanationHandlerTypes)
            {
                Assert.AreEqual(
                    NormalizeWith(handlerType, groupPart),
                    NormalizeWith(handlerType, usersThenAgents),
                    handlerType.Name);
                Assert.AreEqual(
                    NormalizeWith(handlerType, usersThenAgents),
                    NormalizeWith(handlerType, agentsThenUsers),
                    handlerType.Name);
            }
        }

        [TestMethod]
        public void NormalizeQuery_RealGroupSelectionChange_MakesSnapshotsDiffer()
        {
            const string usersOnly = "[{\"type\":\"GroupMembership\",\"source\":\"11111111-1111-1111-1111-111111111111\",\"identityTypes\":[\"users\"]}]";
            const string agentsOnly = "[{\"type\":\"GroupMembership\",\"source\":\"11111111-1111-1111-1111-111111111111\",\"identityTypes\":[\"agents\"]}]";

            foreach (var handlerType in _explanationHandlerTypes)
            {
                Assert.AreNotEqual(
                    NormalizeWith(handlerType, usersOnly),
                    NormalizeWith(handlerType, agentsOnly),
                    handlerType.Name);
            }
        }

        [TestMethod]
        public void NormalizeQuery_MissingExclusionary_IsTheSameAsFalse()
        {
            var equivalentPairs = new[]
            {
                (
                    "[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"A = 1\"},\"exclusionary\":false}]",
                    "[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"A = 1\"}}]"
                ),
                (
                    "[{\"type\":\"GroupMembership\",\"source\":\"11111111-1111-1111-1111-111111111111\",\"exclusionary\":false}]",
                    "[{\"type\":\"GroupMembership\",\"source\":\"11111111-1111-1111-1111-111111111111\"}]"
                ),
                (
                    "[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"A = 1\"},\"exclusionary\":false},{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"B = 1\"},\"exclusionary\":true}]",
                    "[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"A = 1\"}},{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"B = 1\"},\"exclusionary\":true}]"
                ),
            };

            foreach (var handlerType in _explanationHandlerTypes)
            {
                foreach (var (withFalse, withoutExclusionary) in equivalentPairs)
                {
                    Assert.AreEqual(NormalizeWith(handlerType, withFalse), NormalizeWith(handlerType, withoutExclusionary),
                        $"{handlerType.Name}: {withoutExclusionary}");
                }
            }
        }

        [TestMethod]
        public void NormalizeQuery_ExclusionaryTrueOrInvalid_StillDiffersFromMissing()
        {
            const string groupPart = "[{\"type\":\"GroupMembership\",\"source\":\"11111111-1111-1111-1111-111111111111\"}]";
            var changedGroupParts = new[]
            {
                "[{\"type\":\"GroupMembership\",\"source\":\"11111111-1111-1111-1111-111111111111\",\"exclusionary\":true}]",
                "[{\"type\":\"GroupMembership\",\"source\":\"11111111-1111-1111-1111-111111111111\",\"exclusionary\":null}]",
                "[{\"type\":\"GroupMembership\",\"source\":\"11111111-1111-1111-1111-111111111111\",\"exclusionary\":\"false\"}]",
            };

            foreach (var handlerType in _explanationHandlerTypes)
            {
                var original = NormalizeWith(handlerType, groupPart);
                foreach (var changedGroupPart in changedGroupParts)
                {
                    Assert.AreNotEqual(original, NormalizeWith(handlerType, changedGroupPart),
                        $"{handlerType.Name}: {changedGroupPart}");
                }
            }
        }

        [TestMethod]
        public void NormalizeQuery_TextThatIsNotAQueryArray_IsOnlyTrimmed()
        {
            foreach (var handlerType in _explanationHandlerTypes)
            {
                Assert.IsNull(NormalizeWith(handlerType, "   "), handlerType.Name);
                Assert.AreEqual("Q", NormalizeWith(handlerType, "  Q  "), handlerType.Name);
                Assert.AreEqual("[{\"type\":", NormalizeWith(handlerType, " [{\"type\": "), handlerType.Name);
                Assert.AreEqual("{\"type\":\"SqlMembership\"}", NormalizeWith(handlerType, " {\"type\":\"SqlMembership\"} "), handlerType.Name);
            }
        }

        [TestMethod]
        public void ComputeWalkBackFloor_FirstSaveAfterUsersOnlyMigration_IsNotAQueryBoundary()
        {
            var now = DateTime.UtcNow;
            var changes = new List<SyncJobChange>
            {
                ChangeWithSerializedQuery(now.AddHours(-1), SqlPartSavedByUiAfterMigration),
                ChangeWithSerializedQuery(now.AddHours(-10), SqlPartBeforeMigration),
            };
            var currentQueryNormalized = NormalizeWith(typeof(GetRunExplanationHandler), SqlPartSavedByUiAfterMigration);

            var floor = InvokeStaticPrivate<DateTime>(typeof(GetRunExplanationHandler), "ComputeWalkBackFloor", new object?[] { changes, currentQueryNormalized });

            Assert.AreEqual(
                now.AddHours(-10),
                floor,
                "The semantically equivalent pre-migration snapshot is the oldest retained evidence that the positional layout was stable.");
        }

        [TestMethod]
        public async Task ExecuteAsync_FirstSaveAfterUsersOnlyMigration_IsNotReportedAsARuleChange()
        {
            var prompt = await ExplainRunWithConfigChangesAsync(
                ChangeWithSerializedQuery(DateTime.UtcNow.AddMinutes(-30), SqlPartSavedByUiAfterMigration),
                ChangeWithSerializedQuery(DateTime.UtcNow.AddDays(-10), SqlPartBeforeMigration));

            Assert.IsFalse(prompt.Contains("Previous configuration:"),
                "A save that only writes out the users-only default must not be shown as a previous/current configuration change.");
        }

        [TestMethod]
        public async Task ExecuteAsync_SaveThatOnlyDropsExclusionaryFalse_IsNotReportedAsARuleChange()
        {
            var prompt = await ExplainRunWithConfigChangesAsync(
                ChangeWithSerializedQuery(DateTime.UtcNow.AddMinutes(-30), "[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Building = 'B40'\"}}]"),
                ChangeWithSerializedQuery(DateTime.UtcNow.AddDays(-10), "[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Building = 'B40'\"},\"exclusionary\":false}]"));

            Assert.IsFalse(prompt.Contains("Previous configuration:"),
                "A save that only drops \"exclusionary\":false must not be shown as a previous/current configuration change.");
        }

        [TestMethod]
        public async Task ExecuteAsync_ReorderedSelection_IsNotReportedAsARuleChange()
        {
            var prompt = await ExplainRunWithConfigChangesAsync(
                ChangeWithSerializedQuery(DateTime.UtcNow.AddMinutes(-30), "[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Building = 'B40'\"},\"identityTypes\":[\"users\",\"agents\"]}]"),
                ChangeWithSerializedQuery(DateTime.UtcNow.AddDays(-10), "[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Building = 'B40'\"},\"identityTypes\":[\"agents\",\"users\"]}]"));

            Assert.IsFalse(prompt.Contains("Previous configuration:"),
                "Saving the same identity types in another order must not be shown as a previous/current configuration change.");
        }

        [TestMethod]
        public async Task ExecuteAsync_FirstSaveAfterUsersOnlyMigration_DiffsAgainstTheLastRealRuleChange()
        {
            var prompt = await ExplainRunWithConfigChangesAsync(
                DateTime.UtcNow.AddDays(-40),
                ChangeWithSerializedQuery(DateTime.UtcNow.AddMinutes(-30), SqlPartSavedByUiAfterMigration),
                ChangeWithSerializedQuery(DateTime.UtcNow.AddDays(-10), SqlPartBeforeMigration),
                ChangeWithSerializedQuery(DateTime.UtcNow.AddDays(-30), "[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Building = 'B40'\"},\"exclusionary\":true}]"),
                ChangeWithSerializedQuery(DateTime.UtcNow.AddDays(-50), "[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Building = 'B40'\"},\"exclusionary\":true}]"));

            StringAssert.Contains(prompt, "Previous configuration:");
            StringAssert.Contains(prompt, "Membership criteria changed",
                "The diff must skip the save that only wrote out the default and compare with the real rule change before it.");
        }

        [TestMethod]
        public async Task ExecuteAsync_ScheduledNowQueryEdit_IsIncludedInConfigurationHistory()
        {
            var sourceGroupId = Guid.NewGuid();
            var scheduledNowChange = ChangeWithSerializedQuery(
                DateTime.UtcNow.AddMinutes(-30),
                $"[{{\"type\":\"GroupMembership\",\"source\":\"{sourceGroupId}\",\"identityTypes\":[\"agents\"]}}]");
            scheduledNowChange.ChangeReason =
                SyncJobChangeReason.ScheduledNow.ToString();

            var prompt = await ExplainRunWithConfigChangesAsync(
                scheduledNowChange,
                ChangeWithSerializedQuery(
                    DateTime.UtcNow.AddDays(-10),
                    $"[{{\"type\":\"GroupMembership\",\"source\":\"{sourceGroupId}\",\"identityTypes\":[\"users\"]}}]"));

            StringAssert.Contains(
                prompt,
                "Identity types changed from only people to only agent users");
        }

        [TestMethod]
        public async Task ExecuteAsync_NoOpScheduledNow_AttributesTheUnderlyingRuleChange()
        {
            var sourceGroupId = Guid.NewGuid();
            var currentQuery =
                $"[{{\"type\":\"GroupMembership\",\"source\":\"{sourceGroupId}\",\"identityTypes\":[\"agents\"]}}]";
            var previousQuery =
                $"[{{\"type\":\"GroupMembership\",\"source\":\"{sourceGroupId}\",\"identityTypes\":[\"users\"]}}]";
            var noOpScheduledNow = ChangeWithSerializedQuery(
                DateTime.UtcNow.AddDays(-1),
                currentQuery);
            noOpScheduledNow.ChangeReason = SyncJobChangeReason.ScheduledNow.ToString();
            noOpScheduledNow.ChangedByDisplayName = "Sync Presser";
            var ruleChangeTime = DateTime.UtcNow.AddDays(-2);
            var ruleChange = ChangeWithSerializedQuery(ruleChangeTime, currentQuery);
            ruleChange.ChangedByDisplayName = "Configuration Owner";

            var prompt = await ExplainRunWithConfigChangesAsync(
                DateTime.UtcNow.AddDays(-3),
                noOpScheduledNow,
                ruleChange,
                ChangeWithSerializedQuery(DateTime.UtcNow.AddDays(-10), previousQuery));

            StringAssert.Contains(
                prompt,
                $"Last configuration change on {ruleChangeTime:yyyy-MM-dd} by Configuration Owner (Update):");
            Assert.IsFalse(prompt.Contains("Sync Presser"));
        }

        private Task<string> ExplainRunWithConfigChangesAsync(
            params SyncJobChange[] changesNewestFirst) =>
            ExplainRunWithConfigChangesAsync(
                DateTime.UtcNow.AddHours(-1),
                changesNewestFirst);

        private async Task<string> ExplainRunWithConfigChangesAsync(
            DateTime previousRunUpdatedAt,
            params SyncJobChange[] changesNewestFirst)
        {
            _mockSyncJobHistoryRepository.Setup(x => x.GetByRunIdAsync(_runId))
                .ReturnsAsync(new global::Models.SyncJobHistory.SyncJobHistory
                {
                    SyncJobId = _syncJobId,
                    RunId = _runId,
                    Status = "Idle",
                    UpdatedAt = DateTime.UtcNow,
                    StartTime = DateTime.UtcNow.AddMinutes(-5),
                    EndTime = DateTime.UtcNow,
                    UsersAdded = 5,
                    UsersRemoved = 0,
                });
            _mockSyncJobHistoryRepository.Setup(x => x.GetBySyncJobIdAsync(_syncJobId, It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync(new List<global::Models.SyncJobHistory.SyncJobHistory>
                {
                    new()
                    {
                        RunId = Guid.NewGuid(),
                        Status = "Idle",
                        UpdatedByFunction = "GraphUpdater",
                        UpdatedAt = previousRunUpdatedAt
                    }
                });
            _mockSyncJobChangeRepository
                .Setup(x => x.GetRecentConfigChangesBySyncJobIdAsync(_syncJobId, It.IsAny<DateTime>(), It.IsAny<int>()))
                .ReturnsAsync(changesNewestFirst.ToList());

            string? capturedUserPrompt = null;
            _mockOpenAIService
                .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((systemPrompt, userPrompt) => capturedUserPrompt = userPrompt)
                .ReturnsAsync("This sync completed successfully.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedUserPrompt);
            StringAssert.Contains(capturedUserPrompt, "Last configuration change on");
            return capturedUserPrompt!;
        }

        private static string? NormalizeWith(Type handlerType, string? query) =>
            InvokeStaticPrivate<string?>(handlerType, "NormalizeQuery", new object?[] { query });

        private static SyncJobChange ChangeWithSerializedQuery(DateTime changeTime, string query) =>
            new()
            {
                Id = Guid.NewGuid(),
                ChangeTime = changeTime,
                ChangeReason = SyncJobChangeReason.Update.ToString(),
                ChangedByDisplayName = "owner@contoso.com",
                ChangeDetails = JsonSerializer.Serialize(new { query })
            };

        [TestMethod]
        public async Task ExecuteAsync_RemovesCurrentState_MultiPartInclusionary_OnePartReadFails_SuppressesColor()
        {
            // PR-comment #3: with multiple inclusionary SQL rules, if ANY part fails to read against the latest table we must NOT emit combined current-state color from the partial union — a removed user might still match the un-read part. A single read failure suppresses the whole current-state signal instead of risking a false "no longer matches" claim.
            var currentAdfRunId = Guid.NewGuid();
            var previousAdfRunId = Guid.NewGuid();
            var previousRunId = Guid.NewGuid();
            var currentTable = currentAdfRunId.ToString().Replace("-", string.Empty);
            var previousTable = previousAdfRunId.ToString().Replace("-", string.Empty);
            var latestRunId = Guid.NewGuid();
            var latestTable = latestRunId.ToString().Replace("-", string.Empty);
            var removedUser = Guid.NewGuid();
            string? capturedPrompt = null;

            _mockSyncJobRepository.Setup(x => x.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(new SyncJob
                {
                    Id = _syncJobId,
                    TargetOfficeGroupId = _targetGroupId,
                    Query = "[" +
                        "{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Building = 'B40'\"}}," +
                        "{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Department = 'Sales'\"}}" +
                        "]"
                });
            SetupCurrentRunAndPreviousRun(currentAdfRunId, previousAdfRunId, previousRunId, usersRemoved: 1);
            SetupAggregatedRemovedUsers(removedUser);
            // Exact-run and previous ADF tables pruned so single-step and walk-back hedge, making Phase 3 eligible.
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(currentTable)).ReturnsAsync(false);
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(previousTable)).ReturnsAsync(false);
            _mockAdfRunHistoryRepository.Setup(x => x.GetLatestAdfRunAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(new AdfRunHistory { AdfRunId = latestRunId });
            // Part 1 reads successfully (removed user absent -> would be "colored" if we trusted the partial union)...
            SetupSqlEvaluation(
                "Building = 'B40'",
                latestTable,
                IdentityTypeSet.UsersOnly,
                userObjectIds: new[] { Guid.NewGuid() });
            // ...but part 2 FAILS to read against the latest table, so no combined current-state signal is safe.
            _mockSqlMembershipRepository.Setup(x => x.EvaluateMembershipAsync(
                    It.Is<SqlMembershipEvaluationRequest>(request =>
                        MatchesSqlEvaluationRequest(
                            request,
                            "Department = 'Sales'",
                            latestTable,
                            IdentityTypeSet.UsersOnly))))
                .ThrowsAsync(new InvalidOperationException("transient read failure"));
            _mockOpenAIService.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((_, prompt) => capturedPrompt = prompt)
                .ReturnsAsync("This sync completed successfully.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedPrompt);
            Assert.IsFalse(capturedPrompt!.Contains("in the latest HR data (current state, not the removal-time snapshot)"),
                "A partial inclusionary-part read must not emit combined current-state color.");
            StringAssert.Contains(capturedPrompt!, "no attributable source found");
        }

        [TestMethod]
        public async Task ExecuteAsync_RemovesAttribution_WhenCurrentAndPreviousAdfRunMatchAndGroupBlobsUnavailable_SkipsAttribution()
        {
            var sharedAdfRunId = Guid.NewGuid();
            var previousRunId = Guid.NewGuid();
            var removedUser = Guid.NewGuid();
            var missingBlobGroup = Guid.NewGuid();
            var throwingBlobGroup = Guid.NewGuid();
            string? capturedPrompt = null;

            _mockSyncJobRepository.Setup(x => x.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(new SyncJob
                {
                    Id = _syncJobId,
                    TargetOfficeGroupId = _targetGroupId,
                    Query = $"[" +
                        "{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Building = 'B40'\"}}," +
                        $"{{\"type\":\"GroupMembership\",\"source\":\"{missingBlobGroup}\"}}," +
                        $"{{\"type\":\"GroupMembership\",\"source\":\"{throwingBlobGroup}\"}}" +
                        "]"
                });

            SetupCurrentRunAndPreviousRun(sharedAdfRunId, sharedAdfRunId, previousRunId, usersRemoved: 1);
            SetupAggregatedRemovedUsers(removedUser);
            _mockBlobStorageRepository.Setup(x => x.FindPartFilesByRunIdAsync(_targetGroupId.ToString(), _runId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult>
                {
                    ["GroupMembership_2"] = new() { BlobStatus = BlobStatus.Found, Path = "current/missing-previous.json" },
                    ["GroupMembership_3"] = new() { BlobStatus = BlobStatus.Found, Path = "current/throws.json" }
                });
            _mockBlobStorageRepository.Setup(x => x.FindPartFilesByRunIdAsync(_targetGroupId.ToString(), previousRunId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult>
                {
                    ["GroupMembership_2"] = new() { BlobStatus = BlobStatus.NotFound },
                    ["GroupMembership_3"] = new() { BlobStatus = BlobStatus.Found, Path = "previous/throws.json" }
                });
            _mockBlobStorageRepository.Setup(x => x.ExtractGroupMembershipSourceMembersAsync("current/throws.json"))
                .ThrowsAsync(new InvalidOperationException("blob read failed"));
            _mockOpenAIService.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((_, prompt) => capturedPrompt = prompt)
                .ReturnsAsync("This sync completed successfully.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            _mockOpenAIService.Verify(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
            Assert.IsNotNull(capturedPrompt);
            // Section IS present but as a no-signal marker — anti-hallucination enforcement.
            StringAssert.Contains(capturedPrompt!, "Per-part attribution for removed users");
            StringAssert.Contains(capturedPrompt!, "no attributable source found");
            _mockSqlMembershipRepository.Verify(
                x => x.EvaluateMembershipAsync(It.Is<SqlMembershipEvaluationRequest>(request =>
                    request.Filter == "Building = 'B40'")),
                Times.Never);
        }

        [TestMethod]
        public async Task ExecuteAsync_SameSourceIndependentSelections_MapPreviousBlobsByPart()
        {
            var currentAdfRunId = Guid.NewGuid();
            var previousAdfRunId = Guid.NewGuid();
            var previousRunId = Guid.NewGuid();
            var sourceGroupId = Guid.NewGuid();
            var removedUser = Guid.NewGuid();
            var unchangedAgent = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var previousQuery =
                $"[" +
                $"{{\"type\":\"GroupMembership\",\"source\":\"{sourceGroupId}\",\"identityTypes\":[\"users\"]}}," +
                $"{{\"type\":\"GroupMembership\",\"source\":\"{sourceGroupId}\",\"identityTypes\":[\"agents\"]}}" +
                "]";
            var currentQuery =
                $"[" +
                $"{{\"type\":\"GroupMembership\",\"source\":\"{sourceGroupId}\",\"identityTypes\":[\"agents\"]}}," +
                $"{{\"type\":\"GroupMembership\",\"source\":\"{sourceGroupId}\",\"identityTypes\":[\"users\"]}}" +
                "]";
            string? capturedPrompt = null;

            _mockSyncJobRepository.Setup(x => x.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(new SyncJob
                {
                    Id = _syncJobId,
                    TargetOfficeGroupId = _targetGroupId,
                    Query = currentQuery
                });
            SetupCurrentRunAndPreviousRun(
                currentAdfRunId,
                previousAdfRunId,
                previousRunId,
                usersRemoved: 1);
            SetupAggregatedRemovedUsers(removedUser);
            _mockSyncJobChangeRepository
                .Setup(repository =>
                    repository.GetRecentConfigChangesBySyncJobIdAsync(
                        _syncJobId,
                        It.IsAny<DateTime>(),
                        It.IsAny<int>()))
                .ReturnsAsync(new List<SyncJobChange>
                {
                    ChangeWithSerializedQuery(
                        now.AddMinutes(-10),
                        currentQuery),
                    ChangeWithSerializedQuery(
                        now.AddMinutes(-70),
                        previousQuery)
                });
            _mockBlobStorageRepository.Setup(repository =>
                    repository.FindPartFilesByRunIdAsync(
                        _targetGroupId.ToString(),
                        _runId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult>
                {
                    ["GroupMembership_1"] = new()
                    {
                        BlobStatus = BlobStatus.Found,
                        Path = "current/agents.json"
                    },
                    ["GroupMembership_2"] = new()
                    {
                        BlobStatus = BlobStatus.Found,
                        Path = "current/users.json"
                    }
                });
            _mockBlobStorageRepository.Setup(repository =>
                    repository.FindPartFilesByRunIdAsync(
                        _targetGroupId.ToString(),
                        previousRunId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult>
                {
                    ["GroupMembership_1"] = new()
                    {
                        BlobStatus = BlobStatus.Found,
                        Path = "previous/users.json"
                    },
                    ["GroupMembership_2"] = new()
                    {
                        BlobStatus = BlobStatus.Found,
                        Path = "previous/agents.json"
                    }
                });
            _mockBlobStorageRepository.Setup(repository =>
                    repository.ExtractGroupMembershipSourceMembersAsync(
                        "current/agents.json"))
                .ReturnsAsync(new HashSet<Guid> { unchangedAgent });
            _mockBlobStorageRepository.Setup(repository =>
                    repository.ExtractGroupMembershipSourceMembersAsync(
                        "previous/agents.json"))
                .ReturnsAsync(new HashSet<Guid> { unchangedAgent });
            _mockBlobStorageRepository.Setup(repository =>
                    repository.ExtractGroupMembershipSourceMembersAsync(
                        "current/users.json"))
                .ReturnsAsync(new HashSet<Guid>());
            _mockBlobStorageRepository.Setup(repository =>
                    repository.ExtractGroupMembershipSourceMembersAsync(
                        "previous/users.json"))
                .ReturnsAsync(new HashSet<Guid> { removedUser });
            _mockGraphGroupRepository.Setup(repository =>
                    repository.GetGroupNamesAsync(It.IsAny<List<Guid>>()))
                .ReturnsAsync(new Dictionary<Guid, string>
                {
                    [sourceGroupId] = "Shared Source"
                });
            _mockOpenAIService.Setup(service =>
                    service.GetCompletionAsync(
                        It.IsAny<string>(),
                        It.IsAny<string>()))
                .Callback<string, string>((_, prompt) =>
                    capturedPrompt = prompt)
                .ReturnsAsync("This sync completed successfully.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedPrompt);
            StringAssert.Contains(
                capturedPrompt!,
                "only people");
            StringAssert.Contains(
                capturedPrompt!,
                "stopped contributing through this rule");
            Assert.IsFalse(
                capturedPrompt!.Contains("no attributable source found"),
                "The people-only part should compare with its own previous blob.");
        }

        [TestMethod]
        public async Task ExecuteAsync_InsertedSameSourceRule_DoesNotReadAnotherRulesPreviousBlob()
        {
            var currentAdfRunId = Guid.NewGuid();
            var previousAdfRunId = Guid.NewGuid();
            var previousRunId = Guid.NewGuid();
            var sourceGroupId = Guid.NewGuid();
            var removedAgent = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var previousQuery =
                $"[{{\"type\":\"GroupMembership\",\"source\":\"{sourceGroupId}\",\"identityTypes\":[\"agents\"]}}]";
            var currentQuery =
                $"[" +
                $"{{\"type\":\"GroupMembership\",\"source\":\"{sourceGroupId}\",\"identityTypes\":[\"users\"]}}," +
                $"{{\"type\":\"GroupMembership\",\"source\":\"{sourceGroupId}\",\"identityTypes\":[\"agents\"]}}" +
                "]";
            string? capturedPrompt = null;

            _mockSyncJobRepository.Setup(repository => repository.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(new SyncJob
                {
                    Id = _syncJobId,
                    TargetOfficeGroupId = _targetGroupId,
                    Query = currentQuery
                });
            SetupCurrentRunAndPreviousRun(
                currentAdfRunId,
                previousAdfRunId,
                previousRunId,
                usersRemoved: 1);
            SetupAggregatedRemovedUsers(removedAgent);
            _mockSyncJobChangeRepository
                .Setup(repository =>
                    repository.GetRecentConfigChangesBySyncJobIdAsync(
                        _syncJobId,
                        It.IsAny<DateTime>(),
                        It.IsAny<int>()))
                .ReturnsAsync(new List<SyncJobChange>
                {
                    ChangeWithSerializedQuery(
                        now.AddMinutes(-10),
                        currentQuery),
                    ChangeWithSerializedQuery(
                        now.AddMinutes(-70),
                        previousQuery)
                });
            _mockBlobStorageRepository.Setup(repository =>
                    repository.FindPartFilesByRunIdAsync(
                        _targetGroupId.ToString(),
                        _runId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult>
                {
                    ["GroupMembership_1"] = new()
                    {
                        BlobStatus = BlobStatus.Found,
                        Path = "current/users.json"
                    },
                    ["GroupMembership_2"] = new()
                    {
                        BlobStatus = BlobStatus.Found,
                        Path = "current/agents.json"
                    }
                });
            _mockBlobStorageRepository.Setup(repository =>
                    repository.FindPartFilesByRunIdAsync(
                        _targetGroupId.ToString(),
                        previousRunId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult>
                {
                    ["GroupMembership_1"] = new()
                    {
                        BlobStatus = BlobStatus.Found,
                        Path = "previous/agents.json"
                    }
                });
            _mockBlobStorageRepository.Setup(repository =>
                    repository.ExtractGroupMembershipSourceMembersAsync(
                        "current/users.json"))
                .ReturnsAsync(new HashSet<Guid>());
            _mockBlobStorageRepository.Setup(repository =>
                    repository.ExtractGroupMembershipSourceMembersAsync(
                        "current/agents.json"))
                .ReturnsAsync(new HashSet<Guid>());
            _mockBlobStorageRepository.Setup(repository =>
                    repository.ExtractGroupMembershipSourceMembersAsync(
                        "previous/agents.json"))
                .ReturnsAsync(new HashSet<Guid> { removedAgent });
            _mockGraphGroupRepository.Setup(repository =>
                    repository.GetGroupNamesAsync(It.IsAny<List<Guid>>()))
                .ReturnsAsync(new Dictionary<Guid, string>
                {
                    [sourceGroupId] = "Shared Source"
                });
            _mockOpenAIService.Setup(service =>
                    service.GetCompletionAsync(
                        It.IsAny<string>(),
                        It.IsAny<string>()))
                .Callback<string, string>((_, prompt) =>
                    capturedPrompt = prompt)
                .ReturnsAsync("This sync completed successfully.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedPrompt);
            StringAssert.Contains(
                capturedPrompt!,
                $"- Inclusionary GroupMembership part #2 ('Shared Source' ({sourceGroupId}); only agent users): all of the removed users stopped contributing through this rule");
            Assert.IsFalse(
                capturedPrompt!.Contains(
                    $"- Inclusionary GroupMembership part #1 ('Shared Source' ({sourceGroupId}); only people):",
                    StringComparison.Ordinal),
                "An inserted rule must not compare against another same-source rule's previous positional blob.");
            _mockBlobStorageRepository.Verify(
                repository => repository.ExtractGroupMembershipSourceMembersAsync(
                    "previous/agents.json"),
                Times.Once);
        }

        [DataTestMethod]
        [DataRow("ThresholdExceeded", "GroupMembershipObtainer", true)]
        [DataRow("Error", "MembershipAggregator", true)]
        [DataRow("MembershipDataNotFound", "GroupMembershipObtainer", true)]
        [DataRow("Idle", "GraphUpdater", false)]
        [DataRow("Idle", "MembershipAggregator", false)]
        public async Task ExecuteAsync_RuleRemovedBeforePreviousRun_EmitsDeletedRuleSignalOnlyWhenPreviousRunDidNotApplyMembership(
            string previousStatus,
            string previousUpdatedByFunction,
            bool expectsDeletedRuleSignal)
        {
            var currentAdfRunId = Guid.NewGuid();
            var previousAdfRunId = Guid.NewGuid();
            var previousRunId = Guid.NewGuid();
            var deletedSourceGroupId = Guid.NewGuid();
            var retainedSourceGroupId = Guid.NewGuid();
            var removedUser = Guid.NewGuid();
            var retainedUser = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var currentQuery =
                $"[{{\"type\":\"GroupMembership\",\"source\":\"{retainedSourceGroupId}\"}}]";
            var historicalQuery =
                $"[" +
                $"{{\"type\":\"GroupMembership\",\"source\":\"{deletedSourceGroupId}\"}}," +
                $"{{\"type\":\"GroupMembership\",\"source\":\"{retainedSourceGroupId}\"}}" +
                "]";
            string? capturedPrompt = null;

            _mockSyncJobRepository.Setup(repository => repository.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(new SyncJob
                {
                    Id = _syncJobId,
                    TargetOfficeGroupId = _targetGroupId,
                    Query = currentQuery
                });
            SetupCurrentRunAndPreviousRun(
                currentAdfRunId,
                previousAdfRunId,
                previousRunId,
                usersRemoved: 1,
                previousStatus: previousStatus,
                previousUpdatedByFunction: previousUpdatedByFunction);
            if (expectsDeletedRuleSignal)
            {
                var histories = new List<global::Models.SyncJobHistory.SyncJobHistory>
                {
                    new()
                    {
                        SyncJobId = _syncJobId,
                        RunId = previousRunId,
                        Status = previousStatus,
                        UpdatedByFunction = "GroupMembershipObtainer",
                        UpdatedAt = now.AddHours(-1),
                        EndTime = now.AddHours(-1),
                        AdfRunId = previousAdfRunId
                    }
                };
                if (string.Equals(
                    previousStatus,
                    "ThresholdExceeded",
                    StringComparison.OrdinalIgnoreCase))
                {
                    histories.Add(new()
                    {
                        SyncJobId = _syncJobId,
                        RunId = Guid.NewGuid(),
                        Status = "Error",
                        UpdatedByFunction = "MembershipAggregator",
                        UpdatedAt = now.AddMinutes(-75),
                        EndTime = now.AddMinutes(-75),
                        AdfRunId = Guid.NewGuid()
                    });
                }

                histories.Add(new()
                {
                    SyncJobId = _syncJobId,
                    RunId = Guid.NewGuid(),
                    Status = "Idle",
                    UpdatedByFunction = "GraphUpdater",
                    UpdatedAt = now.AddMinutes(-90),
                    EndTime = now.AddMinutes(-90),
                    AdfRunId = Guid.NewGuid()
                });
                _mockSyncJobHistoryRepository
                    .Setup(repository =>
                        repository.GetBySyncJobIdAsync(
                            _syncJobId,
                            It.IsAny<int>(),
                            It.IsAny<int>()))
                    .ReturnsAsync(histories);
            }
            SetupAggregatedRemovedUsers(removedUser);
            _mockSyncJobChangeRepository
                .Setup(repository =>
                    repository.GetRecentConfigChangesBySyncJobIdAsync(
                        _syncJobId,
                        It.IsAny<DateTime>(),
                        It.IsAny<int>()))
                .ReturnsAsync(new List<SyncJobChange>
                {
                    ChangeWithSerializedQuery(
                        now.AddMinutes(-10),
                        currentQuery),
                    ChangeWithSerializedQuery(
                        now.AddMinutes(-70),
                        currentQuery),
                    ChangeWithSerializedQuery(
                        now.AddMinutes(-120),
                        historicalQuery)
                });
            _mockBlobStorageRepository.Setup(repository =>
                    repository.FindPartFilesByRunIdAsync(
                        _targetGroupId.ToString(),
                        _runId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult>
                {
                    ["GroupMembership_1"] = new()
                    {
                        BlobStatus = BlobStatus.Found,
                        Path = "current/retained.json"
                    }
                });
            _mockBlobStorageRepository.Setup(repository =>
                    repository.FindPartFilesByRunIdAsync(
                        _targetGroupId.ToString(),
                        previousRunId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult>
                {
                    ["GroupMembership_1"] = new()
                    {
                        BlobStatus = BlobStatus.Found,
                        Path = "previous/retained.json"
                    }
                });
            _mockBlobStorageRepository.Setup(repository =>
                    repository.ExtractGroupMembershipSourceMembersAsync(
                        "current/retained.json"))
                .ReturnsAsync(new HashSet<Guid> { retainedUser });
            _mockBlobStorageRepository.Setup(repository =>
                    repository.ExtractGroupMembershipSourceMembersAsync(
                        "previous/retained.json"))
                .ReturnsAsync(new HashSet<Guid> { retainedUser });
            _mockGraphGroupRepository.Setup(repository =>
                    repository.GetGroupNamesAsync(It.IsAny<List<Guid>>()))
                .ReturnsAsync(new Dictionary<Guid, string>
                {
                    [deletedSourceGroupId] = "Deleted Source",
                    [retainedSourceGroupId] = "Retained Source"
                });
            _mockOpenAIService.Setup(service =>
                    service.GetCompletionAsync(
                        It.IsAny<string>(),
                        It.IsAny<string>()))
                .Callback<string, string>((_, prompt) =>
                    capturedPrompt = prompt)
                .ReturnsAsync("This sync completed successfully.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedPrompt);
            if (expectsDeletedRuleSignal)
            {
                StringAssert.Contains(
                    capturedPrompt!,
                    "Rule removed from the query in the recent config update");
                StringAssert.Contains(
                    capturedPrompt,
                    "Previous configuration:");
                StringAssert.Contains(
                    capturedPrompt,
                    "What changed:");
                Assert.IsFalse(
                    capturedPrompt.Contains(
                        "configuration is unchanged for THIS sync",
                        StringComparison.Ordinal));
                StringAssert.Contains(
                    capturedPrompt!,
                    "Deleted Source");
                StringAssert.Contains(
                    capturedPrompt!,
                    "per-part membership counts are unavailable");
            }
            else
            {
                Assert.IsFalse(
                    capturedPrompt!.Contains(
                        "Rule removed from the query",
                        StringComparison.Ordinal),
                    capturedPrompt);
            }
            _mockBlobStorageRepository.Verify(
                repository => repository.ExtractGroupMembershipSourceMembersAsync(
                    "previous/retained.json"),
                Times.AtLeastOnce);
        }

        [TestMethod]
        public async Task ExecuteAsync_RuleDeletedThenRestoredWithDifferentSelection_DoesNotEmitDeletedRuleSignal()
        {
            var currentAdfRunId = Guid.NewGuid();
            var previousAdfRunId = Guid.NewGuid();
            var previousRunId = Guid.NewGuid();
            var restoredSourceGroupId = Guid.NewGuid();
            var retainedSourceGroupId = Guid.NewGuid();
            var removedUser = Guid.NewGuid();
            var retainedUser = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var currentQuery =
                $"[" +
                $"{{\"type\":\"GroupMembership\",\"source\":\"{restoredSourceGroupId}\",\"identityTypes\":[\"agents\"]}}," +
                $"{{\"type\":\"GroupMembership\",\"source\":\"{retainedSourceGroupId}\",\"identityTypes\":[\"users\"]}}" +
                "]";
            var restoredPeopleQuery =
                $"[" +
                $"{{\"type\":\"GroupMembership\",\"source\":\"{restoredSourceGroupId}\",\"identityTypes\":[\"users\"]}}," +
                $"{{\"type\":\"GroupMembership\",\"source\":\"{retainedSourceGroupId}\",\"identityTypes\":[\"users\"]}}" +
                "]";
            var deletedQuery =
                $"[{{\"type\":\"GroupMembership\",\"source\":\"{retainedSourceGroupId}\",\"identityTypes\":[\"users\"]}}]";
            string? capturedPrompt = null;

            _mockSyncJobRepository.Setup(repository => repository.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(new SyncJob
                {
                    Id = _syncJobId,
                    TargetOfficeGroupId = _targetGroupId,
                    Query = currentQuery
                });
            SetupCurrentRunAndPreviousRun(
                currentAdfRunId,
                previousAdfRunId,
                previousRunId,
                usersRemoved: 1,
                previousStatus: "ThresholdExceeded");
            SetupAggregatedRemovedUsers(removedUser);
            _mockSyncJobChangeRepository
                .Setup(repository =>
                    repository.GetRecentConfigChangesBySyncJobIdAsync(
                        _syncJobId,
                        It.IsAny<DateTime>(),
                        It.IsAny<int>()))
                .ReturnsAsync(new List<SyncJobChange>
                {
                    ChangeWithSerializedQuery(now.AddMinutes(-10), currentQuery),
                    ChangeWithSerializedQuery(now.AddMinutes(-70), restoredPeopleQuery),
                    ChangeWithSerializedQuery(now.AddMinutes(-90), deletedQuery),
                    ChangeWithSerializedQuery(now.AddMinutes(-120), restoredPeopleQuery)
                });
            _mockBlobStorageRepository.Setup(repository =>
                    repository.FindPartFilesByRunIdAsync(
                        _targetGroupId.ToString(),
                        _runId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult>
                {
                    ["GroupMembership_1"] = new()
                    {
                        BlobStatus = BlobStatus.Found,
                        Path = "current/restored.json"
                    },
                    ["GroupMembership_2"] = new()
                    {
                        BlobStatus = BlobStatus.Found,
                        Path = "current/retained.json"
                    }
                });
            _mockBlobStorageRepository.Setup(repository =>
                    repository.FindPartFilesByRunIdAsync(
                        _targetGroupId.ToString(),
                        previousRunId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult>
                {
                    ["GroupMembership_1"] = new()
                    {
                        BlobStatus = BlobStatus.Found,
                        Path = "previous/restored.json"
                    },
                    ["GroupMembership_2"] = new()
                    {
                        BlobStatus = BlobStatus.Found,
                        Path = "previous/retained.json"
                    }
                });
            _mockBlobStorageRepository.Setup(repository =>
                    repository.ExtractGroupMembershipSourceMembersAsync(
                        It.Is<string>(path => path.EndsWith("restored.json", StringComparison.Ordinal))))
                .ReturnsAsync(new HashSet<Guid>());
            _mockBlobStorageRepository.Setup(repository =>
                    repository.ExtractGroupMembershipSourceMembersAsync(
                        It.Is<string>(path => path.EndsWith("retained.json", StringComparison.Ordinal))))
                .ReturnsAsync(new HashSet<Guid> { retainedUser });
            _mockGraphGroupRepository.Setup(repository =>
                    repository.GetGroupNamesAsync(It.IsAny<List<Guid>>()))
                .ReturnsAsync(new Dictionary<Guid, string>
                {
                    [restoredSourceGroupId] = "Restored Source",
                    [retainedSourceGroupId] = "Retained Source"
                });
            _mockOpenAIService.Setup(service =>
                    service.GetCompletionAsync(
                        It.IsAny<string>(),
                        It.IsAny<string>()))
                .Callback<string, string>((_, prompt) =>
                    capturedPrompt = prompt)
                .ReturnsAsync("This sync completed successfully.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedPrompt);
            Assert.IsFalse(
                capturedPrompt!.Contains(
                    "Rule removed from the query",
                    StringComparison.Ordinal));
            StringAssert.Contains(
                capturedPrompt,
                "Identity types changed from only people to only agent users");
        }

        [TestMethod]
        public async Task ExecuteAsync_ExclusionaryRuleDeletedThenRestoredWithDifferentSelection_DoesNotEmitDeletedRuleSignal()
        {
            var currentAdfRunId = Guid.NewGuid();
            var previousAdfRunId = Guid.NewGuid();
            var previousRunId = Guid.NewGuid();
            var retainedSourceGroupId = Guid.NewGuid();
            var restoredSourceGroupId = Guid.NewGuid();
            var addedUser = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var currentQuery =
                $"[" +
                $"{{\"type\":\"GroupMembership\",\"source\":\"{retainedSourceGroupId}\",\"identityTypes\":[\"users\"]}}," +
                $"{{\"type\":\"GroupMembership\",\"source\":\"{restoredSourceGroupId}\",\"identityTypes\":[\"agents\"],\"exclusionary\":true}}" +
                "]";
            var restoredPeopleQuery =
                $"[" +
                $"{{\"type\":\"GroupMembership\",\"source\":\"{retainedSourceGroupId}\",\"identityTypes\":[\"users\"]}}," +
                $"{{\"type\":\"GroupMembership\",\"source\":\"{restoredSourceGroupId}\",\"identityTypes\":[\"users\"],\"exclusionary\":true}}" +
                "]";
            var deletedQuery =
                $"[{{\"type\":\"GroupMembership\",\"source\":\"{retainedSourceGroupId}\",\"identityTypes\":[\"users\"]}}]";
            string? capturedPrompt = null;

            _mockSyncJobRepository.Setup(repository =>
                    repository.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(new SyncJob
                {
                    Id = _syncJobId,
                    TargetOfficeGroupId = _targetGroupId,
                    Query = currentQuery
                });
            SetupCurrentRunAndPreviousRun(
                currentAdfRunId,
                previousAdfRunId,
                previousRunId,
                usersRemoved: 0,
                usersAdded: 1,
                previousStatus: "ThresholdExceeded");
            SetupAggregatedMembers(MembershipAction.Add, addedUser);
            _mockSyncJobChangeRepository
                .Setup(repository =>
                    repository.GetRecentConfigChangesBySyncJobIdAsync(
                        _syncJobId,
                        It.IsAny<DateTime>(),
                        It.IsAny<int>()))
                .ReturnsAsync(new List<SyncJobChange>
                {
                    ChangeWithSerializedQuery(now.AddMinutes(-10), currentQuery),
                    ChangeWithSerializedQuery(now.AddMinutes(-70), restoredPeopleQuery),
                    ChangeWithSerializedQuery(now.AddMinutes(-90), deletedQuery),
                    ChangeWithSerializedQuery(now.AddMinutes(-120), restoredPeopleQuery)
                });
            _mockBlobStorageRepository.Setup(repository =>
                    repository.FindPartFilesByRunIdAsync(
                        _targetGroupId.ToString(),
                        _runId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult>
                {
                    ["GroupMembership_1"] = new()
                    {
                        BlobStatus = BlobStatus.Found,
                        Path = "current/retained.json"
                    },
                    ["GroupMembership_2"] = new()
                    {
                        BlobStatus = BlobStatus.Found,
                        Path = "current/restored-exclusionary.json"
                    }
                });
            _mockBlobStorageRepository.Setup(repository =>
                    repository.FindPartFilesByRunIdAsync(
                        _targetGroupId.ToString(),
                        previousRunId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult>
                {
                    ["GroupMembership_1"] = new()
                    {
                        BlobStatus = BlobStatus.Found,
                        Path = "previous/retained.json"
                    },
                    ["GroupMembership_2"] = new()
                    {
                        BlobStatus = BlobStatus.Found,
                        Path = "previous/restored-exclusionary.json"
                    }
                });
            _mockBlobStorageRepository.Setup(repository =>
                    repository.ExtractGroupMembershipSourceMembersAsync(
                        "current/retained.json"))
                .ReturnsAsync(new HashSet<Guid> { addedUser });
            _mockBlobStorageRepository.Setup(repository =>
                    repository.ExtractGroupMembershipSourceMembersAsync(
                        "previous/retained.json"))
                .ReturnsAsync(new HashSet<Guid>());
            _mockBlobStorageRepository.Setup(repository =>
                    repository.ExtractGroupMembershipSourceMembersAsync(
                        It.Is<string>(path =>
                            path.EndsWith(
                                "restored-exclusionary.json",
                                StringComparison.Ordinal))))
                .ReturnsAsync(new HashSet<Guid>());
            _mockGraphGroupRepository.Setup(repository =>
                    repository.GetGroupNamesAsync(It.IsAny<List<Guid>>()))
                .ReturnsAsync(new Dictionary<Guid, string>
                {
                    [retainedSourceGroupId] = "Retained Source",
                    [restoredSourceGroupId] = "Restored Exclusionary Source"
                });
            _mockOpenAIService.Setup(service =>
                    service.GetCompletionAsync(
                        It.IsAny<string>(),
                        It.IsAny<string>()))
                .Callback<string, string>((_, prompt) =>
                    capturedPrompt = prompt)
                .ReturnsAsync("This sync completed successfully.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedPrompt);
            Assert.IsFalse(
                capturedPrompt!.Contains(
                    "Exclusionary rule removed from the query",
                    StringComparison.Ordinal),
                capturedPrompt);
            StringAssert.Contains(
                capturedPrompt,
                "Identity types changed from only people to only agent users");
        }

        [TestMethod]
        public async Task ExecuteAsync_IdentitySelectionChanges_DoNotEmitDeletedRuleSignal()
        {
            var currentAdfRunId = Guid.NewGuid();
            var previousAdfRunId = Guid.NewGuid();
            var previousRunId = Guid.NewGuid();
            var sourceGroupId = Guid.NewGuid();
            var removedUser = Guid.NewGuid();
            var retainedUser = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var usersQuery =
                $"[{{\"type\":\"GroupMembership\",\"source\":\"{sourceGroupId}\",\"identityTypes\":[\"users\"]}}]";
            var agentsQuery =
                $"[{{\"type\":\"GroupMembership\",\"source\":\"{sourceGroupId}\",\"identityTypes\":[\"agents\"]}}]";
            var bothQuery =
                $"[{{\"type\":\"GroupMembership\",\"source\":\"{sourceGroupId}\",\"identityTypes\":[\"users\",\"agents\"]}}]";
            string? capturedPrompt = null;

            _mockSyncJobRepository.Setup(repository =>
                    repository.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(new SyncJob
                {
                    Id = _syncJobId,
                    TargetOfficeGroupId = _targetGroupId,
                    Query = bothQuery
                });
            SetupCurrentRunAndPreviousRun(
                currentAdfRunId,
                previousAdfRunId,
                previousRunId,
                usersRemoved: 1);
            SetupAggregatedRemovedUsers(removedUser);
            _mockSyncJobChangeRepository
                .Setup(repository =>
                    repository.GetRecentConfigChangesBySyncJobIdAsync(
                        _syncJobId,
                        It.IsAny<DateTime>(),
                        It.IsAny<int>()))
                .ReturnsAsync(new List<SyncJobChange>
                {
                    ChangeWithSerializedQuery(
                        now.AddMinutes(-10),
                        bothQuery),
                    ChangeWithSerializedQuery(
                        now.AddMinutes(-70),
                        agentsQuery),
                    ChangeWithSerializedQuery(
                        now.AddMinutes(-120),
                        usersQuery)
                });
            _mockBlobStorageRepository.Setup(repository =>
                    repository.FindPartFilesByRunIdAsync(
                        _targetGroupId.ToString(),
                        _runId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult>
                {
                    ["GroupMembership_1"] = new()
                    {
                        BlobStatus = BlobStatus.Found,
                        Path = "current/source.json"
                    }
                });
            _mockBlobStorageRepository.Setup(repository =>
                    repository.FindPartFilesByRunIdAsync(
                        _targetGroupId.ToString(),
                        previousRunId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult>
                {
                    ["GroupMembership_1"] = new()
                    {
                        BlobStatus = BlobStatus.Found,
                        Path = "previous/source.json"
                    }
                });
            _mockBlobStorageRepository.Setup(repository =>
                    repository.ExtractGroupMembershipSourceMembersAsync(
                        "current/source.json"))
                .ReturnsAsync(new HashSet<Guid> { retainedUser });
            _mockBlobStorageRepository.Setup(repository =>
                    repository.ExtractGroupMembershipSourceMembersAsync(
                        "previous/source.json"))
                .ReturnsAsync(new HashSet<Guid> { retainedUser });
            _mockGraphGroupRepository.Setup(repository =>
                    repository.GetGroupNamesAsync(It.IsAny<List<Guid>>()))
                .ReturnsAsync(new Dictionary<Guid, string>
                {
                    [sourceGroupId] = "Retained Source"
                });
            _mockOpenAIService.Setup(service =>
                    service.GetCompletionAsync(
                        It.IsAny<string>(),
                        It.IsAny<string>()))
                .Callback<string, string>((_, prompt) =>
                    capturedPrompt = prompt)
                .ReturnsAsync("This sync completed successfully.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedPrompt);
            Assert.IsFalse(
                capturedPrompt!.Contains(
                    "Rule removed from the query",
                    StringComparison.Ordinal));
            StringAssert.Contains(
                capturedPrompt!,
                "Identity types changed from");
        }

        [TestMethod]
        public async Task ExecuteAsync_SelectionChangedSincePreviousRun_DoesNotCompareDifferentSelectionsByBlobPosition()
        {
            var currentAdfRunId = Guid.NewGuid();
            var previousAdfRunId = Guid.NewGuid();
            var previousRunId = Guid.NewGuid();
            var sourceGroupId = Guid.NewGuid();
            var removedAgent = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var currentQuery =
                $"[{{\"type\":\"GroupMembership\",\"source\":\"{sourceGroupId}\",\"identityTypes\":[\"users\"]}}]";
            var previousQuery =
                $"[{{\"type\":\"GroupMembership\",\"source\":\"{sourceGroupId}\",\"identityTypes\":[\"agents\"]}}]";
            string? capturedPrompt = null;

            _mockSyncJobRepository.Setup(repository =>
                    repository.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(new SyncJob
                {
                    Id = _syncJobId,
                    TargetOfficeGroupId = _targetGroupId,
                    Query = currentQuery
                });
            SetupCurrentRunAndPreviousRun(
                currentAdfRunId,
                previousAdfRunId,
                previousRunId,
                usersRemoved: 1);
            SetupAggregatedRemovedUsers(removedAgent);
            _mockSyncJobChangeRepository
                .Setup(repository =>
                    repository.GetRecentConfigChangesBySyncJobIdAsync(
                        _syncJobId,
                        It.IsAny<DateTime>(),
                        It.IsAny<int>()))
                .ReturnsAsync(new List<SyncJobChange>
                {
                    ChangeWithSerializedQuery(now.AddMinutes(-10), currentQuery),
                    ChangeWithSerializedQuery(now.AddMinutes(-120), previousQuery)
                });
            _mockBlobStorageRepository.Setup(repository =>
                    repository.FindPartFilesByRunIdAsync(
                        _targetGroupId.ToString(),
                        _runId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult>
                {
                    ["GroupMembership_1"] = new()
                    {
                        BlobStatus = BlobStatus.Found,
                        Path = "current/people.json"
                    }
                });
            _mockBlobStorageRepository.Setup(repository =>
                    repository.FindPartFilesByRunIdAsync(
                        _targetGroupId.ToString(),
                        previousRunId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult>
                {
                    ["GroupMembership_1"] = new()
                    {
                        BlobStatus = BlobStatus.Found,
                        Path = "previous/agents.json"
                    }
                });
            _mockBlobStorageRepository.Setup(repository =>
                    repository.ExtractGroupMembershipSourceMembersAsync(
                        "current/people.json"))
                .ReturnsAsync(new HashSet<Guid>());
            _mockBlobStorageRepository.Setup(repository =>
                    repository.ExtractGroupMembershipSourceMembersAsync(
                        "previous/agents.json"))
                .ReturnsAsync(new HashSet<Guid> { removedAgent });
            _mockGraphGroupRepository.Setup(repository =>
                    repository.GetGroupNamesAsync(It.IsAny<List<Guid>>()))
                .ReturnsAsync(new Dictionary<Guid, string>
                {
                    [sourceGroupId] = "Selection-Changed Source"
                });
            _mockOpenAIService.Setup(service =>
                    service.GetCompletionAsync(
                        It.IsAny<string>(),
                        It.IsAny<string>()))
                .Callback<string, string>((_, prompt) =>
                    capturedPrompt = prompt)
                .ReturnsAsync("This sync completed successfully.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedPrompt);
            Assert.IsFalse(
                capturedPrompt!.Contains(
                    "stopped contributing through this rule",
                    StringComparison.Ordinal),
                capturedPrompt);
            StringAssert.Contains(
                capturedPrompt,
                "Identity types changed from only agent users to only people");
        }

        [TestMethod]
        public async Task ExecuteAsync_RemovesWalkBack_SameSourceSelectionReorder_DoesNotCrossLayoutBoundary()
        {
            var previousRunId = Guid.NewGuid();
            var olderRunId = Guid.NewGuid();
            var sourceGroupId = Guid.NewGuid();
            var removedPerson = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var currentQuery =
                $"[" +
                $"{{\"type\":\"GroupMembership\",\"source\":\"{sourceGroupId}\",\"identityTypes\":[\"agents\"]}}," +
                $"{{\"type\":\"GroupMembership\",\"source\":\"{sourceGroupId}\",\"identityTypes\":[\"users\"]}}" +
                "]";
            var previousLayoutQuery =
                $"[" +
                $"{{\"type\":\"GroupMembership\",\"source\":\"{sourceGroupId}\",\"identityTypes\":[\"users\"]}}," +
                $"{{\"type\":\"GroupMembership\",\"source\":\"{sourceGroupId}\",\"identityTypes\":[\"agents\"]}}" +
                "]";
            string? capturedPrompt = null;

            _mockSyncJobRepository.Setup(repository =>
                    repository.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(new SyncJob
                {
                    Id = _syncJobId,
                    TargetOfficeGroupId = _targetGroupId,
                    Query = currentQuery
                });
            SetupCurrentRunAndPreviousRun(
                Guid.NewGuid(),
                Guid.NewGuid(),
                previousRunId,
                usersRemoved: 1,
                previousStatus: "ThresholdExceeded");
            SetupAggregatedRemovedUsers(removedPerson);
            _mockSyncJobHistoryRepository
                .Setup(repository =>
                    repository.GetBySyncJobIdAsync(
                        _syncJobId,
                        It.IsAny<int>(),
                        It.IsAny<int>()))
                .ReturnsAsync(new List<global::Models.SyncJobHistory.SyncJobHistory>
                {
                    new()
                    {
                        SyncJobId = _syncJobId,
                        RunId = previousRunId,
                        Status = "ThresholdExceeded",
                        UpdatedByFunction = "GroupMembershipObtainer",
                        UpdatedAt = now.AddHours(-1),
                        EndTime = now.AddHours(-1)
                    },
                    new()
                    {
                        SyncJobId = _syncJobId,
                        RunId = olderRunId,
                        Status = "Idle",
                        UpdatedByFunction = "GraphUpdater",
                        StartTime = now.AddHours(-3),
                        UpdatedAt = now.AddMinutes(-90),
                        EndTime = now.AddMinutes(-90)
                    }
                });
            _mockSyncJobChangeRepository
                .Setup(repository =>
                    repository.GetRecentConfigChangesBySyncJobIdAsync(
                        _syncJobId,
                        It.IsAny<DateTime>(),
                        It.IsAny<int>()))
                .ReturnsAsync(new List<SyncJobChange>
                {
                    ChangeWithSerializedQuery(now.AddHours(-2), currentQuery),
                    ChangeWithSerializedQuery(
                        now.AddHours(-4),
                        previousLayoutQuery)
                });
            _mockBlobStorageRepository.Setup(repository =>
                    repository.FindPartFilesByRunIdAsync(
                        _targetGroupId.ToString(),
                        _runId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult>
                {
                    ["GroupMembership_1"] = new()
                    {
                        BlobStatus = BlobStatus.Found,
                        Path = "current/agents.json"
                    },
                    ["GroupMembership_2"] = new()
                    {
                        BlobStatus = BlobStatus.Found,
                        Path = "current/people.json"
                    }
                });
            _mockBlobStorageRepository.Setup(repository =>
                    repository.FindPartFilesByRunIdAsync(
                        _targetGroupId.ToString(),
                        previousRunId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult>
                {
                    ["GroupMembership_1"] = new()
                    {
                        BlobStatus = BlobStatus.Found,
                        Path = "previous/agents.json"
                    },
                    ["GroupMembership_2"] = new()
                    {
                        BlobStatus = BlobStatus.Found,
                        Path = "previous/people.json"
                    }
                });
            _mockBlobStorageRepository.Setup(repository =>
                    repository.FindPartFilesByRunIdAsync(
                        _targetGroupId.ToString(),
                        olderRunId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult>
                {
                    ["GroupMembership_1"] = new()
                    {
                        BlobStatus = BlobStatus.Found,
                        Path = "older/people-at-index-1.json"
                    }
                });
            _mockBlobStorageRepository.Setup(repository =>
                    repository.ExtractGroupMembershipSourceMembersAsync(
                        It.Is<string>(path =>
                            path.StartsWith(
                                "current/",
                                StringComparison.Ordinal)
                            || path.StartsWith(
                                "previous/",
                                StringComparison.Ordinal))))
                .ReturnsAsync(new HashSet<Guid>());
            _mockBlobStorageRepository.Setup(repository =>
                    repository.ExtractGroupMembershipSourceMembersAsync(
                        "older/people-at-index-1.json"))
                .ReturnsAsync(new HashSet<Guid> { removedPerson });
            _mockGraphGroupRepository.Setup(repository =>
                    repository.GetGroupNamesAsync(It.IsAny<List<Guid>>()))
                .ReturnsAsync(new Dictionary<Guid, string>
                {
                    [sourceGroupId] = "Reordered Source"
                });
            _mockOpenAIService.Setup(service =>
                    service.GetCompletionAsync(
                        It.IsAny<string>(),
                        It.IsAny<string>()))
                .Callback<string, string>((_, prompt) =>
                    capturedPrompt = prompt)
                .ReturnsAsync("This sync completed successfully.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedPrompt);
            Assert.IsFalse(
                capturedPrompt!.Contains(
                    "selected contribution was last present",
                    StringComparison.Ordinal),
                capturedPrompt);
            _mockBlobStorageRepository.Verify(repository =>
                    repository.ExtractGroupMembershipSourceMembersAsync(
                        "older/people-at-index-1.json"),
                Times.Never);
        }

        [TestMethod]
        public async Task ExecuteAsync_RoleFlipWithSameSelection_UsesPreviousBlobWhenCurrentBlobIsMissing()
        {
            var previousRunId = Guid.NewGuid();
            var sourceGroupId = Guid.NewGuid();
            var removedPerson = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var currentQuery =
                $"[{{\"type\":\"GroupMembership\",\"source\":\"{sourceGroupId}\",\"identityTypes\":[\"users\"],\"exclusionary\":true}}]";
            var appliedQuery =
                $"[{{\"type\":\"GroupMembership\",\"source\":\"{sourceGroupId}\",\"identityTypes\":[\"users\"]}}]";
            string? capturedPrompt = null;

            _mockSyncJobRepository.Setup(repository =>
                    repository.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(new SyncJob
                {
                    Id = _syncJobId,
                    TargetOfficeGroupId = _targetGroupId,
                    Query = currentQuery
                });
            SetupCurrentRunAndPreviousRun(
                Guid.NewGuid(),
                Guid.NewGuid(),
                previousRunId,
                usersRemoved: 1,
                previousStatus: "ThresholdExceeded");
            SetupAggregatedRemovedUsers(removedPerson);
            _mockSyncJobChangeRepository
                .Setup(repository =>
                    repository.GetRecentConfigChangesBySyncJobIdAsync(
                        _syncJobId,
                        It.IsAny<DateTime>(),
                        It.IsAny<int>()))
                .ReturnsAsync(new List<SyncJobChange>
                {
                    ChangeWithSerializedQuery(now.AddMinutes(-10), currentQuery),
                    ChangeWithSerializedQuery(now.AddMinutes(-120), appliedQuery)
                });
            _mockBlobStorageRepository.Setup(repository =>
                    repository.FindPartFilesByRunIdAsync(
                        _targetGroupId.ToString(),
                        _runId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult>());
            _mockBlobStorageRepository.Setup(repository =>
                    repository.FindPartFilesByRunIdAsync(
                        _targetGroupId.ToString(),
                        previousRunId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult>
                {
                    ["GroupMembership_1"] = new()
                    {
                        BlobStatus = BlobStatus.Found,
                        Path = "previous/people.json"
                    }
                });
            _mockBlobStorageRepository.Setup(repository =>
                    repository.ExtractGroupMembershipSourceMembersAsync(
                        "previous/people.json"))
                .ReturnsAsync(new HashSet<Guid> { removedPerson });
            _mockGraphGroupRepository.Setup(repository =>
                    repository.GetGroupNamesAsync(It.IsAny<List<Guid>>()))
                .ReturnsAsync(new Dictionary<Guid, string>
                {
                    [sourceGroupId] = "Role-Flipped Source"
                });
            _mockOpenAIService.Setup(service =>
                    service.GetCompletionAsync(
                        It.IsAny<string>(),
                        It.IsAny<string>()))
                .Callback<string, string>((_, prompt) =>
                    capturedPrompt = prompt)
                .ReturnsAsync("This sync completed successfully.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedPrompt);
            StringAssert.Contains(
                capturedPrompt,
                "of the removed users match this now-exclusionary rule");
            _mockBlobStorageRepository.Verify(repository =>
                    repository.ExtractGroupMembershipSourceMembersAsync(
                        "previous/people.json"),
                Times.Once);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task ExecuteAsync_RoleFlipWithSelectionChange_DoesNotUsePreviousPopulation(
            bool currentBlobIsUnreadable)
        {
            var previousRunId = Guid.NewGuid();
            var sourceGroupId = Guid.NewGuid();
            var removedPerson = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var currentQuery =
                $"[{{\"type\":\"GroupMembership\",\"source\":\"{sourceGroupId}\",\"identityTypes\":[\"agents\"],\"exclusionary\":true}}]";
            var appliedQuery =
                $"[{{\"type\":\"GroupMembership\",\"source\":\"{sourceGroupId}\",\"identityTypes\":[\"users\"]}}]";
            string? capturedPrompt = null;

            _mockSyncJobRepository.Setup(repository =>
                    repository.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(new SyncJob
                {
                    Id = _syncJobId,
                    TargetOfficeGroupId = _targetGroupId,
                    Query = currentQuery
                });
            SetupCurrentRunAndPreviousRun(
                Guid.NewGuid(),
                Guid.NewGuid(),
                previousRunId,
                usersRemoved: 1,
                previousStatus: "ThresholdExceeded");
            SetupAggregatedRemovedUsers(removedPerson);
            _mockSyncJobChangeRepository
                .Setup(repository =>
                    repository.GetRecentConfigChangesBySyncJobIdAsync(
                        _syncJobId,
                        It.IsAny<DateTime>(),
                        It.IsAny<int>()))
                .ReturnsAsync(new List<SyncJobChange>
                {
                    ChangeWithSerializedQuery(now.AddMinutes(-10), currentQuery),
                    ChangeWithSerializedQuery(now.AddMinutes(-120), appliedQuery)
                });
            var currentPartFiles = new Dictionary<string, BlobResult>();
            if (currentBlobIsUnreadable)
            {
                currentPartFiles["GroupMembership_1"] = new()
                {
                    BlobStatus = BlobStatus.Found,
                    Path = "current/agents.json"
                };
                _mockBlobStorageRepository.Setup(repository =>
                        repository.ExtractGroupMembershipSourceMembersAsync(
                            "current/agents.json"))
                    .ThrowsAsync(new InvalidOperationException("Unreadable current contribution."));
            }
            _mockBlobStorageRepository.Setup(repository =>
                    repository.FindPartFilesByRunIdAsync(
                        _targetGroupId.ToString(),
                        _runId.ToString()))
                .ReturnsAsync(currentPartFiles);
            _mockBlobStorageRepository.Setup(repository =>
                    repository.FindPartFilesByRunIdAsync(
                        _targetGroupId.ToString(),
                        previousRunId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult>
                {
                    ["GroupMembership_1"] = new()
                    {
                        BlobStatus = BlobStatus.Found,
                        Path = "previous/people.json"
                    }
                });
            _mockBlobStorageRepository.Setup(repository =>
                    repository.ExtractGroupMembershipSourceMembersAsync(
                        "previous/people.json"))
                .ReturnsAsync(new HashSet<Guid> { removedPerson });
            _mockGraphGroupRepository.Setup(repository =>
                    repository.GetGroupNamesAsync(It.IsAny<List<Guid>>()))
                .ReturnsAsync(new Dictionary<Guid, string>
                {
                    [sourceGroupId] = "Role-Flipped Source"
                });
            _mockOpenAIService.Setup(service =>
                    service.GetCompletionAsync(
                        It.IsAny<string>(),
                        It.IsAny<string>()))
                .Callback<string, string>((_, prompt) =>
                    capturedPrompt = prompt)
                .ReturnsAsync("This sync completed successfully.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedPrompt);
            StringAssert.Contains(
                capturedPrompt,
                "per-part membership counts are unavailable");
            Assert.IsFalse(
                capturedPrompt!.Contains(
                    "match this now-exclusionary rule",
                    StringComparison.Ordinal),
                capturedPrompt);
            _mockBlobStorageRepository.Verify(repository =>
                    repository.ExtractGroupMembershipSourceMembersAsync(
                        "previous/people.json"),
                Times.Never);
        }

        [DataTestMethod]
        [DataRow(true, "Rule flipped from inclusionary to exclusionary")]
        [DataRow(false, "Rule flipped from exclusionary to inclusionary")]
        public async Task ExecuteAsync_RoleFlipFollowedBySelectionChange_EmitsOutstandingRoleFlip(
            bool currentIsExclusionary,
            string expectedSignal)
        {
            var currentAdfRunId = Guid.NewGuid();
            var previousAdfRunId = Guid.NewGuid();
            var previousRunId = Guid.NewGuid();
            var sourceGroupId = Guid.NewGuid();
            var affectedUser = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var currentExclusionaryJson = currentIsExclusionary
                ? ",\"exclusionary\":true"
                : string.Empty;
            var baselineExclusionaryJson = currentIsExclusionary
                ? string.Empty
                : ",\"exclusionary\":true";
            var currentQuery =
                $"[{{\"type\":\"GroupMembership\",\"source\":\"{sourceGroupId}\",\"identityTypes\":[\"agents\"]{currentExclusionaryJson}}}]";
            var roleFlippedQuery =
                $"[{{\"type\":\"GroupMembership\",\"source\":\"{sourceGroupId}\",\"identityTypes\":[\"users\"]{currentExclusionaryJson}}}]";
            var appliedQuery =
                $"[{{\"type\":\"GroupMembership\",\"source\":\"{sourceGroupId}\",\"identityTypes\":[\"users\"]{baselineExclusionaryJson}}}]";
            string? capturedPrompt = null;

            _mockSyncJobRepository.Setup(repository =>
                    repository.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(new SyncJob
                {
                    Id = _syncJobId,
                    TargetOfficeGroupId = _targetGroupId,
                    Query = currentQuery
                });
            SetupCurrentRunAndPreviousRun(
                currentAdfRunId,
                previousAdfRunId,
                previousRunId,
                usersRemoved: currentIsExclusionary ? 1 : 0,
                usersAdded: currentIsExclusionary ? 0 : 1,
                previousStatus: "ThresholdExceeded");
            SetupAggregatedMembers(
                currentIsExclusionary
                    ? MembershipAction.Remove
                    : MembershipAction.Add,
                affectedUser);
            _mockSyncJobChangeRepository
                .Setup(repository =>
                    repository.GetRecentConfigChangesBySyncJobIdAsync(
                        _syncJobId,
                        It.IsAny<DateTime>(),
                        It.IsAny<int>()))
                .ReturnsAsync(new List<SyncJobChange>
                {
                    ChangeWithSerializedQuery(now.AddMinutes(-10), currentQuery),
                    ChangeWithSerializedQuery(now.AddMinutes(-70), roleFlippedQuery),
                    ChangeWithSerializedQuery(now.AddMinutes(-120), appliedQuery)
                });
            _mockBlobStorageRepository.Setup(repository =>
                    repository.FindPartFilesByRunIdAsync(
                        _targetGroupId.ToString(),
                        _runId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult>
                {
                    ["GroupMembership_1"] = new()
                    {
                        BlobStatus = BlobStatus.Found,
                        Path = "current/flipped.json"
                    }
                });
            _mockBlobStorageRepository.Setup(repository =>
                    repository.FindPartFilesByRunIdAsync(
                        _targetGroupId.ToString(),
                        previousRunId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult>
                {
                    ["GroupMembership_1"] = new()
                    {
                        BlobStatus = BlobStatus.Found,
                        Path = "previous/flipped.json"
                    }
                });
            _mockBlobStorageRepository.Setup(repository =>
                    repository.ExtractGroupMembershipSourceMembersAsync(
                        It.Is<string>(path =>
                            path.EndsWith(
                                "flipped.json",
                                StringComparison.Ordinal))))
                .ReturnsAsync(new HashSet<Guid> { affectedUser });
            _mockGraphGroupRepository.Setup(repository =>
                    repository.GetGroupNamesAsync(It.IsAny<List<Guid>>()))
                .ReturnsAsync(new Dictionary<Guid, string>
                {
                    [sourceGroupId] = "Role-Flipped Source"
                });
            _mockOpenAIService.Setup(service =>
                    service.GetCompletionAsync(
                        It.IsAny<string>(),
                        It.IsAny<string>()))
                .Callback<string, string>((_, prompt) =>
                    capturedPrompt = prompt)
                .ReturnsAsync("This sync completed successfully.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedPrompt);
            StringAssert.Contains(capturedPrompt!, expectedSignal);
            StringAssert.Contains(capturedPrompt, "Role-Flipped Source");
        }

        [TestMethod]
        public async Task ExecuteAsync_RevertedRoleFlipFollowedBySelectionChange_DoesNotEmitRoleFlipSignal()
        {
            var currentAdfRunId = Guid.NewGuid();
            var previousAdfRunId = Guid.NewGuid();
            var previousRunId = Guid.NewGuid();
            var sourceGroupId = Guid.NewGuid();
            var addedUser = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var currentQuery =
                $"[{{\"type\":\"GroupMembership\",\"source\":\"{sourceGroupId}\",\"identityTypes\":[\"agents\"]}}]";
            var temporaryExclusionaryQuery =
                $"[{{\"type\":\"GroupMembership\",\"source\":\"{sourceGroupId}\",\"identityTypes\":[\"users\"],\"exclusionary\":true}}]";
            var appliedQuery =
                $"[{{\"type\":\"GroupMembership\",\"source\":\"{sourceGroupId}\",\"identityTypes\":[\"users\"]}}]";
            string? capturedPrompt = null;

            _mockSyncJobRepository.Setup(repository =>
                    repository.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(new SyncJob
                {
                    Id = _syncJobId,
                    TargetOfficeGroupId = _targetGroupId,
                    Query = currentQuery
                });
            SetupCurrentRunAndPreviousRun(
                currentAdfRunId,
                previousAdfRunId,
                previousRunId,
                usersRemoved: 0,
                usersAdded: 1,
                previousStatus: "ThresholdExceeded");
            SetupAggregatedMembers(MembershipAction.Add, addedUser);
            _mockSyncJobChangeRepository
                .Setup(repository =>
                    repository.GetRecentConfigChangesBySyncJobIdAsync(
                        _syncJobId,
                        It.IsAny<DateTime>(),
                        It.IsAny<int>()))
                .ReturnsAsync(new List<SyncJobChange>
                {
                    ChangeWithSerializedQuery(now.AddMinutes(-10), currentQuery),
                    ChangeWithSerializedQuery(
                        now.AddMinutes(-70),
                        temporaryExclusionaryQuery),
                    ChangeWithSerializedQuery(now.AddMinutes(-120), appliedQuery)
                });
            _mockBlobStorageRepository.Setup(repository =>
                    repository.FindPartFilesByRunIdAsync(
                        _targetGroupId.ToString(),
                        _runId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult>
                {
                    ["GroupMembership_1"] = new()
                    {
                        BlobStatus = BlobStatus.Found,
                        Path = "current/reverted.json"
                    }
                });
            _mockBlobStorageRepository.Setup(repository =>
                    repository.FindPartFilesByRunIdAsync(
                        _targetGroupId.ToString(),
                        previousRunId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult>
                {
                    ["GroupMembership_1"] = new()
                    {
                        BlobStatus = BlobStatus.Found,
                        Path = "previous/reverted.json"
                    }
                });
            _mockBlobStorageRepository.Setup(repository =>
                    repository.ExtractGroupMembershipSourceMembersAsync(
                        It.Is<string>(path =>
                            path.EndsWith(
                                "reverted.json",
                                StringComparison.Ordinal))))
                .ReturnsAsync(new HashSet<Guid> { addedUser });
            _mockGraphGroupRepository.Setup(repository =>
                    repository.GetGroupNamesAsync(It.IsAny<List<Guid>>()))
                .ReturnsAsync(new Dictionary<Guid, string>
                {
                    [sourceGroupId] = "Reverted Source"
                });
            _mockOpenAIService.Setup(service =>
                    service.GetCompletionAsync(
                        It.IsAny<string>(),
                        It.IsAny<string>()))
                .Callback<string, string>((_, prompt) =>
                    capturedPrompt = prompt)
                .ReturnsAsync("This sync completed successfully.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedPrompt);
            Assert.IsFalse(
                capturedPrompt!.Contains(
                    "Rule flipped from inclusionary to exclusionary",
                    StringComparison.Ordinal),
                capturedPrompt);
            Assert.IsFalse(
                capturedPrompt.Contains(
                    "Rule flipped from exclusionary to inclusionary",
                    StringComparison.Ordinal),
                capturedPrompt);
            Assert.IsFalse(
                capturedPrompt.Contains(
                    "changed from exclusionary to inclusionary",
                    StringComparison.Ordinal),
                capturedPrompt);
        }

        [TestMethod]
        public async Task ExecuteAsync_RevertedRoleFlipOutsideRecentHistory_DoesNotReplaceAppliedBaseline()
        {
            var currentAdfRunId = Guid.NewGuid();
            var previousAdfRunId = Guid.NewGuid();
            var previousRunId = Guid.NewGuid();
            var sourceGroupId = Guid.NewGuid();
            var removedUser = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var appliedAndCurrentQuery =
                $"[{{\"type\":\"GroupMembership\",\"source\":\"{sourceGroupId}\",\"exclusionary\":true}}]";
            var temporaryInclusionaryQuery =
                $"[{{\"type\":\"GroupMembership\",\"source\":\"{sourceGroupId}\"}}]";
            var unchangedCurrentSnapshots = Enumerable.Range(10, 10)
                .Select(minutes =>
                    ChangeWithSerializedQuery(
                        now.AddMinutes(-minutes),
                        appliedAndCurrentQuery))
                .ToList();
            string? capturedPrompt = null;

            _mockSyncJobRepository.Setup(repository =>
                    repository.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(new SyncJob
                {
                    Id = _syncJobId,
                    TargetOfficeGroupId = _targetGroupId,
                    Query = appliedAndCurrentQuery
                });
            SetupCurrentRunAndPreviousRun(
                currentAdfRunId,
                previousAdfRunId,
                previousRunId,
                usersRemoved: 1,
                previousStatus: "ThresholdExceeded");
            SetupAggregatedRemovedUsers(removedUser);
            _mockSyncJobChangeRepository
                .Setup(repository =>
                    repository.GetRecentConfigChangesBySyncJobIdAsync(
                        _syncJobId,
                        It.IsAny<DateTime>(),
                        It.IsAny<int>()))
                .ReturnsAsync((Guid _, DateTime cutoff, int count) =>
                {
                    if (count == 10)
                    {
                        return unchangedCurrentSnapshots;
                    }

                    if (cutoff <= now.AddMinutes(-90))
                    {
                        return new List<SyncJobChange>
                        {
                            ChangeWithSerializedQuery(
                                now.AddMinutes(-120),
                                appliedAndCurrentQuery)
                        };
                    }

                    if (cutoff <= now.AddMinutes(-30))
                    {
                        return new List<SyncJobChange>
                        {
                            ChangeWithSerializedQuery(
                                now.AddMinutes(-70),
                                temporaryInclusionaryQuery)
                        };
                    }

                    return new List<SyncJobChange>
                    {
                        ChangeWithSerializedQuery(
                            now.AddMinutes(-10),
                            appliedAndCurrentQuery)
                    };
                });
            _mockBlobStorageRepository.Setup(repository =>
                    repository.FindPartFilesByRunIdAsync(
                        _targetGroupId.ToString(),
                        _runId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult>
                {
                    ["GroupMembership_1"] = new()
                    {
                        BlobStatus = BlobStatus.Found,
                        Path = "current/restored-applied-role.json"
                    }
                });
            _mockBlobStorageRepository.Setup(repository =>
                    repository.ExtractGroupMembershipSourceMembersAsync(
                        "current/restored-applied-role.json"))
                .ReturnsAsync(new HashSet<Guid> { removedUser });
            _mockGraphGroupRepository.Setup(repository =>
                    repository.GetGroupNamesAsync(It.IsAny<List<Guid>>()))
                .ReturnsAsync(new Dictionary<Guid, string>
                {
                    [sourceGroupId] = "Restored Applied Role Source"
                });
            _mockOpenAIService.Setup(service =>
                    service.GetCompletionAsync(
                        It.IsAny<string>(),
                        It.IsAny<string>()))
                .Callback<string, string>((_, prompt) =>
                    capturedPrompt = prompt)
                .ReturnsAsync("This sync completed successfully.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedPrompt);
            Assert.IsFalse(
                capturedPrompt!.Contains(
                    "Rule flipped from inclusionary to exclusionary",
                    StringComparison.Ordinal),
                capturedPrompt);
        }

        [TestMethod]
        public async Task ExecuteAsync_UnchangedSnapshotAfterPreviousRun_AttributesUnderlyingEarlierChange()
        {
            var currentAdfRunId = Guid.NewGuid();
            var previousAdfRunId = Guid.NewGuid();
            var previousRunId = Guid.NewGuid();
            var sourceGroupId = Guid.NewGuid();
            var addedUser = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var query =
                $"[{{\"type\":\"GroupMembership\",\"source\":\"{sourceGroupId}\"}}]";
            var appliedChange = ChangeWithSerializedQuery(
                now.AddMinutes(-120),
                query);
            var scheduledNowChange = ChangeWithSerializedQuery(
                now.AddMinutes(-10),
                query);
            scheduledNowChange.ChangeReason =
                SyncJobChangeReason.ScheduledNow.ToString();
            string? capturedPrompt = null;

            _mockSyncJobRepository.Setup(repository =>
                    repository.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(new SyncJob
                {
                    Id = _syncJobId,
                    TargetOfficeGroupId = _targetGroupId,
                    Query = query
                });
            SetupCurrentRunAndPreviousRun(
                currentAdfRunId,
                previousAdfRunId,
                previousRunId,
                usersRemoved: 0,
                usersAdded: 1,
                previousStatus: "ThresholdExceeded");
            SetupAggregatedMembers(MembershipAction.Add, addedUser);
            _mockSyncJobChangeRepository
                .Setup(repository =>
                    repository.GetRecentConfigChangesBySyncJobIdAsync(
                        _syncJobId,
                        It.IsAny<DateTime>(),
                        It.IsAny<int>()))
                .ReturnsAsync((Guid _, DateTime asOf, int count) =>
                {
                    var matchingChanges = (asOf < now.AddMinutes(-60)
                        ? new[] { appliedChange }
                        : new[] { scheduledNowChange, appliedChange })
                        .AsEnumerable();
                    return count > 0
                        ? matchingChanges.Take(count).ToList()
                        : matchingChanges.ToList();
                });
            _mockGraphGroupRepository.Setup(repository =>
                    repository.GetGroupNamesAsync(It.IsAny<List<Guid>>()))
                .ReturnsAsync(new Dictionary<Guid, string>
                {
                    [sourceGroupId] = "Unchanged Source"
                });
            _mockOpenAIService.Setup(service =>
                    service.GetCompletionAsync(
                        It.IsAny<string>(),
                        It.IsAny<string>()))
                .Callback<string, string>((_, prompt) =>
                    capturedPrompt = prompt)
                .ReturnsAsync("This sync completed successfully.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedPrompt);
            StringAssert.Contains(
                capturedPrompt!,
                "this change occurred BEFORE the previous run");
            Assert.IsFalse(capturedPrompt.Contains("ScheduledNow"));
        }

        [TestMethod]
        public async Task ExecuteAsync_RemovesAttribution_WhenAggregatedRemovedSetEmpty_SkipsAttribution()
        {
            var currentAdfRunId = Guid.NewGuid();
            var previousAdfRunId = Guid.NewGuid();
            var previousRunId = Guid.NewGuid();
            string? capturedPrompt = null;

            SetupCurrentRunAndPreviousRun(currentAdfRunId, previousAdfRunId, previousRunId, usersRemoved: 1);
            _mockBlobStorageRepository.Setup(x => x.FindAggregatedFileByRunIdAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new BlobResult { BlobStatus = BlobStatus.Found, Path = "test/empty-removes.json" });
            _mockBlobStorageRepository.Setup(x => x.DownloadFileAsync("test/empty-removes.json"))
                .ReturnsAsync(new BlobResult
                {
                    BlobStatus = BlobStatus.Found,
                    Content = $"{{\"SourceMembers\":[{{\"ObjectId\":\"{Guid.NewGuid()}\",\"MembershipAction\":1}}]}}"
                });
            _mockOpenAIService.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((_, prompt) => capturedPrompt = prompt)
                .ReturnsAsync("This sync completed successfully.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            _mockOpenAIService.Verify(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
            Assert.IsNotNull(capturedPrompt);
            Assert.IsFalse(capturedPrompt!.Contains("Per-part attribution for removed users", StringComparison.OrdinalIgnoreCase));
        }

        // ---------------- New exclusionary rule attribution for removals ----------------

        [DataTestMethod]
        [DataRow(true, true)]
        [DataRow(false, false)]
        public async Task ExecuteAsync_RemovesAttribution_NewExclusionarySourceAdded_EmitsOnlyWhenRemovedUserMatches(
            bool sourceContainsRemovedUser,
            bool expectsAttribution)
        {
            var currentAdfRunId = Guid.NewGuid();
            var previousAdfRunId = Guid.NewGuid();
            var previousRunId = Guid.NewGuid();
            var inclSource = Guid.NewGuid();
            var newExclSource = Guid.NewGuid();
            var removedUser = Guid.NewGuid();
            string? capturedPrompt = null;

            _mockSyncJobRepository.Setup(x => x.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(new SyncJob
                {
                    Id = _syncJobId,
                    TargetOfficeGroupId = _targetGroupId,
                    // Current query has the incl source + a NEW exclusionary source.
                    Query = $"[{{\"type\":\"GroupMembership\",\"source\":\"{inclSource}\"}}," +
                            $"{{\"type\":\"GroupMembership\",\"source\":\"{newExclSource}\",\"exclusionary\":true}}]"
                });

            SetupCurrentRunAndPreviousRun(currentAdfRunId, previousAdfRunId, previousRunId, usersRemoved: 1);
            SetupAggregatedRemovedUsers(removedUser);

            // Previous config: only the inclusionary source. The newExclSource is NEW in this update.
            _mockSyncJobChangeRepository
                .Setup(x => x.GetRecentConfigChangesBySyncJobIdAsync(_syncJobId, It.IsAny<DateTime>(), It.IsAny<int>()))
                .ReturnsAsync(new List<SyncJobChange>
                {
                    new() { Id = Guid.NewGuid(), SyncJobId = _syncJobId, ChangeReason = SyncJobChangeReason.Update.ToString(), ChangeTime = DateTime.UtcNow.AddMinutes(-2),
                            ChangeDetails = "{\"query\":\"[{\\\"type\\\":\\\"GroupMembership\\\",\\\"source\\\":\\\"" + inclSource + "\\\"},{\\\"type\\\":\\\"GroupMembership\\\",\\\"source\\\":\\\"" + newExclSource + "\\\",\\\"exclusionary\\\":true}]\"}" },
                    new() { Id = Guid.NewGuid(), SyncJobId = _syncJobId, ChangeReason = SyncJobChangeReason.Update.ToString(), ChangeTime = DateTime.UtcNow.AddHours(-2),
                            ChangeDetails = "{\"query\":\"[{\\\"type\\\":\\\"GroupMembership\\\",\\\"source\\\":\\\"" + inclSource + "\\\"}]\"}" }
                });

            // Current per-part blobs: index 1 = incl source, index 2 = new excl source (contains removedUser).
            _mockBlobStorageRepository.Setup(x => x.FindPartFilesByRunIdAsync(_targetGroupId.ToString(), _runId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult>
                {
                    ["GroupMembership_1"] = new() { BlobStatus = BlobStatus.Found, Path = "current/incl.json" },
                    ["GroupMembership_2"] = new() { BlobStatus = BlobStatus.Found, Path = "current/newexcl.json" }
                });
            _mockBlobStorageRepository.Setup(x => x.FindPartFilesByRunIdAsync(_targetGroupId.ToString(), previousRunId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult>
                {
                    ["GroupMembership_1"] = new() { BlobStatus = BlobStatus.Found, Path = "previous/incl.json" }
                });
            _mockBlobStorageRepository.Setup(x => x.ExtractGroupMembershipSourceMembersAsync("current/incl.json"))
                .ReturnsAsync(new HashSet<Guid> { removedUser });
            _mockBlobStorageRepository.Setup(x => x.ExtractGroupMembershipSourceMembersAsync("previous/incl.json"))
                .ReturnsAsync(new HashSet<Guid> { removedUser });
            _mockBlobStorageRepository.Setup(x => x.ExtractGroupMembershipSourceMembersAsync("current/newexcl.json"))
                .ReturnsAsync(sourceContainsRemovedUser
                    ? new HashSet<Guid> { removedUser }
                    : new HashSet<Guid> { Guid.NewGuid() });

            _mockGraphGroupRepository.Setup(x => x.GetGroupNamesAsync(It.IsAny<List<Guid>>()))
                .ReturnsAsync(new Dictionary<Guid, string>
                {
                    [inclSource] = "IncludedSource",
                    [newExclSource] = "NewExcludedSource"
                });
            _mockOpenAIService.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((_, prompt) => capturedPrompt = prompt)
                .ReturnsAsync("Done.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedPrompt);
            Assert.AreEqual(
                expectsAttribution,
                capturedPrompt!.Contains(
                    "New exclusionary rule added in the recent config update",
                    StringComparison.Ordinal),
                capturedPrompt);
            if (expectsAttribution)
            {
                StringAssert.Contains(capturedPrompt, "NewExcludedSource");
                StringAssert.Contains(
                    capturedPrompt,
                    "newly added exclusionary rule");
            }
        }

        // ---------------- Removed exclusionary rule attribution for additions ----------------

        [TestMethod]
        public async Task ExecuteAsync_AddsAttribution_DeletedExclusionarySource_EmitsAttributionLine()
        {
            var currentAdfRunId = Guid.NewGuid();
            var previousAdfRunId = Guid.NewGuid();
            var previousRunId = Guid.NewGuid();
            var inclSource = Guid.NewGuid();
            var deletedExclSource = Guid.NewGuid();
            var addedUser = Guid.NewGuid();
            string? capturedPrompt = null;

            _mockSyncJobRepository.Setup(x => x.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(new SyncJob
                {
                    Id = _syncJobId,
                    TargetOfficeGroupId = _targetGroupId,
                    // Current query: ONLY the incl source. The excl source was deleted in this update.
                    Query = $"[{{\"type\":\"GroupMembership\",\"source\":\"{inclSource}\"}}]"
                });

            _mockSyncJobHistoryRepository.Setup(x => x.GetByRunIdAsync(_runId))
                .ReturnsAsync(new global::Models.SyncJobHistory.SyncJobHistory
                {
                    SyncJobId = _syncJobId,
                    RunId = _runId,
                    Status = "Idle",
                    UpdatedAt = DateTime.UtcNow,
                    StartTime = DateTime.UtcNow.AddMinutes(-5),
                    EndTime = DateTime.UtcNow,
                    UsersAdded = 1,
                    UsersRemoved = 0,
                    BeforeSyncUserCount = 100,
                    AfterSyncUserCount = 101,
                    AdfRunId = currentAdfRunId
                });
            _mockSyncJobHistoryRepository.Setup(x => x.GetBySyncJobIdAsync(_syncJobId, It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync(new List<global::Models.SyncJobHistory.SyncJobHistory>
                {
                    new() { SyncJobId = _syncJobId, RunId = previousRunId, Status = "Idle", UpdatedAt = DateTime.UtcNow.AddHours(-1),
                            StartTime = DateTime.UtcNow.AddHours(-1).AddMinutes(-5), EndTime = DateTime.UtcNow.AddHours(-1),
                            UsersAdded = 0, UsersRemoved = 0, AdfRunId = previousAdfRunId }
                });

            _mockBlobStorageRepository.Setup(x => x.FindAggregatedFileByRunIdAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new BlobResult { BlobStatus = BlobStatus.Found, Path = "test/adds.json" });
            _mockBlobStorageRepository.Setup(x => x.DownloadFileAsync("test/adds.json"))
                .ReturnsAsync(new BlobResult { BlobStatus = BlobStatus.Found, Content = $"{{\"SourceMembers\":[{{\"ObjectId\":\"{addedUser}\",\"MembershipAction\":1}}]}}" });

            _mockSyncJobChangeRepository
                .Setup(x => x.GetRecentConfigChangesBySyncJobIdAsync(_syncJobId, It.IsAny<DateTime>(), It.IsAny<int>()))
                .ReturnsAsync(new List<SyncJobChange>
                {
                    new() { Id = Guid.NewGuid(), SyncJobId = _syncJobId, ChangeReason = SyncJobChangeReason.Update.ToString(), ChangeTime = DateTime.UtcNow.AddMinutes(-2),
                            ChangeDetails = "{\"query\":\"[{\\\"type\\\":\\\"GroupMembership\\\",\\\"source\\\":\\\"" + inclSource + "\\\"}]\"}" },
                    new() { Id = Guid.NewGuid(), SyncJobId = _syncJobId, ChangeReason = SyncJobChangeReason.Update.ToString(), ChangeTime = DateTime.UtcNow.AddHours(-2),
                            ChangeDetails = "{\"query\":\"[{\\\"type\\\":\\\"GroupMembership\\\",\\\"source\\\":\\\"" + inclSource + "\\\"},{\\\"type\\\":\\\"GroupMembership\\\",\\\"source\\\":\\\"" + deletedExclSource + "\\\",\\\"exclusionary\\\":true}]\"}" }
                });

            _mockBlobStorageRepository.Setup(x => x.FindPartFilesByRunIdAsync(_targetGroupId.ToString(), _runId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult>
                {
                    ["GroupMembership_1"] = new() { BlobStatus = BlobStatus.Found, Path = "current/incl.json" }
                });
            _mockBlobStorageRepository.Setup(x => x.FindPartFilesByRunIdAsync(_targetGroupId.ToString(), previousRunId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult>
                {
                    ["GroupMembership_1"] = new() { BlobStatus = BlobStatus.Found, Path = "previous/incl.json" },
                    ["GroupMembership_2"] = new() { BlobStatus = BlobStatus.Found, Path = "previous/deletedexcl.json" }
                });
            _mockBlobStorageRepository.Setup(x => x.ExtractGroupMembershipSourceMembersAsync("current/incl.json"))
                .ReturnsAsync(new HashSet<Guid> { addedUser });
            _mockBlobStorageRepository.Setup(x => x.ExtractGroupMembershipSourceMembersAsync("previous/incl.json"))
                .ReturnsAsync(new HashSet<Guid> { addedUser });
            _mockBlobStorageRepository.Setup(x => x.ExtractGroupMembershipSourceMembersAsync("previous/deletedexcl.json"))
                .ReturnsAsync(new HashSet<Guid> { addedUser });

            _mockGraphGroupRepository.Setup(x => x.GetGroupNamesAsync(It.IsAny<List<Guid>>()))
                .ReturnsAsync(new Dictionary<Guid, string>
                {
                    [inclSource] = "IncludedSource",
                    [deletedExclSource] = "DeletedExclusionarySource"
                });
            _mockOpenAIService.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((_, prompt) => capturedPrompt = prompt)
                .ReturnsAsync("Done.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedPrompt);
            StringAssert.Contains(capturedPrompt!, "Exclusionary rule removed from the query in the recent config update");
            StringAssert.Contains(capturedPrompt!, "DeletedExclusionarySource");
            StringAssert.Contains(capturedPrompt!, "previously blocked by this now-removed exclusionary rule");
        }

        // ---------------- StartTime-preferring config cutoff ----------------

        [TestMethod]
        public void GetRunConfigCutoff_StartTimeAvailable_ReturnsStartTime()
        {
            var start = new DateTime(2026, 7, 16, 23, 20, 0, DateTimeKind.Utc);
            var end = new DateTime(2026, 7, 16, 23, 30, 55, DateTimeKind.Utc);
            var history = new global::Models.SyncJobHistory.SyncJobHistory { StartTime = start, EndTime = end, UpdatedAt = end };
            var result = InvokeStaticPrivate<DateTime>(typeof(GetRunExplanationHandler), "GetRunConfigCutoff", new object?[] { history });
            Assert.AreEqual(start, result);
        }

        [TestMethod]
        public void GetRunConfigCutoff_StartTimeNull_ReturnsEndTimeMinus30Seconds()
        {
            var end = new DateTime(2026, 7, 16, 23, 30, 55, DateTimeKind.Utc);
            var history = new global::Models.SyncJobHistory.SyncJobHistory { StartTime = null, EndTime = end, UpdatedAt = end };
            var result = InvokeStaticPrivate<DateTime>(typeof(GetRunExplanationHandler), "GetRunConfigCutoff", new object?[] { history });
            Assert.AreEqual(end.AddSeconds(-30), result);
        }

        [TestMethod]
        public void GetRunConfigCutoff_StartTimeAndEndTimeNull_ReturnsUpdatedAt()
        {
            var updated = new DateTime(2026, 7, 16, 23, 30, 0, DateTimeKind.Utc);
            var history = new global::Models.SyncJobHistory.SyncJobHistory { StartTime = null, EndTime = null, UpdatedAt = updated };
            var result = InvokeStaticPrivate<DateTime>(typeof(GetRunExplanationHandler), "GetRunConfigCutoff", new object?[] { history });
            Assert.AreEqual(updated, result);
        }

        private void SetupCurrentRunAndPreviousRun(
            Guid currentAdfRunId,
            Guid previousAdfRunId,
            Guid previousRunId,
            int usersRemoved,
            int usersAdded = 0,
            string previousStatus = "Idle",
            string? previousUpdatedByFunction = null)
        {
            var now = DateTime.UtcNow;
            _mockSyncJobHistoryRepository.Setup(x => x.GetByRunIdAsync(_runId))
                .ReturnsAsync(new global::Models.SyncJobHistory.SyncJobHistory
                {
                    SyncJobId = _syncJobId,
                    RunId = _runId,
                    Status = "Idle",
                    UpdatedAt = now,
                    StartTime = now.AddMinutes(-5),
                    EndTime = now,
                    UsersAdded = usersAdded,
                    UsersRemoved = usersRemoved,
                    BeforeSyncUserCount = 100,
                    AfterSyncUserCount = 100 - usersRemoved + usersAdded,
                    AdfRunId = currentAdfRunId
                });

            var previousRuns =
                new List<global::Models.SyncJobHistory.SyncJobHistory>
                {
                    new()
                    {
                        SyncJobId = _syncJobId,
                        RunId = previousRunId,
                        Status = previousStatus,
                        UpdatedByFunction = previousUpdatedByFunction
                            ?? (string.Equals(
                                previousStatus,
                                "Idle",
                                StringComparison.OrdinalIgnoreCase)
                                    ? "GraphUpdater"
                                    : "GroupMembershipObtainer"),
                        UpdatedAt = now.AddHours(-1),
                        StartTime = now.AddHours(-1).AddMinutes(-5),
                        EndTime = now.AddHours(-1),
                        UsersAdded = 0,
                        UsersRemoved = 0,
                        AdfRunId = previousAdfRunId
                    }
                };
            if (!string.Equals(
                previousStatus,
                "Idle",
                StringComparison.OrdinalIgnoreCase))
            {
                previousRuns.Add(new()
                {
                    SyncJobId = _syncJobId,
                    RunId = Guid.NewGuid(),
                    Status = "Idle",
                    UpdatedByFunction = "GraphUpdater",
                    UpdatedAt = now.AddMinutes(-100),
                    StartTime = now.AddMinutes(-105),
                    EndTime = now.AddMinutes(-100),
                    UsersAdded = 0,
                    UsersRemoved = 0,
                    AdfRunId = Guid.NewGuid()
                });
            }

            _mockSyncJobHistoryRepository
                .Setup(x =>
                    x.GetBySyncJobIdAsync(
                        _syncJobId,
                        It.IsAny<int>(),
                        It.IsAny<int>()))
                .ReturnsAsync(previousRuns);
        }

        private void SetupAggregatedRemovedUsers(params Guid[] removedUsers)
        {
            SetupAggregatedMembers(MembershipAction.Remove, removedUsers);
        }

        private void SetupAggregatedMembers(MembershipAction membershipAction, params Guid[] objectIds)
        {
            var members = string.Join(",", objectIds.Select(id => $"{{\"ObjectId\":\"{id}\",\"MembershipAction\":{(int)membershipAction}}}"));
            _mockBlobStorageRepository.Setup(x => x.FindAggregatedFileByRunIdAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new BlobResult { BlobStatus = BlobStatus.Found, Path = "test/aggregated.json" });
            _mockBlobStorageRepository.Setup(x => x.DownloadFileAsync("test/aggregated.json"))
                .ReturnsAsync(new BlobResult { BlobStatus = BlobStatus.Found, Content = $"{{\"SourceMembers\":[{members}]}}" });
        }

        private void SetupSqlEvaluation(
            string filter,
            string tableName,
            IdentityTypeSet identityTypes,
            int? managerEmployeeId = null,
            int depth = 0,
            IReadOnlyCollection<Guid>? userObjectIds = null,
            IReadOnlyCollection<Guid>? agentObjectIds = null)
        {
            var selectedUserObjectIds = userObjectIds ?? Array.Empty<Guid>();
            var selectedAgentObjectIds = agentObjectIds ?? Array.Empty<Guid>();
            var referencedIdentityTypes = new List<IdentityType>();

            if (identityTypes.Includes(IdentityType.Users))
            {
                referencedIdentityTypes.Add(IdentityType.Users);
            }

            if (identityTypes.Includes(IdentityType.Agents))
            {
                referencedIdentityTypes.Add(IdentityType.Agents);
            }

            _mockSqlMembershipRepository
                .Setup(x => x.EvaluateMembershipAsync(
                    It.Is<SqlMembershipEvaluationRequest>(request =>
                        MatchesSqlEvaluationRequest(
                            request,
                            filter,
                            tableName,
                            identityTypes,
                            managerEmployeeId,
                            depth))))
                .ReturnsAsync(new SqlMembershipEvaluationResult(
                    selectedUserObjectIds,
                    selectedAgentObjectIds,
                    selectedUserObjectIds.Count,
                    referencedIdentityTypes));
        }

        private static bool MatchesSqlEvaluationRequest(
            SqlMembershipEvaluationRequest request,
            string filter,
            string tableName,
            IdentityTypeSet identityTypes,
            int? managerEmployeeId = null,
            int depth = 0) =>
            request.Filter == filter &&
            request.TableName == tableName &&
            request.IdentityTypes == identityTypes &&
            request.ManagerEmployeeId == managerEmployeeId &&
            request.Depth == depth;

        // Builds a SyncJobChange for ComputeWalkBackFloor tests: a config change carrying `query` in ChangeDetails, or a status-only change (no query) when query is null.
        private static SyncJobChange ChangeWithQuery(DateTime changeTime, string? query) =>
            new()
            {
                ChangeTime = changeTime,
                ChangeDetails = query == null ? "{\"status\":\"SubmissionApproved\"}" : $"{{\"query\":\"{query}\"}}"
            };

        // Reflection helper for private static methods so we don't have to widen visibility.
        private static T? InvokeStaticPrivate<T>(Type type, string name, object?[] args)
        {
            var method = type.GetMethod(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(method, $"Method {name} not found on {type.Name}");
            var raw = method!.Invoke(null, args);
            return raw is null ? default : (T)raw;
        }

        // ---------------- DescribeMembershipRulesForOwner + OwnerFriendlyFilterFormatter ----------------

        private static GetRunExplanationHandler.QueryPartInfo NewSqlPart(
            string? filter, int index = 0, bool exclusionary = false, string? managerId = null, int? depth = null)
            => new()
            {
                Index = index,
                Type = "SqlMembership",
                Filter = filter,
                Exclusionary = exclusionary,
                ManagerId = managerId,
                ManagerDepth = depth
            };

        private static string DescribeSingle(
            GetRunExplanationHandler.QueryPartInfo part,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? mappings = null,
            IReadOnlyDictionary<Guid, string>? groupNames = null,
            IReadOnlyDictionary<int, string>? managerNames = null)
            => GetRunExplanationHandler.DescribeMembershipRulesForOwner(
                new List<GetRunExplanationHandler.QueryPartInfo> { part }, mappings, groupNames, managerNames);

        [TestMethod]
        public void DescribeRules_NullParts_ReturnsNoRules()
            => Assert.AreEqual("No membership rules configured.",
                GetRunExplanationHandler.DescribeMembershipRulesForOwner(null, null, null, null));

        [TestMethod]
        public void DescribeRules_EmptyParts_ReturnsNoRules()
            => Assert.AreEqual("No membership rules configured.",
                GetRunExplanationHandler.DescribeMembershipRulesForOwner(
                    new List<GetRunExplanationHandler.QueryPartInfo>(), null, null, null));

        [TestMethod]
        public void DescribeRules_Equals_StringValueQuoted()
            => StringAssert.Contains(DescribeSingle(NewSqlPart("Building = 'B40'")), "Building is \"B40\"");

        [TestMethod]
        public void DescribeRules_GreaterOrEqual_NumericValue_HumanizesNbr()
            => StringAssert.Contains(DescribeSingle(NewSqlPart("PayScaleStockLevelNbr >= 65")),
                "Pay Scale Stock Level Number is at least 65");

        [TestMethod]
        public void DescribeRules_BooleanIndicator_One_YieldsYes()
            => StringAssert.Contains(DescribeSingle(NewSqlPart("SupervisorInd = 1")), "Supervisor Indicator is Yes");

        [TestMethod]
        public void DescribeRules_BooleanFlag_Zero_YieldsNo()
            => StringAssert.Contains(DescribeSingle(NewSqlPart("ActiveFlag = 0")), "Active Flag is No");

        [TestMethod]
        public void DescribeRules_NotEquals_YieldsIsNot()
            => StringAssert.Contains(DescribeSingle(NewSqlPart("Status <> 'X'")), "Status is not \"X\"");

        [TestMethod]
        public void DescribeRules_LessThanOrEqual_YieldsIsAtMost()
            => StringAssert.Contains(DescribeSingle(NewSqlPart("Level <= 5")), "Level is at most 5");

        [TestMethod]
        public void DescribeRules_GreaterThan_YieldsIsGreaterThan()
            => StringAssert.Contains(DescribeSingle(NewSqlPart("Age > 30")), "Age is greater than 30");

        [TestMethod]
        public void DescribeRules_LessThan_YieldsIsLessThan()
            => StringAssert.Contains(DescribeSingle(NewSqlPart("Age < 30")), "Age is less than 30");

        [TestMethod]
        public void DescribeRules_In_ThreeValues_JoinsWithOr()
            => StringAssert.Contains(DescribeSingle(NewSqlPart("Region IN ('US', 'EU', 'APAC')")),
                "Region is one of \"US\", \"EU\", or \"APAC\"");

        [TestMethod]
        public void DescribeRules_NotIn_YieldsIsNotOneOf()
            => StringAssert.Contains(DescribeSingle(NewSqlPart("Region NOT IN ('US', 'EU')")),
                "Region is not one of \"US\" or \"EU\"");

        [TestMethod]
        public void DescribeRules_LikeContains()
            => StringAssert.Contains(DescribeSingle(NewSqlPart("Title LIKE '%manager%'")), "Title contains \"manager\"");

        [TestMethod]
        public void DescribeRules_LikeStartsWith()
            => StringAssert.Contains(DescribeSingle(NewSqlPart("Title LIKE 'Senior%'")), "Title starts with \"Senior\"");

        [TestMethod]
        public void DescribeRules_LikeEndsWith()
            => StringAssert.Contains(DescribeSingle(NewSqlPart("Title LIKE '%Director'")), "Title ends with \"Director\"");

        [TestMethod]
        public void DescribeRules_LikeExact_Matches()
            => StringAssert.Contains(DescribeSingle(NewSqlPart("Title LIKE 'Exact'")), "Title matches \"Exact\"");

        [TestMethod]
        public void DescribeRules_NotLike_DoesNotContain()
            => StringAssert.Contains(DescribeSingle(NewSqlPart("Title NOT LIKE '%temp%'")), "Title does not contain \"temp\"");

        [TestMethod]
        public void DescribeRules_IsNull_HasNoValue()
            => StringAssert.Contains(DescribeSingle(NewSqlPart("Manager IS NULL")), "Manager has no value");

        [TestMethod]
        public void DescribeRules_IsNotNull_HasAValue()
            => StringAssert.Contains(DescribeSingle(NewSqlPart("Manager IS NOT NULL")), "Manager has a value");

        [TestMethod]
        public void DescribeRules_AndConnector()
            => StringAssert.Contains(DescribeSingle(NewSqlPart("Building = 'B40' AND Level >= 5")),
                "Building is \"B40\" and Level is at least 5");

        [TestMethod]
        public void DescribeRules_OrWithParens()
            => StringAssert.Contains(DescribeSingle(NewSqlPart("(RegionA = '1' OR RegionB = '2')")),
                "(Region A is 1 or Region B is 2)");

        [TestMethod]
        public void DescribeRules_InvalidConnector_FallsBackToGeneric()
            => StringAssert.Contains(DescribeSingle(NewSqlPart("Building = 'B40' XYZ Level >= 5")),
                "the configured HR criteria");

        [TestMethod]
        public void DescribeRules_EmptyFilter_FallsBackToGeneric()
            => StringAssert.Contains(DescribeSingle(NewSqlPart("")), "the configured HR criteria");

        [TestMethod]
        public void DescribeRules_NoPredicate_FallsBackToGeneric()
            => StringAssert.Contains(DescribeSingle(NewSqlPart("just some words")), "the configured HR criteria");

        [TestMethod]
        public void DescribeRules_EqualsNoValue_UnspecifiedValue()
            => StringAssert.Contains(DescribeSingle(NewSqlPart("Building =")), "Building is an unspecified value");

        [TestMethod]
        public void DescribeRules_UnicodeStringLiteral_Unquotes()
            => StringAssert.Contains(DescribeSingle(NewSqlPart("City = N'Redmond'")), "City is \"Redmond\"");

        [TestMethod]
        public void DescribeRules_EscapedQuoteInValue_Unescaped()
            => StringAssert.Contains(DescribeSingle(NewSqlPart("Name = 'O''Brien'")), "Name is \"O'Brien\"");

        [TestMethod]
        public void DescribeRules_CodeAttribute_NoMapping_UnavailableDescription()
            => StringAssert.Contains(DescribeSingle(NewSqlPart("CostCenter_Code = '10181410'")),
                "Cost Center is a configured value whose description is unavailable");

        [TestMethod]
        public void DescribeRules_CodeAttribute_WithMapping_UsesDescription()
        {
            var mappings = new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["CostCenter_Code"] = new Dictionary<string, string> { ["10181410"] = "Redmond HQ" }
            };
            StringAssert.Contains(DescribeSingle(NewSqlPart("CostCenter_Code = '10181410'"), mappings),
                "Cost Center is \"Redmond HQ\"");
        }

        [TestMethod]
        public void DescribeRules_CodeAttribute_BaseAttributeMappingFallback()
        {
            var mappings = new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["Building"] = new Dictionary<string, string> { ["24"] = "Building 24", ["25"] = "Building 25" }
            };
            StringAssert.Contains(DescribeSingle(NewSqlPart("Building_Code IN (24, 25)"), mappings),
                "Building is one of \"Building 24\" or \"Building 25\"");
        }

        [TestMethod]
        public void DescribeRules_Exclusionary_UsesExcludes()
            => StringAssert.Contains(DescribeSingle(NewSqlPart("Building = 'B40'", exclusionary: true)),
                "Excludes employees where");

        [TestMethod]
        public void DescribeRules_SqlWithManagerScope_ResolvesName()
        {
            var managers = new Dictionary<int, string> { [582877] = "Paul Daly" };
            var result = DescribeSingle(NewSqlPart("Building = 'B40'", managerId: "582877"), managerNames: managers);
            StringAssert.Contains(result, "management chain rooted at");
            StringAssert.Contains(result, "Paul Daly");
        }

        [TestMethod]
        public void DescribeRules_GroupMembership_ResolvesGroupName()
        {
            var g = Guid.NewGuid();
            var part = new GetRunExplanationHandler.QueryPartInfo
            {
                Type = "GroupMembership",
                Source = g.ToString(),
                IdentityTypes = IdentityTypeSet.GroupDefault
            };
            var names = new Dictionary<Guid, string> { [g] = "HR Team" };
            StringAssert.Contains(
                GetRunExplanationHandler.DescribeMembershipRulesForOwner(new[] { part }, null, names, null),
                "people and agent users from source group \"HR Team\"");
        }

        [TestMethod]
        public void DescribeRules_GroupMembership_UnresolvedGuid_UsesRawSource()
        {
            var g = Guid.NewGuid();
            var part = new GetRunExplanationHandler.QueryPartInfo { Type = "GroupMembership", Source = g.ToString() };
            StringAssert.Contains(
                GetRunExplanationHandler.DescribeMembershipRulesForOwner(new[] { part }, null, null, null),
                g.ToString());
        }

        [TestMethod]
        public void DescribeRules_GroupMembership_NullSource_NameUnavailable()
        {
            var part = new GetRunExplanationHandler.QueryPartInfo { Type = "GroupMembership", Source = null };
            StringAssert.Contains(
                GetRunExplanationHandler.DescribeMembershipRulesForOwner(new[] { part }, null, null, null),
                "whose name is unavailable");
        }

        [TestMethod]
        public void DescribeRules_GroupOwnership_UsesOwnersPhrase()
        {
            var part = new GetRunExplanationHandler.QueryPartInfo { Type = "GroupOwnership", Source = "not-a-guid" };
            StringAssert.Contains(
                GetRunExplanationHandler.DescribeMembershipRulesForOwner(new[] { part }, null, null, null),
                "owners of source group");
        }

        [TestMethod]
        public void DescribeRules_TeamsChannel_UsesChannelPhrase()
        {
            var part = new GetRunExplanationHandler.QueryPartInfo { Type = "TeamsChannelMembership", Source = "not-a-guid" };
            StringAssert.Contains(
                GetRunExplanationHandler.DescribeMembershipRulesForOwner(new[] { part }, null, null, null),
                "members of Teams channel");
        }

        [TestMethod]
        public void DescribeRules_PlaceMembership_UsesPlacePhrase()
        {
            var part = new GetRunExplanationHandler.QueryPartInfo { Type = "PlaceMembership" };
            StringAssert.Contains(
                GetRunExplanationHandler.DescribeMembershipRulesForOwner(new[] { part }, null, null, null),
                "users returned by the configured place criteria");
        }

        [TestMethod]
        public void DescribeRules_UnknownType_HumanizesTypeName()
        {
            var part = new GetRunExplanationHandler.QueryPartInfo { Type = "FooBarSource" };
            StringAssert.Contains(
                GetRunExplanationHandler.DescribeMembershipRulesForOwner(new[] { part }, null, null, null),
                "configured Foo Bar Source source");
        }

        // ---------------- DescribeQueryDiff ----------------

        [TestMethod]
        public void DescribeQueryDiff_BothNull_ReturnsEmpty()
            => Assert.AreEqual(string.Empty, GetRunExplanationHandler.DescribeQueryDiff(null, null));

        [TestMethod]
        public void DescribeQueryDiff_GroupSourceAdded()
        {
            var prev = "[{\"type\":\"GroupMembership\",\"source\":\"11111111-1111-1111-1111-111111111111\"}]";
            var curr = "[{\"type\":\"GroupMembership\",\"source\":\"11111111-1111-1111-1111-111111111111\"},{\"type\":\"GroupMembership\",\"source\":\"22222222-2222-2222-2222-222222222222\",\"identityTypes\":[\"agents\"]}]";
            var result = GetRunExplanationHandler.DescribeQueryDiff(prev, curr);

            StringAssert.Contains(result, "New inclusionary group source added");
            StringAssert.Contains(result, "selecting only agent users");
        }

        [TestMethod]
        public void DescribeQueryDiff_SqlRuleAdded()
        {
            var prev = "[{\"type\":\"GroupMembership\",\"source\":\"11111111-1111-1111-1111-111111111111\"}]";
            var curr = "[{\"type\":\"GroupMembership\",\"source\":\"11111111-1111-1111-1111-111111111111\"},{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Building = 'B40'\"}}]";
            var result = GetRunExplanationHandler.DescribeQueryDiff(prev, curr);
            StringAssert.Contains(result, "New inclusionary HR membership rule added");
            StringAssert.Contains(result, "Building is \"B40\"");
        }

        [TestMethod]
        public void DescribeQueryDiff_GroupSourceRemoved()
        {
            var prev = "[{\"type\":\"GroupMembership\",\"source\":\"11111111-1111-1111-1111-111111111111\"},{\"type\":\"GroupMembership\",\"source\":\"22222222-2222-2222-2222-222222222222\",\"identityTypes\":[\"users\"]}]";
            var curr = "[{\"type\":\"GroupMembership\",\"source\":\"11111111-1111-1111-1111-111111111111\"}]";
            var result = GetRunExplanationHandler.DescribeQueryDiff(prev, curr);

            StringAssert.Contains(result, "group source removed");
            StringAssert.Contains(result, "selected only people");
        }

        [TestMethod]
        public void DescribeQueryDiff_GroupSourceWithOmittedSelection_DescribesBothIdentityTypes()
        {
            const string prev = "[]";
            const string curr = "[{\"type\":\"GroupMembership\",\"source\":\"11111111-1111-1111-1111-111111111111\"}]";

            StringAssert.Contains(
                GetRunExplanationHandler.DescribeQueryDiff(prev, curr),
                "selecting people and agent users");
        }

        [TestMethod]
        public void DescribeQueryDiff_FilterModified()
        {
            var prev = "[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Building = 'B40'\"}}]";
            var curr = "[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Building = 'B41'\"}}]";
            StringAssert.Contains(GetRunExplanationHandler.DescribeQueryDiff(prev, curr),
                "Membership criteria changed from");
        }

        [TestMethod]
        public void DescribeQueryDiff_ExclusionaryFlipped()
        {
            var prev = "[{\"type\":\"GroupMembership\",\"source\":\"11111111-1111-1111-1111-111111111111\",\"exclusionary\":false}]";
            var curr = "[{\"type\":\"GroupMembership\",\"source\":\"11111111-1111-1111-1111-111111111111\",\"exclusionary\":true}]";
            StringAssert.Contains(GetRunExplanationHandler.DescribeQueryDiff(prev, curr),
                "changed from inclusionary to exclusionary");
        }

        [TestMethod]
        public void DescribeQueryDiff_ManagerScopeChanged()
        {
            var prev = "[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Building = 'B40'\",\"manager\":{\"id\":100}}}]";
            var curr = "[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Building = 'B40'\",\"manager\":{\"id\":200}}}]";
            StringAssert.Contains(GetRunExplanationHandler.DescribeQueryDiff(prev, curr),
                "Manager scope");
        }

        [TestMethod]
        public void DescribeQueryDiff_IdentitySelectionChanged()
        {
            var previous =
                "[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Building = 'B40'\"},\"identityTypes\":[\"users\"]}]";
            var current =
                "[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Building = 'B40'\"},\"identityTypes\":[\"users\",\"agents\"]}]";

            StringAssert.Contains(
                GetRunExplanationHandler.DescribeQueryDiff(previous, current),
                "Identity types changed from only employees to employees and the AI agents they own");
        }

        [TestMethod]
        public void DescribeQueryDiff_GroupIdentitySelectionChanged()
        {
            const string groupId = "11111111-1111-1111-1111-111111111111";
            var previous =
                $"[{{\"type\":\"GroupMembership\",\"source\":\"{groupId}\",\"identityTypes\":[\"users\"]}}]";
            var current =
                $"[{{\"type\":\"GroupMembership\",\"source\":\"{groupId}\",\"identityTypes\":[\"agents\"]}}]";

            StringAssert.Contains(
                GetRunExplanationHandler.DescribeQueryDiff(previous, current),
                "Identity types changed from only people to only agent users");
        }

        [TestMethod]
        public void DescribeQueryDiff_GroupDefaultAndOrderAreUnchanged()
        {
            const string groupId = "11111111-1111-1111-1111-111111111111";
            var omitted =
                $"[{{\"type\":\"GroupMembership\",\"source\":\"{groupId}\"}}]";
            var explicitBoth =
                $"[{{\"type\":\"GroupMembership\",\"source\":\"{groupId}\",\"identityTypes\":[\"agents\",\"users\"]}}]";

            Assert.AreEqual(
                string.Empty,
                GetRunExplanationHandler.DescribeQueryDiff(omitted, explicitBoth));
        }

        [TestMethod]
        public void DescribeQueryDiff_SameSourceWithIndependentSelections_IsDuplicateSafe()
        {
            const string groupId = "11111111-1111-1111-1111-111111111111";
            var previous =
                $"[" +
                $"{{\"type\":\"GroupMembership\",\"source\":\"{groupId}\",\"identityTypes\":[\"users\"]}}," +
                $"{{\"type\":\"GroupMembership\",\"source\":\"{groupId}\",\"identityTypes\":[\"agents\"]}}" +
                "]";
            var current =
                $"[" +
                $"{{\"type\":\"GroupMembership\",\"source\":\"{groupId}\",\"identityTypes\":[\"agents\"]}}," +
                $"{{\"type\":\"GroupMembership\",\"source\":\"{groupId}\",\"identityTypes\":[\"users\"]}}" +
                "]";

            Assert.AreEqual(
                string.Empty,
                GetRunExplanationHandler.DescribeQueryDiff(previous, current));
        }

        [TestMethod]
        public void DescribeQueryDiff_SameSourceSelectionChange_ReportsOnlyChangedPart()
        {
            const string groupId = "11111111-1111-1111-1111-111111111111";
            var previous =
                $"[" +
                $"{{\"type\":\"GroupMembership\",\"source\":\"{groupId}\",\"identityTypes\":[\"users\"]}}," +
                $"{{\"type\":\"GroupMembership\",\"source\":\"{groupId}\",\"identityTypes\":[\"agents\"]}}" +
                "]";
            var current =
                $"[" +
                $"{{\"type\":\"GroupMembership\",\"source\":\"{groupId}\",\"identityTypes\":[\"users\",\"agents\"]}}," +
                $"{{\"type\":\"GroupMembership\",\"source\":\"{groupId}\",\"identityTypes\":[\"agents\"]}}" +
                "]";

            var result =
                GetRunExplanationHandler.DescribeQueryDiff(previous, current);

            StringAssert.Contains(
                result,
                "Identity types changed from only people to people and agent users");
            Assert.AreEqual(
                1,
                result.Split(
                    "Identity types changed",
                    StringSplitOptions.None).Length - 1);
        }

        [TestMethod]
        public void DescribeQueryDiff_OtherTypeSourceAdded()
        {
            var prev = "[{\"type\":\"GroupMembership\",\"source\":\"11111111-1111-1111-1111-111111111111\"}]";
            var curr = "[{\"type\":\"GroupMembership\",\"source\":\"11111111-1111-1111-1111-111111111111\"},{\"type\":\"PlaceMembership\",\"source\":\"place-1\"}]";
            StringAssert.Contains(GetRunExplanationHandler.DescribeQueryDiff(prev, curr),
                "New inclusionary source added (type: PlaceMembership)");
        }

        // ---------------- First-run explanation (BuildInitialRunExplanation / SummarizeRulesInline) ----------------

        [TestMethod]
        public void BuildInitialRunExplanation_AddedAndRemoved_EstablishesMembership()
        {
            var result = InvokeStaticPrivate<string>(typeof(GetRunExplanationHandler),
                "BuildInitialRunExplanation", new object?[] { 5, 3, string.Empty });
            StringAssert.Contains(result!, "first sync for this job");
            StringAssert.Contains(result!, "established");
            StringAssert.Contains(result!, "its configured membership rules");
        }

        [TestMethod]
        public void BuildInitialRunExplanation_RemovedOnly_AlignsByRemoving()
        {
            var result = InvokeStaticPrivate<string>(typeof(GetRunExplanationHandler),
                "BuildInitialRunExplanation", new object?[] { 0, 4, string.Empty });
            StringAssert.Contains(result!, "removing pre-existing members");
        }

        [TestMethod]
        public void BuildInitialRunExplanation_AddedOnly_PopulatesGroup()
        {
            var result = InvokeStaticPrivate<string>(typeof(GetRunExplanationHandler),
                "BuildInitialRunExplanation", new object?[] { 6, 0, string.Empty });
            StringAssert.Contains(result!, "populated the destination group for the first time");
        }

        [TestMethod]
        public void BuildInitialRunExplanation_WithRulesInline_IncludesRules()
        {
            var rules = "- Rule 1: Includes employees where Building is \"B40\".";
            var result = InvokeStaticPrivate<string>(typeof(GetRunExplanationHandler),
                "BuildInitialRunExplanation", new object?[] { 6, 0, rules });
            StringAssert.Contains(result!, "its configured rules (");
            StringAssert.Contains(result!, "includes employees where");
        }

        [TestMethod]
        public void SummarizeRulesInline_Empty_ReturnsEmpty()
            => Assert.AreEqual(string.Empty, InvokeStaticPrivate<string>(typeof(GetRunExplanationHandler),
                "SummarizeRulesInline", new object?[] { "" }));

        [TestMethod]
        public void SummarizeRulesInline_TooManyRules_ReturnsEmpty()
        {
            var rules = string.Join("\n", new[] { "- Rule 1: A", "- Rule 2: B", "- Rule 3: C", "- Rule 4: D" });
            Assert.AreEqual(string.Empty, InvokeStaticPrivate<string>(typeof(GetRunExplanationHandler),
                "SummarizeRulesInline", new object?[] { rules }));
        }

        [TestMethod]
        public void SummarizeRulesInline_NoRulesConfigured_Filtered_ReturnsEmpty()
            => Assert.AreEqual(string.Empty, InvokeStaticPrivate<string>(typeof(GetRunExplanationHandler),
                "SummarizeRulesInline", new object?[] { "No membership rules configured." }));

        [TestMethod]
        public void SummarizeRulesInline_SingleRule_StripsPrefixLowercases()
        {
            var result = InvokeStaticPrivate<string>(typeof(GetRunExplanationHandler),
                "SummarizeRulesInline", new object?[] { "- Rule 1: Includes employees where Building is \"B40\"." });
            Assert.AreEqual("includes employees where Building is \"B40\"", result);
        }
    }
}
