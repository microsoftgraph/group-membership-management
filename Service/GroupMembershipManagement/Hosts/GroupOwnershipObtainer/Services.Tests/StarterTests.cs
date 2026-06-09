// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using Hosts.GroupOwnershipObtainer;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using Repositories.Contracts.InjectConfig;
using Repositories.Mocks;
using Services.Contracts;
using System.Text;
using System.Text.Json;

namespace Tests.Services
{
    [TestClass]
    public class StarterTests
    {
        private Mock<IDryRunValue> _dryRunValue = null!;
        private Mock<ISyncJobStatusService> _syncJobStatusService = null!;
        private Mock<MockDurableTaskClient> _durableTaskClient = null!;
        private SyncJob _syncJob = null!;

        [TestInitialize]
        public void Setup()
        {
            _dryRunValue = new Mock<IDryRunValue>();
            _syncJobStatusService = new Mock<ISyncJobStatusService>();
            _durableTaskClient = new Mock<MockDurableTaskClient>();

            _syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                Query = "<query>",
                Status = "InProgress",
                Period = 6,
                MembershipType = "GroupMembership",
                Group = new Group
                {
                    GroupId = Guid.NewGuid()
                }
            };
        }

        [TestMethod]
        public async Task TestRegularSyncJobRun()
        {
            var syncJobBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(_syncJob));
            var properties = new Dictionary<string, object>
            {
                { "CurrentPart", 1},
                { "TotalParts", 3 }
            };

            var message = ServiceBusModelFactory.ServiceBusReceivedMessage(new BinaryData(syncJobBytes), properties: properties);
            var starterFunction = new StarterFunction(
                NullLogger<StarterFunction>.Instance,
                _syncJobStatusService.Object,
                _dryRunValue.Object);
            await starterFunction.RunAsync(message, _durableTaskClient.Object);

            _durableTaskClient.Verify(x => x.ScheduleNewOrchestrationInstanceAsync(
                                                        It.IsAny<TaskName>(),
                                                        It.Is<OrchestratorRequest>(r => r.CurrentPart == 1 && r.TotalParts == 3),
                                                        null,
                                                        It.IsAny<CancellationToken>()
                                                ), Times.Once);

            _syncJobStatusService.Verify(x => x.UpdateJobStatusAsync(
                                                    It.IsAny<SyncJob>(),
                                                    It.IsAny<SyncStatus?>(),
                                                    It.IsAny<Models.SyncJobHistory.SyncJobHistory?>(),
                                                    It.IsAny<string?>()), Times.Never);
        }

        [TestMethod]
        public async Task TestDryRunSyncJobRun()
        {
            _dryRunValue.SetupGet(x => x.DryRunEnabled).Returns(true);
            _syncJob.DryRunTimeStamp = DateTime.UtcNow.AddHours(-1);

            var syncJobBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(_syncJob));
            var properties = new Dictionary<string, object>
            {
                { "CurrentPart", 1},
                { "TotalParts", 3 }
            };

            var message = ServiceBusModelFactory.ServiceBusReceivedMessage(new BinaryData(syncJobBytes), properties: properties);

            var starterFunction = new StarterFunction(
                NullLogger<StarterFunction>.Instance,
                _syncJobStatusService.Object,
                _dryRunValue.Object);
            await starterFunction.RunAsync(message, _durableTaskClient.Object);

            _durableTaskClient.Verify(x => x.ScheduleNewOrchestrationInstanceAsync(
                                                        It.IsAny<TaskName>(),
                                                        It.IsAny<OrchestratorRequest>(),
                                                        null,
                                                        It.IsAny<CancellationToken>()), Times.Never);
            _syncJobStatusService.Verify(x => x.UpdateJobStatusAsync(
                                                    It.Is<SyncJob>(job => job.Status == SyncStatus.Idle.ToString()),
                                                    It.Is<SyncStatus?>(s => s == SyncStatus.Idle),
                                                    It.Is<Models.SyncJobHistory.SyncJobHistory>(h => h.Status == SyncStatus.Idle.ToString() && h.UpdatedByFunction == "GroupOwnershipObtainer" && h.EndTime.HasValue),
                                                    It.Is<string?>(fn => fn == "GroupOwnershipObtainer")), Times.Once);
        }
    }
}
