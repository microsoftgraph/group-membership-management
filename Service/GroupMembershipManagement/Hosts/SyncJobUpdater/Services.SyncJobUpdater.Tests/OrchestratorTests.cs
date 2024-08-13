// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Models;
using Repositories.Contracts;
using Services.SyncJobUpdater.Tests.Mocks;
using Hosts.SyncJobUpdater;

namespace Services.Tests
{
    [TestClass]
    public class OrchestratorFunctionTests
    {
        private Mock<IDurableOrchestrationContext> _mockContext;
        private MockLoggingRepository _mockLogger;
        private OrchestratorFunction _orchestratorFunction;
        private const string GroupMembership = "GroupMembership";

        [TestInitialize]
        public void Setup()
        {
            _mockContext = new Mock<IDurableOrchestrationContext>();
            _mockLogger = new MockLoggingRepository();
            _orchestratorFunction = new OrchestratorFunction();
        }

        [TestMethod]
        public async Task RunOrchestratorTest()
        {
            var syncJob = SampleDataHelper.CreateSampleSyncJobs(1, GroupMembership).First();
            var orchestratorRequest = new OrchestratorRequest
            {
                SyncJob = syncJob,
                Status = SyncStatus.Idle.ToString()
            };

            _mockContext.Setup(c => c.GetInput<OrchestratorRequest>()).Returns(orchestratorRequest);

            await _orchestratorFunction.RunOrchestratorAsync(_mockContext.Object, null);
            _mockContext.Verify(context => context.CallActivityAsync(nameof(JobStatusUpdaterFunction), It.IsAny<JobStatusUpdaterRequest>()), Times.Once);
            _mockContext.Verify(context => context.CallActivityAsync(nameof(LoggerFunction), It.IsAny<LoggerRequest>()), Times.Exactly(2));
        }

        [TestMethod]
        public async Task RunOrchestratorTestWithUnknownStatus()
        {
            var syncJob = SampleDataHelper.CreateSampleSyncJobs(1, GroupMembership).First();
            var orchestratorRequest = new OrchestratorRequest
            {
                SyncJob = syncJob,
                Status = "Unknown"
            };
            var loggerRequests = new List<LoggerRequest>();
            _mockContext
                .Setup(context => context.CallActivityAsync(nameof(LoggerFunction), It.IsAny<LoggerRequest>()))
                .Callback<string, object>((name, request) => loggerRequests.Add((LoggerRequest)request))
                .Returns(Task.CompletedTask);

            _mockContext.Setup(c => c.GetInput<OrchestratorRequest>()).Returns(orchestratorRequest);

            await _orchestratorFunction.RunOrchestratorAsync(_mockContext.Object, null);
            _mockContext.Verify(context => context.CallActivityAsync(nameof(LoggerFunction), It.IsAny<LoggerRequest>()), Times.Exactly(2), "Expected LoggerFunction to be called exactly twice.");
            Assert.IsTrue(loggerRequests.Any(req => req.Message.Contains("unknown status")), "Expected an error log message for unknown status.");
            _mockContext.Verify(context => context.CallActivityAsync(nameof(JobStatusUpdaterFunction), It.IsAny<JobStatusUpdaterRequest>()), Times.Once);
        }

    }
}
