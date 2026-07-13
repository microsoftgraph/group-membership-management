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
                                            groupsRepository.Object,
                                            channelsRepository.Object,
                                            logger,
                                            telemetryClient,
                                            dryRunValue.Object,
                                            dfService.Object);

            await sqlMembershipObtainerService.UploadMembershipFileAsync(profiles, syncJob, syncJob.Group.GroupId, currentPart, false);

            blobStorageRepository.Verify(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()), Times.Once());
            Assert.AreEqual(profiles.Count, groupMembership.SourceMembers.Count);
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
                                            groupsRepository.Object,
                                            channelsRepository.Object,
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
                                            groupsRepository.Object,
                                            channelsRepository.Object,
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
                                            groupsRepository.Object,
                                            channelsRepository.Object,
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
                                            groupsRepository.Object,
                                            channelsRepository.Object,
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
                                            groupsRepository.Object,
                                            channelsRepository.Object,
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
                                            groupsRepository.Object,
                                            channelsRepository.Object,
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
                                            groupsRepository.Object,
                                            channelsRepository.Object,
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
                                            groupsRepository.Object,
                                            channelsRepository.Object,
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
                                            groupsRepository.Object,
                                            channelsRepository.Object,
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
                                            groupsRepository.Object,
                                            channelsRepository.Object,
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
                                            groupsRepository.Object,
                                            channelsRepository.Object,
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
                                            groupsRepository.Object,
                                            channelsRepository.Object,
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
                                            groupsRepository.Object,
                                            channelsRepository.Object,
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
    }
}