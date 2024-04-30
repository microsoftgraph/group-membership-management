// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.GroupMembershipObtainer;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Polly;
using Repositories.Contracts.InjectConfig;
using Repositories.Mocks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tests.FunctionApps.Mocks;
using Tests.Services;
using Models;
using Models.Notifications;
using Repositories.ServiceBusQueue;
using Moq;
using Repositories.Contracts;

namespace Tests.FunctionApps
{
    [TestClass]
    public class SGMembershipCalculatorTests
    {
        int _partIndex = 0;
        private QuerySample _querySample;
        private Dictionary<Guid, List<AzureADUser>> _groupsToUsers;
        private Mock<IServiceBusQueueRepository> _notificationsQueueRepository;
        private MockDatabaseSyncJobRepository _syncJobs;
        private MockDryRunValue _dryRun;
        private MockBlobStorageRepository _blobRepository;
        private TelemetryClient _telemetryClient;
        private Mock<ITeamsChannelRepository> _teamsChannelRepository;
        private Mock<IDatabaseDestinationAttributesRepository> _databaseDestinationAttributesRepository;
        private Guid _destinationGroup;
        private List<AzureADUser> _allUsers;
        [TestInitialize]
        public void Setup()
        {
            _querySample = QuerySample.GenerateQuerySample("GroupMembership");
            _groupsToUsers = new Dictionary<Guid, List<AzureADUser>>
            {
                { _querySample.QueryParts[_partIndex].SourceId, new List<AzureADUser> { new AzureADUser { ObjectId = Guid.NewGuid() } } }
            };
            _notificationsQueueRepository = new Mock<IServiceBusQueueRepository>();
            _syncJobs = new MockDatabaseSyncJobRepository();
            _dryRun = new MockDryRunValue { DryRunEnabled = false };
            _blobRepository = new MockBlobStorageRepository();
            _telemetryClient = new TelemetryClient(new TelemetryConfiguration());
            _teamsChannelRepository = new Mock<ITeamsChannelRepository>();
            _databaseDestinationAttributesRepository = new Mock<IDatabaseDestinationAttributesRepository>();
            _destinationGroup = Guid.NewGuid();
            _allUsers = new List<AzureADUser>();

        }

        [TestMethod]
        [DataRow(0, 0)]
        [DataRow(3, 0)]
        [DataRow(0, 3)]
        [DataRow(3, 3)]
        public async Task ProperlyGetsAndSendsMembership(int getGroupExceptions, int getMembersExceptions)
        {
            var graphRepo = new MockGraphGroupRepository()
            {
                GroupsToUsers = _groupsToUsers,
                ThrowSocketExceptionsFromGroupExistsBeforeSuccess = getGroupExceptions,
                ThrowSocketExceptionsFromGetUsersInGroupBeforeSuccess = getMembersExceptions
            };

            var calc = new SGMembershipCalculator(graphRepo, _blobRepository, _syncJobs, _notificationsQueueRepository.Object, _databaseDestinationAttributesRepository.Object, new MockLoggingRepository(), _dryRun);
            var testJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                TargetOfficeGroupId = _destinationGroup,
                Query = _querySample.GetQuery(),
                Status = "InProgress"
            };

            _syncJobs.Jobs.Add(testJob);

            var partOneSource = _querySample.GetSourceId(_partIndex);
            await calc.SendMembershipAsync(testJob, _allUsers, _partIndex, true);

            var groupExistsResult = await calc.GroupExistsAsync(partOneSource, Guid.NewGuid());
            Assert.AreEqual(OutcomeType.Successful, groupExistsResult.Outcome);
            Assert.AreEqual(true, groupExistsResult.Result);
        }

