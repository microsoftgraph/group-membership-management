// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using Entities;
using Hosts.SecurityGroup;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Models.Entities;
using Newtonsoft.Json;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Tests.Services
{
    [TestClass]
    public class StarterTests
    {
        private Mock<IDryRunValue> _dryRunValue;
        private Mock<ILoggingRepository> _loggingRepository;
        private Mock<ISyncJobRepository> _syncJobRepository;
        private Mock<IDurableOrchestrationClient> _durableOrchestrationClient;
        private SyncJob _syncJob;

        [TestInitialize]
        public void Setup()
        {
            _dryRunValue = new Mock<IDryRunValue>();
            _loggingRepository = new Mock<ILoggingRepository>();
            _syncJobRepository = new Mock<ISyncJobRepository>();
            _durableOrchestrationClient = new Mock<IDurableOrchestrationClient>();

            _syncJob = new SyncJob
            {
                RowKey = Guid.NewGuid().ToString(),
                PartitionKey = "00-00-0000",
                TargetOfficeGroupId = Guid.NewGuid(),
                Query = QuerySample.GenerateQuerySample("SecurityGroup").GetQuery(),
                Status = "InProgress",
                Period = 6
            };
        }

        [TestMethod]
        public async Task TestRegularSyncJobRun()
        {
            var syncJobBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(_syncJob));
            var properties = new Dictionary<string, object>
            {
                { "CurrentPart", 1},
                { "TotalParts", 3 }
            };

            var message = ServiceBusModelFactory.ServiceBusReceivedMessage(new BinaryData(syncJobBytes), properties: properties);
            var starterFunction = new StarterFunction(_loggingRepository.Object, _syncJobRepository.Object, _dryRunValue.Object);
            await starterFunction.RunAsync(message, _durableOrchestrationClient.Object);

            _durableOrchestrationClient.Verify(x => x.StartNewAsync(
                                                        It.IsAny<string>(),
                                                        It.Is<OrchestratorRequest>(r => r.CurrentPart == 1 && r.TotalParts == 3)
                                                ), Times.Once);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                                    It.Is<LogMessage>(m => m.Message.StartsWith("InstanceId")),
                                                    It.IsAny<VerbosityLevel>(),
                                                    It.IsAny<string>(),
                                                    It.IsAny<string>()
                                                ), Times.Once);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                                    It.Is<LogMessage>(m => m.Message.Contains("function completed")),
                                                    It.IsAny<VerbosityLevel>(),
                                                    It.IsAny<string>(),
                                                    It.IsAny<string>()
                                                ), Times.Once);
        }

        [TestMethod]
        public async Task TestDryRunSyncJobRun()
        {
            _dryRunValue.SetupGet(x => x.DryRunEnabled).Returns(true);
            _syncJob.DryRunTimeStamp = DateTime.UtcNow.AddHours(-1);

            var syncJobBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(_syncJob));
            var properties = new Dictionary<string, object>
            {
                { "CurrentPart", 1},
                { "TotalParts", 3 }
            };

            var message = ServiceBusModelFactory.ServiceBusReceivedMessage(new BinaryData(syncJobBytes), properties: properties);

            var starterFunction = new StarterFunction(_loggingRepository.Object, _syncJobRepository.Object, _dryRunValue.Object);
            await starterFunction.RunAsync(message, _durableOrchestrationClient.Object);

            _durableOrchestrationClient.Verify(x => x.StartNewAsync(It.IsAny<string>(), It.IsAny<OrchestratorRequest>()), Times.Never);
            _syncJobRepository.Verify(x => x.UpdateSyncJobStatusAsync(It.IsAny<IEnumerable<SyncJob>>(), It.Is<SyncStatus>(s => s == SyncStatus.Idle)), Times.Once);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                                It.Is<LogMessage>(m => m.Message.StartsWith("Setting the status of the sync back to Idle")),
                                                It.IsAny<VerbosityLevel>(),
                                                It.IsAny<string>(),
                                                It.IsAny<string>()
                                                ), Times.Once);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                                It.Is<LogMessage>(m => m.Message.StartsWith("InstanceId")),
                                                It.IsAny<VerbosityLevel>(),
                                                It.IsAny<string>(),
                                                It.IsAny<string>()
                                                ), Times.Never);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                                It.Is<LogMessage>(m => m.Message.Contains("function completed")),
                                                It.IsAny<VerbosityLevel>(),
                                                It.IsAny<string>(),
                                                It.IsAny<string>()
                                                ), Times.Once);
        }
    }
}
