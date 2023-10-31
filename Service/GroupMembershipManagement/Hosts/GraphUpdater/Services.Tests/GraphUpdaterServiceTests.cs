// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using DIConcreteTypes;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Graph;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Models;
using Repositories.Contracts;
using Repositories.Mocks;
using Services.Tests.Mocks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using Models.ServiceBus;
using Repositories.Contracts.InjectConfig;
using Repositories.GraphGroups;
using Repositories.Logging;
using Repositories.EntityFramework.Contexts.Migrations;
using Services.Contracts;
using Microsoft.Graph.Models.Security;
using Microsoft.IdentityModel.Abstractions;
using System.Security.Cryptography;

namespace Services.Tests
{
    [TestClass]
    public class GraphUpdaterServiceTests
    {
        [TestMethod]
        public async Task GetFirstMembersPageTest()
        {
            var mockLogs = new MockLoggingRepository();
            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var mockGraphGroup = new Mock<IGraphGroupRepository>();
            var mockMail = new MockMailRepository();
            var mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com", "recipient@domain.com", "recipient@domain.com");
            var mockSyncJobs = new MockDatabaseSyncJobRepository();
            var mockNotificationType = new MockNotificationTypesRepository();
            var mockDisabledJobNotification = new MockDisabledJobNotificationRepository();
            var samplePageResponse = GetPageSampleResponse(100, true);
            var userCount = 100;
            mockGraphGroup.Setup(x => x.GetFirstTransitiveMembersPageAsync(It.IsAny<Guid>())).ReturnsAsync(samplePageResponse);
            mockGraphGroup.SetupAllProperties();

            var graphUpdaterService = new GraphUpdaterService(mockLogs, telemetryClient, mockGraphGroup.Object, mockMail, mailSenders, mockSyncJobs, mockNotificationType, mockDisabledJobNotification);

            var groupId = Guid.NewGuid();
            var runId = Guid.NewGuid();
            graphUpdaterService.RunId = runId;

            var response = await graphUpdaterService.GetFirstMembersPageAsync(groupId, runId);

            Assert.IsNotNull(response.NextPageUrl);
            Assert.AreEqual(userCount, response.Members.Count);
            Assert.AreNotEqual(Guid.Empty, mockGraphGroup.Object.RunId);
            Assert.AreEqual(graphUpdaterService.RunId, mockGraphGroup.Object.RunId);

        }

        [TestMethod]
        public async Task GetNextMembersPageTest()
        {
            var mockLogs = new MockLoggingRepository();
            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var mockGraphGroup = new Mock<IGraphGroupRepository>();
            var mockMail = new MockMailRepository();
            var mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com", "recipient@domain.com", "recipient@domain.com");
            var mockSyncJobs = new MockDatabaseSyncJobRepository();
			var mockNotificationType = new MockNotificationTypesRepository();
			var mockDisabledJobNotification = new MockDisabledJobNotificationRepository();
			var samplePageResponse = GetPageSampleResponse(100, true);
            var userCount = 100;
            mockGraphGroup.Setup(x => x.GetNextTransitiveMembersPageAsync(It.IsAny<string>())).ReturnsAsync(samplePageResponse);

            var graphUpdaterService = new GraphUpdaterService(mockLogs, telemetryClient, mockGraphGroup.Object, mockMail, mailSenders, mockSyncJobs, mockNotificationType, mockDisabledJobNotification);

            var groupId = Guid.NewGuid();
            var runId = Guid.NewGuid();
            var nextPageUrl = samplePageResponse.nextPageUrl;

            var response = await graphUpdaterService.GetNextMembersPageAsync(nextPageUrl, runId);
            Assert.IsNotNull(response.NextPageUrl);
            Assert.AreEqual(userCount, response.Members.Count);
        }

