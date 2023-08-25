// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Core;
using Hosts.GraphUpdater;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Graph.Models;
using Microsoft.VisualStudio.TestPlatform.CommunicationUtilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class MessageOrchestratorTests
    {
        private MembershipHttpRequest _membershipHttpRequest;
        private DurableOrchestrationStatus _durableOrchestrationStatus;

        private Mock<IMessageEntity> _messageEntity;
        private Mock<IMessageTracker> _messageTracker;
        private Mock<IDurableOrchestrationContext> _durableOrchestrationContext;

        [TestInitialize]
        public void Setup()
        {
            _membershipHttpRequest = new MembershipHttpRequest
            {
                FilePath = "/file-path/file.json",
                SyncJob = new SyncJob
                {
                    Id = Guid.NewGuid(),
                },
                ProjectedMemberCount = 1000
            };

            _messageEntity = new Mock<IMessageEntity>();
            _messageTracker = new Mock<IMessageTracker>();

            _durableOrchestrationContext = new Mock<IDurableOrchestrationContext>();
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync(nameof(LoggerFunction), It.IsAny<LoggerRequest>()));
            _durableOrchestrationContext.Setup(x => x.GetInput<MembershipHttpRequest>()).Returns(() => _membershipHttpRequest);
            _durableOrchestrationContext.Setup(x => x.CreateEntityProxy<IMessageTracker>(It.IsAny<EntityId>()))
                                        .Returns(() => _messageTracker.Object);
            _durableOrchestrationContext.Setup(x => x.CreateEntityProxy<IMessageEntity>(It.IsAny<EntityId>()))
                                        .Returns(() => _messageEntity.Object);
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<DurableOrchestrationStatus>(nameof(StatusReaderFunction), It.IsAny<string>()))
                                        .ReturnsAsync(() => _durableOrchestrationStatus);

        }

        [TestMethod]
        public async Task RunMessageOrchestratorForInitialMessage()
        {
            var jobId = _membershipHttpRequest.SyncJob.Id;
            var messageOrchestrator = new MessageOrchestrator();
            var message = $"Calling {nameof(MessageProcessorOrchestrator)} for jobId {jobId}";

            await messageOrchestrator.RunMessageOrchestratorAsync(_durableOrchestrationContext.Object);

            _durableOrchestrationContext.Verify(x => x.CallActivityAsync(
                                                                        nameof(LoggerFunction),
                                                                        It.Is<LoggerRequest>(m => m.Message.Equals(message))
                                                                        ), Times.Once());

            _durableOrchestrationContext.Verify(x => x.StartNewOrchestration(
                                                                            nameof(MessageProcessorOrchestrator),
                                                                            null,
                                                                            nameof(MessageProcessorOrchestrator)
                                                                           ), Times.Once());
        }

        [TestMethod]
        public async Task RunMessageOrchestratorForSubsequentMessage()
        {
            var jobIds = new List<Guid>
            {
                Guid.NewGuid(),
                Guid.NewGuid()
            };

            var message = $"Calling {nameof(MessageProcessorOrchestrator)} for jobId";

            foreach (var jobId in jobIds)
            {
                _membershipHttpRequest.SyncJob.Id = jobId;
                var messageOrchestrator = new MessageOrchestrator();
                await messageOrchestrator.RunMessageOrchestratorAsync(_durableOrchestrationContext.Object);

                if (_durableOrchestrationStatus == null)
                {
                    _durableOrchestrationStatus = new DurableOrchestrationStatus
                    {
                        RuntimeStatus = OrchestrationRuntimeStatus.Running
                    };
                }
            }

            _durableOrchestrationContext.Verify(x => x.CallActivityAsync(
                                                                        nameof(LoggerFunction),
                                                                        It.Is<LoggerRequest>(m => m.Message.StartsWith(message))
                                                                        ), Times.Once());

            _durableOrchestrationContext.Verify(x => x.StartNewOrchestration(
                                                                            nameof(MessageProcessorOrchestrator),
                                                                            null,
                                                                            nameof(MessageProcessorOrchestrator)
                                                                            ), Times.Once());
        }
    }
}
