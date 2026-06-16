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
using Services.Messages.Responses;
using Services.WebApi;
using Services.WebApi.Contracts;
using System.Net;

namespace WebApi.Tests
{
    [TestClass]
    public class GetSyncExplanationHandlerTests
    {
        private Mock<IDatabaseSyncJobsRepository> _mockSyncJobRepository = null!;
        private Mock<ISyncJobHistoryRepository> _mockSyncJobHistoryRepository = null!;
        private Mock<ISyncJobChangeRepository> _mockSyncJobChangeRepository = null!;
        private Mock<IBlobStorageRepository> _mockBlobStorageRepository = null!;
        private Mock<IDataFactoryRepository> _mockDataFactoryRepository = null!;
        private Mock<ISqlMembershipRepository> _mockSqlMembershipRepository = null!;
        private Mock<IDatabaseSqlMembershipSourcesRepository> _mockSqlMembershipSourcesRepository = null!;
        private Mock<IGraphGroupRepository> _mockGraphGroupRepository = null!;
        private Mock<IOpenAIService> _mockOpenAIService = null!;
        private GetSyncExplanationHandler _handler = null!;

        private Guid _syncJobId;
        private Guid _targetGroupId;
        private Guid _runId;
        private Guid _userObjectId;
        private const string TestUserIdentity = "test-user-id";

