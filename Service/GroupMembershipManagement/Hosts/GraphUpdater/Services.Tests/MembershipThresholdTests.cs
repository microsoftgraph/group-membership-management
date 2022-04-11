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
using Microsoft.Graph;
using System.Collections;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Localization;
using Repositories.Localization;
using Microsoft.Extensions.Logging.Abstractions;

namespace Services.Tests
{
    [TestClass]
    public class MembershipThresholdTests
    {
        const string SyncThresholdBothEmailBody = "SyncThresholdBothEmailBody";
        const string SyncJobDisabledEmailBody = "SyncJobDisabledEmailBody";

        Guid _rundId;
        Guid _targetGroupId;
        string _partitionKey;
        string _rowKey;
        AzureADGroup[] _sources;
        List<AzureADUser> _users;
        SyncJob _job;
        GroupMembership _membership;
        GMMResources _gmmResources = new GMMResources
        {
            LearnMoreAboutGMMUrl = "http://learn-more-url"
        };
        MockLocalizationRepository _localizationRepository = new MockLocalizationRepository();

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
                Query = _sources[0].ObjectId.ToString(),
                TargetOfficeGroupId = _targetGroupId,
                ThresholdPercentageForAdditions = 50,
                ThresholdPercentageForRemovals = 50,
                Period = 6,
                Requestor = "requestor@mail.com,requestor2@mail.com",
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
            var thresholdConfig = new ThresholdConfig(5, 3, 3, 10);

            dryRun.DryRunEnabled = false;

            var deltaCalculator = new DeltaCalculatorService(
                                     calculator,
                                     syncjobRepository,
                                     loggingRepository,
                                     senderRecipients,
                                     graphUpdaterService,
                                     dryRun,
                                     thresholdConfig,
                                     _gmmResources,
                                     _localizationRepository);


            var targetGroupUsers = new List<AzureADUser>();

            syncjobRepository.ExistingSyncJobs.Add((_partitionKey, _rowKey), _job);
            graphUpdaterService.GroupsToUsers.Add(_sources[0].ObjectId, _users);
            graphUpdaterService.GroupsToUsers.Add(_targetGroupId, targetGroupUsers);
            graphUpdaterService.Groups.Add(_sources[0].ObjectId, new Microsoft.Graph.Group { Id = _sources[0].ObjectId.ToString(), DisplayName = "Source Group" });
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
            var thresholdConfig = new ThresholdConfig(5, 3, 3, 10);
            var graphUpdaterService = new MockGraphUpdaterService(mailRepository);

            var deltaCalculator = new DeltaCalculatorService(
                                    calculator,
                                    syncjobRepository,
                                    loggingRepository,
                                    senderRecipients,
                                    graphUpdaterService,
                                    dryRun,
                                    thresholdConfig,
                                    _gmmResources,
                                    _localizationRepository);


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
            var thresholdConfig = new ThresholdConfig(5, 3, 3, 10);
            var graphUpdaterService = new MockGraphUpdaterService(mailRepository);
            
            var deltaCalculator = new DeltaCalculatorService(
                                    calculator,
                                    syncjobRepository,
                                    loggingRepository,
                                    senderRecipients,
                                    graphUpdaterService,
                                    dryRun,
                                    thresholdConfig,
                                    _gmmResources,
                                    _localizationRepository);

            var targetGroupUsers = new List<AzureADUser>();

            _job.LastRunTime = DateTime.UtcNow.AddDays(-1);
            _job.ThresholdViolations = 2;

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
            Assert.AreEqual(SyncThresholdBothEmailBody, emailMessage.Content);
            Assert.AreEqual(_targetGroupId.ToString(), emailMessage.AdditionalContentParams[1]);
            Assert.AreEqual(5, emailMessage.AdditionalContentParams.Length);
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
            var thresholdConfig = new ThresholdConfig(5, 3, 3, 10);
            var graphUpdaterService = new MockGraphUpdaterService(mailRepository);

            var deltaCalculator = new DeltaCalculatorService(
                                    calculator,
                                    syncjobRepository,
                                    loggingRepository,
                                    senderRecipients,
                                    graphUpdaterService,
                                    dryRun,
                                    thresholdConfig,
                                    _gmmResources,
                                    _localizationRepository);

