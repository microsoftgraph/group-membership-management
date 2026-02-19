// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.AzureMaintenance;
using Models.ServiceBus;
using Models.ThresholdNotifications;
using Moq;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
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
            loggerMock.Setup(x => x.LogMessageAsync(It.IsAny<LogMessage>(), It.IsAny<VerbosityLevel>(), It.IsAny<string>(), It.IsAny<string>()))
                      .Returns(Task.CompletedTask);
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
                    MembershipType = "GroupMembership",
                    LastRunTime = SqlDateTime.MinValue.Value,
                    RunId = Guid.NewGuid()
                };
                job.Group = new Group
                {
                    SyncJobId = job.Id,
                    GroupId = Guid.NewGuid()
                };
                job.TargetOfficeGroupId = job.Group.GroupId;
                jobs.Add(job);
            }

            var syncJobRepository = new Mock<IDatabaseSyncJobsRepository>();
            var groupsRepository = new Mock<IDatabaseGroupsRepository>();
            var channelsRepository = new Mock<IDatabaseChannelsRepository>();
            var purgedSyncJobRepository = new Mock<IDatabasePurgedSyncJobsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var handleInactiveJobsConfig = new Mock<IHandleInactiveJobsConfig>();
            var notificationRepository = new Mock<INotificationRepository>();
            var notificationQueueRepository = new Mock<IServiceBusQueueRepository>();

            purgedSyncJobRepository.Setup(x => x.InsertPurgedSyncJobsAsync(It.IsAny<IEnumerable<PurgedSyncJob>>())).ReturnsAsync(2);

            var azureMaintenanceService = new AzureMaintenanceService(syncJobRepository.Object,
                                                groupsRepository.Object,
                                                channelsRepository.Object,
                                                purgedSyncJobRepository.Object,
                                                graphGroupRepository.Object,
                                                handleInactiveJobsConfig.Object,
                                                notificationRepository.Object,
                                                notificationQueueRepository.Object,
                                                loggerMock.Object, new Mock<ISyncJobHistoryRepository>().Object);

            var backedUpJobs = await azureMaintenanceService.BackupInactiveJobsAsync(jobs);
            Assert.AreEqual(backedUpJobs.Count, jobs.Count);
            purgedSyncJobRepository.Verify(x => x.InsertPurgedSyncJobsAsync(It.IsAny<IEnumerable<PurgedSyncJob>>()), Times.Once());

            jobs = new List<SyncJob>();
            backedUpJobs = await azureMaintenanceService.BackupInactiveJobsAsync(jobs);
            Assert.AreEqual(backedUpJobs.Count, 0);
        }

        [TestMethod]
        public async Task TestRemoveBackups()
        {
            var loggerMock = new Mock<ILoggingRepository>();
            loggerMock.Setup(x => x.LogMessageAsync(It.IsAny<LogMessage>(), It.IsAny<VerbosityLevel>(), It.IsAny<string>(), It.IsAny<string>()))
                      .Returns(Task.CompletedTask);

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
            var groupsRepository = new Mock<IDatabaseGroupsRepository>();
            var channelsRepository = new Mock<IDatabaseChannelsRepository>();
            var purgedSyncJobRepository = new Mock<IDatabasePurgedSyncJobsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var handleInactiveJobsConfig = new Mock<IHandleInactiveJobsConfig>();
            var notificationRepository = new Mock<INotificationRepository>();
            var notificationQueueRepository = new Mock<IServiceBusQueueRepository>();

            purgedSyncJobRepository.Setup(x => x.GetPurgedSyncJobsAsync(It.IsAny<DateTime>())).ReturnsAsync(tables);
            purgedSyncJobRepository.Setup(x => x.DeletePurgedSyncJobsAsync(It.IsAny<IEnumerable<PurgedSyncJob>>())).ReturnsAsync(2);

            var azureMaintenanceService = new AzureMaintenanceService(syncJobRepository.Object,
                                                groupsRepository.Object,
                                                channelsRepository.Object,
                                                purgedSyncJobRepository.Object,
                                                graphGroupRepository.Object,
                                                handleInactiveJobsConfig.Object,
                                                notificationRepository.Object,
                                                notificationQueueRepository.Object,
                                                loggerMock.Object, new Mock<ISyncJobHistoryRepository>().Object);

            var countOfRemovedBackUps = await azureMaintenanceService.RemoveBackupsAsync();
            Assert.AreEqual(countOfRemovedBackUps, tables.Count);
            purgedSyncJobRepository.Verify(x => x.GetPurgedSyncJobsAsync(It.IsAny<DateTime>()), Times.Once());
            purgedSyncJobRepository.Verify(x => x.DeletePurgedSyncJobsAsync(It.IsAny<IEnumerable<PurgedSyncJob>>()), Times.Once());
        }

        [TestMethod]
        public async Task TestRemoveInactiveJobs()
        {
            var loggerMock = new Mock<ILoggingRepository>();
            loggerMock.Setup(x => x.LogMessageAsync(It.IsAny<LogMessage>(), It.IsAny<VerbosityLevel>(), It.IsAny<string>(), It.IsAny<string>()))
                      .Returns(Task.CompletedTask);

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
                    LastRunTime = SqlDateTime.MinValue.Value,
                    RunId = Guid.NewGuid(),
                    MembershipType = "GroupMembership"
                };

                job.Group = new Group
                {
                    SyncJobId = job.Id,
                    GroupId = Guid.NewGuid()
                };
                job.TargetOfficeGroupId = job.Group.GroupId;

                jobs.Add(job);
            }

            var j = GetJobs(jobs);

            var syncJobRepository = new Mock<IDatabaseSyncJobsRepository>();
            var groupsRepository = new Mock<IDatabaseGroupsRepository>();
            var channelsRepository = new Mock<IDatabaseChannelsRepository>();
            var purgedSyncJobRepository = new Mock<IDatabasePurgedSyncJobsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var handleInactiveJobsConfig = new Mock<IHandleInactiveJobsConfig>();
            var notificationRepository = new Mock<INotificationRepository>();
            var notificationQueueRepository = new Mock<IServiceBusQueueRepository>();

            var azureMaintenanceService = new AzureMaintenanceService(syncJobRepository.Object,
                                                groupsRepository.Object,
                                                channelsRepository.Object,
                                                purgedSyncJobRepository.Object,
                                                graphGroupRepository.Object,
                                                handleInactiveJobsConfig.Object,
                                                notificationRepository.Object,
                                                notificationQueueRepository.Object,
                                                loggerMock.Object, new Mock<ISyncJobHistoryRepository>().Object);

            await azureMaintenanceService.RemoveInactiveJobsAsync(j);
            syncJobRepository.Verify(x => x.DeleteSyncJobsAsync(It.IsAny<IEnumerable<SyncJob>>()), Times.Once());
        }

        [TestMethod]
        public async Task TestExpireNotifications()
        {
            var loggerMock = new Mock<ILoggingRepository>();
            loggerMock.Setup(x => x.LogMessageAsync(It.IsAny<LogMessage>(), It.IsAny<VerbosityLevel>(), It.IsAny<string>(), It.IsAny<string>()))
                      .Returns(Task.CompletedTask);

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
                    LastRunTime = DateTime.FromFileTimeUtc(0),
                    RunId = Guid.NewGuid(),
                    MembershipType = "GroupMembership"
                };

                job.Group = new Group
                {
                    SyncJobId = job.Id,
                    GroupId = Guid.NewGuid()
                };
                job.TargetOfficeGroupId = job.Group.GroupId;

                jobs.Add(job);
            }

            var j = GetJobs(jobs);

            var syncJobRepository = new Mock<IDatabaseSyncJobsRepository>();
            var groupsRepository = new Mock<IDatabaseGroupsRepository>();
            var channelsRepository = new Mock<IDatabaseChannelsRepository>();
            var purgedSyncJobRepository = new Mock<IDatabasePurgedSyncJobsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var handleInactiveJobsConfig = new Mock<IHandleInactiveJobsConfig>();
            var notificationRepository = new Mock<INotificationRepository>();
            var notificationQueueRepository = new Mock<IServiceBusQueueRepository>();

            var notification = new ThresholdNotification
            {
                ChangePercentageForAdditions = Random.Shared.Next(51, 100),
                ChangePercentageForRemovals = Random.Shared.Next(51, 100),
                ChangeQuantityForAdditions = Random.Shared.Next(50, 1000),
                ChangeQuantityForRemovals = Random.Shared.Next(50, 1000),
                CreatedTime = DateTime.UtcNow,
                Resolution = ThresholdNotificationResolution.Unresolved,
                Id = Guid.NewGuid(),
                ResolvedBy = string.Empty,
                ResolvedTime = DateTime.UtcNow,
                Status = ThresholdNotificationStatus.AwaitingResponse,
                TargetOfficeGroupId = Guid.NewGuid(),
                ThresholdPercentageForAdditions = Random.Shared.Next(1, 50),
                ThresholdPercentageForRemovals = Random.Shared.Next(1, 50),
                CardState = ThresholdNotificationCardState.DefaultCard
            };
            var azureMaintenanceService = new AzureMaintenanceService(syncJobRepository.Object,
                                    groupsRepository.Object,
                                    channelsRepository.Object,
                                    purgedSyncJobRepository.Object,
                                    graphGroupRepository.Object,
                                    handleInactiveJobsConfig.Object,
                                    notificationRepository.Object,
                                    notificationQueueRepository.Object,
                                    loggerMock.Object, new Mock<ISyncJobHistoryRepository>().Object);

            notificationRepository.Setup(x => x.GetThresholdNotificationBySyncJobIdAsync(It.IsAny<Guid>())).Returns(() => Task.FromResult(notification));
            await azureMaintenanceService.ExpireNotificationsAsync(j);
            notificationRepository.Verify(x => x.SaveNotificationAsync(It.IsAny<ThresholdNotification>()), Times.Exactly(2));
        }

        [TestMethod]
        public async Task TestGetGroupName()
        {
            var loggerMock = new Mock<ILoggingRepository>();
            loggerMock.Setup(x => x.LogMessageAsync(It.IsAny<LogMessage>(), It.IsAny<VerbosityLevel>(), It.IsAny<string>(), It.IsAny<string>()))
                      .Returns(Task.CompletedTask);

            var syncJobRepository = new Mock<IDatabaseSyncJobsRepository>();
            var groupsRepository = new Mock<IDatabaseGroupsRepository>();
            var channelsRepository = new Mock<IDatabaseChannelsRepository>();
            var purgedSyncJobRepository = new Mock<IDatabasePurgedSyncJobsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var handleInactiveJobsConfig = new Mock<IHandleInactiveJobsConfig>();
            var notificationRepository = new Mock<INotificationRepository>();
            var notificationQueueRepository = new Mock<IServiceBusQueueRepository>();

            graphGroupRepository.Setup(x => x.GetGroupNameAsync(It.IsAny<Guid>())).ReturnsAsync(() => "Test Group");

            var azureMaintenanceService = new AzureMaintenanceService(syncJobRepository.Object,
                                                groupsRepository.Object,
                                                channelsRepository.Object,
                                                purgedSyncJobRepository.Object,
                                                graphGroupRepository.Object,
                                                handleInactiveJobsConfig.Object,
                                                notificationRepository.Object,
                                                notificationQueueRepository.Object,
                                                loggerMock.Object, new Mock<ISyncJobHistoryRepository>().Object);

            await azureMaintenanceService.GetGroupNameAsync(Guid.NewGuid());
            graphGroupRepository.Verify(x => x.GetGroupNameAsync(It.IsAny<Guid>()), Times.Once());
        }

        [TestMethod]
        public async Task TestGetSyncJobs()
        {
            var loggerMock = new Mock<ILoggingRepository>();
            loggerMock.Setup(x => x.LogMessageAsync(It.IsAny<LogMessage>(), It.IsAny<VerbosityLevel>(), It.IsAny<string>(), It.IsAny<string>()))
                      .Returns(Task.CompletedTask);

            var jobList = new List<SyncJob>();
            var job = new SyncJob
            {
                Requestor = "requestor@email.com",
                Id = Guid.NewGuid(),
                Period = 6,
                Query = "[{ \"type\": \"GroupMembership\", \"source\": \"da144736-962b-4879-a304-acd9f5221e78\"}]",
                StartDate = DateTime.UtcNow.AddDays(-1),
                Status = SyncStatus.CustomerPaused.ToString(),
                LastRunTime = SqlDateTime.MinValue.Value,
                RunId = Guid.NewGuid(),
                MembershipType = "GroupMembership"
            };
            job.Group = new Group
            {
                SyncJobId = job.Id,
                GroupId = Guid.NewGuid()
            };
            job.TargetOfficeGroupId = job.Group.GroupId;
            jobList.Add(job);

            var syncJobRepository = new Mock<IDatabaseSyncJobsRepository>();
            var groupsRepository = new Mock<IDatabaseGroupsRepository>();
            var channelsRepository = new Mock<IDatabaseChannelsRepository>();
            var purgedSyncJobRepository = new Mock<IDatabasePurgedSyncJobsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var handleInactiveJobsConfig = new Mock<IHandleInactiveJobsConfig>();
            var notificationRepository = new Mock<INotificationRepository>();
            var notificationQueueRepository = new Mock<IServiceBusQueueRepository>();

            var azureMaintenanceService = new AzureMaintenanceService(syncJobRepository.Object,
                                                groupsRepository.Object,
                                                channelsRepository.Object,
                                                purgedSyncJobRepository.Object,
                                                graphGroupRepository.Object,
                                                handleInactiveJobsConfig.Object,
                                                notificationRepository.Object,
                                                notificationQueueRepository.Object,
                                                loggerMock.Object, new Mock<ISyncJobHistoryRepository>().Object);

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
            loggerMock.Setup(x => x.LogMessageAsync(It.IsAny<LogMessage>(), It.IsAny<VerbosityLevel>(), It.IsAny<string>(), It.IsAny<string>()))
                      .Returns(Task.CompletedTask);

            var job = new SyncJob
            {
                Requestor = $"requestor@email.com",
                Id = Guid.NewGuid(),
                Period = 6,
                Query = "[{ \"type\": \"GroupMembership\", \"source\": [\"da144736-962b-4879-a304-acd9f5221e78\"]}]",
                StartDate = DateTime.UtcNow.AddDays(-1),
                Status = SyncStatus.CustomerPaused.ToString(),
                LastRunTime = SqlDateTime.MinValue.Value,
                RunId = Guid.NewGuid(),
                MembershipType = "GroupMembership"
            };
            job.Group = new Group
            {
                SyncJobId = job.Id,
                GroupId = Guid.NewGuid()
            };
            job.TargetOfficeGroupId = job.Group.GroupId;

            var purgedJob = new PurgedSyncJob
            {
                Requestor = $"requestor@email.com",
                Id = Guid.NewGuid(),
                Period = 6,
                Query = "[{ \"type\": \"GroupMembership\", \"source\": [\"da144736-962b-4879-a304-acd9f5221e78\"]}]",
                StartDate = DateTime.UtcNow.AddDays(-1),
                Status = SyncStatus.CustomerPaused.ToString(),
                LastRunTime = SqlDateTime.MinValue.Value,
                RunId = Guid.NewGuid(),
                TargetOfficeGroupId = job.Group.GroupId
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
            var groupsRepository = new Mock<IDatabaseGroupsRepository>();
            var channelsRepository = new Mock<IDatabaseChannelsRepository>();
            var purgedSyncJobRepository = new Mock<IDatabasePurgedSyncJobsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var handleInactiveJobsConfig = new Mock<IHandleInactiveJobsConfig>();
            var notificationRepository = new Mock<INotificationRepository>();
            var notificationQueueRepository = new Mock<IServiceBusQueueRepository>();

            _ = graphGroupRepository.Setup(x => x.GetGroupOwnersAsync(job.Group.GroupId, 0)).ReturnsAsync(users);

            var azureMaintenanceService = new AzureMaintenanceService(syncJobRepository.Object,
                                                groupsRepository.Object,
                                                channelsRepository.Object,
                                                purgedSyncJobRepository.Object,
                                                graphGroupRepository.Object,
                                                handleInactiveJobsConfig.Object,
                                                notificationRepository.Object,
                                                notificationQueueRepository.Object,
                                                loggerMock.Object, new Mock<ISyncJobHistoryRepository>().Object);

            await azureMaintenanceService.SendPurgingEmailAsync(purgedJob, Models.Notifications.NotificationMessageType.InactiveSyncJobNotification);
            notificationQueueRepository.Verify(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>()), Times.Once());
        }

        [TestMethod]
        public async Task TestGetJobsApproachingPurging()
        {
            var loggerMock = new Mock<ILoggingRepository>();
            loggerMock.Setup(x => x.LogMessageAsync(It.IsAny<LogMessage>(), It.IsAny<VerbosityLevel>(), It.IsAny<string>(), It.IsAny<string>()))
                      .Returns(Task.CompletedTask);

            var activeSyncJobs = new List<SyncJob>();

            // Configure purging period to 30 days and warning period to 7 days
            // This means warningCutOffDate = DateTime.UtcNow.Date.AddDays(7-30) = DateTime.UtcNow.Date.AddDays(-23)
            var warningCutOffDate = DateTime.UtcNow.Date.AddDays(-23);

            // Add job that should receive warning (last run exactly at warning threshold)
            var jobNeedingWarning = new SyncJob
            {
                Id = Guid.NewGuid(),
                Status = SyncStatus.CustomerPaused.ToString(),
                LastRunTime = warningCutOffDate, // Exactly matches warning cutoff date
                Requestor = "test@email.com",
                MembershipType = "GroupMembership"
            };
            jobNeedingWarning.Group = new Group
            {
                SyncJobId = jobNeedingWarning.Id,
                GroupId = Guid.NewGuid()
            };
            jobNeedingWarning.TargetOfficeGroupId = jobNeedingWarning.Group.GroupId;
            activeSyncJobs.Add(jobNeedingWarning);

            // Add job that already got warning (different date)
            var jobAlreadyWarned = new SyncJob
            {
                Id = Guid.NewGuid(),
                Status = SyncStatus.CustomerPaused.ToString(),
                LastRunTime = warningCutOffDate.AddDays(-1), // One day before warning cutoff
                Requestor = "test2@email.com",
                MembershipType = "GroupMembership"
            };
            jobAlreadyWarned.Group = new Group
            {
                SyncJobId = jobAlreadyWarned.Id,
                GroupId = Guid.NewGuid()
            };
            jobAlreadyWarned.TargetOfficeGroupId = jobAlreadyWarned.Group.GroupId;
            activeSyncJobs.Add(jobAlreadyWarned);

            // Add job that is too recent
            var jobTooRecent = new SyncJob
            {
                Id = Guid.NewGuid(),
                Status = SyncStatus.CustomerPaused.ToString(),
                LastRunTime = DateTime.UtcNow.Date, // Today - too recent for warning
                Requestor = "test3@email.com",
                MembershipType = "GroupMembership"
            };
            jobTooRecent.Group = new Group
            {
                SyncJobId = jobTooRecent.Id,
                GroupId = Guid.NewGuid()
            };
            jobTooRecent.TargetOfficeGroupId = jobTooRecent.Group.GroupId;
            activeSyncJobs.Add(jobTooRecent);

            // Add job from future date (should not match)
            var jobFromFuture = new SyncJob
            {
                Id = Guid.NewGuid(),
                Status = SyncStatus.CustomerPaused.ToString(),
                LastRunTime = warningCutOffDate.AddDays(1), // One day after warning cutoff
                Requestor = "test4@email.com",
                MembershipType = "GroupMembership"
            };
            jobFromFuture.Group = new Group
            {
                SyncJobId = jobFromFuture.Id,
                GroupId = Guid.NewGuid()
            };
            jobFromFuture.TargetOfficeGroupId = jobFromFuture.Group.GroupId;
            activeSyncJobs.Add(jobFromFuture);

            var syncJobRepository = new Mock<IDatabaseSyncJobsRepository>();
            var groupsRepository = new Mock<IDatabaseGroupsRepository>();
            var channelsRepository = new Mock<IDatabaseChannelsRepository>();
            var purgedSyncJobRepository = new Mock<IDatabasePurgedSyncJobsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var handleInactiveJobsConfig = new Mock<IHandleInactiveJobsConfig>();
            var notificationRepository = new Mock<INotificationRepository>();
            var notificationQueueRepository = new Mock<IServiceBusQueueRepository>();

            // Configure the mock to return our test data
            syncJobRepository.Setup(x => x.GetSyncJobsAsync(It.IsAny<bool>(), It.IsAny<SyncStatus[]>())).ReturnsAsync(activeSyncJobs);

            // Configure purging period to 30 days and warning period to 7 days
            handleInactiveJobsConfig.Setup(x => x.NumberOfDaysBeforePurging).Returns(30);
            handleInactiveJobsConfig.Setup(x => x.NumberOfDaysBeforePurgingToSendWarning).Returns(7);

            var azureMaintenanceService = new AzureMaintenanceService(syncJobRepository.Object,
                                                groupsRepository.Object,
                                                channelsRepository.Object,
                                                purgedSyncJobRepository.Object,
                                                graphGroupRepository.Object,
                                                handleInactiveJobsConfig.Object,
                                                notificationRepository.Object,
                                                notificationQueueRepository.Object,
                                                loggerMock.Object, new Mock<ISyncJobHistoryRepository>().Object);

            var jobsApproachingPurging = await azureMaintenanceService.GetJobsApproachingPurgingAsync();

            // Should return only the job that has LastRunTime exactly matching the warning cutoff date
            Assert.AreEqual(1, jobsApproachingPurging.Count);
            Assert.AreEqual(jobNeedingWarning.Id, jobsApproachingPurging[0].Id);

            syncJobRepository.Verify(x => x.GetSyncJobsAsync(It.IsAny<bool>(), It.IsAny<SyncStatus[]>()), Times.Once());
        }


        [TestMethod]
        public async Task TestPurgeOldHistory()
        {
            var loggerMock = new Mock<ILoggingRepository>();
            loggerMock.Setup(x => x.LogMessageAsync(It.IsAny<LogMessage>(), It.IsAny<VerbosityLevel>(), It.IsAny<string>(), It.IsAny<string>()))
                      .Returns(Task.CompletedTask);

            var syncJobRepository = new Mock<IDatabaseSyncJobsRepository>();
            var groupsRepository = new Mock<IDatabaseGroupsRepository>();
            var channelsRepository = new Mock<IDatabaseChannelsRepository>();
            var purgedSyncJobRepository = new Mock<IDatabasePurgedSyncJobsRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var handleInactiveJobsConfig = new Mock<IHandleInactiveJobsConfig>();
            var notificationRepository = new Mock<INotificationRepository>();
            var notificationQueueRepository = new Mock<IServiceBusQueueRepository>();
            var syncJobHistoryRepository = new Mock<ISyncJobHistoryRepository>();

            handleInactiveJobsConfig.Setup(x => x.JobHistoryRetentionDays).Returns(30);
            syncJobHistoryRepository.Setup(x => x.DeleteOlderThanAsync(It.IsAny<DateTime>())).ReturnsAsync(15);

            var azureMaintenanceService = new AzureMaintenanceService(
                                                syncJobRepository.Object,
                                                groupsRepository.Object,
                                                channelsRepository.Object,
                                                purgedSyncJobRepository.Object,
                                                graphGroupRepository.Object,
                                                handleInactiveJobsConfig.Object,
                                                notificationRepository.Object,
                                                notificationQueueRepository.Object,
                                                loggerMock.Object,
                                                syncJobHistoryRepository.Object);

            var deletedCount = await azureMaintenanceService.PurgeOldHistoryAsync();

            Assert.AreEqual(15, deletedCount);
            syncJobHistoryRepository.Verify(x => x.DeleteOlderThanAsync(It.IsAny<DateTime>()), Times.Once());
            loggerMock.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.Contains("Purged 15 job history records")), It.IsAny<VerbosityLevel>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once());
        }
        public IEnumerable<SyncJob> GetJobs(List<SyncJob> jobs)
        {
            List<SyncJob> j = jobs;
            return j;
        }
    }
}