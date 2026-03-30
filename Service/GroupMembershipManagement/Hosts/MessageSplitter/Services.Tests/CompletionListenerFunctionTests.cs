// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Messaging.ServiceBus;
using Hosts.MessageSplitter;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models.ServiceBus;
using Moq;
using System.Text;
using System.Text.Json;

namespace Services.Tests
{
    [TestClass]
    public class CompletionListenerFunctionTests
    {
        private Mock<ServiceBusMessageActions> _actions;
        private Mock<DurableTaskClient> _durableClient;

        [TestInitialize]
        public void Setup()
        {
            _actions = new Mock<ServiceBusMessageActions>();
            _durableClient = new Mock<DurableTaskClient>("test");

            _actions
                .Setup(x => x.CompleteMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), It.IsAny<CancellationToken>()))
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
        public async Task ProcessCompletionAsync_UsesBody_WhenPresent()
        {
            var runId = Guid.NewGuid();
            var signal = new MessageSplitterCompletionSignal(runId, "small");
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(signal));

            var message = ServiceBusModelFactory.ServiceBusReceivedMessage(new BinaryData(bytes));

            var function = new CompletionListenerFunction(NullLogger<CompletionListenerFunction>.Instance);
            await function.ProcessCompletionAsync(message, _actions.Object, _durableClient.Object);

            _durableClient.Verify(x => x.ScheduleNewOrchestrationInstanceAsync(
                It.Is<TaskName>(t => t.Name == nameof(CompletionOrchestratorFunction)),
                It.Is<object>(o => o is MessageSplitterCompletionSignal && ((MessageSplitterCompletionSignal)o).RunId == runId && ((MessageSplitterCompletionSignal)o).LaneSize == "small"),
                It.IsAny<StartOrchestrationOptions>(),
                It.IsAny<CancellationToken>()), Times.Once());

            _actions.Verify(x => x.CompleteMessageAsync(message, It.IsAny<CancellationToken>()), Times.Once());
        }

        [TestMethod]
        public async Task ProcessCompletionAsync_FallsBackToProperties_WhenBodyEmpty()
        {
            var runId = Guid.NewGuid();

            var props = new Dictionary<string, object>
            {
                { "RunId", runId.ToString() },
                { "MessageType", "completion_small" }
            };

            var message = ServiceBusModelFactory.ServiceBusReceivedMessage(new BinaryData(Array.Empty<byte>()), properties: props);

            var function = new CompletionListenerFunction(NullLogger<CompletionListenerFunction>.Instance);
            await function.ProcessCompletionAsync(message, _actions.Object, _durableClient.Object);

            _durableClient.Verify(x => x.ScheduleNewOrchestrationInstanceAsync(
                It.Is<TaskName>(t => t.Name == nameof(CompletionOrchestratorFunction)),
                It.Is<object>(o => o is MessageSplitterCompletionSignal && ((MessageSplitterCompletionSignal)o).RunId == runId && ((MessageSplitterCompletionSignal)o).LaneSize == "small"),
                It.IsAny<StartOrchestrationOptions>(),
                It.IsAny<CancellationToken>()), Times.Once());

            _actions.Verify(x => x.CompleteMessageAsync(message, It.IsAny<CancellationToken>()), Times.Once());
        }

        [TestMethod]
        public async Task ProcessCompletionAsync_DeadLetters_WhenInvalid()
        {
            var props = new Dictionary<string, object>
            {
                { "RunId", "not-a-guid" },
                { "MessageType", "completion_small" }
            };

            var message = ServiceBusModelFactory.ServiceBusReceivedMessage(new BinaryData(Array.Empty<byte>()), properties: props);

            var function = new CompletionListenerFunction(NullLogger<CompletionListenerFunction>.Instance);
            await function.ProcessCompletionAsync(message, _actions.Object, _durableClient.Object);

            _actions.Verify(x => x.DeadLetterMessageAsync(
                message,
                It.IsAny<Dictionary<string, object>>(),
                "InvalidCompletionMessage",
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()), Times.Once());

            _actions.Verify(x => x.CompleteMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), It.IsAny<CancellationToken>()), Times.Never());
        }
    }
}
