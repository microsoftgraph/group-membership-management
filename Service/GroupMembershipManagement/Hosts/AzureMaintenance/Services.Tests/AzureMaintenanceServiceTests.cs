// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.AzureMaintenance;
using Models.ThresholdNotifications;
using Moq;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class AzureMaintenanceServiceTests
    {
        [TestMethod]
        public async Task TestBackupInactiveJobs()
        {
            var loggerMock = new Mock<ILoggingRepository>();
            loggerMock.Setup(x => x.LogMessageAsync(It.IsAny<LogMessage>(), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()));
            var purgedJobs = new List<PurgedSyncJob>();

            for (int i = 0; i < 2; i++)
            {
                var job = new PurgedSyncJob
                {
                    Requestor = $"requestor_{i}@email.com",
                    Id = Guid.NewGuid(),
                    Period = 6,
                    Query = "[{ \"type\": \"GroupMembership\", \"source\": \"da144736-962b-4879-a304-acd9f5221e78\"}]",
                    StartDate = DateTime.UtcNow.AddDays(-1),
                    Status = SyncStatus.CustomerPaused.ToString(),
                    TargetOfficeGroupId = Guid.NewGuid(),
                    LastRunTime = SqlDateTime.MinValue.Value,
                    RunId = Guid.NewGuid(),
                    PurgedAt = DateTime.UtcNow
                };

                purgedJobs.Add(job);
            }

            var jobs = new List<SyncJob>();

            for (int i = 0; i < 2; i++)
            {
                var job = new SyncJob
                {
                    Requestor = $"requestor_{i}@email.com",
                    Id = Guid.NewGuid(),
                    Period = 6,
                    Query = "[{ \"type\": \"GroupMembership\", \"source\": \"da144736-962b-4879-a304-acd9f5221e78\"}]",
                    StartDate = DateTime.UtcNow.AddDays(-1),
                    Status = SyncStatus.CustomerPaused.ToString(),
                    TargetOfficeGroupId = Guid.NewGuid(),
                    LastRunTime = SqlDateTime.MinValue.Value,
                    RunId = Guid.NewGuid()
                };

                jobs.Add(job);
            }

            var syncJobRepository = new Mock<IDatabaseSyncJobsRepository>();
            var purgedSyncJobRepository = new Mock<IDatabasePurgedSyncJobsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var mailAddresses = new Mock<IEmailSenderRecipient>();
            var mailRepository = new Mock<IMailRepository>();
            var handleInactiveJobsConfig = new Mock<IHandleInactiveJobsConfig>();
            var notificationRepository = new Mock<INotificationRepository>();

            purgedSyncJobRepository.Setup(x => x.InsertPurgedSyncJobsAsync(It.IsAny<IEnumerable<PurgedSyncJob>>())).ReturnsAsync(2);

            var azureMaintenanceService = new AzureMaintenanceService(syncJobRepository.Object,
                                                purgedSyncJobRepository.Object,
                                                graphGroupRepository.Object,
                                                mailAddresses.Object,
                                                mailRepository.Object,
                                                handleInactiveJobsConfig.Object,
                                                notificationRepository.Object);

            var countOfBackedUpJobs = await azureMaintenanceService.BackupInactiveJobsAsync(jobs);
            Assert.AreEqual(countOfBackedUpJobs, jobs.Count);
            purgedSyncJobRepository.Verify(x => x.InsertPurgedSyncJobsAsync(It.IsAny<List<PurgedSyncJob>>()), Times.Once());

            jobs = new List<SyncJob>();
            countOfBackedUpJobs = await azureMaintenanceService.BackupInactiveJobsAsync(jobs);
            Assert.AreEqual(countOfBackedUpJobs, 0);
        }

        [TestMethod]
        public async Task TestRemoveBackups()
        {
            var loggerMock = new Mock<ILoggingRepository>();
            loggerMock.Setup(x => x.LogMessageAsync(It.IsAny<LogMessage>(), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()));

            var tables = new List<PurgedSyncJob>();

            for (int i = 0; i < 2; i++)
            {
                var table = new PurgedSyncJob
                {
                    Requestor = $"requestor_{i}@email.com",
                    Id = Guid.NewGuid(),
                    Period = 6,
                    Query = "[{ \"type\": \"GroupMembership\", \"source\": \"da144736-962b-4879-a304-acd9f5221e78\"}]",
                    StartDate = DateTime.UtcNow.AddDays(-1),
                    Status = SyncStatus.CustomerPaused.ToString(),
                    TargetOfficeGroupId = Guid.NewGuid(),
                    LastRunTime = SqlDateTime.MinValue.Value,
                    RunId = Guid.NewGuid(),
                    PurgedAt = DateTime.UtcNow.AddDays(-50)
                };

                tables.Add(table);
            }

            var syncJobRepository = new Mock<IDatabaseSyncJobsRepository>();
            var purgedSyncJobRepository = new Mock<IDatabasePurgedSyncJobsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var mailAddresses = new Mock<IEmailSenderRecipient>();
            var mailRepository = new Mock<IMailRepository>();
            var handleInactiveJobsConfig = new Mock<IHandleInactiveJobsConfig>();
            var notificationRepository = new Mock<INotificationRepository>();

            purgedSyncJobRepository.Setup(x => x.GetPurgedSyncJobsAsync(It.IsAny<DateTime>())).ReturnsAsync(tables);
            purgedSyncJobRepository.Setup(x => x.DeletePurgedSyncJobsAsync(It.IsAny<IEnumerable<PurgedSyncJob>>())).ReturnsAsync(2);

            var azureMaintenanceService = new AzureMaintenanceService(syncJobRepository.Object,
                                                purgedSyncJobRepository.Object,
                                                graphGroupRepository.Object,
                                                mailAddresses.Object,
                                                mailRepository.Object,
                                                handleInactiveJobsConfig.Object,
                                                notificationRepository.Object);

            var countOfRemovedBackUps = await azureMaintenanceService.RemoveBackupsAsync();
            Assert.AreEqual(countOfRemovedBackUps, tables.Count);
            purgedSyncJobRepository.Verify(x => x.GetPurgedSyncJobsAsync(It.IsAny<DateTime>()), Times.Once());
            purgedSyncJobRepository.Verify(x => x.DeletePurgedSyncJobsAsync(It.IsAny<IEnumerable<PurgedSyncJob>>()), Times.Once());
        }

        [TestMethod]
        public async Task TestRemoveInactiveJobs()
        {
            var loggerMock = new Mock<ILoggingRepository>();
            loggerMock.Setup(x => x.LogMessageAsync(It.IsAny<LogMessage>(), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()));

            var jobs = new List<SyncJob>();

            for (int i = 0; i < 2; i++)
            {
                var job = new SyncJob
                {
                    Requestor = $"requestor_{i}@email.com",
                    Id = Guid.NewGuid(),
                    Period = 6,
                    Query = "[{ \"type\": \"GroupMembership\", \"source\": \"da144736-962b-4879-a304-acd9f5221e78\"}]",
                    StartDate = DateTime.UtcNow.AddDays(-1),
                    Status = SyncStatus.CustomerPaused.ToString(),
                    TargetOfficeGroupId = Guid.NewGuid(),
                    LastRunTime = SqlDateTime.MinValue.Value,
                    RunId = Guid.NewGuid()
                };

                jobs.Add(job);
            }

            var j = GetJobs(jobs);

            var syncJobRepository = new Mock<IDatabaseSyncJobsRepository>();
            var purgedSyncJobRepository = new Mock<IDatabasePurgedSyncJobsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var mailAddresses = new Mock<IEmailSenderRecipient>();
            var mailRepository = new Mock<IMailRepository>();
            var handleInactiveJobsConfig = new Mock<IHandleInactiveJobsConfig>();
            var notificationRepository = new Mock<INotificationRepository>();

            var azureMaintenanceService = new AzureMaintenanceService(syncJobRepository.Object,
                                                purgedSyncJobRepository.Object,
                                                graphGroupRepository.Object,
                                                mailAddresses.Object,
                                                mailRepository.Object,
                                                handleInactiveJobsConfig.Object,
                                                notificationRepository.Object);

            await azureMaintenanceService.RemoveInactiveJobsAsync(j);
            syncJobRepository.Verify(x => x.DeleteSyncJobsAsync(It.IsAny<List<SyncJob>>()), Times.Once());
        }

        [TestMethod]
        public async Task TestExpireNotifications()
        {
            var loggerMock = new Mock<ILoggingRepository>();
            loggerMock.Setup(x => x.LogMessageAsync(It.IsAny<LogMessage>(), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()));

            var jobs = new List<SyncJob>();

            for (int i = 0; i < 2; i++)
            {
                var job = new SyncJob
                {
                    Requestor = $"requestor_{i}@email.com",
                    PartitionKey = DateTime.UtcNow.ToString("MMddyyyy"),
                    RowKey = Guid.NewGuid().ToString(),
                    Period = 6,
                    Query = "[{ \"type\": \"SecurityGroup\", \"sources\": [\"da144736-962b-4879-a304-acd9f5221e78\"]}]",
                    StartDate = DateTime.UtcNow.AddDays(-1),
                    Status = SyncStatus.ThresholdExceeded.ToString(),
                    TargetOfficeGroupId = Guid.NewGuid(),
                    LastRunTime = DateTime.FromFileTimeUtc(0),
                    RunId = Guid.NewGuid()
                };

                jobs.Add(job);
            }

            var j = GetJobs(jobs);

            var azureTableBackupRepository = new Mock<IAzureTableBackupRepository>();
            var azureBlobBackupRepository = new Mock<IAzureStorageBackupRepository>();
            var syncJobRepository = new Mock<ISyncJobRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var mailAddresses = new Mock<IEmailSenderRecipient>();
            var mailRepository = new Mock<IMailRepository>();
            var handleInactiveJobsConfig = new Mock<IHandleInactiveJobsConfig>();
            var notificationRepository = new Mock<INotificationRepository>();
            var notification = new ThresholdNotification
            {
                ChangePercentageForAdditions = Random.Shared.Next(51, 100),
                ChangePercentageForRemovals = Random.Shared.Next(51, 100),
                ChangeQuantityForAdditions = Random.Shared.Next(50, 1000),
                ChangeQuantityForRemovals = Random.Shared.Next(50, 1000),
                CreatedTime = DateTime.UtcNow,
                Resolution = ThresholdNotificationResolution.Unresolved,
                Id = Guid.NewGuid(),
                SyncJobPartitionKey = Guid.NewGuid().ToString(),
                SyncJobRowKey = Guid.NewGuid().ToString(),
                ResolvedByUPN = string.Empty,
                ResolvedTime = DateTime.UtcNow,
                Status = ThresholdNotificationStatus.AwaitingResponse,
                TargetOfficeGroupId = Guid.NewGuid(),
                ThresholdPercentageForAdditions = Random.Shared.Next(1, 50),
                ThresholdPercentageForRemovals = Random.Shared.Next(1, 50),
                CardState = ThresholdNotificationCardState.DefaultCard
            };
            var azureTableBackupService = new AzureMaintenanceService(loggerMock.Object,
                                                azureTableBackupRepository.Object,
                                                azureBlobBackupRepository.Object,
                                                syncJobRepository.Object,
                                                graphGroupRepository.Object,
                                                mailAddresses.Object,
                                                mailRepository.Object,
                                                handleInactiveJobsConfig.Object,
                                                notificationRepository.Object);


            notificationRepository.Setup(x => x.GetThresholdNotificationBySyncJobKeysAsync(It.IsAny<string>(), It.IsAny<string>())).Returns(() => Task.FromResult(notification));
            await azureTableBackupService.ExpireNotificationsAsync(j);
            notificationRepository.Verify(x => x.UpdateNotificationStatusAsync(It.IsAny<ThresholdNotification>(), It.IsAny<ThresholdNotificationStatus>()), Times.Exactly(2));
        }

        [TestMethod]
        public async Task TestGetGroupName()
        {
            var loggerMock = new Mock<ILoggingRepository>();
            loggerMock.Setup(x => x.LogMessageAsync(It.IsAny<LogMessage>(), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()));

            var syncJobRepository = new Mock<IDatabaseSyncJobsRepository>();
            var purgedSyncJobRepository = new Mock<IDatabasePurgedSyncJobsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var mailAddresses = new Mock<IEmailSenderRecipient>();
            var mailRepository = new Mock<IMailRepository>();
            var handleInactiveJobsConfig = new Mock<IHandleInactiveJobsConfig>();
            var notificationRepository = new Mock<INotificationRepository>();

            graphGroupRepository.Setup(x => x.GetGroupNameAsync(It.IsAny<Guid>())).ReturnsAsync(() => "Test Group");

            var azureMaintenanceService = new AzureMaintenanceService(syncJobRepository.Object,
                                                purgedSyncJobRepository.Object,
                                                graphGroupRepository.Object,
                                                mailAddresses.Object,
                                                mailRepository.Object,
                                                handleInactiveJobsConfig.Object,
                                                notificationRepository.Object);

            await azureMaintenanceService.GetGroupNameAsync(Guid.NewGuid());
            graphGroupRepository.Verify(x => x.GetGroupNameAsync(It.IsAny<Guid>()), Times.Once());
        }

        [TestMethod]
        public async Task TestGetSyncJobs()
        {
            var loggerMock = new Mock<ILoggingRepository>();
            loggerMock.Setup(x => x.LogMessageAsync(It.IsAny<LogMessage>(), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()));

            var jobList = new List<SyncJob>();
            var job = new SyncJob
            {
                Requestor = "requestor@email.com",
                Id = Guid.NewGuid(),
                Period = 6,
                Query = "[{ \"type\": \"GroupMembership\", \"source\": \"da144736-962b-4879-a304-acd9f5221e78\"}]",
                StartDate = DateTime.UtcNow.AddDays(-1),
                Status = SyncStatus.CustomerPaused.ToString(),
                TargetOfficeGroupId = Guid.NewGuid(),
                LastRunTime = SqlDateTime.MinValue.Value,
                RunId = Guid.NewGuid()
            };
            jobList.Add(job);

            var syncJobRepository = new Mock<IDatabaseSyncJobsRepository>();
            var purgedSyncJobRepository = new Mock<IDatabasePurgedSyncJobsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var mailAddresses = new Mock<IEmailSenderRecipient>();
            var mailRepository = new Mock<IMailRepository>();
            var handleInactiveJobsConfig = new Mock<IHandleInactiveJobsConfig>();
            var notificationRepository = new Mock<INotificationRepository>();

            var azureMaintenanceService = new AzureMaintenanceService(syncJobRepository.Object,
                                                purgedSyncJobRepository.Object,
                                                graphGroupRepository.Object,
                                                mailAddresses.Object,
                                                mailRepository.Object,
                                                handleInactiveJobsConfig.Object,
                                                notificationRepository.Object);

            var jobs = await azureMaintenanceService.GetSyncJobsAsync();
            Assert.AreEqual(jobs.Count, 0);
            syncJobRepository.Verify(x => x.GetSyncJobsAsync(It.IsAny<bool>(), It.IsAny<SyncStatus[]>()), Times.Once());

            syncJobRepository.Setup(x => x.GetSyncJobsAsync(It.IsAny<bool>(), It.IsAny<SyncStatus[]>())).ReturnsAsync(jobList);
            jobs = await azureMaintenanceService.GetSyncJobsAsync();
            Assert.AreEqual(jobs.Count, 1);
            syncJobRepository.Verify(x => x.GetSyncJobsAsync(It.IsAny<bool>(), It.IsAny<SyncStatus[]>()), Times.Exactly(2));
        }

        [TestMethod]
        public async Task TestSendEmail()
        {
            var loggerMock = new Mock<ILoggingRepository>();
            loggerMock.Setup(x => x.LogMessageAsync(It.IsAny<LogMessage>(), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()));

            var job = new SyncJob
            {
                Requestor = $"requestor@email.com",
                Id = Guid.NewGuid(),
                Period = 6,
                Query = "[{ \"type\": \"GroupMembership\", \"source\": [\"da144736-962b-4879-a304-acd9f5221e78\"]}]",
                StartDate = DateTime.UtcNow.AddDays(-1),
                Status = SyncStatus.CustomerPaused.ToString(),
                TargetOfficeGroupId = Guid.NewGuid(),
                LastRunTime = SqlDateTime.MinValue.Value,
                RunId = Guid.NewGuid()
            };

            var users = new List<AzureADUser>();

            for (int i = 0; i < 2; i++)
            {
                var user = new AzureADUser
                {
                    Mail = $"requestor_{i}@email.com"
                };

                users.Add(user);
            }

            var syncJobRepository = new Mock<IDatabaseSyncJobsRepository>();
            var purgedSyncJobRepository = new Mock<IDatabasePurgedSyncJobsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var mailAddresses = new Mock<IEmailSenderRecipient>();
            var mailRepository = new Mock<IMailRepository>();
            var handleInactiveJobsConfig = new Mock<IHandleInactiveJobsConfig>();
            var notificationRepository = new Mock<INotificationRepository>();

            _ = graphGroupRepository.Setup(x => x.GetGroupOwnersAsync(job.TargetOfficeGroupId, 0)).ReturnsAsync(users);

            var azureMaintenanceService = new AzureMaintenanceService(syncJobRepository.Object,
                                                purgedSyncJobRepository.Object,
                                                graphGroupRepository.Object,
                                                mailAddresses.Object,
                                                mailRepository.Object,
                                                handleInactiveJobsConfig.Object,
                                                notificationRepository.Object);

            await azureMaintenanceService.SendEmailAsync(job, "Test Group");
            mailRepository.Verify(x => x.SendMailAsync(It.IsAny<EmailMessage>(), It.IsAny<Guid>(), It.IsAny<string>()), Times.Once());
        }

        public IEnumerable<SyncJob> GetJobs(List<SyncJob> jobs)
        {
            List<SyncJob> j = jobs;
            return j;
        }
    }
}