        [TestMethod]
        [DataRow(0, 0)]
        [DataRow(3, 0)]
        [DataRow(0, 3)]
        [DataRow(3, 3)]
        public async Task ProperlyGetsAndSendsMembershipWithMultipleSources(int getGroupExceptions, int getMembersExceptions)
        {
            const int userCount = 50000;
            var mockGroups = new Dictionary<Guid, List<AzureADUser>>();

            for (int i = 0; i < userCount; i++)
            {
                var currentGroup = _querySample.GetSourceId(_partIndex);
                var userToAdd = new AzureADUser { ObjectId = Guid.NewGuid() };
                if (mockGroups.TryGetValue(currentGroup, out var users))
                {
                    users.Add(userToAdd);
                }
                else
                {
                    mockGroups.Add(currentGroup, new List<AzureADUser> { userToAdd });
                }
            }

            var graphRepo = new MockGraphGroupRepository()
            {
                GroupsToUsers = mockGroups,
                ThrowSocketExceptionsFromGroupExistsBeforeSuccess = getGroupExceptions,
                ThrowSocketExceptionsFromGetUsersInGroupBeforeSuccess = getMembersExceptions
            };

            var calc = new SGMembershipCalculator(graphRepo, _blobRepository, _syncJobs, _notificationsQueueRepository.Object, _databaseDestinationAttributesRepository.Object, new MockLoggingRepository(), _dryRun);
            var testJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                TargetOfficeGroupId = _destinationGroup,
                Query = _querySample.GetQuery(),
                Status = "InProgress"
            };

            _syncJobs.Jobs.Add(testJob);

            var partOneSource = _querySample.GetSourceId(_partIndex);
            await calc.SendMembershipAsync(testJob, _allUsers, _partIndex, true);

