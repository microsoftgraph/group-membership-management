// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using DIConcreteTypes;
using GraphUpdater.QueueMessageOrchestrator;
using Hosts.GraphUpdater;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using Repositories.Mocks;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class StarterFunctionTests
    {
        private string _instanceId;
        private MockLoggingRepository _loggerMock;
        private Mock<IDurableOrchestrationClient> _durableClientMock;
        private SyncJob _syncJob;
        private Mock<ServiceBusReceiver> _serviceBusReceiverMock;
        private MembershipUpdaters _membershipUpdaters;
        private IOptions<MultiLaneConfig> _multilaneConfig;
        private string _subscriptionName = "GraphUpdater";
        private string _laneSize = "Small";

        [TestInitialize]
        public void SetupTest()
        {
            _instanceId = "1234567890";
            _durableClientMock = new Mock<IDurableOrchestrationClient>();
            _loggerMock = new MockLoggingRepository();
            _serviceBusReceiverMock = new Mock<ServiceBusReceiver>();
            _syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                TargetOfficeGroupId = Guid.NewGuid(),
                ThresholdPercentageForAdditions = 80,
                ThresholdPercentageForRemovals = 20,
                LastRunTime = DateTime.UtcNow.AddDays(-1),
                Requestor = "user@domail.com",
                RunId = Guid.NewGuid(),
                ThresholdViolations = 0
            };

            _multilaneConfig = Options.Create(new MultiLaneConfig
            {
                IsEnabled = false,
                Small = 20,
                Medium = 60,
            });

            _membershipUpdaters = Helpers.GetAvailableMembershipUpdaters(currentLaneSize: _laneSize);
        }

        [TestMethod]
        public async Task ProcessValidRequestTest()
        {
            _multilaneConfig.Value.IsEnabled = false;
            _membershipUpdaters = Helpers.GetAvailableMembershipUpdaters(currentLaneSize: null);

            _instanceId = $"{nameof(QueueMessageOrchestratorFunction)}_{_subscriptionName.ToLowerInvariant()}";

            _durableClientMock
                .Setup(x => x.StartNewAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(_instanceId);

            var starterFunction = new StarterFunction(_loggerMock, _serviceBusReceiverMock.Object, _membershipUpdaters, _multilaneConfig);
            var timer = new TimerInfo(null, null);

            await starterFunction.RunAsync(timer, _durableClientMock.Object);

            Assert.IsNotNull(_loggerMock.MessagesLogged.Single(x => x.Message.Contains("function started")));
            _durableClientMock.Verify(x => x.StartNewAsync(nameof(QueueMessageOrchestratorFunction), _instanceId, It.IsAny<QueueMessageOrchestratorRequest>()), Times.Once());
            Assert.IsNotNull(_loggerMock.MessagesLogged.Single(x => x.Message == $"Calling {_instanceId}"));
            Assert.IsNotNull(_loggerMock.MessagesLogged.Single(x => x.Message.Contains("function complete")));
        }
        [TestMethod]
        public async Task ProcessValidMultiLaneRequestTest()
        {
            _multilaneConfig.Value.IsEnabled = true;

            _durableClientMock
                .Setup(x => x.StartNewAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>()))
                .ReturnsAsync(_instanceId);

            var instanceIdPrefix = $"{nameof(QueueMessageOrchestratorFunction)}_{_subscriptionName.ToLowerInvariant()}_";
            var starterFunction = new StarterFunction(_loggerMock, _serviceBusReceiverMock.Object, _membershipUpdaters, _multilaneConfig);
            var timer = new TimerInfo(null, null);

            await starterFunction.RunAsync(timer, _durableClientMock.Object);

            var laneInstances = _membershipUpdaters.AvailableInstances["GroupMembership"][_laneSize].Instances;

            Assert.AreEqual(laneInstances, _loggerMock.MessagesLogged.Count(x => x.Message.Contains("function started")));

            _durableClientMock.Verify(x => x.StartNewAsync(nameof(QueueMessageOrchestratorFunction), It.IsAny<string>(), It.IsAny<QueueMessageOrchestratorRequest>()), Times.Exactly(laneInstances));

            Assert.AreEqual(laneInstances, _loggerMock.MessagesLogged.Count(x => x.Message.StartsWith($"Calling {instanceIdPrefix}")));
            Assert.AreEqual(laneInstances, _loggerMock.MessagesLogged.Count(x => x.Message.Contains("function complete")));
        }
    }
}
