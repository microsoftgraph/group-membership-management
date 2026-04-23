// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

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
            var loggingRepository = new Mock<ILoggingRepository>();

            var service = new AutoApproverService(
                syncJobsRepository.Object,
                settingsRepository.Object,
                graphGroupRepository.Object,
                syncJobChangeRepository.Object,
                loggingRepository.Object);
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
                Status = SyncStatus.PendingReview.ToString(),
                StartDate = DateTime.UtcNow.AddHours(-1),
                Query = $"[{{\"type\":\"GroupMembership\",\"source\":\"{groupId1}\"}},{{\"type\":\"GroupMembership\",\"source\":\"{groupId2}\"}}]"
            };

            var syncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            var settingsRepository = new Mock<IDatabaseSettingsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var syncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            var loggingRepository = new Mock<ILoggingRepository>();

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
                loggingRepository.Object);

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
        public async Task ProcessAutoApproval_GroupMembershipHidden_DoesNotApproveAsync()
        {
            var syncJobId = Guid.NewGuid();
            var groupId = Guid.NewGuid();

            var syncJob = new SyncJob
            {
                Id = syncJobId,
                Status = SyncStatus.PendingReview.ToString(),
                Query = $"[{{\"type\":\"GroupMembership\",\"source\":\"{groupId}\"}}]"
            };

            var syncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            var settingsRepository = new Mock<IDatabaseSettingsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var syncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            var loggingRepository = new Mock<ILoggingRepository>();

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
                loggingRepository.Object);

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
        public async Task ProcessAutoApproval_OrgLeaderMatchingManager_ApprovesJobAsync()
        {
            var syncJobId = Guid.NewGuid();
            var requestorId = Guid.NewGuid().ToString();
            var managerId = 12345;

            var syncJob = new SyncJob
            {
                Id = syncJobId,
                Status = SyncStatus.PendingReview.ToString(),
                Query = $"[{{\"type\":\"SqlMembership\",\"source\":{{\"manager\":{{\"id\":{managerId}}}}}}}]"
            };

            var syncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            var settingsRepository = new Mock<IDatabaseSettingsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var syncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            var loggingRepository = new Mock<ILoggingRepository>();

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
                loggingRepository.Object);

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
        public async Task ProcessAutoApproval_StatusNotPendingReview_SkipsAsync()
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
            var loggingRepository = new Mock<ILoggingRepository>();

            syncJobsRepository.Setup(x => x.GetSyncJobAsync(syncJobId)).ReturnsAsync(syncJob);

            var service = new AutoApproverService(
                syncJobsRepository.Object,
                settingsRepository.Object,
                graphGroupRepository.Object,
                syncJobChangeRepository.Object,
                loggingRepository.Object);

            await service.ProcessAutoApprovalAsync(new AutoApprovalQueueMessage
            {
                SyncJobId = syncJobId,
                RequestorObjectId = Guid.NewGuid().ToString(),
                RequestorDisplayName = "Requestor"
            });

            syncJobsRepository.Verify(x => x.UpdateSyncJobsAsync(It.IsAny<IEnumerable<SyncJob>>(), It.IsAny<SyncStatus?>()), Times.Never);
            syncJobChangeRepository.Verify(x => x.Save(It.IsAny<SyncJobChange>()), Times.Never);
        }
    }
}
