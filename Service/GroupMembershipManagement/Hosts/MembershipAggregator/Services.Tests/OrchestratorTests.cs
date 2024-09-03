// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.MembershipAggregator;
using MembershipAggregator.Services.Entities;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Services.Tests
{

    [TestClass]
    public class OrchestratorTests
    {
        private SyncJob _syncJob;
        private JobState _jobState;
        private JobTrackerEntityFake _jobTrackerEntity;
        private MembershipAggregatorHttpRequest _membershipAggregatorHttpRequest;
        private MembershipSubOrchestratorResponse _membershipSubOrchestratorResponse;
        private TelemetryClient _telemetryClient;

        private Mock<IConfiguration> _configuration;
        private Mock<ILoggingRepository> _loggingRepository;
        private Mock<IDatabaseSyncJobsRepository> _syncJobRepository;
        private Mock<TaskOrchestrationContext> _durableContext;
        private Mock<IServiceBusTopicsRepository> _serviceBusTopicsRepository;

        [TestInitialize]
        public void SetupTest()
        {
            _configuration = new Mock<IConfiguration>();
            _loggingRepository = new Mock<ILoggingRepository>();
            _syncJobRepository = new Mock<IDatabaseSyncJobsRepository>();
            _durableContext = new Mock<TaskOrchestrationContext>();
            _telemetryClient = new TelemetryClient(new TelemetryConfiguration());
            _serviceBusTopicsRepository = new Mock<IServiceBusTopicsRepository>();
            _jobState = new JobState();
            _jobTrackerEntity = new JobTrackerEntityFake();
            _jobTrackerEntity.SetState(_jobState);


            var targetOfficeGroupId = Guid.NewGuid();
            _syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                TargetOfficeGroupId = targetOfficeGroupId,
                ThresholdPercentageForAdditions = 80,
                ThresholdPercentageForRemovals = 20,
                LastRunTime = DateTime.UtcNow.AddDays(-1),
                Requestor = "user@domail.com",
                RunId = Guid.NewGuid(),
                ThresholdViolations = 0,
                Query = "[{\"type\":\"GroupMembership\",\"source\":\"9e9b029b-52a7-467a-94fb-325b6241022b\"},{\"type\":\"GroupOwnership\",\"source\":[\"GroupMembership\"]}]",
                Destination = $"[{{\"value\":{{\"objectId\":\"{targetOfficeGroupId}\"}},\"type\":\"GraphUpdater\"}}]"
            };

            _membershipAggregatorHttpRequest = new MembershipAggregatorHttpRequest
            {
                FilePath = "/file-path.json",
                SyncJob = _syncJob,
                PartNumber = 1,
                PartsCount = 1
            };

            _membershipSubOrchestratorResponse = new MembershipSubOrchestratorResponse
            {
                FilePath = "http://file-path",
                MembershipDeltaStatus = MembershipDeltaStatus.Ok
            };

            _configuration.Setup(x => x[It.Is<string>(x => x == "graphUpdaterUrl")])
                            .Returns("http://graph-updater-url");
            _configuration.Setup(x => x[It.Is<string>(x => x == "graphUpdaterFunctionKey")])
                            .Returns("112233445566");

            _syncJobRepository.Setup(x => x.GetSyncJobAsync(It.IsAny<Guid>()))
                                .ReturnsAsync(() => _syncJob);

            _durableContext.Setup(x => x.GetInput<MembershipAggregatorHttpRequest>())
                            .Returns(() => _membershipAggregatorHttpRequest);

            _durableContext.Setup(x => x.CallActivityAsync(It.Is<TaskName>(x => x.Name == nameof(TelemetryTrackerFunction)), It.IsAny<TelemetryTrackerRequest>(), It.IsAny<TaskOptions>()))
                    .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                    {
                        var telemetryRequest = request as TelemetryTrackerRequest;
                        await CallTelemetryTrackerFunctionAsync(telemetryRequest);
                    });

            _durableContext.Setup(x => x.CallActivityAsync(It.Is<TaskName>(x => x.Name == nameof(LoggerFunction)), It.IsAny<LoggerRequest>(), It.IsAny<TaskOptions>()))
                            .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                            {
                                var loggerRequest = request as LoggerRequest;
                                await CallLoggerFunctionAsync(loggerRequest);
                            });

            _durableContext.Setup(x => x.CallActivityAsync(It.Is<TaskName>(x => x.Name == nameof(JobStatusUpdaterFunction)), It.IsAny<JobStatusUpdaterRequest>(), It.IsAny<TaskOptions>()))
                            .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                            {
                                var updateRequest = request as JobStatusUpdaterRequest;
                                await CallJobStatusUpdaterFunctionAsync(updateRequest);
                            });

            _durableContext.Setup(x => x.CallSubOrchestratorAsync<MembershipSubOrchestratorResponse>
                                                (
                                                    It.Is<TaskName>(x => x.Name == nameof(MembershipSubOrchestratorFunction)),
                                                    It.IsAny<MembershipSubOrchestratorRequest>(),
                                                    It.IsAny<TaskOptions>())
                                                )
                            .ReturnsAsync(() => _membershipSubOrchestratorResponse);

            _durableContext.Setup(x => x.CallActivityAsync(nameof(TopicMessageSenderFunction), It.IsAny<MembershipHttpRequest>(), It.IsAny<TaskOptions>()))
                           .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                           {
                               var membershipRequest = request as MembershipHttpRequest;
                               await CallTopicMessageSenderFunctionAsync(membershipRequest);
                           });

            var entitiesMock = new Mock<TaskOrchestrationEntityFeature>();

            entitiesMock.Setup(x => x.CallEntityAsync(It.IsAny<EntityInstanceId>(), "SetTotalParts", It.IsAny<int>(), null))
                        .Callback<EntityInstanceId, string, object, CallEntityOptions>(async (entityId, operationName, request, options) =>
                        {
                            await _jobTrackerEntity.SetTotalParts((int)request);
                        });

            entitiesMock.Setup(x => x.CallEntityAsync(It.IsAny<EntityInstanceId>(), "AddCompletedPart", It.IsAny<string>(), null))
                        .Callback<EntityInstanceId, string, object, CallEntityOptions>(async (entityId, operationName, request, options) =>
                        {
                            await _jobTrackerEntity.AddCompletedPart((string)request);
                        });

            entitiesMock.Setup(x => x.CallEntityAsync(It.IsAny<EntityInstanceId>(), "SetDestinationPart", It.IsAny<string>(), null))
                        .Callback<EntityInstanceId, string, object, CallEntityOptions>(async (entityId, operationName, request, options) =>
                        {
                            await _jobTrackerEntity.SetDestinationPart((string)request);
                        });

            entitiesMock.Setup(x => x.CallEntityAsync<bool>(It.IsAny<EntityInstanceId>(), It.Is<string>(x => x == "IsComplete"), null, null))
                        .Returns(async () => await _jobTrackerEntity.IsComplete());

            _durableContext.Setup(x => x.Entities).Returns(entitiesMock.Object);
        }

        [TestMethod]
        public async Task TestJobWithSinglePartAsync()
        {
            var orchestratorFunction = new OrchestratorFunction(_configuration.Object, _loggingRepository.Object);
            await orchestratorFunction.RunOrchestratorAsync(_durableContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("Sent message")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()));
            _syncJobRepository.Verify(x => x.UpdateSyncJobsAsync(It.IsAny<IEnumerable<SyncJob>>(), It.IsAny<SyncStatus>()), Times.Never());
        }

        [TestMethod]
        public async Task TestMissingPartAsync()
        {
            var entitiesMock = new Mock<TaskOrchestrationEntityFeature>();
            entitiesMock.Setup(x => x.CallEntityAsync<bool>(It.IsAny<EntityInstanceId>(), It.Is<string>(x => x == "IsComplete"), null, null))
                        .ReturnsAsync(() => false);
            _durableContext.Setup(x => x.Entities).Returns(entitiesMock.Object);

            _membershipAggregatorHttpRequest.PartsCount = 2;

            var orchestratorFunction = new OrchestratorFunction(_configuration.Object, _loggingRepository.Object);
            await orchestratorFunction.RunOrchestratorAsync(_durableContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("Sent message")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            _syncJobRepository.Verify(x => x.UpdateSyncJobsAsync(It.IsAny<IEnumerable<SyncJob>>(), It.IsAny<SyncStatus>()), Times.Never());
        }

        [TestMethod]
        public async Task TestDestinationPartAsync()
        {
            _membershipAggregatorHttpRequest.IsDestinationPart = true;

            var orchestratorFunction = new OrchestratorFunction(_configuration.Object, _loggingRepository.Object);
            await orchestratorFunction.RunOrchestratorAsync(_durableContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("Sent message")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()));
            _syncJobRepository.Verify(x => x.UpdateSyncJobsAsync(It.IsAny<IEnumerable<SyncJob>>(), It.IsAny<SyncStatus>()), Times.Never());
        }

        [TestMethod]
        public async Task TestNotSuccessMembershipDeltaStatusAsync()
        {
            _membershipSubOrchestratorResponse.MembershipDeltaStatus = MembershipDeltaStatus.Error;

            var orchestratorFunction = new OrchestratorFunction(_configuration.Object, _loggingRepository.Object);
            await orchestratorFunction.RunOrchestratorAsync(_durableContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message == "Calling GraphUpdater"), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("GraphUpdater response Code")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _syncJobRepository.Verify(x => x.UpdateSyncJobsAsync(It.IsAny<IEnumerable<SyncJob>>(), It.IsAny<SyncStatus>()), Times.Never());
        }

        [TestMethod]
        public async Task HandleFileNotFoundAsync()
        {
            _durableContext.Setup(x => x.CallSubOrchestratorAsync<MembershipSubOrchestratorResponse>
                                               (
                                                   nameof(MembershipSubOrchestratorFunction),
                                                   It.IsAny<MembershipSubOrchestratorRequest>(),
                                                   It.IsAny<TaskOptions>())
                                               )
                            .Throws<FileNotFoundException>();

            var orchestratorFunction = new OrchestratorFunction(_configuration.Object, _loggingRepository.Object);
            await Assert.ThrowsExceptionAsync<FileNotFoundException>(async () => await orchestratorFunction.RunOrchestratorAsync(_durableContext.Object));

            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message == "Calling GraphUpdater"), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("GraphUpdater response Code")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            _durableContext.Verify(x => x.CallActivityAsync(
                                                            It.Is<TaskName>(x => x.Name == nameof(JobStatusUpdaterFunction)),
                                                            It.Is<JobStatusUpdaterRequest>(x => x.Status == SyncStatus.FileNotFound),
                                                            It.IsAny<TaskOptions>()
                                                           )
                                            , Times.Once());
        }

        [TestMethod]
        public async Task HandleUnexpectedExceptionAsync()
        {
            _durableContext.Setup(x => x.CallSubOrchestratorAsync<MembershipSubOrchestratorResponse>
                                               (
                                                   It.Is<TaskName>(x => x.Name == nameof(MembershipSubOrchestratorFunction)),
                                                   It.IsAny<MembershipSubOrchestratorRequest>(),
                                                   It.IsAny<TaskOptions>())
                                               )
                            .Throws<Exception>();

            var orchestratorFunction = new OrchestratorFunction(_configuration.Object, _loggingRepository.Object);
            await Assert.ThrowsExceptionAsync<Exception>(async () => await orchestratorFunction.RunOrchestratorAsync(_durableContext.Object));

            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message == "Calling GraphUpdater"), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("GraphUpdater response Code")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("Unexpected exception")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Once());
            _durableContext.Verify(x => x.CallActivityAsync(
                                                            It.Is<TaskName>(x => x.Name == nameof(JobStatusUpdaterFunction)),
                                                            It.Is<JobStatusUpdaterRequest>(x => x.Status == SyncStatus.Error),
                                                            It.IsAny<TaskOptions>()
                                                           )
                                            , Times.Once());
        }

        private async Task CallTelemetryTrackerFunctionAsync(TelemetryTrackerRequest request)
        {
            var telemetryTrackerFunction = new TelemetryTrackerFunction(_loggingRepository.Object, _telemetryClient);
            await telemetryTrackerFunction.TrackEventAsync(request);
        }

        private async Task CallLoggerFunctionAsync(LoggerRequest request)
        {
            var loggerFunction = new LoggerFunction(_loggingRepository.Object);
            await loggerFunction.LogMessageAsync(request);
        }

        private async Task CallJobStatusUpdaterFunctionAsync(JobStatusUpdaterRequest request)
        {
            var jobStatusUpdaterFunction = new JobStatusUpdaterFunction(_loggingRepository.Object, _syncJobRepository.Object);
            await jobStatusUpdaterFunction.UpdateJobStatusAsync(request);
        }

        private async Task CallTopicMessageSenderFunctionAsync(MembershipHttpRequest request)
        {
            var topicMessageSenderFunction = new TopicMessageSenderFunction(_loggingRepository.Object, _serviceBusTopicsRepository.Object);
            await topicMessageSenderFunction.SendMessageAsync(request);
        }
    }
}