        [TestInitialize]
        public void Initialize()
        {
            _mockSyncJobRepository = new Mock<IDatabaseSyncJobsRepository>();
            _mockSyncJobHistoryRepository = new Mock<ISyncJobHistoryRepository>();
            _mockSyncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            _mockBlobStorageRepository = new Mock<IBlobStorageRepository>();
            _mockDataFactoryRepository = new Mock<IDataFactoryRepository>();
            _mockSqlMembershipRepository = new Mock<ISqlMembershipRepository>();
            _mockSqlMembershipSourcesRepository = new Mock<IDatabaseSqlMembershipSourcesRepository>();
            _mockGraphGroupRepository = new Mock<IGraphGroupRepository>();
            _mockOpenAIService = new Mock<IOpenAIService>();

            _handler = new GetSyncExplanationHandler(
                NullLogger<GetSyncExplanationHandler>.Instance,
                _mockSyncJobRepository.Object,
                _mockSyncJobHistoryRepository.Object,
                _mockSyncJobChangeRepository.Object,
                _mockBlobStorageRepository.Object,
                _mockDataFactoryRepository.Object,
                _mockSqlMembershipRepository.Object,
                _mockSqlMembershipSourcesRepository.Object,
                _mockGraphGroupRepository.Object,
                _mockOpenAIService.Object);

            _syncJobId = Guid.NewGuid();
            _targetGroupId = Guid.NewGuid();
            _runId = Guid.NewGuid();
            _userObjectId = Guid.NewGuid();

            _mockSyncJobRepository
                .Setup(x => x.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(new SyncJob
                {
                    Id = _syncJobId,
                    TargetOfficeGroupId = _targetGroupId,
                    Query = "[{\"type\":\"SqlMembership\",\"filter\":\"Building = 'B40'\"}]"
                });

            _mockSyncJobHistoryRepository
                .Setup(x => x.GetByRunIdAsync(_runId))
                .ReturnsAsync(new global::Models.SyncJobHistory.SyncJobHistory
                {
                    SyncJobId = _syncJobId,
                    RunId = _runId,
                    Status = SyncStatus.Idle.ToString(),
                    StartTime = DateTime.UtcNow.AddMinutes(-10),
                    EndTime = DateTime.UtcNow,
                    UsersAdded = 5,
                    UsersRemoved = 2,
                    BeforeSyncUserCount = 100,
                    AfterSyncUserCount = 103
                });

            _mockSqlMembershipSourcesRepository
                .Setup(x => x.GetDefaultSourceAttributesAsync())
                .ReturnsAsync(new List<SqlMembershipAttribute>());

            _mockSyncJobChangeRepository
                .Setup(x => x.GetPageBySyncJobId(
                    It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(),
                    It.IsAny<SyncJobChangeSortingField>(), It.IsAny<bool>()))
                .ReturnsAsync(new RepositoryPage<SyncJobChange> { Items = new List<SyncJobChange>() });

            _mockSyncJobChangeRepository
                .Setup(x => x.GetRecentConfigChangesBySyncJobIdAsync(
                    It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<int>()))
                .ReturnsAsync(new List<SyncJobChange>());
        }

        [TestMethod]
        public async Task ExecuteAsync_ReturnsNotFound_WhenSyncJobDoesNotExist()
        {
            _mockSyncJobRepository
                .Setup(x => x.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync((SyncJob?)null);

            var response = await _handler.ExecuteAsync(
                new GetSyncExplanationRequest(_syncJobId, _runId, _userObjectId, TestUserIdentity, hasAiSyncJobRole: true));

            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        }

        [TestMethod]
        public async Task ExecuteAsync_ReturnsNotFound_WhenRunHistoryDoesNotExist()
        {
            _mockSyncJobHistoryRepository
                .Setup(x => x.GetByRunIdAsync(_runId))
                .ReturnsAsync((global::Models.SyncJobHistory.SyncJobHistory?)null);

            var response = await _handler.ExecuteAsync(
                new GetSyncExplanationRequest(_syncJobId, _runId, _userObjectId, TestUserIdentity, hasAiSyncJobRole: true));

            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        }

        [TestMethod]
        public async Task ExecuteAsync_ReturnsFallback_WhenBlobNotFound()
        {
            _mockBlobStorageRepository
                .Setup(x => x.FindAggregatedFileByRunIdAsync(
                    _targetGroupId.ToString(), _runId.ToString()))
                .ReturnsAsync(new BlobResult { BlobStatus = BlobStatus.NotFound });

            var response = await _handler.ExecuteAsync(
                new GetSyncExplanationRequest(_syncJobId, _runId, _userObjectId, TestUserIdentity, hasAiSyncJobRole: true));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("The specific reason could not be determined from the available data.", response.Explanation);
        }

        [TestMethod]
        public async Task ExecuteAsync_ReturnsFallback_WhenUserNotInBlob()
        {
            var otherUserId = Guid.NewGuid();
            var blobPath = $"{_targetGroupId}/{_runId}_Aggregated.json";
            var json = $"{{\"SourceMembers\":[{{\"ObjectId\":\"{otherUserId}\",\"MembershipAction\":\"Add\"}}]}}";

            _mockBlobStorageRepository
                .Setup(x => x.FindAggregatedFileByRunIdAsync(
                    _targetGroupId.ToString(), _runId.ToString()))
                .ReturnsAsync(new BlobResult { BlobStatus = BlobStatus.Found, Path = blobPath });

            _mockBlobStorageRepository
                .Setup(x => x.DownloadFileAsync(blobPath))
                .ReturnsAsync(new BlobResult { BlobStatus = BlobStatus.Found, Content = json });

            var response = await _handler.ExecuteAsync(
                new GetSyncExplanationRequest(_syncJobId, _runId, _userObjectId, TestUserIdentity, hasAiSyncJobRole: true));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("The specific reason could not be determined from the available data.", response.Explanation);
        }

        [TestMethod]
        public async Task ExecuteAsync_CallsOpenAI_WhenUserFoundInBlob()
        {
            SetupBlobWithUser(MembershipAction.Add);
            SetupAdfData(new Dictionary<string, string> { { "Building", "B40" } });

            _mockOpenAIService
                .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync("The user was added because their Building property matches the filter.");

            var response = await _handler.ExecuteAsync(
                new GetSyncExplanationRequest(_syncJobId, _runId, _userObjectId, TestUserIdentity, hasAiSyncJobRole: true));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("The user was added because their Building property matches the filter.", response.Explanation);
            _mockOpenAIService.Verify(x => x.GetCompletionAsync(
                It.IsAny<string>(), It.Is<string>(p => p.Contains("Added"))), Times.Once);
        }

        [TestMethod]
        public async Task ExecuteAsync_ReturnsRemovedVerb_WhenUserRemovedInBlob()
        {
            SetupBlobWithUser(MembershipAction.Remove);
            SetupAdfData(new Dictionary<string, string> { { "Building", "B99" } });

            _mockOpenAIService
                .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync("The user was removed because their building changed.");

            var response = await _handler.ExecuteAsync(
                new GetSyncExplanationRequest(_syncJobId, _runId, _userObjectId, TestUserIdentity, hasAiSyncJobRole: true));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            _mockOpenAIService.Verify(x => x.GetCompletionAsync(
                It.IsAny<string>(), It.Is<string>(p => p.Contains("Removed"))), Times.Once);
        }

        [TestMethod]
        public async Task ExecuteAsync_ReturnsFallback_WhenOpenAITimesOut()
        {
            SetupBlobWithUser(MembershipAction.Add);
            SetupAdfData(new Dictionary<string, string> { { "Building", "B40" } });

            _mockOpenAIService
                .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new TimeoutException("Request timed out"));

            var response = await _handler.ExecuteAsync(
                new GetSyncExplanationRequest(_syncJobId, _runId, _userObjectId, TestUserIdentity, hasAiSyncJobRole: true));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("The specific reason could not be determined from the available data.", response.Explanation);
        }

        [TestMethod]
        public async Task ExecuteAsync_ReturnsInternalServerError_OnUnexpectedException()
        {
            // Throw at repository level before blob lookup, so it's not caught by inner try/catch
            _mockSyncJobRepository
                .Setup(x => x.GetSyncJobAsync(_syncJobId))
                .ThrowsAsync(new InvalidOperationException("Unexpected error"));

            var response = await _handler.ExecuteAsync(
                new GetSyncExplanationRequest(_syncJobId, _runId, _userObjectId, TestUserIdentity, hasAiSyncJobRole: true));

            Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        }

        [TestMethod]
        public async Task ExecuteAsync_ReturnsFallback_WhenOpenAIReturnsEmpty()
        {
            SetupBlobWithUser(MembershipAction.Add);
            SetupAdfData(new Dictionary<string, string> { { "Building", "B40" } });

            _mockOpenAIService
                .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync("");

            var response = await _handler.ExecuteAsync(
                new GetSyncExplanationRequest(_syncJobId, _runId, _userObjectId, TestUserIdentity, hasAiSyncJobRole: true));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("The specific reason could not be determined from the available data.", response.Explanation);
        }

        [TestMethod]
        public async Task ExecuteAsync_IncludesConfigChanges_WhenPresent()
        {
            SetupBlobWithUser(MembershipAction.Add);
            SetupAdfData(new Dictionary<string, string> { { "Building", "B40" } });

            var runHistory = new global::Models.SyncJobHistory.SyncJobHistory
            {
                SyncJobId = _syncJobId,
                RunId = _runId,
                Status = SyncStatus.Idle.ToString(),
                StartTime = DateTime.UtcNow.AddMinutes(-10),
                EndTime = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                UsersAdded = 5,
                UsersRemoved = 0,
                BeforeSyncUserCount = 100,
                AfterSyncUserCount = 105
            };

            _mockSyncJobHistoryRepository
                .Setup(x => x.GetByRunIdAsync(_runId))
                .ReturnsAsync(runHistory);

            _mockSyncJobChangeRepository
                .Setup(x => x.GetPageBySyncJobId(
                    _syncJobId, It.IsAny<int>(), It.IsAny<int>(),
                    It.IsAny<SyncJobChangeSortingField>(), It.IsAny<bool>()))
                .ReturnsAsync(new RepositoryPage<SyncJobChange>
                {
                    Items = new List<SyncJobChange>
                    {
                        new SyncJobChange
                        {
                            ChangeTime = DateTime.UtcNow.AddMinutes(-5),
                            ChangeReason = "Update",
                            ChangedByDisplayName = "Admin User",
                            BusinessJustification = "Updated building filter"
                        }
                    }
                });

            string? capturedUserPrompt = null;
            _mockOpenAIService
                .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((sys, user) => capturedUserPrompt = user)
                .ReturnsAsync("The user was added due to a recent configuration change.");

            var response = await _handler.ExecuteAsync(
                new GetSyncExplanationRequest(_syncJobId, _runId, _userObjectId, TestUserIdentity, hasAiSyncJobRole: true));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedUserPrompt);
            Assert.IsTrue(capturedUserPrompt!.Contains("Admin User"),
                "Config change details should be included in prompt");
            Assert.IsTrue(capturedUserPrompt.Contains("Updated building filter"),
                "Business justification should be included in prompt");
        }

