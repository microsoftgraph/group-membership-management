// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using DIConcreteTypes;
using Entities;
using Hosts.GraphUpdater;
using Microsoft.VisualStudio.TestTools.UnitTesting;
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
    public class GroupUpdaterServiceTests
    {
        [TestMethod]
        public async Task AddUsersToGroupInDryRunMode()
        {
            var mockLogs = new MockLoggingRepository();
            var mockGraphGroup = new MockGraphGroupRepository();
            var dryRun = new DryRunValue(true);
            var groupUpdaterService = new GroupUpdaterService(mockLogs, mockGraphGroup, dryRun);

            var runId = Guid.NewGuid();
            var groupId = Guid.NewGuid();
            mockGraphGroup.GroupsToUsers.Add(groupId, new List<AzureADUser>());

            var newUsers = new List<AzureADUser>();
            for (int i = 0; i < 10; i++)
            {
                newUsers.Add(new AzureADUser { ObjectId = Guid.NewGuid() });
            }

            var status = await groupUpdaterService.AddUsersToGroupAsync(newUsers, groupId, runId);

            Assert.AreEqual(GraphUpdaterStatus.Ok, status);
            Assert.AreEqual(0, mockGraphGroup.GroupsToUsers[groupId].Count);
        }

        [TestMethod]
        public async Task AddUsersToGroupInNormalMode()
        {
            var mockLogs = new MockLoggingRepository();
            var mockGraphGroup = new MockGraphGroupRepository();
            var dryRun = new DryRunValue(false);
            var groupUpdaterService = new GroupUpdaterService(mockLogs, mockGraphGroup, dryRun);

            var runId = Guid.NewGuid();
            var groupId = Guid.NewGuid();
            mockGraphGroup.GroupsToUsers.Add(groupId, new List<AzureADUser>());

            var newUsers = new List<AzureADUser>();
            for (int i = 0; i < 10; i++)
            {
                newUsers.Add(new AzureADUser { ObjectId = Guid.NewGuid() });
            }

            var status = await groupUpdaterService.AddUsersToGroupAsync(newUsers, groupId, runId);

            Assert.AreEqual(GraphUpdaterStatus.Ok, status);
            Assert.AreEqual(newUsers.Count, mockGraphGroup.GroupsToUsers[groupId].Count);
        }

        [TestMethod]
        public async Task RemoveUsersToGroupInDryRunMode()
        {
            var mockLogs = new MockLoggingRepository();
            var mockGraphGroup = new MockGraphGroupRepository();
            var dryRun = new DryRunValue(true);
            var groupUpdaterService = new GroupUpdaterService(mockLogs, mockGraphGroup, dryRun);

            var runId = Guid.NewGuid();
            var groupId = Guid.NewGuid();
            var newUsers = new List<AzureADUser>();
            for (int i = 0; i < 10; i++)
            {
                newUsers.Add(new AzureADUser { ObjectId = Guid.NewGuid() });
            }

            mockGraphGroup.GroupsToUsers.Add(groupId, newUsers);

            var status = await groupUpdaterService.RemoveUsersFromGroupAsync(newUsers.Take(5).ToList(), groupId, runId);

            Assert.AreEqual(GraphUpdaterStatus.Ok, status);
            Assert.AreEqual(newUsers.Count, mockGraphGroup.GroupsToUsers[groupId].Count);
        }

        [TestMethod]
        public async Task RemoveUsersToGroupInNormalMode()
        {
            var mockLogs = new MockLoggingRepository();
            var mockGraphGroup = new MockGraphGroupRepository();
            var dryRun = new DryRunValue(false);
            var groupUpdaterService = new GroupUpdaterService(mockLogs, mockGraphGroup, dryRun);

            var runId = Guid.NewGuid();
            var groupId = Guid.NewGuid();
            var newUsers = new List<AzureADUser>();
            for (int i = 0; i < 10; i++)
            {
                newUsers.Add(new AzureADUser { ObjectId = Guid.NewGuid() });
            }

            mockGraphGroup.GroupsToUsers.Add(groupId, new List<AzureADUser>(newUsers));

            var usersToRemove = newUsers.Take(5).ToList();
            var status = await groupUpdaterService.RemoveUsersFromGroupAsync(usersToRemove, groupId, runId);

            Assert.AreEqual(GraphUpdaterStatus.Ok, status);
            Assert.AreEqual(newUsers.Count - usersToRemove.Count, mockGraphGroup.GroupsToUsers[groupId].Count);
        }
    }
}
