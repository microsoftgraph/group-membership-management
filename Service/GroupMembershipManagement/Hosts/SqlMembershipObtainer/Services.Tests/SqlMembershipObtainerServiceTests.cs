// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.ServiceBus;
using Moq;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.Mocks;
using Services.Contracts;
using Services.Tests.Helpers;
using SqlMembershipObtainer.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class SqlMembershipObtainerServiceTests
    {
        public SqlMembershipObtainerServiceTests()
        {
        }

        [TestMethod]
        public async Task SendGroupMembershipTest()
        {
            var sqlMembershipRepository = new Mock<ISqlMembershipRepository>();
            var blobStorageRepository = new Mock<IBlobStorageRepository>();
            var syncJobStatusService = new Mock<ISyncJobStatusService>();
            var groupsRepository = new Mock<IDatabaseGroupsRepository>();
            var channelsRepository = new Mock<IDatabaseChannelsRepository>();
            var logger = NullLogger<SqlMembershipObtainerService>.Instance;
            var telemetryClient = new TelemetryClient(new TelemetryConfiguration());
            var dryRunValue = new Mock<IDryRunValue>();
            var groupMembership = default(GroupMembership);
            var messages = new List<string>();
            var dfRepository = new Mock<IDataFactoryRepository>();
            var dfService = new Mock<IDataFactoryService>();

            blobStorageRepository.Setup(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                                    .Callback<string, string, Dictionary<string, string>>((path, content, metadata) =>
                                    {
                                        groupMembership = JsonSerializer.Deserialize<GroupMembership>(content);
                                    });

            var currentPart = 1;
            var syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                MembershipType = "GroupMembership"
            };
            syncJob.Group = new Group
            {
                SyncJobId = syncJob.Id,
                GroupId = Guid.NewGuid()
            };

            var organization = new OrganizationCreator().GenerateOrganizationHierarchy();
            var profiles = organization.SelectMany(x => x.Entities)
                                        .Select(x => new GraphProfileInformation { Id = x.AzureObjectId, PersonnelNumber = x.PersonnelNumber })
                                        .ToList();

            var sqlMembershipObtainerService = new SqlMembershipObtainerService(
                                            sqlMembershipRepository.Object,
                                            blobStorageRepository.Object,
                                            syncJobStatusService.Object,
                                            new MockDestinationResolver(groupsRepository.Object, channelsRepository.Object),
                                            logger,
                                            telemetryClient,
                                            dryRunValue.Object,
                                            dfService.Object);

            await sqlMembershipObtainerService.UploadMembershipFileAsync(profiles, syncJob, syncJob.Group.GroupId, currentPart, false);

            blobStorageRepository.Verify(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()), Times.Once());
            Assert.AreEqual(profiles.Count, groupMembership.SourceMembers.Count);
        }

        private static readonly string[] _conformanceCorpus =
        {
            "f0e1d2c3-b4a5-9687-7869-5a4b3c2d1e0f",
            "00000100-0000-0000-0000-000000000000",
            "00000001-0000-0000-0000-000000000000",
            "80000000-0000-0000-0000-000000000000",
            "7fffffff-ffff-ffff-ffff-ffffffffffff",
            "00000000-8000-0000-0000-000000000000",
            "00000000-7fff-0000-0000-000000000000",
            "00000000-0000-8000-0000-000000000000",
            "00000000-0000-7fff-0000-000000000000",
            "00000000-0000-0000-8000-000000000000",
            "00000000-0000-0000-7f00-000000000000",
            "00000000-0000-0000-0000-000080000000",
            "00000000-0000-0000-0000-00007f000000",
            "00000000-0000-0000-0000-000000000080",
            "00000000-0000-0000-0000-00000000007f",
            "ffffffff-ffff-ffff-ffff-ffffffffffff",
            "00000000-0000-0000-0000-000000000000",
        };

        private static List<Guid> ExpectedCanonicalOrder() =>
            _conformanceCorpus.Select(Guid.Parse)
                             .OrderBy(g => g.ToString("D"), StringComparer.Ordinal)
                             .ToList();

        [TestMethod]
        public async Task UploadMembershipFileAsync_StagesMembersInCanonicalOrder()
        {
            var blobStorageRepository = new Mock<IBlobStorageRepository>();
            var groupMembership = default(GroupMembership);
            blobStorageRepository.Setup(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                                 .Callback<string, string, Dictionary<string, string>>((path, content, metadata) =>
                                 {
                                     groupMembership = JsonSerializer.Deserialize<GroupMembership>(content);
                                 });

            var service = new SqlMembershipObtainerService(
                                new Mock<ISqlMembershipRepository>().Object,
                                blobStorageRepository.Object,
                                new Mock<ISyncJobStatusService>().Object,
                                new MockDestinationResolver(
                                    new Mock<IDatabaseGroupsRepository>().Object,
                                    new Mock<IDatabaseChannelsRepository>().Object),
                                NullLogger<SqlMembershipObtainerService>.Instance,
                                new TelemetryClient(new TelemetryConfiguration()),
                                new Mock<IDryRunValue>().Object,
                                new Mock<IDataFactoryService>().Object);

            var syncJob = new SyncJob { Id = Guid.NewGuid(), RunId = Guid.NewGuid(), MembershipType = "GroupMembership" };
            syncJob.Group = new Group { SyncJobId = syncJob.Id, GroupId = Guid.NewGuid() };

            // Deliberately unsorted input, so a missing sort cannot pass by accident.
            var profiles = _conformanceCorpus
                .Select((id, i) => new GraphProfileInformation { Id = id, PersonnelNumber = i.ToString() })
                .ToList();

            await service.UploadMembershipFileAsync(profiles, syncJob, syncJob.Group.GroupId, 1, false);

            var staged = groupMembership.SourceMembers.Select(x => x.ObjectId).ToList();

            CollectionAssert.AreEqual(ExpectedCanonicalOrder(), staged,
                "SqlMembershipObtainer staged members in an order other than the canonical one.");

        }

        [TestMethod]
        public async Task GetChildEntitiesAsync_ReturnsExpectedResponse_AndProfilesCount()
        {
            var sqlMembershipRepository = new Mock<ISqlMembershipRepository>();
            var blobStorageRepository = new Mock<IBlobStorageRepository>();
            var syncJobStatusService = new Mock<ISyncJobStatusService>();
            var groupsRepository = new Mock<IDatabaseGroupsRepository>();
            var channelsRepository = new Mock<IDatabaseChannelsRepository>();
            var logger = NullLogger<SqlMembershipObtainerService>.Instance;
            var telemetryClient = new TelemetryClient(new TelemetryConfiguration());
            var dryRunValue = new Mock<IDryRunValue>();
            var dfService = new Mock<IDataFactoryService>();

            var syncJob = new SyncJob { Id = Guid.NewGuid(), RunId = Guid.NewGuid() };
            var groupId = Guid.NewGuid();
            var currentPart = 1;
            var tableName = "TestTable";
            var filter = "filter";
            var depth = 2;
            var exclusionary = false;

            var personEntities = new List<PersonEntity>
            {
                new PersonEntity { PersonnelNumber = "1", AzureObjectId = Guid.NewGuid().ToString() },
                new PersonEntity { PersonnelNumber = "2", AzureObjectId = Guid.NewGuid().ToString() }
            };

            sqlMembershipRepository
                .Setup(x => x.GetChildEntitiesAsync(filter, 0, tableName, depth))
                .ReturnsAsync(personEntities);

            var sqlMembershipObtainerService = new SqlMembershipObtainerService(
                                            sqlMembershipRepository.Object,
                                            blobStorageRepository.Object,
                                            syncJobStatusService.Object,
                                            new MockDestinationResolver(groupsRepository.Object, channelsRepository.Object),
                                            logger,
                                            telemetryClient,
                                            dryRunValue.Object,
                                            dfService.Object);

            var result = await sqlMembershipObtainerService.GetChildEntitiesAsync(filter, 0, tableName, depth, syncJob, groupId, currentPart, exclusionary);

            Assert.IsNotNull(result);
            Assert.AreEqual(SyncStatus.InProgress, result.Status);
        }

        [TestMethod]
        public async Task FilterChildEntitiesAsync_ReturnsExpectedResponse_AndProfilesCount()
        {
            var sqlMembershipRepository = new Mock<ISqlMembershipRepository>();
            var blobStorageRepository = new Mock<IBlobStorageRepository>();
            var syncJobStatusService = new Mock<ISyncJobStatusService>();
            var groupsRepository = new Mock<IDatabaseGroupsRepository>();
            var channelsRepository = new Mock<IDatabaseChannelsRepository>();
            var logger = NullLogger<SqlMembershipObtainerService>.Instance;
            var telemetryClient = new TelemetryClient(new TelemetryConfiguration());
            var dryRunValue = new Mock<IDryRunValue>();
            var dfService = new Mock<IDataFactoryService>();

            var syncJob = new SyncJob { Id = Guid.NewGuid(), RunId = Guid.NewGuid() };
            var groupId = Guid.NewGuid();
            var currentPart = 1;
            var tableName = "TestTable";
            var query = "query";
            var exclusionary = false;

            var personEntities = new List<PersonEntity>
            {
                new PersonEntity { PersonnelNumber = "1", AzureObjectId = Guid.NewGuid().ToString() },
                new PersonEntity { PersonnelNumber = "2", AzureObjectId = Guid.NewGuid().ToString() }
            };

            sqlMembershipRepository
                .Setup(x => x.FilterChildEntitiesAsync(query, tableName))
                .ReturnsAsync(personEntities);

            var sqlMembershipObtainerService = new SqlMembershipObtainerService(
                                            sqlMembershipRepository.Object,
                                            blobStorageRepository.Object,
                                            syncJobStatusService.Object,
                                            new MockDestinationResolver(groupsRepository.Object, channelsRepository.Object),
                                            logger,
                                            telemetryClient,
                                            dryRunValue.Object,
                                            dfService.Object);

            var result = await sqlMembershipObtainerService.FilterChildEntitiesAsync(query, tableName, syncJob, groupId, currentPart, exclusionary);

            Assert.IsNotNull(result);
            Assert.AreEqual(SyncStatus.InProgress, result.Status);
        }

        [TestMethod]
        public async Task FilterChildEntitiesAsync_NullEmployeeId_ThrowsInvalidOperationException()
        {
            var sqlMembershipRepository = new Mock<ISqlMembershipRepository>();
            var blobStorageRepository = new Mock<IBlobStorageRepository>();
            var syncJobStatusService = new Mock<ISyncJobStatusService>();
            var groupsRepository = new Mock<IDatabaseGroupsRepository>();
            var channelsRepository = new Mock<IDatabaseChannelsRepository>();
            var logger = NullLogger<SqlMembershipObtainerService>.Instance;
            var telemetryClient = new TelemetryClient(new TelemetryConfiguration());
            var dryRunValue = new Mock<IDryRunValue>();
            var dfService = new Mock<IDataFactoryService>();

            var syncJob = new SyncJob { Id = Guid.NewGuid(), RunId = Guid.NewGuid() };
            var groupId = Guid.NewGuid();
            var currentPart = 1;
            var tableName = "TestTable";
            var query = "query";
            var exclusionary = false;

            var personEntities = new List<PersonEntity>
            {
                new PersonEntity { PersonnelNumber = "1", AzureObjectId = Guid.NewGuid().ToString() },
                new PersonEntity { PersonnelNumber = null, AzureObjectId = Guid.NewGuid().ToString() }
            };

            sqlMembershipRepository
                .Setup(x => x.FilterChildEntitiesAsync(query, tableName))
                .ReturnsAsync(personEntities);

            var sqlMembershipObtainerService = new SqlMembershipObtainerService(
                                            sqlMembershipRepository.Object,
                                            blobStorageRepository.Object,
                                            syncJobStatusService.Object,
                                            new MockDestinationResolver(groupsRepository.Object, channelsRepository.Object),
                                            logger,
                                            telemetryClient,
                                            dryRunValue.Object,
                                            dfService.Object);

            var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => sqlMembershipObtainerService.FilterChildEntitiesAsync(query, tableName, syncJob, groupId, currentPart, exclusionary));

            StringAssert.Contains(exception.Message, "EmployeeId");
            StringAssert.Contains(exception.Message, "1 HR row(s)");
            blobStorageRepository.Verify(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()), Times.Never());
        }

        [TestMethod]
        public async Task FilterChildEntitiesAsync_NullAzureObjectId_ThrowsInvalidOperationException()
        {
            var sqlMembershipRepository = new Mock<ISqlMembershipRepository>();
            var blobStorageRepository = new Mock<IBlobStorageRepository>();
            var syncJobStatusService = new Mock<ISyncJobStatusService>();
            var groupsRepository = new Mock<IDatabaseGroupsRepository>();
            var channelsRepository = new Mock<IDatabaseChannelsRepository>();
            var logger = NullLogger<SqlMembershipObtainerService>.Instance;
            var telemetryClient = new TelemetryClient(new TelemetryConfiguration());
            var dryRunValue = new Mock<IDryRunValue>();
            var dfService = new Mock<IDataFactoryService>();

            var syncJob = new SyncJob { Id = Guid.NewGuid(), RunId = Guid.NewGuid() };
            var groupId = Guid.NewGuid();
            var currentPart = 1;
            var tableName = "TestTable";
            var query = "query";
            var exclusionary = false;

            var personEntities = new List<PersonEntity>
            {
                new PersonEntity { PersonnelNumber = "1", AzureObjectId = Guid.NewGuid().ToString() },
                new PersonEntity { PersonnelNumber = "2", AzureObjectId = null }
            };

            sqlMembershipRepository
                .Setup(x => x.FilterChildEntitiesAsync(query, tableName))
                .ReturnsAsync(personEntities);

            var sqlMembershipObtainerService = new SqlMembershipObtainerService(
                                            sqlMembershipRepository.Object,
                                            blobStorageRepository.Object,
                                            syncJobStatusService.Object,
                                            new MockDestinationResolver(groupsRepository.Object, channelsRepository.Object),
                                            logger,
                                            telemetryClient,
                                            dryRunValue.Object,
                                            dfService.Object);

            var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => sqlMembershipObtainerService.FilterChildEntitiesAsync(query, tableName, syncJob, groupId, currentPart, exclusionary));

            StringAssert.Contains(exception.Message, "AzureObjectId");
            StringAssert.Contains(exception.Message, "1 HR row(s)");
            blobStorageRepository.Verify(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()), Times.Never());
        }

        [TestMethod]
        public async Task GetChildEntitiesAsync_NullAzureObjectId_ThrowsInvalidOperationException()
        {
            var sqlMembershipRepository = new Mock<ISqlMembershipRepository>();
            var blobStorageRepository = new Mock<IBlobStorageRepository>();
            var syncJobStatusService = new Mock<ISyncJobStatusService>();
            var groupsRepository = new Mock<IDatabaseGroupsRepository>();
            var channelsRepository = new Mock<IDatabaseChannelsRepository>();
            var logger = NullLogger<SqlMembershipObtainerService>.Instance;
            var telemetryClient = new TelemetryClient(new TelemetryConfiguration());
            var dryRunValue = new Mock<IDryRunValue>();
            var dfService = new Mock<IDataFactoryService>();

            var syncJob = new SyncJob { Id = Guid.NewGuid(), RunId = Guid.NewGuid() };
            var groupId = Guid.NewGuid();
            var currentPart = 1;
            var tableName = "TestTable";
            var filter = "filter";
            var depth = 2;
            var exclusionary = false;

            var personEntities = new List<PersonEntity>
            {
                new PersonEntity { PersonnelNumber = "1", AzureObjectId = Guid.NewGuid().ToString() },
                new PersonEntity { PersonnelNumber = "2", AzureObjectId = null }
            };

            sqlMembershipRepository
                .Setup(x => x.GetChildEntitiesAsync(filter, 0, tableName, depth))
                .ReturnsAsync(personEntities);

            var sqlMembershipObtainerService = new SqlMembershipObtainerService(
                                            sqlMembershipRepository.Object,
                                            blobStorageRepository.Object,
                                            syncJobStatusService.Object,
                                            new MockDestinationResolver(groupsRepository.Object, channelsRepository.Object),
                                            logger,
                                            telemetryClient,
                                            dryRunValue.Object,
                                            dfService.Object);

            var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => sqlMembershipObtainerService.GetChildEntitiesAsync(filter, 0, tableName, depth, syncJob, groupId, currentPart, exclusionary));

            StringAssert.Contains(exception.Message, "AzureObjectId");
            StringAssert.Contains(exception.Message, "1 HR row(s)");
            blobStorageRepository.Verify(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()), Times.Never());
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow("   ")]
        public async Task GetChildEntitiesAsync_EmptyOrWhitespaceAzureObjectId_ThrowsInvalidOperationException(string azureObjectId)
        {
            var sqlMembershipRepository = new Mock<ISqlMembershipRepository>();
            var blobStorageRepository = new Mock<IBlobStorageRepository>();
            var syncJobStatusService = new Mock<ISyncJobStatusService>();
            var groupsRepository = new Mock<IDatabaseGroupsRepository>();
            var channelsRepository = new Mock<IDatabaseChannelsRepository>();
            var logger = NullLogger<SqlMembershipObtainerService>.Instance;
            var telemetryClient = new TelemetryClient(new TelemetryConfiguration());
            var dryRunValue = new Mock<IDryRunValue>();
            var dfService = new Mock<IDataFactoryService>();

            var syncJob = new SyncJob { Id = Guid.NewGuid(), RunId = Guid.NewGuid() };
            var groupId = Guid.NewGuid();
            var currentPart = 1;
            var tableName = "TestTable";
            var filter = "filter";
            var depth = 2;
            var exclusionary = false;

            var personEntities = new List<PersonEntity>
            {
                new PersonEntity { PersonnelNumber = "1", AzureObjectId = Guid.NewGuid().ToString() },
                new PersonEntity { PersonnelNumber = "2", AzureObjectId = azureObjectId }
            };

            sqlMembershipRepository
                .Setup(x => x.GetChildEntitiesAsync(filter, 0, tableName, depth))
                .ReturnsAsync(personEntities);

            var sqlMembershipObtainerService = new SqlMembershipObtainerService(
                                            sqlMembershipRepository.Object,
                                            blobStorageRepository.Object,
                                            syncJobStatusService.Object,
                                            new MockDestinationResolver(groupsRepository.Object, channelsRepository.Object),
                                            logger,
                                            telemetryClient,
                                            dryRunValue.Object,
                                            dfService.Object);

            var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => sqlMembershipObtainerService.GetChildEntitiesAsync(filter, 0, tableName, depth, syncJob, groupId, currentPart, exclusionary));

            StringAssert.Contains(exception.Message, "AzureObjectId");
            StringAssert.Contains(exception.Message, "1 HR row(s)");
            blobStorageRepository.Verify(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()), Times.Never());
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow("   ")]
        public async Task FilterChildEntitiesAsync_EmptyOrWhitespaceAzureObjectId_ThrowsInvalidOperationException(string azureObjectId)
        {
            var sqlMembershipRepository = new Mock<ISqlMembershipRepository>();
            var blobStorageRepository = new Mock<IBlobStorageRepository>();
            var syncJobStatusService = new Mock<ISyncJobStatusService>();
            var groupsRepository = new Mock<IDatabaseGroupsRepository>();
            var channelsRepository = new Mock<IDatabaseChannelsRepository>();
            var logger = NullLogger<SqlMembershipObtainerService>.Instance;
            var telemetryClient = new TelemetryClient(new TelemetryConfiguration());
            var dryRunValue = new Mock<IDryRunValue>();
            var dfService = new Mock<IDataFactoryService>();

            var syncJob = new SyncJob { Id = Guid.NewGuid(), RunId = Guid.NewGuid() };
            var groupId = Guid.NewGuid();
            var currentPart = 1;
            var tableName = "TestTable";
            var query = "query";
            var exclusionary = false;

            var personEntities = new List<PersonEntity>
            {
                new PersonEntity { PersonnelNumber = "1", AzureObjectId = Guid.NewGuid().ToString() },
                new PersonEntity { PersonnelNumber = "2", AzureObjectId = azureObjectId }
            };

            sqlMembershipRepository
                .Setup(x => x.FilterChildEntitiesAsync(query, tableName))
                .ReturnsAsync(personEntities);

            var sqlMembershipObtainerService = new SqlMembershipObtainerService(
                                            sqlMembershipRepository.Object,
                                            blobStorageRepository.Object,
                                            syncJobStatusService.Object,
                                            new MockDestinationResolver(groupsRepository.Object, channelsRepository.Object),
                                            logger,
                                            telemetryClient,
                                            dryRunValue.Object,
                                            dfService.Object);

            var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => sqlMembershipObtainerService.FilterChildEntitiesAsync(query, tableName, syncJob, groupId, currentPart, exclusionary));

            StringAssert.Contains(exception.Message, "AzureObjectId");
            StringAssert.Contains(exception.Message, "1 HR row(s)");
            blobStorageRepository.Verify(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()), Times.Never());
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow("   ")]
        public async Task FilterChildEntitiesAsync_EmptyOrWhitespaceEmployeeId_ThrowsInvalidOperationException(string personnelNumber)
        {
            var sqlMembershipRepository = new Mock<ISqlMembershipRepository>();
            var blobStorageRepository = new Mock<IBlobStorageRepository>();
            var syncJobStatusService = new Mock<ISyncJobStatusService>();
            var groupsRepository = new Mock<IDatabaseGroupsRepository>();
            var channelsRepository = new Mock<IDatabaseChannelsRepository>();
            var logger = NullLogger<SqlMembershipObtainerService>.Instance;
            var telemetryClient = new TelemetryClient(new TelemetryConfiguration());
            var dryRunValue = new Mock<IDryRunValue>();
            var dfService = new Mock<IDataFactoryService>();

            var syncJob = new SyncJob { Id = Guid.NewGuid(), RunId = Guid.NewGuid() };
            var groupId = Guid.NewGuid();
            var currentPart = 1;
            var tableName = "TestTable";
            var query = "query";
            var exclusionary = false;

            var personEntities = new List<PersonEntity>
            {
                new PersonEntity { PersonnelNumber = "1", AzureObjectId = Guid.NewGuid().ToString() },
                new PersonEntity { PersonnelNumber = personnelNumber, AzureObjectId = Guid.NewGuid().ToString() }
            };

            sqlMembershipRepository
                .Setup(x => x.FilterChildEntitiesAsync(query, tableName))
                .ReturnsAsync(personEntities);

            var sqlMembershipObtainerService = new SqlMembershipObtainerService(
                                            sqlMembershipRepository.Object,
                                            blobStorageRepository.Object,
                                            syncJobStatusService.Object,
                                            new MockDestinationResolver(groupsRepository.Object, channelsRepository.Object),
                                            logger,
                                            telemetryClient,
                                            dryRunValue.Object,
                                            dfService.Object);

            var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => sqlMembershipObtainerService.FilterChildEntitiesAsync(query, tableName, syncJob, groupId, currentPart, exclusionary));

            StringAssert.Contains(exception.Message, "EmployeeId");
            StringAssert.Contains(exception.Message, "1 HR row(s)");
            blobStorageRepository.Verify(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()), Times.Never());
        }

        [TestMethod]
        public async Task GetChildEntitiesAsync_NullEmployeeId_DoesNotThrow()
        {
            var sqlMembershipRepository = new Mock<ISqlMembershipRepository>();
            var blobStorageRepository = new Mock<IBlobStorageRepository>();
            var syncJobStatusService = new Mock<ISyncJobStatusService>();
            var groupsRepository = new Mock<IDatabaseGroupsRepository>();
            var channelsRepository = new Mock<IDatabaseChannelsRepository>();
            var logger = NullLogger<SqlMembershipObtainerService>.Instance;
            var telemetryClient = new TelemetryClient(new TelemetryConfiguration());
            var dryRunValue = new Mock<IDryRunValue>();
            var dfService = new Mock<IDataFactoryService>();
            var groupMembership = default(GroupMembership);

            blobStorageRepository.Setup(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                                    .Callback<string, string, Dictionary<string, string>>((path, content, metadata) =>
                                    {
                                        groupMembership = JsonSerializer.Deserialize<GroupMembership>(content);
                                    });

            var syncJob = new SyncJob { Id = Guid.NewGuid(), RunId = Guid.NewGuid() };
            var groupId = Guid.NewGuid();
            var currentPart = 1;
            var tableName = "TestTable";
            var filter = "filter";
            var depth = 2;
            var exclusionary = false;

            var nullEmployeeIdObjectId = Guid.NewGuid();
            var personEntities = new List<PersonEntity>
            {
                new PersonEntity { PersonnelNumber = "1", AzureObjectId = Guid.NewGuid().ToString() },
                new PersonEntity { PersonnelNumber = null, AzureObjectId = nullEmployeeIdObjectId.ToString() }
            };

            sqlMembershipRepository
                .Setup(x => x.GetChildEntitiesAsync(filter, 0, tableName, depth))
                .ReturnsAsync(personEntities);

            var sqlMembershipObtainerService = new SqlMembershipObtainerService(
                                            sqlMembershipRepository.Object,
                                            blobStorageRepository.Object,
                                            syncJobStatusService.Object,
                                            new MockDestinationResolver(groupsRepository.Object, channelsRepository.Object),
                                            logger,
                                            telemetryClient,
                                            dryRunValue.Object,
                                            dfService.Object);

            var result = await sqlMembershipObtainerService.GetChildEntitiesAsync(filter, 0, tableName, depth, syncJob, groupId, currentPart, exclusionary);

            Assert.IsNotNull(result);
            Assert.AreEqual(SyncStatus.InProgress, result.Status);
            blobStorageRepository.Verify(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()), Times.Once());
            Assert.IsNotNull(groupMembership);
            Assert.AreEqual(2, groupMembership.SourceMembers.Count);
            Assert.IsTrue(groupMembership.SourceMembers.Any(x => x.ObjectId == nullEmployeeIdObjectId));
        }

        [TestMethod]
        public async Task FilterChildEntitiesAsync_AllZeroGuidAzureObjectId_SlipsThroughAsEmptyGuidMember()
        {
            // Documents current (intentional) behavior: an all-zeros GUID string is a syntactically
            // valid GUID, so it is NOT null/empty/whitespace and passes the guard. It parses to
            // Guid.Empty and is uploaded as a source member. The guard only targets null/empty/whitespace
            // AzureObjectId values, not semantically-empty (all-zeros) GUIDs.
            var sqlMembershipRepository = new Mock<ISqlMembershipRepository>();
            var blobStorageRepository = new Mock<IBlobStorageRepository>();
            var syncJobStatusService = new Mock<ISyncJobStatusService>();
            var groupsRepository = new Mock<IDatabaseGroupsRepository>();
            var channelsRepository = new Mock<IDatabaseChannelsRepository>();
            var logger = NullLogger<SqlMembershipObtainerService>.Instance;
            var telemetryClient = new TelemetryClient(new TelemetryConfiguration());
            var dryRunValue = new Mock<IDryRunValue>();
            var dfService = new Mock<IDataFactoryService>();
            var groupMembership = default(GroupMembership);

            blobStorageRepository.Setup(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                                    .Callback<string, string, Dictionary<string, string>>((path, content, metadata) =>
                                    {
                                        groupMembership = JsonSerializer.Deserialize<GroupMembership>(content);
                                    });

            var syncJob = new SyncJob { Id = Guid.NewGuid(), RunId = Guid.NewGuid() };
            var groupId = Guid.NewGuid();
            var currentPart = 1;
            var tableName = "TestTable";
            var query = "query";
            var exclusionary = false;

            var validObjectId = Guid.NewGuid();
            var personEntities = new List<PersonEntity>
            {
                new PersonEntity { PersonnelNumber = "1", AzureObjectId = validObjectId.ToString() },
                new PersonEntity { PersonnelNumber = "2", AzureObjectId = "00000000-0000-0000-0000-000000000000" }
            };

            sqlMembershipRepository
                .Setup(x => x.FilterChildEntitiesAsync(query, tableName))
                .ReturnsAsync(personEntities);

            var sqlMembershipObtainerService = new SqlMembershipObtainerService(
                                            sqlMembershipRepository.Object,
                                            blobStorageRepository.Object,
                                            syncJobStatusService.Object,
                                            new MockDestinationResolver(groupsRepository.Object, channelsRepository.Object),
                                            logger,
                                            telemetryClient,
                                            dryRunValue.Object,
                                            dfService.Object);

            var result = await sqlMembershipObtainerService.FilterChildEntitiesAsync(query, tableName, syncJob, groupId, currentPart, exclusionary);

            Assert.IsNotNull(result);
            Assert.AreEqual(SyncStatus.InProgress, result.Status);
            blobStorageRepository.Verify(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()), Times.Once());
            Assert.IsNotNull(groupMembership);
            Assert.AreEqual(2, groupMembership.SourceMembers.Count);
            Assert.IsTrue(groupMembership.SourceMembers.Any(x => x.ObjectId == validObjectId));
            Assert.IsTrue(groupMembership.SourceMembers.Any(x => x.ObjectId == Guid.Empty));
        }

        [TestMethod]
        public async Task GetTableNameAsync_ReturnsAdfRunIdGuid_WhenAdfReturnsValidGuid()
        {
            var sqlMembershipRepository = new Mock<ISqlMembershipRepository>();
            var blobStorageRepository = new Mock<IBlobStorageRepository>();
            var syncJobStatusService = new Mock<ISyncJobStatusService>();
            var groupsRepository = new Mock<IDatabaseGroupsRepository>();
            var channelsRepository = new Mock<IDatabaseChannelsRepository>();
            var logger = NullLogger<SqlMembershipObtainerService>.Instance;
            var telemetryClient = new TelemetryClient(new TelemetryConfiguration());
            var dryRunValue = new Mock<IDryRunValue>();
            var dfService = new Mock<IDataFactoryService>();

            var runId = Guid.NewGuid();
            var targetGroupId = Guid.NewGuid();
            var adfRunIdString = "12345678-1234-1234-1234-123456789012";
            var expectedTableName = adfRunIdString.Replace("-", "");
            var expectedAdfRunGuid = Guid.Parse(adfRunIdString);

            dfService.Setup(x => x.GetMostRecentSucceededRunIdAsync(runId))
                .ReturnsAsync(adfRunIdString);

            sqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(expectedTableName))
                .ReturnsAsync(true);

            var sqlMembershipObtainerService = new SqlMembershipObtainerService(
                                            sqlMembershipRepository.Object,
                                            blobStorageRepository.Object,
                                            syncJobStatusService.Object,
                                            new MockDestinationResolver(groupsRepository.Object, channelsRepository.Object),
                                            logger,
                                            telemetryClient,
                                            dryRunValue.Object,
                                            dfService.Object);

            var result = await sqlMembershipObtainerService.GetTableNameAsync(runId, targetGroupId);

            Assert.IsNotNull(result);
            Assert.AreEqual(expectedTableName, result.TableName);
            Assert.AreEqual(expectedAdfRunGuid, result.AdfRunId);
        }

        [TestMethod]
        public async Task GetTableNameAsync_ReturnsNullAdfRunId_WhenAdfReturnsNonGuidString()
        {
            var sqlMembershipRepository = new Mock<ISqlMembershipRepository>();
            var blobStorageRepository = new Mock<IBlobStorageRepository>();
            var syncJobStatusService = new Mock<ISyncJobStatusService>();
            var groupsRepository = new Mock<IDatabaseGroupsRepository>();
            var channelsRepository = new Mock<IDatabaseChannelsRepository>();
            var logger = NullLogger<SqlMembershipObtainerService>.Instance;
            var telemetryClient = new TelemetryClient(new TelemetryConfiguration());
            var dryRunValue = new Mock<IDryRunValue>();
            var dfService = new Mock<IDataFactoryService>();

            var runId = Guid.NewGuid();
            var targetGroupId = Guid.NewGuid();
            var adfRunIdString = "not-a-guid";
            var expectedTableName = adfRunIdString.Replace("-", "");

            dfService.Setup(x => x.GetMostRecentSucceededRunIdAsync(runId))
                .ReturnsAsync(adfRunIdString);

            sqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(expectedTableName))
                .ReturnsAsync(true);

            var sqlMembershipObtainerService = new SqlMembershipObtainerService(
                                            sqlMembershipRepository.Object,
                                            blobStorageRepository.Object,
                                            syncJobStatusService.Object,
                                            new MockDestinationResolver(groupsRepository.Object, channelsRepository.Object),
                                            logger,
                                            telemetryClient,
                                            dryRunValue.Object,
                                            dfService.Object);

            var result = await sqlMembershipObtainerService.GetTableNameAsync(runId, targetGroupId);

            Assert.IsNotNull(result);
            Assert.AreEqual(expectedTableName, result.TableName);
            Assert.IsNull(result.AdfRunId);
        }

        [TestMethod]
        public async Task GetTableNameAsync_ReturnsEmptyTableName_WhenTableDoesNotExist()
        {
            var sqlMembershipRepository = new Mock<ISqlMembershipRepository>();
            var blobStorageRepository = new Mock<IBlobStorageRepository>();
            var syncJobStatusService = new Mock<ISyncJobStatusService>();
            var groupsRepository = new Mock<IDatabaseGroupsRepository>();
            var channelsRepository = new Mock<IDatabaseChannelsRepository>();
            var logger = NullLogger<SqlMembershipObtainerService>.Instance;
            var telemetryClient = new TelemetryClient(new TelemetryConfiguration());
            var dryRunValue = new Mock<IDryRunValue>();
            var dfService = new Mock<IDataFactoryService>();

            var runId = Guid.NewGuid();
            var targetGroupId = Guid.NewGuid();
            var adfRunIdString = "12345678-1234-1234-1234-123456789012";
            var expectedTableName = adfRunIdString.Replace("-", "");
            var expectedAdfRunGuid = Guid.Parse(adfRunIdString);

            dfService.Setup(x => x.GetMostRecentSucceededRunIdAsync(runId))
                .ReturnsAsync(adfRunIdString);

            sqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(expectedTableName))
                .ReturnsAsync(false);

            var sqlMembershipObtainerService = new SqlMembershipObtainerService(
                                            sqlMembershipRepository.Object,
                                            blobStorageRepository.Object,
                                            syncJobStatusService.Object,
                                            new MockDestinationResolver(groupsRepository.Object, channelsRepository.Object),
                                            logger,
                                            telemetryClient,
                                            dryRunValue.Object,
                                            dfService.Object);

            var result = await sqlMembershipObtainerService.GetTableNameAsync(runId, targetGroupId);

            Assert.IsNotNull(result);
            Assert.AreEqual("", result.TableName);
            // AdfRunId is parsed from the ADF API response regardless of whether the table exists.
            Assert.AreEqual(expectedAdfRunGuid, result.AdfRunId);
        }

        [TestMethod]
        public async Task EvaluateMembershipAsync_ReturnsCompleteRepositoryResult()
        {
            var repository = new Mock<ISqlMembershipRepository>();
            var request = new SqlMembershipEvaluationRequest(
                managerEmployeeId: null,
                depth: 0,
                filter: "Department = 'Sales'",
                IdentityTypeSet.All,
                tableName: "run123");
            var expected = new SqlMembershipEvaluationResult(
                new[] { Guid.NewGuid() },
                new[] { Guid.NewGuid() },
                1,
                new[] { IdentityType.Users });
            repository.Setup(
                    current => current.EvaluateMembershipAsync(request))
                .ReturnsAsync(expected);

            var service = CreateService(
                repository,
                new Mock<IBlobStorageRepository>());
            var result = await service.EvaluateMembershipAsync(
                request,
                new SyncJob { RunId = Guid.NewGuid() },
                Guid.NewGuid(),
                currentPart: 1);

            Assert.AreSame(expected, result);
            repository.Verify(
                current => current.EvaluateMembershipAsync(request),
                Times.Once());
        }

        [TestMethod]
        public async Task UploadMembershipEvaluationAsync_PublishesDistinctCanonicalUnion()
        {
            var blobStorageRepository = new Mock<IBlobStorageRepository>();
            GroupMembership publishedMembership = null;
            blobStorageRepository.Setup(
                    repository => repository.UploadFileAsync(
                        It.IsAny<string>(),
                        It.IsAny<string>(),
                        It.IsAny<Dictionary<string, string>>()))
                .Callback<string, string, Dictionary<string, string>>(
                    (_, content, _) =>
                        publishedMembership =
                            JsonSerializer.Deserialize<GroupMembership>(content))
                .Returns(Task.CompletedTask);

            var duplicateAcrossTypes =
                Guid.Parse("00000000-0000-0000-0000-000000000002");
            var first =
                Guid.Parse("00000000-0000-0000-0000-000000000001");
            var last =
                Guid.Parse("00000000-0000-0000-0000-000000000003");
            var evaluation = new SqlMembershipEvaluationResult(
                new[] { last, duplicateAcrossTypes },
                new[] { duplicateAcrossTypes, first },
                selectedHumanCount: 2,
                Array.Empty<IdentityType>());
            var syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                MembershipType = "GroupMembership",
            };
            var groupId = Guid.NewGuid();
            syncJob.Group = new Group
            {
                SyncJobId = syncJob.Id,
                GroupId = groupId,
            };

            var service = CreateService(
                new Mock<ISqlMembershipRepository>(),
                blobStorageRepository);
            await service.UploadMembershipEvaluationAsync(
                evaluation,
                syncJob,
                groupId,
                currentPart: 1,
                exclusionary: true);

            Assert.IsNotNull(publishedMembership);
            CollectionAssert.AreEqual(
                new[] { first, duplicateAcrossTypes, last },
                publishedMembership.SourceMembers
                    .Select(member => member.ObjectId)
                    .ToArray());
            Assert.IsTrue(publishedMembership.Exclusionary);
        }

        [TestMethod]
        public async Task UploadMembershipEvaluationAsync_WritesTheSameFileAsTheProfileUpload()
        {
            var uploadedContents = new List<string>();
            var blobStorageRepository = new Mock<IBlobStorageRepository>();
            blobStorageRepository.Setup(
                    repository => repository.UploadFileAsync(
                        It.IsAny<string>(),
                        It.IsAny<string>(),
                        It.IsAny<Dictionary<string, string>>()))
                .Callback<string, string, Dictionary<string, string>>(
                    (_, content, _) => uploadedContents.Add(content))
                .Returns(Task.CompletedTask);
            var service = CreateService(
                new Mock<ISqlMembershipRepository>(),
                blobStorageRepository);
            var syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                MembershipType = "GroupMembership",
                Query = """[{"type":"SqlMembership","source":{"filter":"Department = 'Sales'"}}]""",
            };
            var groupId = Guid.NewGuid();
            var userObjectIds = new HashSet<Guid>(_conformanceCorpus.Select(Guid.Parse));
            var agentObjectIds = new HashSet<Guid>
            {
                Guid.Parse("11111111-2222-3333-4444-555555555555"),
                userObjectIds.First(objectId => objectId != Guid.Empty),
            };
            var selections = new[]
            {
                (Users: userObjectIds, Agents: new HashSet<Guid>()),
                (Users: userObjectIds, Agents: agentObjectIds),
                (Users: new HashSet<Guid>(), Agents: agentObjectIds),
            };

            foreach (var (users, agents) in selections)
            {
                uploadedContents.Clear();
                var evaluation = new SqlMembershipEvaluationResult(
                    users,
                    agents,
                    selectedHumanCount: users.Count,
                    Array.Empty<IdentityType>());
                var profiles = users
                    .Concat(agents)
                    .Distinct()
                    .Select(objectId => new GraphProfileInformation { Id = objectId.ToString("D") })
                    .ToList();

                await service.UploadMembershipEvaluationAsync(evaluation, syncJob, groupId, currentPart: 1, exclusionary: true);
                await service.UploadMembershipFileAsync(profiles, syncJob, groupId, currentPart: 1, exclusionary: true);

                Assert.AreEqual(2, uploadedContents.Count);
                Assert.AreEqual(uploadedContents[1], uploadedContents[0]);
            }
        }

        private static SqlMembershipObtainerService CreateService(
            Mock<ISqlMembershipRepository> repository,
            Mock<IBlobStorageRepository> blobStorageRepository)
        {
            return new SqlMembershipObtainerService(
                repository.Object,
                blobStorageRepository.Object,
                new Mock<ISyncJobStatusService>().Object,
                new MockDestinationResolver(
                    new Mock<IDatabaseGroupsRepository>().Object,
                    new Mock<IDatabaseChannelsRepository>().Object),
                NullLogger<SqlMembershipObtainerService>.Instance,
                new TelemetryClient(new TelemetryConfiguration()),
                new Mock<IDryRunValue>().Object,
                new Mock<IDataFactoryService>().Object);
        }
    }
}