// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Messaging.ServiceBus;
using Hosts.MessageSplitter;
using MessageSplitter.Contracts;
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
        private Mock<IMessageSplitterService> _messageSplitterService;
        private Mock<ServiceBusMessageActions> _actions;
        private Mock<DurableTaskClient> _durableClient;

        [TestInitialize]
        public void Setup()
        {
            _loggingRepository = new Mock<ILoggingRepository>();
            _messageSplitterService = new Mock<IMessageSplitterService>();
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

            _messageSplitterService
                .Setup(x => x.UpdateJobStatusAsync(It.IsAny<Guid>(), It.IsAny<SyncStatus>()))
                .Returns(Task.CompletedTask);
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

            var function = new PendingDispatcherFunction(_loggingRepository.Object, _messageSplitterService.Object);
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

            var function = new PendingDispatcherFunction(_loggingRepository.Object, _messageSplitterService.Object);
            await function.ProcessPendingAsync(message, _actions.Object, _durableClient.Object);

            _actions.Verify(x => x.DeadLetterMessageAsync(
                message,
                It.IsAny<Dictionary<string, object>>(),
                "InvalidPendingMessage",
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()), Times.Once());

            _actions.Verify(x => x.DeferMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), It.IsAny<IDictionary<string, object>>(), It.IsAny<CancellationToken>()), Times.Never());
        }

        [TestMethod]
        public async Task ProcessPendingAsync_DoesNotDefer_WhenOrchestratorSchedulingFails()
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
            var message = ServiceBusModelFactory.ServiceBusReceivedMessage(new BinaryData(bytes), sequenceNumber: 123, deliveryCount: 1);

            // Simulate orchestrator scheduling failure (e.g., gRPC timeout)
            _durableClient
                .Setup(x => x.ScheduleNewOrchestrationInstanceAsync(
                    It.IsAny<TaskName>(),
                    It.IsAny<object>(),
                    It.IsAny<StartOrchestrationOptions>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("gRPC timeout"));

            var function = new PendingDispatcherFunction(_loggingRepository.Object, _messageSplitterService.Object);

            await Assert.ThrowsExceptionAsync<Exception>(
                () => function.ProcessPendingAsync(message, _actions.Object, _durableClient.Object));

            // Verify that the message was NOT deferred since orchestrator scheduling failed
            _actions.Verify(x => x.DeferMessageAsync(
                It.IsAny<ServiceBusReceivedMessage>(),
                It.IsAny<IDictionary<string, object>>(),
                It.IsAny<CancellationToken>()), Times.Never());

            // Verify job status was NOT updated (not at max delivery count)
            _messageSplitterService.Verify(x => x.UpdateJobStatusAsync(
                It.IsAny<Guid>(),
                It.IsAny<SyncStatus>()), Times.Never());
        }

        [TestMethod]
        public async Task ProcessPendingAsync_SetsJobToError_WhenMaxDeliveryCountReached()
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
            // Set deliveryCount to 10 (max delivery count)
            var message = ServiceBusModelFactory.ServiceBusReceivedMessage(new BinaryData(bytes), sequenceNumber: 123, deliveryCount: 10);

            // Simulate orchestrator scheduling failure
            _durableClient
                .Setup(x => x.ScheduleNewOrchestrationInstanceAsync(
                    It.IsAny<TaskName>(),
                    It.IsAny<object>(),
                    It.IsAny<StartOrchestrationOptions>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("gRPC timeout"));

            var function = new PendingDispatcherFunction(_loggingRepository.Object, _messageSplitterService.Object);

            // Should NOT throw - should handle gracefully at max delivery count
            await function.ProcessPendingAsync(message, _actions.Object, _durableClient.Object);

            // Verify job status was set to Error
            _messageSplitterService.Verify(x => x.UpdateJobStatusAsync(
                syncJobId,
                SyncStatus.Error), Times.Once());

            // Verify message was dead-lettered
            _actions.Verify(x => x.DeadLetterMessageAsync(
                message,
                It.IsAny<Dictionary<string, object>>(),
                "MaxDeliveryCountExceeded",
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()), Times.Once());

            // Verify message was NOT deferred
            _actions.Verify(x => x.DeferMessageAsync(
                It.IsAny<ServiceBusReceivedMessage>(),
                It.IsAny<IDictionary<string, object>>(),
                It.IsAny<CancellationToken>()), Times.Never());
        }
    }
}
