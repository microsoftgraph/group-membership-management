// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.MessageSplitter;
using MessageSplitter.Entities;
using Microsoft.DurableTask;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using Repositories.Contracts;

namespace Services.Tests
{
    [TestClass]
    public class OrchestratorFunctionTests
    {
        private SyncJob _syncJob;
        private MembershipUpdaters _membershipUpdaters;
        private OrchestratorRequest _orchestratorRequest;
        private Mock<ILoggingRepository> _loggingRepository;
        private Mock<TaskOrchestrationContext> _durableContext;


        [TestInitialize]
        public void SetupTest()
        {
            _loggingRepository = new Mock<ILoggingRepository>();
            _durableContext = new Mock<TaskOrchestrationContext>();
            _membershipUpdaters = Helpers.GetAvailableMembershipUpdaters();

            _syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                TargetOfficeGroupId = Guid.NewGuid(),
                ThresholdPercentageForAdditions = 80,
                ThresholdPercentageForRemovals = 20,
                LastRunTime = DateTime.UtcNow.AddDays(-1),
                Requestor = "user@domail.com",
                RunId = Guid.NewGuid(),
                ThresholdViolations = 0
            };

            _orchestratorRequest = new OrchestratorRequest
            {
                CurrentLaneSize = "Small",
                InstanceToUse = 1,
                MessageId = Guid.NewGuid().ToString(),
                SubscriptionName = "Small",
                UpdaterType = "GroupMembership",
                MembershipRequest = new MembershipHttpRequest
                {
                    SyncJob = _syncJob
                }
            };

            _durableContext.Setup(x => x.GetInput<OrchestratorRequest>())
                           .Returns(() => _orchestratorRequest);
        }

        [TestMethod]
        public async Task RunOrchestratorAsync()
        {
            var orchestratorFunction = new OrchestratorFunction(_loggingRepository.Object, _membershipUpdaters);
            await orchestratorFunction.RunOrchestratorAsync(_durableContext.Object);

            _durableContext.Verify(x => x.CallActivityAsync(nameof(LoggerFunction),
                                                            It.Is<LoggerRequest>(r => r.Message.Message.StartsWith("Processing message")),
                                                            It.IsAny<TaskOptions>()),
                                                            Times.Once());

            _durableContext.Verify(x => x.CallActivityAsync(nameof(TopicMessageSenderFunction),
                                                It.IsAny<TopicMessageSenderRequest>(),
                                                It.IsAny<TaskOptions>()),
                                                Times.Once());

        }

        [TestMethod]
        public async Task RunOrchestratorWithFailuresAsync()
        {
            _durableContext.Setup(x => x.CallActivityAsync(nameof(TopicMessageSenderFunction),
                                                It.IsAny<TopicMessageSenderRequest>(),
                                                It.IsAny<TaskOptions>()))
                           .ThrowsAsync(new Exception("Test exception"));

            var orchestratorFunction = new OrchestratorFunction(_loggingRepository.Object, _membershipUpdaters);
            await Assert.ThrowsExceptionAsync<Exception>(async () => await orchestratorFunction.RunOrchestratorAsync(_durableContext.Object));

            _durableContext.Verify(x => x.CallActivityAsync(nameof(LoggerFunction),
                                                            It.Is<LoggerRequest>(r => r.Message.Message.StartsWith("Processing message")),
                                                            It.IsAny<TaskOptions>()),
                                                            Times.Once());

            _durableContext.Verify(x => x.CallActivityAsync(nameof(TopicMessageSenderFunction),
                                                It.IsAny<TopicMessageSenderRequest>(),
                                                It.IsAny<TaskOptions>()),
                                                Times.Once());

            _durableContext.Verify(x => x.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                                            It.Is<JobStatusUpdaterRequest>(r => r.Status == SyncStatus.Error),
                                                            It.IsAny<TaskOptions>()),
                                                            Times.Once());
        }
    }
}
