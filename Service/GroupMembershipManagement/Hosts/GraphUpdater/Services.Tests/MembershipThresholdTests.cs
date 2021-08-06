// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Entities.ServiceBus;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using Services.Tests.Mocks;
using Repositories.MembershipDifference;
using DIConcreteTypes;
using System.Threading.Tasks;
using Repositories.Mocks;
using Services.Entities;

namespace Services.Tests
{
    [TestClass]
    public class MembershipThresholdTests
    {
        const string SyncThresholdIncreaseEmailBody = "SyncThresholdIncreaseEmailBody";
        const string SyncThresholdDecreaseEmailBody = "SyncThresholdDecreaseEmailBody";
        const string SyncThresholdBothEmailBody = "SyncThresholdBothEmailBody";

        Guid _rundId;
        Guid _targetGroupId;
        string _partitionKey;
        string _rowKey;
        AzureADGroup[] _sources;
        List<AzureADUser> _users;
        SyncJob _job;
        GroupMembership _membership;

        [TestInitialize]
        public void SetupData()
        {
            _rundId = Guid.NewGuid();
            _targetGroupId = Guid.NewGuid();
            _partitionKey = "2021-1-1";
            _rowKey = Guid.NewGuid().ToString();
            _sources = MockGroupMembershipHelper.CreateMockGroups().Take(1).ToArray();
            _users = MakeUsers(10, 0);

            _membership = new GroupMembership
            {
                Destination = new AzureADGroup { ObjectId = _targetGroupId },
                IsLastMessage = true,
                RunId = _rundId,
                SourceMembers = _users,
                SyncJobPartitionKey = _partitionKey,
                SyncJobRowKey = _rowKey
            };

            _job = new SyncJob
            {
                PartitionKey = _partitionKey,
                RowKey = _rowKey,
                Status = SyncStatus.Idle.ToString(),
                LastRunTime = DateTime.FromFileTimeUtc(0),
                Enabled = true,
                Query = _sources[0].ObjectId.ToString(),
                TargetOfficeGroupId = _targetGroupId,
                ThresholdPercentageForAdditions = 50,
                ThresholdPercentageForRemovals = 50,
                Period = 6,
                Requestor = "user@domain.com",
                RunId = _rundId,
                StartDate = DateTime.UtcNow.Date.AddDays(-10),
                Type = "SecurityGroup"
            };
        }

        [TestMethod]
        public async Task CalculateDeltaForInitialGroupSync()
        {
            var calculator = new MembershipDifferenceCalculator<AzureADUser>();
            var senderRecipients = new EmailSenderRecipient();
            var syncjobRepository = new MockSyncJobRepository();
            var loggingRepository = new MockLoggingRepository();
            var mailRepository = new MockMailRepository();
            var graphUpdaterService = new MockGraphUpdaterService(mailRepository);
            var dryRun = new DryRunValue();
            dryRun.DryRunEnabled = false;

            var deltaCalculator = new DeltaCalculatorService(
                                     calculator,
                                     syncjobRepository,
                                     loggingRepository,
                                     senderRecipients,
                                     dryRun,
                                     graphUpdaterService);


            var targetGroupUsers = new List<AzureADUser>();

            syncjobRepository.ExistingSyncJobs.Add((_partitionKey, _rowKey), _job);
            graphUpdaterService.GroupsToUsers.Add(_sources[0].ObjectId, _users);
            graphUpdaterService.GroupsToUsers.Add(_targetGroupId, targetGroupUsers);
            graphUpdaterService.Groups.Add(_sources[0].ObjectId, new Microsoft.Graph.Group { Id = _sources[0].ObjectId.ToString(),  DisplayName = "Source Group" });
            graphUpdaterService.Groups.Add(_targetGroupId, new Microsoft.Graph.Group { Id = _targetGroupId.ToString(), DisplayName = "Target Group" });

            var response = await deltaCalculator.CalculateDifferenceAsync(_membership, targetGroupUsers);

            Assert.AreEqual(SyncStatus.Idle, response.SyncStatus);
            Assert.AreEqual(_users.Count, response.MembersToAdd.Count);
            Assert.IsTrue(response.IsInitialSync);
            Assert.AreEqual(GraphUpdaterStatus.Ok, response.GraphUpdaterStatus);
        }

        [TestMethod]
        public async Task CalculateDeltaForInitialGroupSyncWithMissingTargetGroup()
        {
            var calculator = new MembershipDifferenceCalculator<AzureADUser>();
            var senderRecipients = new EmailSenderRecipient();

            var syncjobRepository = new MockSyncJobRepository();
            var loggingRepository = new MockLoggingRepository();
            var mailRepository = new MockMailRepository();
            var dryRun = new DryRunValue();
            var graphUpdaterService = new MockGraphUpdaterService(mailRepository);

            var deltaCalculator = new DeltaCalculatorService(
                                    calculator,
                                    syncjobRepository,
                                    loggingRepository,
                                    senderRecipients,
                                    dryRun,
                                    graphUpdaterService);


            syncjobRepository.ExistingSyncJobs.Add((_partitionKey, _rowKey), _job);
            graphUpdaterService.GroupsToUsers.Add(_sources[0].ObjectId, _users);

            var response = await deltaCalculator.CalculateDifferenceAsync(_membership, new List<AzureADUser>());

            Assert.AreEqual(SyncStatus.Error, response.SyncStatus);
            Assert.AreEqual(GraphUpdaterStatus.Error, response.GraphUpdaterStatus);
            Assert.IsTrue(loggingRepository.MessagesLogged.Any(x => x.Message.Contains($"destination group {_membership.Destination} doesn't exist")));
        }

