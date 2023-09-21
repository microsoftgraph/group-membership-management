// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.TeamsChannelUpdater;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;

namespace Services.Tests
{
    [TestClass]
    public class MessageProcessorOrchestratorTests
    {
        private string _messageId;
        private MembershipHttpRequest _membershipHttpRequest;
        private DurableOrchestrationStatus _durableOrchestrationStatus;

        private Mock<IMessageEntity> _messageEntity;
        private Mock<IMessageTracker> _messageTracker;
        private Mock<IDurableOrchestrationContext> _durableOrchestrationContext;




        [TestInitialize]
        public void Setup()
        {
            _messageEntity = new Mock<IMessageEntity>();
            _messageTracker = new Mock<IMessageTracker>();

            _messageTracker.Setup(x => x.GetNextMessageIdAsync()).ReturnsAsync(() => _messageId);
            _messageEntity.Setup(x => x.GetAsync()).ReturnsAsync(() => _membershipHttpRequest);


            _durableOrchestrationContext = new Mock<IDurableOrchestrationContext>();
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync(nameof(LoggerFunction), It.IsAny<LoggerRequest>()));
            _durableOrchestrationContext.Setup(x => x.CreateEntityProxy<IMessageTracker>(It.IsAny<EntityId>()))
                                        .Returns(() => _messageTracker.Object);
            _durableOrchestrationContext.Setup(x => x.CreateEntityProxy<IMessageEntity>(It.IsAny<EntityId>()))
                                        .Returns(() => _messageEntity.Object);
        }

        [TestMethod]
        public async Task RunMessageProcessorWithASingleMessageInTheQueue()
        {
            _messageId = Guid.NewGuid().ToString();
            _membershipHttpRequest = new MembershipHttpRequest
            {
                FilePath = "/file-path/file.json",
                SyncJob = new SyncJob
                {
                    Id = Guid.NewGuid(),
                    RunId = Guid.NewGuid()
                },
                ProjectedMemberCount = 1000
            };

            var messageProcessorOrchestrator = new MessageProcessorOrchestrator();
            await messageProcessorOrchestrator.RunMessageProcessorAsync(_durableOrchestrationContext.Object);

            _durableOrchestrationContext.Verify(x => x.CallSubOrchestratorAsync(
                                                                                nameof(OrchestratorFunction),
                                                                                It.IsAny<object>()
                                                                                ), Times.Once());
        }

        [TestMethod]
        public async Task RunMessageProcessorWithAnEmptyQueueAsync()
        {
            var message = $"{nameof(MessageProcessorOrchestrator)} has no messages to process";

            var messageProcessorOrchestrator = new MessageProcessorOrchestrator();
            await messageProcessorOrchestrator.RunMessageProcessorAsync(_durableOrchestrationContext.Object);

            _durableOrchestrationContext.Verify(x => x.CallSubOrchestratorAsync(
                                                                                nameof(OrchestratorFunction),
                                                                                It.IsAny<object>()
                                                                                ), Times.Never());

            _durableOrchestrationContext.Verify(x => x.CallActivityAsync(
                                                                        nameof(LoggerFunction),
                                                                        It.Is<LoggerRequest>(m => m.Message.StartsWith(message))
                                                                        ), Times.Once());

        }
    }
}
