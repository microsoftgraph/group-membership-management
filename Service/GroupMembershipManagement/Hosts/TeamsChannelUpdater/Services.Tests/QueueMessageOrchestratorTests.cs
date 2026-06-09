// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using Hosts.TeamsChannelUpdater;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using System.Text;
using System.Text.Json;

namespace Services.Tests
{
    [TestClass]
    public class QueueMessageOrchestratorTests
    {
        private MembershipHttpRequest _request;
        private Mock<TaskOrchestrationContext> _context;
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

            _context = new Mock<TaskOrchestrationContext>();
            _serviceBusReceiverMock = new Mock<ServiceBusReceiver>();

            _context.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(NullLogger.Instance);

            _context.Setup(x => x.CallActivityAsync<MembershipHttpRequest>(It.Is<TaskName>(n => n == nameof(MessageReaderFunction)), null))
                .ReturnsAsync(() => _request);

            _context.SetupGet(x => x.IsReplaying).Returns(false);

            _serviceBusReceiverMock.Setup(x => x.ReceiveMessageAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                                   .ReturnsAsync(() =>
                                   {
                                       if (_request == null) return null;
                                       var contentBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(_request));
                                       var properties = new Dictionary<string, object> { { "Type", "GroupMembership" } };
                                       var message = ServiceBusModelFactory.ServiceBusReceivedMessage(new BinaryData(contentBytes), properties: properties);
                                       return message;
                                   });
        }

        [TestMethod]
        public async Task RunOrchestratorWithMessagesInQueueAsync()
        {
            var orchestrator = new QueueMessageOrchestratorFunction();
            await orchestrator.RunOrchestratorAsync(_context.Object);

            _context.Verify(x => x.CallSubOrchestratorAsync(nameof(OrchestratorFunction), It.IsAny<MembershipHttpRequest>(), null), Times.Once());

            _context.Verify(x => x.ContinueAsNew(It.IsAny<object>(), It.IsAny<bool>()), Times.Once());
        }

        [TestMethod]
        public async Task RunOrchestratorWithNoMessagesInQueueAsync()
        {
            _request = null;

            var orchestrator = new QueueMessageOrchestratorFunction();
            await orchestrator.RunOrchestratorAsync(_context.Object);

            _context.Verify(x => x.CallSubOrchestratorAsync(nameof(OrchestratorFunction), It.IsAny<MembershipHttpRequest>(), null), Times.Never());
        }

        [TestMethod]
        public async Task MainOrchestratorFailsAsync()
        {
            _context.Setup(x => x.CallSubOrchestratorAsync(nameof(OrchestratorFunction), It.IsAny<MembershipHttpRequest>(), null))
                .Throws(new Exception("Main orchestrator failed."));

            var orchestrator = new QueueMessageOrchestratorFunction();
            await orchestrator.RunOrchestratorAsync(_context.Object);

            _context.Verify(x => x.CallSubOrchestratorAsync(nameof(OrchestratorFunction), It.IsAny<MembershipHttpRequest>(), null), Times.Once());
        }

        private async Task<MembershipHttpRequest> CallMessageReaderFunctionAsync()
        {
            var messageReader = new MessageReaderFunction(NullLogger<MessageReaderFunction>.Instance, _serviceBusReceiverMock.Object);
            var response = await messageReader.GetSyncJobAsync(null);
            return response;
        }
    }
}