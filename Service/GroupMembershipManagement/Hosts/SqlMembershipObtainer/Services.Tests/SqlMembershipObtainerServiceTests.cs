// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
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
            var loggingRepository = new Mock<ILoggingRepository>();
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
                                            loggingRepository.Object,
                                            telemetryClient,
                                            dryRunValue.Object,
                                            dfService.Object);

            await sqlMembershipObtainerService.UploadMembershipFileAsync(profiles, syncJob, syncJob.Group.GroupId, currentPart, false);

            blobStorageRepository.Verify(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()), Times.Once());
            Assert.AreEqual(profiles.Count, groupMembership.SourceMembers.Count);

            loggingRepository.Verify(x => x.LogMessageAsync(
                                            It.Is<LogMessage>(m => m.Message.StartsWith("SqlMembershipObtainer service completed")),
                                            It.IsAny<VerbosityLevel>(),
                                            It.IsAny<string>(),
                                            It.IsAny<string>()), Times.Once());
        }

        [TestMethod]
        public async Task GetChildEntitiesAsync_ReturnsExpectedResponse_AndProfilesCount()
        {
            var sqlMembershipRepository = new Mock<ISqlMembershipRepository>();
            var blobStorageRepository = new Mock<IBlobStorageRepository>();
            var syncJobStatusService = new Mock<ISyncJobStatusService>();
            var groupsRepository = new Mock<IDatabaseGroupsRepository>();
            var channelsRepository = new Mock<IDatabaseChannelsRepository>();
            var loggingRepository = new Mock<ILoggingRepository>();
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

            var capturedProfiles = new List<GraphProfileInformation>();
            var expectedResponse = new MembershipFileResult { Status = SyncStatus.InProgress, FilePath = "file-path" };

            var sqlMembershipObtainerService = new SqlMembershipObtainerService(
                                            sqlMembershipRepository.Object,
                                            blobStorageRepository.Object,
                                            syncJobStatusService.Object,
                                            groupsRepository.Object,
                                            channelsRepository.Object,
                                            loggingRepository.Object,
                                            telemetryClient,
                                            dryRunValue.Object,
                                            dfService.Object);

            var result = await sqlMembershipObtainerService.GetChildEntitiesAsync(filter, 0, tableName, depth, syncJob, groupId, currentPart, exclusionary);

            loggingRepository.Verify(x => x.LogMessageAsync(
                It.Is<LogMessage>(m => m.Message.Contains($"Retrieved a total of {personEntities.Count} records from {tableName} table")),
                It.IsAny<VerbosityLevel>(),
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Once());
        }

        [TestMethod]
        public async Task FilterChildEntitiesAsync_ReturnsExpectedResponse_AndProfilesCount()
        {
            var sqlMembershipRepository = new Mock<ISqlMembershipRepository>();
            var blobStorageRepository = new Mock<IBlobStorageRepository>();
            var syncJobStatusService = new Mock<ISyncJobStatusService>();
            var groupsRepository = new Mock<IDatabaseGroupsRepository>();
            var channelsRepository = new Mock<IDatabaseChannelsRepository>();
            var loggingRepository = new Mock<ILoggingRepository>();
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

            var capturedProfiles = new List<GraphProfileInformation>();
            var expectedResponse = new MembershipFileResult { Status = SyncStatus.InProgress, FilePath = "file-path" };

            var sqlMembershipObtainerService = new SqlMembershipObtainerService(
                                            sqlMembershipRepository.Object,
                                            blobStorageRepository.Object,
                                            syncJobStatusService.Object,
                                            groupsRepository.Object,
                                            channelsRepository.Object,
                                            loggingRepository.Object,
                                            telemetryClient,
                                            dryRunValue.Object,
                                            dfService.Object);

            var result = await sqlMembershipObtainerService.FilterChildEntitiesAsync(query, tableName, syncJob, groupId, currentPart, exclusionary);

            loggingRepository.Verify(x => x.LogMessageAsync(
                It.Is<LogMessage>(m => m.Message.Contains($"Retrieved a total of {personEntities.Count} records from {tableName} table")),
                It.IsAny<VerbosityLevel>(),
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Once());
        }

    }
}
