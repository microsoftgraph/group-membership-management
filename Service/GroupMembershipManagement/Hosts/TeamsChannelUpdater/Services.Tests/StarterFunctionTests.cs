// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using Hosts.TeamsChannelUpdater;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using Repositories.Contracts;
using Repositories.Mocks;

namespace Services.Tests
{
    [TestClass]
    public class StarterFunctionTests
    {
        private string _instanceId;
        private Mock<ILoggingRepository> _loggerMock;
        private Mock<MockDurableTaskClient> _durableClientMock;
        private SyncJob _syncJob;
        private Mock<ServiceBusReceiver> _serviceBusReceiverMock;

        [TestInitialize]
        public void SetupTest()
        {
            _instanceId = "1234567890";
            _durableClientMock = new Mock<MockDurableTaskClient>();
            _loggerMock = new Mock<ILoggingRepository>();
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
            _durableClientMock
                .Setup(x => x.ScheduleNewOrchestrationInstanceAsync(It.IsAny<TaskName>(), It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(_instanceId);

            var instanceId = nameof(QueueMessageOrchestratorFunction);
            var starterFunction = new StarterFunction(_loggerMock.Object, _serviceBusReceiverMock.Object);
            var timer = new TimerInfo();

            await starterFunction.RunAsync(timer, _durableClientMock.Object);

            _loggerMock.Verify(x => x.LogMessageAsync(
                                        It.Is<LogMessage>(m => m.Message.Contains("function started")),
                                        It.IsAny<VerbosityLevel>(),
                                        It.IsAny<string>(),
                                        It.IsAny<string>()
                                        ), Times.Once());

            _durableClientMock.Verify(x => x.ScheduleNewOrchestrationInstanceAsync(instanceId, instanceId, null, It.IsAny<CancellationToken>()), Times.Once());

            _loggerMock.Verify(x => x.LogMessageAsync(
                            It.Is<LogMessage>(m => m.Message == $"Calling {instanceId}"),
                            It.IsAny<VerbosityLevel>(),
                            It.IsAny<string>(),
                            It.IsAny<string>()
                            ), Times.Once());

            _loggerMock.Verify(x => x.LogMessageAsync(
                            It.Is<LogMessage>(m => m.Message.Contains("function complete")),
                            It.IsAny<VerbosityLevel>(),
                            It.IsAny<string>(),
                            It.IsAny<string>()
                            ), Times.Once());
        }
    }
}