            var users = _membership.SourceMembers;
            _membership.SourceMembers = users.Take(2).ToList();
            _job.LastRunTime = DateTime.UtcNow.AddDays(-1);
            _job.ThresholdViolations = 2;

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
            Assert.AreEqual(SyncThresholdBothEmailBody, emailMessage.Content);
            Assert.AreEqual(_targetGroupId.ToString(), emailMessage.AdditionalContentParams[1]);
            Assert.AreEqual(5, emailMessage.AdditionalContentParams.Length);
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
            var thresholdConfig = new ThresholdConfig(5, 3, 3, 10);
            var graphUpdaterService = new MockGraphUpdaterService(mailRepository);

            var deltaCalculator = new DeltaCalculatorService(
                                    calculator,
                                    syncjobRepository,
                                    loggingRepository,
                                    senderRecipients,
                                    graphUpdaterService,
                                    dryRun,
                                    thresholdConfig,
                                    _gmmResources,
                                    _localizationRepository);

            var users = _membership.SourceMembers;
            _membership.SourceMembers = users.Take(2).ToList();
            _job.LastRunTime = DateTime.UtcNow.AddDays(-1);
            _job.ThresholdViolations = 2;

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
            Assert.AreEqual(5, emailMessage.AdditionalContentParams.Length);
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
            var thresholdConfig = new ThresholdConfig(5, 3, 3, 10);
            var graphUpdaterService = new MockGraphUpdaterService(mailRepository);
            var deltaCalculator = new DeltaCalculatorService(
                                    calculator,
                                    syncjobRepository,
                                    loggingRepository,
                                    senderRecipients,
                                    graphUpdaterService,
                                    dryRun,
                                    thresholdConfig,
                                    _gmmResources,
                                    _localizationRepository);

            var targetGroupUsers = new List<AzureADUser>();

            graphUpdaterService.GroupsToUsers.Add(_sources[0].ObjectId, _users);
            graphUpdaterService.GroupsToUsers.Add(_targetGroupId, targetGroupUsers);

            var response = await deltaCalculator.CalculateDifferenceAsync(_membership, targetGroupUsers);

            Assert.AreEqual(SyncStatus.Error, response.SyncStatus);
            Assert.AreEqual(GraphUpdaterStatus.Error, response.GraphUpdaterStatus);
        }

        [TestMethod]
        [DataRow(0)]
        [DataRow(1)]
        [DataRow(6)]
        [DataRow(7)]
        [DataRow(8)]
        [DataRow(10)]
        public async Task DoNotSendNotificationToRequestor(int currentThresoldViolations)
        {
            var calculator = new MembershipDifferenceCalculator<AzureADUser>();
            var senderRecipients = new EmailSenderRecipient
            {
                SyncDisabledCCAddresses = "support@email.com"
            };

            var syncjobRepository = new MockSyncJobRepository();
            var loggingRepository = new MockLoggingRepository();
            var mailRepository = new MockMailRepository();
            var dryRun = new DryRunValue();
            var thresholdConfig = new ThresholdConfig(5, 3, 3, 10);
            var graphUpdaterService = new MockGraphUpdaterService(mailRepository);

            var deltaCalculator = new DeltaCalculatorService(
                                    calculator,
                                    syncjobRepository,
                                    loggingRepository,
                                    senderRecipients,
                                    graphUpdaterService,
                                    dryRun,
                                    thresholdConfig,
                                    _gmmResources,
                                    _localizationRepository);

            var targetGroupUsers = new List<AzureADUser>();
            var ownersPage = new GroupOwnersPage();
            var owners = new List<User>();

            foreach (var email in _job.Requestor.Split(",", StringSplitOptions.RemoveEmptyEntries))
            {
                owners.Add(new User { Mail = email });
            }

            owners.AddRange(GenerateGraphUsers(3));
            owners.ForEach(ownersPage.Add);

            _job.LastRunTime = DateTime.UtcNow.AddDays(-1);
            _job.ThresholdViolations = currentThresoldViolations;

            syncjobRepository.ExistingSyncJobs.Add((_partitionKey, _rowKey), _job);
            graphUpdaterService.GroupsToUsers.Add(_sources[0].ObjectId, _users);
            graphUpdaterService.GroupsToUsers.Add(_targetGroupId, targetGroupUsers);
            graphUpdaterService.Groups.Add(_sources[0].ObjectId, new Group { Id = _sources[0].ObjectId.ToString(), DisplayName = "Source Group" });
            graphUpdaterService.Groups.Add(_targetGroupId, new Group { Id = _targetGroupId.ToString(), DisplayName = "Target Group", Owners = ownersPage });

            var response = await deltaCalculator.CalculateDifferenceAsync(_membership, targetGroupUsers);

            var emailMessage = mailRepository.SentEmails.Count();

            Assert.AreEqual(SyncStatus.Idle, response.SyncStatus);
            Assert.AreEqual(GraphUpdaterStatus.ThresholdExceeded, response.GraphUpdaterStatus);
            Assert.AreEqual(targetGroupUsers.Count, graphUpdaterService.GroupsToUsers[_targetGroupId].Count);
            Assert.IsTrue(loggingRepository.MessagesLogged.Any(x => x.Message.Contains("is greater than threshold value")));
            Assert.AreEqual(0, mailRepository.SentEmails.Count);
        }

