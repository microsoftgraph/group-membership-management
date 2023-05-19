// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.SecurityGroup;
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

namespace Tests.FunctionApps
{
    [TestClass]
    public class SGMembershipCalculatorTests
    {
        int _partIndex = 0;
        private QuerySample _querySample;
        private Dictionary<Guid, List<AzureADUser>> _groupsToUsers;

        [TestInitialize]
        public void Setup()
        {
            _querySample = QuerySample.GenerateQuerySample("SecurityGroup");
            _groupsToUsers = new Dictionary<Guid, List<AzureADUser>>
            {
                { _querySample.QueryParts[_partIndex].SourceId, new List<AzureADUser> { new AzureADUser { ObjectId = Guid.NewGuid() } } }
            };
        }


        [TestMethod]
        [DataRow(0, 0)]
        [DataRow(3, 0)]
        [DataRow(0, 3)]
        [DataRow(3, 3)]
        public async Task ProperlyGetsAndSendsMembership(int getGroupExceptions, int getMembersExceptions)
        {
            var allUsers = new List<AzureADUser>();
            var destinationGroup = Guid.NewGuid();
            var graphRepo = new MockGraphGroupRepository()
            {
                GroupsToUsers = _groupsToUsers,
                ThrowSocketExceptionsFromGroupExistsBeforeSuccess = getGroupExceptions,
                ThrowSocketExceptionsFromGetUsersInGroupBeforeSuccess = getMembersExceptions
            };

            var mail = new MockMailRepository();
            var mailAddresses = new MockEmail<IEmailSenderRecipient>();
            var syncJobs = new MockSyncJobRepository();
            var dryRun = new MockDryRunValue() { DryRunEnabled = false };
            var blobRepository = new MockBlobStorageRepository();
            var telemetryClient = new TelemetryClient(new TelemetryConfiguration());
            var calc = new SGMembershipCalculator(graphRepo, blobRepository, mail, mailAddresses, syncJobs, new MockLoggingRepository(), dryRun);
            var testJob = new SyncJob
            {
                RowKey = "row",
                PartitionKey = "partition",
                TargetOfficeGroupId = destinationGroup,
                Query = _querySample.GetQuery(),
                Status = "InProgress"
            };

            syncJobs.ExistingSyncJobs.Add((testJob.RowKey, testJob.PartitionKey), testJob);

            var partOneSource = _querySample.GetSourceId(_partIndex);
            await calc.SendMembershipAsync(testJob, allUsers, _partIndex, true);

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
            var destinationGroup = Guid.NewGuid();
            var allUsers = new List<AzureADUser>();
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

            var mail = new MockMailRepository();
            var mailAddresses = new MockEmail<IEmailSenderRecipient>();
            var syncJobs = new MockSyncJobRepository();
            var dryRun = new MockDryRunValue() { DryRunEnabled = false };
            var blobRepository = new MockBlobStorageRepository();
            var telemetryClient = new TelemetryClient(new TelemetryConfiguration());
            var calc = new SGMembershipCalculator(graphRepo, blobRepository, mail, mailAddresses, syncJobs, new MockLoggingRepository(), dryRun);
            var testJob = new SyncJob
            {
                RowKey = "row",
                PartitionKey = "partition",
                TargetOfficeGroupId = destinationGroup,
                Query = _querySample.GetQuery(),
                Status = "InProgress"
            };

            syncJobs.ExistingSyncJobs.Add((testJob.RowKey, testJob.PartitionKey), testJob);

            var partOneSource = _querySample.GetSourceId(_partIndex);
            await calc.SendMembershipAsync(testJob, allUsers, _partIndex, true);

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
            var destinationGroup = Guid.NewGuid();
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

            var mail = new MockMailRepository();
            var mailAddresses = new MockEmail<IEmailSenderRecipient>();
            var syncJobs = new MockSyncJobRepository();
            var dryRun = new MockDryRunValue() { DryRunEnabled = false };
            var blobRepository = new MockBlobStorageRepository();
            var telemetryClient = new TelemetryClient(new TelemetryConfiguration());
            var calc = new SGMembershipCalculator(graphRepo, blobRepository, mail, mailAddresses, syncJobs, new MockLoggingRepository(), dryRun);
            var testJob = new SyncJob
            {
                RowKey = "row",
                PartitionKey = "partition",
                TargetOfficeGroupId = destinationGroup,
                Query = _querySample.GetQuery(),
                Status = "InProgress"
            };

            syncJobs.ExistingSyncJobs.Add((testJob.RowKey, testJob.PartitionKey), testJob);

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
            Guid destinationGroup = Guid.NewGuid();

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

            var mail = new MockMailRepository();
            var mailAddresses = new MockEmail<IEmailSenderRecipient>();
            var syncJobs = new MockSyncJobRepository();
            var dryRun = new MockDryRunValue() { DryRunEnabled = false };
            var blobRepository = new MockBlobStorageRepository();
            var telemetryClient = new TelemetryClient(new TelemetryConfiguration());

            var calc = new SGMembershipCalculator(graphRepo, blobRepository, mail, mailAddresses, syncJobs, new MockLoggingRepository(), dryRun);

            var sampleQuery = QuerySample.GenerateQuerySample("SecurityGroup");
            var testJob = new SyncJob
            {
                RowKey = "row",
                PartitionKey = "partition",
                TargetOfficeGroupId = destinationGroup,
                Query = sampleQuery.GetQuery(),
                Status = "InProgress"
            };

            syncJobs.ExistingSyncJobs.Add((testJob.RowKey, testJob.PartitionKey), testJob);

            var partIndex = 0;
            var partOneSource = sampleQuery.GetSourceId(partIndex);

            await calc.SendEmailAsync(testJob, Guid.NewGuid(), "Subject", "Content", null);
            Assert.AreEqual(0, blobRepository.Sent.Count);
        }

