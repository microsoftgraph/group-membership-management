// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Messaging.ServiceBus;
using Hosts.MessageSplitter;
using MessageSplitter.Contracts;
using MessageSplitter.Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using Repositories.Contracts;
using System.Text;
using System.Text.Json;

namespace Services.Tests
{
    [TestClass]
    public class StarterFunctionTests
    {
        private SyncJob _syncJob;
        private string _instanceId;
        private MembershipUpdaters _membershipUpdaters;
        private Mock<DurableTaskClient> _durableClient;
        private Mock<ILoggingRepository> _loggingRepository;
        private Mock<IMessageSplitterService> _messageSplitterService;
        private Mock<ServiceBusMessageActions> _serviceBusMessageActions;

        [TestInitialize]
        public void SetupTest()
        {
            _instanceId = "1234567890";
            _loggingRepository = new Mock<ILoggingRepository>();
            _messageSplitterService = new Mock<IMessageSplitterService>();
            _durableClient = new Mock<DurableTaskClient>("test");
            _serviceBusMessageActions = new Mock<ServiceBusMessageActions>();

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

            _membershipUpdaters = Helpers.GetAvailableMembershipUpdaters();

            _durableClient
                  .Setup(x => x.ScheduleNewOrchestrationInstanceAsync(It.IsAny<TaskName>(),
                                                                      It.IsAny<MembershipAggregatorHttpRequest>(),
                                                                      It.IsAny<StartOrchestrationOptions>(),
                                                                      It.IsAny<CancellationToken>()
                                                                      ))
                  .ReturnsAsync(_instanceId);

            var dec = new Mock<DurableEntityClient>("test");
            dec.Setup(x => x.GetEntityAsync<int>(It.IsAny<EntityInstanceId>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(() => new EntityMetadata<int>(new EntityInstanceId("test", "test"), 1));

            _durableClient.Setup(x => x.Entities).Returns(dec.Object);
            _durableClient.Setup(x => x.GetInstanceAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                          .ReturnsAsync(() => new OrchestrationMetadata("test", "test") { RuntimeStatus = OrchestrationRuntimeStatus.Completed });

        }

        [TestMethod]
        public async Task ProcessServiceBusMessageAsync()
        {
            var starterFunction = new StarterFunction(_loggingRepository.Object, _messageSplitterService.Object, _membershipUpdaters);
            var content = new MembershipAggregatorHttpRequest
            {
                FilePath = "file/path/name.json",
                SyncJob = _syncJob,
                PartNumber = 1,
                PartsCount = 1
            };

            var contentBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(content));

            var properties = new Dictionary<string, object>
            {
                { "Type", "GroupMembership" }
            };

            var message = ServiceBusModelFactory.ServiceBusReceivedMessage(new BinaryData(contentBytes), properties: properties);
            await starterFunction.ProcessServiceBusMessageAsync(message, _serviceBusMessageActions.Object, _durableClient.Object);

            _durableClient.Verify(x => x.ScheduleNewOrchestrationInstanceAsync(
                        nameof(OrchestratorFunction),
                        It.IsAny<OrchestratorRequest>(),
                        It.IsAny<StartOrchestrationOptions>(),
                        It.IsAny<CancellationToken>()), Times.Once());


            _loggingRepository.Verify(x => x.LogMessageAsync(
                        It.Is<LogMessage>(m => m.Message.StartsWith("InstanceID:")),
                        VerbosityLevel.INFO,
                        It.IsAny<string>(),
                        It.IsAny<string>()), Times.Once());
        }

        [TestMethod]
        public async Task ProcessLargeServiceBusMessageAsync()
        {
            var starterFunction = new StarterFunction(_loggingRepository.Object, _messageSplitterService.Object, Helpers.GetAvailableMembershipUpdaters(currentLaneSize: "Large"));
            var content = new MembershipAggregatorHttpRequest
            {
                FilePath = "file/path/name.json",
                SyncJob = _syncJob,
                PartNumber = 1,
                PartsCount = 1
            };

            var contentBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(content));

            var properties = new Dictionary<string, object>
            {
                { "Type", "GroupMembership" }
            };

            var message = ServiceBusModelFactory.ServiceBusReceivedMessage(new BinaryData(contentBytes), properties: properties);
            await starterFunction.ProcessServiceBusMessageAsync(message, _serviceBusMessageActions.Object, _durableClient.Object);

            _durableClient.Verify(x => x.ScheduleNewOrchestrationInstanceAsync(
                        nameof(OrchestratorFunction),
                        It.IsAny<OrchestratorRequest>(),
                        It.IsAny<StartOrchestrationOptions>(),
                        It.IsAny<CancellationToken>()), Times.Once());


            _loggingRepository.Verify(x => x.LogMessageAsync(
                        It.Is<LogMessage>(m => m.Message.StartsWith("InstanceID:")),
                        VerbosityLevel.INFO,
                        It.IsAny<string>(),
                        It.IsAny<string>()), Times.Once());
        }


        [TestMethod]
        public async Task ProcessServiceBusMessageWithInvalidTypeAsync()
        {
            var starterFunction = new StarterFunction(_loggingRepository.Object, _messageSplitterService.Object, _membershipUpdaters);
            var content = new MembershipAggregatorHttpRequest
            {
                FilePath = "file/path/name.json",
                SyncJob = _syncJob,
                PartNumber = 1,
                PartsCount = 1
            };

            var contentBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(content));

            var properties = new Dictionary<string, object>
            {
                { "Type", "INVALID_TYPE" }
            };

            var message = ServiceBusModelFactory.ServiceBusReceivedMessage(new BinaryData(contentBytes), properties: properties);
            await Assert.ThrowsExceptionAsync<KeyNotFoundException>(async () => await starterFunction.ProcessServiceBusMessageAsync(message, _serviceBusMessageActions.Object, _durableClient.Object));

            _durableClient.Verify(x => x.ScheduleNewOrchestrationInstanceAsync(
                        nameof(OrchestratorFunction),
                        It.IsAny<OrchestratorRequest>(),
                        It.IsAny<StartOrchestrationOptions>(),
                        It.IsAny<CancellationToken>()), Times.Never());


            _loggingRepository.Verify(x => x.LogMessageAsync(
                        It.Is<LogMessage>(m => m.Message.StartsWith("Unexpected error")),
                        It.IsAny<VerbosityLevel>(),
                        It.IsAny<string>(),
                        It.IsAny<string>()), Times.Once());

            _messageSplitterService.Verify(x => x.UpdateJobStatusAsync(It.IsAny<Guid>(), SyncStatus.Error), Times.Once());
        }
    }
}
