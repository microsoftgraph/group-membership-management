// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Models;
using SqlMembershipObtainer;
using SqlMembershipObtainer.Entities;
using System;
using System.Threading.Tasks;
using Services.Contracts;

namespace Services.Tests
{
    [TestClass]
    public class OrganizationProcessorFunctionTests
    {
        private Mock<ISqlMembershipObtainerService> _sqlMembershipObtainerService = null;
        private MembershipFileResult _groupMembershipSenderResponse = null;

        [TestInitialize]
        public void Setup()
        {
            _sqlMembershipObtainerService = new Mock<ISqlMembershipObtainerService>();

            _sqlMembershipObtainerService.Setup(x => x.FilterChildEntitiesAsync(
                                                                It.IsAny<string>(),
                                                                It.IsAny<string>(),
                                                                It.IsAny<SyncJob>(),
                                                                It.IsAny<Guid>(),
                                                                It.IsAny<int>(),
                                                                It.IsAny<bool>(),
                                                                It.IsAny<string>()
                                                                )).ReturnsAsync(() => _groupMembershipSenderResponse);

            _sqlMembershipObtainerService.Setup(x => x.GetChildEntitiesAsync(
                                                    It.IsAny<string>(),
                                                    It.IsAny<int>(),
                                                    It.IsAny<string>(),
                                                    It.IsAny<int>(),
                                                    It.IsAny<SyncJob>(),
                                                    It.IsAny<Guid>(),
                                                    It.IsAny<int>(),
                                                    It.IsAny<bool>(),
                                                    It.IsAny<string>()
                                                    )).ReturnsAsync(() => _groupMembershipSenderResponse);

        }

        [TestMethod]
        public async Task ProcessQueryWithOrgLeadersTest()
        {
            var orgProcessorContext = new Mock<IDurableOrchestrationContext>();
            var request = new OrganizationProcessorRequest
            {
                Query = new Query { Filter = "Department = 'IT'", Manager = new Manager { Id = 123, Depth = 1 } },
                SyncJob = new SyncJob
                {
                    Id = Guid.NewGuid(),
                    RunId = Guid.NewGuid(),
                    MembershipType = "GroupMembership"
                },
                GroupId = Guid.NewGuid(),
                CurrentPart = 1,
                Exclusionary = false,
                AdaptiveCardTemplateDirectory = ""
            };

            orgProcessorContext.Setup(x => x.GetInput<OrganizationProcessorRequest>()).Returns(request);
            orgProcessorContext.Setup(x => x.CallActivityAsync<string>(nameof(TableNameReaderFunction), It.IsAny<TableNameReaderRequest>()))
                .ReturnsAsync("sometable");
            orgProcessorContext.Setup(x => x.CallActivityAsync<MembershipFileResult>(
                nameof(ManagerOrgReaderFunction), It.IsAny<ManagerOrgReaderRequest>()))
                .ReturnsAsync(new MembershipFileResult());

            var function = new OrganizationProcessorFunction();
            await function.ProcessQueryAsync(orgProcessorContext.Object);
            orgProcessorContext.Verify(x => x.CallActivityAsync<MembershipFileResult>(
                nameof(ManagerOrgReaderFunction), It.IsAny<ManagerOrgReaderRequest>()), Times.Once());
            orgProcessorContext.Verify(x => x.CallActivityAsync<MembershipFileResult>(
                nameof(ChildEntitiesFilterFunction), It.IsAny<ChildEntitiesFilterRequest>()), Times.Never());
        }


        [TestMethod]
        public async Task ProcessQueryWithNoOrgLeadersTest()
        {
            var orgProcessorContext = new Mock<IDurableOrchestrationContext>();
            var request = new OrganizationProcessorRequest
            {
                Query = new Query { Filter = "Department = 'IT'" },
                SyncJob = new SyncJob
                {
                    Id = Guid.NewGuid(),
                    RunId = Guid.NewGuid(),
                    MembershipType = "GroupMembership"
                },
                GroupId = Guid.NewGuid(),
                CurrentPart = 1,
                Exclusionary = false,
                AdaptiveCardTemplateDirectory = ""
            };

            orgProcessorContext.Setup(x => x.GetInput<OrganizationProcessorRequest>()).Returns(request);
            orgProcessorContext.Setup(x => x.CallActivityAsync<string>(nameof(TableNameReaderFunction), It.IsAny<TableNameReaderRequest>()))
                .ReturnsAsync("sometable");
            orgProcessorContext.Setup(x => x.CallActivityAsync<MembershipFileResult>(
                nameof(ManagerOrgReaderFunction), It.IsAny<ManagerOrgReaderRequest>()))
                .ReturnsAsync(new MembershipFileResult());

            var function = new OrganizationProcessorFunction();
            await function.ProcessQueryAsync(orgProcessorContext.Object);
            orgProcessorContext.Verify(x => x.CallActivityAsync<MembershipFileResult>(
                nameof(ManagerOrgReaderFunction), It.IsAny<ManagerOrgReaderRequest>()), Times.Never());
            orgProcessorContext.Verify(x => x.CallActivityAsync<MembershipFileResult>(
                nameof(ChildEntitiesFilterFunction), It.IsAny<ChildEntitiesFilterRequest>()), Times.Once());
        }
    }
}
