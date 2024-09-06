// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.DurableTask;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using Repositories.Contracts;
using Services.Contracts;
using Services.Tests.Helpers;
using SqlMembershipObtainer;
using SqlMembershipObtainer.Entities;
using SqlMembershipObtainer.SubOrchestrator;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class OrganizationProcessorFunctionTests
    {
        private Mock<ISqlMembershipObtainerService> _sqlMembershipObtainerService = null;
        private Mock<ILoggingRepository> _loggingRepository = null;
        private List<PersonEntity> _personEntities = null;

        [TestInitialize]
        public void Setup()
        {
            _sqlMembershipObtainerService = new Mock<ISqlMembershipObtainerService>();
            _loggingRepository = new Mock<ILoggingRepository>();

            _sqlMembershipObtainerService.Setup(x => x.FilterChildEntitiesAsync(
                                                                It.IsAny<string>(),
                                                                It.IsAny<string>(),
                                                                It.IsAny<Guid?>(),
                                                                It.IsAny<Guid?>()
                                                                )).ReturnsAsync(() => _personEntities);

            _sqlMembershipObtainerService.Setup(x => x.GetChildEntitiesAsync(
                                                    It.IsAny<string>(),
                                                    It.IsAny<int>(),
                                                    It.IsAny<string>(),
                                                    It.IsAny<int>(),
                                                    It.IsAny<Guid?>(),
                                                    It.IsAny<Guid?>()
                                                    )).ReturnsAsync(() => _personEntities);

        }

        [TestMethod]
        public async Task ProcessQueryWithOrgLeadersTest()
        {
            var context = new Mock<TaskOrchestrationContext>();
            var queryFunction = new OrganizationProcessorFunction();
            var organization = new OrganizationCreator().GenerateOrganizationHierarchy();

            CustomizeOrganization(organization);

            var rootentity = organization.Where(x => x.LevelId == 1).First().Entities.First();
            var engineerCount = organization.SelectMany(x => x.Entities).Count(e => e.StandardTitle == "Engineer");
            _personEntities = organization.SelectMany(x => x.Entities).Where(e => e.StandardTitle == "Engineer").ToList();

            var organizationProcessorRequest = new OrganizationProcessorRequest
            {
                Query = new Query()
                {
                    Manager = new Manager {
                        Depth = 0,
                        Id = 1000
                    },
                    Filter = "StandardTitle eq 'Engineer'"
                },
                SyncJob = new SyncJob
                {
                    Id = Guid.NewGuid(),
                    RunId = Guid.NewGuid()
                }
            };

            GraphProfileInformationResponse managerOrgProcessorResponse = null;
            GraphProfileInformationResponse managerOrgReaderResponse = null;

            context.Setup(x => x.GetInput<OrganizationProcessorRequest>()).Returns(organizationProcessorRequest);

            context.Setup(x => x.CallActivityAsync(It.IsAny<TaskName>(), It.IsAny<LoggerRequest>(), It.IsAny<TaskOptions>()));

            context.Setup(x => x.CallActivityAsync<string>(nameof(TableNameReaderFunction), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>())).ReturnsAsync("tbl112233445566");

            context.Setup(x => x.CallSubOrchestratorAsync<GraphProfileInformationResponse>(
                                                                            nameof(ManagerOrgReaderFunction),
                                                                            It.IsAny<ManagerOrgReaderRequest>(),
                                                                            It.IsAny<TaskOptions>()))
                    .ReturnsAsync(() => managerOrgProcessorResponse);

            context.Setup(x => x.CallActivityAsync<GraphProfileInformationResponse>(
                                                                nameof(ManagerOrgReaderFunction),
                                                                It.IsAny<ManagerOrgReaderRequest>(), 
                                                                It.IsAny<TaskOptions>()))
                    .Callback<TaskName, object, TaskOptions>(async (name, requestObject, options) =>
                    {
                        var request = requestObject as ManagerOrgReaderRequest;
                        managerOrgReaderResponse = await ManagerOrgReaderFunctionAsync(request);
                    })
                    .ReturnsAsync(() => managerOrgReaderResponse);

            var profiles = await queryFunction.ProcessQueryAsync(context.Object);

            Assert.AreEqual(engineerCount, profiles.GraphProfileCount);
        }

        [TestMethod]
        public async Task ProcessQueryWithNoOrgLeadersTest()
        {
            var context = new Mock<TaskOrchestrationContext>();
            var queryFunction = new OrganizationProcessorFunction();
            var organization = new OrganizationCreator().GenerateOrganizationHierarchy();

            CustomizeOrganization(organization);

            var rootentity = organization.Where(x => x.LevelId == 1).First().Entities.First();
            var engineerCount = organization.SelectMany(x => x.Entities).Count(e => e.StandardTitle == "Engineer");
            _personEntities = organization.SelectMany(x => x.Entities).Where(e => e.StandardTitle == "Engineer").ToList();

            var organizationProcessorRequest = new OrganizationProcessorRequest
            {
                Query = new Query
                {
                    Filter = "StandardTitle eq 'Engineer'"
                },
                SyncJob = new SyncJob
                {
                    Id = Guid.NewGuid(),
                    RunId = Guid.NewGuid()
                }
            };

            ManagerOrgReaderRequest managerOrgReaderRequest = null;
            GraphProfileInformationResponse childEntitiesFilterResponse = null;

            context.Setup(x => x.GetInput<OrganizationProcessorRequest>()).Returns(organizationProcessorRequest);
            context.Setup(x => x.GetInput<ManagerOrgReaderRequest>()).Returns(() => managerOrgReaderRequest);

            context.Setup(x => x.CallActivityAsync(It.IsAny<TaskName>(), It.IsAny<LoggerRequest>(), It.IsAny<TaskOptions>()));

            context.Setup(x => x.CallActivityAsync<string>(nameof(TableNameReaderFunction), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>())).ReturnsAsync("tbl112233445566");

            context.Setup(x => x.CallActivityAsync<GraphProfileInformationResponse>(It.IsAny<TaskName>(), It.IsAny<ChildEntitiesFilterRequest>(), It.IsAny<TaskOptions>()))
                .Callback<TaskName, object, TaskOptions>(async (name, requestObject, options) =>
                {
                    var request = requestObject as ChildEntitiesFilterRequest;
                    childEntitiesFilterResponse = await CallChildEntitiesFilterFunctionAsync(request);

                })
                .ReturnsAsync(() => childEntitiesFilterResponse);

            var profiles = await queryFunction.ProcessQueryAsync(context.Object);

            Assert.AreEqual(engineerCount, profiles.GraphProfileCount);
        }

        private async Task<GraphProfileInformationResponse> CallChildEntitiesFilterFunctionAsync(ChildEntitiesFilterRequest request)
        {
            var function = new ChildEntitiesFilterFunction(_sqlMembershipObtainerService.Object, _loggingRepository.Object);
            return await function.FilterChildEntities(request);
        }

        private async Task<GraphProfileInformationResponse> ManagerOrgReaderFunctionAsync(ManagerOrgReaderRequest request)
        {
            var function = new ManagerOrgReaderFunction(_sqlMembershipObtainerService.Object, _loggingRepository.Object);
            return await function.ReadUsersAsync(request);
        }

        private void CustomizeOrganization(List<OrganizationLevel> organization)
        {
            foreach (var level in organization)
            {
                var index = 0;
                foreach (var entity in level.Entities)
                {
                    var isEven = index++ % 2 == 0;
                    entity.CompanyCode = isEven ? "2" : "1";
                    entity.StandardTitle = isEven ? "PM" : "Engineer";
                    entity.PersonnelNumber = Guid.NewGuid().ToString();
                }
            }
        }
    }
}
