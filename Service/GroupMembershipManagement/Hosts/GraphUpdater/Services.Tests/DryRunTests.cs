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
using System.Diagnostics.CodeAnalysis;

namespace Services.Tests
{
    [ExcludeFromCodeCoverage]

    [TestClass]
    public class DryRunTests
    {
        Guid _rundId;
        Guid _targetGroupId;
        string _partitionKey;
        string _rowKey;
        AzureADGroup[] _sources;
        List<AzureADUser> _users;
        SyncJob _job;
        SyncJob _newSyncJob;
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
                Type = "SecurityGroup",
                IsDryRunEnabled = false,
                DryRunTimeStamp = DateTime.FromFileTimeUtc(0)
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
            dryRun.DryRunEnabled = false;

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
        public async Task GraphUpdaterDryRunCalculateMembershipTests()
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
            var intialRunTime = DateTime.UtcNow.AddDays(-1);
            _job.LastRunTime = intialRunTime;
            _job.IsDryRunEnabled = true;
            _membership.MembershipObtainerDryRunEnabled = true;

            var targetGroupUsers = users.Skip(2).Take(2).ToList();

            syncjobRepository.ExistingSyncJobs.Add((_partitionKey, _rowKey), _job);
            graphGroupsRepository.GroupsToUsers.Add(_sources[0].ObjectId, _membership.SourceMembers);
            graphGroupsRepository.GroupsToUsers.Add(_targetGroupId, targetGroupUsers);

            await graphUpdater.CalculateDifference(_membership);

            Assert.AreEqual(SyncStatus.Idle.ToString(), _job.Status);
            Assert.AreEqual(targetGroupUsers.Count, graphGroupsRepository.GroupsToUsers[_targetGroupId].Count);
            Assert.IsTrue(loggingRepository.MessagesLogged.Any(x => x.Message.Contains("Membership will not be updated.")));
            Assert.AreEqual(_job.LastRunTime, intialRunTime);
        }

        [TestMethod]
        public async Task IsDryRunEnabledDryRunCalculateMembershipTests()
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
            var intialRunTime = DateTime.UtcNow.AddDays(-1);
            _job.LastRunTime = intialRunTime;
            _job.IsDryRunEnabled = true;
            _membership.MembershipObtainerDryRunEnabled = false;

            var targetGroupUsers = users.Skip(2).Take(2).ToList();

            syncjobRepository.ExistingSyncJobs.Add((_partitionKey, _rowKey), _job);
            graphGroupsRepository.GroupsToUsers.Add(_sources[0].ObjectId, _membership.SourceMembers);
            graphGroupsRepository.GroupsToUsers.Add(_targetGroupId, targetGroupUsers);

            await graphUpdater.CalculateDifference(_membership);

