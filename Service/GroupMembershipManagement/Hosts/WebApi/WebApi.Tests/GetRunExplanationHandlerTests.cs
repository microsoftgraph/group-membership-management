// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging.Abstractions;
using Models;
using Models.SyncJobChange;
using Moq;
using Repositories.Contracts;
using Services;
using Services.Contracts;
using Services.Messages.Requests;
using Services.WebApi.Contracts;
using System.Net;

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
        private Mock<IDataFactoryRepository> _mockDataFactoryRepository = null!;
        private Mock<IOpenAIService> _mockOpenAIService = null!;
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
            _mockDataFactoryRepository = new Mock<IDataFactoryRepository>();
            _mockOpenAIService = new Mock<IOpenAIService>();

            _handler = new GetRunExplanationHandler(
                NullLogger<GetRunExplanationHandler>.Instance,
                _mockSyncJobRepository.Object,
                _mockSyncJobHistoryRepository.Object,
                _mockSyncJobChangeRepository.Object,
                _mockGraphGroupRepository.Object,
                _mockBlobStorageRepository.Object,
                _mockSqlMembershipRepository.Object,
                _mockDataFactoryRepository.Object,
                _mockOpenAIService.Object);

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
                    ThresholdViolations = 0
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
                    ThresholdViolations = 0
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
                    ThresholdViolations = 0
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
                    ThresholdViolations = 0
                });

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("This sync completed successfully.", response.Explanation);
            _mockOpenAIService.Verify(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        [TestMethod]
        public async Task ExecuteAsync_ThresholdBlockedByViolationsDelta_CallsOpenAI()
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
                    ThresholdViolations = 3
                });

            _mockSyncJobHistoryRepository.Setup(x => x.GetBySyncJobIdAsync(_syncJobId, It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync(new List<global::Models.SyncJobHistory.SyncJobHistory>
                {
                    new() { RunId = Guid.NewGuid(), UpdatedAt = DateTime.UtcNow.AddHours(-1), ThresholdViolations = 1 }
                });

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            _mockOpenAIService.Verify(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
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
                    ThresholdViolations = 0
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
                    ThresholdViolations = 0
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
                    ThresholdViolations = 0
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
                    ThresholdViolations = 0
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
                    ThresholdViolations = 0
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
                    ThresholdViolations = 0
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
                    ThresholdViolations = 0
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

        // ---------------- QueryPartInfo.Key ----------------

        [TestMethod]
        public void QueryPartInfo_Key_WithSource_UsesTypeAndSource()
        {
            var part = new GetRunExplanationHandler.QueryPartInfo { Type = "GroupMembership", Source = "abc", Index = 5 };
            Assert.AreEqual("GroupMembership|abc", part.Key);
        }

        [TestMethod]
        public void QueryPartInfo_Key_NoSource_UsesTypeAndIndex()
        {
            var part = new GetRunExplanationHandler.QueryPartInfo { Type = "SqlMembership", Source = null, Index = 5 };
            Assert.AreEqual("SqlMembership|5", part.Key);
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
                    ThresholdViolations = 0
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
                    ThresholdViolations = 0
                });

            _mockSyncJobHistoryRepository.Setup(x => x.GetBySyncJobIdAsync(_syncJobId, It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync(new List<global::Models.SyncJobHistory.SyncJobHistory>
                {
                    new() { RunId = Guid.NewGuid(), UpdatedAt = previousRunTime, ThresholdViolations = 0 }
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
                    ThresholdViolations = 0
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
                    ThresholdViolations = 0,
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

            _mockDataFactoryRepository
                .Setup(x => x.GetMostRecentSucceededRunIdAsync())
                .ReturnsAsync(Guid.NewGuid().ToString());

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
                    ThresholdViolations = 0
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
                        ThresholdViolations = 0
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
                    ThresholdViolations = 0,
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
                        ThresholdViolations = 0
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
            string? capturedPrompt = null;

            _mockSyncJobRepository.Setup(x => x.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(new SyncJob
                {
                    Id = _syncJobId,
                    TargetOfficeGroupId = _targetGroupId,
                    Query = $"[" +
                        "{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Department = 'Engineering'\"}}," +
                        "{\"type\":\"SqlMembership\",\"source\":{\"manager\":{\"id\":42,\"depth\":2},\"filter\":\"EmployeeType = 'Vendor'\"},\"exclusionary\":true}," +
                        $"{{\"type\":\"GroupMembership\",\"source\":\"{inclusionaryGroup}\"}}," +
                        $"{{\"type\":\"GroupMembership\",\"source\":\"{exclusionaryGroup}\",\"exclusionary\":true}}" +
                        "]"
                });

            SetupCurrentRunAndPreviousRun(currentAdfRunId, previousAdfRunId, previousRunId, usersRemoved: 4);
            SetupAggregatedRemovedUsers(sqlDropped, sqlNewlyExcluded, groupLeft, groupNewlyExcluded);

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
            _mockSqlMembershipRepository.Setup(x => x.FilterChildEntitiesAsync("Department = 'Engineering'", currentTable))
                .ReturnsAsync(new List<SqlMembershipObtainer.Entities.PersonEntity>());
            _mockSqlMembershipRepository.Setup(x => x.FilterChildEntitiesAsync("Department = 'Engineering'", previousTable))
                .ReturnsAsync(new List<SqlMembershipObtainer.Entities.PersonEntity> { Person(sqlDropped) });
            _mockSqlMembershipRepository.Setup(x => x.GetChildEntitiesAsync("EmployeeType = 'Vendor'", 42, currentTable, 2))
                .ReturnsAsync(new List<SqlMembershipObtainer.Entities.PersonEntity> { Person(sqlNewlyExcluded) });
            _mockSqlMembershipRepository.Setup(x => x.GetChildEntitiesAsync("EmployeeType = 'Vendor'", 42, previousTable, 2))
                .ReturnsAsync(new List<SqlMembershipObtainer.Entities.PersonEntity>());
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
            StringAssert.Contains(capturedPrompt!, "left this source");
            StringAssert.Contains(capturedPrompt!, "newly appear in this excluded source");
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
            _mockSqlMembershipRepository.Verify(x => x.FilterChildEntitiesAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
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
            _mockSqlMembershipRepository.Verify(x => x.FilterChildEntitiesAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
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
                    new() { SyncJobId = _syncJobId, RunId = previousRunId, Status = "Idle", UpdatedAt = now.AddHours(-1), UsersRemoved = 0, ThresholdViolations = 0 },
                    new() { SyncJobId = _syncJobId, RunId = olderRunId, Status = "Idle", UpdatedAt = now.AddHours(-2), UsersRemoved = 0, ThresholdViolations = 0 }
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
            StringAssert.Contains(capturedPrompt!, "left this source");
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
                    new() { SyncJobId = _syncJobId, RunId = previousRunId, Status = "Idle", UpdatedAt = now.AddHours(-1), UsersRemoved = 0, ThresholdViolations = 0, AdfRunId = null }
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
            _mockSqlMembershipRepository.Verify(x => x.FilterChildEntitiesAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
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

            // Mark THIS run threshold-blocked (ThresholdViolations increased over the previous run) so it takes the blocked path.
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
                    ThresholdViolations = 1,
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
            _mockDataFactoryRepository.Setup(x => x.GetMostRecentSucceededRunIdAsync()).ReturnsAsync(latestRunId.ToString());
            _mockSqlMembershipRepository.Setup(x => x.FilterChildEntitiesAsync("Building = 'B40'", latestTable))
                .ReturnsAsync(new List<SqlMembershipObtainer.Entities.PersonEntity> { Person(Guid.NewGuid()) });
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
            _mockDataFactoryRepository.Setup(x => x.GetMostRecentSucceededRunIdAsync()).ReturnsAsync(latestRunId.ToString());
            // The removed user is still returned by the rule against the latest table, so the guard fires.
            _mockSqlMembershipRepository.Setup(x => x.FilterChildEntitiesAsync("Building = 'B40'", latestTable))
                .ReturnsAsync(new List<SqlMembershipObtainer.Entities.PersonEntity> { Person(removedUser) });
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
            // Phase 3 evaluates the WHOLE manager-rooted part against the latest table (via GetChildEntitiesAsync), so a user no longer in the chain is colored even if they still match the filter attribute.
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
            _mockDataFactoryRepository.Setup(x => x.GetMostRecentSucceededRunIdAsync()).ReturnsAsync(latestRunId.ToString());
            // The removed user is no longer in the management chain under 42 in the latest table.
            _mockSqlMembershipRepository.Setup(x => x.GetChildEntitiesAsync("EmployeeType = 'FTE'", 42, latestTable, 2))
                .ReturnsAsync(new List<SqlMembershipObtainer.Entities.PersonEntity> { Person(Guid.NewGuid()) });
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
            _mockSqlMembershipRepository.Setup(x => x.FilterChildEntitiesAsync("Building = 'B40'", currentTable))
                .ReturnsAsync(new List<SqlMembershipObtainer.Entities.PersonEntity>());
            _mockSqlMembershipRepository.Setup(x => x.FilterChildEntitiesAsync("Building = 'B40'", previousTable))
                .ReturnsAsync(new List<SqlMembershipObtainer.Entities.PersonEntity> { Person(removedUser) });
            _mockDataFactoryRepository.Setup(x => x.GetMostRecentSucceededRunIdAsync()).ReturnsAsync(latestRunId.ToString());
            // If Phase 3 wrongly ran, this latest-table match would let it emit color; the guard must prevent that.
            _mockSqlMembershipRepository.Setup(x => x.FilterChildEntitiesAsync("Building = 'B40'", latestTable))
                .ReturnsAsync(new List<SqlMembershipObtainer.Entities.PersonEntity> { Person(Guid.NewGuid()) });
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
            _mockDataFactoryRepository.Setup(x => x.GetMostRecentSucceededRunIdAsync()).ReturnsAsync((string?)null);
            _mockOpenAIService.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((_, prompt) => capturedPrompt = prompt)
                .ReturnsAsync("This sync completed successfully.");

            var response = await _handler.ExecuteAsync(BuildRequest());

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedPrompt);
            Assert.IsFalse(capturedPrompt!.Contains("in the latest HR data (current state, not the removal-time snapshot)"),
                "Phase 3 must be skipped when there is no latest ADF table.");
            StringAssert.Contains(capturedPrompt!, "no attributable source found");
            _mockSqlMembershipRepository.Verify(x => x.FilterChildEntitiesAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
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
                    ThresholdViolations = 1,
                    AdfRunId = currentAdfRunId
                });
            // Exact-run snapshot pruned (Phase 3 eligible) and the previous snapshot pruned too (single-step hedges).
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(currentTable)).ReturnsAsync(false);
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(previousTable)).ReturnsAsync(false);
            // Prior source blobs are gone so the walk-back also finds nothing.
            _mockBlobStorageRepository.Setup(x => x.FindPartFilesByRunIdAsync(_targetGroupId.ToString(), previousRunId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult>());
            // Latest ADF table is available and the proposed-removed user no longer matches the rule today.
            _mockDataFactoryRepository.Setup(x => x.GetMostRecentSucceededRunIdAsync()).ReturnsAsync(latestRunId.ToString());
            _mockSqlMembershipRepository.Setup(x => x.FilterChildEntitiesAsync("Building = 'B40'", latestTable))
                .ReturnsAsync(new List<SqlMembershipObtainer.Entities.PersonEntity> { Person(Guid.NewGuid()) });
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
                    ThresholdViolations = 0,
                    AdfRunId = null
                });
            // Prior source blobs are gone so the walk-back finds nothing.
            _mockBlobStorageRepository.Setup(x => x.FindPartFilesByRunIdAsync(_targetGroupId.ToString(), previousRunId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult>());
            // Latest ADF table is available and the removed user no longer matches the rule today.
            _mockDataFactoryRepository.Setup(x => x.GetMostRecentSucceededRunIdAsync()).ReturnsAsync(latestRunId.ToString());
            _mockSqlMembershipRepository.Setup(x => x.FilterChildEntitiesAsync("Building = 'B40'", latestTable))
                .ReturnsAsync(new List<SqlMembershipObtainer.Entities.PersonEntity> { Person(Guid.NewGuid()) });
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
                    ThresholdViolations = 0,
                    AdfRunId = null
                });
            _mockBlobStorageRepository.Setup(x => x.FindPartFilesByRunIdAsync(_targetGroupId.ToString(), previousRunId.ToString()))
                .ReturnsAsync(new Dictionary<string, BlobResult>());
            _mockDataFactoryRepository.Setup(x => x.GetMostRecentSucceededRunIdAsync()).ReturnsAsync(latestRunId.ToString());
            // The proposed-removed user is STILL in the management chain today, so the contradiction guard must fire.
            _mockSqlMembershipRepository.Setup(x => x.GetChildEntitiesAsync("EmployeeType = 'FTE'", 42, latestTable, 2))
                .ReturnsAsync(new List<SqlMembershipObtainer.Entities.PersonEntity> { Person(removedUser) });
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
                    ThresholdViolations = 1,
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
                    ThresholdViolations = 0,
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
                    ThresholdViolations = 0,
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
                    ThresholdViolations = 0,
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
                    ThresholdViolations = 1,
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
                    ThresholdViolations = 0,
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
                    ThresholdViolations = 1,
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
                    ThresholdViolations = 1,
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
                    ThresholdViolations = 0,
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
            StringAssert.Contains(response.Explanation!, "added people who newly match");
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
                    ThresholdViolations = 0,
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
            StringAssert.Contains(response.Explanation!, "added people who newly match");
            StringAssert.Contains(response.Explanation!, "removed people who were in the group but no longer match");
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
                    ThresholdViolations = 1,
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
            StringAssert.Contains(addsOnly, "added people who newly match");

            var mixed = InvokeStaticPrivate<string>(type, "BuildNonInitialRuleExplanation", new object?[] { 2, 3, false, rules })!;
            StringAssert.Contains(mixed, "added people who newly match");
            StringAssert.Contains(mixed, "removed people who were in the group but no longer match");
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
        public void ComputeWalkBackFloor_AllIdenticalResubmits_ReturnsMinValue()
        {
            // PR-comment #2: when every recorded change kept the same query (e.g. identical resubmits after a threshold block), there is no query boundary to floor at, so the removes walk-back should scan as far back as retained runs allow.
            var now = DateTime.UtcNow;
            var changes = new List<SyncJobChange>
            {
                ChangeWithQuery(now.AddHours(-1), "Q"),
                ChangeWithQuery(now.AddHours(-10), "Q"),
            };

            var floor = InvokeStaticPrivate<DateTime>(typeof(GetRunExplanationHandler), "ComputeWalkBackFloor", new object?[] { changes, "Q" });

            Assert.AreEqual(DateTime.MinValue, floor);
        }

        [TestMethod]
        public void ComputeWalkBackFloor_IdenticalResubmitThenDifferentQuery_FloorsAtQueryChangeNotResubmit()
        {
            // PR-comment #2: the most recent change is an identical resubmit; the floor must skip it and land on the older change that actually set the current query, so the whole same-query stretch stays inside the walk-back window and older removals remain attributable.
            var now = DateTime.UtcNow;
            var latestResubmit = now.AddHours(-1);
            var realQueryChange = now.AddHours(-30);
            var changes = new List<SyncJobChange>
            {
                ChangeWithQuery(latestResubmit, "Q"),
                ChangeWithQuery(now.AddHours(-5), "Q"),
                ChangeWithQuery(realQueryChange, "Q_OLD"),
            };

            var floor = InvokeStaticPrivate<DateTime>(typeof(GetRunExplanationHandler), "ComputeWalkBackFloor", new object?[] { changes, "Q" });

            Assert.AreEqual(realQueryChange, floor, "Floor must be the most recent DIFFERING-query change, not an identical resubmit.");
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
            _mockDataFactoryRepository.Setup(x => x.GetMostRecentSucceededRunIdAsync()).ReturnsAsync(latestRunId.ToString());
            // Part 1 reads successfully (removed user absent -> would be "colored" if we trusted the partial union)...
            _mockSqlMembershipRepository.Setup(x => x.FilterChildEntitiesAsync("Building = 'B40'", latestTable))
                .ReturnsAsync(new List<SqlMembershipObtainer.Entities.PersonEntity> { Person(Guid.NewGuid()) });
            // ...but part 2 FAILS to read against the latest table, so no combined current-state signal is safe.
            _mockSqlMembershipRepository.Setup(x => x.FilterChildEntitiesAsync("Department = 'Sales'", latestTable))
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
            _mockSqlMembershipRepository.Verify(x => x.FilterChildEntitiesAsync("Building = 'B40'", It.IsAny<string>()), Times.Never);
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

        // ---------------- Fix #1: New exclusionary source attribution for removes ----------------

        [TestMethod]
        public async Task ExecuteAsync_RemovesAttribution_NewExclusionarySourceAdded_EmitsAttributionLine()
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
                .ReturnsAsync(new HashSet<Guid> { removedUser });

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
            StringAssert.Contains(capturedPrompt!, "New exclusionary source added in the recent config update");
            StringAssert.Contains(capturedPrompt!, "NewExcludedSource");
            StringAssert.Contains(capturedPrompt!, "newly-excluded source");
        }

        // ---------------- Fix #2: Deleted exclusionary source attribution for adds ----------------

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
                    ThresholdViolations = 0,
                    AdfRunId = currentAdfRunId
                });
            _mockSyncJobHistoryRepository.Setup(x => x.GetBySyncJobIdAsync(_syncJobId, It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync(new List<global::Models.SyncJobHistory.SyncJobHistory>
                {
                    new() { SyncJobId = _syncJobId, RunId = previousRunId, Status = "Idle", UpdatedAt = DateTime.UtcNow.AddHours(-1),
                            StartTime = DateTime.UtcNow.AddHours(-1).AddMinutes(-5), EndTime = DateTime.UtcNow.AddHours(-1),
                            UsersAdded = 0, UsersRemoved = 0, ThresholdViolations = 0, AdfRunId = previousAdfRunId }
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
            StringAssert.Contains(capturedPrompt!, "Exclusionary source removed from query in the recent config update");
            StringAssert.Contains(capturedPrompt!, "DeletedExclusionarySource");
            StringAssert.Contains(capturedPrompt!, "previously excluded by this now-removed exclusion");
        }

        // ---------------- Fix #4: StartTime-preferring config cutoff ----------------

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

        private void SetupCurrentRunAndPreviousRun(Guid currentAdfRunId, Guid previousAdfRunId, Guid previousRunId, int usersRemoved)
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
                    UsersAdded = 0,
                    UsersRemoved = usersRemoved,
                    BeforeSyncUserCount = 100,
                    AfterSyncUserCount = 100 - usersRemoved,
                    ThresholdViolations = 0,
                    AdfRunId = currentAdfRunId
                });

            _mockSyncJobHistoryRepository.Setup(x => x.GetBySyncJobIdAsync(_syncJobId, It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync(new List<global::Models.SyncJobHistory.SyncJobHistory>
                {
                    new()
                    {
                        SyncJobId = _syncJobId,
                        RunId = previousRunId,
                        Status = "Idle",
                        UpdatedAt = now.AddHours(-1),
                        StartTime = now.AddHours(-1).AddMinutes(-5),
                        EndTime = now.AddHours(-1),
                        UsersAdded = 0,
                        UsersRemoved = 0,
                        ThresholdViolations = 0,
                        AdfRunId = previousAdfRunId
                    }
                });
        }

        private void SetupAggregatedRemovedUsers(params Guid[] removedUsers)
        {
            var members = string.Join(",", removedUsers.Select(id => $"{{\"ObjectId\":\"{id}\",\"MembershipAction\":2}}"));
            _mockBlobStorageRepository.Setup(x => x.FindAggregatedFileByRunIdAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new BlobResult { BlobStatus = BlobStatus.Found, Path = "test/removes.json" });
            _mockBlobStorageRepository.Setup(x => x.DownloadFileAsync("test/removes.json"))
                .ReturnsAsync(new BlobResult { BlobStatus = BlobStatus.Found, Content = $"{{\"SourceMembers\":[{members}]}}" });
        }

        private static SqlMembershipObtainer.Entities.PersonEntity Person(Guid azureObjectId) =>
            new()
            {
                PersonnelNumber = string.Empty,
                AzureObjectId = azureObjectId.ToString()
            };

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
            var part = new GetRunExplanationHandler.QueryPartInfo { Type = "GroupMembership", Source = g.ToString() };
            var names = new Dictionary<Guid, string> { [g] = "HR Team" };
            StringAssert.Contains(
                GetRunExplanationHandler.DescribeMembershipRulesForOwner(new[] { part }, null, names, null),
                "members of source group \"HR Team\"");
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
            var curr = "[{\"type\":\"GroupMembership\",\"source\":\"11111111-1111-1111-1111-111111111111\"},{\"type\":\"GroupMembership\",\"source\":\"22222222-2222-2222-2222-222222222222\"}]";
            StringAssert.Contains(GetRunExplanationHandler.DescribeQueryDiff(prev, curr),
                "New inclusionary group source added");
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
            var prev = "[{\"type\":\"GroupMembership\",\"source\":\"11111111-1111-1111-1111-111111111111\"},{\"type\":\"GroupMembership\",\"source\":\"22222222-2222-2222-2222-222222222222\"}]";
            var curr = "[{\"type\":\"GroupMembership\",\"source\":\"11111111-1111-1111-1111-111111111111\"}]";
            StringAssert.Contains(GetRunExplanationHandler.DescribeQueryDiff(prev, curr),
                "group source removed");
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
