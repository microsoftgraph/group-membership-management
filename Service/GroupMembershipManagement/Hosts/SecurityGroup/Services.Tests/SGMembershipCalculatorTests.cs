// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Hosts.SecurityGroup;
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

namespace Tests.FunctionApps
{
    [TestClass]
    public class SGMembershipCalculatorTests
    {
        [TestMethod]
        [DataRow(0, 0)]
        [DataRow(3, 0)]
        [DataRow(0, 3)]
        [DataRow(3, 3)]
        public async Task ProperlyGetsAndSendsMembership(int getGroupExceptions, int getMembersExceptions)
        {
            const int userCount = 50000;
            var allUsers = new List<AzureADUser> { };
            var destinationGroup = Guid.NewGuid();
            var partIndex = 0;
            var sampleQuery = QuerySample.GenerateQuerySample("SecurityGroup");
            var groupsToUsers = sampleQuery.QueryParts[partIndex].SourceIds.ToDictionary(
                                                                                x => x,
                                                                                x => Enumerable.Range(0, userCount)
                                                                                                .Select(x => new AzureADUser { ObjectId = Guid.NewGuid() }).ToList());
            var graphRepo = new MockGraphGroupRepository()
            {
                GroupsToUsers = groupsToUsers,
                ThrowSocketExceptionsFromGroupExistsBeforeSuccess = getGroupExceptions,
                ThrowSocketExceptionsFromGetUsersInGroupBeforeSuccess = getMembersExceptions
            };

            var mail = new MockMailRepository();
            var mailAddresses = new MockEmail<IEmailSenderRecipient>();
            var syncJobs = new MockSyncJobRepository();
            var dryRun = new MockDryRunValue() { DryRunEnabled = false };
            var blobRepository = new MockBlobStorageRepository();
            var calc = new SGMembershipCalculator(graphRepo, blobRepository, mail, mailAddresses, syncJobs, new MockLoggingRepository(), dryRun);
            var testJob = new SyncJob
            {
                RowKey = "row",
                PartitionKey = "partition",
                TargetOfficeGroupId = destinationGroup,
                Query = sampleQuery.GetQuery(),
                Status = "InProgress"
            };

            syncJobs.ExistingSyncJobs.Add((testJob.RowKey, testJob.PartitionKey), testJob);

            var partOneSources = sampleQuery.GetSourceIds(partIndex);
            var groups = calc.ReadSourceGroups(partOneSources);
            await calc.SendMembershipAsync(testJob, allUsers, partIndex);
            foreach (var group in groups)
            {
                var groupExistsResult = await calc.GroupExistsAsync(group.ObjectId, Guid.NewGuid());
                Assert.AreEqual(OutcomeType.Successful, groupExistsResult.Outcome);
                Assert.AreEqual(true, groupExistsResult.Result);
            }

            Assert.AreEqual(sampleQuery.QueryParts[partIndex].SourceIds.Count, groups.Length);
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
            var partIndex = 0;
            var sampleQuery = QuerySample.GenerateQuerySample("SecurityGroup");
            var groupsToUsers = sampleQuery.QueryParts[partIndex].SourceIds.ToDictionary(
                                                                                x => x,
                                                                                x => Enumerable.Range(0, userCount)
                                                                                                .Select(x => new AzureADUser { ObjectId = Guid.NewGuid() }).ToList());

            var mockGroups = new Dictionary<Guid, List<AzureADUser>>();
            for (int i = 0; i < userCount; i++)
            {
                var currentGroup = sampleQuery.QueryParts[partIndex].SourceIds[i % sampleQuery.QueryParts[partIndex].SourceIds.Count];
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
            var calc = new SGMembershipCalculator(graphRepo, blobRepository, mail, mailAddresses, syncJobs, new MockLoggingRepository(), dryRun);
            var testJob = new SyncJob
            {
                RowKey = "row",
                PartitionKey = "partition",
                TargetOfficeGroupId = destinationGroup,
                Query = sampleQuery.GetQuery(),
                Status = "InProgress"
            };

            syncJobs.ExistingSyncJobs.Add((testJob.RowKey, testJob.PartitionKey), testJob);

            var partOneSources = sampleQuery.GetSourceIds(partIndex);
            var groups = calc.ReadSourceGroups(partOneSources);
            await calc.SendMembershipAsync(testJob, allUsers, partIndex);

            foreach (var group in groups)
            {
                var groupExistsResult = await calc.GroupExistsAsync(group.ObjectId, Guid.NewGuid());
                Assert.AreEqual(OutcomeType.Successful, groupExistsResult.Outcome);
                Assert.AreEqual(true, groupExistsResult.Result);
            }

            Assert.AreEqual(sampleQuery.QueryParts[partIndex].SourceIds.Count, groups.Length);
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
            var partIndex = 0;
            var sampleQuery = QuerySample.GenerateQuerySample("SecurityGroup");
            var groupsToUsers = sampleQuery.QueryParts[partIndex].SourceIds.ToDictionary(
                                                                                x => x,
                                                                                x => Enumerable.Range(0, userCount)
                                                                                                .Select(x => new AzureADUser { ObjectId = Guid.NewGuid() }).ToList());

            for (int i = 0; i < userCount; i++)
            {
                var currentGroup = sampleQuery.QueryParts[partIndex].SourceIds[i % sampleQuery.QueryParts[partIndex].SourceIds.Count];
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
            var calc = new SGMembershipCalculator(graphRepo, blobRepository, mail, mailAddresses, syncJobs, new MockLoggingRepository(), dryRun);
            var testJob = new SyncJob
            {
                RowKey = "row",
                PartitionKey = "partition",
                TargetOfficeGroupId = destinationGroup,
                Query = sampleQuery.GetQuery(),
                Status = "InProgress"
            };

            syncJobs.ExistingSyncJobs.Add((testJob.RowKey, testJob.PartitionKey), testJob);

            var partOneSources = sampleQuery.GetSourceIds(partIndex);
            var groups = calc.ReadSourceGroups(partOneSources);
            foreach (var group in groups)
            {
                var groupExistsResult = await calc.GroupExistsAsync(group.ObjectId, Guid.NewGuid());
                var response = await calc.GetFirstTransitiveMembersPageAsync(group.ObjectId, Guid.NewGuid());
                Assert.IsNotNull(response.nextPageUrl);
                response = await calc.GetNextTransitiveMembersPageAsync("nextPageLink", response.usersFromGroup);
                Assert.AreEqual("", response.nextPageUrl);
                Assert.AreEqual(OutcomeType.Successful, groupExistsResult.Outcome);
                Assert.AreEqual(true, groupExistsResult.Result);
            }

            Assert.AreEqual(sampleQuery.QueryParts[partIndex].SourceIds.Count, groups.Length);
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
            var partOneSources = sampleQuery.GetSourceIds(partIndex);
            var groups = calc.ReadSourceGroups(partOneSources);
            foreach (var group in groups)
            {
                await calc.SendEmailAsync(testJob, Guid.NewGuid(), "Content", null);
            }

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
            var partOneSources = sampleQuery.GetSourceIds(partIndex);
            var groups = calc.ReadSourceGroups(partOneSources);
            foreach (var group in groups)
            {
                await calc.SendEmailAsync(testJob, Guid.NewGuid(), "Content", null);
            }
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

            var calc = new SGMembershipCalculator(graphRepo, blobRepository, mail, mailAddresses, syncJobs, new MockLoggingRepository(), dryRun);

            var sampleQuery = QuerySample.GenerateQuerySample("SecurityGroup");
            var testJob = new SyncJob
            {
                RowKey = "row",
                PartitionKey = "partition",
                TargetOfficeGroupId = destinationGroup,
                Query =sampleQuery.GetQuery(),
                Status = "InProgress"
            };

            syncJobs.ExistingSyncJobs.Add((testJob.RowKey, testJob.PartitionKey), testJob);

            var partIndex = 0;
            var partOneSources = sampleQuery.GetSourceIds(partIndex);
            var groups = calc.ReadSourceGroups(partOneSources);
            foreach (var group in groups)
            {
                await calc.SendEmailAsync(testJob, Guid.NewGuid(), "Content", null);
            }
            Assert.AreEqual(0, blobRepository.Sent.Count);
        }

        [TestMethod]
        [DataRow(0, 0)]
        [DataRow(3, 0)]
        [DataRow(0, 3)]
        [DataRow(3, 3)]
        public void ProperlyErrorsOnAllNonGuidGroups(int getGroupExceptions, int getMembersExceptions)
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

            var calc = new SGMembershipCalculator(graphRepo, blobRepository, mail, mailAddresses, syncJobs, new MockLoggingRepository(), dryRun);

            var testJob = new SyncJob
            {
                RowKey = "row",
                PartitionKey = "partition",
                TargetOfficeGroupId = destinationGroup,
                Query = "asfdasdf;asfdoiu;oasfdjlsadhfa;;;",
                Status = "InProgress"
            };

            syncJobs.ExistingSyncJobs.Add((testJob.RowKey, testJob.PartitionKey), testJob);

            var groups = calc.ReadSourceGroups(testJob.Query);
            Assert.AreEqual(0, groups.Length);
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
            var partOneSources = sampleQuery.GetSourceIds(partIndex);
            var groups = calc.ReadSourceGroups(partOneSources);
            foreach (var group in groups)
            {
                var groupExistsResult = await calc.GroupExistsAsync(group.ObjectId, Guid.NewGuid());
                Assert.AreEqual(false, groupExistsResult.Result);
            }
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
            var partOneSources = sampleQuery.GetSourceIds(partIndex);
            var groups = calc.ReadSourceGroups(partOneSources);
            foreach (var group in groups)
            {
                await calc.SendEmailAsync(testJob, Guid.NewGuid(), "Content", null);
            }
            Assert.AreEqual(0, blobRepository.Sent.Count);
        }
    }
}