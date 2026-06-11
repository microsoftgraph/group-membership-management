// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using DIConcreteTypes;
using Hosts.MessageSplitter;
using MessageSplitter.TrackerOrchestrator;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Entities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Services.Tests
{
    [TestClass]
    public class InstanceTrackerOrchestratorTests
    {
        [TestMethod]
        public async Task UpdateInstanceTrackerAsync_IncrementsAndWraps()
        {
            var updaters = Helpers.GetAvailableMembershipUpdaters(currentLaneSize: "Small");
            var orchestrator = new InstanceTrackerOrchestrator(updaters);

            var request = new InstanceTrackerOrchestratorRequest("GroupMembership", "Small");

            var context = new Mock<TaskOrchestrationContext> { DefaultValue = DefaultValue.Mock };
            context.Setup(x => x.GetInput<InstanceTrackerOrchestratorRequest>()).Returns(request);

            context.Setup(x => x.Entities.LockEntitiesAsync(It.IsAny<EntityInstanceId>()))
                   .ReturnsAsync(new TestAsyncDisposable());

            // First call returns 0 so we wrap to 1
            context.SetupSequence(x => x.Entities.CallEntityAsync<int>(
                    It.IsAny<EntityInstanceId>(),
                    "Get",
                    It.IsAny<CallEntityOptions>()))
                   .ReturnsAsync(0);

            context.Setup(x => x.Entities.CallEntityAsync(
                    It.IsAny<EntityInstanceId>(),
                    "Set",
                    1,
                    It.IsAny<CallEntityOptions>()))
                   .Returns(Task.CompletedTask);

            var instance = await orchestrator.UpdateInstanceTrackerAsync(context.Object);

            Assert.AreEqual(1, instance);
            context.Verify(x => x.Entities.CallEntityAsync(
                It.IsAny<EntityInstanceId>(),
                "Set",
                1,
                It.IsAny<CallEntityOptions>()), Times.Once());
        }
    }
}