        [TestMethod]
        public async Task ExecuteAsync_IncludesConfigDiff_WhenFilterChanged()
        {
            SetupBlobWithUser(MembershipAction.Remove);
            SetupAdfData(new Dictionary<string, string> { { "Building", "B99" } });

            var previousChangeDetails = "{\"Query\":\"[{\\\"type\\\":\\\"SqlMembership\\\",\\\"filter\\\":\\\"Building = 'B40'\\\"}]\"}";
            var currentChangeDetails = "{\"Query\":\"[{\\\"type\\\":\\\"SqlMembership\\\",\\\"filter\\\":\\\"Building = 'B50'\\\"}]\"}";

            _mockSyncJobChangeRepository
                .Setup(x => x.GetRecentConfigChangesBySyncJobIdAsync(
                    _syncJobId, It.IsAny<DateTime>(), It.IsAny<int>()))
                .ReturnsAsync(new List<SyncJobChange>
                {
                    new SyncJobChange
                    {
                        ChangeTime = DateTime.UtcNow.AddDays(-1),
                        ChangeReason = "Update",
                        ChangedByDisplayName = "Admin User",
                        ChangeDetails = currentChangeDetails
                    },
                    new SyncJobChange
                    {
                        ChangeTime = DateTime.UtcNow.AddDays(-10),
                        ChangeReason = "Onboarding",
                        ChangedByDisplayName = "Admin User",
                        ChangeDetails = previousChangeDetails
                    }
                });

            string? capturedUserPrompt = null;
            _mockOpenAIService
                .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((sys, user) => capturedUserPrompt = user)
                .ReturnsAsync("The user was removed due to a filter change.");

            var response = await _handler.ExecuteAsync(
                new GetSyncExplanationRequest(_syncJobId, _runId, _userObjectId, TestUserIdentity, hasAiSyncJobRole: true));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedUserPrompt);
            Assert.IsTrue(capturedUserPrompt!.Contains("Previous configuration:"),
                "Prompt should contain previous configuration when config changed");
            Assert.IsTrue(capturedUserPrompt.Contains("Current configuration:"),
                "Prompt should contain current configuration when config changed");
            Assert.IsTrue(capturedUserPrompt.Contains("Configuration history:"),
                "Prompt should contain configuration history section");
            Assert.IsTrue(capturedUserPrompt.Contains("What changed:"),
                "Prompt should contain structural diff section");
            Assert.IsTrue(capturedUserPrompt.Contains("Filter changed from"),
                "Prompt should describe the specific filter change");
        }

