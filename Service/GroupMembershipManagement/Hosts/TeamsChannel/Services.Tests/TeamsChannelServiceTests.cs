using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Repositories.Contracts;
using Repositories.Mocks;
using TeamsChannel.Service;

namespace Services.Tests
{
    [TestClass]
    public class TeamsChannelServiceTests
    {
        private TeamsChannelService _service;

        [TestInitialize]
        public void SetUp()
        {
            var mockTeamsChannelRepository = new Mock<ITeamsChannelRepository>();
            var mockBlobStorageRepository = new Mock<IBlobStorageRepository>();
            var mockSyncJobRepository = new MockSyncJobRepository();
            var mockHttpClientFactory = new Mock<IHttpClientFactory>();

            _service = new TeamsChannelService(mockTeamsChannelRepository.Object, mockBlobStorageRepository.Object, mockHttpClientFactory.Object, mockSyncJobRepository, new MockLoggingRepository());
        }

        [TestMethod]
        public void TestMethod1()
        {
        }
    }
}