// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using Hosts.TeamsChannelUpdater;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class StarterFunctionTests
    {
        private string _instanceId;
        private Mock<DurableTaskClient> _durableClientMock;
        private SyncJob _syncJob;
        private MembershipHttpRequest _request;

        [TestInitialize]
        public void SetupTest()
        {
            _instanceId = "1234567890";
            _durableClientMock = new Mock<DurableTaskClient>("test");
            _syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                ThresholdPercentageForAdditions = 80,
                ThresholdPercentageForRemovals = 20,
                LastRunTime = DateTime.UtcNow.AddDays(-1),
                Requestor = "user@domail.com",
                RunId = Guid.NewGuid(),
                MembershipType = "TeamsChannelMembership"
            };
            _request = new MembershipHttpRequest
            {
                FilePath = "/file/path/name.json",
                SyncJob = _syncJob,
                GroupId = Guid.NewGuid(),
                ProjectedMemberCount = 2,
                MembersToBeAdded = 1,
                MembersToBeRemoved = 1
            };

            // No existing orchestration for this run id -> the trigger should schedule a new one.
            _durableClientMock
                .Setup(x => x.GetInstanceAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((OrchestrationMetadata)null);

            _durableClientMock
                .Setup(x => x.ScheduleNewOrchestrationInstanceAsync(It.IsAny<TaskName>(), It.IsAny<object>(), It.IsAny<StartOrchestrationOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(_instanceId);
        }

        private static ServiceBusReceivedMessage CreateMessage(object body)
        {
            var contentBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(body));
            return ServiceBusModelFactory.ServiceBusReceivedMessage(new BinaryData(contentBytes), messageId: Guid.NewGuid().ToString());
        }

        [TestMethod]
        public async Task ProcessValidRequestTest()
        {
            var starterFunction = new StarterFunction(NullLogger<StarterFunction>.Instance);
            var message = CreateMessage(_request);

            await starterFunction.RunAsync(message, _durableClientMock.Object);

            _durableClientMock.Verify(x => x.ScheduleNewOrchestrationInstanceAsync(
                It.Is<TaskName>(n => n == nameof(OrchestratorFunction)),
                It.IsAny<object>(),
                It.Is<StartOrchestrationOptions>(o => o.InstanceId.Contains(_syncJob.RunId.ToString())),
                It.IsAny<CancellationToken>()), Times.Once());
        }

        [TestMethod]
        public async Task EmptyMessageDoesNotStartOrchestrationTest()
        {
            var starterFunction = new StarterFunction(NullLogger<StarterFunction>.Instance);
            var emptyMessage = ServiceBusModelFactory.ServiceBusReceivedMessage(new BinaryData(Encoding.UTF8.GetBytes("null")), messageId: "empty");

            await starterFunction.RunAsync(emptyMessage, _durableClientMock.Object);

            _durableClientMock.Verify(x => x.ScheduleNewOrchestrationInstanceAsync(
                It.IsAny<TaskName>(), It.IsAny<object>(), It.IsAny<StartOrchestrationOptions>(), It.IsAny<CancellationToken>()), Times.Never());
        }
    }
}
