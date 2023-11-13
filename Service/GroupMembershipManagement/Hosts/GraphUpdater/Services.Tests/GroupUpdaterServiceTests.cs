// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using DIConcreteTypes;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Repositories.Mocks;
using Services.Entities;
using Services.Tests.Mocks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class GraphUpdaterServiceUpdateGroupsTests
    {
        [TestMethod]
        public async Task AddUsersToGroupInNormalMode()
        {
            var mockLogs = new MockLoggingRepository();
            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var mockGraphGroup = new MockGraphGroupRepository();
            var mockMail = new MockMailRepository();
            var mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com", "recipient@domain.com", "recipient@domain.com");
            var mockSyncJobs = new MockDatabaseSyncJobRepository();
			var mockNotificationType = new MockNotificationTypesRepository();
			var mockJobNotification = new MockJobNotificationRepository();
			var graphUpdaterService = new GraphUpdaterService(mockLogs, telemetryClient, mockGraphGroup, mockMail, mailSenders, mockSyncJobs, mockNotificationType, mockJobNotification);

            var runId = Guid.NewGuid();
            var groupId = Guid.NewGuid();
            mockGraphGroup.GroupsToUsers.Add(groupId, new List<AzureADUser>());
            bool isInitialSync = false;

            var newUsers = new List<AzureADUser>();
            var userCount = 10;

            for (int i = 0; i < userCount; i++)
            {
                newUsers.Add(new AzureADUser { ObjectId = Guid.NewGuid() });
            }

            var status = await graphUpdaterService.AddUsersToGroupAsync(newUsers, groupId, runId, isInitialSync);

            Assert.AreEqual(GraphUpdaterStatus.Ok, status.Status);
            Assert.AreEqual(newUsers.Count, mockGraphGroup.GroupsToUsers[groupId].Count);
        }

        [TestMethod]
        public async Task RemoveUsersToGroupInNormalMode()
        {
            var mockLogs = new MockLoggingRepository();
            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var mockGraphGroup = new MockGraphGroupRepository();
            var mockMail = new MockMailRepository();
            var mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com", "recipient@domain.com", "recipient@domain.com");
            var mockSyncJobs = new MockDatabaseSyncJobRepository();
			var mockNotificationType = new MockNotificationTypesRepository();
			var mockJobNotification = new MockJobNotificationRepository();
			var graphUpdaterService = new GraphUpdaterService(mockLogs, telemetryClient, mockGraphGroup, mockMail, mailSenders, mockSyncJobs, mockNotificationType, mockJobNotification);

            var runId = Guid.NewGuid();
            var groupId = Guid.NewGuid();
            bool isInitialSync = false;

            var newUsers = new List<AzureADUser>();
            for (int i = 0; i < 10; i++)
            {
                newUsers.Add(new AzureADUser { ObjectId = Guid.NewGuid() });
            }

            mockGraphGroup.GroupsToUsers.Add(groupId, new List<AzureADUser>(newUsers));

            var usersToRemove = newUsers.Take(5).ToList();
            var status = await graphUpdaterService.RemoveUsersFromGroupAsync(usersToRemove, groupId, runId, isInitialSync);

            Assert.AreEqual(GraphUpdaterStatus.Ok, status.Status);
            Assert.AreEqual(newUsers.Count - usersToRemove.Count, mockGraphGroup.GroupsToUsers[groupId].Count);
        }

    }
}