        [TestMethod]
        public async Task CalculateDeltaForNonInitialGroupSyncExceedingIncreaseThreshold()
        {
            var calculator = new MembershipDifferenceCalculator<AzureADUser>();
            var senderRecipients = new EmailSenderRecipient();

            var syncjobRepository = new MockSyncJobRepository();
            var loggingRepository = new MockLoggingRepository();
            var mailRepository = new MockMailRepository();
            var dryRun = new DryRunValue();
            var graphUpdaterService = new MockGraphUpdaterService(mailRepository);

            var deltaCalculator = new DeltaCalculatorService(
                                    calculator,
                                    syncjobRepository,
                                    loggingRepository,
                                    senderRecipients,
                                    dryRun,
                                    graphUpdaterService);

            var targetGroupUsers = new List<AzureADUser>();

            _job.LastRunTime = DateTime.UtcNow.AddDays(-1);

            syncjobRepository.ExistingSyncJobs.Add((_partitionKey, _rowKey), _job);
            graphUpdaterService.GroupsToUsers.Add(_sources[0].ObjectId, _users);
            graphUpdaterService.GroupsToUsers.Add(_targetGroupId, targetGroupUsers);
            graphUpdaterService.Groups.Add(_sources[0].ObjectId, new Microsoft.Graph.Group { Id = _sources[0].ObjectId.ToString(), DisplayName = "Source Group" });
            graphUpdaterService.Groups.Add(_targetGroupId, new Microsoft.Graph.Group { Id = _targetGroupId.ToString(), DisplayName = "Target Group" });

            var response = await deltaCalculator.CalculateDifferenceAsync(_membership, targetGroupUsers);

            var emailMessage = mailRepository.SentEmails.Single();

            Assert.AreEqual(SyncStatus.Idle, response.SyncStatus);
            Assert.AreEqual(GraphUpdaterStatus.ThresholdExceeded, response.GraphUpdaterStatus);
            Assert.AreEqual(targetGroupUsers.Count, graphUpdaterService.GroupsToUsers[_targetGroupId].Count);
            Assert.IsTrue(loggingRepository.MessagesLogged.Any(x => x.Message.Contains("is greater than threshold value")));
            Assert.AreEqual(SyncThresholdIncreaseEmailBody, emailMessage.Content);
            Assert.AreEqual(_targetGroupId.ToString(), emailMessage.AdditionalContentParams[1]);
            Assert.AreEqual(4, emailMessage.AdditionalContentParams.Length);
        }

        [TestMethod]
        public async Task CalculateDeltaForNonInitialGroupSyncExceedingDecreaseThreshold()
        {
            var calculator = new MembershipDifferenceCalculator<AzureADUser>();
            var senderRecipients = new EmailSenderRecipient();

            var syncjobRepository = new MockSyncJobRepository();
            var loggingRepository = new MockLoggingRepository();
            var mailRepository = new MockMailRepository();
            var dryRun = new DryRunValue();
            var graphUpdaterService = new MockGraphUpdaterService(mailRepository);

            var deltaCalculator = new DeltaCalculatorService(
                                    calculator,
                                    syncjobRepository,
                                    loggingRepository,
                                    senderRecipients,
                                    dryRun,
                                    graphUpdaterService);

            var users = _membership.SourceMembers;
            _membership.SourceMembers = users.Take(2).ToList();
            _job.LastRunTime = DateTime.UtcNow.AddDays(-1);

            var targetGroupUsers = users.Take(6).ToList();

            syncjobRepository.ExistingSyncJobs.Add((_partitionKey, _rowKey), _job);
            graphUpdaterService.GroupsToUsers.Add(_sources[0].ObjectId, _membership.SourceMembers);
            graphUpdaterService.GroupsToUsers.Add(_targetGroupId, targetGroupUsers);
            graphUpdaterService.Groups.Add(_sources[0].ObjectId, new Microsoft.Graph.Group { Id = _sources[0].ObjectId.ToString(), DisplayName = "Source Group" });
            graphUpdaterService.Groups.Add(_targetGroupId, new Microsoft.Graph.Group { Id = _targetGroupId.ToString(), DisplayName = "Target Group" });

            var response = await deltaCalculator.CalculateDifferenceAsync(_membership, targetGroupUsers);

            var emailMessage = mailRepository.SentEmails.Single();

            Assert.AreEqual(SyncStatus.Idle, response.SyncStatus);
            Assert.AreEqual(GraphUpdaterStatus.ThresholdExceeded, response.GraphUpdaterStatus);
            Assert.AreEqual(targetGroupUsers.Count, graphUpdaterService.GroupsToUsers[_targetGroupId].Count);
            Assert.IsTrue(loggingRepository.MessagesLogged.Any(x => x.Message.Contains("is lesser than threshold value")));
            Assert.AreEqual(SyncThresholdDecreaseEmailBody, emailMessage.Content);
            Assert.AreEqual(_targetGroupId.ToString(), emailMessage.AdditionalContentParams[1]);
            Assert.AreEqual(4, emailMessage.AdditionalContentParams.Length);
        }

