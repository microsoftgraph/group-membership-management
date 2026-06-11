// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

using DIConcreteTypes;
using Hosts.MessageSplitter;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;

namespace Services.Tests
{
    [TestClass]
    public class OrchestratorFunctionTests
    {
        private SyncJob _syncJob;
        private Group _group;
        private MembershipUpdaters _membershipUpdaters;
        private OrchestratorRequest _orchestratorRequest;
        private Mock<TaskOrchestrationContext> _durableContext;


        [TestInitialize]
        public void SetupTest()
        {
            _durableContext = new Mock<TaskOrchestrationContext>();
            _membershipUpdaters = Helpers.GetAvailableMembershipUpdaters();

            _syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                ThresholdPercentageForAdditions = 80,
                ThresholdPercentageForRemovals = 20,
                LastRunTime = DateTime.UtcNow.AddDays(-1),
                Requestor = "user@domail.com",
                RunId = Guid.NewGuid(),
                ThresholdViolations = 0
            };

            _group = new Group
            {
                SyncJobId = _syncJob.Id,
                GroupId = Guid.NewGuid()
            };

            _orchestratorRequest = new OrchestratorRequest
            {
                CurrentLaneSize = "Small",
                MessageId = Guid.NewGuid().ToString(),
                SubscriptionName = "Small",
                UpdaterType = "GroupMembership",
                MembershipRequest = new MembershipHttpRequest
                {
                    SyncJob = _syncJob,
                    FilePath = "/test/path/file.json",
                    ProjectedMemberCount = 100,
                    MembersToBeAdded = 25,
                    MembersToBeRemoved = 15,
                    GroupId = _group.GroupId
                }
            };

            _durableContext.Setup(x => x.GetInput<OrchestratorRequest>())
                           .Returns(() => _orchestratorRequest);

            _durableContext.Setup(x => x.Entities.LockEntitiesAsync(It.IsAny<EntityInstanceId>()));

            _durableContext.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(NullLogger.Instance);
        }

        [TestMethod]
        public async Task RunOrchestratorAsync()
        {
            var orchestratorFunction = new OrchestratorFunction(_membershipUpdaters);
            await orchestratorFunction.RunOrchestratorAsync(_durableContext.Object);

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

            var orchestratorFunction = new OrchestratorFunction(_membershipUpdaters);
            await Assert.ThrowsExceptionAsync<Exception>(async () => await orchestratorFunction.RunOrchestratorAsync(_durableContext.Object));

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
