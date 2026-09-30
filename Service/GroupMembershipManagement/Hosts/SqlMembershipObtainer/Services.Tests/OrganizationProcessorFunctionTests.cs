// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Models;
using SqlMembershipObtainer;
using SqlMembershipObtainer.Entities;
using System;
using System.Collections.Generic;
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
            _groupMembershipSenderResponse = new MembershipFileResult
            {
                Status = SyncStatus.InProgress,
                FilePath = "membership.json",
            };

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

            _sqlMembershipObtainerService.Setup(
                    x => x.EvaluateMembershipAsync(
                        It.IsAny<SqlMembershipEvaluationRequest>(),
                        It.IsAny<SyncJob>(),
                        It.IsAny<Guid>(),
                        It.IsAny<int>()))
                .ReturnsAsync(
                    new SqlMembershipEvaluationResult(
                        Array.Empty<Guid>(),
                        Array.Empty<Guid>(),
                        0,
                        Array.Empty<IdentityType>()));

            _sqlMembershipObtainerService.Setup(
                    x => x.UploadMembershipEvaluationAsync(
                        It.IsAny<SqlMembershipEvaluationResult>(),
                        It.IsAny<SyncJob>(),
                        It.IsAny<Guid>(),
                        It.IsAny<int>(),
                        It.IsAny<bool>()))
                .ReturnsAsync(() => _groupMembershipSenderResponse);

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
                Exclusionary = false,
                IdentityTypes = new[] { "agents" },
            };

            orgProcessorContext.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(NullLogger.Instance);
            orgProcessorContext.Setup(x => x.GetInput<OrganizationProcessorRequest>()).Returns(request);
            orgProcessorContext.Setup(x => x.CallActivityAsync<TableNameResult>(nameof(TableNameReaderFunction), It.IsAny<TableNameReaderRequest>(), It.IsAny<TaskOptions>()))
                .ReturnsAsync(new TableNameResult { TableName = "sometable", AdfRunId = Guid.NewGuid() });
            orgProcessorContext.Setup(x => x.CallActivityAsync<MembershipFileResult>(
                nameof(ManagerOrgReaderFunction), It.IsAny<ManagerOrgReaderRequest>(), It.IsAny<TaskOptions>()))
                .ReturnsAsync(new MembershipFileResult());

            var function = new OrganizationProcessorFunction();
            await function.ProcessQueryAsync(orgProcessorContext.Object);

            orgProcessorContext.Verify(x => x.CallActivityAsync<TableNameResult>(
                nameof(TableNameReaderFunction),
                It.Is<TableNameReaderRequest>(r => r.CurrentPart == 1 && r.TotalParts == 1),
                It.IsAny<TaskOptions>()), Times.Once());
            orgProcessorContext.Verify(x => x.CallActivityAsync<MembershipFileResult>(
                nameof(ManagerOrgReaderFunction),
                It.Is<ManagerOrgReaderRequest>(
                    r => r.CurrentPart == 1 &&
                         r.TotalParts == 1 &&
                         r.TableName == "sometable" &&
                         r.IdentityTypes.Count == 1 &&
                         r.IdentityTypes[0] == "agents"),
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
                Exclusionary = false,
                IdentityTypes = new[] { "users", "agents" },
            };

            orgProcessorContext.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(NullLogger.Instance);
            orgProcessorContext.Setup(x => x.GetInput<OrganizationProcessorRequest>()).Returns(request);
            orgProcessorContext.Setup(x => x.CallActivityAsync<TableNameResult>(nameof(TableNameReaderFunction), It.IsAny<TableNameReaderRequest>(), It.IsAny<TaskOptions>()))
                .ReturnsAsync(new TableNameResult { TableName = "sometable", AdfRunId = Guid.NewGuid() });
            orgProcessorContext.Setup(x => x.CallActivityAsync<MembershipFileResult>(
                nameof(ChildEntitiesFilterFunction), It.IsAny<ChildEntitiesFilterRequest>(), It.IsAny<TaskOptions>()))
                .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                {
                    await CallChildEntitiesFilterFunctionAsync(request as ChildEntitiesFilterRequest);
                })
                .ReturnsAsync(new MembershipFileResult());

            var function = new OrganizationProcessorFunction();
            await function.ProcessQueryAsync(orgProcessorContext.Object);

            orgProcessorContext.Verify(x => x.CallActivityAsync<TableNameResult>(
                nameof(TableNameReaderFunction),
                It.Is<TableNameReaderRequest>(r => r.CurrentPart == 1 && r.TotalParts == 1),
                It.IsAny<TaskOptions>()), Times.Once());
            orgProcessorContext.Verify(x => x.CallActivityAsync<MembershipFileResult>(
                nameof(ManagerOrgReaderFunction), It.IsAny<ManagerOrgReaderRequest>(), It.IsAny<TaskOptions>()), Times.Never());
            orgProcessorContext.Verify(x => x.CallActivityAsync<MembershipFileResult>(
                nameof(ChildEntitiesFilterFunction),
                It.Is<ChildEntitiesFilterRequest>(
                    r => r.CurrentPart == 1 &&
                         r.TotalParts == 1 &&
                         r.TableName == "sometable" &&
                         r.IdentityTypes.Count == 2 &&
                         r.IdentityTypes[0] == "users" &&
                         r.IdentityTypes[1] == "agents"),
                It.IsAny<TaskOptions>()), Times.Once());
        }

        [TestMethod]
        public async Task ManagerActivity_EvaluatesCompletelyBeforeUpload()
        {
            var callOrder = new List<string>();
            SqlMembershipEvaluationRequest capturedRequest = null;
            var evaluation = new SqlMembershipEvaluationResult(
                new[] { Guid.NewGuid() },
                new[] { Guid.NewGuid() },
                1,
                new[] { IdentityType.Users, IdentityType.Agents });

            _sqlMembershipObtainerService.Setup(
                    service => service.EvaluateMembershipAsync(
                        It.IsAny<SqlMembershipEvaluationRequest>(),
                        It.IsAny<SyncJob>(),
                        It.IsAny<Guid>(),
                        It.IsAny<int>()))
                .Callback<
                    SqlMembershipEvaluationRequest,
                    SyncJob,
                    Guid,
                    int>((request, _, _, _) =>
                    {
                        callOrder.Add("evaluate");
                        capturedRequest = request;
                    })
                .ReturnsAsync(evaluation);
            _sqlMembershipObtainerService.Setup(
                    service => service.UploadMembershipEvaluationAsync(
                        evaluation,
                        It.IsAny<SyncJob>(),
                        It.IsAny<Guid>(),
                        It.IsAny<int>(),
                        It.IsAny<bool>()))
                .Callback(() => callOrder.Add("upload"))
                .ReturnsAsync(_groupMembershipSenderResponse);

            var request = new ManagerOrgReaderRequest
            {
                PersonnelNumber = 123,
                Depth = 2,
                Filter = "users.Department = 'Sales'",
                IdentityTypes = new[] { "agents", "users" },
                TableName = "9ff66298c27f4f4295e8633e90648241",
                SyncJob = new SyncJob { RunId = Guid.NewGuid() },
                GroupId = Guid.NewGuid(),
                CurrentPart = 1,
                TotalParts = 1,
                Exclusionary = true,
            };

            var function = new ManagerOrgReaderFunction(
                NullLogger<ManagerOrgReaderFunction>.Instance,
                _sqlMembershipObtainerService.Object);
            var response = await function.ReadUsersAsync(request);

            Assert.AreSame(_groupMembershipSenderResponse, response);
            Assert.IsNotNull(capturedRequest);
            Assert.AreEqual(123, capturedRequest.ManagerEmployeeId);
            Assert.AreEqual(2, capturedRequest.Depth);
            Assert.AreEqual("9ff66298c27f4f4295e8633e90648241", capturedRequest.TableName);
            Assert.AreEqual(
                Guid.Parse("9ff66298-c27f-4f42-95e8-633e90648241"),
                capturedRequest.AdfRunId);
            Assert.AreEqual(IdentityTypeSet.All, capturedRequest.IdentityTypes);
            CollectionAssert.AreEqual(
                new[] { "evaluate", "upload" },
                callOrder);
        }

        [TestMethod]
        public async Task FilterActivity_LegacyReplayDefaultsToUsers()
        {
            SqlMembershipEvaluationRequest capturedRequest = null;
            var evaluation = new SqlMembershipEvaluationResult(
                Array.Empty<Guid>(),
                Array.Empty<Guid>(),
                0,
                Array.Empty<IdentityType>());

            _sqlMembershipObtainerService.Setup(
                    service => service.EvaluateMembershipAsync(
                        It.IsAny<SqlMembershipEvaluationRequest>(),
                        It.IsAny<SyncJob>(),
                        It.IsAny<Guid>(),
                        It.IsAny<int>()))
                .Callback<
                    SqlMembershipEvaluationRequest,
                    SyncJob,
                    Guid,
                    int>((request, _, _, _) => capturedRequest = request)
                .ReturnsAsync(evaluation);
            _sqlMembershipObtainerService.Setup(
                    service => service.UploadMembershipEvaluationAsync(
                        evaluation,
                        It.IsAny<SyncJob>(),
                        It.IsAny<Guid>(),
                        It.IsAny<int>(),
                        It.IsAny<bool>()))
                .ReturnsAsync(_groupMembershipSenderResponse);

            var function = new ChildEntitiesFilterFunction(
                NullLogger<ChildEntitiesFilterFunction>.Instance,
                _sqlMembershipObtainerService.Object);
            await function.FilterChildEntities(
                new ChildEntitiesFilterRequest
                {
                    Query = "Department = 'Sales'",
                    IdentityTypes = null,
                    TableName = "legacyrun",
                    SyncJob = new SyncJob { RunId = Guid.NewGuid() },
                    GroupId = Guid.NewGuid(),
                    CurrentPart = 1,
                    TotalParts = 1,
                    Exclusionary = false,
                });

            Assert.IsNotNull(capturedRequest);
            Assert.AreEqual(IdentityTypeSet.UsersOnly, capturedRequest.IdentityTypes);
            Assert.AreEqual("legacyrun", capturedRequest.TableName);
        }

        [TestMethod]
        public async Task FilterActivity_DoesNotUploadAfterEvaluationFailure()
        {
            _sqlMembershipObtainerService.Setup(
                    service => service.EvaluateMembershipAsync(
                        It.IsAny<SqlMembershipEvaluationRequest>(),
                        It.IsAny<SyncJob>(),
                        It.IsAny<Guid>(),
                        It.IsAny<int>()))
                .ThrowsAsync(new InvalidOperationException("evaluation failed"));

            var function = new ChildEntitiesFilterFunction(
                NullLogger<ChildEntitiesFilterFunction>.Instance,
                _sqlMembershipObtainerService.Object);

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => function.FilterChildEntities(
                    new ChildEntitiesFilterRequest
                    {
                        Query = "Department = 'Sales'",
                        IdentityTypes = new[] { "users" },
                        TableName = "run123",
                        SyncJob = new SyncJob { RunId = Guid.NewGuid() },
                        GroupId = Guid.NewGuid(),
                        CurrentPart = 1,
                        TotalParts = 1,
                        Exclusionary = false,
                    }));

            _sqlMembershipObtainerService.Verify(
                service => service.UploadMembershipEvaluationAsync(
                    It.IsAny<SqlMembershipEvaluationResult>(),
                    It.IsAny<SyncJob>(),
                    It.IsAny<Guid>(),
                    It.IsAny<int>(),
                    It.IsAny<bool>()),
                Times.Never());
        }

        private async Task CallChildEntitiesFilterFunctionAsync(ChildEntitiesFilterRequest request)
        {
            var function = new ChildEntitiesFilterFunction(NullLogger<ChildEntitiesFilterFunction>.Instance, _sqlMembershipObtainerService.Object);
            await function.FilterChildEntities(request);
        }
    }
}