        [TestMethod]
        [DataRow(true, false)]
        [DataRow(false, true)]
        [DataRow(true, true)]
        public async Task ProperlyErrorsOnUnexpectedException(bool errorOnGroupExists, bool errorOnGetUsers)
        {
            const int userCount = 2500213;
            Guid[] sourceGroups = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
            Guid destinationGroup = Guid.NewGuid();

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

            var mail = new MockMailRepository();
            var mailAddresses = new MockEmail<IEmailSenderRecipient>();
            var syncJobs = new MockSyncJobRepository();
            var dryRun = new MockDryRunValue() { DryRunEnabled = false };
            var blobRepository = new MockBlobStorageRepository();
            var telemetryClient = new TelemetryClient(new TelemetryConfiguration());

            var calc = new SGMembershipCalculator(graphRepo, blobRepository, mail, mailAddresses, syncJobs, new MockLoggingRepository(), dryRun);

            var sampleQuery = QuerySample.GenerateQuerySample("SecurityGroup");
            var testJob = new SyncJob
            {
                RowKey = "row",
                PartitionKey = "partition",
                TargetOfficeGroupId = destinationGroup,
                Query = sampleQuery.GetQuery(),
                Status = "InProgress",
                RunId = new Guid()
            };

            syncJobs.ExistingSyncJobs.Add((testJob.RowKey, testJob.PartitionKey), testJob);

            var partOneSource = _querySample.GetSourceId(_partIndex);

            await calc.SendEmailAsync(testJob, Guid.NewGuid(), "Subject", "Content", null);
            Assert.AreEqual(0, blobRepository.Sent.Count);
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
            Guid destinationGroup = Guid.NewGuid();

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

            var mail = new MockMailRepository();
            var mailAddresses = new MockEmail<IEmailSenderRecipient>();
            var syncJobs = new MockSyncJobRepository();
            var dryRun = new MockDryRunValue() { DryRunEnabled = false };
            var blobRepository = new MockBlobStorageRepository();
            var telemetryClient = new TelemetryClient(new TelemetryConfiguration());

            var calc = new SGMembershipCalculator(graphRepo, blobRepository, mail, mailAddresses, syncJobs, new MockLoggingRepository(), dryRun);

            var sampleQuery = QuerySample.GenerateQuerySample("SecurityGroup");
            var testJob = new SyncJob
            {
                RowKey = "row",
                PartitionKey = "partition",
                TargetOfficeGroupId = destinationGroup,
                Query = sampleQuery.GetQuery(),
                Status = "InProgress"
            };

            syncJobs.ExistingSyncJobs.Add((testJob.RowKey, testJob.PartitionKey), testJob);

            var partOneSource = _querySample.GetSourceId(_partIndex);

            await calc.SendEmailAsync(testJob, Guid.NewGuid(), "Subject", "Content", null);
            Assert.AreEqual(0, blobRepository.Sent.Count);
        }

