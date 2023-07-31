// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.FeatureManagement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Entities;
using Models.ServiceBus;
using Moq;
using Moq.Protected;
using Repositories.Contracts;
using System.Net;
using TeamsChannel.Service;
using TeamsChannel.Service.Contracts;

namespace Services.Tests
{
    [TestClass]
    public class TeamsChannelServiceTests
    {
        private TeamsChannelMembershipObtainerService _service = null!;
        private ChannelSyncInfo _syncInfo = null!;
        private Mock<IDatabaseSyncJobsRepository> _syncJobRepository = null!;
        private Mock<ITeamsChannelRepository> _mockTeamsChannelRepository = null!;
        private Mock<IBlobStorageRepository> _mockBlobStorageRepository = null!;
        private Mock<IHttpClientFactory> _mockHttpClientFactory = null!;
        private Mock<IServiceBusQueueRepository> _serviceBusQueueRepository = null!;
        private Mock<IConfigurationRefresherProvider> _configurationRefresherProvider = null!;
        private Mock<ILoggingRepository> _loggingRepository = null!;
        private Mock<HttpMessageHandler> _messageHandler = null!;
        private HttpStatusCode _responseStatusCode = HttpStatusCode.NoContent;


        private Dictionary<AzureADTeamsChannel, List<AzureADTeamsUser>> _mockChannels = new Dictionary<AzureADTeamsChannel, List<AzureADTeamsUser>>
        {
            { new AzureADTeamsChannel { ObjectId = Guid.Empty, ChannelId = "some channel" },
                new List<AzureADTeamsUser> { new AzureADTeamsUser { ObjectId = Guid.NewGuid(), ConversationMemberId = "first guy" }, new AzureADTeamsUser { ObjectId = Guid.NewGuid(), ConversationMemberId = "second guy" } } },
            { new AzureADTeamsChannel { ObjectId = Guid.Parse("00000000-0000-0000-0000-000000000001"), ChannelId = "another channel" },
                new List<AzureADTeamsUser> { new AzureADTeamsUser { ObjectId = Guid.NewGuid(), ConversationMemberId = "third guy" }, new AzureADTeamsUser { ObjectId = Guid.NewGuid(), ConversationMemberId = "fourth guy" } } }
        };
        const string ExpectedFilename = "/00000000-0000-0000-0000-000000000042/03281995-010203_00000000-0000-0000-0000-000000000012_TeamsChannel_1.json";


        [TestInitialize]
        public void SetUp()
        {
            _mockTeamsChannelRepository = new Mock<ITeamsChannelRepository>();
            _mockTeamsChannelRepository.Setup<Task<List<AzureADTeamsUser>>>(repo => repo.ReadUsersFromChannelAsync(It.IsIn<AzureADTeamsChannel>(_mockChannels.Keys), It.IsAny<Guid>()))
                .ReturnsAsync((AzureADTeamsChannel c, Guid g) => _mockChannels[c]);
            _mockTeamsChannelRepository.Setup<Task<string>>(repo => repo.GetChannelTypeAsync(It.IsIn<AzureADTeamsChannel>(_mockChannels.Keys), It.IsAny<Guid>()))
                .ReturnsAsync((AzureADTeamsChannel tc, Guid _) => tc.ChannelId == "some channel" ? "Private" : "Standard");

            _mockBlobStorageRepository = new Mock<IBlobStorageRepository>();
            _syncJobRepository = new Mock<IDatabaseSyncJobsRepository>();

            _mockHttpClientFactory = new Mock<IHttpClientFactory>();
            _loggingRepository = new Mock<ILoggingRepository>();
            _serviceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            _configurationRefresherProvider = new Mock<IConfigurationRefresherProvider>();

            _messageHandler = new Mock<HttpMessageHandler>();
            _messageHandler
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                        "SendAsync",
                        ItExpr.IsAny<HttpRequestMessage>(),
                        ItExpr.IsAny<CancellationToken>())
                               .ReturnsAsync(() => new HttpResponseMessage
                               {
                                   StatusCode = _responseStatusCode
                               });

            var httpClient = new HttpClient(_messageHandler.Object) { BaseAddress = new Uri("https://graph.microsoft.com/v1.0/") };
            _mockHttpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(httpClient);

            var configurationRefresher = new Mock<IConfigurationRefresher>();
            configurationRefresher.Setup(x => x.TryRefreshAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

            _configurationRefresherProvider.Setup(x => x.Refreshers)
                                            .Returns(() => new List<IConfigurationRefresher> { configurationRefresher.Object });

            _service = new TeamsChannelMembershipObtainerService(_mockTeamsChannelRepository.Object,
                                                _mockBlobStorageRepository.Object,
                                                _mockHttpClientFactory.Object,
                                                _syncJobRepository.Object,
                                                _loggingRepository.Object,
                                                _configurationRefresherProvider.Object,
                                                _serviceBusQueueRepository.Object);

            _syncInfo = new ChannelSyncInfo
            {
                TotalParts = 1,
                CurrentPart = 1,
                IsDestinationPart = true,
                SyncJob = new SyncJob
                {
                    RunId = Guid.Parse("00000000-0000-0000-0000-000000000012"),
                    Status = SyncStatus.InProgress.ToString(),
                    TargetOfficeGroupId = Guid.Parse("00000000-0000-0000-0000-000000000042"),
                    Timestamp = new DateTimeOffset(1995, 03, 28, 1, 2, 3, TimeSpan.Zero),
                    Query = @"[{""type"":""GroupMembership"",""source"":""00000000-0000-0000-0000-000000000000""}]",
                    Destination = @"[{""type"":""TeamsChannel"",""value"":{""groupId"":""00000000-0000-0000-0000-000000000000"", ""channelId"":""some channel""}}]"
                }
            };


        }

