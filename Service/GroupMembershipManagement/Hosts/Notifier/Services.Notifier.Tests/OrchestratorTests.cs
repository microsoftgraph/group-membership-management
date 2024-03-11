// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models.Notifications;
using Models.ThresholdNotifications;
using Repositories.Contracts;
using Hosts.Notifier;
using System.Text.Json;
using Models.ServiceBus;
using Services.Tests;
using System.Linq;

namespace Services.Notifier.Tests
{
    [TestClass]
    public class OrchestratorFunctionTests
    {
        private Mock<IDurableOrchestrationContext> _durableContext;
        private Mock<ILoggingRepository> _loggerFunction;
        private OrchestratorFunction _orchestratorFunction;
        private const string GroupMembership = "GroupMembership";

        [TestInitialize]
        public void SetupTest()
        {
            _durableContext = new Mock<IDurableOrchestrationContext>();
            _loggerFunction = new Mock<ILoggingRepository>();
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
            _durableContext.Setup(x => x.CallActivityAsync<ThresholdNotification>(nameof(CreateThresholdNotificationFunction), It.IsAny<OrchestratorRequest>()))
                            .ReturnsAsync(thresholdNotification);

            await _orchestratorFunction.RunOrchestratorAsync(_durableContext.Object);

            _durableContext.Verify(x => x.CallActivityAsync<ThresholdNotification>(
                nameof(CreateThresholdNotificationFunction), It.IsAny<OrchestratorRequest>()), Times.Once);
            _durableContext.Verify(x => x.CallActivityAsync(
                nameof(SendThresholdNotification), It.IsAny<ThresholdNotification>()), Times.Once);
            _durableContext.Verify(x => x.CallActivityAsync(
                nameof(UpdateNotificationStatusFunction), It.IsAny<UpdateNotificationStatusRequest>()), Times.Once);
            _durableContext.Verify(x => x.CallActivityAsync(
                nameof(LoggerFunction), It.IsAny<LoggerRequest>()), Times.AtLeastOnce);
        }
    }
}