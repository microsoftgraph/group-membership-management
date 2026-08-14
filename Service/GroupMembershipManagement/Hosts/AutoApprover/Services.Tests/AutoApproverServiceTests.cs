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
        private static AutoApproverService CreateService(
            Mock<IDatabaseSyncJobsRepository> syncJobsRepository,
            Mock<IDatabaseSettingsRepository> settingsRepository,
            Mock<IGraphGroupRepository> graphGroupRepository,
            Mock<ISyncJobChangeRepository> syncJobChangeRepository,
            Mock<ILogger<AutoApproverService>> logger,
            Mock<ISqlMembershipRepository> sqlMembershipRepository = null,
            Mock<IDataFactoryRepository> dataFactoryRepository = null)
        {
            return new AutoApproverService(
                syncJobsRepository.Object,
                settingsRepository.Object,
                graphGroupRepository.Object,
                syncJobChangeRepository.Object,
                (sqlMembershipRepository ?? new Mock<ISqlMembershipRepository>()).Object,
                (dataFactoryRepository ?? new Mock<IDataFactoryRepository>()).Object,
                logger.Object);
        }

        private static void EnablePerPartOnly(Mock<IDatabaseSettingsRepository> settingsRepository)
        {
            settingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsPerPartAutoApprovalEnabled))
                              .ReturnsAsync(new Setting { SettingKey = SettingKey.IsPerPartAutoApprovalEnabled, SettingValue = "true" });
        }

        private static AutoApprovalQueueMessage Message(Guid syncJobId, string requestorId) =>
            new AutoApprovalQueueMessage { SyncJobId = syncJobId, RequestorObjectId = requestorId, RequestorDisplayName = "Requestor" };

        private static void VerifyApproved(Mock<IDatabaseSyncJobsRepository> jobs, Mock<ISyncJobChangeRepository> changes, Guid syncJobId)
        {
            jobs.Verify(x => x.UpdateSyncJobsAsync(It.Is<IEnumerable<SyncJob>>(j => j.First().Status == SyncStatus.Idle.ToString()), null), Times.Once);
            changes.Verify(x => x.Save(It.Is<SyncJobChange>(c => c.SyncJobId == syncJobId && c.ChangeReason == SyncJobChangeReason.OnboardingAutoApproved.ToString())), Times.Once);
        }

        private static void VerifyPendingReview(Mock<IDatabaseSyncJobsRepository> jobs, Mock<ISyncJobChangeRepository> changes)
        {
            jobs.Verify(x => x.UpdateSyncJobsAsync(It.Is<IEnumerable<SyncJob>>(j => j.First().Status == SyncStatus.PendingReview.ToString()), It.IsAny<SyncStatus?>()), Times.Once);
            changes.Verify(x => x.Save(It.IsAny<SyncJobChange>()), Times.Never);
        }

        [TestMethod]
        public void TestServiceInstantiation()
        {
            var syncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            var settingsRepository = new Mock<IDatabaseSettingsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var syncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            var logger = new Mock<ILogger<AutoApproverService>>();

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger);
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

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger);

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

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger);

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

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger);

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

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger);

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

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger);

            await service.ProcessAutoApprovalAsync(new AutoApprovalQueueMessage
            {
                SyncJobId = syncJobId,
                RequestorObjectId = Guid.NewGuid().ToString(),
                RequestorDisplayName = "Requestor"
            });

            syncJobsRepository.Verify(x => x.UpdateSyncJobsAsync(It.IsAny<IEnumerable<SyncJob>>(), It.IsAny<SyncStatus?>()), Times.Never);
            syncJobChangeRepository.Verify(x => x.Save(It.IsAny<SyncJobChange>()), Times.Never);
        }

        // ── Per-Part Rule ──

        [TestMethod]
        public async Task PerPart_GroupPublic_ApprovesAsync()
        {
            var syncJobId = Guid.NewGuid();
            var groupId = Guid.NewGuid();
            var requestorId = Guid.NewGuid().ToString();

            var syncJob = new SyncJob { Id = syncJobId, Status = SyncStatus.PendingAutoApproval.ToString(), Query = $"[{{\"type\":\"GroupMembership\",\"source\":\"{groupId}\"}}]" };

            var syncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            var settingsRepository = new Mock<IDatabaseSettingsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var syncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            var logger = new Mock<ILogger<AutoApproverService>>();

            syncJobsRepository.Setup(x => x.GetSyncJobAsync(syncJobId)).ReturnsAsync(syncJob);
            EnablePerPartOnly(settingsRepository);
            graphGroupRepository.Setup(x => x.GetGroupsAsync(It.IsAny<List<Guid>>()))
                                .ReturnsAsync(new List<AzureADGroup> { new AzureADGroup { ObjectId = groupId, Visibility = "Public" } });

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger);
            await service.ProcessAutoApprovalAsync(Message(syncJobId, requestorId));

            VerifyApproved(syncJobsRepository, syncJobChangeRepository, syncJobId);
            graphGroupRepository.Verify(x => x.GetGroupOwnersAsync(It.IsAny<Guid>(), It.IsAny<int>()), Times.Never);
        }

        [TestMethod]
        public async Task PerPart_GroupPrivateRequestorIsOwner_ApprovesAsync()
        {
            var syncJobId = Guid.NewGuid();
            var groupId = Guid.NewGuid();
            var requestorId = Guid.NewGuid().ToString();

            var syncJob = new SyncJob { Id = syncJobId, Status = SyncStatus.PendingAutoApproval.ToString(), Query = $"[{{\"type\":\"GroupMembership\",\"source\":\"{groupId}\"}}]" };

            var syncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            var settingsRepository = new Mock<IDatabaseSettingsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var syncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            var logger = new Mock<ILogger<AutoApproverService>>();

            syncJobsRepository.Setup(x => x.GetSyncJobAsync(syncJobId)).ReturnsAsync(syncJob);
            EnablePerPartOnly(settingsRepository);
            graphGroupRepository.Setup(x => x.GetGroupsAsync(It.IsAny<List<Guid>>()))
                                .ReturnsAsync(new List<AzureADGroup> { new AzureADGroup { ObjectId = groupId, Visibility = "Private" } });
            graphGroupRepository.Setup(x => x.GetGroupOwnersAsync(groupId, It.IsAny<int>()))
                                .ReturnsAsync(new List<AzureADUser> { new AzureADUser { ObjectId = Guid.Parse(requestorId) } });

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger);
            await service.ProcessAutoApprovalAsync(Message(syncJobId, requestorId));

            VerifyApproved(syncJobsRepository, syncJobChangeRepository, syncJobId);
        }

        [TestMethod]
        public async Task PerPart_GroupPrivateRequestorNotOwner_MovesToPendingReviewAsync()
        {
            var syncJobId = Guid.NewGuid();
            var groupId = Guid.NewGuid();
            var requestorId = Guid.NewGuid().ToString();

            var syncJob = new SyncJob { Id = syncJobId, Status = SyncStatus.PendingAutoApproval.ToString(), Query = $"[{{\"type\":\"GroupMembership\",\"source\":\"{groupId}\"}}]" };

            var syncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            var settingsRepository = new Mock<IDatabaseSettingsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var syncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            var logger = new Mock<ILogger<AutoApproverService>>();

            syncJobsRepository.Setup(x => x.GetSyncJobAsync(syncJobId)).ReturnsAsync(syncJob);
            EnablePerPartOnly(settingsRepository);
            graphGroupRepository.Setup(x => x.GetGroupsAsync(It.IsAny<List<Guid>>()))
                                .ReturnsAsync(new List<AzureADGroup> { new AzureADGroup { ObjectId = groupId, Visibility = "Private" } });
            graphGroupRepository.Setup(x => x.GetGroupOwnersAsync(groupId, It.IsAny<int>()))
                                .ReturnsAsync(new List<AzureADUser> { new AzureADUser { ObjectId = Guid.NewGuid() } });

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger);
            await service.ProcessAutoApprovalAsync(Message(syncJobId, requestorId));

            VerifyPendingReview(syncJobsRepository, syncJobChangeRepository);
        }

        [TestMethod]
        public async Task PerPart_GroupNotFound_MovesToPendingReviewAsync()
        {
            var syncJobId = Guid.NewGuid();
            var groupId = Guid.NewGuid();

            var syncJob = new SyncJob { Id = syncJobId, Status = SyncStatus.PendingAutoApproval.ToString(), Query = $"[{{\"type\":\"GroupMembership\",\"source\":\"{groupId}\"}}]" };

            var syncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            var settingsRepository = new Mock<IDatabaseSettingsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var syncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            var logger = new Mock<ILogger<AutoApproverService>>();

            syncJobsRepository.Setup(x => x.GetSyncJobAsync(syncJobId)).ReturnsAsync(syncJob);
            EnablePerPartOnly(settingsRepository);
            graphGroupRepository.Setup(x => x.GetGroupsAsync(It.IsAny<List<Guid>>())).ReturnsAsync(new List<AzureADGroup>());

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger);
            await service.ProcessAutoApprovalAsync(Message(syncJobId, Guid.NewGuid().ToString()));

            VerifyPendingReview(syncJobsRepository, syncJobChangeRepository);
        }

        [TestMethod]
        public async Task PerPart_GroupVisibilityError_MovesToPendingReviewAsync()
        {
            var syncJobId = Guid.NewGuid();
            var groupId = Guid.NewGuid();

            var syncJob = new SyncJob { Id = syncJobId, Status = SyncStatus.PendingAutoApproval.ToString(), Query = $"[{{\"type\":\"GroupMembership\",\"source\":\"{groupId}\"}}]" };

            var syncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            var settingsRepository = new Mock<IDatabaseSettingsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var syncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            var logger = new Mock<ILogger<AutoApproverService>>();

            syncJobsRepository.Setup(x => x.GetSyncJobAsync(syncJobId)).ReturnsAsync(syncJob);
            EnablePerPartOnly(settingsRepository);
            graphGroupRepository.Setup(x => x.GetGroupsAsync(It.IsAny<List<Guid>>())).ThrowsAsync(new Exception("graph down"));

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger);
            await service.ProcessAutoApprovalAsync(Message(syncJobId, Guid.NewGuid().ToString()));

            VerifyPendingReview(syncJobsRepository, syncJobChangeRepository);
        }

        [TestMethod]
        public async Task PerPart_GroupOwnersError_MovesToPendingReviewAsync()
        {
            var syncJobId = Guid.NewGuid();
            var groupId = Guid.NewGuid();

            var syncJob = new SyncJob { Id = syncJobId, Status = SyncStatus.PendingAutoApproval.ToString(), Query = $"[{{\"type\":\"GroupMembership\",\"source\":\"{groupId}\"}}]" };

            var syncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            var settingsRepository = new Mock<IDatabaseSettingsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var syncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            var logger = new Mock<ILogger<AutoApproverService>>();

            syncJobsRepository.Setup(x => x.GetSyncJobAsync(syncJobId)).ReturnsAsync(syncJob);
            EnablePerPartOnly(settingsRepository);
            graphGroupRepository.Setup(x => x.GetGroupsAsync(It.IsAny<List<Guid>>()))
                                .ReturnsAsync(new List<AzureADGroup> { new AzureADGroup { ObjectId = groupId, Visibility = "Private" } });
            graphGroupRepository.Setup(x => x.GetGroupOwnersAsync(groupId, It.IsAny<int>())).ThrowsAsync(new Exception("owners down"));

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger);
            await service.ProcessAutoApprovalAsync(Message(syncJobId, Guid.NewGuid().ToString()));

            VerifyPendingReview(syncJobsRepository, syncJobChangeRepository);
        }

        [TestMethod]
        public async Task PerPart_SqlManagerSelfNoFilter_ApprovesAsync()
        {
            var syncJobId = Guid.NewGuid();
            var requestorId = Guid.NewGuid().ToString();
            var managerId = 4242;

            var syncJob = new SyncJob { Id = syncJobId, Status = SyncStatus.PendingAutoApproval.ToString(), Query = $"[{{\"type\":\"SqlMembership\",\"source\":{{\"manager\":{{\"id\":{managerId}}}}}}}]" };

            var syncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            var settingsRepository = new Mock<IDatabaseSettingsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var syncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            var logger = new Mock<ILogger<AutoApproverService>>();
            var sqlMembershipRepository = new Mock<ISqlMembershipRepository>();
            var dataFactoryRepository = new Mock<IDataFactoryRepository>();

            syncJobsRepository.Setup(x => x.GetSyncJobAsync(syncJobId)).ReturnsAsync(syncJob);
            EnablePerPartOnly(settingsRepository);
            dataFactoryRepository.Setup(x => x.GetMostRecentSucceededRunIdAsync()).ReturnsAsync("abc-def");
            sqlMembershipRepository.Setup(x => x.GetUserEmployeeIdAsync(requestorId, It.IsAny<string>())).ReturnsAsync(managerId);

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger, sqlMembershipRepository, dataFactoryRepository);
            await service.ProcessAutoApprovalAsync(Message(syncJobId, requestorId));

            VerifyApproved(syncJobsRepository, syncJobChangeRepository, syncJobId);
        }

        [TestMethod]
        public async Task PerPart_SqlManagerSelfWithFilter_ApprovesAsync()
        {
            var syncJobId = Guid.NewGuid();
            var requestorId = Guid.NewGuid().ToString();
            var managerId = 4242;

            // A source filter is present but the per-part rule does not consider filters, so a
            // manager-self SQL part still approves regardless of what the filter references.
            var syncJob = new SyncJob { Id = syncJobId, Status = SyncStatus.PendingAutoApproval.ToString(), Query = $"[{{\"type\":\"SqlMembership\",\"source\":{{\"manager\":{{\"id\":{managerId}}},\"filter\":\"Department eq 'Sales'\"}}}}]" };

            var syncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            var settingsRepository = new Mock<IDatabaseSettingsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var syncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            var logger = new Mock<ILogger<AutoApproverService>>();
            var sqlMembershipRepository = new Mock<ISqlMembershipRepository>();
            var dataFactoryRepository = new Mock<IDataFactoryRepository>();

            syncJobsRepository.Setup(x => x.GetSyncJobAsync(syncJobId)).ReturnsAsync(syncJob);
            EnablePerPartOnly(settingsRepository);
            dataFactoryRepository.Setup(x => x.GetMostRecentSucceededRunIdAsync()).ReturnsAsync("abc-def");
            sqlMembershipRepository.Setup(x => x.GetUserEmployeeIdAsync(requestorId, It.IsAny<string>())).ReturnsAsync(managerId);

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger, sqlMembershipRepository, dataFactoryRepository);
            await service.ProcessAutoApprovalAsync(Message(syncJobId, requestorId));

            VerifyApproved(syncJobsRepository, syncJobChangeRepository, syncJobId);
        }

        [TestMethod]
        public async Task PerPart_SqlFilterOnlyNoManager_MovesToPendingReviewAsync()
        {
            var syncJobId = Guid.NewGuid();
            var requestorId = Guid.NewGuid().ToString();

            // A SQL part with a filter but no manager cannot be manager-self-approved, so the
            // per-part parser rejects the query (fail-closed) and the job stays in review.
            var syncJob = new SyncJob { Id = syncJobId, Status = SyncStatus.PendingAutoApproval.ToString(), Query = "[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Department eq 'Sales'\"}}]" };

            var syncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            var settingsRepository = new Mock<IDatabaseSettingsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var syncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            var logger = new Mock<ILogger<AutoApproverService>>();
            var sqlMembershipRepository = new Mock<ISqlMembershipRepository>();
            var dataFactoryRepository = new Mock<IDataFactoryRepository>();

            syncJobsRepository.Setup(x => x.GetSyncJobAsync(syncJobId)).ReturnsAsync(syncJob);
            EnablePerPartOnly(settingsRepository);
            dataFactoryRepository.Setup(x => x.GetMostRecentSucceededRunIdAsync()).ReturnsAsync("abc-def");
            sqlMembershipRepository.Setup(x => x.GetUserEmployeeIdAsync(requestorId, It.IsAny<string>())).ReturnsAsync(4242);

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger, sqlMembershipRepository, dataFactoryRepository);
            await service.ProcessAutoApprovalAsync(Message(syncJobId, requestorId));

            VerifyPendingReview(syncJobsRepository, syncJobChangeRepository);
        }

        [TestMethod]
        public async Task PerPart_SqlIdentityMismatch_MovesToPendingReviewAsync()
        {
            var syncJobId = Guid.NewGuid();
            var requestorId = Guid.NewGuid().ToString();

            var syncJob = new SyncJob { Id = syncJobId, Status = SyncStatus.PendingAutoApproval.ToString(), Query = "[{\"type\":\"SqlMembership\",\"source\":{\"manager\":{\"id\":4242}}}]" };

            var syncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            var settingsRepository = new Mock<IDatabaseSettingsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var syncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            var logger = new Mock<ILogger<AutoApproverService>>();
            var sqlMembershipRepository = new Mock<ISqlMembershipRepository>();
            var dataFactoryRepository = new Mock<IDataFactoryRepository>();

            syncJobsRepository.Setup(x => x.GetSyncJobAsync(syncJobId)).ReturnsAsync(syncJob);
            EnablePerPartOnly(settingsRepository);
            dataFactoryRepository.Setup(x => x.GetMostRecentSucceededRunIdAsync()).ReturnsAsync("abc-def");
            sqlMembershipRepository.Setup(x => x.GetUserEmployeeIdAsync(requestorId, It.IsAny<string>())).ReturnsAsync(999);

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger, sqlMembershipRepository, dataFactoryRepository);
            await service.ProcessAutoApprovalAsync(Message(syncJobId, requestorId));

            VerifyPendingReview(syncJobsRepository, syncJobChangeRepository);
        }

        [TestMethod]
        public async Task PerPart_SqlEmployeeIdMissing_MovesToPendingReviewAsync()
        {
            var syncJobId = Guid.NewGuid();
            var requestorId = Guid.NewGuid().ToString();

            var syncJob = new SyncJob { Id = syncJobId, Status = SyncStatus.PendingAutoApproval.ToString(), Query = "[{\"type\":\"SqlMembership\",\"source\":{\"manager\":{\"id\":4242}}}]" };

            var syncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            var settingsRepository = new Mock<IDatabaseSettingsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var syncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            var logger = new Mock<ILogger<AutoApproverService>>();
            var sqlMembershipRepository = new Mock<ISqlMembershipRepository>();
            var dataFactoryRepository = new Mock<IDataFactoryRepository>();

            syncJobsRepository.Setup(x => x.GetSyncJobAsync(syncJobId)).ReturnsAsync(syncJob);
            EnablePerPartOnly(settingsRepository);
            dataFactoryRepository.Setup(x => x.GetMostRecentSucceededRunIdAsync()).ReturnsAsync("abc-def");
            sqlMembershipRepository.Setup(x => x.GetUserEmployeeIdAsync(requestorId, It.IsAny<string>())).ReturnsAsync((int?)null);

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger, sqlMembershipRepository, dataFactoryRepository);
            await service.ProcessAutoApprovalAsync(Message(syncJobId, requestorId));

            VerifyPendingReview(syncJobsRepository, syncJobChangeRepository);
        }

        [TestMethod]
        public async Task PerPart_SqlRunIdError_MovesToPendingReviewAsync()
        {
            var syncJobId = Guid.NewGuid();
            var requestorId = Guid.NewGuid().ToString();

            var syncJob = new SyncJob { Id = syncJobId, Status = SyncStatus.PendingAutoApproval.ToString(), Query = "[{\"type\":\"SqlMembership\",\"source\":{\"manager\":{\"id\":4242}}}]" };

            var syncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            var settingsRepository = new Mock<IDatabaseSettingsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var syncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            var logger = new Mock<ILogger<AutoApproverService>>();
            var sqlMembershipRepository = new Mock<ISqlMembershipRepository>();
            var dataFactoryRepository = new Mock<IDataFactoryRepository>();

            syncJobsRepository.Setup(x => x.GetSyncJobAsync(syncJobId)).ReturnsAsync(syncJob);
            EnablePerPartOnly(settingsRepository);
            dataFactoryRepository.Setup(x => x.GetMostRecentSucceededRunIdAsync()).ThrowsAsync(new Exception("adf down"));

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger, sqlMembershipRepository, dataFactoryRepository);
            await service.ProcessAutoApprovalAsync(Message(syncJobId, requestorId));

            VerifyPendingReview(syncJobsRepository, syncJobChangeRepository);
            sqlMembershipRepository.Verify(x => x.GetUserEmployeeIdAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [TestMethod]
        public async Task PerPart_SqlUserLookupThrows_MovesToPendingReviewAsync()
        {
            var syncJobId = Guid.NewGuid();
            var requestorId = Guid.NewGuid().ToString();

            var syncJob = new SyncJob { Id = syncJobId, Status = SyncStatus.PendingAutoApproval.ToString(), Query = "[{\"type\":\"SqlMembership\",\"source\":{\"manager\":{\"id\":4242}}}]" };

            var syncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            var settingsRepository = new Mock<IDatabaseSettingsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var syncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            var logger = new Mock<ILogger<AutoApproverService>>();
            var sqlMembershipRepository = new Mock<ISqlMembershipRepository>();
            var dataFactoryRepository = new Mock<IDataFactoryRepository>();

            syncJobsRepository.Setup(x => x.GetSyncJobAsync(syncJobId)).ReturnsAsync(syncJob);
            EnablePerPartOnly(settingsRepository);
            dataFactoryRepository.Setup(x => x.GetMostRecentSucceededRunIdAsync()).ReturnsAsync("abc-def");
            sqlMembershipRepository.Setup(x => x.GetUserEmployeeIdAsync(requestorId, It.IsAny<string>())).ThrowsAsync(new Exception("sql down"));

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger, sqlMembershipRepository, dataFactoryRepository);
            await service.ProcessAutoApprovalAsync(Message(syncJobId, requestorId));

            VerifyPendingReview(syncJobsRepository, syncJobChangeRepository);
        }

        [TestMethod]
        public async Task PerPart_MixedGroupPublicAndSqlManagerSelf_ApprovesAsync()
        {
            var syncJobId = Guid.NewGuid();
            var groupId = Guid.NewGuid();
            var requestorId = Guid.NewGuid().ToString();
            var managerId = 4242;

            var syncJob = new SyncJob { Id = syncJobId, Status = SyncStatus.PendingAutoApproval.ToString(), Query = $"[{{\"type\":\"GroupMembership\",\"source\":\"{groupId}\"}},{{\"type\":\"SqlMembership\",\"source\":{{\"manager\":{{\"id\":{managerId}}}}}}}]" };

            var syncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            var settingsRepository = new Mock<IDatabaseSettingsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var syncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            var logger = new Mock<ILogger<AutoApproverService>>();
            var sqlMembershipRepository = new Mock<ISqlMembershipRepository>();
            var dataFactoryRepository = new Mock<IDataFactoryRepository>();

            syncJobsRepository.Setup(x => x.GetSyncJobAsync(syncJobId)).ReturnsAsync(syncJob);
            EnablePerPartOnly(settingsRepository);
            graphGroupRepository.Setup(x => x.GetGroupsAsync(It.IsAny<List<Guid>>()))
                                .ReturnsAsync(new List<AzureADGroup> { new AzureADGroup { ObjectId = groupId, Visibility = "Public" } });
            dataFactoryRepository.Setup(x => x.GetMostRecentSucceededRunIdAsync()).ReturnsAsync("abc-def");
            sqlMembershipRepository.Setup(x => x.GetUserEmployeeIdAsync(requestorId, It.IsAny<string>())).ReturnsAsync(managerId);

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger, sqlMembershipRepository, dataFactoryRepository);
            await service.ProcessAutoApprovalAsync(Message(syncJobId, requestorId));

            VerifyApproved(syncJobsRepository, syncJobChangeRepository, syncJobId);
        }

        [TestMethod]
        public async Task PerPart_AllMustPass_OneGroupFails_MovesToPendingReviewAsync()
        {
            var syncJobId = Guid.NewGuid();
            var publicGroupId = Guid.NewGuid();
            var privateGroupId = Guid.NewGuid();
            var requestorId = Guid.NewGuid().ToString();

            var syncJob = new SyncJob { Id = syncJobId, Status = SyncStatus.PendingAutoApproval.ToString(), Query = $"[{{\"type\":\"GroupMembership\",\"source\":\"{publicGroupId}\"}},{{\"type\":\"GroupMembership\",\"source\":\"{privateGroupId}\"}}]" };

            var syncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            var settingsRepository = new Mock<IDatabaseSettingsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var syncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            var logger = new Mock<ILogger<AutoApproverService>>();

            syncJobsRepository.Setup(x => x.GetSyncJobAsync(syncJobId)).ReturnsAsync(syncJob);
            EnablePerPartOnly(settingsRepository);
            graphGroupRepository.Setup(x => x.GetGroupsAsync(It.Is<List<Guid>>(l => l.Contains(publicGroupId))))
                                .ReturnsAsync(new List<AzureADGroup> { new AzureADGroup { ObjectId = publicGroupId, Visibility = "Public" } });
            graphGroupRepository.Setup(x => x.GetGroupsAsync(It.Is<List<Guid>>(l => l.Contains(privateGroupId))))
                                .ReturnsAsync(new List<AzureADGroup> { new AzureADGroup { ObjectId = privateGroupId, Visibility = "Private" } });
            graphGroupRepository.Setup(x => x.GetGroupOwnersAsync(privateGroupId, It.IsAny<int>()))
                                .ReturnsAsync(new List<AzureADUser> { new AzureADUser { ObjectId = Guid.NewGuid() } });

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger);
            await service.ProcessAutoApprovalAsync(Message(syncJobId, requestorId));

            VerifyPendingReview(syncJobsRepository, syncJobChangeRepository);
        }

        [TestMethod]
        public async Task PerPart_KillSwitchDisabled_DoesNotEvaluateAndMovesToPendingReviewAsync()
        {
            var syncJobId = Guid.NewGuid();
            var groupId = Guid.NewGuid();

            var syncJob = new SyncJob { Id = syncJobId, Status = SyncStatus.PendingAutoApproval.ToString(), Query = $"[{{\"type\":\"GroupMembership\",\"source\":\"{groupId}\"}}]" };

            var syncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            var settingsRepository = new Mock<IDatabaseSettingsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var syncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            var logger = new Mock<ILogger<AutoApproverService>>();

            syncJobsRepository.Setup(x => x.GetSyncJobAsync(syncJobId)).ReturnsAsync(syncJob);
            settingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsPerPartAutoApprovalEnabled))
                              .ReturnsAsync(new Setting { SettingKey = SettingKey.IsPerPartAutoApprovalEnabled, SettingValue = "false" });

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger);
            await service.ProcessAutoApprovalAsync(Message(syncJobId, Guid.NewGuid().ToString()));

            VerifyPendingReview(syncJobsRepository, syncJobChangeRepository);
            graphGroupRepository.Verify(x => x.GetGroupsAsync(It.IsAny<List<Guid>>()), Times.Never);
        }

        [TestMethod]
        public async Task PerPart_UnparseableQuery_MovesToPendingReviewAsync()
        {
            var syncJobId = Guid.NewGuid();

            var syncJob = new SyncJob { Id = syncJobId, Status = SyncStatus.PendingAutoApproval.ToString(), Query = "not-json" };

            var syncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            var settingsRepository = new Mock<IDatabaseSettingsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var syncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            var logger = new Mock<ILogger<AutoApproverService>>();

            syncJobsRepository.Setup(x => x.GetSyncJobAsync(syncJobId)).ReturnsAsync(syncJob);
            EnablePerPartOnly(settingsRepository);

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger);
            await service.ProcessAutoApprovalAsync(Message(syncJobId, Guid.NewGuid().ToString()));

            VerifyPendingReview(syncJobsRepository, syncJobChangeRepository);
        }

        // ── Per-Part Rule precedence ──
        // The Per-Part Rule is authoritative: when enabled it is the only rule consulted, so its
        // verdict cannot be widened by the group-based or org-leader rules being enabled too.

        private static void EnableAllRules(Mock<IDatabaseSettingsRepository> settingsRepository)
        {
            settingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled))
                              .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled, SettingValue = "true" });
            settingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled))
                              .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled, SettingValue = "true" });
            settingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsPerPartAutoApprovalEnabled))
                              .ReturnsAsync(new Setting { SettingKey = SettingKey.IsPerPartAutoApprovalEnabled, SettingValue = "true" });
        }

        [TestMethod]
        public async Task PerPart_AllRulesEnabled_PerPartDeclines_OtherRulesSkippedAsync()
        {
            var syncJobId = Guid.NewGuid();
            var groupId1 = Guid.NewGuid();
            var groupId2 = Guid.NewGuid();
            var requestorId = Guid.NewGuid().ToString();

            // Two Private groups the requestor owns neither of. The group-based rule would approve
            // this (no HiddenMembership source), so an approval here would prove the rules were OR-ed.
            var syncJob = new SyncJob
            {
                Id = syncJobId,
                Status = SyncStatus.PendingAutoApproval.ToString(),
                Query = $"[{{\"type\":\"GroupMembership\",\"source\":\"{groupId1}\"}},{{\"type\":\"GroupMembership\",\"source\":\"{groupId2}\"}}]"
            };

            var syncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            var settingsRepository = new Mock<IDatabaseSettingsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var syncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            var logger = new Mock<ILogger<AutoApproverService>>();

            syncJobsRepository.Setup(x => x.GetSyncJobAsync(syncJobId)).ReturnsAsync(syncJob);
            EnableAllRules(settingsRepository);
            graphGroupRepository.Setup(x => x.GetGroupsAsync(It.IsAny<List<Guid>>()))
                                .ReturnsAsync((List<Guid> ids) => ids.Select(id => new AzureADGroup { ObjectId = id, Visibility = "Private" }).ToList());
            graphGroupRepository.Setup(x => x.GetGroupOwnersAsync(It.IsAny<Guid>(), It.IsAny<int>()))
                                .ReturnsAsync(new List<AzureADUser> { new AzureADUser { ObjectId = Guid.NewGuid() } });

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger);
            await service.ProcessAutoApprovalAsync(Message(syncJobId, requestorId));

            VerifyPendingReview(syncJobsRepository, syncJobChangeRepository);

            // Per-part looks up one group at a time and stops at the first failure; the group-based
            // rule would have looked up both sources in a single call.
            graphGroupRepository.Verify(x => x.GetGroupsAsync(It.Is<List<Guid>>(ids => ids.Count > 1)), Times.Never);
            graphGroupRepository.Verify(x => x.GetUserWithOnPremisesImmutableIdAsync(It.IsAny<string>(), It.IsAny<Guid?>()), Times.Never);
        }

        [TestMethod]
        public async Task PerPart_AllRulesEnabled_PerPartApprovesHiddenOwner_ApprovesAsync()
        {
            var syncJobId = Guid.NewGuid();
            var groupId = Guid.NewGuid();
            var requestorId = Guid.NewGuid().ToString();

            // The group-based rule rejects any HiddenMembership source; per-part approves it because
            // the requestor is an owner. Per-part is authoritative, so the job is approved.
            var syncJob = new SyncJob { Id = syncJobId, Status = SyncStatus.PendingAutoApproval.ToString(), Query = $"[{{\"type\":\"GroupMembership\",\"source\":\"{groupId}\"}}]" };

            var syncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            var settingsRepository = new Mock<IDatabaseSettingsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var syncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            var logger = new Mock<ILogger<AutoApproverService>>();

            syncJobsRepository.Setup(x => x.GetSyncJobAsync(syncJobId)).ReturnsAsync(syncJob);
            EnableAllRules(settingsRepository);
            graphGroupRepository.Setup(x => x.GetGroupsAsync(It.IsAny<List<Guid>>()))
                                .ReturnsAsync(new List<AzureADGroup> { new AzureADGroup { ObjectId = groupId, Visibility = "HiddenMembership" } });
            graphGroupRepository.Setup(x => x.GetGroupOwnersAsync(groupId, It.IsAny<int>()))
                                .ReturnsAsync(new List<AzureADUser> { new AzureADUser { ObjectId = Guid.Parse(requestorId) } });

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger);
            await service.ProcessAutoApprovalAsync(Message(syncJobId, requestorId));

            VerifyApproved(syncJobsRepository, syncJobChangeRepository, syncJobId);
            graphGroupRepository.Verify(x => x.GetUserWithOnPremisesImmutableIdAsync(It.IsAny<string>(), It.IsAny<Guid?>()), Times.Never);
        }

        [TestMethod]
        public async Task PerPart_Disabled_GroupBasedAndOrgLeaderStillEvaluatedAsync()
        {
            var syncJobId = Guid.NewGuid();
            var groupId = Guid.NewGuid();
            var requestorId = Guid.NewGuid().ToString();

            // Same shape per-part would reject (Private, requestor not an owner). With per-part off the
            // legacy OR-ed behaviour must be unchanged, so the group-based rule approves it.
            var syncJob = new SyncJob { Id = syncJobId, Status = SyncStatus.PendingAutoApproval.ToString(), Query = $"[{{\"type\":\"GroupMembership\",\"source\":\"{groupId}\"}}]" };

            var syncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            var settingsRepository = new Mock<IDatabaseSettingsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var syncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            var logger = new Mock<ILogger<AutoApproverService>>();

            syncJobsRepository.Setup(x => x.GetSyncJobAsync(syncJobId)).ReturnsAsync(syncJob);
            settingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled))
                              .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled, SettingValue = "true" });
            settingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled))
                              .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled, SettingValue = "true" });
            settingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsPerPartAutoApprovalEnabled))
                              .ReturnsAsync(new Setting { SettingKey = SettingKey.IsPerPartAutoApprovalEnabled, SettingValue = "false" });
            graphGroupRepository.Setup(x => x.GetGroupsAsync(It.IsAny<List<Guid>>()))
                                .ReturnsAsync(new List<AzureADGroup> { new AzureADGroup { ObjectId = groupId, Visibility = "Private" } });
            graphGroupRepository.Setup(x => x.GetGroupOwnersAsync(It.IsAny<Guid>(), It.IsAny<int>()))
                                .ReturnsAsync(new List<AzureADUser> { new AzureADUser { ObjectId = Guid.NewGuid() } });

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger);
            await service.ProcessAutoApprovalAsync(Message(syncJobId, requestorId));

            VerifyApproved(syncJobsRepository, syncJobChangeRepository, syncJobId);
        }

        [TestMethod]
        public async Task PerPart_FilterOnlySqlPart_RejectsThatPartWithoutFailingWholeParseAsync()
        {
            var syncJobId = Guid.NewGuid();
            var groupId = Guid.NewGuid();
            var requestorId = Guid.NewGuid().ToString();

            // A filter-only SqlMembership source is a supported GMM query shape (~29% of PROD jobs have
            // one). It must be rejected as a single part rather than failing the parse for the whole
            // query, so the decline names the offending part instead of claiming the query is unreadable.
            var syncJob = new SyncJob
            {
                Id = syncJobId,
                Status = SyncStatus.PendingAutoApproval.ToString(),
                Query = $"[{{\"type\":\"GroupMembership\",\"source\":\"{groupId}\"}},{{\"type\":\"SqlMembership\",\"source\":{{\"filter\":\"Building_Code = 102628\"}}}}]"
            };

            var syncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            var settingsRepository = new Mock<IDatabaseSettingsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var syncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            var logger = new Mock<ILogger<AutoApproverService>>();

            syncJobsRepository.Setup(x => x.GetSyncJobAsync(syncJobId)).ReturnsAsync(syncJob);
            EnableAllRules(settingsRepository);
            graphGroupRepository.Setup(x => x.GetGroupsAsync(It.IsAny<List<Guid>>()))
                                .ReturnsAsync(new List<AzureADGroup> { new AzureADGroup { ObjectId = groupId, Visibility = "Public" } });

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger);
            await service.ProcessAutoApprovalAsync(Message(syncJobId, requestorId));

            VerifyPendingReview(syncJobsRepository, syncJobChangeRepository);

            // The parse now succeeds, so the public group part ahead of the SQL part is evaluated.
            // Reaching Graph at all proves the query was not discarded up front as unparsable.
            graphGroupRepository.Verify(x => x.GetGroupsAsync(It.IsAny<List<Guid>>()), Times.AtLeastOnce);
        }

        [TestMethod]
        public async Task PerPart_FilterOnlySqlPart_IsNeverApprovedAsync()
        {
            var syncJobId = Guid.NewGuid();
            var requestorId = Guid.NewGuid().ToString();

            // Fail-closed guard for the parser change: a source with no manager names nobody to match,
            // so it must never approve even when the requestor resolves to an EmployeeId.
            var syncJob = new SyncJob
            {
                Id = syncJobId,
                Status = SyncStatus.PendingAutoApproval.ToString(),
                Query = "[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Building_Code = 102628\"}}]"
            };

            var syncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            var settingsRepository = new Mock<IDatabaseSettingsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var syncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            var logger = new Mock<ILogger<AutoApproverService>>();

            syncJobsRepository.Setup(x => x.GetSyncJobAsync(syncJobId)).ReturnsAsync(syncJob);
            EnableAllRules(settingsRepository);

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger);
            await service.ProcessAutoApprovalAsync(Message(syncJobId, requestorId));

            VerifyPendingReview(syncJobsRepository, syncJobChangeRepository);
        }

        [TestMethod]
        public async Task PerPart_SqlManagerSelfWithDepthAndExclusionary_ApprovesAsync()
        {
            var syncJobId = Guid.NewGuid();
            var requestorId = Guid.NewGuid().ToString();
            var managerId = 4242;

            // manager.depth and exclusionary are not consulted by the rule. Ignoring depth is the safe
            // direction: SqlMembershipRepository.GetChildEntitiesAsync seeds its recursive CTE on the
            // manager and recurses strictly downward (ON e.ManagerId = emp.EmployeeId), so a depth only
            // narrows the requestor's own subtree. An absent depth is the broader query, and that shape
            // is already auto-approved, so a bounded depth can never widen who the requestor may pull.
            var syncJob = new SyncJob
            {
                Id = syncJobId,
                Status = SyncStatus.PendingAutoApproval.ToString(),
                Query = $"[{{\"type\":\"SqlMembership\",\"source\":{{\"manager\":{{\"id\":{managerId},\"depth\":5}},\"filter\":\"Building_Code = 102628\"}},\"exclusionary\":true}}]"
            };

            var syncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            var settingsRepository = new Mock<IDatabaseSettingsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var syncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            var logger = new Mock<ILogger<AutoApproverService>>();
            var sqlMembershipRepository = new Mock<ISqlMembershipRepository>();
            var dataFactoryRepository = new Mock<IDataFactoryRepository>();

            syncJobsRepository.Setup(x => x.GetSyncJobAsync(syncJobId)).ReturnsAsync(syncJob);
            EnablePerPartOnly(settingsRepository);
            dataFactoryRepository.Setup(x => x.GetMostRecentSucceededRunIdAsync()).ReturnsAsync("abc-def");
            sqlMembershipRepository.Setup(x => x.GetUserEmployeeIdAsync(requestorId, It.IsAny<string>())).ReturnsAsync(managerId);

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger, sqlMembershipRepository, dataFactoryRepository);
            await service.ProcessAutoApprovalAsync(Message(syncJobId, requestorId));

            VerifyApproved(syncJobsRepository, syncJobChangeRepository, syncJobId);
        }

        [TestMethod]
        public async Task PerPart_UnsupportedSourceType_DeclineNamesTheCauseAsync()
        {
            var syncJobId = Guid.NewGuid();
            var requestorId = Guid.NewGuid().ToString();

            // A source type the rule does not handle must decline with the specific cause named, so an
            // operator can tell an unsupported-but-valid GMM shape apart from genuinely malformed JSON
            // without having to inspect the query (which carries HR identifiers).
            var syncJob = new SyncJob
            {
                Id = syncJobId,
                Status = SyncStatus.PendingAutoApproval.ToString(),
                Query = "[{\"type\":\"TeamsChannelMembership\",\"source\":{\"group\":\"abc\"}}]"
            };

            var syncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            var settingsRepository = new Mock<IDatabaseSettingsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var syncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            var logger = new Mock<ILogger<AutoApproverService>>();

            syncJobsRepository.Setup(x => x.GetSyncJobAsync(syncJobId)).ReturnsAsync(syncJob);
            EnablePerPartOnly(settingsRepository);

            // Source-generated [LoggerMessage] methods short-circuit on IsEnabled, which a Moq ILogger
            // returns false for by default, so nothing would be logged without this.
            logger.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger);
            await service.ProcessAutoApprovalAsync(Message(syncJobId, requestorId));

            VerifyPendingReview(syncJobsRepository, syncJobChangeRepository);

            logger.Verify(x => x.Log(
                LogLevel.Information,
                It.Is<EventId>(e => e.Id == 250134),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("UnsupportedSourceType")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);
        }
    }
}
