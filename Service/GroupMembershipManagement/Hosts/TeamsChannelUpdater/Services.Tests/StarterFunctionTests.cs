// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using Hosts.TeamsChannelUpdater;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.ServiceBus;
using Moq;

namespace Services.Tests
{
    [TestClass]
    public class StarterFunctionTests
    {
        private string _instanceId;
        private Mock<DurableTaskClient> _durableClientMock;
        private SyncJob _syncJob;
        private Channel _channel;
        private Mock<ServiceBusReceiver> _serviceBusReceiverMock;

        [TestInitialize]
        public void SetupTest()
        {
            _instanceId = "1234567890";
            _durableClientMock = new Mock<DurableTaskClient>("test");
            _serviceBusReceiverMock = new Mock<ServiceBusReceiver>();
            _syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                ThresholdPercentageForAdditions = 80,
                ThresholdPercentageForRemovals = 20,
                LastRunTime = DateTime.UtcNow.AddDays(-1),
                Requestor = "user@domail.com",
                RunId = Guid.NewGuid(),
                ThresholdViolations = 0,
                MembershipType = "TeamsChannelMembership"
            };
            _channel = new Models.Channel
            {
                ChannelId = "channelId",
                GroupId = Guid.NewGuid(),
                SyncJobId = _syncJob.Id
            };
        }

        [TestMethod]
        public async Task ProcessValidRequestTest()
        {
            _durableClientMock
                .Setup(x => x.ScheduleNewOrchestrationInstanceAsync(It.IsAny<Microsoft.DurableTask.TaskName>(), It.IsAny<object>(), It.IsAny<Microsoft.DurableTask.StartOrchestrationOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(_instanceId);

            var instanceId = nameof(QueueMessageOrchestratorFunction);
            var starterFunction = new StarterFunction(NullLogger<StarterFunction>.Instance, _serviceBusReceiverMock.Object);
            var timerInfo = new FakeTimerInfo();

            await starterFunction.RunAsync(timerInfo, _durableClientMock.Object);

            _durableClientMock.Verify(x => x.ScheduleNewOrchestrationInstanceAsync(It.IsAny<Microsoft.DurableTask.TaskName>(), It.IsAny<object>(), It.IsAny<Microsoft.DurableTask.StartOrchestrationOptions>(), It.IsAny<CancellationToken>()), Times.Once());
        }
    }

    public class FakeTimerInfo : TimerInfo
    {
    }
}