        [TestMethod]
        [DataRow(2)]
        [DataRow(3)]
        [DataRow(4)]
        [DataRow(5)]
        [DataRow(9)]
        public async Task SendNotificationToRequestor(int currentThresoldViolations)
        {
            var calculator = new MembershipDifferenceCalculator<AzureADUser>();
            var senderRecipients = new EmailSenderRecipient
            {
                SyncDisabledCCAddresses = "support@email.com"
            };

            var syncjobRepository = new MockSyncJobRepository();
            var loggingRepository = new MockLoggingRepository();
            var mailRepository = new MockMailRepository();
            var dryRun = new DryRunValue();
            var thresholdConfig = new ThresholdConfig(5, 3, 3, 10);
            var graphUpdaterService = new MockGraphUpdaterService(mailRepository);

            var deltaCalculator = new DeltaCalculatorService(
                                    calculator,
                                    syncjobRepository,
                                    loggingRepository,
                                    senderRecipients,
                                    graphUpdaterService,
                                    dryRun,
                                    thresholdConfig,
                                    _gmmResources,
                                    _localizationRepository);

            var targetGroupUsers = new List<AzureADUser>();
            var ownersPage = new GroupOwnersPage();
            var owners = new List<User>();

            foreach (var email in _job.Requestor.Split(",", StringSplitOptions.RemoveEmptyEntries))
            {
                owners.Add(new User { Mail = email });
            }

            owners.AddRange(GenerateGraphUsers(3));
            owners.ForEach(ownersPage.Add);

            _job.LastRunTime = DateTime.UtcNow.AddDays(-1);
            _job.ThresholdViolations = currentThresoldViolations;

            syncjobRepository.ExistingSyncJobs.Add((_partitionKey, _rowKey), _job);
            graphUpdaterService.GroupsToUsers.Add(_sources[0].ObjectId, _users);
            graphUpdaterService.GroupsToUsers.Add(_targetGroupId, targetGroupUsers);
            graphUpdaterService.Groups.Add(_sources[0].ObjectId, new Group { Id = _sources[0].ObjectId.ToString(), DisplayName = "Source Group" });
            graphUpdaterService.Groups.Add(_targetGroupId, new Group { Id = _targetGroupId.ToString(), DisplayName = "Target Group", Owners = ownersPage });

            var response = await deltaCalculator.CalculateDifferenceAsync(_membership, targetGroupUsers);

            var emailMessage = mailRepository.SentEmails.Single();

            Assert.AreEqual(SyncStatus.Idle, response.SyncStatus);
            Assert.AreEqual(GraphUpdaterStatus.ThresholdExceeded, response.GraphUpdaterStatus);
            Assert.AreEqual(targetGroupUsers.Count, graphUpdaterService.GroupsToUsers[_targetGroupId].Count);
            Assert.IsTrue(loggingRepository.MessagesLogged.Any(x => x.Message.Contains("is greater than threshold value")));            

            if (currentThresoldViolations != 9)
            {
                Assert.AreEqual(SyncThresholdBothEmailBody, emailMessage.Content);
                Assert.AreEqual(5, emailMessage.AdditionalContentParams.Length);
            }
            else
            {
                Assert.AreEqual(SyncJobDisabledEmailBody, emailMessage.Content);
                Assert.AreEqual(4, emailMessage.AdditionalContentParams.Length);
            }

            Assert.AreEqual(_targetGroupId.ToString(), emailMessage.AdditionalContentParams[1]);
            Assert.AreEqual(_job.Requestor, emailMessage.ToEmailAddresses);
        }

