// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Entities.ServiceBus;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using Services.Tests.Mocks;
using Hosts.GraphUpdater;
using Repositories.MembershipDifference;
using DIConcreteTypes;
using System.Threading.Tasks;
using Repositories.Mocks;

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
                Errored = false,
                IsLastMessage = true,
                RunId = _rundId,
                SourceMembers = _users,
                Sources = _sources,
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
        public async Task InitialGroupSync()
        {
            var calculator = new MembershipDifferenceCalculator<AzureADUser>();
            var senderRecipients = new EmailSenderRecipient();
            var graphGroupsRepository = new MockGraphGroupRepository();
            var syncjobRepository = new MockSyncJobRepository();
            var loggingRepository = new MockLoggingRepository();
            var mailRepository = new MockMailRepository();
            var dryRun = new DryRunValue();

            var graphUpdater = new GraphUpdaterApplication(
                                     calculator,
                                     syncjobRepository,
                                     loggingRepository,
                                     mailRepository,
                                     graphGroupsRepository,
                                     senderRecipients,
                                     dryRun);


            syncjobRepository.ExistingSyncJobs.Add((_partitionKey, _rowKey), _job);
            graphGroupsRepository.GroupsToUsers.Add(_sources[0].ObjectId, _users);
            graphGroupsRepository.GroupsToUsers.Add(_targetGroupId, new List<AzureADUser>());

            await graphUpdater.CalculateDifference(_membership);

            Assert.AreEqual(SyncStatus.Idle.ToString(), _job.Status);
            Assert.AreEqual(_users.Count, graphGroupsRepository.GroupsToUsers[_targetGroupId].Count);
        }

        [TestMethod]
        public async Task InitialGroupSyncWithMissingTargetGroup()
        {
            var calculator = new MembershipDifferenceCalculator<AzureADUser>();
            var senderRecipients = new EmailSenderRecipient();
            var graphGroupsRepository = new MockGraphGroupRepository();
            var syncjobRepository = new MockSyncJobRepository();
            var loggingRepository = new MockLoggingRepository();
            var mailRepository = new MockMailRepository();
            var dryRun = new DryRunValue();

            var graphUpdater = new GraphUpdaterApplication(
                                    calculator,
                                    syncjobRepository,
                                    loggingRepository,
                                    mailRepository,
                                    graphGroupsRepository,
                                    senderRecipients,
                                    dryRun);


            syncjobRepository.ExistingSyncJobs.Add((_partitionKey, _rowKey), _job);
            graphGroupsRepository.GroupsToUsers.Add(_sources[0].ObjectId, _users);

            await graphUpdater.CalculateDifference(_membership);

            Assert.AreEqual(SyncStatus.Error.ToString(), _job.Status);
            Assert.IsFalse(graphGroupsRepository.GroupsToUsers.ContainsKey(_targetGroupId));
            Assert.IsTrue(loggingRepository.MessagesLogged.Any(x => x.Message.Contains($"destination group {_membership.Destination} doesn't exist")));
        }

        [TestMethod]
        public async Task ErroredInitialGroupSync()
        {
            var calculator = new MembershipDifferenceCalculator<AzureADUser>();
            var senderRecipients = new EmailSenderRecipient();
            var graphGroupsRepository = new MockGraphGroupRepository();
            var syncjobRepository = new MockSyncJobRepository();
            var loggingRepository = new MockLoggingRepository();
            var mailRepository = new MockMailRepository();
            var dryRun = new DryRunValue();

            var graphUpdater = new GraphUpdaterApplication(
                                    calculator,
                                    syncjobRepository,
                                    loggingRepository,
                                    mailRepository,
                                    graphGroupsRepository,
                                    senderRecipients,
                                    dryRun);


            syncjobRepository.ExistingSyncJobs.Add((_partitionKey, _rowKey), _job);
            graphGroupsRepository.GroupsToUsers.Add(_sources[0].ObjectId, _users);
            graphGroupsRepository.GroupsToUsers.Add(_targetGroupId, new List<AzureADUser>());

            _membership.Errored = true;

            await graphUpdater.CalculateDifference(_membership);

            Assert.AreEqual(SyncStatus.Error.ToString(), _job.Status);
            Assert.IsTrue(loggingRepository.MessagesLogged.Any(x => x.Message.Contains("calculator reported an error. Not syncing and marking as error")));
        }

        [TestMethod]
        public async Task NonInitialGroupSyncExceedingIncreaseThreshold()
        {
            var calculator = new MembershipDifferenceCalculator<AzureADUser>();
            var senderRecipients = new EmailSenderRecipient();
            var graphGroupsRepository = new MockGraphGroupRepository();
            var syncjobRepository = new MockSyncJobRepository();
            var loggingRepository = new MockLoggingRepository();
            var mailRepository = new MockMailRepository();
            var dryRun = new DryRunValue();

            var graphUpdater = new GraphUpdaterApplication(
                                    calculator,
                                    syncjobRepository,
                                    loggingRepository,
                                    mailRepository,
                                    graphGroupsRepository,
                                    senderRecipients,
                                    dryRun);

            var targetGroupUsers = new List<AzureADUser>();

            _job.LastRunTime = DateTime.UtcNow.AddDays(-1);

            syncjobRepository.ExistingSyncJobs.Add((_partitionKey, _rowKey), _job);
            graphGroupsRepository.GroupsToUsers.Add(_sources[0].ObjectId, _users);
            graphGroupsRepository.GroupsToUsers.Add(_targetGroupId, targetGroupUsers);

            await graphUpdater.CalculateDifference(_membership);

            var emailMessage = mailRepository.SentEmails.Single();

            Assert.AreEqual(SyncStatus.Idle.ToString(), _job.Status);
            Assert.AreEqual(targetGroupUsers.Count, graphGroupsRepository.GroupsToUsers[_targetGroupId].Count);
            Assert.IsTrue(loggingRepository.MessagesLogged.Any(x => x.Message.Contains("is greater than threshold value")));
            Assert.AreEqual(SyncThresholdIncreaseEmailBody, emailMessage.Content);
            Assert.AreEqual(_targetGroupId.ToString(), emailMessage.AdditionalContentParams[1]);
            Assert.AreEqual(4, emailMessage.AdditionalContentParams.Length);
        }

        [TestMethod]
        public async Task NonInitialGroupSyncExceedingDecreaseThreshold()
        {
            var calculator = new MembershipDifferenceCalculator<AzureADUser>();
            var senderRecipients = new EmailSenderRecipient();
            var graphGroupsRepository = new MockGraphGroupRepository();
            var syncjobRepository = new MockSyncJobRepository();
            var loggingRepository = new MockLoggingRepository();
            var mailRepository = new MockMailRepository();
            var dryRun = new DryRunValue();

            var graphUpdater = new GraphUpdaterApplication(
                                    calculator,
                                    syncjobRepository,
                                    loggingRepository,
                                    mailRepository,
                                    graphGroupsRepository,
                                    senderRecipients,
                                    dryRun);

            var users = _membership.SourceMembers;
            _membership.SourceMembers = users.Take(2).ToList();
            _job.LastRunTime = DateTime.UtcNow.AddDays(-1);

            var targetGroupUsers = users.Take(6).ToList();

            syncjobRepository.ExistingSyncJobs.Add((_partitionKey, _rowKey), _job);
            graphGroupsRepository.GroupsToUsers.Add(_sources[0].ObjectId, _membership.SourceMembers);
            graphGroupsRepository.GroupsToUsers.Add(_targetGroupId, targetGroupUsers);

            await graphUpdater.CalculateDifference(_membership);

            var emailMessage = mailRepository.SentEmails.Single();

            Assert.AreEqual(SyncStatus.Idle.ToString(), _job.Status);
            Assert.AreEqual(targetGroupUsers.Count, graphGroupsRepository.GroupsToUsers[_targetGroupId].Count);
            Assert.IsTrue(loggingRepository.MessagesLogged.Any(x => x.Message.Contains("is lesser than threshold value")));
            Assert.AreEqual(SyncThresholdDecreaseEmailBody, emailMessage.Content);
            Assert.AreEqual(_targetGroupId.ToString(), emailMessage.AdditionalContentParams[1]);
            Assert.AreEqual(4, emailMessage.AdditionalContentParams.Length);
        }

        [TestMethod]
        public async Task NonInitialGroupSyncExceedingBothThresholds()
        {
            var calculator = new MembershipDifferenceCalculator<AzureADUser>();
            var senderRecipients = new EmailSenderRecipient();
            var graphGroupsRepository = new MockGraphGroupRepository();
            var syncjobRepository = new MockSyncJobRepository();
            var loggingRepository = new MockLoggingRepository();
            var mailRepository = new MockMailRepository();
            var dryRun = new DryRunValue();

            var graphUpdater = new GraphUpdaterApplication(
                                    calculator,
                                    syncjobRepository,
                                    loggingRepository,
                                    mailRepository,
                                    graphGroupsRepository,
                                    senderRecipients,
                                    dryRun);

            var users = _membership.SourceMembers;
            _membership.SourceMembers = users.Take(2).ToList();
            _job.LastRunTime = DateTime.UtcNow.AddDays(-1);

            var targetGroupUsers = users.Skip(2).Take(2).ToList();

            syncjobRepository.ExistingSyncJobs.Add((_partitionKey, _rowKey), _job);
            graphGroupsRepository.GroupsToUsers.Add(_sources[0].ObjectId, _membership.SourceMembers);
            graphGroupsRepository.GroupsToUsers.Add(_targetGroupId, targetGroupUsers);

            await graphUpdater.CalculateDifference(_membership);

            var emailMessage = mailRepository.SentEmails.Single();

            Assert.AreEqual(SyncStatus.Idle.ToString(), _job.Status);
            Assert.AreEqual(targetGroupUsers.Count, graphGroupsRepository.GroupsToUsers[_targetGroupId].Count);
            Assert.IsTrue(loggingRepository.MessagesLogged.Any(x => x.Message.Contains("is lesser than threshold value")));
            Assert.AreEqual(SyncThresholdBothEmailBody, emailMessage.Content);
            Assert.AreEqual(_targetGroupId.ToString(), emailMessage.AdditionalContentParams[1]);
            Assert.AreEqual(6, emailMessage.AdditionalContentParams.Length);
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

