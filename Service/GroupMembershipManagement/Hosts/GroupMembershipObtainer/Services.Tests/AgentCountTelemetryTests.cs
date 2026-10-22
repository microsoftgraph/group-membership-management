// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.GroupMembershipObtainer;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.AzureMaintenance;
using Models.ServiceBus;
using Moq;
using Repositories.Contracts;
using Repositories.Contracts.DestinationResolution;
using Repositories.Contracts.InjectConfig;
using Repositories.Mocks;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tests.Services.Infrastructure;

namespace Tests.Services
{
    /// <summary>
    /// I15 and FR-020: an agent count that could not be read is never emitted; a measured count is emitted unchanged.
    /// </summary>
    [TestClass]
    public sealed class AgentCountTelemetryTests
    {
        [DataTestMethod]
        [DataRow(true, DisplayName = "full read")]
        [DataRow(false, DisplayName = "initial delta read")]
        public async Task UnreadableAgentCount_EmitsNoEventAndLogsWarning(bool fullRead)
        {
            var harness = CreateHarness(fullRead, agentCount: null);

            await harness.Function.RunSubOrchestratorAsync(harness.Context.Object);

            Assert.AreEqual(0, harness.Channel.Events.Count(e => e.Name == "AgentMembersRead"),
                "A failed agent count must not be emitted as a measurement.");
            Assert.AreEqual(1, harness.Logger.Entries.Count(entry =>
                entry.Level == LogLevel.Warning &&
                entry.Message.Contains("agent count", StringComparison.OrdinalIgnoreCase)));
        }

        [DataTestMethod]
        [DataRow(true, "Transitive", DisplayName = "full read")]
        [DataRow(false, "Delta", DisplayName = "initial delta read")]
        public async Task MeasuredAgentCount_EmitsEventUnchanged(bool fullRead, string readPath)
        {
            var harness = CreateHarness(fullRead, agentCount: 7);

            await harness.Function.RunSubOrchestratorAsync(harness.Context.Object);

            var agentEvent = harness.Channel.Events.Single(e => e.Name == "AgentMembersRead");
            Assert.AreEqual("7", agentEvent.Properties["AgentCount"]);
            Assert.AreEqual(readPath, agentEvent.Properties["ReadPath"]);
            Assert.AreEqual("Source", agentEvent.Properties["GroupRole"]);
            Assert.AreEqual(harness.Request.SourceGroup.ObjectId.ToString(), agentEvent.Properties["GroupObjectId"]);
            Assert.AreEqual(harness.Request.GroupId.ToString(), agentEvent.Properties["TargetGroupObjectId"]);
            Assert.AreEqual(0, harness.Logger.Entries.Count(entry =>
                entry.Level == LogLevel.Warning &&
                entry.Message.Contains("agent count", StringComparison.OrdinalIgnoreCase)));
        }

        [TestMethod]
        public async Task MeasuredZero_IsStillEmitted()
        {
            var harness = CreateHarness(fullRead: true, agentCount: 0);

            await harness.Function.RunSubOrchestratorAsync(harness.Context.Object);

            Assert.AreEqual("0", harness.Channel.Events.Single(e => e.Name == "AgentMembersRead").Properties["AgentCount"]);
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow(4)]
        public async Task GetAgentUserCountFunction_PassesThroughRepositoryResult(int? repositoryCount)
        {
            var repository = new Mock<IGraphGroupRepository>();
            repository.Setup(r => r.GetAgentUserCountAsync(It.IsAny<Guid>())).ReturnsAsync(repositoryCount);
            var resolver = new Mock<IDestinationResolver>();
            var calculator = new SGMembershipCalculator(
                repository.Object,
                new InMemoryBlobStorageRepository(),
                Mock.Of<IDatabaseSyncJobsRepository>(),
                resolver.Object,
                Mock.Of<IServiceBusQueueRepository>(),
                Mock.Of<IDatabaseDestinationAttributesRepository>(),
                NullLogger<SGMembershipCalculator>.Instance,
                Mock.Of<IDryRunValue>(),
                Mock.Of<ISyncJobStatusService>());
            var function = new GetAgentUserCountFunction(NullLogger<GetAgentUserCountFunction>.Instance, calculator);

            var result = await function.GetAgentUserCountAsync(new GetAgentUserCountRequest
            {
                GroupId = Guid.NewGuid(),
                SyncJob = new SyncJob { Id = Guid.NewGuid(), RunId = Guid.NewGuid() }
            });

            Assert.AreEqual(repositoryCount, result);
        }

