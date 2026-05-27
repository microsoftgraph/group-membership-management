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
        private Mock<IOpenAIService> _mockOpenAIService = null!;
        private GetSyncExplanationHandler _handler = null!;

        private Guid _syncJobId;
        private Guid _targetGroupId;
        private Guid _runId;
        private Guid _userObjectId;

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
        }

        [TestMethod]
        public async Task ExecuteAsync_ReturnsNotFound_WhenSyncJobDoesNotExist()
        {
            _mockSyncJobRepository
                .Setup(x => x.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync((SyncJob?)null);

            var response = await _handler.ExecuteAsync(
                new GetSyncExplanationRequest(_syncJobId, _runId, _userObjectId));

            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        }

        [TestMethod]
        public async Task ExecuteAsync_ReturnsNotFound_WhenRunHistoryDoesNotExist()
        {
            _mockSyncJobHistoryRepository
                .Setup(x => x.GetByRunIdAsync(_runId))
                .ReturnsAsync((global::Models.SyncJobHistory.SyncJobHistory?)null);

            var response = await _handler.ExecuteAsync(
                new GetSyncExplanationRequest(_syncJobId, _runId, _userObjectId));

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
                new GetSyncExplanationRequest(_syncJobId, _runId, _userObjectId));

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
                new GetSyncExplanationRequest(_syncJobId, _runId, _userObjectId));

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
                new GetSyncExplanationRequest(_syncJobId, _runId, _userObjectId));

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
                new GetSyncExplanationRequest(_syncJobId, _runId, _userObjectId));

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
                new GetSyncExplanationRequest(_syncJobId, _runId, _userObjectId));

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
                new GetSyncExplanationRequest(_syncJobId, _runId, _userObjectId));

            Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        }

        [TestMethod]
        public async Task ExecuteAsync_FiltersSensitiveAttributes()
        {
            SetupBlobWithUser(MembershipAction.Add);
            SetupAdfData(new Dictionary<string, string>
            {
                { "Building", "B40" },
                { "PayStockLevel", "67" }
            });

            _mockSyncJobRepository
                .Setup(x => x.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(new SyncJob
                {
                    Id = _syncJobId,
                    TargetOfficeGroupId = _targetGroupId,
                    Query = "[{\"type\":\"SqlMembership\",\"filter\":\"Building = 'B40' AND PayStockLevel = '67'\"}]"
                });

            _mockSqlMembershipSourcesRepository
                .Setup(x => x.GetDefaultSourceAttributesAsync())
                .ReturnsAsync(new List<SqlMembershipAttribute>
                {
                    new SqlMembershipAttribute { Name = "PayStockLevel", Sensitive = true }
                });

            string? capturedUserPrompt = null;
            _mockOpenAIService
                .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((sys, user) => capturedUserPrompt = user)
                .ReturnsAsync("The user was added.");

            var response = await _handler.ExecuteAsync(
                new GetSyncExplanationRequest(_syncJobId, _runId, _userObjectId));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedUserPrompt);
            Assert.IsTrue(capturedUserPrompt!.Contains("[PROTECTED - value hidden]"),
                "Sensitive attribute should be marked as protected in prompt");
            // Verify the attribute line doesn't expose the raw value.
            // The query itself may contain the value, so check the attributes section specifically.
            var attrSectionStart = capturedUserPrompt.IndexOf("HR attributes");
            var attrSectionEnd = capturedUserPrompt.IndexOf("Recent configuration changes");
            if (attrSectionStart >= 0 && attrSectionEnd > attrSectionStart)
            {
                var attrSection = capturedUserPrompt.Substring(attrSectionStart, attrSectionEnd - attrSectionStart);
                Assert.IsTrue(attrSection.Contains("PayStockLevel: [PROTECTED - value hidden]"),
                    "PayStockLevel should show as PROTECTED in attributes section");
            }
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
                new GetSyncExplanationRequest(_syncJobId, _runId, _userObjectId));

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
                new GetSyncExplanationRequest(_syncJobId, _runId, _userObjectId));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(capturedUserPrompt);
            Assert.IsTrue(capturedUserPrompt!.Contains("Admin User"),
                "Config change details should be included in prompt");
            Assert.IsTrue(capturedUserPrompt.Contains("Updated building filter"),
                "Business justification should be included in prompt");
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
