using Entities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models.Entities;
using Moq;
using Repositories.Contracts;
using Repositories.Mocks;
using TeamsChannel.Service;
using TeamsChannel.Service.Contracts;

namespace Services.Tests
{
    [TestClass]
    public class TeamsChannelServiceTests
    {
        private TeamsChannelService _service;
        private Mock<ITeamsChannelRepository> _mockTeamsChannelRepository;
        private Dictionary<AzureADTeamsChannel, List<AzureADTeamsUser>> _mockChannels = new Dictionary<AzureADTeamsChannel, List<AzureADTeamsUser>>
        {
            { new AzureADTeamsChannel { ObjectId = Guid.Empty, ChannelId = "some channel" },
                new List<AzureADTeamsUser> { new AzureADTeamsUser { ObjectId = Guid.NewGuid(), TeamsId = "first guy" }, new AzureADTeamsUser { ObjectId = Guid.NewGuid(), TeamsId = "second guy" } } },
            { new AzureADTeamsChannel { ObjectId = Guid.Parse("00000000-0000-0000-0000-000000000001"), ChannelId = "another channel" },
                new List<AzureADTeamsUser> { new AzureADTeamsUser { ObjectId = Guid.NewGuid(), TeamsId = "third guy" }, new AzureADTeamsUser { ObjectId = Guid.NewGuid(), TeamsId = "fourth guy" } } }
        };

        [TestInitialize]
        public void SetUp()
        {
            _mockTeamsChannelRepository = new Mock<ITeamsChannelRepository>();
            _mockTeamsChannelRepository.Setup<Task<List<AzureADTeamsUser>>>(repo => repo.ReadUsersFromChannel(It.IsIn<AzureADTeamsChannel>(_mockChannels.Keys), It.IsAny<Guid>()))
                .ReturnsAsync((AzureADTeamsChannel c, Guid g) => _mockChannels[c]);
            var mockBlobStorageRepository = new Mock<IBlobStorageRepository>();
            var mockSyncJobRepository = new MockSyncJobRepository();
            var mockHttpClientFactory = new Mock<IHttpClientFactory>();

            _service = new TeamsChannelService(_mockTeamsChannelRepository.Object, mockBlobStorageRepository.Object, mockHttpClientFactory.Object, mockSyncJobRepository, new MockLoggingRepository());
        }

        [TestMethod]
        public async Task GetsUsersFromTeam()
        {
            var userList = await _service.GetUsersFromTeam(new ChannelSyncInfo
            {
                CurrentPart = 1,
                SyncJob = new SyncJob
                {
                    Query = @"[{""type"":""TeamsChannel"",""source"":{""group"":""00000000-0000-0000-0000-000000000000"", ""channel"":""some channel""}},{""type"":""TeamsChannel"",""source"":{""group"":""00000000-0000-0000-0000-000000000001"", ""channel"":""another channel""}}]"
                }
            });

            Assert.IsTrue(userList.SequenceEqual(_mockChannels[new AzureADTeamsChannel { ObjectId = Guid.Empty, ChannelId = "some channel" }]));
            Assert.AreEqual(2, userList.Count);
        }
    }
}