// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Messaging.ServiceBus;
using DIConcreteTypes;
using Hosts.MessageSplitter;
using MessageSplitter.Contracts;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Client;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using Repositories.Contracts;
using System;
using System.Text;
using System.Text.Json;

namespace Services.Tests
{
    [TestClass]
    public class StarterFunctionTests
    {
        private SyncJob _syncJob;
        private Group _group;
        private MembershipUpdaters _membershipUpdaters;
        private Mock<ILoggingRepository> _loggingRepository;
        private Mock<IMessageSplitterService> _messageSplitterService;
        private Mock<ServiceBusMessageActions> _serviceBusMessageActions;
        private Mock<IServiceBusTopicsRepository> _messageSplitterTopicSenderRepository;
        private Mock<DurableTaskClient> _durableClient;
        private RunLimiterSettings _runLimiterSettings;

        [TestInitialize]
        public void SetupTest()
        {
            _loggingRepository = new Mock<ILoggingRepository>();
            _messageSplitterService = new Mock<IMessageSplitterService>();
            _serviceBusMessageActions = new Mock<ServiceBusMessageActions>();
            _messageSplitterTopicSenderRepository = new Mock<IServiceBusTopicsRepository>();
            _durableClient = new Mock<DurableTaskClient>("test");
            _runLimiterSettings = new RunLimiterSettings
            {
                IsEnabled = true,
                MaxInFlightMessages = 5,
                LeaseTimeoutMinutes = 10
            };

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
                SyncJobId = _syncJob.Id,
                GroupId = Guid.NewGuid()
            };

            _membershipUpdaters = Helpers.GetAvailableMembershipUpdaters();
        }

        [TestMethod]
        public async Task ProcessServiceBusMessageAsync()
        {
            var starterFunction = new StarterFunction(
                _loggingRepository.Object,
                _messageSplitterService.Object,
                _membershipUpdaters,
                _messageSplitterTopicSenderRepository.Object,
                _runLimiterSettings);

            var content = new MembershipHttpRequest
            {
                FilePath = "file/path/name.json",
                SyncJob = _syncJob,
                GroupId = _group.GroupId,
                ProjectedMemberCount = 1000,
                MembersToBeAdded = 1000,
                MembersToBeRemoved = 0
            };

            var contentBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(content));

            var properties = new Dictionary<string, object>
            {
                { "Type", "GroupMembership" }
            };

            var message = ServiceBusModelFactory.ServiceBusReceivedMessage(new BinaryData(contentBytes), properties: properties);
            await starterFunction.ProcessServiceBusMessageAsync(message, _serviceBusMessageActions.Object, _durableClient.Object);

            _messageSplitterTopicSenderRepository.Verify(x => x.AddMessageAsync(It.IsAny<Models.ServiceBus.ServiceBusMessage>()), Times.Once());
            _serviceBusMessageActions.Verify(x => x.CompleteMessageAsync(message, It.IsAny<CancellationToken>()), Times.Once());

            _messageSplitterTopicSenderRepository.Verify(x => x.AddMessageAsync(
                It.Is<Models.ServiceBus.ServiceBusMessage>(m =>
                    m.ApplicationProperties["MessageType"].Equals("pending_small")
                )), Times.Once());
        }

        [TestMethod]
        public async Task ProcessLargeServiceBusMessageAsync()
        {
            var starterFunction = new StarterFunction(
                _loggingRepository.Object,
                _messageSplitterService.Object,
                Helpers.GetAvailableMembershipUpdaters(currentLaneSize: "Large"),
                _messageSplitterTopicSenderRepository.Object,
                _runLimiterSettings);

            var content = new MembershipHttpRequest
            {
                FilePath = "file/path/name.json",
                SyncJob = _syncJob,
                GroupId = _group.GroupId,
                ProjectedMemberCount = 1000,
                MembersToBeAdded = 1000,
                MembersToBeRemoved = 0
            };

            var contentBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(content));

            var properties = new Dictionary<string, object>
            {
                { "Type", "GroupMembership" }
            };

            var message = ServiceBusModelFactory.ServiceBusReceivedMessage(new BinaryData(contentBytes), properties: properties);
            await starterFunction.ProcessServiceBusMessageAsync(message, _serviceBusMessageActions.Object, _durableClient.Object);

            _messageSplitterTopicSenderRepository.Verify(x => x.AddMessageAsync(It.IsAny<Models.ServiceBus.ServiceBusMessage>()), Times.Once());
            _serviceBusMessageActions.Verify(x => x.CompleteMessageAsync(message, It.IsAny<CancellationToken>()), Times.Once());

            _messageSplitterTopicSenderRepository.Verify(x => x.AddMessageAsync(
                It.Is<Models.ServiceBus.ServiceBusMessage>(m =>
                    m.ApplicationProperties["MessageType"].Equals("pending_large")
                )), Times.Once());
        }


        [TestMethod]
        public async Task ProcessServiceBusMessageWithInvalidTypeAsync()
        {
            var starterFunction = new StarterFunction(
                _loggingRepository.Object,
                _messageSplitterService.Object,
                _membershipUpdaters,
                _messageSplitterTopicSenderRepository.Object,
                _runLimiterSettings);

            var content = new MembershipHttpRequest
            {
                FilePath = "file/path/name.json",
                SyncJob = _syncJob,
                GroupId = _group.GroupId,
                ProjectedMemberCount = 1000,
                MembersToBeAdded = 1000,
                MembersToBeRemoved = 0
            };

            var contentBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(content));

            var properties = new Dictionary<string, object>
            {
                { "Type", "INVALID_TYPE" }
            };

            var message = ServiceBusModelFactory.ServiceBusReceivedMessage(new BinaryData(contentBytes), properties: properties);
            await Assert.ThrowsExceptionAsync<KeyNotFoundException>(async () => await starterFunction.ProcessServiceBusMessageAsync(message, _serviceBusMessageActions.Object, _durableClient.Object));

            _messageSplitterTopicSenderRepository.Verify(x => x.AddMessageAsync(It.IsAny<Models.ServiceBus.ServiceBusMessage>()), Times.Never());


            _loggingRepository.Verify(x => x.LogMessageAsync(
                        It.Is<LogMessage>(m => m.Message.StartsWith("Unexpected error")),
                        It.IsAny<VerbosityLevel>(),
                        It.IsAny<string>(),
                        It.IsAny<string>()), Times.Once());

            _messageSplitterService.Verify(x => x.UpdateJobStatusAsync(It.IsAny<Guid>(), SyncStatus.Error), Times.Once());
        }
    }
}
