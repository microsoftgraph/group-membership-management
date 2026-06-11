// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Logging.Abstractions;
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
                                                                It.IsAny<bool>()
                                                                )).ReturnsAsync(() => _groupMembershipSenderResponse);

            _sqlMembershipObtainerService.Setup(x => x.GetChildEntitiesAsync(
                                                    It.IsAny<string>(),
                                                    It.IsAny<int>(),
                                                    It.IsAny<string>(),
                                                    It.IsAny<int>(),
                                                    It.IsAny<SyncJob>(),
                                                    It.IsAny<Guid>(),
                                                    It.IsAny<int>(),
                                                    It.IsAny<bool>()
                                                    )).ReturnsAsync(() => _groupMembershipSenderResponse);

        }

        [TestMethod]
        public async Task ProcessQueryWithOrgLeadersTest()
        {
            var orgProcessorContext = new Mock<TaskOrchestrationContext>();
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
                TotalParts = 1,
                Exclusionary = false
            };

            orgProcessorContext.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(NullLogger.Instance);
            orgProcessorContext.Setup(x => x.GetInput<OrganizationProcessorRequest>()).Returns(request);
            orgProcessorContext.Setup(x => x.CallActivityAsync<string>(nameof(TableNameReaderFunction), It.IsAny<TableNameReaderRequest>(), It.IsAny<TaskOptions>()))
                .ReturnsAsync("sometable");
            orgProcessorContext.Setup(x => x.CallActivityAsync<MembershipFileResult>(
                nameof(ManagerOrgReaderFunction), It.IsAny<ManagerOrgReaderRequest>(), It.IsAny<TaskOptions>()))
                .ReturnsAsync(new MembershipFileResult());

            var function = new OrganizationProcessorFunction();
            await function.ProcessQueryAsync(orgProcessorContext.Object);

            orgProcessorContext.Verify(x => x.CallActivityAsync<string>(
                nameof(TableNameReaderFunction),
                It.Is<TableNameReaderRequest>(r => r.CurrentPart == 1 && r.TotalParts == 1),
                It.IsAny<TaskOptions>()), Times.Once());
            orgProcessorContext.Verify(x => x.CallActivityAsync<MembershipFileResult>(
                nameof(ManagerOrgReaderFunction),
                It.Is<ManagerOrgReaderRequest>(r => r.CurrentPart == 1 && r.TotalParts == 1),
                It.IsAny<TaskOptions>()), Times.Once());
            orgProcessorContext.Verify(x => x.CallActivityAsync<MembershipFileResult>(
                nameof(ChildEntitiesFilterFunction), It.IsAny<ChildEntitiesFilterRequest>(), It.IsAny<TaskOptions>()), Times.Never());
        }


        [TestMethod]
        public async Task ProcessQueryWithNoOrgLeadersTest()
        {
            var orgProcessorContext = new Mock<TaskOrchestrationContext>();
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
                TotalParts = 1,
                Exclusionary = false
            };

            orgProcessorContext.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(NullLogger.Instance);
            orgProcessorContext.Setup(x => x.GetInput<OrganizationProcessorRequest>()).Returns(request);
            orgProcessorContext.Setup(x => x.CallActivityAsync<string>(nameof(TableNameReaderFunction), It.IsAny<TableNameReaderRequest>(), It.IsAny<TaskOptions>()))
                .ReturnsAsync("sometable");
            orgProcessorContext.Setup(x => x.CallActivityAsync<MembershipFileResult>(
                nameof(ChildEntitiesFilterFunction), It.IsAny<ChildEntitiesFilterRequest>(), It.IsAny<TaskOptions>()))
                .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                {
                    await CallChildEntitiesFilterFunctionAsync(request as ChildEntitiesFilterRequest);
                })
                .ReturnsAsync(new MembershipFileResult());

            var function = new OrganizationProcessorFunction();
            await function.ProcessQueryAsync(orgProcessorContext.Object);

            orgProcessorContext.Verify(x => x.CallActivityAsync<string>(
                nameof(TableNameReaderFunction),
                It.Is<TableNameReaderRequest>(r => r.CurrentPart == 1 && r.TotalParts == 1),
                It.IsAny<TaskOptions>()), Times.Once());
            orgProcessorContext.Verify(x => x.CallActivityAsync<MembershipFileResult>(
                nameof(ManagerOrgReaderFunction), It.IsAny<ManagerOrgReaderRequest>(), It.IsAny<TaskOptions>()), Times.Never());
            orgProcessorContext.Verify(x => x.CallActivityAsync<MembershipFileResult>(
                nameof(ChildEntitiesFilterFunction),
                It.Is<ChildEntitiesFilterRequest>(r => r.CurrentPart == 1 && r.TotalParts == 1),
                It.IsAny<TaskOptions>()), Times.Once());
        }

        private async Task CallChildEntitiesFilterFunctionAsync(ChildEntitiesFilterRequest request)
        {
            var function = new ChildEntitiesFilterFunction(NullLogger<ChildEntitiesFilterFunction>.Instance, _sqlMembershipObtainerService.Object);
            await function.FilterChildEntities(request);
        }
    }
}