        [TestMethod]
        public async Task SendNotificationToOwners()
        {
            var calculator = new MembershipDifferenceCalculator<AzureADUser>();
            var senderRecipients = new EmailSenderRecipient
            {
                SyncDisabledCCAddresses = "support@email.com"
            };

            var syncjobRepository = new MockSyncJobRepository();
            var loggingRepository = new MockLoggingRepository();
            var mailRepository = new MockMailRepository();
            var dryRun = new DryRunValue();
            var thresholdConfig = new ThresholdConfig(5, 3, 3, 10);
            var graphUpdaterService = new MockGraphUpdaterService(mailRepository);

            var deltaCalculator = new DeltaCalculatorService(
                                    calculator,
                                    syncjobRepository,
                                    loggingRepository,
                                    senderRecipients,
                                    graphUpdaterService,
                                    dryRun,
                                    thresholdConfig,
                                    _gmmResources,
                                    _localizationRepository);

            var targetGroupUsers = new List<AzureADUser>();
            var ownersPage = new GroupOwnersPage();
            var owners = new List<User>(GenerateGraphUsers(3));
            owners.ForEach(ownersPage.Add);

            _job.LastRunTime = DateTime.UtcNow.AddDays(-1);
            _job.ThresholdViolations = 2;

            syncjobRepository.ExistingSyncJobs.Add((_partitionKey, _rowKey), _job);
            graphUpdaterService.GroupsToUsers.Add(_sources[0].ObjectId, _users);
            graphUpdaterService.GroupsToUsers.Add(_targetGroupId, targetGroupUsers);
            graphUpdaterService.Groups.Add(_sources[0].ObjectId, new Group { Id = _sources[0].ObjectId.ToString(), DisplayName = "Source Group" });
            graphUpdaterService.Groups.Add(_targetGroupId, new Group { Id = _targetGroupId.ToString(), DisplayName = "Target Group", Owners = ownersPage });

            var response = await deltaCalculator.CalculateDifferenceAsync(_membership, targetGroupUsers);

            var emailMessage = mailRepository.SentEmails.Single();

            Assert.AreEqual(SyncStatus.Idle, response.SyncStatus);
            Assert.AreEqual(GraphUpdaterStatus.ThresholdExceeded, response.GraphUpdaterStatus);
            Assert.AreEqual(targetGroupUsers.Count, graphUpdaterService.GroupsToUsers[_targetGroupId].Count);
            Assert.IsTrue(loggingRepository.MessagesLogged.Any(x => x.Message.Contains("is greater than threshold value")));
            Assert.AreEqual(SyncThresholdBothEmailBody, emailMessage.Content);
            Assert.AreEqual(_targetGroupId.ToString(), emailMessage.AdditionalContentParams[1]);
            Assert.AreEqual(5, emailMessage.AdditionalContentParams.Length);

            foreach (var owner in owners)
            {
                Assert.IsTrue(emailMessage.ToEmailAddresses.Contains(owner.Mail));
            }
        }