        [TestMethod]
        public async Task ExecuteAsync_ShowsUnchangedFilter_WhenQueryNotModified()
        {
            SetupBlobWithUser(MembershipAction.Add);
            SetupAdfData(new Dictionary<string, string> { { "Building", "B40" } });

            var sameDetails = "{\"Query\":\"[{\\\"type\\\":\\\"SqlMembership\\\",\\\"filter\\\":\\\"Building = 'B40'\\\"}]\"}";

            _mockSyncJobChangeRepository
                .Setup(x => x.GetRecentConfigChangesBySyncJobIdAsync(
                    _syncJobId, It.IsAny<DateTime>(), It.IsAny<int>()))
                .ReturnsAsync(new List<SyncJobChange>
                {
                    new SyncJobChange
                    {
                        ChangeTime = DateTime.UtcNow.AddDays(-1),
                        ChangeReason = "Update",
                        ChangedByDisplayName = "Admin User",
                        ChangeDetails = sameDetails
                    },
                    new SyncJobChange
                    {
                        ChangeTime = DateTime.UtcNow.AddDays(-10),
                        ChangeReason = "Onboarding",
                        ChangedByDisplayName = "Admin User",
                        ChangeDetails = sameDetails
                    }
                });

            string? capturedUserPrompt = null;
            _mockOpenAIService
                .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((sys, user) => capturedUserPrompt = user)
                .ReturnsAsync("The user was added because their Building matches the filter.");

            var response = await _handler.ExecuteAsync(
                new GetSyncExplanationRequest(_syncJobId, _runId, _userObjectId, TestUserIdentity, hasAiSyncJobRole: true));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedUserPrompt);
            Assert.IsTrue(capturedUserPrompt!.Contains("Configuration (unchanged)"),
                "Prompt should indicate config was unchanged when queries match");
            Assert.IsFalse(capturedUserPrompt.Contains("Previous configuration:"),
                "Prompt should not show previous/current diff when config unchanged");
        }

        [TestMethod]
        public async Task ExecuteAsync_ShowsInitialFilter_WhenOnlyOneConfigChange()
        {
            SetupBlobWithUser(MembershipAction.Add);
            SetupAdfData(new Dictionary<string, string> { { "Building", "B40" } });

            var onboardingDetails = "{\"Query\":\"[{\\\"type\\\":\\\"SqlMembership\\\",\\\"filter\\\":\\\"Building = 'B40'\\\"}]\"}";

            _mockSyncJobChangeRepository
                .Setup(x => x.GetRecentConfigChangesBySyncJobIdAsync(
                    _syncJobId, It.IsAny<DateTime>(), It.IsAny<int>()))
                .ReturnsAsync(new List<SyncJobChange>
                {
                    new SyncJobChange
                    {
                        ChangeTime = DateTime.UtcNow.AddDays(-5),
                        ChangeReason = "Onboarding",
                        ChangedByDisplayName = "Admin User",
                        ChangeDetails = onboardingDetails
                    }
                });

            string? capturedUserPrompt = null;
            _mockOpenAIService
                .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((sys, user) => capturedUserPrompt = user)
                .ReturnsAsync("The user was added.");

            var response = await _handler.ExecuteAsync(
                new GetSyncExplanationRequest(_syncJobId, _runId, _userObjectId, TestUserIdentity, hasAiSyncJobRole: true));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedUserPrompt);
            Assert.IsTrue(capturedUserPrompt!.Contains("Initial configuration:"),
                "Prompt should show 'Initial configuration' when only onboarding change exists");
        }

        [TestMethod]
        public async Task ExecuteAsync_HandlesNullChangeDetails_Gracefully()
        {
            SetupBlobWithUser(MembershipAction.Add);
            SetupAdfData(new Dictionary<string, string> { { "Building", "B40" } });

            _mockSyncJobChangeRepository
                .Setup(x => x.GetRecentConfigChangesBySyncJobIdAsync(
                    _syncJobId, It.IsAny<DateTime>(), It.IsAny<int>()))
                .ReturnsAsync(new List<SyncJobChange>
                {
                    new SyncJobChange
                    {
                        ChangeTime = DateTime.UtcNow.AddDays(-1),
                        ChangeReason = "Update",
                        ChangedByDisplayName = "Admin User",
                        ChangeDetails = null
                    }
                });

            _mockOpenAIService
                .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync("The user was added.");

            var response = await _handler.ExecuteAsync(
                new GetSyncExplanationRequest(_syncJobId, _runId, _userObjectId, TestUserIdentity, hasAiSyncJobRole: true));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("The user was added.", response.Explanation);
        }

        [TestMethod]
        public async Task ExecuteAsync_DescribesNewGroupSourceAdded()
        {
            SetupBlobWithUser(MembershipAction.Add);
            SetupAdfData(new Dictionary<string, string> { { "Building", "B40" } });

            var sourceGroupId = Guid.NewGuid();
            var previousChangeDetails = "{\"Query\":\"[{\\\"type\\\":\\\"SqlMembership\\\",\\\"filter\\\":\\\"Building = 'B40'\\\"}]\"}";
            var currentChangeDetails = $"{{\"Query\":\"[{{\\\"type\\\":\\\"SqlMembership\\\",\\\"filter\\\":\\\"Building = 'B40'\\\"}},{{\\\"type\\\":\\\"GroupMembership\\\",\\\"source\\\":\\\"{sourceGroupId}\\\"}}]\"}}";

            _mockSyncJobChangeRepository
                .Setup(x => x.GetRecentConfigChangesBySyncJobIdAsync(
                    _syncJobId, It.IsAny<DateTime>(), It.IsAny<int>()))
                .ReturnsAsync(new List<SyncJobChange>
                {
                    new SyncJobChange
                    {
                        ChangeTime = DateTime.UtcNow.AddDays(-1),
                        ChangeReason = "Update",
                        ChangedByDisplayName = "Admin User",
                        ChangeDetails = currentChangeDetails
                    },
                    new SyncJobChange
                    {
                        ChangeTime = DateTime.UtcNow.AddDays(-10),
                        ChangeReason = "Onboarding",
                        ChangedByDisplayName = "Admin User",
                        ChangeDetails = previousChangeDetails
                    }
                });

            string? capturedUserPrompt = null;
            _mockOpenAIService
                .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((sys, user) => capturedUserPrompt = user)
                .ReturnsAsync("The user was added because a new group was included.");

            var response = await _handler.ExecuteAsync(
                new GetSyncExplanationRequest(_syncJobId, _runId, _userObjectId, TestUserIdentity, hasAiSyncJobRole: true));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedUserPrompt);
            Assert.IsTrue(capturedUserPrompt!.Contains("New inclusionary group source added"),
                "Prompt should describe the new group source that was added");
            Assert.IsTrue(capturedUserPrompt.Contains(sourceGroupId.ToString()),
                "Prompt should include the source group ID");
        }

        [TestMethod]
        public async Task ExecuteAsync_DescribesNewExclusionaryPartAdded()
        {
            SetupBlobWithUser(MembershipAction.Remove);
            SetupAdfData(new Dictionary<string, string> { { "Building", "B40" } });

            var previousChangeDetails = "{\"Query\":\"[{\\\"type\\\":\\\"SqlMembership\\\",\\\"filter\\\":\\\"Building = 'B40'\\\"}]\"}";
            var currentChangeDetails = "{\"Query\":\"[{\\\"type\\\":\\\"SqlMembership\\\",\\\"filter\\\":\\\"Building = 'B40'\\\"},{\\\"type\\\":\\\"SqlMembership\\\",\\\"filter\\\":\\\"Department = 'Sales'\\\",\\\"exclusionary\\\":true}]\"}";

            _mockSyncJobChangeRepository
                .Setup(x => x.GetRecentConfigChangesBySyncJobIdAsync(
                    _syncJobId, It.IsAny<DateTime>(), It.IsAny<int>()))
                .ReturnsAsync(new List<SyncJobChange>
                {
                    new SyncJobChange
                    {
                        ChangeTime = DateTime.UtcNow.AddDays(-1),
                        ChangeReason = "Update",
                        ChangedByDisplayName = "Admin User",
                        ChangeDetails = currentChangeDetails
                    },
                    new SyncJobChange
                    {
                        ChangeTime = DateTime.UtcNow.AddDays(-10),
                        ChangeReason = "Onboarding",
                        ChangedByDisplayName = "Admin User",
                        ChangeDetails = previousChangeDetails
                    }
                });

            string? capturedUserPrompt = null;
            _mockOpenAIService
                .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((sys, user) => capturedUserPrompt = user)
                .ReturnsAsync("The user was removed because a new exclusion rule was added.");

            var response = await _handler.ExecuteAsync(
                new GetSyncExplanationRequest(_syncJobId, _runId, _userObjectId, TestUserIdentity, hasAiSyncJobRole: true));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedUserPrompt);
            Assert.IsTrue(capturedUserPrompt!.Contains("New exclusionary HR/SQL filter source added"),
                "Prompt should describe the new exclusionary source");
            Assert.IsTrue(capturedUserPrompt.Contains("Department = 'Sales'"),
                "Prompt should include the exclusion filter criteria");
        }

        [TestMethod]
        public async Task ExecuteAsync_DescribesGroupSourceRemoved()
        {
            SetupBlobWithUser(MembershipAction.Remove);
            SetupAdfData(new Dictionary<string, string> { { "Building", "B40" } });

            var sourceGroupId = Guid.NewGuid();
            var previousChangeDetails = $"{{\"Query\":\"[{{\\\"type\\\":\\\"SqlMembership\\\",\\\"filter\\\":\\\"Building = 'B40'\\\"}},{{\\\"type\\\":\\\"GroupMembership\\\",\\\"source\\\":\\\"{sourceGroupId}\\\"}}]\"}}";
            var currentChangeDetails = "{\"Query\":\"[{\\\"type\\\":\\\"SqlMembership\\\",\\\"filter\\\":\\\"Building = 'B40'\\\"}]\"}";

            _mockSyncJobChangeRepository
                .Setup(x => x.GetRecentConfigChangesBySyncJobIdAsync(
                    _syncJobId, It.IsAny<DateTime>(), It.IsAny<int>()))
                .ReturnsAsync(new List<SyncJobChange>
                {
                    new SyncJobChange
                    {
                        ChangeTime = DateTime.UtcNow.AddDays(-1),
                        ChangeReason = "Update",
                        ChangedByDisplayName = "Admin User",
                        ChangeDetails = currentChangeDetails
                    },
                    new SyncJobChange
                    {
                        ChangeTime = DateTime.UtcNow.AddDays(-10),
                        ChangeReason = "Onboarding",
                        ChangedByDisplayName = "Admin User",
                        ChangeDetails = previousChangeDetails
                    }
                });

            string? capturedUserPrompt = null;
            _mockOpenAIService
                .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((sys, user) => capturedUserPrompt = user)
                .ReturnsAsync("The user was removed because a group source was removed.");

            var response = await _handler.ExecuteAsync(
                new GetSyncExplanationRequest(_syncJobId, _runId, _userObjectId, TestUserIdentity, hasAiSyncJobRole: true));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedUserPrompt);
            Assert.IsTrue(capturedUserPrompt!.Contains("group source removed"),
                "Prompt should describe the removed group source");
            Assert.IsTrue(capturedUserPrompt.Contains(sourceGroupId.ToString()),
                "Prompt should include the removed source group ID");
        }

        [TestMethod]
        public async Task OwnerWithoutAiRole_ReturnsOk()
        {
            SetupBlobWithUser(MembershipAction.Add);

            _mockGraphGroupRepository
                .Setup(x => x.IsEmailRecipientOwnerOfGroupAsync(TestUserIdentity, _targetGroupId, It.IsAny<bool>()))
                .ReturnsAsync(true);

            _mockOpenAIService
                .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync("Explanation text");

            var response = await _handler.ExecuteAsync(
                new GetSyncExplanationRequest(_syncJobId, _runId, _userObjectId, TestUserIdentity, hasAiSyncJobRole: false));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(response.Explanation);
        }

        [TestMethod]
        public async Task NonOwnerWithoutAiRole_ReturnsForbidden()
        {
            _mockGraphGroupRepository
                .Setup(x => x.IsEmailRecipientOwnerOfGroupAsync(TestUserIdentity, _targetGroupId, It.IsAny<bool>()))
                .ReturnsAsync(false);

            var response = await _handler.ExecuteAsync(
                new GetSyncExplanationRequest(_syncJobId, _runId, _userObjectId, TestUserIdentity, hasAiSyncJobRole: false));

            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [TestMethod]
        public async Task NonOwnerWithAiRole_ReturnsOk()
        {
            SetupBlobWithUser(MembershipAction.Add);

            _mockOpenAIService
                .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync("Explanation text");

            var response = await _handler.ExecuteAsync(
                new GetSyncExplanationRequest(_syncJobId, _runId, _userObjectId, TestUserIdentity, hasAiSyncJobRole: true));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            _mockGraphGroupRepository.Verify(
                x => x.IsEmailRecipientOwnerOfGroupAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<bool>()),
                Times.Never,
                "Should not check ownership when user has AI role");
        }

        private void SetupBlobWithUser(MembershipAction action)
        {
            var blobPath = $"{_targetGroupId}/{_runId}_Aggregated.json";
            var actionValue = (int)action;
            var json = $"{{\"SourceMembers\":[{{\"ObjectId\":\"{_userObjectId}\",\"MembershipAction\":{actionValue}}}]}}";

            _mockBlobStorageRepository
                .Setup(x => x.FindAggregatedFileByRunIdAsync(
                    _targetGroupId.ToString(), _runId.ToString()))
                .ReturnsAsync(new BlobResult { BlobStatus = BlobStatus.Found, Path = blobPath });

            _mockBlobStorageRepository
                .Setup(x => x.DownloadFileAsync(blobPath))
                .ReturnsAsync(new BlobResult { BlobStatus = BlobStatus.Found, Content = json });
        }

        [TestMethod]
        public async Task ExecuteAsync_UsesAdfRunIdFromHistory_WhenAvailable()
        {
            var specificAdfRunId = Guid.NewGuid();
            var tableName = specificAdfRunId.ToString().Replace("-", "");

            var runHistory = new global::Models.SyncJobHistory.SyncJobHistory
            {
                SyncJobId = _syncJobId,
                RunId = _runId,
                Status = SyncStatus.Idle.ToString(),
                StartTime = DateTime.UtcNow.AddMinutes(-10),
                EndTime = DateTime.UtcNow,
                UsersAdded = 5,
                UsersRemoved = 2,
                BeforeSyncUserCount = 100,
                AfterSyncUserCount = 103,
                AdfRunId = specificAdfRunId
            };

            _mockSyncJobHistoryRepository
                .Setup(x => x.GetByRunIdAsync(_runId))
                .ReturnsAsync(runHistory);

            SetupBlobWithUser(MembershipAction.Add);

            _mockSqlMembershipRepository
                .Setup(x => x.CheckIfTableExistsAsync(tableName))
                .ReturnsAsync(true);

            _mockSqlMembershipRepository
                .Setup(x => x.GetUserAttributesAsync(
                    _userObjectId.ToString(), tableName))
                .ReturnsAsync(new Dictionary<string, string> { { "Building", "B40" } });

            _mockOpenAIService
                .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync("User was added because their Building is B40.");

            var response = await _handler.ExecuteAsync(
                new GetSyncExplanationRequest(_syncJobId, _runId, _userObjectId, TestUserIdentity, hasAiSyncJobRole: true));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("User was added because their Building is B40.", response.Explanation);

            _mockDataFactoryRepository.Verify(
                x => x.GetMostRecentSucceededRunIdAsync(), Times.Never,
                "Should use AdfRunId from history instead of fetching the most recent run.");

            _mockSqlMembershipRepository.Verify(
                x => x.CheckIfTableExistsAsync(tableName), Times.Once,
                "Should look up the table using the AdfRunId from history.");

            _mockSqlMembershipRepository.Verify(
                x => x.GetUserAttributesAsync(_userObjectId.ToString(), tableName), Times.Once,
                "Should fetch user attributes from the table matching the AdfRunId from history.");
        }

        [TestMethod]
        public async Task ExecuteAsync_ReturnsAdfUnavailable_WhenAdfRunIdIsNull()
        {
            var runHistory = new global::Models.SyncJobHistory.SyncJobHistory
            {
                SyncJobId = _syncJobId,
                RunId = _runId,
                Status = SyncStatus.Idle.ToString(),
                StartTime = DateTime.UtcNow.AddMinutes(-10),
                EndTime = DateTime.UtcNow,
                UsersAdded = 5,
                UsersRemoved = 2,
                BeforeSyncUserCount = 100,
                AfterSyncUserCount = 103,
                AdfRunId = null
            };

            _mockSyncJobHistoryRepository
                .Setup(x => x.GetByRunIdAsync(_runId))
                .ReturnsAsync(runHistory);

            SetupBlobWithUser(MembershipAction.Add);

            _mockOpenAIService
                .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.Is<string>(p => p.Contains("ADF data unavailable."))))
                .ReturnsAsync("Unable to determine the exact reason due to unavailable HR data.");

            var response = await _handler.ExecuteAsync(
                new GetSyncExplanationRequest(_syncJobId, _runId, _userObjectId, TestUserIdentity, hasAiSyncJobRole: true));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

            _mockDataFactoryRepository.Verify(
                x => x.GetMostRecentSucceededRunIdAsync(), Times.Never,
                "Should not attempt fallback when AdfRunId is null.");
        }

        [TestMethod]
        public async Task ExecuteAsync_ReturnsAdfUnavailable_WhenAdfRunIdIsEmpty()
        {
            var runHistory = new global::Models.SyncJobHistory.SyncJobHistory
            {
                SyncJobId = _syncJobId,
                RunId = _runId,
                Status = SyncStatus.Idle.ToString(),
                StartTime = DateTime.UtcNow.AddMinutes(-10),
                EndTime = DateTime.UtcNow,
                UsersAdded = 5,
                UsersRemoved = 2,
                BeforeSyncUserCount = 100,
                AfterSyncUserCount = 103,
                AdfRunId = Guid.Empty
            };

            _mockSyncJobHistoryRepository
                .Setup(x => x.GetByRunIdAsync(_runId))
                .ReturnsAsync(runHistory);

            SetupBlobWithUser(MembershipAction.Add);

            _mockOpenAIService
                .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.Is<string>(p => p.Contains("ADF data unavailable."))))
                .ReturnsAsync("Unable to determine the exact reason due to unavailable HR data.");

            var response = await _handler.ExecuteAsync(
                new GetSyncExplanationRequest(_syncJobId, _runId, _userObjectId, TestUserIdentity, hasAiSyncJobRole: true));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

            _mockDataFactoryRepository.Verify(
                x => x.GetMostRecentSucceededRunIdAsync(), Times.Never,
                "Should not attempt fallback when AdfRunId is Guid.Empty.");
        }

        private void SetupAdfData(Dictionary<string, string> userAttributes)
        {
            var adfRunId = Guid.NewGuid().ToString();
            var tableName = adfRunId.Replace("-", "");

            _mockDataFactoryRepository
                .Setup(x => x.GetMostRecentSucceededRunIdAsync())
                .ReturnsAsync(adfRunId);

            _mockSqlMembershipRepository
                .Setup(x => x.CheckIfTableExistsAsync(tableName))
                .ReturnsAsync(true);

            _mockSqlMembershipRepository
                .Setup(x => x.GetUserAttributesAsync(
                    _userObjectId.ToString(), tableName))
                .ReturnsAsync(userAttributes);
        }
    }
}
