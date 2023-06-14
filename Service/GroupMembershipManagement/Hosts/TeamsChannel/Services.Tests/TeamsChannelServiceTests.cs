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
using Repositories.FeatureFlag;
using Repositories.Mocks;
using System.Net;
using TeamsChannel.Service;
using TeamsChannel.Service.Contracts;

namespace Services.Tests
{
    [TestClass]
    public class TeamsChannelServiceTests
    {
        private TeamsChannelService _service = null!;
        private ChannelSyncInfo _syncInfo = null!;
        private Mock<ITeamsChannelRepository> _mockTeamsChannelRepository = null!;
        private Mock<IBlobStorageRepository> _mockBlobStorageRepository = null!;
        private Mock<IHttpClientFactory> _mockHttpClientFactory = null!;
        private Mock<IFeatureManager> _featureManager = null!;
        private Mock<IServiceBusQueueRepository> _serviceBusQueueRepository = null!;
        private Mock<IConfigurationRefresherProvider> _configurationRefresherProvider = null!;
        private Mock<ILoggingRepository> _loggingRepository = null!;
        private Mock<HttpMessageHandler> _messageHandler = null!;
        private bool _isFeatureFlagEnabled = false;
        private HttpStatusCode _responseStatusCode = HttpStatusCode.NoContent;
        private FeatureFlagRepository _featureFlagRepository = null!;


        private Dictionary<AzureADTeamsChannel, List<AzureADTeamsUser>> _mockChannels = new Dictionary<AzureADTeamsChannel, List<AzureADTeamsUser>>
        {
            { new AzureADTeamsChannel { ObjectId = Guid.Empty, ChannelId = "some channel" },
                new List<AzureADTeamsUser> { new AzureADTeamsUser { ObjectId = Guid.NewGuid(), TeamsId = "first guy" }, new AzureADTeamsUser { ObjectId = Guid.NewGuid(), TeamsId = "second guy" } } },
            { new AzureADTeamsChannel { ObjectId = Guid.Parse("00000000-0000-0000-0000-000000000001"), ChannelId = "another channel" },
                new List<AzureADTeamsUser> { new AzureADTeamsUser { ObjectId = Guid.NewGuid(), TeamsId = "third guy" }, new AzureADTeamsUser { ObjectId = Guid.NewGuid(), TeamsId = "fourth guy" } } }
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
            var mockSyncJobRepository = new MockSyncJobRepository();
            _mockHttpClientFactory = new Mock<IHttpClientFactory>();
            _loggingRepository = new Mock<ILoggingRepository>();

            _featureManager = new Mock<IFeatureManager>();
            _serviceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            _configurationRefresherProvider = new Mock<IConfigurationRefresherProvider>();

            _featureFlagRepository = new FeatureFlagRepository(_loggingRepository.Object,
                                                                 _featureManager.Object,
                                                                 _configurationRefresherProvider.Object);

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

            _featureManager.Setup(x => x.IsEnabledAsync(It.IsAny<string>()))
                           .ReturnsAsync(() => _isFeatureFlagEnabled);

            var configurationRefresher = new Mock<IConfigurationRefresher>();
            configurationRefresher.Setup(x => x.TryRefreshAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

            _configurationRefresherProvider.Setup(x => x.Refreshers)
                                            .Returns(() => new List<IConfigurationRefresher> { configurationRefresher.Object });

            _service = new TeamsChannelService(_mockTeamsChannelRepository.Object,
                                                _mockBlobStorageRepository.Object,
                                                _mockHttpClientFactory.Object,
                                                mockSyncJobRepository,
                                                _loggingRepository.Object,
                                                _featureManager.Object,
                                                _configurationRefresherProvider.Object,
                                                _serviceBusQueueRepository.Object,
                                                _featureFlagRepository);

            _syncInfo = new ChannelSyncInfo
            {
                CurrentPart = 1,
                IsDestinationPart = true,
                SyncJob = new SyncJob
                {
                    RunId = Guid.Parse("00000000-0000-0000-0000-000000000012"),
                    Status = SyncStatus.InProgress.ToString(),
                    TargetOfficeGroupId = Guid.Parse("00000000-0000-0000-0000-000000000042"),
                    Timestamp = new DateTimeOffset(1995, 03, 28, 1, 2, 3, TimeSpan.Zero),
                    Query = @"[{""type"":""TeamsChannel"",""source"":{""group"":""00000000-0000-0000-0000-000000000000"", ""channel"":""some channel""}},{""type"":""TeamsChannel"",""source"":{""group"":""00000000-0000-0000-0000-000000000001"", ""channel"":""another channel""}}]"
                }
            };


        }

        [TestMethod]
        public async Task VerifyRejectsNonDestinationPrivateChannels()
        {
            var badSyncInfo = new ChannelSyncInfo
            {
                CurrentPart = 2,
                IsDestinationPart = true,
                SyncJob = new SyncJob
                {
                    RunId = Guid.Parse("00000000-0000-0000-0000-000000000012"),
                    Status = SyncStatus.InProgress.ToString(),
                    TargetOfficeGroupId = Guid.Parse("00000000-0000-0000-0000-000000000042"),
                    Timestamp = new DateTimeOffset(1995, 03, 28, 1, 2, 3, TimeSpan.Zero),
                    Query = @"[{""type"":""TeamsChannel"",""source"":{""group"":""00000000-0000-0000-0000-000000000000"", ""channel"":""some channel""}},{""type"":""TeamsChannel"",""source"":{""group"":""00000000-0000-0000-0000-000000000001"", ""channel"":""another channel""}}]"
                }
            };

            var verification = await _service.VerifyChannelAsync(badSyncInfo);

            Assert.IsFalse(verification.isGood);
            Assert.AreEqual(SyncStatus.TeamsChannelNotPrivate.ToString(), badSyncInfo.SyncJob.Status);
        }


        [TestMethod]
        public async Task VerifyAcceptsGoodSync()
        {
            var verification = await _service.VerifyChannelAsync(_syncInfo);

            Assert.IsTrue(verification.isGood);
            Assert.AreEqual(SyncStatus.InProgress.ToString(), _syncInfo.SyncJob.Status);
        }

        [TestMethod]
        public async Task VerifyRejectsNonPrivateChannels()
        {
            var badSyncInfo = new ChannelSyncInfo
            {
                CurrentPart = 1,
                IsDestinationPart = false,
                SyncJob = new SyncJob
                {
                    RunId = Guid.Parse("00000000-0000-0000-0000-000000000012"),
                    TargetOfficeGroupId = Guid.Parse("00000000-0000-0000-0000-000000000042"),
                    Timestamp = new DateTimeOffset(1995, 03, 28, 1, 2, 3, TimeSpan.Zero),
                    Query = @"[{""type"":""TeamsChannel"",""source"":{""group"":""00000000-0000-0000-0000-000000000000"", ""channel"":""some channel""}},{""type"":""TeamsChannel"",""source"":{""group"":""00000000-0000-0000-0000-000000000001"", ""channel"":""another channel""}}]"
                }
            };

            var verification = await _service.VerifyChannelAsync(badSyncInfo);

            Assert.IsFalse(verification.isGood);
            Assert.AreEqual(SyncStatus.PrivateChannelNotDestination.ToString(), badSyncInfo.SyncJob.Status);
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
            await _service.MarkSyncJobAsErroredAsync(_syncInfo.SyncJob);
            Assert.AreEqual(SyncStatus.Error.ToString(), _syncInfo.SyncJob.Status);
        }

        [TestMethod]
        public async Task SendMembershipRequestViaServiceBus()
        {
            _isFeatureFlagEnabled = true;
            await _service.MakeMembershipAggregatorRequestAsync(_syncInfo, "blob-path");

            _featureManager.Verify(x => x.IsEnabledAsync(It.IsAny<string>()), Times.Once);
            _serviceBusQueueRepository.Verify(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>()), Times.Once);
        }