            var groupExistsResult = await calc.GroupExistsAsync(partOneSource, Guid.NewGuid());
            Assert.AreEqual(OutcomeType.Successful, groupExistsResult.Outcome);
            Assert.AreEqual(true, groupExistsResult.Result);
        }

        [TestMethod]
        [DataRow(0, 0)]
        [DataRow(3, 0)]
        [DataRow(0, 3)]
        [DataRow(3, 3)]
        public async Task ProperlyGetsMembersFromPages(int getGroupExceptions, int getMembersExceptions)
        {
            const int userCount = 50000;
            var mockGroups = new Dictionary<Guid, List<AzureADUser>>();

            for (int i = 0; i < userCount; i++)
            {
                var currentGroup = _querySample.GetSourceId(_partIndex);
                var userToAdd = new AzureADUser { ObjectId = Guid.NewGuid() };
                if (mockGroups.TryGetValue(currentGroup, out var users))
                {
                    users.Add(userToAdd);
                }
                else
                {
                    mockGroups.Add(currentGroup, new List<AzureADUser> { userToAdd });
                }
            }

            var graphRepo = new MockGraphGroupRepository()
            {
                GroupsToUsers = mockGroups,
                ThrowSocketExceptionsFromGroupExistsBeforeSuccess = getGroupExceptions,
                ThrowSocketExceptionsFromGetUsersInGroupBeforeSuccess = getMembersExceptions
            };

            var calc = new SGMembershipCalculator(graphRepo, _blobRepository, _syncJobs, _notificationsQueueRepository.Object, _databaseDestinationAttributesRepository.Object, new MockLoggingRepository(), _dryRun);
            var testJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                TargetOfficeGroupId = _destinationGroup,
                Query = _querySample.GetQuery(),
                Status = "InProgress"
            };

            _syncJobs.Jobs.Add(testJob);

            var partOneSource = _querySample.GetSourceId(_partIndex);
            var groupExistsResult = await calc.GroupExistsAsync(partOneSource, Guid.NewGuid());
            var response = await calc.GetFirstTransitiveMembersPageAsync(partOneSource, Guid.NewGuid());
            Assert.IsNotNull(response.NextPageUrl);
            response = await calc.GetNextTransitiveMembersPageAsync("nextPageLink");
            Assert.AreEqual("", response.NextPageUrl);
            Assert.AreEqual(OutcomeType.Successful, groupExistsResult.Outcome);
            Assert.AreEqual(true, groupExistsResult.Result);
        }

        [TestMethod]
        [DataRow(10, 0)]
        [DataRow(0, 10)]
        [DataRow(10, 10)]
        public async Task ProperlyErrorsAfterTooManyRetries(int getGroupExceptions, int getMembersExceptions)
        {
            const int userCount = 2500213;
            Guid[] sourceGroups = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();

            var mockGroups = new Dictionary<Guid, List<AzureADUser>>();
            for (int i = 0; i < userCount; i++)
            {
                var currentGroup = sourceGroups[i % sourceGroups.Length];
                var userToAdd = new AzureADUser { ObjectId = Guid.NewGuid() };
                if (mockGroups.TryGetValue(currentGroup, out var users))
                {
                    users.Add(userToAdd);
                }
                else
                {
                    mockGroups.Add(currentGroup, new List<AzureADUser> { userToAdd });
                }
            }

            var graphRepo = new MockGraphGroupRepository()
            {
                GroupsToUsers = mockGroups,
                ThrowSocketExceptionsFromGroupExistsBeforeSuccess = getGroupExceptions,
                ThrowSocketExceptionsFromGetUsersInGroupBeforeSuccess = getMembersExceptions
            };

            var calc = new SGMembershipCalculator(graphRepo, _blobRepository, _syncJobs, _notificationsQueueRepository.Object, _databaseDestinationAttributesRepository.Object, new MockLoggingRepository(), _dryRun);

            var sampleQuery = QuerySample.GenerateQuerySample("GroupMembership");
            var testJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                TargetOfficeGroupId = _destinationGroup,
                Query = sampleQuery.GetQuery(),
                Status = "InProgress"
            };



            var partIndex = 0;
            var partOneSource = sampleQuery.GetSourceId(partIndex);

            await calc.SendEmailAsync(testJob, NotificationMessageType.NotValidSourceNotification, null);
            Assert.AreEqual(0, _blobRepository.Sent.Count);
        }

        [TestMethod]
        [DataRow(true, false)]
        [DataRow(false, true)]
        [DataRow(true, true)]
        public async Task ProperlyErrorsOnUnexpectedException(bool errorOnGroupExists, bool errorOnGetUsers)
        {
            const int userCount = 2500213;
            Guid[] sourceGroups = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();

            var mockGroups = new Dictionary<Guid, List<AzureADUser>>();
            for (int i = 0; i < userCount; i++)
            {
                var currentGroup = sourceGroups[i % sourceGroups.Length];
                var userToAdd = new AzureADUser { ObjectId = Guid.NewGuid() };
                if (mockGroups.TryGetValue(currentGroup, out var users))
                {
                    users.Add(userToAdd);
                }
                else
                {
                    mockGroups.Add(currentGroup, new List<AzureADUser> { userToAdd });
                }
            }

            var graphRepo = new MockGraphGroupRepository()
            {
                GroupsToUsers = mockGroups,
                ThrowNonSocketExceptionFromGetUsersInGroup = errorOnGetUsers,
                ThrowNonSocketExceptionFromGroupExists = errorOnGroupExists
            };

            var calc = new SGMembershipCalculator(graphRepo, _blobRepository, _syncJobs, _notificationsQueueRepository.Object, _databaseDestinationAttributesRepository.Object, new MockLoggingRepository(), _dryRun);

            var sampleQuery = QuerySample.GenerateQuerySample("GroupMembership");
            var testJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                TargetOfficeGroupId = _destinationGroup,
                Query = sampleQuery.GetQuery(),
                Status = "InProgress",
                RunId = new Guid()
            };

            _syncJobs.Jobs.Add(testJob);

            var partOneSource = _querySample.GetSourceId(_partIndex);

            await calc.SendEmailAsync(testJob, NotificationMessageType.NotValidSourceNotification, null);
            Assert.AreEqual(0, _blobRepository.Sent.Count);
        }

        [TestMethod]
        [DataRow(0, 0)]
        [DataRow(3, 0)]
        [DataRow(0, 3)]
        [DataRow(3, 3)]
        public async Task ProperlyErrorsOnNonexistentGroups(int getGroupExceptions, int getMembersExceptions)
        {
            const int userCount = 2500213;
            var sourceGroups = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();

            var mockGroups = new Dictionary<Guid, List<AzureADUser>>();
            for (int i = 0; i < userCount; i++)
            {
                var currentGroup = sourceGroups[i % sourceGroups.Length];
                var userToAdd = new AzureADUser { ObjectId = Guid.NewGuid() };
                if (mockGroups.TryGetValue(currentGroup, out var users))
                {
                    users.Add(userToAdd);
                }
                else
                {
                    mockGroups.Add(currentGroup, new List<AzureADUser> { userToAdd });
                }
            }

            var graphRepo = new MockGraphGroupRepository()
            {
                GroupsToUsers = mockGroups,
                ThrowSocketExceptionsFromGroupExistsBeforeSuccess = getGroupExceptions,
                ThrowSocketExceptionsFromGetUsersInGroupBeforeSuccess = getMembersExceptions
            };

            var calc = new SGMembershipCalculator(graphRepo, _blobRepository, _syncJobs, _notificationsQueueRepository.Object, _databaseDestinationAttributesRepository.Object, new MockLoggingRepository(), _dryRun);
            var sampleQuery = QuerySample.GenerateQuerySample("GroupMembership");
            var testJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                TargetOfficeGroupId = _destinationGroup,
                Query = sampleQuery.GetQuery(),
                Status = "InProgress"
            };

            _syncJobs.Jobs.Add(testJob);

            var partOneSource = _querySample.GetSourceId(_partIndex);

            await calc.SendEmailAsync(testJob, NotificationMessageType.NotValidSourceNotification, null);
            Assert.AreEqual(0, _blobRepository.Sent.Count);
        }

        [TestMethod]
        [DataRow(0, 0)]
        [DataRow(3, 0)]
        [DataRow(0, 3)]
        [DataRow(3, 3)]
        public async Task ProperlyErrorsOnAllNonexistentGroups(int getGroupExceptions, int getMembersExceptions)
        {
            Guid[] sourceGroups = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
            var graphRepo = new MockGraphGroupRepository()
            {
                GroupsToUsers = new Dictionary<Guid, List<AzureADUser>>(),
                ThrowSocketExceptionsFromGroupExistsBeforeSuccess = getGroupExceptions,
                ThrowSocketExceptionsFromGetUsersInGroupBeforeSuccess = getMembersExceptions
            };

            var calc = new SGMembershipCalculator(graphRepo, _blobRepository, _syncJobs, _notificationsQueueRepository.Object, _databaseDestinationAttributesRepository.Object, new MockLoggingRepository(), _dryRun);

            var sampleQuery = QuerySample.GenerateQuerySample("GroupMembership");
            var testJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                TargetOfficeGroupId = _destinationGroup,
                Query = sampleQuery.GetQuery(),
                Status = "InProgress"
            };

            _syncJobs.Jobs.Add(testJob);

            var partOneSource = _querySample.GetSourceId(_partIndex);
            var groupExistsResult = await calc.GroupExistsAsync(partOneSource, Guid.NewGuid());
            Assert.AreEqual(false, groupExistsResult.Result);
            Assert.AreEqual(0, _blobRepository.Sent.Count);
        }

        [TestMethod]
        [DataRow(0, 0)]
        [DataRow(3, 0)]
        [DataRow(0, 3)]
        [DataRow(3, 3)]
        public async Task IgnoresNonGuidArguments(int getGroupExceptions, int getMembersExceptions)
        {
            const int userCount = 2500213;
            Guid[] sourceGroups = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();

            var mockGroups = new Dictionary<Guid, List<AzureADUser>>();
            for (int i = 0; i < userCount; i++)
            {
                var currentGroup = sourceGroups[i % sourceGroups.Length];
                var userToAdd = new AzureADUser { ObjectId = Guid.NewGuid() };
                if (mockGroups.TryGetValue(currentGroup, out var users))
                {
                    users.Add(userToAdd);
                }
                else
                {
                    mockGroups.Add(currentGroup, new List<AzureADUser> { userToAdd });
                }
            }

            var graphRepo = new MockGraphGroupRepository()
            {
                GroupsToUsers = mockGroups,
                ThrowSocketExceptionsFromGroupExistsBeforeSuccess = getGroupExceptions,
                ThrowSocketExceptionsFromGetUsersInGroupBeforeSuccess = getMembersExceptions
            };

            var calc = new SGMembershipCalculator(graphRepo, _blobRepository, _syncJobs, _notificationsQueueRepository.Object, _databaseDestinationAttributesRepository.Object, new MockLoggingRepository(), _dryRun);

            var sampleQuery = QuerySample.GenerateQuerySample("GroupMembership");
            var testJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                TargetOfficeGroupId = _destinationGroup,
                Query = sampleQuery.GetQuery(),
                Status = "InProgress"
            };

            _syncJobs.Jobs.Add(testJob);

            var partOneSource = _querySample.GetSourceId(_partIndex);
            await calc.SendEmailAsync(testJob, NotificationMessageType.NotValidSourceNotification, null);
            Assert.AreEqual(0, _blobRepository.Sent.Count);
        }
        [TestMethod]
        [DataRow(0, 0)]
        [DataRow(3, 0)]
        [DataRow(0, 3)]
        [DataRow(3, 3)]
        public async Task ReturnsEmptyForNullDestination(int getGroupExceptions, int getMembersExceptions)
        {

            var graphRepo = new MockGraphGroupRepository()
            {
                GroupsToUsers = _groupsToUsers,
                ThrowSocketExceptionsFromGroupExistsBeforeSuccess = getGroupExceptions,
                ThrowSocketExceptionsFromGetUsersInGroupBeforeSuccess = getMembersExceptions
            };
            var calc = new SGMembershipCalculator(graphRepo, _blobRepository, _syncJobs, _notificationsQueueRepository.Object, _databaseDestinationAttributesRepository.Object, new MockLoggingRepository(), _dryRun);
            var syncJob = new SyncJob { Destination = "[{\"type\":\"GroupMembership\",\"value\":{\"objectId\":\"\"}}]\r\n" };
            var result = await calc.GetDestinationNameAsync(syncJob);

            Assert.AreEqual(null, result);
        }
        [TestMethod]
        [DataRow(0, 0)]
        [DataRow(3, 0)]
        [DataRow(0, 3)]
        [DataRow(3, 3)]
        public async Task HandlesInvalidJsonFormat(int getGroupExceptions, int getMembersExceptions)
        {
            var graphRepo = new MockGraphGroupRepository()
            {
                GroupsToUsers = _groupsToUsers,
                ThrowSocketExceptionsFromGroupExistsBeforeSuccess = getGroupExceptions,
                ThrowSocketExceptionsFromGetUsersInGroupBeforeSuccess = getMembersExceptions
            };
            var calc = new SGMembershipCalculator(graphRepo, _blobRepository, _syncJobs, _notificationsQueueRepository.Object, _databaseDestinationAttributesRepository.Object, new MockLoggingRepository(), _dryRun);
            var syncJob = new SyncJob { Destination = "[{\"type\":\"GroupMembership\",\"value\":{\"objectId\":\"\"}}]\r\n" };
            var result = await calc.GetDestinationNameAsync(syncJob);

            Assert.IsNull(result, "Expected null for invalid JSON format");
        }

        [TestMethod]
        [DataRow(0, 0)]
        [DataRow(3, 0)]
        [DataRow(0, 3)]
        [DataRow(3, 3)]
        public async Task HandlesCorrectJsonFormat(int getGroupExceptions, int getMembersExceptions)
        {
            var graphRepo = new MockGraphGroupRepository()
            {
                GroupsToUsers = _groupsToUsers,
                ThrowSocketExceptionsFromGroupExistsBeforeSuccess = getGroupExceptions,
                ThrowSocketExceptionsFromGetUsersInGroupBeforeSuccess = getMembersExceptions
            };
            var calc = new SGMembershipCalculator(graphRepo, _blobRepository, _syncJobs, _notificationsQueueRepository.Object, _databaseDestinationAttributesRepository.Object, new MockLoggingRepository(), _dryRun);
            var syncJob = new SyncJob { Destination = "[{\"type\":\"GroupMembership\",\"value\":{\"objectId\":\"3ec876c5-59c2-44ee-b068-a8575995bf34\"}}]\r\n" };
            var result = await calc.GetDestinationNameAsync(syncJob);

            Assert.IsNotNull(result);
        }
        [TestMethod]
        [DataRow(0, 0)]
        [DataRow(3, 0)]
        [DataRow(0, 3)]
        [DataRow(3, 3)]
        public async Task ReturnsExsitDestination(int getGroupExceptions, int getMembersExceptions)
        {
            var graphRepo = new MockGraphGroupRepository()
            {
                GroupsToUsers = _groupsToUsers,
                ThrowSocketExceptionsFromGroupExistsBeforeSuccess = getGroupExceptions,
                ThrowSocketExceptionsFromGetUsersInGroupBeforeSuccess = getMembersExceptions
            };
            var calc = new SGMembershipCalculator(graphRepo, _blobRepository, _syncJobs, _notificationsQueueRepository.Object, _databaseDestinationAttributesRepository.Object, new MockLoggingRepository(), _dryRun);
            var syncJob = new SyncJob { Destination = "[{\"type\":\"GroupMembership\",\"value\":{\"objectId\":\"3ec876c5-59c2-44ee-b068-a8575995bf34\"}}]\r\n" };
            var result = await calc.GetDestinationNameAsync(syncJob);

            Assert.AreEqual("GroupName", result);
        }
    }
}