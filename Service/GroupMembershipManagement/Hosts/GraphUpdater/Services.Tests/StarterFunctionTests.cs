// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using Hosts.GraphUpdater;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using Repositories.Mocks;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class StarterFunctionTests
    {
        private string _instanceId;
        private MockLoggingRepository _loggerMock;
        private Mock<MockDurableTaskClient> _durableOrchestrationClient;
        private SyncJob _syncJob;
        private Mock<ServiceBusReceiver> _serviceBusReceiverMock;

        [TestInitialize]
        public void SetupTest()
        {
            _instanceId = "1234567890";
            _durableOrchestrationClient = new Mock<MockDurableTaskClient>();
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
        }

        [TestMethod]
        public async Task ProcessValidRequestTest()
        {
            _durableOrchestrationClient
                .Setup(x => x.ScheduleNewOrchestrationInstanceAsync(It.IsAny<TaskName>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(_instanceId);

            var instanceId = nameof(QueueMessageOrchestratorFunction);
            var starterFunction = new StarterFunction(_loggerMock, _serviceBusReceiverMock.Object);
            var timer = new TimerInfo();

            await starterFunction.RunAsync(timer, _durableOrchestrationClient.Object);

            Assert.IsNotNull(_loggerMock.MessagesLogged.Single(x => x.Message.Contains("function started")));
            _durableOrchestrationClient.Verify(x => x.ScheduleNewOrchestrationInstanceAsync(It.IsAny<TaskName>(), instanceId, null,It.IsAny<CancellationToken>()), Times.Once());
            Assert.IsNotNull(_loggerMock.MessagesLogged.Single(x => x.Message == $"Calling {instanceId}"));
            Assert.IsNotNull(_loggerMock.MessagesLogged.Single(x => x.Message.Contains("function complete")));
        }
    }
}