        [TestMethod]
        public async Task CalculateDeltaForNonInitialGroupSyncExceedingBothThresholds()
        {
            var calculator = new MembershipDifferenceCalculator<AzureADUser>();
            var senderRecipients = new EmailSenderRecipient();

            var syncjobRepository = new MockSyncJobRepository();
            var loggingRepository = new MockLoggingRepository();
            var mailRepository = new MockMailRepository();
            var dryRun = new DryRunValue();
            var graphUpdaterService = new MockGraphUpdaterService(mailRepository);

            var deltaCalculator = new DeltaCalculatorService(
                                    calculator,
                                    syncjobRepository,
                                    loggingRepository,
                                    senderRecipients,
                                    dryRun,
                                    graphUpdaterService);

            var users = _membership.SourceMembers;
            _membership.SourceMembers = users.Take(2).ToList();
            _job.LastRunTime = DateTime.UtcNow.AddDays(-1);

            var targetGroupUsers = users.Skip(2).Take(2).ToList();

            syncjobRepository.ExistingSyncJobs.Add((_partitionKey, _rowKey), _job);
            graphUpdaterService.GroupsToUsers.Add(_sources[0].ObjectId, _membership.SourceMembers);
            graphUpdaterService.GroupsToUsers.Add(_targetGroupId, targetGroupUsers);
            graphUpdaterService.Groups.Add(_sources[0].ObjectId, new Microsoft.Graph.Group { Id = _sources[0].ObjectId.ToString(), DisplayName = "Source Group" });
            graphUpdaterService.Groups.Add(_targetGroupId, new Microsoft.Graph.Group { Id = _targetGroupId.ToString(), DisplayName = "Target Group" });

            var response = await deltaCalculator.CalculateDifferenceAsync(_membership, targetGroupUsers);

            var emailMessage = mailRepository.SentEmails.Single();

            Assert.AreEqual(SyncStatus.Idle, response.SyncStatus);
            Assert.AreEqual(GraphUpdaterStatus.ThresholdExceeded, response.GraphUpdaterStatus);
            Assert.AreEqual(targetGroupUsers.Count, graphUpdaterService.GroupsToUsers[_targetGroupId].Count);
            Assert.IsTrue(loggingRepository.MessagesLogged.Any(x => x.Message.Contains("is lesser than threshold value")));
            Assert.AreEqual(SyncThresholdBothEmailBody, emailMessage.Content);
            Assert.AreEqual(_targetGroupId.ToString(), emailMessage.AdditionalContentParams[1]);
            Assert.AreEqual(6, emailMessage.AdditionalContentParams.Length);
        }

        [TestMethod]
        public async Task CalculateDeltaWithMissingSyncJob()
        {
            var calculator = new MembershipDifferenceCalculator<AzureADUser>();
            var senderRecipients = new EmailSenderRecipient();

            var syncjobRepository = new MockSyncJobRepository();
            var loggingRepository = new MockLoggingRepository();
            var mailRepository = new MockMailRepository();
            var dryRun = new DryRunValue();
            var graphUpdaterService = new MockGraphUpdaterService(mailRepository);

            var deltaCalculator = new DeltaCalculatorService(
                                    calculator,
                                    syncjobRepository,
                                    loggingRepository,
                                    senderRecipients,
                                    dryRun,
                                    graphUpdaterService);

            var targetGroupUsers = new List<AzureADUser>();

            graphUpdaterService.GroupsToUsers.Add(_sources[0].ObjectId, _users);
            graphUpdaterService.GroupsToUsers.Add(_targetGroupId, targetGroupUsers);

            var response = await deltaCalculator.CalculateDifferenceAsync(_membership, targetGroupUsers);

            Assert.AreEqual(SyncStatus.Error, response.SyncStatus);
            Assert.AreEqual(GraphUpdaterStatus.Error, response.GraphUpdaterStatus);
        }

        private List<AzureADUser> MakeUsers(int size, int startIdx)
        {
            var helper = new TestObjectHelpers();
            var users = new AzureADUser[size];
            for (int i = 0; i < size; i++)
            {
                int thisIdx = startIdx + i;
                users[i] = helper.UserNamed(thisIdx);
            }
            return users.ToList();
        }
    }
}

