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
            Mock<IDataFactoryRepository> dataFactoryRepository = null,
            Mock<IDatabaseSqlMembershipSourcesRepository> sqlMembershipSourcesRepository = null)
        {
            return new AutoApproverService(
                syncJobsRepository.Object,
                settingsRepository.Object,
                graphGroupRepository.Object,
                syncJobChangeRepository.Object,
                (sqlMembershipRepository ?? new Mock<ISqlMembershipRepository>()).Object,
                (dataFactoryRepository ?? new Mock<IDataFactoryRepository>()).Object,
                (sqlMembershipSourcesRepository ?? new Mock<IDatabaseSqlMembershipSourcesRepository>()).Object,
                logger.Object);
        }

        /// <summary>Turns on every qualification path.</summary>
        private static void EnableAllPaths(Mock<IDatabaseSettingsRepository> settingsRepository) =>
            EnablePaths(settingsRepository, groupBased: true, orgLeader: true, leaderAssistant: true);

        /// <summary>Turns on group-based and org-leader, leaving the assistant path off.</summary>
        private static void EnableGroupAndOrgLeaderPaths(Mock<IDatabaseSettingsRepository> settingsRepository) =>
            EnablePaths(settingsRepository, groupBased: true, orgLeader: true);

        private static void EnablePaths(Mock<IDatabaseSettingsRepository> settingsRepository, bool groupBased, bool orgLeader, bool leaderAssistant = false)
        {
            settingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled))
                              .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled, SettingValue = groupBased ? "true" : "false" });
            settingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled))
                              .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled, SettingValue = orgLeader ? "true" : "false" });
            settingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForLeaderAssistantEnabled))
                              .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForLeaderAssistantEnabled, SettingValue = leaderAssistant ? "true" : "false" });
        }

        /// <summary>Turns on sensitive attribute screening with the given flagged attribute names.</summary>
        private static Mock<IDatabaseSqlMembershipSourcesRepository> EnableScreening(
            Mock<IDatabaseSettingsRepository> settingsRepository,
            params string[] sensitiveAttributeNames)
        {
            settingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsSensitiveAttributeScreeningEnabled))
                              .ReturnsAsync(new Setting { SettingKey = SettingKey.IsSensitiveAttributeScreeningEnabled, SettingValue = "true" });

            var sourcesRepository = new Mock<IDatabaseSqlMembershipSourcesRepository>();
            sourcesRepository.Setup(x => x.GetDefaultSourceAttributesAsync())
                             .ReturnsAsync(sensitiveAttributeNames
                                 .Select(n => new SqlMembershipAttribute { Name = n, IsSensitive = true })
                                 .ToList());

            return sourcesRepository;
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
            EnableGroupAndOrgLeaderPaths(settingsRepository);

            graphGroupRepository.Setup(x => x.GetGroupsAsync(It.IsAny<List<Guid>>()))
                                .ReturnsAsync((List<Guid> ids) => ids.Select(id => new AzureADGroup { ObjectId = id, Visibility = "Public" }).ToList());

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
            EnableGroupAndOrgLeaderPaths(settingsRepository);
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
            EnableGroupAndOrgLeaderPaths(settingsRepository);
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
            EnableGroupAndOrgLeaderPaths(settingsRepository);
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
            EnableGroupAndOrgLeaderPaths(settingsRepository);
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
            EnableGroupAndOrgLeaderPaths(settingsRepository);
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
            EnableGroupAndOrgLeaderPaths(settingsRepository);
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
            EnableGroupAndOrgLeaderPaths(settingsRepository);
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
            EnableGroupAndOrgLeaderPaths(settingsRepository);
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
            EnableGroupAndOrgLeaderPaths(settingsRepository);
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
            EnableGroupAndOrgLeaderPaths(settingsRepository);
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
            EnableGroupAndOrgLeaderPaths(settingsRepository);
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
            EnableGroupAndOrgLeaderPaths(settingsRepository);
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
            EnableGroupAndOrgLeaderPaths(settingsRepository);
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
            EnableGroupAndOrgLeaderPaths(settingsRepository);
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
            EnableGroupAndOrgLeaderPaths(settingsRepository);
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

        // Qualification path switches: a part whose path is off is rejected before any lookup.

        [TestMethod]
        public async Task AllPathsDisabled_GroupQuery_RejectsWithoutAnyLookupAsync()
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
            EnablePaths(settingsRepository, groupBased: false, orgLeader: false);

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger);
            await service.ProcessAutoApprovalAsync(Message(syncJobId, Guid.NewGuid().ToString()));

            VerifyPendingReview(syncJobsRepository, syncJobChangeRepository);
            graphGroupRepository.Verify(x => x.GetGroupsAsync(It.IsAny<List<Guid>>()), Times.Never);
            graphGroupRepository.Verify(x => x.GetGroupOwnersAsync(It.IsAny<Guid>(), It.IsAny<int>()), Times.Never);
        }

        [TestMethod]
        public async Task AllPathsDisabled_SqlQuery_RejectsWithoutAnyLookupAsync()
        {
            var syncJobId = Guid.NewGuid();
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
            EnablePaths(settingsRepository, groupBased: false, orgLeader: false, leaderAssistant: false);

            logger.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger, sqlMembershipRepository, dataFactoryRepository);
            await service.ProcessAutoApprovalAsync(Message(syncJobId, Guid.NewGuid().ToString()));

            VerifyPendingReview(syncJobsRepository, syncJobChangeRepository);

            logger.Verify(x => x.Log(
                LogLevel.Information,
                It.Is<EventId>(e => e.Id == 250127),
                It.Is<It.IsAnyType>((v, _) => v.ToString().Contains("Sql_Paths_Disabled")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);

            // Neither SQL path is enabled, so none of their lookups should happen.
            dataFactoryRepository.Verify(x => x.GetMostRecentSucceededRunIdAsync(), Times.Never);
            sqlMembershipRepository.Verify(x => x.GetUserEmployeeIdAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            sqlMembershipRepository.Verify(x => x.GetUserAzureObjectIdAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
            graphGroupRepository.Verify(x => x.GetAssistantsAsync(It.IsAny<Guid>()), Times.Never);

            // Nothing was read, so the context line should be skipped rather than report "not found".
            logger.Verify(x => x.Log(
                LogLevel.Information,
                It.Is<EventId>(e => e.Id == 250133),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Never);
        }

        [TestMethod]
        public async Task GroupPathDisabled_GroupPartRejectedEvenWhenPublicAsync()
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
            EnablePaths(settingsRepository, groupBased: false, orgLeader: true);

            // A Public group would otherwise qualify, so approval here would mean the switch was ignored.
            graphGroupRepository.Setup(x => x.GetGroupsAsync(It.IsAny<List<Guid>>()))
                                .ReturnsAsync(new List<AzureADGroup> { new AzureADGroup { ObjectId = groupId, Visibility = "Public" } });

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger);
            await service.ProcessAutoApprovalAsync(Message(syncJobId, Guid.NewGuid().ToString()));

            VerifyPendingReview(syncJobsRepository, syncJobChangeRepository);
            graphGroupRepository.Verify(x => x.GetGroupsAsync(It.IsAny<List<Guid>>()), Times.Never);
        }

        [TestMethod]
        public async Task SqlPathDisabled_SqlPartRejectedEvenWhenManagerIsRequestorAsync()
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
            EnablePaths(settingsRepository, groupBased: true, orgLeader: false);
            dataFactoryRepository.Setup(x => x.GetMostRecentSucceededRunIdAsync()).ReturnsAsync("abc-def");
            sqlMembershipRepository.Setup(x => x.GetUserEmployeeIdAsync(requestorId, It.IsAny<string>())).ReturnsAsync(managerId);

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger, sqlMembershipRepository, dataFactoryRepository);
            await service.ProcessAutoApprovalAsync(Message(syncJobId, requestorId));

            VerifyPendingReview(syncJobsRepository, syncJobChangeRepository);

            // A disabled SQL path must not cost a run lookup or a users-table read.
            dataFactoryRepository.Verify(x => x.GetMostRecentSucceededRunIdAsync(), Times.Never);
            sqlMembershipRepository.Verify(x => x.GetUserEmployeeIdAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [TestMethod]
        public async Task SqlPathDisabled_MixedQuery_RejectsWholeJobAsync()
        {
            var syncJobId = Guid.NewGuid();
            var groupId = Guid.NewGuid();
            var requestorId = Guid.NewGuid().ToString();
            var managerId = 4242;

            // Every part must qualify, so the disabled SQL part declines the job.
            var syncJob = new SyncJob
            {
                Id = syncJobId,
                Status = SyncStatus.PendingAutoApproval.ToString(),
                Query = $"[{{\"type\":\"GroupMembership\",\"source\":\"{groupId}\"}},{{\"type\":\"SqlMembership\",\"source\":{{\"manager\":{{\"id\":{managerId}}}}}}}]"
            };

            var syncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            var settingsRepository = new Mock<IDatabaseSettingsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var syncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            var logger = new Mock<ILogger<AutoApproverService>>();
            var sqlMembershipRepository = new Mock<ISqlMembershipRepository>();
            var dataFactoryRepository = new Mock<IDataFactoryRepository>();

            syncJobsRepository.Setup(x => x.GetSyncJobAsync(syncJobId)).ReturnsAsync(syncJob);
            EnablePaths(settingsRepository, groupBased: true, orgLeader: false);
            graphGroupRepository.Setup(x => x.GetGroupsAsync(It.IsAny<List<Guid>>()))
                                .ReturnsAsync(new List<AzureADGroup> { new AzureADGroup { ObjectId = groupId, Visibility = "Public" } });
            sqlMembershipRepository.Setup(x => x.GetUserEmployeeIdAsync(requestorId, It.IsAny<string>())).ReturnsAsync(managerId);

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger, sqlMembershipRepository, dataFactoryRepository);
            await service.ProcessAutoApprovalAsync(Message(syncJobId, requestorId));

            VerifyPendingReview(syncJobsRepository, syncJobChangeRepository);
        }

        [TestMethod]
        public async Task GroupPathDisabled_SqlOnlyQueryStillApprovesAsync()
        {
            var syncJobId = Guid.NewGuid();
            var requestorId = Guid.NewGuid().ToString();
            var managerId = 4242;

            // The switches are independent: turning off group sources must not affect a SQL-only job.
            var syncJob = new SyncJob { Id = syncJobId, Status = SyncStatus.PendingAutoApproval.ToString(), Query = $"[{{\"type\":\"SqlMembership\",\"source\":{{\"manager\":{{\"id\":{managerId}}}}}}}]" };

            var syncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            var settingsRepository = new Mock<IDatabaseSettingsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var syncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            var logger = new Mock<ILogger<AutoApproverService>>();
            var sqlMembershipRepository = new Mock<ISqlMembershipRepository>();
            var dataFactoryRepository = new Mock<IDataFactoryRepository>();

            syncJobsRepository.Setup(x => x.GetSyncJobAsync(syncJobId)).ReturnsAsync(syncJob);
            EnablePaths(settingsRepository, groupBased: false, orgLeader: true);
            dataFactoryRepository.Setup(x => x.GetMostRecentSucceededRunIdAsync()).ReturnsAsync("abc-def");
            sqlMembershipRepository.Setup(x => x.GetUserEmployeeIdAsync(requestorId, It.IsAny<string>())).ReturnsAsync(managerId);

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger, sqlMembershipRepository, dataFactoryRepository);
            await service.ProcessAutoApprovalAsync(Message(syncJobId, requestorId));

            VerifyApproved(syncJobsRepository, syncJobChangeRepository, syncJobId);
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
            EnableGroupAndOrgLeaderPaths(settingsRepository);

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger);
            await service.ProcessAutoApprovalAsync(Message(syncJobId, Guid.NewGuid().ToString()));

            VerifyPendingReview(syncJobsRepository, syncJobChangeRepository);
        }

        // No whole-query fallback: a failed part cannot be rescued by the query as a whole.

        [TestMethod]
        public async Task AllPathsEnabled_PartFails_NoWholeQueryFallbackAsync()
        {
            var syncJobId = Guid.NewGuid();
            var groupId1 = Guid.NewGuid();
            var groupId2 = Guid.NewGuid();
            var requestorId = Guid.NewGuid().ToString();

            // The retired whole-query rule would have approved this; approval means a fallback survived.
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
            EnableGroupAndOrgLeaderPaths(settingsRepository);
            graphGroupRepository.Setup(x => x.GetGroupsAsync(It.IsAny<List<Guid>>()))
                                .ReturnsAsync((List<Guid> ids) => ids.Select(id => new AzureADGroup { ObjectId = id, Visibility = "Private" }).ToList());
            graphGroupRepository.Setup(x => x.GetGroupOwnersAsync(It.IsAny<Guid>(), It.IsAny<int>()))
                                .ReturnsAsync(new List<AzureADUser> { new AzureADUser { ObjectId = Guid.NewGuid() } });

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger);
            await service.ProcessAutoApprovalAsync(Message(syncJobId, requestorId));

            VerifyPendingReview(syncJobsRepository, syncJobChangeRepository);

            // Per-part stops at the first failure; the retired rule looked up every source at once.
            graphGroupRepository.Verify(x => x.GetGroupsAsync(It.Is<List<Guid>>(ids => ids.Count > 1)), Times.Never);
            graphGroupRepository.Verify(x => x.GetUserWithOnPremisesImmutableIdAsync(It.IsAny<string>(), It.IsAny<Guid?>()), Times.Never);
        }

        [TestMethod]
        public async Task AllPathsEnabled_HiddenMembershipOwner_ApprovesAsync()
        {
            var syncJobId = Guid.NewGuid();
            var groupId = Guid.NewGuid();
            var requestorId = Guid.NewGuid().ToString();

            // The retired rule rejected HiddenMembership; per-part approves it on ownership.
            var syncJob = new SyncJob { Id = syncJobId, Status = SyncStatus.PendingAutoApproval.ToString(), Query = $"[{{\"type\":\"GroupMembership\",\"source\":\"{groupId}\"}}]" };

            var syncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            var settingsRepository = new Mock<IDatabaseSettingsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var syncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            var logger = new Mock<ILogger<AutoApproverService>>();

            syncJobsRepository.Setup(x => x.GetSyncJobAsync(syncJobId)).ReturnsAsync(syncJob);
            EnableGroupAndOrgLeaderPaths(settingsRepository);
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
        public async Task PerPart_FilterOnlySqlPart_RejectsThatPartWithoutFailingWholeParseAsync()
        {
            var syncJobId = Guid.NewGuid();
            var groupId = Guid.NewGuid();
            var requestorId = Guid.NewGuid().ToString();

            // A filter-only SqlMembership source must reject as one part, not fail the whole parse.
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
            EnableGroupAndOrgLeaderPaths(settingsRepository);
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

            // A source with no manager names nobody to match, so it must never approve.
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
            EnableGroupAndOrgLeaderPaths(settingsRepository);

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

            // depth and exclusionary are not consulted; a depth only narrows the requestor's subtree.
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
            EnableGroupAndOrgLeaderPaths(settingsRepository);
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
            EnableGroupAndOrgLeaderPaths(settingsRepository);

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

        [TestMethod]
        public async Task PerPart_SqlRequestorIsPublishedAssistant_ApprovesAsync()
        {
            var syncJobId = Guid.NewGuid();
            var requestorObjectId = Guid.NewGuid();
            var requestorId = requestorObjectId.ToString();
            var leaderObjectId = Guid.NewGuid();
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
            EnableAllPaths(settingsRepository);
            dataFactoryRepository.Setup(x => x.GetMostRecentSucceededRunIdAsync()).ReturnsAsync("abc-def");

            // The requestor is not the manager, so only the assistant relationship can approve.
            sqlMembershipRepository.Setup(x => x.GetUserEmployeeIdAsync(requestorId, It.IsAny<string>())).ReturnsAsync(999);
            sqlMembershipRepository.Setup(x => x.GetUserAzureObjectIdAsync(managerId, It.IsAny<string>())).Returns(Task.FromResult<string?>(leaderObjectId.ToString()));
            graphGroupRepository.Setup(x => x.GetAssistantsAsync(leaderObjectId))
                                .ReturnsAsync(new List<AzureADUser> { new AzureADUser { ObjectId = requestorObjectId } });

            logger.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger, sqlMembershipRepository, dataFactoryRepository);
            await service.ProcessAutoApprovalAsync(Message(syncJobId, requestorId));

            VerifyApproved(syncJobsRepository, syncJobChangeRepository, syncJobId);

            logger.Verify(x => x.Log(
                LogLevel.Information,
                It.Is<EventId>(e => e.Id == 250135),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);

            // The summary must attribute the part to the path that actually approved it.
            logger.Verify(x => x.Log(
                LogLevel.Information,
                It.Is<EventId>(e => e.Id == 250116),
                It.Is<It.IsAnyType>((v, _) => v.ToString().Contains("SqlMembership(manager-self): 0")
                                           && v.ToString().Contains("SqlMembership(assistant): 1")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);
        }

        [TestMethod]
        public async Task PerPart_SqlPart_LogsLeaderAssistantPathStateAsync()
        {
            var syncJobId = Guid.NewGuid();
            var requestorObjectId = Guid.NewGuid();
            var requestorId = requestorObjectId.ToString();
            var leaderObjectId = Guid.NewGuid();
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
            EnableAllPaths(settingsRepository);
            dataFactoryRepository.Setup(x => x.GetMostRecentSucceededRunIdAsync()).ReturnsAsync("abc-def");
            sqlMembershipRepository.Setup(x => x.GetUserEmployeeIdAsync(requestorId, It.IsAny<string>())).ReturnsAsync(999);
            sqlMembershipRepository.Setup(x => x.GetUserAzureObjectIdAsync(managerId, It.IsAny<string>())).Returns(Task.FromResult<string?>(leaderObjectId.ToString()));
            graphGroupRepository.Setup(x => x.GetAssistantsAsync(leaderObjectId))
                                .ReturnsAsync(new List<AzureADUser> { new AzureADUser { ObjectId = requestorObjectId } });

            logger.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger, sqlMembershipRepository, dataFactoryRepository);
            await service.ProcessAutoApprovalAsync(Message(syncJobId, requestorId));

            // The paths-resolved log covers only the two eagerly read settings, so the assistant
            // path needs its own line or its state is invisible during triage.
            logger.Verify(x => x.Log(
                LogLevel.Information,
                It.Is<EventId>(e => e.Id == 250139),
                It.Is<It.IsAnyType>((v, _) => v.ToString().Contains("leader-assistant: True")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);
        }

        [TestMethod]
        public async Task PerPart_GroupOnlySubmission_DoesNotReadOrLogLeaderAssistantPathAsync()
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
            EnableGroupAndOrgLeaderPaths(settingsRepository);
            graphGroupRepository.Setup(x => x.GetGroupsAsync(It.IsAny<List<Guid>>()))
                                .ReturnsAsync(new List<AzureADGroup> { new AzureADGroup { ObjectId = groupId, Visibility = "Public" } });

            logger.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger);
            await service.ProcessAutoApprovalAsync(Message(syncJobId, requestorId));

            // The setting is read lazily, so a group-only submission must pay for neither the read nor the log.
            settingsRepository.Verify(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForLeaderAssistantEnabled), Times.Never);

            logger.Verify(x => x.Log(
                LogLevel.Information,
                It.Is<EventId>(e => e.Id == 250139),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Never);
        }

        [TestMethod]
        public async Task PerPart_SqlAssistantSubRuleDisabled_MovesToPendingReviewAsync()
        {
            var syncJobId = Guid.NewGuid();
            var requestorObjectId = Guid.NewGuid();
            var requestorId = requestorObjectId.ToString();
            var leaderObjectId = Guid.NewGuid();
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

            // A genuine assistant relationship must still be ignored while that path is off.
            EnableGroupAndOrgLeaderPaths(settingsRepository);
            dataFactoryRepository.Setup(x => x.GetMostRecentSucceededRunIdAsync()).ReturnsAsync("abc-def");
            sqlMembershipRepository.Setup(x => x.GetUserEmployeeIdAsync(requestorId, It.IsAny<string>())).ReturnsAsync(999);
            sqlMembershipRepository.Setup(x => x.GetUserAzureObjectIdAsync(managerId, It.IsAny<string>())).Returns(Task.FromResult<string?>(leaderObjectId.ToString()));
            graphGroupRepository.Setup(x => x.GetAssistantsAsync(leaderObjectId))
                                .ReturnsAsync(new List<AzureADUser> { new AzureADUser { ObjectId = requestorObjectId } });

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger, sqlMembershipRepository, dataFactoryRepository);
            await service.ProcessAutoApprovalAsync(Message(syncJobId, requestorId));

            VerifyPendingReview(syncJobsRepository, syncJobChangeRepository);
            graphGroupRepository.Verify(x => x.GetAssistantsAsync(It.IsAny<Guid>()), Times.Never);
        }

        [TestMethod]
        public async Task PerPart_SqlAssistantPathOnWhileManagerSelfPathOff_ApprovesWithoutReadingEmployeeIdAsync()
        {
            var syncJobId = Guid.NewGuid();
            var requestorObjectId = Guid.NewGuid();
            var requestorId = requestorObjectId.ToString();
            var leaderObjectId = Guid.NewGuid();
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

            // The assistant path stands on its own without manager-self approval.
            EnablePaths(settingsRepository, groupBased: false, orgLeader: false, leaderAssistant: true);
            dataFactoryRepository.Setup(x => x.GetMostRecentSucceededRunIdAsync()).ReturnsAsync("abc-def");
            sqlMembershipRepository.Setup(x => x.GetUserAzureObjectIdAsync(managerId, It.IsAny<string>())).Returns(Task.FromResult<string?>(leaderObjectId.ToString()));
            graphGroupRepository.Setup(x => x.GetAssistantsAsync(leaderObjectId))
                                .ReturnsAsync(new List<AzureADUser> { new AzureADUser { ObjectId = requestorObjectId } });

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger, sqlMembershipRepository, dataFactoryRepository);
            await service.ProcessAutoApprovalAsync(Message(syncJobId, requestorId));

            VerifyApproved(syncJobsRepository, syncJobChangeRepository, syncJobId);

            // Manager-self is off, so the requestor's employee id is never needed.
            sqlMembershipRepository.Verify(x => x.GetUserEmployeeIdAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [TestMethod]
        public async Task PerPart_SqlBothPathsDisabled_SkipsAssistantLookupAsync()
        {
            var syncJobId = Guid.NewGuid();
            var requestorObjectId = Guid.NewGuid();
            var requestorId = requestorObjectId.ToString();
            var leaderObjectId = Guid.NewGuid();
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

            // Both SQL paths off, so a genuine assistant relationship is not enough on its own.
            EnablePaths(settingsRepository, groupBased: true, orgLeader: false, leaderAssistant: false);
            graphGroupRepository.Setup(x => x.GetAssistantsAsync(leaderObjectId))
                                .ReturnsAsync(new List<AzureADUser> { new AzureADUser { ObjectId = requestorObjectId } });

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger, sqlMembershipRepository, dataFactoryRepository);
            await service.ProcessAutoApprovalAsync(Message(syncJobId, requestorId));

            VerifyPendingReview(syncJobsRepository, syncJobChangeRepository);
            graphGroupRepository.Verify(x => x.GetAssistantsAsync(It.IsAny<Guid>()), Times.Never);
            sqlMembershipRepository.Verify(x => x.GetUserAzureObjectIdAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
        }

        [TestMethod]
        public async Task PerPart_SqlRequestorIsNotAnAssistant_MovesToPendingReviewAsync()
        {
            var syncJobId = Guid.NewGuid();
            var requestorId = Guid.NewGuid().ToString();
            var leaderObjectId = Guid.NewGuid();
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
            EnableAllPaths(settingsRepository);
            dataFactoryRepository.Setup(x => x.GetMostRecentSucceededRunIdAsync()).ReturnsAsync("abc-def");
            sqlMembershipRepository.Setup(x => x.GetUserEmployeeIdAsync(requestorId, It.IsAny<string>())).ReturnsAsync(999);
            sqlMembershipRepository.Setup(x => x.GetUserAzureObjectIdAsync(managerId, It.IsAny<string>())).Returns(Task.FromResult<string?>(leaderObjectId.ToString()));

            // The leader publishes an assistant, just not this requestor.
            graphGroupRepository.Setup(x => x.GetAssistantsAsync(leaderObjectId))
                                .ReturnsAsync(new List<AzureADUser> { new AzureADUser { ObjectId = Guid.NewGuid() } });

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger, sqlMembershipRepository, dataFactoryRepository);
            await service.ProcessAutoApprovalAsync(Message(syncJobId, requestorId));

            VerifyPendingReview(syncJobsRepository, syncJobChangeRepository);
        }

        [TestMethod]
        public async Task PerPart_SqlLeaderPublishesNoAssistant_MovesToPendingReviewAsync()
        {
            var syncJobId = Guid.NewGuid();
            var requestorId = Guid.NewGuid().ToString();
            var leaderObjectId = Guid.NewGuid();
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
            EnableAllPaths(settingsRepository);
            dataFactoryRepository.Setup(x => x.GetMostRecentSucceededRunIdAsync()).ReturnsAsync("abc-def");
            sqlMembershipRepository.Setup(x => x.GetUserEmployeeIdAsync(requestorId, It.IsAny<string>())).ReturnsAsync(999);
            sqlMembershipRepository.Setup(x => x.GetUserAzureObjectIdAsync(managerId, It.IsAny<string>())).Returns(Task.FromResult<string?>(leaderObjectId.ToString()));

            // A successful read returning no assistant is an authoritative "no", not an outage.
            graphGroupRepository.Setup(x => x.GetAssistantsAsync(leaderObjectId)).ReturnsAsync(new List<AzureADUser>());

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger, sqlMembershipRepository, dataFactoryRepository);
            await service.ProcessAutoApprovalAsync(Message(syncJobId, requestorId));

            VerifyPendingReview(syncJobsRepository, syncJobChangeRepository);
        }

        [TestMethod]
        public async Task PerPart_SqlAssistantLookupUnavailable_MovesToPendingReviewAsync()
        {
            var syncJobId = Guid.NewGuid();
            var requestorId = Guid.NewGuid().ToString();
            var leaderObjectId = Guid.NewGuid();
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
            EnableAllPaths(settingsRepository);
            dataFactoryRepository.Setup(x => x.GetMostRecentSucceededRunIdAsync()).ReturnsAsync("abc-def");
            sqlMembershipRepository.Setup(x => x.GetUserEmployeeIdAsync(requestorId, It.IsAny<string>())).ReturnsAsync(999);
            sqlMembershipRepository.Setup(x => x.GetUserAzureObjectIdAsync(managerId, It.IsAny<string>())).Returns(Task.FromResult<string?>(leaderObjectId.ToString()));

            // Fail closed: an unreadable profile must never be treated as "no assistant".
            graphGroupRepository.Setup(x => x.GetAssistantsAsync(leaderObjectId)).ThrowsAsync(new InvalidOperationException("graph unavailable"));

            logger.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger, sqlMembershipRepository, dataFactoryRepository);
            await service.ProcessAutoApprovalAsync(Message(syncJobId, requestorId));

            VerifyPendingReview(syncJobsRepository, syncJobChangeRepository);

            logger.Verify(x => x.Log(
                LogLevel.Error,
                It.Is<EventId>(e => e.Id == 250136),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);
        }

        [TestMethod]
        public async Task PerPart_SqlManagerIdentityUnresolved_MovesToPendingReviewAsync()
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
            EnableAllPaths(settingsRepository);
            dataFactoryRepository.Setup(x => x.GetMostRecentSucceededRunIdAsync()).ReturnsAsync("abc-def");
            sqlMembershipRepository.Setup(x => x.GetUserEmployeeIdAsync(requestorId, It.IsAny<string>())).ReturnsAsync(999);

            // The manager has no usable directory object id, so there is nobody to read a profile for.
            sqlMembershipRepository.Setup(x => x.GetUserAzureObjectIdAsync(managerId, It.IsAny<string>())).Returns(Task.FromResult<string?>(string.Empty));

            logger.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger, sqlMembershipRepository, dataFactoryRepository);
            await service.ProcessAutoApprovalAsync(Message(syncJobId, requestorId));

            VerifyPendingReview(syncJobsRepository, syncJobChangeRepository);
            graphGroupRepository.Verify(x => x.GetAssistantsAsync(It.IsAny<Guid>()), Times.Never);

            logger.Verify(x => x.Log(
                LogLevel.Error,
                It.Is<EventId>(e => e.Id == 250137),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);
        }

        [TestMethod]
        public async Task PerPart_SqlManagerSelfWins_NoProfileReadAsync()
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
            EnableAllPaths(settingsRepository);
            dataFactoryRepository.Setup(x => x.GetMostRecentSucceededRunIdAsync()).ReturnsAsync("abc-def");

            // The requestor is the manager, so no profile read should be added.
            sqlMembershipRepository.Setup(x => x.GetUserEmployeeIdAsync(requestorId, It.IsAny<string>())).ReturnsAsync(managerId);

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger, sqlMembershipRepository, dataFactoryRepository);
            await service.ProcessAutoApprovalAsync(Message(syncJobId, requestorId));

            VerifyApproved(syncJobsRepository, syncJobChangeRepository, syncJobId);
            graphGroupRepository.Verify(x => x.GetAssistantsAsync(It.IsAny<Guid>()), Times.Never);
            sqlMembershipRepository.Verify(x => x.GetUserAzureObjectIdAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
        }

        [TestMethod]
        public async Task PerPart_SqlAssistantWithoutEmployeeId_ApprovesAsync()
        {
            var syncJobId = Guid.NewGuid();
            var requestorObjectId = Guid.NewGuid();
            var requestorId = requestorObjectId.ToString();
            var leaderObjectId = Guid.NewGuid();
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
            EnableAllPaths(settingsRepository);
            dataFactoryRepository.Setup(x => x.GetMostRecentSucceededRunIdAsync()).ReturnsAsync("abc-def");

            // Matched on object id, so an assistant absent from the HR table still approves.
            sqlMembershipRepository.Setup(x => x.GetUserEmployeeIdAsync(requestorId, It.IsAny<string>())).ReturnsAsync((int?)null);
            sqlMembershipRepository.Setup(x => x.GetUserAzureObjectIdAsync(managerId, It.IsAny<string>())).Returns(Task.FromResult<string?>(leaderObjectId.ToString()));
            graphGroupRepository.Setup(x => x.GetAssistantsAsync(leaderObjectId))
                                .ReturnsAsync(new List<AzureADUser> { new AzureADUser { ObjectId = requestorObjectId } });

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger, sqlMembershipRepository, dataFactoryRepository);
            await service.ProcessAutoApprovalAsync(Message(syncJobId, requestorId));

            VerifyApproved(syncJobsRepository, syncJobChangeRepository, syncJobId);
        }

        [TestMethod]
        public async Task PerPart_SqlSameLeaderAcrossParts_ReadsProfileOnceAsync()
        {
            var syncJobId = Guid.NewGuid();
            var requestorObjectId = Guid.NewGuid();
            var requestorId = requestorObjectId.ToString();
            var leaderObjectId = Guid.NewGuid();
            var managerId = 4242;

            var syncJob = new SyncJob
            {
                Id = syncJobId,
                Status = SyncStatus.PendingAutoApproval.ToString(),
                Query = $"[{{\"type\":\"SqlMembership\",\"source\":{{\"manager\":{{\"id\":{managerId}}}}}}},{{\"type\":\"SqlMembership\",\"source\":{{\"manager\":{{\"id\":{managerId}}},\"filter\":\"Department eq 'Sales'\"}}}}]"
            };

            var syncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            var settingsRepository = new Mock<IDatabaseSettingsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var syncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            var logger = new Mock<ILogger<AutoApproverService>>();
            var sqlMembershipRepository = new Mock<ISqlMembershipRepository>();
            var dataFactoryRepository = new Mock<IDataFactoryRepository>();

            syncJobsRepository.Setup(x => x.GetSyncJobAsync(syncJobId)).ReturnsAsync(syncJob);
            EnableAllPaths(settingsRepository);
            dataFactoryRepository.Setup(x => x.GetMostRecentSucceededRunIdAsync()).ReturnsAsync("abc-def");
            sqlMembershipRepository.Setup(x => x.GetUserEmployeeIdAsync(requestorId, It.IsAny<string>())).ReturnsAsync(999);
            sqlMembershipRepository.Setup(x => x.GetUserAzureObjectIdAsync(managerId, It.IsAny<string>())).Returns(Task.FromResult<string?>(leaderObjectId.ToString()));
            graphGroupRepository.Setup(x => x.GetAssistantsAsync(leaderObjectId))
                                .ReturnsAsync(new List<AzureADUser> { new AzureADUser { ObjectId = requestorObjectId } });

            var service = CreateService(syncJobsRepository, settingsRepository, graphGroupRepository, syncJobChangeRepository, logger, sqlMembershipRepository, dataFactoryRepository);
            await service.ProcessAutoApprovalAsync(Message(syncJobId, requestorId));

            VerifyApproved(syncJobsRepository, syncJobChangeRepository, syncJobId);

            // Both parts name the same leader, so the profile is read once for the whole submission.
            graphGroupRepository.Verify(x => x.GetAssistantsAsync(leaderObjectId), Times.Once);
            sqlMembershipRepository.Verify(x => x.GetUserAzureObjectIdAsync(managerId, It.IsAny<string>()), Times.Once);

            // GetOrgLeaderAsync also walks the leader's whole reporting tree to compute a depth this
            // path discards, so resolving the leader must not go through it.
            sqlMembershipRepository.Verify(x => x.GetOrgLeaderAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
        }

        /// <summary>Builds the mocks a manager-self SQL submission needs, with the given filter.</summary>
        private static (Mock<IDatabaseSyncJobsRepository> Jobs, Mock<IDatabaseSettingsRepository> Settings, Mock<IGraphGroupRepository> Graph,
                        Mock<ISyncJobChangeRepository> Changes, Mock<ILogger<AutoApproverService>> Logger, Mock<ISqlMembershipRepository> Sql,
                        Mock<IDataFactoryRepository> DataFactory, Guid SyncJobId, string RequestorId)
            ManagerSelfSubmission(string filter, int managerId = 4242)
        {
            var syncJobId = Guid.NewGuid();
            var requestorId = Guid.NewGuid().ToString();
            var filterJson = filter == null ? string.Empty : $",\"filter\":\"{filter}\"";

            var syncJob = new SyncJob
            {
                Id = syncJobId,
                Status = SyncStatus.PendingAutoApproval.ToString(),
                Query = $"[{{\"type\":\"SqlMembership\",\"source\":{{\"manager\":{{\"id\":{managerId}}}{filterJson}}}}}]"
            };

            var jobs = new Mock<IDatabaseSyncJobsRepository>();
            var settings = new Mock<IDatabaseSettingsRepository>();
            var graph = new Mock<IGraphGroupRepository>();
            var changes = new Mock<ISyncJobChangeRepository>();
            var logger = new Mock<ILogger<AutoApproverService>>();
            var sql = new Mock<ISqlMembershipRepository>();
            var dataFactory = new Mock<IDataFactoryRepository>();

            jobs.Setup(x => x.GetSyncJobAsync(syncJobId)).ReturnsAsync(syncJob);
            EnableAllPaths(settings);
            dataFactory.Setup(x => x.GetMostRecentSucceededRunIdAsync()).ReturnsAsync("abc-def");
            sql.Setup(x => x.GetUserEmployeeIdAsync(requestorId, It.IsAny<string>())).ReturnsAsync(managerId);
            logger.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

            return (jobs, settings, graph, changes, logger, sql, dataFactory, syncJobId, requestorId);
        }

        private static void VerifyLogged(Mock<ILogger<AutoApproverService>> logger, int eventId, Times times) =>
            logger.Verify(x => x.Log(
                It.IsAny<LogLevel>(),
                It.Is<EventId>(e => e.Id == eventId),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()), times);

        [TestMethod]
        public async Task Screening_SafeFilter_ApprovesAsync()
        {
            var m = ManagerSelfSubmission("Department eq 'Sales'");
            var sources = EnableScreening(m.Settings, "PayScaleStockLevelNbr");

            var service = CreateService(m.Jobs, m.Settings, m.Graph, m.Changes, m.Logger, m.Sql, m.DataFactory, sources);
            await service.ProcessAutoApprovalAsync(Message(m.SyncJobId, m.RequestorId));

            VerifyApproved(m.Jobs, m.Changes, m.SyncJobId);
            VerifyLogged(m.Logger, 250141, Times.Never());
        }

        [TestMethod]
        public async Task Screening_SensitiveFilter_ManagerSelfPath_RejectsAsync()
        {
            var m = ManagerSelfSubmission("PayScaleStockLevelNbr eq 60");
            var sources = EnableScreening(m.Settings, "PayScaleStockLevelNbr");

            var service = CreateService(m.Jobs, m.Settings, m.Graph, m.Changes, m.Logger, m.Sql, m.DataFactory, sources);
            await service.ProcessAutoApprovalAsync(Message(m.SyncJobId, m.RequestorId));

            VerifyPendingReview(m.Jobs, m.Changes);
            VerifyLogged(m.Logger, 250141, Times.Once());
        }

        [TestMethod]
        public async Task Screening_SensitiveFilter_AssistantPath_RejectsAsync()
        {
            var syncJobId = Guid.NewGuid();
            var requestorObjectId = Guid.NewGuid();
            var requestorId = requestorObjectId.ToString();
            var leaderObjectId = Guid.NewGuid();
            var managerId = 4242;

            var syncJob = new SyncJob
            {
                Id = syncJobId,
                Status = SyncStatus.PendingAutoApproval.ToString(),
                Query = $"[{{\"type\":\"SqlMembership\",\"source\":{{\"manager\":{{\"id\":{managerId}}},\"filter\":\"PayScaleStockLevelNbr eq 60\"}}}}]"
            };

            var jobs = new Mock<IDatabaseSyncJobsRepository>();
            var settings = new Mock<IDatabaseSettingsRepository>();
            var graph = new Mock<IGraphGroupRepository>();
            var changes = new Mock<ISyncJobChangeRepository>();
            var logger = new Mock<ILogger<AutoApproverService>>();
            var sql = new Mock<ISqlMembershipRepository>();
            var dataFactory = new Mock<IDataFactoryRepository>();

            jobs.Setup(x => x.GetSyncJobAsync(syncJobId)).ReturnsAsync(syncJob);
            EnableAllPaths(settings);
            dataFactory.Setup(x => x.GetMostRecentSucceededRunIdAsync()).ReturnsAsync("abc-def");

            // Not the manager, so only the assistant relationship could have approved this part.
            sql.Setup(x => x.GetUserEmployeeIdAsync(requestorId, It.IsAny<string>())).ReturnsAsync(999);
            sql.Setup(x => x.GetUserAzureObjectIdAsync(managerId, It.IsAny<string>())).Returns(Task.FromResult<string?>(leaderObjectId.ToString()));
            graph.Setup(x => x.GetAssistantsAsync(leaderObjectId))
                 .ReturnsAsync(new List<AzureADUser> { new AzureADUser { ObjectId = requestorObjectId } });
            logger.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

            var sources = EnableScreening(settings, "PayScaleStockLevelNbr");

            var service = CreateService(jobs, settings, graph, changes, logger, sql, dataFactory, sources);
            await service.ProcessAutoApprovalAsync(Message(syncJobId, requestorId));

            VerifyPendingReview(jobs, changes);
            VerifyLogged(logger, 250141, Times.Once());
        }

        [TestMethod]
        public async Task Screening_NoFilter_ApprovesAsync()
        {
            var m = ManagerSelfSubmission(null);
            var sources = EnableScreening(m.Settings, "PayScaleStockLevelNbr");

            var service = CreateService(m.Jobs, m.Settings, m.Graph, m.Changes, m.Logger, m.Sql, m.DataFactory, sources);
            await service.ProcessAutoApprovalAsync(Message(m.SyncJobId, m.RequestorId));

            VerifyApproved(m.Jobs, m.Changes, m.SyncJobId);
        }

        [TestMethod]
        public async Task Screening_NoAttributesFlagged_ApprovesAsync()
        {
            var m = ManagerSelfSubmission("PayScaleStockLevelNbr eq 60");
            var sources = EnableScreening(m.Settings);

            var service = CreateService(m.Jobs, m.Settings, m.Graph, m.Changes, m.Logger, m.Sql, m.DataFactory, sources);
            await service.ProcessAutoApprovalAsync(Message(m.SyncJobId, m.RequestorId));

            // Nothing is marked Sensitive, so turning screening on must not reject everything.
            VerifyApproved(m.Jobs, m.Changes, m.SyncJobId);
            VerifyLogged(m.Logger, 250142, Times.Once());
        }

        [TestMethod]
        public async Task Screening_ListReadFails_RejectsAsync()
        {
            var m = ManagerSelfSubmission("Department eq 'Sales'");
            var sources = EnableScreening(m.Settings);
            sources.Setup(x => x.GetDefaultSourceAttributesAsync()).ThrowsAsync(new Exception("database unavailable"));

            var service = CreateService(m.Jobs, m.Settings, m.Graph, m.Changes, m.Logger, m.Sql, m.DataFactory, sources);
            await service.ProcessAutoApprovalAsync(Message(m.SyncJobId, m.RequestorId));

            VerifyPendingReview(m.Jobs, m.Changes);
            VerifyLogged(m.Logger, 250122, Times.Once());
        }

        [TestMethod]
        public async Task Screening_Disabled_ApprovesSensitiveFilterAsync()
        {
            var m = ManagerSelfSubmission("PayScaleStockLevelNbr eq 60");
            var sources = new Mock<IDatabaseSqlMembershipSourcesRepository>();

            var service = CreateService(m.Jobs, m.Settings, m.Graph, m.Changes, m.Logger, m.Sql, m.DataFactory, sources);
            await service.ProcessAutoApprovalAsync(Message(m.SyncJobId, m.RequestorId));

            VerifyApproved(m.Jobs, m.Changes, m.SyncJobId);

            // The list is still read so the resolved count is logged truthfully, but it gates nothing while screening is off.
            sources.Verify(x => x.GetDefaultSourceAttributesAsync(), Times.Once);
        }

        [TestMethod]
        public async Task Screening_Disabled_ListReadFails_ApprovesWithoutErrorAsync()
        {
            var m = ManagerSelfSubmission("PayScaleStockLevelNbr eq 60");
            var sources = new Mock<IDatabaseSqlMembershipSourcesRepository>();
            sources.Setup(x => x.GetDefaultSourceAttributesAsync()).ThrowsAsync(new Exception("database unavailable"));

            var service = CreateService(m.Jobs, m.Settings, m.Graph, m.Changes, m.Logger, m.Sql, m.DataFactory, sources);
            await service.ProcessAutoApprovalAsync(Message(m.SyncJobId, m.RequestorId));

            // Reading the list is best-effort while screening is off, so a failure must not fail the part or raise an error.
            VerifyApproved(m.Jobs, m.Changes, m.SyncJobId);
            VerifyLogged(m.Logger, 250122, Times.Never());
        }

        [TestMethod]
        public async Task Screening_MatchesWholeWordsOnly_ApprovesAsync()
        {
            var m = ManagerSelfSubmission("SalaryBand eq 'A'");
            var sources = EnableScreening(m.Settings, "Salary");

            var service = CreateService(m.Jobs, m.Settings, m.Graph, m.Changes, m.Logger, m.Sql, m.DataFactory, sources);
            await service.ProcessAutoApprovalAsync(Message(m.SyncJobId, m.RequestorId));

            VerifyApproved(m.Jobs, m.Changes, m.SyncJobId);
        }

        [TestMethod]
        public async Task Screening_SensitiveButDisabledAttribute_RejectsAsync()
        {
            var m = ManagerSelfSubmission("PayScaleStockLevelNbr eq 60");

            m.Settings.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsSensitiveAttributeScreeningEnabled))
                      .ReturnsAsync(new Setting { SettingKey = SettingKey.IsSensitiveAttributeScreeningEnabled, SettingValue = "true" });

            var sources = new Mock<IDatabaseSqlMembershipSourcesRepository>();
            sources.Setup(x => x.GetDefaultSourceAttributesAsync())
                   .ReturnsAsync(new List<SqlMembershipAttribute>
                   {
                       new SqlMembershipAttribute { Name = "PayScaleStockLevelNbr", IsSensitive = true, Enabled = false }
                   });

            var service = CreateService(m.Jobs, m.Settings, m.Graph, m.Changes, m.Logger, m.Sql, m.DataFactory, sources);
            await service.ProcessAutoApprovalAsync(Message(m.SyncJobId, m.RequestorId));

            VerifyPendingReview(m.Jobs, m.Changes);
            VerifyLogged(m.Logger, 250141, Times.Once());
        }

        [TestMethod]
        public async Task Screening_MatchIsCaseInsensitiveAsync()
        {
            var m = ManagerSelfSubmission("payscalestocklevelnbr eq 60");
            var sources = EnableScreening(m.Settings, "PayScaleStockLevelNbr");

            var service = CreateService(m.Jobs, m.Settings, m.Graph, m.Changes, m.Logger, m.Sql, m.DataFactory, sources);
            await service.ProcessAutoApprovalAsync(Message(m.SyncJobId, m.RequestorId));

            VerifyPendingReview(m.Jobs, m.Changes);
        }

        private static (Mock<IDatabaseSyncJobsRepository> jobs,
                        Mock<IDatabaseSettingsRepository> settings,
                        Mock<IGraphGroupRepository> graph,
                        Mock<ISyncJobChangeRepository> changes,
                        Mock<ILogger<AutoApproverService>> logger,
                        SyncJob syncJob)
            SetupGroupBasedApproval(Guid syncJobId, DateTime startDate)
        {
            var groupId = Guid.NewGuid();
            var syncJob = new SyncJob
            {
                Id = syncJobId,
                Status = SyncStatus.PendingAutoApproval.ToString(),
                StartDate = startDate,
                Query = $"[{{\"type\":\"GroupMembership\",\"source\":\"{groupId}\"}}]"
            };

            var jobs = new Mock<IDatabaseSyncJobsRepository>();
            var settings = new Mock<IDatabaseSettingsRepository>();
            var graph = new Mock<IGraphGroupRepository>();
            var changes = new Mock<ISyncJobChangeRepository>();
            var logger = new Mock<ILogger<AutoApproverService>>();

            jobs.Setup(x => x.GetSyncJobAsync(syncJobId)).ReturnsAsync(syncJob);
            settings.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled))
                    .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled, SettingValue = "true" });
            settings.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled))
                    .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled, SettingValue = "false" });
            graph.Setup(x => x.GetGroupsAsync(It.IsAny<List<Guid>>()))
                 .ReturnsAsync(new List<AzureADGroup> { new AzureADGroup { ObjectId = groupId, Visibility = "Public" } });

            return (jobs, settings, graph, changes, logger, syncJob);
        }

        [TestMethod]
        public async Task ProcessAutoApproval_UpdateOrigin_ApprovesWithoutTouchingStartDateAsync()
        {
            // FR-017 / contract test 1: an Update-origin approval writes UpdateAutoApproved, moves the job to
            // Idle, and MUST NOT push StartDate — an update never expedites or reschedules an existing job.
            var syncJobId = Guid.NewGuid();
            var requestorId = Guid.NewGuid().ToString();
            var originalStartDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            var (jobs, settings, graph, changes, logger, syncJob) = SetupGroupBasedApproval(syncJobId, originalStartDate);
            var service = CreateService(jobs, settings, graph, changes, logger);

            await service.ProcessAutoApprovalAsync(new AutoApprovalQueueMessage
            {
                SyncJobId = syncJobId,
                RequestorObjectId = requestorId,
                RequestorDisplayName = "Requestor",
                Origin = AutoApprovalOrigin.Update
            });

            jobs.Verify(x => x.UpdateSyncJobsAsync(It.Is<IEnumerable<SyncJob>>(j =>
                j.First().Status == SyncStatus.Idle.ToString()), null), Times.Once);
            changes.Verify(x => x.Save(It.Is<SyncJobChange>(c =>
                c.SyncJobId == syncJobId && c.ChangeReason == SyncJobChangeReason.UpdateAutoApproved.ToString())), Times.Once);
            Assert.AreEqual(originalStartDate, syncJob.StartDate, "Update-origin approval must not modify StartDate.");
        }

        [TestMethod]
        public async Task ProcessAutoApproval_UpdateOrigin_DeclineMovesToPendingReviewAsync()
        {
            // FR-015 / contract test 2: an Update-origin decline lands in PendingReview with no audit record.
            var syncJobId = Guid.NewGuid();
            var requestorId = Guid.NewGuid().ToString();
            var groupId = Guid.NewGuid();

            var syncJob = new SyncJob
            {
                Id = syncJobId,
                Status = SyncStatus.PendingAutoApproval.ToString(),
                StartDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Query = $"[{{\"type\":\"GroupMembership\",\"source\":\"{groupId}\"}}]"
            };

            var jobs = new Mock<IDatabaseSyncJobsRepository>();
            var settings = new Mock<IDatabaseSettingsRepository>();
            var graph = new Mock<IGraphGroupRepository>();
            var changes = new Mock<ISyncJobChangeRepository>();
            var logger = new Mock<ILogger<AutoApproverService>>();

            jobs.Setup(x => x.GetSyncJobAsync(syncJobId)).ReturnsAsync(syncJob);
            settings.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled))
                    .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled, SettingValue = "true" });
            settings.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled))
                    .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled, SettingValue = "false" });
            graph.Setup(x => x.GetGroupsAsync(It.IsAny<List<Guid>>()))
                 .ReturnsAsync(new List<AzureADGroup> { new AzureADGroup { ObjectId = groupId, Visibility = "HiddenMembership" } });

            var service = CreateService(jobs, settings, graph, changes, logger);

            await service.ProcessAutoApprovalAsync(new AutoApprovalQueueMessage
            {
                SyncJobId = syncJobId,
                RequestorObjectId = requestorId,
                RequestorDisplayName = "Requestor",
                Origin = AutoApprovalOrigin.Update
            });

            jobs.Verify(x => x.UpdateSyncJobsAsync(It.Is<IEnumerable<SyncJob>>(j =>
                j.First().Status == SyncStatus.PendingReview.ToString()), It.IsAny<SyncStatus?>()), Times.Once);
            changes.Verify(x => x.Save(It.IsAny<SyncJobChange>()), Times.Never);
        }

        [TestMethod]
        public async Task ProcessAutoApproval_OnboardingOrigin_PushesStartDateAndWritesOnboardingReasonAsync()
        {
            // FR-018 / contract test 3: onboarding behavior is unchanged — StartDate in the past is pushed out
            // 24h and the audit reason stays OnboardingAutoApproved.
            var syncJobId = Guid.NewGuid();
            var requestorId = Guid.NewGuid().ToString();
            var originalStartDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            var (jobs, settings, graph, changes, logger, syncJob) = SetupGroupBasedApproval(syncJobId, originalStartDate);
            var service = CreateService(jobs, settings, graph, changes, logger);

            await service.ProcessAutoApprovalAsync(new AutoApprovalQueueMessage
            {
                SyncJobId = syncJobId,
                RequestorObjectId = requestorId,
                RequestorDisplayName = "Requestor",
                Origin = AutoApprovalOrigin.Onboarding
            });

            jobs.Verify(x => x.UpdateSyncJobsAsync(It.Is<IEnumerable<SyncJob>>(j =>
                j.First().Status == SyncStatus.Idle.ToString()), null), Times.Once);
            changes.Verify(x => x.Save(It.Is<SyncJobChange>(c =>
                c.SyncJobId == syncJobId && c.ChangeReason == SyncJobChangeReason.OnboardingAutoApproved.ToString())), Times.Once);
            Assert.IsTrue(syncJob.StartDate > DateTime.UtcNow, "Onboarding approval must push a past StartDate into the future.");
        }

        [TestMethod]
        public async Task ProcessAutoApproval_MissingOrigin_DefaultsToOnboardingBehaviorAsync()
        {
            // FR-016 / contract test 4: a message serialized before the Origin field existed deserializes with
            // Origin == Onboarding and behaves exactly like the onboarding path (StartDate push + Onboarding reason).
            var syncJobId = Guid.NewGuid();
            var requestorId = Guid.NewGuid().ToString();
            var originalStartDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            var (jobs, settings, graph, changes, logger, syncJob) = SetupGroupBasedApproval(syncJobId, originalStartDate);
            var service = CreateService(jobs, settings, graph, changes, logger);

            var legacyJson = $"{{\"SyncJobId\":\"{syncJobId}\",\"RequestorObjectId\":\"{requestorId}\",\"RequestorDisplayName\":\"Requestor\"}}";
            var message = System.Text.Json.JsonSerializer.Deserialize<AutoApprovalQueueMessage>(legacyJson);
            Assert.AreEqual(AutoApprovalOrigin.Onboarding, message.Origin, "Absent Origin must default to Onboarding.");

            await service.ProcessAutoApprovalAsync(message);

            changes.Verify(x => x.Save(It.Is<SyncJobChange>(c =>
                c.SyncJobId == syncJobId && c.ChangeReason == SyncJobChangeReason.OnboardingAutoApproved.ToString())), Times.Once);
            Assert.IsTrue(syncJob.StartDate > DateTime.UtcNow, "Missing-origin message must behave like onboarding and push StartDate.");
        }
    }
}
