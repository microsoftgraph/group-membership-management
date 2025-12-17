// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Messaging.ServiceBus;
using DIConcreteTypes;
using Hosts.MembershipAggregator;
using MembershipAggregator.Services.Entities;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.SyncJobHistory;
using Moq;
using Repositories.Contracts;
using Repositories.ServiceBusTopics;
using Services.Contracts;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class OrchestratorTests
    {
    private const int SMALL = 400;

        private SyncJob _syncJob;
        private Group _group;
        private JobState _jobState;
        private MembershipAggregatorHttpRequest _membershipAggregatorHttpRequest;
        private MembershipSubOrchestratorResponse _membershipSubOrchestratorResponse;
        private TelemetryClient _telemetryClient;
        private bool _hasSourceCompleted = true;

        private Mock<IConfiguration> _configuration;
        private JobTrackerEntity _jobTrackerEntity;
        private Mock<ILoggingRepository> _loggingRepository;
        private Mock<IDatabaseSyncJobsRepository> _syncJobRepository;
        private Mock<TaskOrchestrationContext> _durableContext;
        private Mock<IServiceBusTopicsRepository> _serviceBusTopicsRepository;
        private IOptions<MultiLaneConfig> _multilaneConfig;
        private ServiceBusTopicsRepository _messageSplitterSender;
        private Mock<ServiceBusSender> _serviceBusSender;
        private Mock<TaskOrchestrationEntityFeature> _entityFeature;
        private Mock<ISyncJobStatusService> _syncJobStatusService;

        private Action<ServiceBusMessage> _onSendingMessage;

        [TestInitialize]
        public void SetupTest()
        {
            _configuration = new Mock<IConfiguration>();
            _jobTrackerEntity = new JobTrackerEntity();
            _entityFeature = new Mock<TaskOrchestrationEntityFeature>();
            _loggingRepository = new Mock<ILoggingRepository>();
            _syncJobRepository = new Mock<IDatabaseSyncJobsRepository>();
            _durableContext = new Mock<TaskOrchestrationContext>();
            _telemetryClient = new TelemetryClient(new TelemetryConfiguration());
            _serviceBusTopicsRepository = new Mock<IServiceBusTopicsRepository>();
            _multilaneConfig = Options.Create(new MultiLaneConfig
            {
                IsEnabled = false,
                Small = SMALL
            });

            _syncJobStatusService = new Mock<ISyncJobStatusService>();

            _serviceBusSender = new Mock<ServiceBusSender>();
            _serviceBusSender.Setup(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()))
                            .Callback<ServiceBusMessage, CancellationToken>((message, token) => _onSendingMessage?.Invoke(message));

            _messageSplitterSender = new ServiceBusTopicsRepository(_serviceBusSender.Object);

            _syncJobStatusService.Setup(x => x.UpdateJobStatusAsync(It.IsAny<SyncJob>(), It.IsAny<SyncStatus?>(), It.IsAny<SyncJobHistory>(), It.IsAny<string>()))
                                  .Returns(Task.CompletedTask);

            var targetOfficeGroupId = Guid.NewGuid();
            _syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                ThresholdPercentageForAdditions = 80,
                ThresholdPercentageForRemovals = 20,
                LastRunTime = DateTime.UtcNow.AddDays(-1),
                Requestor = "user@domail.com",
                RunId = Guid.NewGuid(),
                ThresholdViolations = 0,
                Query = "[{\"type\":\"GroupMembership\",\"source\":\"9e9b029b-52a7-467a-94fb-325b6241022b\"},{\"type\":\"GroupOwnership\",\"source\":[\"GroupMembership\"]}]",
                MembershipType = "GroupMembership"
            };

            _group = new Group
            {
                GroupId = targetOfficeGroupId,
                SyncJobId = _syncJob.Id
            };

            _membershipAggregatorHttpRequest = new MembershipAggregatorHttpRequest
            {
                FilePath = "/file-path.json",
                SyncJob = _syncJob,
                PartNumber = 1,
                PartsCount = 1,
                IsDestinationPart = false
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

            _durableContext.Setup(x => x.CallActivityAsync<Guid>(nameof(GetGroupFunction), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()))
                            .ReturnsAsync(_group.GroupId);

            _durableContext.Setup(x => x.CallActivityAsync(nameof(TelemetryTrackerFunction), It.IsAny<TelemetryTrackerRequest>(), It.IsAny<TaskOptions>()))
                    .Callback<TaskName, object, TaskOptions>(async (name, request, taskOptions) =>
                    {
                        var telemetryRequest = request as TelemetryTrackerRequest;
                        await CallTelemetryTrackerFunctionAsync(telemetryRequest);
                    });

            _durableContext.Setup(x => x.CallActivityAsync(nameof(LoggerFunction), It.IsAny<LoggerRequest>(), It.IsAny<TaskOptions>()))
                            .Callback<TaskName, object, TaskOptions>(async (name, request, taskOptions) =>
                            {
                                var loggerRequest = request as LoggerRequest;
                                await CallLoggerFunctionAsync(loggerRequest);
                            });

            _durableContext.Setup(x => x.CallActivityAsync(nameof(JobStatusUpdaterFunction), It.IsAny<JobStatusUpdaterRequest>(), It.IsAny<TaskOptions>()))
                            .Callback<TaskName, object, TaskOptions>(async (name, request, taskOptions) =>
                            {
                                var updateRequest = request as JobStatusUpdaterRequest;
                                await CallJobStatusUpdaterFunctionAsync(updateRequest);
                            });

            _durableContext.Setup(x => x.CallSubOrchestratorAsync<MembershipSubOrchestratorResponse>
                                                (
                                                    nameof(MembershipSubOrchestratorFunction),
                                                    It.IsAny<MembershipSubOrchestratorRequest>(),
                                                    It.IsAny<TaskOptions>())
                                                )
                            .ReturnsAsync(() => _membershipSubOrchestratorResponse);

            _durableContext.Setup(x => x.CallActivityAsync(nameof(TopicMessageSenderFunction), It.IsAny<MembershipHttpRequest>(), It.IsAny<TaskOptions>()))
                           .Callback<TaskName, object, TaskOptions>(async (name, request, taskOptions) =>
                           {
                               var membershipRequest = request as MembershipHttpRequest;
                               await CallTopicMessageSenderFunctionAsync(membershipRequest);
                           });

            
            _entityFeature.Setup(x => x.LockEntitiesAsync(It.IsAny<IEnumerable<EntityInstanceId>>())).ReturnsAsync(Mock.Of<IAsyncDisposable>());
            _entityFeature.Setup(x => x.CallEntityAsync<bool>(
                        It.IsAny<EntityInstanceId>(),
                        nameof(JobTrackerEntity.IsComplete),
                        It.IsAny<object>(),
                        It.IsAny<CallEntityOptions>()
                       )).ReturnsAsync(() => _hasSourceCompleted);

            _entityFeature.Setup(x => x.CallEntityAsync(
                        It.IsAny<EntityInstanceId>(),
                        nameof(JobTrackerEntity.SetDestinationPart),
                        It.IsAny<object>(),
                        It.IsAny<CallEntityOptions>()
                       ))
                    .Callback<EntityInstanceId, string, object, CallEntityOptions>((entityId, operationName, input, options) => 
                    {
                        _jobTrackerEntity.SetDestinationPart(input as string);
                    });

            _durableContext.Setup(x => x.Entities).Returns(() => _entityFeature.Object);
        }

        [TestMethod]
        public async Task HandleMissingGroupIdAsync()
        {
            var orchestratorFunction = new OrchestratorFunction(_configuration.Object, _loggingRepository.Object);
            _durableContext.Setup(x => x.CallActivityAsync<Guid>(nameof(GetGroupFunction), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()))
              .ReturnsAsync(Guid.Empty);
            await orchestratorFunction.RunOrchestratorAsync(_durableContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("Unable to get group id")), VerbosityLevel.DEBUG, It.IsAny<string>(), It.IsAny<string>()));
                        _syncJobStatusService.Verify(x => x.UpdateJobStatusAsync(
                                                                                                                                        It.IsAny<SyncJob>(),
                                                                                                                                        It.Is<SyncStatus?>(status => status == SyncStatus.Error),
                                                                                                                                        It.IsAny<SyncJobHistory>(),
                                                                                                                                        It.IsAny<string>()), Times.Once());
        }

        [TestMethod]
        public async Task TestJobWithSinglePartAsync()
        {
            var orchestratorFunction = new OrchestratorFunction(_configuration.Object, _loggingRepository.Object);
            await orchestratorFunction.RunOrchestratorAsync(_durableContext.Object);

            Assert.IsNull(_jobTrackerEntity.JobState.DestinationPart);
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("Sent message")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()));
            _syncJobStatusService.Verify(x => x.UpdateJobStatusAsync(It.IsAny<SyncJob>(), It.IsAny<SyncStatus?>(), It.IsAny<SyncJobHistory>(), It.IsAny<string>()), Times.Never());
        }

        [TestMethod]
        public async Task TestMissingPartAsync()
        {
            _hasSourceCompleted = false;
            _membershipAggregatorHttpRequest = new MembershipAggregatorHttpRequest
            {
                FilePath = "/file-path.json",
                SyncJob = _syncJob,
                PartNumber = 1,
                PartsCount = 2,
                IsDestinationPart = false
            };

            var orchestratorFunction = new OrchestratorFunction(_configuration.Object, _loggingRepository.Object);
            await orchestratorFunction.RunOrchestratorAsync(_durableContext.Object);

            Assert.IsNull(_jobTrackerEntity.JobState.DestinationPart);
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("Sent message")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            _syncJobStatusService.Verify(x => x.UpdateJobStatusAsync(It.IsAny<SyncJob>(), It.IsAny<SyncStatus?>(), It.IsAny<SyncJobHistory>(), It.IsAny<string>()), Times.Never());
        }

        [TestMethod]
        public async Task TestDestinationPartAsync()
        {            
            _membershipAggregatorHttpRequest = new MembershipAggregatorHttpRequest
            {
                FilePath = "/file-path.json",
                SyncJob = _syncJob,
                PartNumber = 1,
                PartsCount = 1,
                IsDestinationPart = true
            };

            var orchestratorFunction = new OrchestratorFunction(_configuration.Object, _loggingRepository.Object);
            await orchestratorFunction.RunOrchestratorAsync(_durableContext.Object);

            Assert.IsNotNull(_jobTrackerEntity.JobState.DestinationPart);
            Assert.AreEqual(_membershipAggregatorHttpRequest.FilePath, _jobTrackerEntity.JobState.DestinationPart);
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("Sent message")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()));
            _syncJobStatusService.Verify(x => x.UpdateJobStatusAsync(It.IsAny<SyncJob>(), It.IsAny<SyncStatus?>(), It.IsAny<SyncJobHistory>(), It.IsAny<string>()), Times.Never());
        }

        [TestMethod]
        public async Task TestNotSuccessMembershipDeltaStatusAsync()
        {
            _membershipSubOrchestratorResponse.MembershipDeltaStatus = MembershipDeltaStatus.Error;

            var orchestratorFunction = new OrchestratorFunction(_configuration.Object, _loggingRepository.Object);
            await orchestratorFunction.RunOrchestratorAsync(_durableContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message == "Calling GraphUpdater"), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("GraphUpdater response Code")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _syncJobStatusService.Verify(x => x.UpdateJobStatusAsync(It.IsAny<SyncJob>(), It.IsAny<SyncStatus?>(), It.IsAny<SyncJobHistory>(), It.IsAny<string>()), Times.Never());
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
                                                            nameof(JobStatusUpdaterFunction),
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
                                                  nameof(MembershipSubOrchestratorFunction),
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
                                                            nameof(JobStatusUpdaterFunction),
                                                            It.Is<JobStatusUpdaterRequest>(x => x.Status == SyncStatus.Error),
                                                            It.IsAny<TaskOptions>()
                                                           )
                                            , Times.Once());

        }

        [TestMethod]
    public async Task SendNewJobGoesToSmallOrLargeBasedOnCountAsync()
        {
            _syncJob.LastRunTime = System.Data.SqlTypes.SqlDateTime.MinValue.Value;

            _multilaneConfig = Options.Create(new MultiLaneConfig
            {
                IsEnabled = true,
                Small = SMALL
            });

            // When onboarding (no prior run), we still classify by size only.
            _membershipSubOrchestratorResponse.MembersToBeAdded = SMALL; // boundary small

            var laneSize = string.Empty;
            _onSendingMessage = message =>
            {
                laneSize = message.ApplicationProperties["LaneSize"].ToString();
            };

            var orchestratorFunction = new OrchestratorFunction(_configuration.Object, _loggingRepository.Object);
            await orchestratorFunction.RunOrchestratorAsync(_durableContext.Object);

            Assert.AreEqual("Small", laneSize);
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("Sent message to Small lane")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()));
            _syncJobStatusService.Verify(x => x.UpdateJobStatusAsync(It.IsAny<SyncJob>(), It.IsAny<SyncStatus?>(), It.IsAny<SyncJobHistory>(), It.IsAny<string>()), Times.Never());
        }

        [TestMethod]
        public async Task SendSmallJobToMessageSplitterTopicAsync()
        {
            _multilaneConfig = Options.Create(new MultiLaneConfig
            {
                IsEnabled = true,
                Small = SMALL
            });

            _membershipSubOrchestratorResponse.MembersToBeAdded = 20;

            _syncJob.LastRunTime = DateTime.UtcNow.AddDays(-1);

            var laneSize = string.Empty;
            _onSendingMessage = message =>
            {
                laneSize = message.ApplicationProperties["LaneSize"].ToString();
            };

            var orchestratorFunction = new OrchestratorFunction(_configuration.Object, _loggingRepository.Object);
            await orchestratorFunction.RunOrchestratorAsync(_durableContext.Object);

            Assert.AreEqual("Small", laneSize);
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("Sent message to Small lane")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()));
            _syncJobStatusService.Verify(x => x.UpdateJobStatusAsync(It.IsAny<SyncJob>(), It.IsAny<SyncStatus?>(), It.IsAny<SyncJobHistory>(), It.IsAny<string>()), Times.Never());
        }

        [TestMethod]
    public async Task SendLargeJobAtSmallBoundaryToMessageSplitterTopicAsync()
        {
            _multilaneConfig = Options.Create(new MultiLaneConfig
            {
                IsEnabled = true,
                Small = SMALL
            });

            // larger than Small threshold should be Large since only Small/Large are supported
            _membershipSubOrchestratorResponse.MembersToBeAdded = SMALL + 1;

            _syncJob.LastRunTime = DateTime.UtcNow.AddDays(-1);

            var laneSize = string.Empty;
            _onSendingMessage = message =>
            {
                laneSize = message.ApplicationProperties["LaneSize"].ToString();
            };

            var orchestratorFunction = new OrchestratorFunction(_configuration.Object, _loggingRepository.Object);
            await orchestratorFunction.RunOrchestratorAsync(_durableContext.Object);

            Assert.AreEqual("Large", laneSize);
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("Sent message to Large lane")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()));
            _syncJobStatusService.Verify(x => x.UpdateJobStatusAsync(It.IsAny<SyncJob>(), It.IsAny<SyncStatus?>(), It.IsAny<SyncJobHistory>(), It.IsAny<string>()), Times.Never());
        }

        [TestMethod]
    public async Task SendLargeJobToMessageSplitterTopicAsync()
        {
            _multilaneConfig = Options.Create(new MultiLaneConfig
            {
                IsEnabled = true,
                Small = SMALL
            });

            _membershipSubOrchestratorResponse.MembersToBeAdded = SMALL + 1;

            _syncJob.LastRunTime = DateTime.UtcNow.AddDays(-1);

            var laneSize = string.Empty;
            _onSendingMessage = message =>
            {
                laneSize = message.ApplicationProperties["LaneSize"].ToString();
            };

            var orchestratorFunction = new OrchestratorFunction(_configuration.Object, _loggingRepository.Object);
            await orchestratorFunction.RunOrchestratorAsync(_durableContext.Object);

            Assert.AreEqual("Large", laneSize);
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("Sent message to Large lane")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()));
            _syncJobStatusService.Verify(x => x.UpdateJobStatusAsync(It.IsAny<SyncJob>(), It.IsAny<SyncStatus?>(), It.IsAny<SyncJobHistory>(), It.IsAny<string>()), Times.Never());
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
            var jobStatusUpdaterFunction = new JobStatusUpdaterFunction(_loggingRepository.Object, _syncJobRepository.Object, _syncJobStatusService.Object);
            await jobStatusUpdaterFunction.UpdateJobStatusAsync(request);
        }

        private async Task CallTopicMessageSenderFunctionAsync(MembershipHttpRequest request)
        {
            
            var topicMessageSenderRepository = new TopicMessageSenderService(_loggingRepository.Object, _serviceBusTopicsRepository.Object, _messageSplitterSender, _multilaneConfig.Value);
            var topicMessageSenderFunction = new TopicMessageSenderFunction(_loggingRepository.Object, topicMessageSenderRepository);

            await topicMessageSenderFunction.SendMessageAsync(request);
        }
    }
}