        private static Harness CreateHarness(bool fullRead, int? agentCount)
        {
            var request = new GroupMembershipRequest
            {
                SyncJob = new SyncJob
                {
                    Id = Guid.NewGuid(),
                    RunId = Guid.NewGuid(),
                    Query = "[{\"type\":\"GroupMembership\"}]"
                },
                SourceGroup = new AzureADGroup { ObjectId = Guid.NewGuid() },
                GroupId = Guid.NewGuid(),
                CurrentPart = 1,
                TotalParts = 2,
                IsDestinationPart = false
            };
            var harness = new Harness(request, fullRead);
            var context = harness.Context;
            context.Setup(c => c.GetInput<GroupMembershipRequest>()).Returns(request);
            context.Setup(c => c.CreateReplaySafeLogger(It.IsAny<string>())).Returns(harness.Logger);
            context.SetupGet(c => c.IsReplaying).Returns(false);
            context.SetupGet(c => c.CurrentUtcDateTime).Returns(new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc));
            context
                .Setup(c => c.CallActivityAsync<bool>(
                    It.Is<TaskName>(name => name.Name == nameof(GroupValidatorFunction)),
                    It.IsAny<object>(),
                    It.IsAny<TaskOptions>()))
                .ReturnsAsync(true);
            context
                .Setup(c => c.CallActivityAsync<int>(
                    It.Is<TaskName>(name => name.Name == nameof(GetTransitiveGroupCountFunction)),
                    It.IsAny<object>(),
                    It.IsAny<TaskOptions>()))
                .ReturnsAsync(fullRead ? 1 : 0);
            context
                .Setup(c => c.CallActivityAsync<string>(
                    It.Is<TaskName>(name => name.Name == nameof(FileDownloaderFunction)),
                    It.IsAny<object>(),
                    It.IsAny<TaskOptions>()))
                .ReturnsAsync((string)null);
            context
                .Setup(c => c.CallActivityAsync<BlobResult>(
                    It.Is<TaskName>(name => name.Name == nameof(BlobCheckerFunction)),
                    It.IsAny<object>(),
                    It.IsAny<TaskOptions>()))
                .ReturnsAsync(new BlobResult { BlobStatus = BlobStatus.NotFound });
            context
                .Setup(c => c.CallActivityAsync<string>(
                    It.Is<TaskName>(name => name.Name == nameof(MembersReaderFunction)),
                    It.IsAny<object>(),
                    It.IsAny<TaskOptions>()))
                .ReturnsAsync((string)null);
            context
                .Setup(c => c.CallActivityAsync<DeltaUrls>(
                    It.Is<TaskName>(name => name.Name == nameof(DeltaUserReaderFunction)),
                    It.IsAny<object>(),
                    It.IsAny<TaskOptions>()))
                .ReturnsAsync(new DeltaUrls { DeltaUrl = "delta" });
            context
                .Setup(c => c.CallActivityAsync<GroupMembershipFileResult>(
                    It.Is<TaskName>(name => name.Name == nameof(TransitiveAndDeltaUsersSenderFunction)),
                    It.IsAny<object>(),
                    It.IsAny<TaskOptions>()))
                .ReturnsAsync(new GroupMembershipFileResult { FilePath = "part.json", MemberCount = 1 });
            context
                .Setup(c => c.CallActivityAsync<int?>(
                    It.Is<TaskName>(name => name.Name == nameof(GetAgentUserCountFunction)),
                    It.IsAny<object>(),
                    It.IsAny<TaskOptions>()))
                .ReturnsAsync(agentCount);
            return harness;
        }

        private sealed class Harness
        {
            public Harness(GroupMembershipRequest request, bool fullRead)
            {
                Request = request;
                var configuration = TelemetryConfiguration.CreateDefault();
                configuration.TelemetryChannel = Channel;
                Function = new SubOrchestratorFunction(
                    new MockDeltaCachingConfig { DeltaCacheEnabled = true },
                    new TelemetryClient(configuration));
            }

            public GroupMembershipRequest Request { get; }
            public Mock<TaskOrchestrationContext> Context { get; } = new();
            public RecordingLogger<SubOrchestratorFunction> Logger { get; } = new();
            public CapturingChannel Channel { get; } = new();
            public SubOrchestratorFunction Function { get; }
        }

        private sealed class CapturingChannel : ITelemetryChannel
        {
            public List<EventTelemetry> Events { get; } = new();
            public bool? DeveloperMode { get; set; }
            public string EndpointAddress { get; set; }
            public void Send(ITelemetry item)
            {
                if (item is EventTelemetry eventTelemetry)
                {
                    Events.Add(eventTelemetry);
                }
            }

            public void Flush()
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