        [TestMethod]
        public async Task GroupExistsTest()
        {
            var mockLogs = new MockLoggingRepository();
            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var mockGraphGroup = new MockGraphGroupRepository();
            var mockMail = new MockMailRepository();
            var mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com", "recipient@domain.com", "recipient@domain.com");
            var mockSyncJobs = new MockDatabaseSyncJobRepository();
			var mockNotificationType = new MockNotificationTypesRepository();
			var mockDisabledJobNotification = new MockDisabledJobNotificationRepository();
			var graphUpdaterService = new GraphUpdaterService(mockLogs, telemetryClient, mockGraphGroup, mockMail, mailSenders, mockSyncJobs,mockNotificationType,mockDisabledJobNotification);

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
            var mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com", "recipient@domain.com", "recipient@domain.com");
            var mockSyncJobs = new MockDatabaseSyncJobRepository();
			var mockNotificationType = new MockNotificationTypesRepository();
			var mockDisabledJobNotification = new MockDisabledJobNotificationRepository();
			var graphUpdaterService = new GraphUpdaterService(mockLogs, telemetryClient, mockGraphGroup, mockMail, mailSenders, mockSyncJobs, mockNotificationType, mockDisabledJobNotification);
			var lastRunTime = DateTime.UtcNow.AddDays(-1);

			var job = new SyncJob { Id = Guid.NewGuid(), Status = SyncStatus.Idle.ToString(), LastRunTime = lastRunTime };

			await mockSyncJobs.AddSyncJobAsync(job);
			var toEmail = "user@domain";
            var template = "SampleTemplate";

            await graphUpdaterService.SendEmailAsync(toEmail, template, new string[0] { }, job);

            Assert.AreEqual(1, mockMail.SentEmails.Count);
        }
		[TestMethod]
		public async Task VerifyEmailNotSentIfDisabled()
		{
			var mockLogs = new MockLoggingRepository();
			var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
			var mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com", "recipient@domain.com", "recipient@domain.com");
			var mockSyncJobs = new MockDatabaseSyncJobRepository();
			var runId = Guid.NewGuid();
			var lastRunTime = DateTime.UtcNow.AddDays(-1);
			var job = new SyncJob { Id = Guid.NewGuid(), Status = SyncStatus.Idle.ToString(), LastRunTime = lastRunTime };

			await mockSyncJobs.AddSyncJobAsync(job);

			var mockGraphGroup = new MockGraphGroupRepository();
			var mockMail = new MockMailRepository();
			var toEmail = "user@domain";
			var notificationName = "SyncCompletedEmailType";
			var notificationTypeId = 1;
			var mockNotificationTypesData = new Dictionary<string, NotificationType>
			{
				{ notificationName, new NotificationType { Id = notificationTypeId, Disabled = false } }
			};
			var mockNotificationType = new MockNotificationTypesRepository(mockNotificationTypesData);

			var mockDisabledJobNotificationData = new Dictionary<(Guid, int), bool>
			{
				{ (job.Id, notificationTypeId), true }
			};
			var mockDisabledJobNotification = new MockDisabledJobNotificationRepository(mockDisabledJobNotificationData);
			var graphUpdaterService = new GraphUpdaterService(mockLogs, telemetryClient, mockGraphGroup, mockMail, mailSenders, mockSyncJobs, mockNotificationType, mockDisabledJobNotification);

			await graphUpdaterService.SendEmailAsync(toEmail, notificationName, new string[0] { }, job);
			Assert.AreEqual(0, mockMail.SentEmails.Count);
		}
		[TestMethod]
        public async Task UpdateSyncJobStatusTest()
        {
            var mockLogs = new MockLoggingRepository();
            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var mockGraphGroup = new MockGraphGroupRepository();
            var mockMail = new MockMailRepository();
            var mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com", "recipient@domain.com", "recipient@domain.com");
            var mockSyncJobs = new MockDatabaseSyncJobRepository();
			var mockNotificationType = new MockNotificationTypesRepository();
			var mockDisabledJobNotification = new MockDisabledJobNotificationRepository();

			var graphUpdaterService = new GraphUpdaterService(mockLogs, telemetryClient, mockGraphGroup, mockMail, mailSenders, mockSyncJobs, mockNotificationType,mockDisabledJobNotification);

            var runId = Guid.NewGuid();
            var lastRunTime = DateTime.UtcNow.AddDays(-1);
            var job = new SyncJob { Id = Guid.NewGuid(), Status = SyncStatus.InProgress.ToString(), LastRunTime = lastRunTime };

            await mockSyncJobs.AddSyncJobAsync(job);

            await graphUpdaterService.UpdateSyncJobStatusAsync(job, SyncStatus.Idle, false, runId);

            Assert.AreEqual(SyncStatus.Idle.ToString(), job.Status);
            Assert.IsTrue(job.LastRunTime > lastRunTime);
            Assert.IsTrue(job.DryRunTimeStamp < lastRunTime);
            Assert.IsNotNull(job.RunId);
        }

