// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Messaging.ServiceBus;
using Hosts.MessageSplitter;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
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
    public class PendingDispatcherFunctionTests
    {
        private Mock<ILoggingRepository> _loggingRepository;
        private Mock<ServiceBusMessageActions> _actions;
        private Mock<DurableTaskClient> _durableClient;

        [TestInitialize]
        public void Setup()
        {
            _loggingRepository = new Mock<ILoggingRepository>();
            _actions = new Mock<ServiceBusMessageActions>();
            _durableClient = new Mock<DurableTaskClient>("test");

            _actions
                .Setup(x => x.DeferMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), It.IsAny<IDictionary<string, object>>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            _actions
                .Setup(x => x.DeadLetterMessageAsync(
                    It.IsAny<ServiceBusReceivedMessage>(),
                    It.IsAny<Dictionary<string, object>>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            _durableClient
                .Setup(x => x.ScheduleNewOrchestrationInstanceAsync(
                    It.IsAny<TaskName>(),
                    It.IsAny<object>(),
                    It.IsAny<StartOrchestrationOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync("instance-id");
        }

        [TestMethod]
        public async Task ProcessPendingAsync_IndexesAndDefers_AndKicksDrain()
        {
            var runId = Guid.NewGuid();
            var syncJobId = Guid.NewGuid();

            var request = new OrchestratorRequest
            {
                CurrentLaneSize = "Small",
                MessageId = Guid.NewGuid().ToString(),
                SubscriptionName = "Small",
                UpdaterType = "GroupMembership",
                MembershipRequest = new MembershipHttpRequest
                {
                    SyncJob = new SyncJob { Id = syncJobId, RunId = runId },
                    FilePath = "/file.json",
                    ProjectedMemberCount = 1,
                    MembersToBeAdded = 1,
                    MembersToBeRemoved = 0,
                    GroupId = Guid.NewGuid()
                }
            };

            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request));
            var message = ServiceBusModelFactory.ServiceBusReceivedMessage(new BinaryData(bytes), sequenceNumber: 123);

            var function = new PendingDispatcherFunction(_loggingRepository.Object);
            await function.ProcessPendingAsync(message, _actions.Object, _durableClient.Object);

            _actions.Verify(x => x.DeferMessageAsync(message, It.IsAny<IDictionary<string, object>>(), It.IsAny<CancellationToken>()), Times.Once());

            _durableClient.Verify(x => x.ScheduleNewOrchestrationInstanceAsync(
                It.Is<TaskName>(t => t.Name == nameof(DeferredPendingEnqueueOrchestrator)),
                It.Is<object>(o => o is DeferredPendingEnqueueRequest),
                It.IsAny<StartOrchestrationOptions>(),
                It.IsAny<CancellationToken>()), Times.Once());

            _loggingRepository.Verify(x => x.UpsertSyncJobProperties(runId, It.IsAny<Dictionary<string, string>>()));
        }

        [TestMethod]
        public async Task ProcessPendingAsync_DeadLetters_WhenInvalidJson()
        {
            var bytes = Encoding.UTF8.GetBytes("{ this is not valid json");
            var message = ServiceBusModelFactory.ServiceBusReceivedMessage(new BinaryData(bytes));

            var function = new PendingDispatcherFunction(_loggingRepository.Object);
            await function.ProcessPendingAsync(message, _actions.Object, _durableClient.Object);

            _actions.Verify(x => x.DeadLetterMessageAsync(
                message,
                It.IsAny<Dictionary<string, object>>(),
                "InvalidPendingMessage",
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()), Times.Once());

            _actions.Verify(x => x.DeferMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), It.IsAny<IDictionary<string, object>>(), It.IsAny<CancellationToken>()), Times.Never());
        }
    }
}