        [TestMethod]
        public async Task VerifyRejectsInvalidDestinationQuery()
        {
            var badSyncInfo = new ChannelSyncInfo
            {
                TotalParts = 1,
                CurrentPart = 2,
                IsDestinationPart = false,
                SyncJob = new SyncJob
                {
                    RunId = Guid.Parse("00000000-0000-0000-0000-000000000012"),
                    Status = SyncStatus.InProgress.ToString(),
                    TargetOfficeGroupId = Guid.Parse("00000000-0000-0000-0000-000000000042"),
                    Timestamp = new DateTimeOffset(1995, 03, 28, 1, 2, 3, TimeSpan.Zero),
                    Query = @"[{""type"":""GroupMembership"",""source"":""00000000-0000-0000-0000-000000000000""}]",
                    Destination = @"[{""type"":""TeamsChannel"",""value"":{""XXgroupIdXX"":""00000000-0000-0000-0000-000000000000"", ""channelId"":""some channel""}}]"
                }
            };

            var verification = await _service.VerifyChannelAsync(badSyncInfo);

            Assert.IsFalse(verification.isGood);
        }

        [TestMethod]
        public async Task VerifyRejectsNonDestinationPrivateChannels()
        {
            var badSyncInfo = new ChannelSyncInfo
            {
                TotalParts = 1,
                CurrentPart = 2,
                IsDestinationPart = false,
                SyncJob = new SyncJob
                {
                    RunId = Guid.Parse("00000000-0000-0000-0000-000000000012"),
                    Status = SyncStatus.InProgress.ToString(),
                    TargetOfficeGroupId = Guid.Parse("00000000-0000-0000-0000-000000000042"),
                    Timestamp = new DateTimeOffset(1995, 03, 28, 1, 2, 3, TimeSpan.Zero),
                    Query = @"[{""type"":""GroupMembership"",""source"":""00000000-0000-0000-0000-000000000000""}]",
                    Destination = @"[{""type"":""TeamsChannel"",""value"":{""groupId"":""00000000-0000-0000-0000-000000000000"", ""channelId"":""some channel""}}]"
                }
            };

            var verification = await _service.VerifyChannelAsync(badSyncInfo);

            Assert.IsFalse(verification.isGood);
         }


        [TestMethod]
        public async Task VerifyAcceptsGoodSync()
        {
            var verification = await _service.VerifyChannelAsync(_syncInfo);

            Assert.IsTrue(verification.isGood);
            Assert.AreEqual(SyncStatus.InProgress.ToString(), _syncInfo.SyncJob.Status);
        }


        [TestMethod]
        public async Task GetsUsersFromTeam()
        {
            var sourceChannel = new AzureADTeamsChannel { ObjectId = Guid.Empty, ChannelId = "some channel" };
            var userList = await _service.GetUsersFromTeamAsync(sourceChannel, _syncInfo.SyncJob.RunId.Value);


            Assert.IsTrue(userList.SequenceEqual(_mockChannels[sourceChannel]));
            Assert.AreEqual(2, userList.Count);
            _mockTeamsChannelRepository.Verify(m => m.ReadUsersFromChannelAsync(sourceChannel, _syncInfo.SyncJob.RunId.Value));
        }

        [TestMethod]
        public async Task CanUploadMembership()
        {
            var sourceChannel = new AzureADTeamsChannel { ObjectId = Guid.Empty, ChannelId = "some channel" };
            var sourceMembers = _mockChannels[sourceChannel];

            var filePath = await _service.UploadMembershipAsync(sourceMembers, _syncInfo, false);

            Assert.AreEqual(ExpectedFilename, filePath);
            _mockBlobStorageRepository.Verify(mock => mock.UploadFileAsync(ExpectedFilename, It.IsNotNull<string>(), It.IsAny<Dictionary<string, string>>()));
        }

        [TestMethod]
        public async Task CanMarkJobsAsError()
        {
            _syncJobRepository.Setup(repo => repo.UpdateSyncJobStatusAsync(It.IsAny<IEnumerable<SyncJob>>(), It.IsAny<SyncStatus>()))
               .Returns((IEnumerable<SyncJob> jobs, SyncStatus status) =>  UpdateJobStatus(jobs, status));

            await _service.UpdateSyncJobStatusAsync(_syncInfo.SyncJob, SyncStatus.Error);
            Assert.AreEqual(SyncStatus.Error.ToString(), _syncInfo.SyncJob.Status);
        }

        private Task UpdateJobStatus(IEnumerable<SyncJob> jobs, SyncStatus status)
        {
            foreach (var job in jobs)
                job.Status = status.ToString();
            return Task.CompletedTask;
        }

        [TestMethod]
        public async Task SendMembershipRequestViaServiceBus()
        {
            await _service.MakeMembershipAggregatorRequestAsync(_syncInfo, "blob-path");
            _serviceBusQueueRepository.Verify(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>()), Times.Once);
        }
    }
}