        [TestMethod]
        public async Task UpdateSyncJobStatusDryRunModeTest()
        {
            var mockLogs = new MockLoggingRepository();
            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var mockGraphGroup = new MockGraphGroupRepository();
            var mockMail = new MockMailRepository();
            var mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com", "recipient@domain.com", "recipient@domain.com");
            var mockSyncJobs = new MockDatabaseSyncJobRepository();
			var mockNotificationType = new MockNotificationTypesRepository();
			var mockDisabledJobNotification = new MockDisabledJobNotificationRepository();
			var graphUpdaterService = new GraphUpdaterService(mockLogs, telemetryClient, mockGraphGroup, mockMail, mailSenders, mockSyncJobs, mockNotificationType,mockDisabledJobNotification);

            var runId = Guid.NewGuid();
            var lastRunTime = DateTime.UtcNow.AddDays(-1);
            var job = new SyncJob { Id = Guid.NewGuid(), DryRunTimeStamp = lastRunTime };

            await mockSyncJobs.AddSyncJobAsync(job);

            await graphUpdaterService.UpdateSyncJobStatusAsync(job, SyncStatus.Idle, true, runId);

            Assert.AreEqual(SyncStatus.Idle.ToString(), job.Status);
            Assert.IsTrue(job.DryRunTimeStamp > lastRunTime);
            Assert.IsTrue(job.LastRunTime < lastRunTime);
            Assert.IsNotNull(job.RunId);
        }

        [TestMethod]
        public async Task GetSyncJobStatusTest()
        {
            var mockLogs = new MockLoggingRepository();
            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var mockGraphGroup = new MockGraphGroupRepository();
            var mockMail = new MockMailRepository();
            var mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com", "recipient@domain.com", "recipient@domain.com");
            var mockSyncJobs = new MockDatabaseSyncJobRepository();
			var mockNotificationType = new MockNotificationTypesRepository();
			var mockDisabledJobNotification = new MockDisabledJobNotificationRepository();
			var graphUpdaterService = new GraphUpdaterService(mockLogs, telemetryClient, mockGraphGroup, mockMail, mailSenders, mockSyncJobs,mockNotificationType,mockDisabledJobNotification);
            var lastRunTime = DateTime.UtcNow.AddDays(-1);
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
            var mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com", "recipient@domain.com", "recipient@domain.com");
            var mockSyncJobs = new MockDatabaseSyncJobRepository();
			var mockNotificationType = new MockNotificationTypesRepository();
			var mockDisabledJobNotification = new MockDisabledJobNotificationRepository();
			var graphUpdaterService = new GraphUpdaterService(mockLogs, telemetryClient, mockGraphGroup.Object, mockMail, mailSenders, mockSyncJobs, mockNotificationType,mockDisabledJobNotification);

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
            var mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com", "recipient@domain.com", "recipient@domain.com");
            var mockSyncJobs = new MockDatabaseSyncJobRepository();
			var mockNotificationType = new MockNotificationTypesRepository();
			var mockDisabledJobNotification = new MockDisabledJobNotificationRepository();
			var graphUpdaterService = new GraphUpdaterService(mockLogs, telemetryClient, mockGraphGroup.Object, mockMail, mailSenders, mockSyncJobs, mockNotificationType, mockDisabledJobNotification);

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
            var mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com", "recipient@domain.com", "recipient@domain.com");
            var mockSyncJobs = new MockDatabaseSyncJobRepository();
			var mockNotificationType = new MockNotificationTypesRepository();
			var mockDisabledJobNotification = new MockDisabledJobNotificationRepository();
			var graphUpdaterService = new GraphUpdaterService(mockLogs, telemetryClient, mockGraphGroup.Object, mockMail, mailSenders, mockSyncJobs, mockNotificationType, mockDisabledJobNotification);

            var groupOwner = "nonowner@test.com";
            mockGraphGroup.Setup(x => x.IsEmailRecipientOwnerOfGroupAsync(It.IsAny<String>(), It.IsAny<Guid>())).ReturnsAsync(false);

            var response = await graphUpdaterService.IsEmailRecipientOwnerOfGroupAsync(groupOwner, Guid.NewGuid());

            Assert.IsFalse(response);
        }
    }
}
