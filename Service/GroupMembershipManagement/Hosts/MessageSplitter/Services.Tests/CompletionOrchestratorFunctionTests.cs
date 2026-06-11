// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.MessageSplitter;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.ServiceBus;
using Moq;

namespace Services.Tests
{
    [TestClass]
    public class CompletionOrchestratorFunctionTests
    {
        [TestMethod]
        public async Task RunAsync_ReleasesLease_AndLogs()
        {
            var runId = Guid.NewGuid();
            var signal = new MessageSplitterCompletionSignal(runId, "small");

            var context = new Mock<TaskOrchestrationContext> { DefaultValue = DefaultValue.Mock };
            context.Setup(x => x.GetInput<MessageSplitterCompletionSignal>()).Returns(signal);
            context.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(NullLogger.Instance);

            context.Setup(x => x.Entities.CallEntityAsync<bool>(
                    It.IsAny<EntityInstanceId>(),
                    nameof(RunLimiter.Release),
                    runId,
                    It.IsAny<CallEntityOptions>()))
                   .ReturnsAsync(true);

                context.Setup(x => x.CallSubOrchestratorAsync(
                    nameof(DeferredPendingDrainOrchestrator),
                    It.IsAny<DeferredPendingDrainRequest>(),
                    It.IsAny<TaskOptions>()))
                   .Returns(Task.CompletedTask);

            var orchestrator = new CompletionOrchestratorFunction(new RunLimiterSettings { IsEnabled = true });
            await orchestrator.RunAsync(context.Object);

            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                It.IsAny<EntityInstanceId>(),
                nameof(RunLimiter.Release),
                runId,
                It.IsAny<CallEntityOptions>()), Times.Once());

            context.Verify(x => x.CallSubOrchestratorAsync(
                nameof(DeferredPendingDrainOrchestrator),
                It.IsAny<DeferredPendingDrainRequest>(),
                It.IsAny<TaskOptions>()), Times.Once());
        }

        [TestMethod]
        public async Task RunAsync_WhenRunLimiterDisabled_NoOps()
        {
            var runId = Guid.NewGuid();
            var signal = new MessageSplitterCompletionSignal(runId, "small");

            var context = new Mock<TaskOrchestrationContext> { DefaultValue = DefaultValue.Mock };
            context.Setup(x => x.GetInput<MessageSplitterCompletionSignal>()).Returns(signal);
            context.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(NullLogger.Instance);

            var orchestrator = new CompletionOrchestratorFunction(new RunLimiterSettings { IsEnabled = false });
            await orchestrator.RunAsync(context.Object);
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                It.IsAny<EntityInstanceId>(),
                nameof(RunLimiter.Release),
                It.IsAny<object>(),
                It.IsAny<CallEntityOptions>()), Times.Never());
            context.Verify(x => x.CallSubOrchestratorAsync(
                nameof(DeferredPendingDrainOrchestrator),
                It.IsAny<DeferredPendingDrainRequest>(),
                It.IsAny<TaskOptions>()), Times.Never());
        }
    }
}
