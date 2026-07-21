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

        // Reflection helper for private static methods so we don't have to widen visibility.
        private static T? InvokeStaticPrivate<T>(Type type, string name, object?[] args)
        {
            var method = type.GetMethod(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(method, $"Method {name} not found on {type.Name}");
            var raw = method!.Invoke(null, args);
            return raw is null ? default : (T)raw;
        }
    }
}
