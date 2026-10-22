// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.GroupMembershipObtainer;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.ServiceBus;
using Moq;
using System;
using System.Threading.Tasks;

namespace Tests.Services
{
    [TestClass]
    public sealed class OrchestratorIdentitySelectionTests
    {
        [TestMethod]
        public async Task RunOrchestratorAsync_ExplicitSelection_CarriesCanonicalNames()
        {
            var harness = CreateHarness(
                "[{\"type\":\"GroupMembership\",\"source\":\"" +
                Guid.NewGuid() +
                "\",\"identityTypes\":[\"agents\",\"users\"]}]");

            await new OrchestratorFunction().RunOrchestratorAsync(
                harness.Context.Object);

            Assert.IsNotNull(harness.SubOrchestratorRequest);
            CollectionAssert.AreEqual(
                new[] { "users", "agents" },
                harness.SubOrchestratorRequest.IdentityTypes);
        }

        [TestMethod]
        public async Task RunOrchestratorAsync_OmittedSelection_CarriesNull()
        {
            var harness = CreateHarness(
                "[{\"type\":\"GroupMembership\",\"source\":\"" +
                Guid.NewGuid() +
                "\"}]");

            await new OrchestratorFunction().RunOrchestratorAsync(
                harness.Context.Object);

            Assert.IsNotNull(harness.SubOrchestratorRequest);
            Assert.IsNull(harness.SubOrchestratorRequest.IdentityTypes);
        }

        [TestMethod]
        public async Task RunOrchestratorAsync_DuplicateSelectionProperty_StopsWithSchemaError()
        {
            var sourceGroupId = Guid.NewGuid();
            var harness = CreateHarness(
                "[{\"type\":\"GroupMembership\",\"source\":\"" +
                sourceGroupId +
                "\",\"identityTypes\":[\"users\"],\"identityTypes\":[\"agents\"]}]");

            await new OrchestratorFunction().RunOrchestratorAsync(
                harness.Context.Object);

            Assert.IsNull(harness.SubOrchestratorRequest);
            harness.Context.Verify(
                context => context.CallActivityAsync(
                    It.Is<TaskName>(name =>
                        name.Name == nameof(JobStatusUpdaterFunction)),
                    It.Is<JobStatusUpdaterRequest>(
                        request => request.Status == SyncStatus.SchemaError),
                    It.IsAny<TaskOptions>()),
                Times.Once);
        }

        private static OrchestratorHarness CreateHarness(string query)
        {
            var groupId = Guid.NewGuid();
            var syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                Query = query
            };
            var request = new OrchestratorRequest
            {
                SyncJob = syncJob,
                CurrentPart = 1,
                TotalParts = 2
            };
            var context = new Mock<TaskOrchestrationContext>();
            var harness = new OrchestratorHarness(context);
            context
                .Setup(orchestrationContext =>
                    orchestrationContext.GetInput<OrchestratorRequest>())
                .Returns(request);
            context
                .Setup(orchestrationContext =>
                    orchestrationContext.CreateReplaySafeLogger(
                        It.IsAny<string>()))
                .Returns(NullLogger.Instance);
            context
                .Setup(orchestrationContext =>
                    orchestrationContext.CallActivityAsync<Guid>(
                        It.Is<TaskName>(name =>
                            name.Name == nameof(GetGroupFunction)),
                        It.IsAny<GetGroupRequest>(),
                        It.IsAny<TaskOptions>()))
                .ReturnsAsync(groupId);
            context
                .Setup(orchestrationContext =>
                    orchestrationContext.CallActivityAsync<GroupReaderResponse>(
                        It.Is<TaskName>(name =>
                            name.Name == nameof(GroupReaderFunction)),
                        It.IsAny<GroupReaderRequest>(),
                        It.IsAny<TaskOptions>()))
                .ReturnsAsync(new GroupReaderResponse
                {
                    SourceGroup = new AzureADGroup
                    {
                        ObjectId = Guid.Parse(
                            System.Text.Json.JsonDocument.Parse(query)
                                .RootElement[0]
                                .GetProperty("source")
                                .GetString())
                    }
                });
            context
                .Setup(orchestrationContext =>
                    orchestrationContext.CallActivityAsync<bool>(
                        It.Is<TaskName>(name =>
                            name.Name == nameof(SchemaValidatorFunction)),
                        It.IsAny<SchemaValidatorRequest>(),
                        It.IsAny<TaskOptions>()))
                .ReturnsAsync(true);
            context
                .Setup(orchestrationContext =>
                    orchestrationContext.CallSubOrchestratorAsync<SubOrchestratorResponse>(
                        It.Is<TaskName>(name =>
                            name.Name == nameof(SubOrchestratorFunction)),
                        It.IsAny<GroupMembershipRequest>(),
                        It.IsAny<TaskOptions>()))
                .Returns<TaskName, GroupMembershipRequest, TaskOptions>(
                    (_, subOrchestratorRequest, _) =>
                    {
                        harness.SubOrchestratorRequest =
                            subOrchestratorRequest;
                        return Task.FromResult(new SubOrchestratorResponse
                        {
                            Status = SyncStatus.InProgress,
                            FilePath = "part.json"
                        });
                    });
            context
                .Setup(orchestrationContext =>
                    orchestrationContext.CallActivityAsync(
                        It.Is<TaskName>(name =>
                            name.Name == nameof(QueueMessageSenderFunction)),
                        It.IsAny<MembershipAggregatorHttpRequest>(),
                        It.IsAny<TaskOptions>()))
                .Returns(Task.CompletedTask);
            context
                .Setup(orchestrationContext =>
                    orchestrationContext.CallActivityAsync(
                        It.IsAny<TaskName>(),
                        It.IsAny<JobStatusUpdaterRequest>(),
                        It.IsAny<TaskOptions>()))
                .Returns(Task.CompletedTask);
            return harness;
        }

        private sealed class OrchestratorHarness
        {
            public OrchestratorHarness(Mock<TaskOrchestrationContext> context)
            {
                Context = context;
            }

            public Mock<TaskOrchestrationContext> Context { get; }
            public GroupMembershipRequest SubOrchestratorRequest { get; set; }
        }
    }
}