        [TestMethod]
        public async Task SendMembershipRequestViaHTTP_Success()
        {
            _isFeatureFlagEnabled = false;
            _responseStatusCode = HttpStatusCode.NoContent;
            await _service.MakeMembershipAggregatorRequestAsync(_syncInfo, "blob-path");

            _featureManager.Verify(x => x.IsEnabledAsync(It.IsAny<string>()), Times.Once);
            _messageHandler.Protected().Verify(
                       "SendAsync",
                       Times.Once(),
                       ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Post),
                       ItExpr.IsAny<CancellationToken>()
                    );

            _loggingRepository
                .Verify(x => x.LogMessageAsync(
                                    It.Is<LogMessage>(x => x.Message.StartsWith("In Service, successfully made POST request")),
                                    It.IsAny<VerbosityLevel>(),
                                    It.IsAny<string>(),
                                    It.IsAny<string>()),
                                    Times.Once);
        }

        [TestMethod]
        public async Task SendMembershipRequestViaHTTP_Failure()
        {
            _isFeatureFlagEnabled = false;
            _responseStatusCode = HttpStatusCode.BadRequest;
            await _service.MakeMembershipAggregatorRequestAsync(_syncInfo, "blob-path");

            _featureManager.Verify(x => x.IsEnabledAsync(It.IsAny<string>()), Times.Once);
            _messageHandler.Protected().Verify(
                       "SendAsync",
                       Times.Once(),
                       ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Post),
                       ItExpr.IsAny<CancellationToken>()
                    );

            _loggingRepository
                .Verify(x => x.LogMessageAsync(
                                    It.Is<LogMessage>(x => x.Message.StartsWith("In Service, POST request failed")),
                                    It.IsAny<VerbosityLevel>(),
                                    It.IsAny<string>(),
                                    It.IsAny<string>()),
                                    Times.Once);
        }
    }
}
