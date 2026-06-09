// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using BusinessLogic.SyncJobUpdater;
using DIConcreteTypes;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Notifications;
using Models.ServiceBus;
using Models.SyncJobHistory;
using Moq;
using Repositories.Contracts;
using Repositories.Mocks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Services.Contracts;
using Services.Tests.Mocks;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class GraphUpdaterServiceTests
    {
        [TestMethod]
        public async Task GroupExistsTest()
        {
            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var mockGraphGroup = new MockGraphGroupRepository();
            var mockMail = new MockMailRepository();
            var mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com");
            var mockSyncJobs = new MockDatabaseSyncJobRepository();
            var mockGroups = new MockDatabaseGroupsRepository();
            var mockNotificationType = new MockNotificationTypesRepository();
			var mockJobNotification = new MockJobNotificationRepository();
            var mockServiceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            var mockSyncJobStatusService = new Mock<ISyncJobStatusService>();
            var mockSyncJobHistoryRepository = new Mock<ISyncJobHistoryRepository>();

            var graphUpdaterService = new GraphUpdaterService(NullLogger<GraphUpdaterService>.Instance, telemetryClient, mockGraphGroup, mockMail, mailSenders, mockSyncJobs, mockGroups, mockNotificationType, mockJobNotification, mockServiceBusQueueRepository.Object, mockSyncJobStatusService.Object,mockSyncJobHistoryRepository.Object);

            var groupId = Guid.NewGuid();

            mockGraphGroup.GroupsToUsers.Add(groupId, new List<AzureADUser>());

            var response = await graphUpdaterService.GroupExistsAsync(groupId);

            Assert.IsTrue(response);
        }

        [TestMethod]
        public async Task SendEmailTest()
        {
            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var mockGraphGroup = new MockGraphGroupRepository();
            var mockMail = new MockMailRepository();
            var mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com");
            var mockSyncJobs = new MockDatabaseSyncJobRepository();
            var mockGroups = new MockDatabaseGroupsRepository();
            var mockNotificationType = new MockNotificationTypesRepository();
			var mockJobNotification = new MockJobNotificationRepository();
            var mockServiceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            var mockSyncJobStatusService = new Mock<ISyncJobStatusService>();
            var mockSyncJobHistoryRepository = new Mock<ISyncJobHistoryRepository>();

            var graphUpdaterService = new GraphUpdaterService(NullLogger<GraphUpdaterService>.Instance, telemetryClient, mockGraphGroup, mockMail, mailSenders, mockSyncJobs, mockGroups, mockNotificationType, mockJobNotification, mockServiceBusQueueRepository.Object, mockSyncJobStatusService.Object, mockSyncJobHistoryRepository.Object);
            var lastRunTime = DateTime.UtcNow.AddDays(-1);

			var job = new SyncJob { Id = Guid.NewGuid(), Status = SyncStatus.Idle.ToString(), LastRunTime = lastRunTime };

			await mockSyncJobs.AddSyncJobAsync(job);

            var notificationMessageType = NotificationMessageType.SyncCompletedNotification;

            await graphUpdaterService.SendEmailAsync(job, notificationMessageType, new string[0] { });
            mockServiceBusQueueRepository.Verify(x => x.SendMessageAsync(It.Is<ServiceBusMessage>(msg =>
               msg.ApplicationProperties.ContainsKey("MessageType") &&
               msg.ApplicationProperties["MessageType"].ToString() == NotificationMessageType.SyncCompletedNotification.ToString())),
               Times.Exactly(1));
        }
		[TestMethod]
		public async Task VerifyEmailNotSentIfDisabled()
		{
			var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
			var mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com");
			var mockSyncJobs = new MockDatabaseSyncJobRepository();
            var mockGroups = new MockDatabaseGroupsRepository();
            var lastRunTime = DateTime.UtcNow.AddDays(-1);
			var job = new SyncJob { Id = Guid.NewGuid(), Status = SyncStatus.Idle.ToString(), LastRunTime = lastRunTime };
			await mockSyncJobs.AddSyncJobAsync(job);
			var mockGraphGroup = new MockGraphGroupRepository();
			var mockMail = new MockMailRepository();
			var toEmail = "user@domain";
			var notificationName = "SyncCompletedEmailType";
            var notificationMessageType = NotificationMessageType.SyncCompletedNotification;

            var notificationTypeId = 2;
			var mockNotificationTypesData = new Dictionary<string, NotificationType>
			{
				{ notificationName, new NotificationType { Id = notificationTypeId, Disabled = false } }
			};
			var mockNotificationType = new MockNotificationTypesRepository(mockNotificationTypesData);
			var mockJobNotificationData = new Dictionary<(Guid, int), bool>
			{
				{ (job.Id, notificationTypeId), true }
			};
			var mockJobNotification = new MockJobNotificationRepository(mockJobNotificationData);
            var mockServiceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            var mockSyncJobStatusService = new Mock<ISyncJobStatusService>();
            var mockSyncJobHistoryRepository = new Mock<ISyncJobHistoryRepository>();

            var graphUpdaterService = new GraphUpdaterService(NullLogger<GraphUpdaterService>.Instance, telemetryClient, mockGraphGroup, mockMail, mailSenders, mockSyncJobs, mockGroups, mockNotificationType, mockJobNotification, mockServiceBusQueueRepository.Object, mockSyncJobStatusService.Object, mockSyncJobHistoryRepository.Object);
            await graphUpdaterService.SendEmailAsync(job, notificationMessageType, new string[0] { });
            Assert.AreEqual(0, mockMail.SentEmails.Count);
		}

		[TestMethod]
		public async Task VerifyEmailNotSentIfGloballyDisabled()
		{
			var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
			var mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com");
			var mockSyncJobs = new MockDatabaseSyncJobRepository();
            var mockGroups = new MockDatabaseGroupsRepository();
			var lastRunTime = DateTime.UtcNow.AddDays(-1);
			var job = new SyncJob { Id = Guid.NewGuid(), Status = SyncStatus.Idle.ToString(), LastRunTime = lastRunTime };
			await mockSyncJobs.AddSyncJobAsync(job);
			var mockGraphGroup = new MockGraphGroupRepository();
			var mockMail = new MockMailRepository();
			var toEmail = "user@domain";
			var notificationName = "SyncCompletedEmailType";
            var notificationMessageType = NotificationMessageType.SyncCompletedNotification;
			var notificationTypeId = 2;
			var mockNotificationTypesData = new Dictionary<string, NotificationType>
			{
				{ notificationName, new NotificationType { Id = notificationTypeId, Disabled = true } }
			};
			var mockNotificationType = new MockNotificationTypesRepository(mockNotificationTypesData);
			var mockJobNotification = new MockJobNotificationRepository();
            var mockServiceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            var mockSyncJobStatusService = new Mock<ISyncJobStatusService>();
            var mockSyncJobHistoryRepository = new Mock<ISyncJobHistoryRepository>();

            var graphUpdaterService = new GraphUpdaterService(NullLogger<GraphUpdaterService>.Instance, telemetryClient, mockGraphGroup, mockMail, mailSenders, mockSyncJobs, mockGroups, mockNotificationType, mockJobNotification, mockServiceBusQueueRepository.Object, mockSyncJobStatusService.Object, mockSyncJobHistoryRepository.Object); await graphUpdaterService.SendEmailAsync(job, notificationMessageType, new string[0] { });
            Assert.AreEqual(0, mockMail.SentEmails.Count);
		}
		[TestMethod]
        public async Task UpdateSyncJobStatusTest()
        {
            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var mockGraphGroup = new MockGraphGroupRepository();
            var mockMail = new MockMailRepository();
            var mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com");
            var mockSyncJobs = new MockDatabaseSyncJobRepository();
            var mockGroups = new MockDatabaseGroupsRepository();
            var mockNotificationType = new MockNotificationTypesRepository();
			var mockJobNotification = new MockJobNotificationRepository();

            var mockServiceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            var mockSyncJobStatusService = new Mock<ISyncJobStatusService>();
            var mockSyncJobHistoryRepository = new Mock<ISyncJobHistoryRepository>();

            var graphUpdaterService = new GraphUpdaterService(NullLogger<GraphUpdaterService>.Instance, telemetryClient, mockGraphGroup, mockMail, mailSenders, mockSyncJobs, mockGroups, mockNotificationType, mockJobNotification, mockServiceBusQueueRepository.Object, mockSyncJobStatusService.Object, mockSyncJobHistoryRepository.Object);
            var runId = Guid.NewGuid();
            var lastRunTime = DateTime.UtcNow.AddDays(-1);
            var lastSuccessfulStartTime = DateTime.UtcNow.AddMinutes(-30);
            var job = new SyncJob { Id = Guid.NewGuid(), Status = SyncStatus.InProgress.ToString(), LastRunTime = lastRunTime, LastSuccessfulStartTime = lastSuccessfulStartTime };

            await mockSyncJobs.AddSyncJobAsync(job);

            await graphUpdaterService.UpdateSyncJobStatusAsync(job, SyncStatus.Idle, false, runId, null, null);

            Assert.AreEqual(SyncStatus.Idle.ToString(), job.Status);
            Assert.IsTrue(job.LastRunTime > lastRunTime);
            Assert.IsTrue(job.DryRunTimeStamp < lastRunTime);
            Assert.IsNotNull(job.RunId);
            
            mockSyncJobStatusService.Verify(x => x.UpdateJobStatusAsync(
                It.IsAny<SyncJob>(),
                SyncStatus.Idle,
                It.Is<SyncJobHistory>(h => 
                    h.UpdatedByFunction == "GraphUpdater" && 
                    h.RunId == runId &&
                    h.EndTime.HasValue),
                It.IsAny<string?>()),
                Times.Once);
        }

        [TestMethod]
        public async Task UpdateSyncJobStatusDryRunModeTest()
        {
            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var mockGraphGroup = new MockGraphGroupRepository();
            var mockMail = new MockMailRepository();
            var mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com");
            var mockSyncJobs = new MockDatabaseSyncJobRepository();
            var mockGroups = new MockDatabaseGroupsRepository();
            var mockNotificationType = new MockNotificationTypesRepository();
			var mockJobNotification = new MockJobNotificationRepository();
            var mockServiceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            var mockSyncJobStatusService = new Mock<ISyncJobStatusService>();
            var mockSyncJobHistoryRepository = new Mock<ISyncJobHistoryRepository>();

            var graphUpdaterService = new GraphUpdaterService(NullLogger<GraphUpdaterService>.Instance, telemetryClient, mockGraphGroup,mockMail, mailSenders, mockSyncJobs, mockGroups, mockNotificationType, mockJobNotification, mockServiceBusQueueRepository.Object, mockSyncJobStatusService.Object, mockSyncJobHistoryRepository.Object);
            var runId = Guid.NewGuid();
            var lastRunTime = DateTime.UtcNow.AddDays(-1);
            var lastSuccessfulStartTime = DateTime.UtcNow.AddMinutes(-30);
            var job = new SyncJob { Id = Guid.NewGuid(), DryRunTimeStamp = lastRunTime, LastSuccessfulStartTime = lastSuccessfulStartTime };

            await mockSyncJobs.AddSyncJobAsync(job);

            await graphUpdaterService.UpdateSyncJobStatusAsync(job, SyncStatus.Idle, true, runId, null, null);

            Assert.AreEqual(SyncStatus.Idle.ToString(), job.Status);
            Assert.IsTrue(job.DryRunTimeStamp > lastRunTime);
            Assert.IsTrue(job.LastRunTime < lastRunTime);
            Assert.IsNotNull(job.RunId);
            
            mockSyncJobStatusService.Verify(x => x.UpdateJobStatusAsync(
                It.IsAny<SyncJob>(),
                SyncStatus.Idle,
                It.Is<SyncJobHistory>(h => 
                    h.UpdatedByFunction == "GraphUpdater" && 
                    h.RunId == runId &&
                    h.EndTime.HasValue),
                It.IsAny<string?>()),
                Times.Once);
        }

        [TestMethod]
        public async Task GetSyncJobStatusTest()
        {
            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var mockGraphGroup = new MockGraphGroupRepository();
            var mockMail = new MockMailRepository();
            var mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com");
            var mockSyncJobs = new MockDatabaseSyncJobRepository();
            var mockGroups = new MockDatabaseGroupsRepository();
            var mockNotificationType = new MockNotificationTypesRepository();
			var mockJobNotification = new MockJobNotificationRepository();
            var mockServiceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            var mockSyncJobStatusService = new Mock<ISyncJobStatusService>();
            var mockSyncJobHistoryRepository = new Mock<ISyncJobHistoryRepository>();

            var graphUpdaterService = new GraphUpdaterService(NullLogger<GraphUpdaterService>.Instance, telemetryClient, mockGraphGroup, mockMail, mailSenders, mockSyncJobs, mockGroups, mockNotificationType, mockJobNotification, mockServiceBusQueueRepository.Object, mockSyncJobStatusService.Object, mockSyncJobHistoryRepository.Object); var lastRunTime = DateTime.UtcNow.AddDays(-1);
            var job = new SyncJob { Id = Guid.NewGuid(), Status = SyncStatus.InProgress.ToString(), LastRunTime = lastRunTime };

            await mockSyncJobs.AddSyncJobAsync(job);

            var response = await graphUpdaterService.GetSyncJobAsync(job.Id);

            Assert.IsNotNull(response);
            Assert.AreEqual(job.Status, response.Status);
            Assert.AreEqual(job.LastRunTime, response.LastRunTime);
        }

        [TestMethod]
        public async Task GetGroupNameTest()
        {
            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var mockGraphGroup = new Mock<IGraphGroupRepository>();
            var mockMail = new MockMailRepository();
            var mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com");
            var mockSyncJobs = new MockDatabaseSyncJobRepository();
            var mockGroups = new MockDatabaseGroupsRepository();
            var mockNotificationType = new MockNotificationTypesRepository();
			var mockJobNotification = new MockJobNotificationRepository();
            var mockServiceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            var mockSyncJobStatusService = new Mock<ISyncJobStatusService>();
            var mockSyncJobHistoryRepository = new Mock<ISyncJobHistoryRepository>();

            var graphUpdaterService = new GraphUpdaterService(NullLogger<GraphUpdaterService>.Instance, telemetryClient, mockGraphGroup.Object, mockMail, mailSenders, mockSyncJobs, mockGroups, mockNotificationType, mockJobNotification, mockServiceBusQueueRepository.Object, mockSyncJobStatusService.Object, mockSyncJobHistoryRepository.Object);
            var groupName = "MyTestGroup";
            mockGraphGroup.Setup(x => x.GetGroupNameAsync(It.IsAny<Guid>())).ReturnsAsync(groupName);

            var response = await graphUpdaterService.GetGroupNameAsync(Guid.NewGuid());

            Assert.AreEqual(groupName, response);
        }

        private (List<AzureADUser> users,
                 Dictionary<string, int> nonUserGraphObjects,
                 string nextPageUrl) GetPageSampleResponse(int userCount, bool withNextPage)
        {

            string nextPageUrl = null;
            var users = new List<AzureADUser>();
            var nonUserGraphObjects = new Dictionary<string, int>();

            for (int i = 0; i < userCount; i++)
            {
                users.Add(new AzureADUser { ObjectId = Guid.NewGuid() });
            }

            if (withNextPage)
            {
                nextPageUrl = "http://graph.next.page.url";
            }

            return (users, nonUserGraphObjects, nextPageUrl);
        }

        [TestMethod]
        public async Task IsEmailRecipientOwnerOfGroupAsync_ShouldReturnTrue_WhenRecipientIsOwner()
        {
            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var mockGraphGroup = new Mock<IGraphGroupRepository>();
            var mockMail = new MockMailRepository();
            var mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com");
            var mockSyncJobs = new MockDatabaseSyncJobRepository();
            var mockGroups = new MockDatabaseGroupsRepository();
            var mockNotificationType = new MockNotificationTypesRepository();
			var mockJobNotification = new MockJobNotificationRepository();
            var mockServiceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            var mockSyncJobStatusService = new Mock<ISyncJobStatusService>();
            var mockSyncJobHistoryRepository = new Mock<ISyncJobHistoryRepository>();

            var graphUpdaterService = new GraphUpdaterService(NullLogger<GraphUpdaterService>.Instance, telemetryClient, mockGraphGroup.Object, mockMail, mailSenders, mockSyncJobs, mockGroups, mockNotificationType, mockJobNotification, mockServiceBusQueueRepository.Object, mockSyncJobStatusService.Object, mockSyncJobHistoryRepository.Object);
            var groupOwner = "owner@test.com";
            mockGraphGroup.Setup(x => x.IsEmailRecipientOwnerOfGroupAsync(It.IsAny<String>(), It.IsAny<Guid>(), It.IsAny<bool>())).ReturnsAsync(true);

            var response = await graphUpdaterService.IsEmailRecipientOwnerOfGroupAsync(groupOwner, Guid.NewGuid());

            Assert.IsTrue(response);
        }

        [TestMethod]
        public async Task IsEmailRecipientOwnerOfGroupAsync_ShouldReturnFalse_WhenRecipientIsNotOwner()
        {
            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var mockGraphGroup = new Mock<IGraphGroupRepository>();
            var mockMail = new MockMailRepository();
            var mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com");
            var mockSyncJobs = new MockDatabaseSyncJobRepository();
            var mockGroups = new MockDatabaseGroupsRepository();
            var mockNotificationType = new MockNotificationTypesRepository();
			var mockJobNotification = new MockJobNotificationRepository();
            var mockServiceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            var mockSyncJobStatusService = new Mock<ISyncJobStatusService>();
            var mockSyncJobHistoryRepository = new Mock<ISyncJobHistoryRepository>();

            var graphUpdaterService = new GraphUpdaterService(NullLogger<GraphUpdaterService>.Instance, telemetryClient, mockGraphGroup.Object, mockMail, mailSenders, mockSyncJobs, mockGroups, mockNotificationType, mockJobNotification, mockServiceBusQueueRepository.Object, mockSyncJobStatusService.Object, mockSyncJobHistoryRepository.Object);
            var groupOwner = "nonowner@test.com";
            mockGraphGroup.Setup(x => x.IsEmailRecipientOwnerOfGroupAsync(It.IsAny<String>(), It.IsAny<Guid>(), It.IsAny<bool>())).ReturnsAsync(false);

            var response = await graphUpdaterService.IsEmailRecipientOwnerOfGroupAsync(groupOwner, Guid.NewGuid());

            Assert.IsFalse(response);
        }

        [TestMethod]
        public async Task UpdateSyncJobStatusAsync_ShouldCalculateAfterSyncUserCount_WhenStatusIsIdleWithChanges()
        {
            // Arrange
            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var mockGraphGroup = new MockGraphGroupRepository();
            var mockMail = new MockMailRepository();
            var mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com");
            var mockSyncJobs = new MockDatabaseSyncJobRepository();
            var mockGroups = new MockDatabaseGroupsRepository();
            var mockNotificationType = new MockNotificationTypesRepository();
            var mockJobNotification = new MockJobNotificationRepository();
            var mockServiceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            var mockSyncJobStatusService = new Mock<ISyncJobStatusService>();
            var mockSyncJobHistoryRepository = new Mock<ISyncJobHistoryRepository>();

            var runId = Guid.NewGuid();
            var job = new SyncJob { Id = Guid.NewGuid(), Status = SyncStatus.InProgress.ToString(), RunId = runId };
            
            // Setup existing history with BeforeSyncUserCount
            var existingHistory = new SyncJobHistory
            {
                SyncJobId = job.Id,
                RunId = runId,
                BeforeSyncUserCount = 100,
                Status = SyncStatus.InProgress.ToString()
            };
            mockSyncJobHistoryRepository.Setup(x => x.GetByRunIdAsync(runId)).ReturnsAsync(existingHistory);

            await mockSyncJobs.AddSyncJobAsync(job);

            var graphUpdaterService = new GraphUpdaterService(NullLogger<GraphUpdaterService>.Instance, telemetryClient, mockGraphGroup, mockMail, mailSenders, 
                mockSyncJobs, mockGroups, mockNotificationType, mockJobNotification, mockServiceBusQueueRepository.Object, 
                mockSyncJobStatusService.Object, mockSyncJobHistoryRepository.Object);

            // Act
            await graphUpdaterService.UpdateSyncJobStatusAsync(job, SyncStatus.Idle, false, runId, usersAdded: 10, usersRemoved: 5);

            // Assert
            mockSyncJobStatusService.Verify(x => x.UpdateJobStatusAsync(
                It.IsAny<SyncJob>(),
                SyncStatus.Idle,
                It.Is<SyncJobHistory>(h => 
                    h.AfterSyncUserCount == 105 && // 100 + 10 - 5
                    h.UsersAdded == 10 &&
                    h.UsersRemoved == 5),
                It.IsAny<string?>()),
                Times.Once);
        }

        [TestMethod]
        public async Task UpdateSyncJobStatusAsync_ShouldNotCalculateAfterSyncUserCount_WhenStatusIsNotIdle()
        {
            // Arrange
            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var mockGraphGroup = new MockGraphGroupRepository();
            var mockMail = new MockMailRepository();
            var mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com");
            var mockSyncJobs = new MockDatabaseSyncJobRepository();
            var mockGroups = new MockDatabaseGroupsRepository();
            var mockNotificationType = new MockNotificationTypesRepository();
            var mockJobNotification = new MockJobNotificationRepository();
            var mockServiceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            var mockSyncJobStatusService = new Mock<ISyncJobStatusService>();
            var mockSyncJobHistoryRepository = new Mock<ISyncJobHistoryRepository>();

            var runId = Guid.NewGuid();
            var job = new SyncJob { Id = Guid.NewGuid(), Status = SyncStatus.InProgress.ToString(), RunId = runId };

            await mockSyncJobs.AddSyncJobAsync(job);

            var graphUpdaterService = new GraphUpdaterService(NullLogger<GraphUpdaterService>.Instance, telemetryClient, mockGraphGroup, mockMail, mailSenders, 
                mockSyncJobs, mockGroups, mockNotificationType, mockJobNotification, mockServiceBusQueueRepository.Object, 
                mockSyncJobStatusService.Object, mockSyncJobHistoryRepository.Object);

            // Act
            await graphUpdaterService.UpdateSyncJobStatusAsync(job, SyncStatus.InProgress, false, runId, usersAdded: 10, usersRemoved: 5);

            // Assert
            mockSyncJobStatusService.Verify(x => x.UpdateJobStatusAsync(
                It.IsAny<SyncJob>(),
                SyncStatus.InProgress,
                It.Is<SyncJobHistory>(h => h.AfterSyncUserCount == null),
                It.IsAny<string?>()),
                Times.Once);

            // Verify GetByRunIdAsync was never called since status is not Idle
            mockSyncJobHistoryRepository.Verify(x => x.GetByRunIdAsync(It.IsAny<Guid>()), Times.Never);
        }

        [TestMethod]
        public async Task UpdateSyncJobStatusAsync_ShouldReturnNullAfterSyncUserCount_WhenBeforeSyncUserCountIsNull()
        {
            // Arrange
            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var mockGraphGroup = new MockGraphGroupRepository();
            var mockMail = new MockMailRepository();
            var mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com");
            var mockSyncJobs = new MockDatabaseSyncJobRepository();
            var mockGroups = new MockDatabaseGroupsRepository();
            var mockNotificationType = new MockNotificationTypesRepository();
            var mockJobNotification = new MockJobNotificationRepository();
            var mockServiceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            var mockSyncJobStatusService = new Mock<ISyncJobStatusService>();
            var mockSyncJobHistoryRepository = new Mock<ISyncJobHistoryRepository>();

            var runId = Guid.NewGuid();
            var job = new SyncJob { Id = Guid.NewGuid(), Status = SyncStatus.InProgress.ToString(), RunId = runId };
            
            // Setup existing history WITHOUT BeforeSyncUserCount
            var existingHistory = new SyncJobHistory
            {
                SyncJobId = job.Id,
                RunId = runId,
                BeforeSyncUserCount = null, // NULL BeforeSyncUserCount
                Status = SyncStatus.InProgress.ToString()
            };
            mockSyncJobHistoryRepository.Setup(x => x.GetByRunIdAsync(runId)).ReturnsAsync(existingHistory);

            await mockSyncJobs.AddSyncJobAsync(job);

            var graphUpdaterService = new GraphUpdaterService(NullLogger<GraphUpdaterService>.Instance, telemetryClient, mockGraphGroup, mockMail, mailSenders, 
                mockSyncJobs, mockGroups, mockNotificationType, mockJobNotification, mockServiceBusQueueRepository.Object, 
                mockSyncJobStatusService.Object, mockSyncJobHistoryRepository.Object);

            // Act
            await graphUpdaterService.UpdateSyncJobStatusAsync(job, SyncStatus.Idle, false, runId, usersAdded: 10, usersRemoved: 5);

            // Assert
            mockSyncJobStatusService.Verify(x => x.UpdateJobStatusAsync(
                It.IsAny<SyncJob>(),
                SyncStatus.Idle,
                It.Is<SyncJobHistory>(h => h.AfterSyncUserCount == null),
                It.IsAny<string?>()),
                Times.Once);
        }

        [TestMethod]
        public async Task UpdateSyncJobStatusAsync_ShouldKeepSameCount_WhenNoChangesOccur()
        {
            // Arrange
            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var mockGraphGroup = new MockGraphGroupRepository();
            var mockMail = new MockMailRepository();
            var mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com");
            var mockSyncJobs = new MockDatabaseSyncJobRepository();
            var mockGroups = new MockDatabaseGroupsRepository();
            var mockNotificationType = new MockNotificationTypesRepository();
            var mockJobNotification = new MockJobNotificationRepository();
            var mockServiceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            var mockSyncJobStatusService = new Mock<ISyncJobStatusService>();
            var mockSyncJobHistoryRepository = new Mock<ISyncJobHistoryRepository>();

            var runId = Guid.NewGuid();
            var job = new SyncJob { Id = Guid.NewGuid(), Status = SyncStatus.InProgress.ToString(), RunId = runId };
            
            // Setup existing history with BeforeSyncUserCount
            var existingHistory = new SyncJobHistory
            {
                SyncJobId = job.Id,
                RunId = runId,
                BeforeSyncUserCount = 100,
                Status = SyncStatus.InProgress.ToString()
            };
            mockSyncJobHistoryRepository.Setup(x => x.GetByRunIdAsync(runId)).ReturnsAsync(existingHistory);

            await mockSyncJobs.AddSyncJobAsync(job);

            var graphUpdaterService = new GraphUpdaterService(NullLogger<GraphUpdaterService>.Instance, telemetryClient, mockGraphGroup, mockMail, mailSenders, 
                mockSyncJobs, mockGroups, mockNotificationType, mockJobNotification, mockServiceBusQueueRepository.Object, 
                mockSyncJobStatusService.Object, mockSyncJobHistoryRepository.Object);

            // Act - No users added or removed
            await graphUpdaterService.UpdateSyncJobStatusAsync(job, SyncStatus.Idle, false, runId, usersAdded: 0, usersRemoved: 0);

            // Assert - Count should stay the same
            mockSyncJobStatusService.Verify(x => x.UpdateJobStatusAsync(
                It.IsAny<SyncJob>(),
                SyncStatus.Idle,
                It.Is<SyncJobHistory>(h => 
                    h.AfterSyncUserCount == 100 && // Same as BeforeSyncUserCount
                    h.UsersAdded == 0 &&
                    h.UsersRemoved == 0),
                It.IsAny<string?>()),
                Times.Once);
        }

        [TestMethod]
        public async Task UpdateSyncJobStatusAsync_ShouldCalculateCorrectly_WithOnlyAdds()
        {
            // Arrange
            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var mockGraphGroup = new MockGraphGroupRepository();
            var mockMail = new MockMailRepository();
            var mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com");
            var mockSyncJobs = new MockDatabaseSyncJobRepository();
            var mockGroups = new MockDatabaseGroupsRepository();
            var mockNotificationType = new MockNotificationTypesRepository();
            var mockJobNotification = new MockJobNotificationRepository();
            var mockServiceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            var mockSyncJobStatusService = new Mock<ISyncJobStatusService>();
            var mockSyncJobHistoryRepository = new Mock<ISyncJobHistoryRepository>();

            var runId = Guid.NewGuid();
            var job = new SyncJob { Id = Guid.NewGuid(), Status = SyncStatus.InProgress.ToString(), RunId = runId };
            
            var existingHistory = new SyncJobHistory
            {
                SyncJobId = job.Id,
                RunId = runId,
                BeforeSyncUserCount = 50,
                Status = SyncStatus.InProgress.ToString()
            };
            mockSyncJobHistoryRepository.Setup(x => x.GetByRunIdAsync(runId)).ReturnsAsync(existingHistory);

            await mockSyncJobs.AddSyncJobAsync(job);

            var graphUpdaterService = new GraphUpdaterService(NullLogger<GraphUpdaterService>.Instance, telemetryClient, mockGraphGroup, mockMail, mailSenders, 
                mockSyncJobs, mockGroups, mockNotificationType, mockJobNotification, mockServiceBusQueueRepository.Object, 
                mockSyncJobStatusService.Object, mockSyncJobHistoryRepository.Object);

            // Act - Only adds, no removes
            await graphUpdaterService.UpdateSyncJobStatusAsync(job, SyncStatus.Idle, false, runId, usersAdded: 25, usersRemoved: null);

            // Assert
            mockSyncJobStatusService.Verify(x => x.UpdateJobStatusAsync(
                It.IsAny<SyncJob>(),
                SyncStatus.Idle,
                It.Is<SyncJobHistory>(h => 
                    h.AfterSyncUserCount == 75 && // 50 + 25
                    h.UsersAdded == 25 &&
                    h.UsersRemoved == null),
                It.IsAny<string?>()),
                Times.Once);
        }

        [TestMethod]
        public async Task UpdateSyncJobStatusAsync_ShouldCalculateCorrectly_WithOnlyRemoves()
        {
            // Arrange
            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var mockGraphGroup = new MockGraphGroupRepository();
            var mockMail = new MockMailRepository();
            var mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com");
            var mockSyncJobs = new MockDatabaseSyncJobRepository();
            var mockGroups = new MockDatabaseGroupsRepository();
            var mockNotificationType = new MockNotificationTypesRepository();
            var mockJobNotification = new MockJobNotificationRepository();
            var mockServiceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            var mockSyncJobStatusService = new Mock<ISyncJobStatusService>();
            var mockSyncJobHistoryRepository = new Mock<ISyncJobHistoryRepository>();

            var runId = Guid.NewGuid();
            var job = new SyncJob { Id = Guid.NewGuid(), Status = SyncStatus.InProgress.ToString(), RunId = runId };
            
            var existingHistory = new SyncJobHistory
            {
                SyncJobId = job.Id,
                RunId = runId,
                BeforeSyncUserCount = 80,
                Status = SyncStatus.InProgress.ToString()
            };
            mockSyncJobHistoryRepository.Setup(x => x.GetByRunIdAsync(runId)).ReturnsAsync(existingHistory);

            await mockSyncJobs.AddSyncJobAsync(job);

            var graphUpdaterService = new GraphUpdaterService(NullLogger<GraphUpdaterService>.Instance, telemetryClient, mockGraphGroup, mockMail, mailSenders, 
                mockSyncJobs, mockGroups, mockNotificationType, mockJobNotification, mockServiceBusQueueRepository.Object, 
                mockSyncJobStatusService.Object, mockSyncJobHistoryRepository.Object);

            // Act - Only removes, no adds
            await graphUpdaterService.UpdateSyncJobStatusAsync(job, SyncStatus.Idle, false, runId, usersAdded: null, usersRemoved: 30);

            // Assert
            mockSyncJobStatusService.Verify(x => x.UpdateJobStatusAsync(
                It.IsAny<SyncJob>(),
                SyncStatus.Idle,
                It.Is<SyncJobHistory>(h => 
                    h.AfterSyncUserCount == 50 && // 80 - 30
                    h.UsersAdded == null &&
                    h.UsersRemoved == 30),
                It.IsAny<string?>()),
                Times.Once);
        }
    }
}
