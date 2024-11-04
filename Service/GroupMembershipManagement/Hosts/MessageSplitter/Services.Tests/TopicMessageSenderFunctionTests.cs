// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using DIConcreteTypes;
using Hosts.MessageSplitter;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.ServiceBus;
using Moq;
using Repositories.Contracts;
using System.Text.Json;

namespace Services.Tests
{
    [TestClass]
    public class TopicMessageSenderFunctionTests
    {
        private IOptions<MultiLaneConfig> _multilaneConfig;
        private Mock<ILoggingRepository> _loggingRepository;
        private Mock<IBlobStorageRepository> _blobStorageRepository;
        private Mock<IServiceBusTopicsRepository> _serviceBusTopicsRepository;
        private GroupMembership _fileContent;
        private int membersToBeAdded = 10;

        [TestInitialize]
        public void SetupTest()
        {
            _loggingRepository = new Mock<ILoggingRepository>();
            _serviceBusTopicsRepository = new Mock<IServiceBusTopicsRepository>();
            _multilaneConfig = Options.Create(new MultiLaneConfig());
            _blobStorageRepository = new Mock<IBlobStorageRepository>();

            _fileContent = new GroupMembership
            {
                SourceMembers = new List<AzureADUser>(),
            };

            for (var i = 0; i < membersToBeAdded; i++)
            {
                _fileContent.SourceMembers.Add(new AzureADUser
                {
                    ObjectId = Guid.NewGuid()
                });
            }

            _blobStorageRepository.Setup(x => x.DownloadFileAsync(It.IsAny<string>()))
                                  .ReturnsAsync(() =>
                                  {
                                      var result = new BlobResult();
                                      result.Content = JsonSerializer.Serialize(_fileContent);
                                      return result;
                                  });
        }

        [TestMethod]
        public async Task TestSendMessageAsync()
        {
            var function = new TopicMessageSenderFunction(
                                    _loggingRepository.Object,
                                    _serviceBusTopicsRepository.Object,
                                    _multilaneConfig,
                                    _blobStorageRepository.Object);


            var request = new TopicMessageSenderRequest
            {
                MembershipRequest = new Models.MembershipHttpRequest
                {
                    SyncJob = new SyncJob
                    {
                        Id = Guid.NewGuid(),
                        TargetOfficeGroupId = Guid.NewGuid(),
                        ThresholdPercentageForAdditions = 80,
                        ThresholdPercentageForRemovals = 20,
                        LastRunTime = DateTime.UtcNow.AddDays(-1),
                        Requestor = "user@domail.com",
                        RunId = Guid.NewGuid(),
                        ThresholdViolations = 0,
                        Destination = "[{\"type\":\"GroupMembership\",\"value\":{\"objectId\":\"00000000-0000-0000-0000-000000000000\"}}]"
                    },
                    FilePath = "test",
                    MembersToBeAdded = membersToBeAdded,
                    MembersToBeRemoved = 0,
                    ProjectedMemberCount = 20
                },
                InstanceToUse = 1,
                LaneSize = "Small"
            };


            await function.SendMessageAsync(request);

            var updaterType = "GroupMembership";
            var type = $"{updaterType}_{request.LaneSize}_{request.InstanceToUse}".ToLowerInvariant(); ;

            _serviceBusTopicsRepository.Verify(x => x.AddMessagesAsync(It.IsAny<List<ServiceBusMessage>>()), Times.Once());
            _serviceBusTopicsRepository.Verify(x => x.AddMessagesAsync(It.Is<List<ServiceBusMessage>>(m => m.All(x => x.MessageId.Contains("GroupMembership")))));
            _serviceBusTopicsRepository.Verify(x => x.AddMessagesAsync(It.Is<List<ServiceBusMessage>>(m => m.All(x => x.ApplicationProperties["Type"].Equals(type)))));
        }
    }
}
