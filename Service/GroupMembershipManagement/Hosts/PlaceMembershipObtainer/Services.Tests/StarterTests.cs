// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using Hosts.PlaceMembershipObtainer;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.Mocks;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.Services
{
    [TestClass]
    public class StarterTests
    {
        private Mock<IDryRunValue> _dryRunValue;
        private Mock<IDatabaseSyncJobsRepository> _syncJobRepository;
        private Mock<MockDurableTaskClient> _durableOrchestrationClient;
        private SyncJob _syncJob;

        [TestInitialize]
        public void Setup()
        {
            _dryRunValue = new Mock<IDryRunValue>();
            _syncJobRepository = new Mock<IDatabaseSyncJobsRepository>();
            _durableOrchestrationClient = new Mock<MockDurableTaskClient>();

            _syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                MembershipType = "GroupMembership",
                Query = "[{ \"type\": \"PlaceMembership\", \"source\": \"https://graph.microsoft.com/v1.0/places/microsoft.graph.room\" }]",
                Status = "InProgress",
                Period = 6
            };
            _syncJob.Group = new Group
            {
                SyncJobId = _syncJob.Id,
                GroupId = Guid.NewGuid()
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
            var starterFunction = new StarterFunction(NullLogger<StarterFunction>.Instance, _syncJobRepository.Object, _dryRunValue.Object);
            await starterFunction.RunAsync(message, _durableOrchestrationClient.Object);

            _durableOrchestrationClient.Verify(x => x.ScheduleNewOrchestrationInstanceAsync(
                                                        It.IsAny<TaskName>(),
                                                        It.Is<OrchestratorRequest>(r => r.CurrentPart == 1 && r.TotalParts == 3),
                                                        null,
                                                        It.IsAny<CancellationToken>()
                                                ), Times.Once);
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

            var starterFunction = new StarterFunction(NullLogger<StarterFunction>.Instance, _syncJobRepository.Object, _dryRunValue.Object);
            await starterFunction.RunAsync(message, _durableOrchestrationClient.Object);

            _durableOrchestrationClient.Verify(x => x.ScheduleNewOrchestrationInstanceAsync(It.IsAny<TaskName>(), It.IsAny<OrchestratorRequest>(), null, It.IsAny<CancellationToken>()), Times.Never);
            _syncJobRepository.Verify(x => x.UpdateSyncJobStatusAsync(It.IsAny<IEnumerable<SyncJob>>(), It.Is<SyncStatus>(s => s == SyncStatus.Idle)), Times.Once);
        }
    }
}