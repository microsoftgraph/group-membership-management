// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.AspNetCore.SignalR;
using Moq;
using Repositories.Contracts;
using Services;
using Services.Messages.Requests;
using Services.Messages.Responses;
using Services.WebApi;
using System.Net;
using System.Threading;

namespace WebApi.Tests
{
    [TestClass]
    public class SearchSyncHistoryByUserHandlerTests
    {
        private Mock<ILoggingRepository> _mockLoggingRepository = null!;
        private Mock<IDatabaseSyncJobsRepository> _mockSyncJobRepository = null!;
        private Mock<ISyncJobHistoryRepository> _mockSyncJobHistoryRepository = null!;
        private Mock<IBlobStorageRepository> _mockBlobStorageRepository = null!;
        private Mock<IGraphGroupRepository> _mockGraphGroupRepository = null!;
        private Mock<IHubContext<SignalRService>> _mockHubContext = null!;
        private Mock<IHubClients> _mockHubClients = null!;
        private Mock<IClientProxy> _mockClientProxy = null!;
        private SearchSyncHistoryByUserHandler _handler = null!;

        private Guid _syncJobId;
        private Guid _targetGroupId;
        private Guid _userObjectId;

        [TestInitialize]
        public void Initialize()
        {
            _mockLoggingRepository = new Mock<ILoggingRepository>();
            _mockSyncJobRepository = new Mock<IDatabaseSyncJobsRepository>();
            _mockSyncJobHistoryRepository = new Mock<ISyncJobHistoryRepository>();
            _mockBlobStorageRepository = new Mock<IBlobStorageRepository>();
            _mockGraphGroupRepository = new Mock<IGraphGroupRepository>();
            _mockHubContext = new Mock<IHubContext<SignalRService>>();
            _mockHubClients = new Mock<IHubClients>();
            _mockClientProxy = new Mock<IClientProxy>();

            _mockHubContext
                .SetupGet(x => x.Clients)
                .Returns(_mockHubClients.Object);

            _mockHubClients
                .Setup(x => x.Group(It.IsAny<string>()))
                .Returns(_mockClientProxy.Object);

            _mockClientProxy
                .Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            _handler = new SearchSyncHistoryByUserHandler(
                _mockLoggingRepository.Object,
                _mockSyncJobRepository.Object,
                _mockSyncJobHistoryRepository.Object,
                _mockBlobStorageRepository.Object,
                _mockGraphGroupRepository.Object,
                _mockHubContext.Object);

            _syncJobId = Guid.NewGuid();
            _targetGroupId = Guid.NewGuid();
            _userObjectId = Guid.NewGuid();

            _mockSyncJobRepository
                .Setup(x => x.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(new global::Models.SyncJob
                {
                    Id = _syncJobId,
                    TargetOfficeGroupId = _targetGroupId
                });
        }

        [TestMethod]
        public async Task ExecuteAsync_ReturnsNotFound_WhenSyncJobDoesNotExist()
        {
            _mockSyncJobRepository
                .Setup(x => x.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync((global::Models.SyncJob?)null);

            var response = await _handler.ExecuteAsync(new SearchSyncHistoryByUserRequest(_syncJobId, _userObjectId));

            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
            Assert.AreEqual(0, response.MatchingRunIds.Count);
            Assert.AreEqual(0, response.RunMembershipChanges.Count);
            Assert.IsFalse(response.CheckedCurrentGroupMembership);
        }

        [TestMethod]
        public async Task ExecuteAsync_ReturnsMatchingRun_WhenAggregatedFileContainsAddOrRemove()
        {
            var runId = Guid.NewGuid();
            var history = new List<global::Models.SyncJobHistory.SyncJobHistory>
            {
                new global::Models.SyncJobHistory.SyncJobHistory
                {
                    SyncJobId = _syncJobId,
                    RunId = runId,
                    Status = global::Models.SyncStatus.Idle.ToString()
                }
            };

            _mockSyncJobHistoryRepository
                .Setup(x => x.GetBySyncJobIdAsync(_syncJobId, It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync((Guid syncJobId, int pageSize, int pageNumber) => pageNumber == 1 ? history : new List<global::Models.SyncJobHistory.SyncJobHistory>());

            var blobPath = $"{_targetGroupId}/{runId}_Aggregated.json";
            var membershipJson = $"{{\"SourceMembers\":[{{\"ObjectId\":\"{_userObjectId}\",\"MembershipAction\":\"Add\"}}]}}";

            _mockBlobStorageRepository
                .Setup(x => x.FindAggregatedFileByRunIdAsync(_targetGroupId.ToString(), runId.ToString()))
                .ReturnsAsync(new global::Models.BlobResult { BlobStatus = global::Models.BlobStatus.Found, Path = blobPath });

            _mockBlobStorageRepository
                .Setup(x => x.DownloadFileAsync(blobPath))
                .ReturnsAsync(new global::Models.BlobResult { BlobStatus = global::Models.BlobStatus.Found, Content = membershipJson });

            var response = await _handler.ExecuteAsync(new SearchSyncHistoryByUserRequest(_syncJobId, _userObjectId));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(1, response.MatchingRunIds.Count);
            Assert.AreEqual(runId, response.MatchingRunIds[0]);
            Assert.AreEqual(1, response.RunMembershipChanges.Count);
            Assert.AreEqual(runId, response.RunMembershipChanges[0].RunId);
            Assert.AreEqual(MembershipChangeType.Added, response.RunMembershipChanges[0].MembershipChangeType);
            Assert.IsFalse(response.CheckedCurrentGroupMembership);

            _mockGraphGroupRepository.Verify(
                x => x.IsEmailRecipientMemberOfGroupAsync(It.IsAny<string>(), It.IsAny<Guid>()),
                Times.Never);
        }

        [TestMethod]
        public async Task ExecuteAsync_ChecksCurrentMembership_WhenNoRunsMatch()
        {
            var runId = Guid.NewGuid();
            var history = new List<global::Models.SyncJobHistory.SyncJobHistory>
            {
                new global::Models.SyncJobHistory.SyncJobHistory
                {
                    SyncJobId = _syncJobId,
                    RunId = runId,
                    Status = global::Models.SyncStatus.Idle.ToString()
                }
            };

            _mockSyncJobHistoryRepository
                .Setup(x => x.GetBySyncJobIdAsync(_syncJobId, It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync((Guid syncJobId, int pageSize, int pageNumber) => pageNumber == 1 ? history : new List<global::Models.SyncJobHistory.SyncJobHistory>());

            var blobPath = $"{_targetGroupId}/{runId}_Aggregated.json";
            var membershipJson = $"{{\"SourceMembers\":[{{\"ObjectId\":\"{Guid.NewGuid()}\",\"MembershipAction\":\"None\"}}]}}";

            _mockBlobStorageRepository
                .Setup(x => x.FindAggregatedFileByRunIdAsync(_targetGroupId.ToString(), runId.ToString()))
                .ReturnsAsync(new global::Models.BlobResult { BlobStatus = global::Models.BlobStatus.Found, Path = blobPath });

            _mockBlobStorageRepository
                .Setup(x => x.DownloadFileAsync(blobPath))
                .ReturnsAsync(new global::Models.BlobResult { BlobStatus = global::Models.BlobStatus.Found, Content = membershipJson });

            _mockGraphGroupRepository
                .Setup(x => x.IsEmailRecipientMemberOfGroupAsync(_userObjectId.ToString(), _targetGroupId))
                .ReturnsAsync(true);

            var response = await _handler.ExecuteAsync(new SearchSyncHistoryByUserRequest(_syncJobId, _userObjectId));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(0, response.MatchingRunIds.Count);
            Assert.AreEqual(0, response.RunMembershipChanges.Count);
            Assert.IsTrue(response.CheckedCurrentGroupMembership);
            Assert.IsTrue(response.UserInCurrentGroup);

            _mockGraphGroupRepository.Verify(
                x => x.IsEmailRecipientMemberOfGroupAsync(_userObjectId.ToString(), _targetGroupId),
                Times.Once);
        }

        [TestMethod]
        public async Task ExecuteAsync_ReturnsRunWithLastMembershipChangeType_WhenBothAddAndRemoveExist()
        {
            var runId = Guid.NewGuid();
            var history = new List<global::Models.SyncJobHistory.SyncJobHistory>
            {
                new global::Models.SyncJobHistory.SyncJobHistory
                {
                    SyncJobId = _syncJobId,
                    RunId = runId,
                    Status = global::Models.SyncStatus.Idle.ToString()
                }
            };

            _mockSyncJobHistoryRepository
                .Setup(x => x.GetBySyncJobIdAsync(_syncJobId, It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync((Guid syncJobId, int pageSize, int pageNumber) => pageNumber == 1 ? history : new List<global::Models.SyncJobHistory.SyncJobHistory>());

            var blobPath = $"{_targetGroupId}/{runId}_Aggregated.json";
            var membershipJson = $"{{\"SourceMembers\":[{{\"ObjectId\":\"{_userObjectId}\",\"MembershipAction\":\"Add\"}},{{\"ObjectId\":\"{_userObjectId}\",\"MembershipAction\":\"Remove\"}}]}}";

            _mockBlobStorageRepository
                .Setup(x => x.FindAggregatedFileByRunIdAsync(_targetGroupId.ToString(), runId.ToString()))
                .ReturnsAsync(new global::Models.BlobResult { BlobStatus = global::Models.BlobStatus.Found, Path = blobPath });

            _mockBlobStorageRepository
                .Setup(x => x.DownloadFileAsync(blobPath))
                .ReturnsAsync(new global::Models.BlobResult { BlobStatus = global::Models.BlobStatus.Found, Content = membershipJson });

            var response = await _handler.ExecuteAsync(new SearchSyncHistoryByUserRequest(_syncJobId, _userObjectId));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(1, response.MatchingRunIds.Count);
            Assert.AreEqual(1, response.RunMembershipChanges.Count);
            Assert.AreEqual(MembershipChangeType.Removed, response.RunMembershipChanges[0].MembershipChangeType);
        }

        [TestMethod]
        public async Task ExecuteAsync_ReturnsInternalServerError_WhenRepositoryThrows()
        {
            _mockSyncJobRepository
                .Setup(x => x.GetSyncJobAsync(_syncJobId))
                .ThrowsAsync(new InvalidOperationException("boom"));

            var response = await _handler.ExecuteAsync(new SearchSyncHistoryByUserRequest(_syncJobId, _userObjectId, "req-err"));

            Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode);
            _mockLoggingRepository.Verify(
                x => x.LogMessageAsync(
                    It.Is<global::Models.LogMessage>(m => m.Message.Contains("Error searching run history")),
                    It.IsAny<VerbosityLevel>(),
                    It.IsAny<string>(),
                    It.IsAny<string>()),
                Times.Once);
        }

        [TestMethod]
        public async Task ExecuteAsync_UsesPagedHistoryAndPublishesProgress_WhenRequestIdProvided()
        {
            var firstRunId = Guid.NewGuid();
            var pageOne = Enumerable.Range(0, 200)
                .Select(index => new global::Models.SyncJobHistory.SyncJobHistory
                {
                    SyncJobId = _syncJobId,
                    RunId = index == 0 ? firstRunId : Guid.Empty,
                    Status = global::Models.SyncStatus.Idle.ToString()
                })
                .ToList();

            _mockSyncJobHistoryRepository
                .Setup(x => x.GetBySyncJobIdAsync(_syncJobId, It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync((Guid syncJobId, int pageSize, int pageNumber) =>
                    pageNumber == 1
                        ? pageOne
                        : new List<global::Models.SyncJobHistory.SyncJobHistory>());

            _mockBlobStorageRepository
                .Setup(x => x.FindAggregatedFileByRunIdAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new global::Models.BlobResult { BlobStatus = global::Models.BlobStatus.NotFound });

            _mockGraphGroupRepository
                .Setup(x => x.IsEmailRecipientMemberOfGroupAsync(_userObjectId.ToString(), _targetGroupId))
                .ReturnsAsync(false);

            var response = await _handler.ExecuteAsync(new SearchSyncHistoryByUserRequest(_syncJobId, _userObjectId, "req-progress"));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsTrue(response.CheckedCurrentGroupMembership);
            Assert.AreEqual(0, response.MatchingRunIds.Count);

            _mockSyncJobHistoryRepository.Verify(x => x.GetBySyncJobIdAsync(_syncJobId, 200, 2), Times.Once);
            _mockHubClients.Verify(x => x.Group(It.IsAny<string>()), Times.AtLeastOnce);
            _mockClientProxy.Verify(
                x => x.SendCoreAsync(
                    SignalRService.SyncHistorySearchProgressEvent,
                    It.IsAny<object[]>(),
                    It.IsAny<CancellationToken>()),
                Times.AtLeastOnce);
            _mockClientProxy.Verify(
                x => x.SendCoreAsync(
                    SignalRService.SyncHistorySearchProgressEvent,
                    It.IsAny<object[]>(),
                    It.IsAny<CancellationToken>()),
                Times.AtMost(102));
        }

        [TestMethod]
        public async Task ExecuteAsync_HandlesInvalidMembershipPayloadShapes_AndParsesNumericAction()
        {
            var runBlobNotFound = Guid.NewGuid();
            var runDownloadNotFound = Guid.NewGuid();
            var runNonArraySourceMembers = Guid.NewGuid();
            var runMixedActions = Guid.NewGuid();

            var history = new List<global::Models.SyncJobHistory.SyncJobHistory>
            {
                new global::Models.SyncJobHistory.SyncJobHistory { SyncJobId = _syncJobId, RunId = runBlobNotFound, Status = global::Models.SyncStatus.Idle.ToString() },
                new global::Models.SyncJobHistory.SyncJobHistory { SyncJobId = _syncJobId, RunId = runDownloadNotFound, Status = global::Models.SyncStatus.Idle.ToString() },
                new global::Models.SyncJobHistory.SyncJobHistory { SyncJobId = _syncJobId, RunId = runNonArraySourceMembers, Status = global::Models.SyncStatus.Idle.ToString() },
                new global::Models.SyncJobHistory.SyncJobHistory { SyncJobId = _syncJobId, RunId = runMixedActions, Status = global::Models.SyncStatus.Idle.ToString() },
            };

            _mockSyncJobHistoryRepository
                .Setup(x => x.GetBySyncJobIdAsync(_syncJobId, It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync((Guid syncJobId, int pageSize, int pageNumber) =>
                    pageNumber == 1
                        ? history
                        : new List<global::Models.SyncJobHistory.SyncJobHistory>());

            var blobPathDownloadNotFound = $"{_targetGroupId}/{runDownloadNotFound}_Aggregated.json";
            var blobPathNonArray = $"{_targetGroupId}/{runNonArraySourceMembers}_Aggregated.json";
            var blobPathMixed = $"{_targetGroupId}/{runMixedActions}_Aggregated.json";

            _mockBlobStorageRepository
                .Setup(x => x.FindAggregatedFileByRunIdAsync(_targetGroupId.ToString(), runBlobNotFound.ToString()))
                .ReturnsAsync(new global::Models.BlobResult { BlobStatus = global::Models.BlobStatus.NotFound });

            _mockBlobStorageRepository
                .Setup(x => x.FindAggregatedFileByRunIdAsync(_targetGroupId.ToString(), runDownloadNotFound.ToString()))
                .ReturnsAsync(new global::Models.BlobResult { BlobStatus = global::Models.BlobStatus.Found, Path = blobPathDownloadNotFound });

            _mockBlobStorageRepository
                .Setup(x => x.DownloadFileAsync(blobPathDownloadNotFound))
                .ReturnsAsync(new global::Models.BlobResult { BlobStatus = global::Models.BlobStatus.NotFound });

            _mockBlobStorageRepository
                .Setup(x => x.FindAggregatedFileByRunIdAsync(_targetGroupId.ToString(), runNonArraySourceMembers.ToString()))
                .ReturnsAsync(new global::Models.BlobResult { BlobStatus = global::Models.BlobStatus.Found, Path = blobPathNonArray });

            _mockBlobStorageRepository
                .Setup(x => x.DownloadFileAsync(blobPathNonArray))
                .ReturnsAsync(new global::Models.BlobResult { BlobStatus = global::Models.BlobStatus.Found, Content = "{\"SourceMembers\":{}}" });

            _mockBlobStorageRepository
                .Setup(x => x.FindAggregatedFileByRunIdAsync(_targetGroupId.ToString(), runMixedActions.ToString()))
                .ReturnsAsync(new global::Models.BlobResult { BlobStatus = global::Models.BlobStatus.Found, Path = blobPathMixed });

            var mixedMembershipJson = "{" +
                                      "\"SourceMembers\":[" +
                                      "{\"ObjectId\":\"not-a-guid\",\"MembershipAction\":\"Add\"}," +
                                      $"{{\"ObjectId\":\"{_userObjectId}\"}}," +
                                      $"{{\"ObjectId\":\"{_userObjectId}\",\"MembershipAction\":999}}," +
                                      $"{{\"ObjectId\":\"{_userObjectId}\",\"MembershipAction\":true}}," +
                                      $"{{\"objectid\":\"{_userObjectId}\",\"membershipaction\":1}}" +
                                      "]}";

            _mockBlobStorageRepository
                .Setup(x => x.DownloadFileAsync(blobPathMixed))
                .ReturnsAsync(new global::Models.BlobResult { BlobStatus = global::Models.BlobStatus.Found, Content = mixedMembershipJson });

            var response = await _handler.ExecuteAsync(new SearchSyncHistoryByUserRequest(_syncJobId, _userObjectId));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(1, response.MatchingRunIds.Count);
            Assert.AreEqual(runMixedActions, response.MatchingRunIds[0]);
            Assert.AreEqual(1, response.RunMembershipChanges.Count);
            Assert.AreEqual(MembershipChangeType.Added, response.RunMembershipChanges[0].MembershipChangeType);
            Assert.IsFalse(response.CheckedCurrentGroupMembership);
        }

        [TestMethod]
        public async Task ExecuteAsync_SkipsMalformedBlobJson_AndContinuesProcessingOtherRuns()
        {
            var malformedJsonRunId = Guid.NewGuid();
            var nonObjectRootRunId = Guid.NewGuid();
            var validRunId = Guid.NewGuid();

            var history = new List<global::Models.SyncJobHistory.SyncJobHistory>
            {
                new global::Models.SyncJobHistory.SyncJobHistory { SyncJobId = _syncJobId, RunId = malformedJsonRunId, Status = global::Models.SyncStatus.Idle.ToString() },
                new global::Models.SyncJobHistory.SyncJobHistory { SyncJobId = _syncJobId, RunId = nonObjectRootRunId, Status = global::Models.SyncStatus.Idle.ToString() },
                new global::Models.SyncJobHistory.SyncJobHistory { SyncJobId = _syncJobId, RunId = validRunId, Status = global::Models.SyncStatus.Idle.ToString() },
            };

            _mockSyncJobHistoryRepository
                .Setup(x => x.GetBySyncJobIdAsync(_syncJobId, It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync((Guid syncJobId, int pageSize, int pageNumber) =>
                    pageNumber == 1
                        ? history
                        : new List<global::Models.SyncJobHistory.SyncJobHistory>());

            var malformedPath = $"{_targetGroupId}/{malformedJsonRunId}_Aggregated.json";
            var nonObjectPath = $"{_targetGroupId}/{nonObjectRootRunId}_Aggregated.json";
            var validPath = $"{_targetGroupId}/{validRunId}_Aggregated.json";

            _mockBlobStorageRepository
                .Setup(x => x.FindAggregatedFileByRunIdAsync(_targetGroupId.ToString(), malformedJsonRunId.ToString()))
                .ReturnsAsync(new global::Models.BlobResult { BlobStatus = global::Models.BlobStatus.Found, Path = malformedPath });

            _mockBlobStorageRepository
                .Setup(x => x.FindAggregatedFileByRunIdAsync(_targetGroupId.ToString(), nonObjectRootRunId.ToString()))
                .ReturnsAsync(new global::Models.BlobResult { BlobStatus = global::Models.BlobStatus.Found, Path = nonObjectPath });

            _mockBlobStorageRepository
                .Setup(x => x.FindAggregatedFileByRunIdAsync(_targetGroupId.ToString(), validRunId.ToString()))
                .ReturnsAsync(new global::Models.BlobResult { BlobStatus = global::Models.BlobStatus.Found, Path = validPath });

            _mockBlobStorageRepository
                .Setup(x => x.DownloadFileAsync(malformedPath))
                .ReturnsAsync(new global::Models.BlobResult { BlobStatus = global::Models.BlobStatus.Found, Content = "{" });

            _mockBlobStorageRepository
                .Setup(x => x.DownloadFileAsync(nonObjectPath))
                .ReturnsAsync(new global::Models.BlobResult { BlobStatus = global::Models.BlobStatus.Found, Content = "[]" });

            _mockBlobStorageRepository
                .Setup(x => x.DownloadFileAsync(validPath))
                .ReturnsAsync(new global::Models.BlobResult
                {
                    BlobStatus = global::Models.BlobStatus.Found,
                    Content = $"{{\"SourceMembers\":[{{\"ObjectId\":\"{_userObjectId}\",\"MembershipAction\":\"Add\"}}]}}"
                });

            var response = await _handler.ExecuteAsync(new SearchSyncHistoryByUserRequest(_syncJobId, _userObjectId));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(1, response.MatchingRunIds.Count);
            Assert.AreEqual(validRunId, response.MatchingRunIds[0]);
            Assert.AreEqual(1, response.RunMembershipChanges.Count);
            Assert.AreEqual(MembershipChangeType.Added, response.RunMembershipChanges[0].MembershipChangeType);
        }
    }
}