        [TestMethod]
        public async Task SendNotificationToSupportAsFallBack()
        {
            var calculator = new MembershipDifferenceCalculator<AzureADUser>();
            var senderRecipients = new EmailSenderRecipient
            {
                SupportEmailAddresses = "support@email.com"
            };

            var syncjobRepository = new MockSyncJobRepository();
            var loggingRepository = new MockLoggingRepository();
            var mailRepository = new MockMailRepository();
            var dryRun = new DryRunValue();
            var thresholdConfig = new ThresholdConfig(5, 3, 3, 10);
            var graphUpdaterService = new MockGraphUpdaterService(mailRepository);

            var deltaCalculator = new DeltaCalculatorService(
                                    calculator,
                                    syncjobRepository,
                                    loggingRepository,
                                    senderRecipients,
                                    graphUpdaterService,
                                    dryRun,
                                    thresholdConfig,
                                    _gmmResources,
                                    _localizationRepository);

            var targetGroupUsers = new List<AzureADUser>();
            var ownersPage = new GroupOwnersPage();
            var owners = new List<User>(GenerateGraphUsers(10));
            owners.ForEach(ownersPage.Add);

            _job.LastRunTime = DateTime.UtcNow.AddDays(-1);
            _job.ThresholdViolations = 2;

            syncjobRepository.ExistingSyncJobs.Add((_partitionKey, _rowKey), _job);
            graphUpdaterService.GroupsToUsers.Add(_sources[0].ObjectId, _users);
            graphUpdaterService.GroupsToUsers.Add(_targetGroupId, targetGroupUsers);
            graphUpdaterService.Groups.Add(_sources[0].ObjectId, new Group { Id = _sources[0].ObjectId.ToString(), DisplayName = "Source Group" });
            graphUpdaterService.Groups.Add(_targetGroupId, new Group { Id = _targetGroupId.ToString(), DisplayName = "Target Group", Owners = ownersPage });

            var response = await deltaCalculator.CalculateDifferenceAsync(_membership, targetGroupUsers);

            var emailMessage = mailRepository.SentEmails.Single();

            Assert.AreEqual(SyncStatus.Idle, response.SyncStatus);
            Assert.AreEqual(GraphUpdaterStatus.ThresholdExceeded, response.GraphUpdaterStatus);
            Assert.AreEqual(targetGroupUsers.Count, graphUpdaterService.GroupsToUsers[_targetGroupId].Count);
            Assert.IsTrue(loggingRepository.MessagesLogged.Any(x => x.Message.Contains("is greater than threshold value")));
            Assert.AreEqual(SyncThresholdBothEmailBody, emailMessage.Content);
            Assert.AreEqual(_targetGroupId.ToString(), emailMessage.AdditionalContentParams[1]);
            Assert.AreEqual(5, emailMessage.AdditionalContentParams.Length);
            Assert.AreEqual(senderRecipients.SupportEmailAddresses, emailMessage.ToEmailAddresses);
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

        private List<User> GenerateGraphUsers(int count)
        {
            var users = new List<User>();

            for (int i = 0; i < count; i++)
            {
                var user = new User
                {
                    Id = Guid.NewGuid().ToString(),
                    Mail = $"user_{i}@mail.com"
                };

                users.Add(user);
            }

            return users;
        }

        private class GroupOwnersPage : IGroupOwnersCollectionWithReferencesPage
        {
            private List<DirectoryObject> _members = new List<DirectoryObject>();

            public DirectoryObject this[int index] { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

            public IGroupOwnersCollectionWithReferencesRequest NextPageRequest => throw new NotImplementedException();

            public IList<DirectoryObject> CurrentPage => _members;

            public IDictionary<string, object> AdditionalData { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

            public int Count => _members.Count;

            public bool IsReadOnly => throw new NotImplementedException();

            public void Add(DirectoryObject item)
            {
                _members.Add(item);
            }

            public void Clear()
            {
                _members.Clear();
            }

            public bool Contains(DirectoryObject item)
            {
                throw new NotImplementedException();
            }

            public void CopyTo(DirectoryObject[] array, int arrayIndex)
            {
                throw new NotImplementedException();
            }

            public IEnumerator<DirectoryObject> GetEnumerator()
            {
                return _members.GetEnumerator();
            }

            public int IndexOf(DirectoryObject item)
            {
                throw new NotImplementedException();
            }

            public void InitializeNextPageRequest(IBaseClient client, string nextPageLinkString)
            {
                throw new NotImplementedException();
            }

            public void Insert(int index, DirectoryObject item)
            {
                throw new NotImplementedException();
            }

            public bool Remove(DirectoryObject item)
            {
                throw new NotImplementedException();
            }

            public void RemoveAt(int index)
            {
                throw new NotImplementedException();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return _members.GetEnumerator();
            }
        }
    }
}

