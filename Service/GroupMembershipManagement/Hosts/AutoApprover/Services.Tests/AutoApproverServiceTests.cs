// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.ServiceBus;
using Models.SyncJobChange;
using Moq;
using Repositories.Contracts;
using Services.AutoApprover;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class AutoApproverServiceTests
    {
        [TestMethod]
        public void TestServiceInstantiation()
        {
            var syncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            var settingsRepository = new Mock<IDatabaseSettingsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var syncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            var logger = new Mock<ILogger<AutoApproverService>>();

            var service = new AutoApproverService(
                syncJobsRepository.Object,
                settingsRepository.Object,
                graphGroupRepository.Object,
                syncJobChangeRepository.Object,
                logger.Object);
            Assert.IsNotNull(service);
        }

        [TestMethod]
        public async Task ProcessAutoApproval_GroupMembershipVisible_ApprovesJobAsync()
        {
            var syncJobId = Guid.NewGuid();
            var groupId1 = Guid.NewGuid();
            var groupId2 = Guid.NewGuid();
            var requestorId = Guid.NewGuid().ToString();

            var syncJob = new SyncJob
            {
                Id = syncJobId,
                Status = SyncStatus.PendingAutoApproval.ToString(),
                StartDate = DateTime.UtcNow.AddHours(-1),
                Query = $"[{{\"type\":\"GroupMembership\",\"source\":\"{groupId1}\"}},{{\"type\":\"GroupMembership\",\"source\":\"{groupId2}\"}}]"
            };

            var syncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            var settingsRepository = new Mock<IDatabaseSettingsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var syncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            var logger = new Mock<ILogger<AutoApproverService>>();

            syncJobsRepository.Setup(x => x.GetSyncJobAsync(syncJobId)).ReturnsAsync(syncJob);
            settingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled))
                              .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled, SettingValue = "true" });
            settingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled))
                              .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled, SettingValue = "false" });

            graphGroupRepository.Setup(x => x.GetGroupsAsync(It.IsAny<List<Guid>>()))
                                .ReturnsAsync(new List<AzureADGroup>
                                {
                                    new AzureADGroup { ObjectId = groupId1, Visibility = "Public" },
                                    new AzureADGroup { ObjectId = groupId2, Visibility = "Private" }
                                });

            var service = new AutoApproverService(
                syncJobsRepository.Object,
                settingsRepository.Object,
                graphGroupRepository.Object,
                syncJobChangeRepository.Object,
                logger.Object);

            await service.ProcessAutoApprovalAsync(new AutoApprovalQueueMessage
            {
                SyncJobId = syncJobId,
                RequestorObjectId = requestorId,
                RequestorDisplayName = "Requestor"
            });

            syncJobsRepository.Verify(x => x.UpdateSyncJobsAsync(It.Is<IEnumerable<SyncJob>>(jobs =>
                jobs.Count() == 1 && jobs.First().Status == SyncStatus.Idle.ToString()), null), Times.Once);

            syncJobChangeRepository.Verify(x => x.Save(It.Is<SyncJobChange>(change =>
                change.SyncJobId == syncJobId && change.ChangeReason == SyncJobChangeReason.OnboardingAutoApproved.ToString())), Times.Once);
        }

        [TestMethod]
        public async Task ProcessAutoApproval_GroupMembershipHidden_MovesToPendingReviewAsync()
        {
            var syncJobId = Guid.NewGuid();
            var groupId = Guid.NewGuid();

            var syncJob = new SyncJob
            {
                Id = syncJobId,
                Status = SyncStatus.PendingAutoApproval.ToString(),
                Query = $"[{{\"type\":\"GroupMembership\",\"source\":\"{groupId}\"}}]"
            };

            var syncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            var settingsRepository = new Mock<IDatabaseSettingsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var syncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            var logger = new Mock<ILogger<AutoApproverService>>();

            syncJobsRepository.Setup(x => x.GetSyncJobAsync(syncJobId)).ReturnsAsync(syncJob);
            settingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled))
                              .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled, SettingValue = "true" });

            graphGroupRepository.Setup(x => x.GetGroupsAsync(It.IsAny<List<Guid>>()))
                                .ReturnsAsync(new List<AzureADGroup>
                                {
                                    new AzureADGroup { ObjectId = groupId, Visibility = "HiddenMembership" }
                                });

            var service = new AutoApproverService(
                syncJobsRepository.Object,
                settingsRepository.Object,
                graphGroupRepository.Object,
                syncJobChangeRepository.Object,
                logger.Object);

            await service.ProcessAutoApprovalAsync(new AutoApprovalQueueMessage
            {
                SyncJobId = syncJobId,
                RequestorObjectId = Guid.NewGuid().ToString(),
                RequestorDisplayName = "Requestor"
            });

            // Not granted: the job is handed off to a human by moving it to PendingReview, but no
            // OnboardingAutoApproved audit record is written.
            syncJobsRepository.Verify(x => x.UpdateSyncJobsAsync(It.Is<IEnumerable<SyncJob>>(jobs =>
                jobs.Count() == 1 && jobs.First().Status == SyncStatus.PendingReview.ToString()), It.IsAny<SyncStatus?>()), Times.Once);
            syncJobChangeRepository.Verify(x => x.Save(It.IsAny<SyncJobChange>()), Times.Never);
        }

        [TestMethod]
        public async Task ProcessAutoApproval_OrgLeaderMatchingManager_ApprovesJobAsync()
        {
            var syncJobId = Guid.NewGuid();
            var requestorId = Guid.NewGuid().ToString();
            var managerId = 12345;

            var syncJob = new SyncJob
            {
                Id = syncJobId,
                Status = SyncStatus.PendingAutoApproval.ToString(),
                Query = $"[{{\"type\":\"SqlMembership\",\"source\":{{\"manager\":{{\"id\":{managerId}}}}}}}]"
            };

            var syncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            var settingsRepository = new Mock<IDatabaseSettingsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var syncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            var logger = new Mock<ILogger<AutoApproverService>>();

            syncJobsRepository.Setup(x => x.GetSyncJobAsync(syncJobId)).ReturnsAsync(syncJob);
            settingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled))
                              .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled, SettingValue = "false" });
            settingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled))
                              .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled, SettingValue = "true" });

            graphGroupRepository.Setup(x => x.GetUserWithOnPremisesImmutableIdAsync(requestorId, It.IsAny<Guid?>()))
                                .ReturnsAsync(new AzureADUser { OnPremisesImmutableId = managerId.ToString() });

            var service = new AutoApproverService(
                syncJobsRepository.Object,
                settingsRepository.Object,
                graphGroupRepository.Object,
                syncJobChangeRepository.Object,
                logger.Object);

            await service.ProcessAutoApprovalAsync(new AutoApprovalQueueMessage
            {
                SyncJobId = syncJobId,
                RequestorObjectId = requestorId,
                RequestorDisplayName = "Requestor"
            });

            syncJobsRepository.Verify(x => x.UpdateSyncJobsAsync(It.Is<IEnumerable<SyncJob>>(jobs =>
                jobs.Count() == 1 && jobs.First().Status == SyncStatus.Idle.ToString()), null), Times.Once);

            syncJobChangeRepository.Verify(x => x.Save(It.Is<SyncJobChange>(change =>
                change.SyncJobId == syncJobId && change.ChangeReason == SyncJobChangeReason.OnboardingAutoApproved.ToString())), Times.Once);
        }

        [TestMethod]
        public async Task ProcessAutoApproval_ChangeRecordSaveFails_RevertsStatusAndRethrowsAsync()
        {
            var syncJobId = Guid.NewGuid();
            var groupId1 = Guid.NewGuid();
            var groupId2 = Guid.NewGuid();
            var requestorId = Guid.NewGuid().ToString();

            var syncJob = new SyncJob
            {
                Id = syncJobId,
                Status = SyncStatus.PendingAutoApproval.ToString(),
                StartDate = DateTime.UtcNow.AddHours(-1),
                Query = $"[{{\"type\":\"GroupMembership\",\"source\":\"{groupId1}\"}},{{\"type\":\"GroupMembership\",\"source\":\"{groupId2}\"}}]"
            };

            var syncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            var settingsRepository = new Mock<IDatabaseSettingsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var syncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            var logger = new Mock<ILogger<AutoApproverService>>();

            syncJobsRepository.Setup(x => x.GetSyncJobAsync(syncJobId)).ReturnsAsync(syncJob);
            settingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled))
                              .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled, SettingValue = "true" });
            settingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled))
                              .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled, SettingValue = "false" });

            graphGroupRepository.Setup(x => x.GetGroupsAsync(It.IsAny<List<Guid>>()))
                                .ReturnsAsync(new List<AzureADGroup>
                                {
                                    new AzureADGroup { ObjectId = groupId1, Visibility = "Public" },
                                    new AzureADGroup { ObjectId = groupId2, Visibility = "Private" }
                                });

            // The same syncJob reference is mutated in place during the revert, so capture the status at
            // call time rather than relying on the object's final state during Verify.
            var observedStatuses = new List<string>();
            syncJobsRepository.Setup(x => x.UpdateSyncJobsAsync(It.IsAny<IEnumerable<SyncJob>>(), It.IsAny<SyncStatus?>()))
                              .Callback<IEnumerable<SyncJob>, SyncStatus?>((jobs, status) => observedStatuses.Add(jobs.First().Status))
                              .Returns(Task.CompletedTask);

            var saveException = new InvalidOperationException("Save failed");
            syncJobChangeRepository.Setup(x => x.Save(It.IsAny<SyncJobChange>())).ThrowsAsync(saveException);

            var service = new AutoApproverService(
                syncJobsRepository.Object,
                settingsRepository.Object,
                graphGroupRepository.Object,
                syncJobChangeRepository.Object,
                logger.Object);

            var thrown = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                service.ProcessAutoApprovalAsync(new AutoApprovalQueueMessage
                {
                    SyncJobId = syncJobId,
                    RequestorObjectId = requestorId,
                    RequestorDisplayName = "Requestor"
                }));

            Assert.AreSame(saveException, thrown);

            // Two persistence calls: the initial flip to Idle, then the compensating revert to PendingAutoApproval.
            syncJobsRepository.Verify(x => x.UpdateSyncJobsAsync(It.IsAny<IEnumerable<SyncJob>>(), It.IsAny<SyncStatus?>()), Times.Exactly(2));
            CollectionAssert.AreEqual(
                new[] { SyncStatus.Idle.ToString(), SyncStatus.PendingAutoApproval.ToString() },
                observedStatuses);

            // The job is left in PendingAutoApproval so the Service Bus retry re-runs the full approval path
            // (and writes the missing audit record) instead of short-circuiting on the status guard.
            Assert.AreEqual(SyncStatus.PendingAutoApproval.ToString(), syncJob.Status);
        }

        [TestMethod]
        public async Task ProcessAutoApproval_StatusNotPendingAutoApproval_SkipsAsync()
        {
            var syncJobId = Guid.NewGuid();
            var syncJob = new SyncJob
            {
                Id = syncJobId,
                Status = SyncStatus.Idle.ToString(),
                Query = "[]"
            };

            var syncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            var settingsRepository = new Mock<IDatabaseSettingsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var syncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            var logger = new Mock<ILogger<AutoApproverService>>();

            syncJobsRepository.Setup(x => x.GetSyncJobAsync(syncJobId)).ReturnsAsync(syncJob);

            var service = new AutoApproverService(
                syncJobsRepository.Object,
                settingsRepository.Object,
                graphGroupRepository.Object,
                syncJobChangeRepository.Object,
                logger.Object);

            await service.ProcessAutoApprovalAsync(new AutoApprovalQueueMessage
            {
                SyncJobId = syncJobId,
                RequestorObjectId = Guid.NewGuid().ToString(),
                RequestorDisplayName = "Requestor"
            });

            syncJobsRepository.Verify(x => x.UpdateSyncJobsAsync(It.IsAny<IEnumerable<SyncJob>>(), It.IsAny<SyncStatus?>()), Times.Never);
            syncJobChangeRepository.Verify(x => x.Save(It.IsAny<SyncJobChange>()), Times.Never);
        }

        [TestMethod]
        public async Task MoveJobToPendingReview_PendingAutoApproval_MovesToPendingReviewAsync()
        {
            var syncJobId = Guid.NewGuid();
            var syncJob = new SyncJob
            {
                Id = syncJobId,
                Status = SyncStatus.PendingAutoApproval.ToString(),
                Query = "[]"
            };

            var syncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            var settingsRepository = new Mock<IDatabaseSettingsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var syncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            var logger = new Mock<ILogger<AutoApproverService>>();

            syncJobsRepository.Setup(x => x.GetSyncJobAsync(syncJobId)).ReturnsAsync(syncJob);

            var service = new AutoApproverService(
                syncJobsRepository.Object,
                settingsRepository.Object,
                graphGroupRepository.Object,
                syncJobChangeRepository.Object,
                logger.Object);

            await service.MoveJobToPendingReviewAsync(syncJobId);

            syncJobsRepository.Verify(x => x.UpdateSyncJobsAsync(It.Is<IEnumerable<SyncJob>>(jobs =>
                jobs.Count() == 1 && jobs.First().Status == SyncStatus.PendingReview.ToString()), It.IsAny<SyncStatus?>()), Times.Once);
        }

        [TestMethod]
        public async Task MoveJobToPendingReview_AlreadyAdvanced_NoOpAsync()
        {
            var syncJobId = Guid.NewGuid();
            var syncJob = new SyncJob
            {
                Id = syncJobId,
                Status = SyncStatus.Idle.ToString(),
                Query = "[]"
            };

            var syncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            var settingsRepository = new Mock<IDatabaseSettingsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var syncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            var logger = new Mock<ILogger<AutoApproverService>>();

            syncJobsRepository.Setup(x => x.GetSyncJobAsync(syncJobId)).ReturnsAsync(syncJob);

            var service = new AutoApproverService(
                syncJobsRepository.Object,
                settingsRepository.Object,
                graphGroupRepository.Object,
                syncJobChangeRepository.Object,
                logger.Object);

            await service.MoveJobToPendingReviewAsync(syncJobId);

            syncJobsRepository.Verify(x => x.UpdateSyncJobsAsync(It.IsAny<IEnumerable<SyncJob>>(), It.IsAny<SyncStatus?>()), Times.Never);
        }

        [TestMethod]
        public async Task MoveJobToPendingReview_JobNotFound_NoOpAsync()
        {
            var syncJobId = Guid.NewGuid();

            var syncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            var settingsRepository = new Mock<IDatabaseSettingsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var syncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            var logger = new Mock<ILogger<AutoApproverService>>();

            syncJobsRepository.Setup(x => x.GetSyncJobAsync(syncJobId)).ReturnsAsync((SyncJob)null);

            var service = new AutoApproverService(
                syncJobsRepository.Object,
                settingsRepository.Object,
                graphGroupRepository.Object,
                syncJobChangeRepository.Object,
                logger.Object);

            await service.MoveJobToPendingReviewAsync(syncJobId);

            syncJobsRepository.Verify(x => x.UpdateSyncJobsAsync(It.IsAny<IEnumerable<SyncJob>>(), It.IsAny<SyncStatus?>()), Times.Never);
        }
    }
}
