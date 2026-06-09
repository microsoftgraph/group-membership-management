// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Models.Notifications;
using Models.ThresholdNotifications;
using Hosts.Notifier;
using System.Text.Json;
using Services.Tests;
using System.Linq;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging.Abstractions;

namespace Services.Notifier.Tests
{
    [TestClass]
    public class OrchestratorFunctionTests
    {
        private Mock<TaskOrchestrationContext> _durableContext;
        private OrchestratorFunction _orchestratorFunction;
        private const string GroupMembership = "GroupMembership";

        [TestInitialize]
        public void SetupTest()
        {
            _durableContext = new Mock<TaskOrchestrationContext>();
            _durableContext.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(NullLogger.Instance);
            _orchestratorFunction = new OrchestratorFunction();
        }

        [TestMethod]
        public async Task RunOrchestratorAsync_ThresholdNotification_CallsAppropriateFunctions()
        {
            var runId = Guid.NewGuid();
            _durableContext.Setup(x => x.NewGuid()).Returns(runId);

            SyncJob job = SampleDataHelper.CreateSampleSyncJobs(1, GroupMembership).First();

            var messageContent = new Dictionary<string, object>
                {
                    { "ThresholdResult", new ThresholdResult() },
                    { "SyncJob", job },
                    { "SendDisableJobNotification", true.ToString() }
                };

            var serializedMessageContent = JsonSerializer.Serialize(messageContent);

            var orchestratorRequest = new OrchestratorRequest
            {
                MessageType = nameof(NotificationMessageType.ThresholdNotification),
                MessageBody = serializedMessageContent
            };
            _durableContext.Setup(x => x.GetInput<OrchestratorRequest>()).Returns(orchestratorRequest);

            var thresholdNotification = new ThresholdNotification();
            _durableContext.Setup(x => x.CallActivityAsync<ThresholdNotification>(
                    nameof(CreateThresholdNotificationFunction),
                    It.IsAny<OrchestratorRequest>(),
                    It.IsAny<TaskOptions>()))
                .ReturnsAsync(thresholdNotification);

            _durableContext.Setup(x => x.CallActivityAsync(
                    nameof(SendThresholdNotification),
                    thresholdNotification,
                    It.IsAny<TaskOptions>()))
                .Returns(Task.CompletedTask);

            _durableContext.Setup(x => x.CallActivityAsync(
                    nameof(UpdateNotificationStatusFunction),
                    It.IsAny<UpdateNotificationStatusRequest>(),
                    It.IsAny<TaskOptions>()))
                .Returns(Task.CompletedTask);

            await _orchestratorFunction.RunOrchestratorAsync(_durableContext.Object);

            _durableContext.Verify(x => x.CreateReplaySafeLogger("Notifier.OrchestratorFunction"), Times.Once);

            _durableContext.Verify(x => x.CallActivityAsync<ThresholdNotification>(
                nameof(CreateThresholdNotificationFunction),
                It.IsAny<OrchestratorRequest>(),
                It.IsAny<TaskOptions>()), Times.Once);

            _durableContext.Verify(x => x.CallActivityAsync(
                nameof(SendThresholdNotification),
                thresholdNotification,
                It.IsAny<TaskOptions>()), Times.Once);

            _durableContext.Verify(x => x.CallActivityAsync(
                nameof(UpdateNotificationStatusFunction),
                It.IsAny<UpdateNotificationStatusRequest>(),
                It.IsAny<TaskOptions>()), Times.Once);
        }
    }
}