        [TestMethod]
        [DataRow(0, 0)]
        [DataRow(3, 0)]
        [DataRow(0, 3)]
        [DataRow(3, 3)]
        public async Task ProperlyErrorsOnAllNonexistentGroups(int getGroupExceptions, int getMembersExceptions)
        {
            Guid[] sourceGroups = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
            Guid destinationGroup = Guid.NewGuid();
            var graphRepo = new MockGraphGroupRepository()
            {
                GroupsToUsers = new Dictionary<Guid, List<AzureADUser>>(),
                ThrowSocketExceptionsFromGroupExistsBeforeSuccess = getGroupExceptions,
                ThrowSocketExceptionsFromGetUsersInGroupBeforeSuccess = getMembersExceptions
            };

            var mail = new MockMailRepository();
            var mailAddresses = new MockEmail<IEmailSenderRecipient>();
            var syncJobs = new MockSyncJobRepository();
            var dryRun = new MockDryRunValue() { DryRunEnabled = false };
            var blobRepository = new MockBlobStorageRepository();
            var telemetryClient = new TelemetryClient(new TelemetryConfiguration());

            var calc = new SGMembershipCalculator(graphRepo, blobRepository, mail, mailAddresses, syncJobs, new MockLoggingRepository(), dryRun);

            var sampleQuery = QuerySample.GenerateQuerySample("SecurityGroup");
            var testJob = new SyncJob
            {
                RowKey = "row",
                PartitionKey = "partition",
                TargetOfficeGroupId = destinationGroup,
                Query = sampleQuery.GetQuery(),
                Status = "InProgress"
            };

            syncJobs.ExistingSyncJobs.Add((testJob.RowKey, testJob.PartitionKey), testJob);

            var partOneSource = _querySample.GetSourceId(_partIndex);
            var groupExistsResult = await calc.GroupExistsAsync(partOneSource, Guid.NewGuid());
            Assert.AreEqual(false, groupExistsResult.Result);
            Assert.AreEqual(0, blobRepository.Sent.Count);
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
            Guid destinationGroup = Guid.NewGuid();

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

            var mail = new MockMailRepository();
            var mailAddresses = new MockEmail<IEmailSenderRecipient>();
            var syncJobs = new MockSyncJobRepository();
            var dryRun = new MockDryRunValue() { DryRunEnabled = false };
            var blobRepository = new MockBlobStorageRepository();
            var telemetryClient = new TelemetryClient(new TelemetryConfiguration());

            var calc = new SGMembershipCalculator(graphRepo, blobRepository, mail, mailAddresses, syncJobs, new MockLoggingRepository(), dryRun);

            var sampleQuery = QuerySample.GenerateQuerySample("SecurityGroup");
            var testJob = new SyncJob
            {
                RowKey = "row",
                PartitionKey = "partition",
                TargetOfficeGroupId = destinationGroup,
                Query = sampleQuery.GetQuery(),
                Status = "InProgress"
            };

            syncJobs.ExistingSyncJobs.Add((testJob.RowKey, testJob.PartitionKey), testJob);

            var partOneSource = _querySample.GetSourceId(_partIndex);
            await calc.SendEmailAsync(testJob, Guid.NewGuid(), "Subject", "Content", null);
            Assert.AreEqual(0, blobRepository.Sent.Count);
        }
    }
}