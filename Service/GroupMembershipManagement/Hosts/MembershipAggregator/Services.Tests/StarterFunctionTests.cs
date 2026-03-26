// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Messaging.ServiceBus;
using Hosts.MembershipAggregator;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
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
        private SyncJob _syncJob;
        private Group _group;
        private string _instanceId;
        private Mock<DurableTaskClient> _durableClient;

        [TestInitialize]
        public void SetupTest()
        {
            _instanceId = "1234567890";
            _durableClient = new Mock<DurableTaskClient>("test");
            _syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                ThresholdPercentageForAdditions = 80,
                ThresholdPercentageForRemovals = 20,
                LastRunTime = DateTime.UtcNow.AddDays(-1),
                Requestor = "user@domail.com",
                RunId = Guid.NewGuid(),
                ThresholdViolations = 0
            };
            _group = new Group
            {
                GroupId = Guid.NewGuid(),
                SyncJobId = _syncJob.Id
            };
            _durableClient
                  .Setup(x => x.ScheduleNewOrchestrationInstanceAsync(
                      It.IsAny<TaskName>(),
                      It.IsAny<object>(),
                      It.IsAny<StartOrchestrationOptions>(),
                      It.IsAny<CancellationToken>()))
                  .ReturnsAsync(_instanceId);
        }

        [TestMethod]
        public async Task ProcessServiceBusMessageAsync()
        {
            var starterFunction = new StarterFunction(NullLogger<StarterFunction>.Instance);
            var content = new MembershipAggregatorHttpRequest
            {
                FilePath = "file/path/name.json",
                SyncJob = _syncJob,
                PartNumber = 1,
                PartsCount = 1,
                IsDestinationPart = false
            };

            var contentBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(content));
            var message = ServiceBusModelFactory.ServiceBusReceivedMessage(new BinaryData(contentBytes));

            await starterFunction.ProcessServiceBusMessageAsync(message, _durableClient.Object);

            _durableClient.Verify(x => x.ScheduleNewOrchestrationInstanceAsync(
                It.IsAny<TaskName>(),
                It.IsAny<object>(),
                It.IsAny<StartOrchestrationOptions>(),
                It.IsAny<CancellationToken>()), Times.Once());
        }
    }
}
