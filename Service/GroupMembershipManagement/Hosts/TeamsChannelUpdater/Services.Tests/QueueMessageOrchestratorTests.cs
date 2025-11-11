// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using Hosts.TeamsChannelUpdater;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using Repositories.Contracts;
using System.Text;
using System.Text.Json;

namespace Services.Tests
{
    [TestClass]
    public class QueueMessageOrchestratorTests
    {
        private MembershipHttpRequest _request;
        private Mock<ILoggingRepository> _loggerMock;
        private Mock<IDurableOrchestrationContext> _context;
        private Mock<ServiceBusReceiver> _serviceBusReceiverMock;

        [TestInitialize]
        public void SetupTest()
        {
            _request = new MembershipHttpRequest
            {
                GroupId = Guid.NewGuid(),
                SyncJob = new SyncJob
                {
                    RunId = Guid.NewGuid(),
                    Channel = new Channel
                    {
                        GroupId = Guid.NewGuid(),
                        ChannelId = "some-channel"
                    },
                    MembershipType = "TeamsChannelMembership"
                },
                FilePath = "file-path",
                ProjectedMemberCount = 1,
                MembersToBeAdded = 0,
                MembersToBeRemoved = 0
            };

            _loggerMock = new Mock<ILoggingRepository>();
            _context = new Mock<IDurableOrchestrationContext>();
            _serviceBusReceiverMock = new Mock<ServiceBusReceiver>();

            MembershipHttpRequest response = null;
            _context.Setup(x => x.CallActivityAsync<MembershipHttpRequest>(nameof(MessageReaderFunction), (object)null))
                .Callback(async () => response = await CallMessageReaderFunctionAsync())
                .ReturnsAsync(() => response);

            _serviceBusReceiverMock.Setup(x => x.ReceiveMessageAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                                   .ReturnsAsync(() =>
                                   {
                                       var contentBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(_request));
                                       var properties = new Dictionary<string, object> { { "Type", "GroupMembership" } };
                                       var message = ServiceBusModelFactory.ServiceBusReceivedMessage(new BinaryData(contentBytes), properties: properties);
                                       return message;
                                   });
        }

        [TestMethod]
        public async Task RunOrchestratorWithMessagesInQueueAsync()
        {
            var orchestrator = new QueueMessageOrchestratorFunction(_loggerMock.Object);
            await orchestrator.RunOrchestratorAsync(_context.Object);

            _context.Verify(x => x.CallActivityAsync(nameof(LoggerFunction),
                   It.Is<LoggerRequest>(r => r.Message == "There are no more messages to process at this time.")), Times.Never());

            _context.Verify(x => x.CallActivityAsync(nameof(LoggerFunction),
                                              It.Is<LoggerRequest>(r => r.Message == $"Processing message for group {_request.GroupId}")), Times.Once());

            _context.Verify(x => x.CallSubOrchestratorAsync(nameof(OrchestratorFunction), It.IsAny<MembershipHttpRequest>()), Times.Once());

            _context.Verify(x => x.ContinueAsNew((object)null, false), Times.Once());
        }

        [TestMethod]
        public async Task RunOrchestratorWithNoMessagesInQueueAsync()
        {
            _request = null;

            var orchestrator = new QueueMessageOrchestratorFunction(_loggerMock.Object);
            await orchestrator.RunOrchestratorAsync(_context.Object);

            _context.Verify(x => x.CallActivityAsync(nameof(LoggerFunction),
                               It.Is<LoggerRequest>(r => r.Message == "There are no more messages to process at this time.")), Times.Once());

            _context.Verify(x => x.CallActivityAsync(nameof(LoggerFunction),
                                              It.Is<LoggerRequest>(r => r.Message.StartsWith("Processing message for group"))), Times.Never());

            _context.Verify(x => x.CallSubOrchestratorAsync(nameof(OrchestratorFunction), It.IsAny<MembershipHttpRequest>()), Times.Never());
        }

        [TestMethod]
        public async Task MainOrchestratorFailsAsync()
        {
            _context.Setup(x => x.CallSubOrchestratorAsync(nameof(OrchestratorFunction), It.IsAny<MembershipHttpRequest>()))
                .Throws(new Exception("Main orchestrator failed."));

            var orchestrator = new QueueMessageOrchestratorFunction(_loggerMock.Object);
            await orchestrator.RunOrchestratorAsync(_context.Object);

            _context.Verify(x => x.CallActivityAsync(nameof(LoggerFunction),
                               It.Is<LoggerRequest>(r => r.Message == "There are no more messages to process at this time.")), Times.Never());

            _context.Verify(x => x.CallActivityAsync(nameof(LoggerFunction),
                                              It.Is<LoggerRequest>(r => r.Message == $"Processing message for group {_request.GroupId}")), Times.Once());

            _context.Verify(x => x.CallSubOrchestratorAsync(nameof(OrchestratorFunction), It.IsAny<MembershipHttpRequest>()), Times.Once());
        }

        private async Task<MembershipHttpRequest> CallMessageReaderFunctionAsync()
        {
            var messageReader = new MessageReaderFunction(_loggerMock.Object, _serviceBusReceiverMock.Object);
            var response = await messageReader.GetSyncJobAsync(null);
            return response;
        }
    }
}
