// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using GraphUpdater.QueueMessageOrchestrator;
using Hosts.GraphUpdater;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using Repositories.Mocks;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class QueueMessageOrchestratorTests
    {
        private MembershipHttpRequest _request;
        private MockLoggingRepository _loggerMock;
        private Mock<TaskOrchestrationContext> _context;
        private Mock<ServiceBusReceiver> _serviceBusReceiverMock;
        private Mock<ServiceBusClient> _serviceBusClient;
        private QueueMessageOrchestratorRequest _orchestratorRequest;
        private OrchestratorRequest _response;

        [TestInitialize]
        public void SetupTest()
        {
            var groupId = Guid.NewGuid();
            
            _request = new MembershipHttpRequest
            {
                SyncJob = new SyncJob
                {
                    RunId = Guid.NewGuid(),
                    MembershipType = "GroupMembership",
                    Group = new Group
                    {
                        GroupId = groupId
                    }
                },
                FilePath = "file-path",
                ProjectedMemberCount = 10,
                MembersToBeAdded = 10,
                MembersToBeRemoved = 0,
                GroupId = groupId
            };

            _loggerMock = new MockLoggingRepository();
            _context = new Mock<TaskOrchestrationContext>();
            _serviceBusClient = new Mock<ServiceBusClient>();
            _serviceBusReceiverMock = new Mock<ServiceBusReceiver>();

            _context.Setup(x => x.CallActivityAsync<Guid>(It.Is<TaskName>(x => x.Name == nameof(GetGroupFunction)), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>())).ReturnsAsync(_request.SyncJob.Group.GroupId);

            _context.Setup(x => x.CallActivityAsync<OrchestratorRequest>(It.Is<TaskName>(x => x.Name == nameof(MessageReaderFunction)), It.IsAny<QueueMessageOrchestratorRequest>(), It.IsAny<TaskOptions>()))
                .Callback(async () => _response = await CallMessageReaderFunctionAsync())
                .ReturnsAsync(() => _response);

            _serviceBusReceiverMock.Setup(x => x.ReceiveMessageAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                                   .ReturnsAsync(() =>
                                   {
                                       var contentBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(_request));
                                       var properties = new Dictionary<string, object> { { "Type", "GroupMembership" } };
                                       var message = ServiceBusModelFactory.ServiceBusReceivedMessage(new BinaryData(contentBytes), properties: properties);
                                       return message;
                                   });
            _serviceBusClient.Setup(x => x.CreateReceiver(It.IsAny<string>(), It.IsAny<string>()))
                             .Returns(_serviceBusReceiverMock.Object);

            _context.Setup(x => x.GetInput<QueueMessageOrchestratorRequest>())
                    .Returns(() => _orchestratorRequest);

            _orchestratorRequest = new QueueMessageOrchestratorRequest
            {
                TopicName = "membershipupdaters",
                SubscriptionName = "GraphUpdater_s1",
                IsMultiLaneEnabled = false
            };
        }

        [TestMethod]
        public async Task RunOrchestratorWithMessagesInQueueAsync()
        {
            var orchestrator = new QueueMessageOrchestratorFunction(_loggerMock);
            await orchestrator.RunOrchestratorAsync(_context.Object);

            _context.Verify(x => x.CallActivityAsync(It.Is<TaskName>(x => x.Name == nameof(LoggerFunction)),
                   It.Is<LoggerRequest>(r => r.Message == "There are no more messages to process at this time."), It.IsAny<TaskOptions>()), Times.Never());

            _context.Verify(x => x.CallActivityAsync(It.Is<TaskName>(x => x.Name == nameof(LoggerFunction)),
                                              It.Is<LoggerRequest>(r => r.Message == $"Processing message for group {_request.SyncJob.Group.GroupId}"), It.IsAny<TaskOptions>()), Times.Once());

            _context.Verify(x => x.CallSubOrchestratorAsync<OrchestrationRuntimeStatus>(It.Is<TaskName>(x => x.Name == nameof(OrchestratorFunction)), It.IsAny<MembershipHttpRequest>(), It.IsAny<TaskOptions>()), Times.Once());

            _context.Verify(x => x.ContinueAsNew(It.IsAny<QueueMessageOrchestratorRequest>(), true), Times.Once());
        }

        [TestMethod]
        public async Task RunOrchestratorWithMultiLaneMessagesInQueueAsync()
        {
            _orchestratorRequest.IsMultiLaneEnabled = true;

            var orchestrator = new QueueMessageOrchestratorFunction(_loggerMock);
            await orchestrator.RunOrchestratorAsync(_context.Object);

            _context.Verify(x => x.CallActivityAsync(It.Is<TaskName>(x => x.Name == nameof(LoggerFunction)),
                   It.Is<LoggerRequest>(r => r.Message == "There are no more messages to process at this time."), It.IsAny<TaskOptions>()), Times.Never());

            _context.Verify(x => x.CallActivityAsync(It.Is<TaskName>(x => x.Name == nameof(LoggerFunction)),
                                              It.Is<LoggerRequest>(r => r.Message == $"Processing message for group {_request.SyncJob.Group.GroupId}"), It.IsAny<TaskOptions>()), Times.Once());

            _context.Verify(x => x.CallSubOrchestratorAsync<OrchestrationRuntimeStatus>(It.Is<TaskName>(x => x.Name == nameof(OrchestratorMultiLaneFunction)), It.IsAny<OrchestratorMultiLaneRequest>(), It.IsAny<TaskOptions>()), Times.Once());

            _context.Verify(x => x.ContinueAsNew(It.IsAny<QueueMessageOrchestratorRequest>(), true), Times.Never());
        }

        [TestMethod]
        public async Task RunOrchestratorWithNoMessagesInQueueAsync()
        {
            _request = null;

            var orchestrator = new QueueMessageOrchestratorFunction(_loggerMock);
            await orchestrator.RunOrchestratorAsync(_context.Object);

            _context.Verify(x => x.CallActivityAsync(It.Is<TaskName>(x => x.Name == nameof(LoggerFunction)),
                               It.Is<LoggerRequest>(r => r.Message == "There are no more messages to process at this time."), It.IsAny<TaskOptions>()), Times.Once());

            _context.Verify(x => x.CallActivityAsync(It.Is<TaskName>(x => x.Name == nameof(LoggerFunction)),
                                              It.Is<LoggerRequest>(r => r.Message.StartsWith("Processing message for group")), It.IsAny<TaskOptions>()), Times.Never());

            _context.Verify(x => x.CallSubOrchestratorAsync<OrchestrationRuntimeStatus>(It.Is<TaskName>(x => x.Name == nameof(OrchestratorFunction)), It.IsAny<MembershipHttpRequest>(), It.IsAny<TaskOptions>()), Times.Never());
        }

        [TestMethod]
        public async Task MainOrchestratorFailsAsync()
        {
            _context.Setup(x => x.CallSubOrchestratorAsync<OrchestrationRuntimeStatus>(It.Is<TaskName>(x => x.Name == nameof(OrchestratorFunction)), It.IsAny<MembershipHttpRequest>(), It.IsAny<TaskOptions>()))
                .Throws(new Exception("Main orchestrator failed."));

            var orchestrator = new QueueMessageOrchestratorFunction(_loggerMock);
            await orchestrator.RunOrchestratorAsync(_context.Object);

            _context.Verify(x => x.CallActivityAsync(It.Is<TaskName>(x => x.Name == nameof(LoggerFunction)),
                               It.Is<LoggerRequest>(r => r.Message == "There are no more messages to process at this time."), It.IsAny<TaskOptions>()), Times.Never());

            _context.Verify(x => x.CallActivityAsync(It.Is<TaskName>(x => x.Name == nameof(LoggerFunction)),
                                              It.Is<LoggerRequest>(r => r.Message == $"Processing message for group {_request.SyncJob.Group.GroupId}"), It.IsAny<TaskOptions>()), Times.Once());

            _context.Verify(x => x.CallSubOrchestratorAsync<OrchestrationRuntimeStatus>(It.Is<TaskName>(x => x.Name == nameof(OrchestratorFunction)), It.IsAny<MembershipHttpRequest>(), It.IsAny<TaskOptions>()), Times.Once());
        }

        private async Task<OrchestratorRequest> CallMessageReaderFunctionAsync()
        {
            var messageReader = new MessageReaderFunction(_loggerMock, _serviceBusClient.Object);
            var response = await messageReader.GetSyncJobAsync(_orchestratorRequest);
            return response;
        }
    }
}



