// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using DIConcreteTypes;
using Entities;
using Models.ServiceBus;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Models;
using Newtonsoft.Json;
using SqlMembershipObtainer.Common.DependencyInjection;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Services.Contracts;

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
            var blobStorageRepository = new Mock<IBlobStorageRepository>();
            var syncJobRepository = new Mock<IDatabaseSyncJobsRepository>();
            var loggingRepository = new Mock<ILoggingRepository>();
            var telemetryClient = new TelemetryClient(new TelemetryConfiguration());
            var sqlMembershipObtainerServiceSecret = new Mock<ISqlMembershipObtainerServiceSecret>();
            var dryRunValue = new Mock<IDryRunValue>();
            var groupMembership = default(GroupMembership);
            var messages = new List<string>();
            var dfRepository = new Mock<IDataFactoryRepository>();
            var dfService = new Mock<IDataFactoryService>();

            blobStorageRepository.Setup(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                                    .Callback<string, string, Dictionary<string, string>>((path, content, metadata) =>
                                    {
                                        groupMembership = JsonConvert.DeserializeObject<GroupMembership>(content);
                                    });

            var currentPart = 1;
            var syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid()
            };

            var organization = new OrganizationCreator().GenerateOrganizationHierarchy();
            var profiles = organization.SelectMany(x => x.Entities)
                                        .Select(x => new GraphProfileInformation { Id = x.AzureObjectId, PersonnelNumber = x.PersonnelNumber })
                                        .ToList();

            var sqlMembershipObtainerService = new SqlMembershipObtainerService(
                                            blobStorageRepository.Object,
                                            syncJobRepository.Object,
                                            loggingRepository.Object,
                                            telemetryClient,
                                            sqlMembershipObtainerServiceSecret.Object,
                                            sqlMembershipObtainerServiceSecret.Object,
                                            dryRunValue.Object,
                                            dfService.Object);

            await sqlMembershipObtainerService.SendGroupMembershipAsync(profiles, syncJob, currentPart, false);

            blobStorageRepository.Verify(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()), Times.Once());
            Assert.AreEqual(profiles.Count, groupMembership.SourceMembers.Count);

            loggingRepository.Verify(x => x.LogMessageAsync(
                                            It.Is<LogMessage>(m => m.Message.StartsWith("SqlMembershipObtainer service completed")),
                                            It.IsAny<VerbosityLevel>(),
                                            It.IsAny<string>(),
                                            It.IsAny<string>()), Times.Once());
        }
    }
}
