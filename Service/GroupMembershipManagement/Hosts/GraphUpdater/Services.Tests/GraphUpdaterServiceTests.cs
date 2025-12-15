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
            var mockLogs = new MockLoggingRepository();
            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var mockGraphGroup = new MockGraphGroupRepository();
            var mockMail = new MockMailRepository();
            var mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com");
            var mockSyncJobs = new MockDatabaseSyncJobRepository();
            var mockGroups = new MockDatabaseGroupsRepository();
            var mockNotificationType = new MockNotificationTypesRepository();
			var mockJobNotification = new MockJobNotificationRepository();
            var mockServiceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            var mockSyncJobStatusService = new Mock<SyncJobStatusService>(null, null);

            var graphUpdaterService = new GraphUpdaterService(mockLogs, telemetryClient, mockGraphGroup, mockMail, mailSenders, mockSyncJobs, mockGroups, mockNotificationType, mockJobNotification, mockServiceBusQueueRepository.Object, mockSyncJobStatusService.Object);

            var groupId = Guid.NewGuid();
            var runId = Guid.NewGuid();

            mockGraphGroup.GroupsToUsers.Add(groupId, new List<AzureADUser>());

            var response = await graphUpdaterService.GroupExistsAsync(groupId, runId);

            Assert.IsTrue(response);
        }

        [TestMethod]
        public async Task SendEmailTest()
        {
            var mockLogs = new MockLoggingRepository();
            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var mockGraphGroup = new MockGraphGroupRepository();
            var mockMail = new MockMailRepository();
            var mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com");
            var mockSyncJobs = new MockDatabaseSyncJobRepository();
            var mockGroups = new MockDatabaseGroupsRepository();
            var mockNotificationType = new MockNotificationTypesRepository();
			var mockJobNotification = new MockJobNotificationRepository();
            var mockServiceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            var mockSyncJobStatusService = new Mock<SyncJobStatusService>(null, null);

            var graphUpdaterService = new GraphUpdaterService(mockLogs, telemetryClient, mockGraphGroup, mockMail, mailSenders, mockSyncJobs, mockGroups, mockNotificationType, mockJobNotification, mockServiceBusQueueRepository.Object, mockSyncJobStatusService.Object);
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
			var mockLogs = new MockLoggingRepository();
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
            var mockSyncJobStatusService = new Mock<SyncJobStatusService>(null, null);

            var graphUpdaterService = new GraphUpdaterService(mockLogs, telemetryClient, mockGraphGroup, mockMail, mailSenders, mockSyncJobs, mockGroups, mockNotificationType, mockJobNotification, mockServiceBusQueueRepository.Object, mockSyncJobStatusService.Object);
            await graphUpdaterService.SendEmailAsync(job, notificationMessageType, new string[0] { });
            Assert.AreEqual(0, mockMail.SentEmails.Count);
		}

		[TestMethod]
		public async Task VerifyEmailNotSentIfGloballyDisabled()
		{
			var mockLogs = new MockLoggingRepository();
			var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
			var mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com");
			var mockSyncJobs = new MockDatabaseSyncJobRepository();
            var mockGroups = new MockDatabaseGroupsRepository();
            var runId = Guid.NewGuid();
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
            var mockSyncJobStatusService = new Mock<SyncJobStatusService>(null, null);

            var graphUpdaterService = new GraphUpdaterService(mockLogs, telemetryClient, mockGraphGroup, mockMail, mailSenders, mockSyncJobs, mockGroups, mockNotificationType, mockJobNotification, mockServiceBusQueueRepository.Object, mockSyncJobStatusService.Object); await graphUpdaterService.SendEmailAsync(job, notificationMessageType, new string[0] { });
            Assert.AreEqual(0, mockMail.SentEmails.Count);
		}
		[TestMethod]
        public async Task UpdateSyncJobStatusTest()
        {
            var mockLogs = new MockLoggingRepository();
            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var mockGraphGroup = new MockGraphGroupRepository();
            var mockMail = new MockMailRepository();
            var mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com");
            var mockSyncJobs = new MockDatabaseSyncJobRepository();
            var mockGroups = new MockDatabaseGroupsRepository();
            var mockNotificationType = new MockNotificationTypesRepository();
			var mockJobNotification = new MockJobNotificationRepository();

            var mockServiceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            var mockSyncJobStatusService = new Mock<SyncJobStatusService>(null, null);

            var graphUpdaterService = new GraphUpdaterService(mockLogs, telemetryClient, mockGraphGroup, mockMail, mailSenders, mockSyncJobs, mockGroups, mockNotificationType, mockJobNotification, mockServiceBusQueueRepository.Object, mockSyncJobStatusService.Object);
            var runId = Guid.NewGuid();
            var lastRunTime = DateTime.UtcNow.AddDays(-1);
            var lastSuccessfulStartTime = DateTime.UtcNow.AddMinutes(-30);
            var job = new SyncJob { Id = Guid.NewGuid(), Status = SyncStatus.InProgress.ToString(), LastRunTime = lastRunTime, LastSuccessfulStartTime = lastSuccessfulStartTime };

            await mockSyncJobs.AddSyncJobAsync(job);

            await graphUpdaterService.UpdateSyncJobStatusAsync(job, SyncStatus.Idle, false, runId);

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
                    h.StartTime == lastSuccessfulStartTime &&
                    h.EndTime.HasValue)),
                Times.Once);
        }

        [TestMethod]
        public async Task UpdateSyncJobStatusDryRunModeTest()
        {
            var mockLogs = new MockLoggingRepository();
            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var mockGraphGroup = new MockGraphGroupRepository();
            var mockMail = new MockMailRepository();
            var mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com");
            var mockSyncJobs = new MockDatabaseSyncJobRepository();
            var mockGroups = new MockDatabaseGroupsRepository();
            var mockNotificationType = new MockNotificationTypesRepository();
			var mockJobNotification = new MockJobNotificationRepository();
            var mockServiceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            var mockSyncJobStatusService = new Mock<SyncJobStatusService>(null, null);

            var graphUpdaterService = new GraphUpdaterService(mockLogs, telemetryClient, mockGraphGroup,mockMail, mailSenders, mockSyncJobs, mockGroups, mockNotificationType, mockJobNotification, mockServiceBusQueueRepository.Object, mockSyncJobStatusService.Object);
            var runId = Guid.NewGuid();
            var lastRunTime = DateTime.UtcNow.AddDays(-1);
            var lastSuccessfulStartTime = DateTime.UtcNow.AddMinutes(-30);
            var job = new SyncJob { Id = Guid.NewGuid(), DryRunTimeStamp = lastRunTime, LastSuccessfulStartTime = lastSuccessfulStartTime };

            await mockSyncJobs.AddSyncJobAsync(job);

            await graphUpdaterService.UpdateSyncJobStatusAsync(job, SyncStatus.Idle, true, runId);

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
                    h.StartTime == lastSuccessfulStartTime &&
                    h.EndTime.HasValue)),
                Times.Once);
        }

        [TestMethod]
        public async Task GetSyncJobStatusTest()
        {
            var mockLogs = new MockLoggingRepository();
            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var mockGraphGroup = new MockGraphGroupRepository();
            var mockMail = new MockMailRepository();
            var mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com");
            var mockSyncJobs = new MockDatabaseSyncJobRepository();
            var mockGroups = new MockDatabaseGroupsRepository();
            var mockNotificationType = new MockNotificationTypesRepository();
			var mockJobNotification = new MockJobNotificationRepository();
            var mockServiceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            var mockSyncJobStatusService = new Mock<SyncJobStatusService>(null, null);

            var graphUpdaterService = new GraphUpdaterService(mockLogs, telemetryClient, mockGraphGroup, mockMail, mailSenders, mockSyncJobs, mockGroups, mockNotificationType, mockJobNotification, mockServiceBusQueueRepository.Object, mockSyncJobStatusService.Object); var lastRunTime = DateTime.UtcNow.AddDays(-1);
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
            var mockLogs = new MockLoggingRepository();
            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var mockGraphGroup = new Mock<IGraphGroupRepository>();
            var mockMail = new MockMailRepository();
            var mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com");
            var mockSyncJobs = new MockDatabaseSyncJobRepository();
            var mockGroups = new MockDatabaseGroupsRepository();
            var mockNotificationType = new MockNotificationTypesRepository();
			var mockJobNotification = new MockJobNotificationRepository();
            var mockServiceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            var mockSyncJobStatusService = new Mock<SyncJobStatusService>(null, null);

            var graphUpdaterService = new GraphUpdaterService(mockLogs, telemetryClient, mockGraphGroup.Object, mockMail, mailSenders, mockSyncJobs, mockGroups, mockNotificationType, mockJobNotification, mockServiceBusQueueRepository.Object, mockSyncJobStatusService.Object);
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
            var mockLogs = new MockLoggingRepository();
            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var mockGraphGroup = new Mock<IGraphGroupRepository>();
            var mockMail = new MockMailRepository();
            var mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com");
            var mockSyncJobs = new MockDatabaseSyncJobRepository();
            var mockGroups = new MockDatabaseGroupsRepository();
            var mockNotificationType = new MockNotificationTypesRepository();
			var mockJobNotification = new MockJobNotificationRepository();
            var mockServiceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            var mockSyncJobStatusService = new Mock<SyncJobStatusService>(null, null);

            var graphUpdaterService = new GraphUpdaterService(mockLogs, telemetryClient, mockGraphGroup.Object, mockMail, mailSenders, mockSyncJobs, mockGroups, mockNotificationType, mockJobNotification, mockServiceBusQueueRepository.Object, mockSyncJobStatusService.Object);
            var groupOwner = "owner@test.com";
            mockGraphGroup.Setup(x => x.IsEmailRecipientOwnerOfGroupAsync(It.IsAny<String>(), It.IsAny<Guid>())).ReturnsAsync(true);

            var response = await graphUpdaterService.IsEmailRecipientOwnerOfGroupAsync(groupOwner, Guid.NewGuid());

            Assert.IsTrue(response);
        }

        [TestMethod]
        public async Task IsEmailRecipientOwnerOfGroupAsync_ShouldReturnFalse_WhenRecipientIsNotOwner()
        {
            var mockLogs = new MockLoggingRepository();
            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var mockGraphGroup = new Mock<IGraphGroupRepository>();
            var mockMail = new MockMailRepository();
            var mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com");
            var mockSyncJobs = new MockDatabaseSyncJobRepository();
            var mockGroups = new MockDatabaseGroupsRepository();
            var mockNotificationType = new MockNotificationTypesRepository();
			var mockJobNotification = new MockJobNotificationRepository();
            var mockServiceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            var mockSyncJobStatusService = new Mock<SyncJobStatusService>(null, null);

            var graphUpdaterService = new GraphUpdaterService(mockLogs, telemetryClient, mockGraphGroup.Object, mockMail, mailSenders, mockSyncJobs, mockGroups, mockNotificationType, mockJobNotification, mockServiceBusQueueRepository.Object, mockSyncJobStatusService.Object);
            var groupOwner = "nonowner@test.com";
            mockGraphGroup.Setup(x => x.IsEmailRecipientOwnerOfGroupAsync(It.IsAny<String>(), It.IsAny<Guid>())).ReturnsAsync(false);

            var response = await graphUpdaterService.IsEmailRecipientOwnerOfGroupAsync(groupOwner, Guid.NewGuid());

            Assert.IsFalse(response);
        }
    }
}