            Assert.AreEqual(SyncStatus.Idle.ToString(), _job.Status);
            Assert.AreEqual(targetGroupUsers.Count, graphGroupsRepository.GroupsToUsers[_targetGroupId].Count);
            Assert.IsTrue(loggingRepository.MessagesLogged.Any(x => x.Message.Contains("Membership will not be updated.")));
            Assert.AreEqual(_job.LastRunTime, intialRunTime);
        }

        [TestMethod]
        public async Task MembershipObtainerDryRunCalculateMembershipTests()
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
            var intialRunTime = DateTime.UtcNow.AddDays(-1);
            _job.LastRunTime = intialRunTime;
            _job.IsDryRunEnabled = false;
            _membership.MembershipObtainerDryRunEnabled = true;

            var targetGroupUsers = users.Skip(2).Take(2).ToList();

            syncjobRepository.ExistingSyncJobs.Add((_partitionKey, _rowKey), _job);
            graphGroupsRepository.GroupsToUsers.Add(_sources[0].ObjectId, _membership.SourceMembers);
            graphGroupsRepository.GroupsToUsers.Add(_targetGroupId, targetGroupUsers);

            await graphUpdater.CalculateDifference(_membership);

            Assert.AreEqual(SyncStatus.Idle.ToString(), _job.Status);
            Assert.AreEqual(targetGroupUsers.Count, graphGroupsRepository.GroupsToUsers[_targetGroupId].Count);
            Assert.IsTrue(loggingRepository.MessagesLogged.Any(x => x.Message.Contains("Membership will not be updated.")));
            Assert.AreEqual(_job.LastRunTime, intialRunTime);
        }

        [TestMethod]
        public async Task AllDryRunCalculateMembershipTests()
        {
            var calculator = new MembershipDifferenceCalculator<AzureADUser>();
            var senderRecipients = new EmailSenderRecipient();
            var graphGroupsRepository = new MockGraphGroupRepository();
            var syncjobRepository = new MockSyncJobRepository();
            var loggingRepository = new MockLoggingRepository();
            var mailRepository = new MockMailRepository();
            var dryRun = new DryRunValue();
            dryRun.DryRunEnabled = true;

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
            var intialRunTime = DateTime.UtcNow.AddDays(-1);
            _job.LastRunTime = intialRunTime;
            _job.IsDryRunEnabled = true;
            _membership.MembershipObtainerDryRunEnabled = true;

            var targetGroupUsers = users.Skip(2).Take(2).ToList();

            syncjobRepository.ExistingSyncJobs.Add((_partitionKey, _rowKey), _job);
            graphGroupsRepository.GroupsToUsers.Add(_sources[0].ObjectId, _membership.SourceMembers);
            graphGroupsRepository.GroupsToUsers.Add(_targetGroupId, targetGroupUsers);

            await graphUpdater.CalculateDifference(_membership);

            Assert.AreEqual(SyncStatus.Idle.ToString(), _job.Status);
            Assert.AreEqual(targetGroupUsers.Count, graphGroupsRepository.GroupsToUsers[_targetGroupId].Count);
            Assert.IsTrue(loggingRepository.MessagesLogged.Any(x => x.Message.Contains("Membership will not be updated.")));
            Assert.AreEqual(_job.LastRunTime, intialRunTime);
        }

        [TestMethod]
        public async Task NonDryRunCalculateMembershipTests()
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
            var intialRunTime = DateTime.UtcNow.AddDays(-1);
            _job.LastRunTime = intialRunTime;
            _job.DryRunTimeStamp = intialRunTime;
            _job.IsDryRunEnabled = false;
            _membership.MembershipObtainerDryRunEnabled = false;

            var targetGroupUsers = users.Skip(2).Take(2).ToList();

            syncjobRepository.ExistingSyncJobs.Add((_partitionKey, _rowKey), _job);
            graphGroupsRepository.GroupsToUsers.Add(_sources[0].ObjectId, _membership.SourceMembers);
            graphGroupsRepository.GroupsToUsers.Add(_targetGroupId, targetGroupUsers);

            await graphUpdater.CalculateDifference(_membership);

            Assert.AreEqual(SyncStatus.Idle.ToString(), _job.Status);
            Assert.AreEqual(targetGroupUsers.Count, graphGroupsRepository.GroupsToUsers[_targetGroupId].Count);
            Assert.IsTrue(loggingRepository.MessagesLogged.Any(x => x.Message.Contains("Syncing")));
            Assert.AreEqual(_job.DryRunTimeStamp, intialRunTime);
        }

        [TestMethod]
        public async Task NonDryRunAddMembersCalculateMembershipTests()
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
            _job.ThresholdPercentageForAdditions = 100;
            _job.ThresholdPercentageForRemovals = 10;
            var intialRunTime = DateTime.UtcNow.AddDays(-1);
            _job.LastRunTime = intialRunTime;
            _job.IsDryRunEnabled = true;
            _membership.MembershipObtainerDryRunEnabled = true;

            var targetGroupUsers = users.Skip(2).Take(2).ToList();

            syncjobRepository.ExistingSyncJobs.Add((_partitionKey, _rowKey), _job);
            graphGroupsRepository.GroupsToUsers.Add(_sources[0].ObjectId, _membership.SourceMembers);
            graphGroupsRepository.GroupsToUsers.Add(_targetGroupId, targetGroupUsers);

            await graphUpdater.CalculateDifference(_membership);

            Assert.AreEqual(SyncStatus.Idle.ToString(), _job.Status);
            Assert.AreEqual(targetGroupUsers.Count, graphGroupsRepository.GroupsToUsers[_targetGroupId].Count);
            Assert.IsTrue(loggingRepository.MessagesLogged.Any(x => x.Message.Contains("Membership will not be updated.")));
            Assert.AreEqual(_job.LastRunTime, intialRunTime);
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

