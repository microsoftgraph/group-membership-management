// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Hosts.MembershipAggregator;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Repositories.Contracts;
using Services.Entities;
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
        private DurableHttpResponse _durableHttpResponse;
        private MembershipAggregatorHttpRequest _membershipAggregatorHttpRequest;
        private MembershipSubOrchestratorResponse _membershipSubOrchestratorResponse;
        private TelemetryClient _telemetryClient;

        private Mock<IConfiguration> _configuration;
        private Mock<JobTrackerEntity> _jobTrackerEntity;
        private Mock<ILoggingRepository> _loggingRepository;
        private Mock<ISyncJobRepository> _syncJobRepository;
        private Mock<IDurableOrchestrationContext> _durableContext;

        [TestInitialize]
        public void SetupTest()
        {
            _configuration = new Mock<IConfiguration>();
            _jobTrackerEntity = new Mock<JobTrackerEntity>();
            _loggingRepository = new Mock<ILoggingRepository>();
            _syncJobRepository = new Mock<ISyncJobRepository>();
            _durableContext = new Mock<IDurableOrchestrationContext>();
            _telemetryClient = new TelemetryClient(new TelemetryConfiguration());

            _syncJob = new SyncJob
            {
                PartitionKey = "00-00-0000",
                RowKey = Guid.NewGuid().ToString(),
                TargetOfficeGroupId = Guid.NewGuid(),
                ThresholdPercentageForAdditions = 80,
                ThresholdPercentageForRemovals = 20,
                LastRunTime = DateTime.UtcNow.AddDays(-1),
                Requestor = "user@domail.com",
                RunId = Guid.NewGuid(),
                ThresholdViolations = 0
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

            _durableHttpResponse = new DurableHttpResponse(System.Net.HttpStatusCode.NoContent);

            _configuration.Setup(x => x[It.Is<string>(x => x == "graphUpdaterUrl")])
                            .Returns("http://graph-updater-url");
            _configuration.Setup(x => x[It.Is<string>(x => x == "graphUpdaterFunctionKey")])
                            .Returns("112233445566");

            _syncJobRepository.Setup(x => x.GetSyncJobAsync(It.IsAny<string>(), It.IsAny<string>()))
                                .ReturnsAsync(() => _syncJob);

            _durableContext.Setup(x => x.GetInput<MembershipAggregatorHttpRequest>())
                            .Returns(() => _membershipAggregatorHttpRequest);

            _durableContext.Setup(x => x.CreateEntityProxy<IJobTracker>(It.IsAny<EntityId>()))
                            .Returns(() => _jobTrackerEntity.Object);

            _durableContext.Setup(x => x.CallHttpAsync(It.IsAny<DurableHttpRequest>()))
                            .ReturnsAsync(() => _durableHttpResponse);

            _durableContext.Setup(x => x.CallActivityAsync(It.Is<string>(x => x == nameof(TelemetryTrackerFunction)), It.IsAny<TelemetryTrackerRequest>()))
                    .Callback<string, object>(async (name, request) =>
                    {
                        var telemetryRequest = request as TelemetryTrackerRequest;
                        await CallTelemetryTrackerFunctionAsync(telemetryRequest);
                    });

            _durableContext.Setup(x => x.CallActivityAsync(It.Is<string>(x => x == nameof(LoggerFunction)), It.IsAny<LoggerRequest>()))
                            .Callback<string, object>(async (name, request) =>
                            {
                                var loggerRequest = request as LoggerRequest;
                                await CallLoggerFunctionAsync(loggerRequest);
                            });

            _durableContext.Setup(x => x.CallActivityAsync(It.Is<string>(x => x == nameof(JobStatusUpdaterFunction)), It.IsAny<JobStatusUpdaterRequest>()))
                            .Callback<string, object>(async (name, request) =>
                            {
                                var updateRequest = request as JobStatusUpdaterRequest;
                                await CallJobStatusUpdaterFunctionAsync(updateRequest);
                            });

            _durableContext.Setup(x => x.CallSubOrchestratorAsync<MembershipSubOrchestratorResponse>
                                                (
                                                    It.Is<string>(x => x == nameof(MembershipSubOrchestratorFunction)),
                                                    It.IsAny<MembershipSubOrchestratorRequest>())
                                                )
                            .ReturnsAsync(() => _membershipSubOrchestratorResponse);
        }

        [TestMethod]
        public async Task TestJobWithSinglePartAsync()
        {
            var orchestratorFunction = new OrchestratorFunction(_configuration.Object, _loggingRepository.Object);
            await orchestratorFunction.RunOrchestratorAsync(_durableContext.Object);

            Assert.IsNull(_jobTrackerEntity.Object.JobState.DestinationPart);
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message == "Calling GraphUpdater"), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()));
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("GraphUpdater response Code")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()));
            _syncJobRepository.Verify(x => x.UpdateSyncJobsAsync(It.IsAny<IEnumerable<SyncJob>>(), It.IsAny<SyncStatus>()), Times.Never());
            _jobTrackerEntity.Verify(x => x.Delete(), Times.Once());
        }

        [TestMethod]
        public async Task TestMissingPartAsync()
        {
            _membershipAggregatorHttpRequest.PartsCount = 2;

            var orchestratorFunction = new OrchestratorFunction(_configuration.Object, _loggingRepository.Object);
            await orchestratorFunction.RunOrchestratorAsync(_durableContext.Object);

            Assert.IsNull(_jobTrackerEntity.Object.JobState.DestinationPart);
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message == "Calling GraphUpdater"), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("GraphUpdater response Code")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            _syncJobRepository.Verify(x => x.UpdateSyncJobsAsync(It.IsAny<IEnumerable<SyncJob>>(), It.IsAny<SyncStatus>()), Times.Never());
            _jobTrackerEntity.Verify(x => x.Delete(), Times.Never());
        }

        [TestMethod]
        public async Task TestDestinationPartAsync()
        {
            _membershipAggregatorHttpRequest.IsDestinationPart = true;

            var orchestratorFunction = new OrchestratorFunction(_configuration.Object, _loggingRepository.Object);
            await orchestratorFunction.RunOrchestratorAsync(_durableContext.Object);

            Assert.IsNotNull(_jobTrackerEntity.Object.JobState.DestinationPart);
            Assert.AreEqual(_membershipAggregatorHttpRequest.FilePath, _jobTrackerEntity.Object.JobState.DestinationPart);
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message == "Calling GraphUpdater"), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()));
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("GraphUpdater response Code")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()));
            _syncJobRepository.Verify(x => x.UpdateSyncJobsAsync(It.IsAny<IEnumerable<SyncJob>>(), It.IsAny<SyncStatus>()), Times.Never());
            _jobTrackerEntity.Verify(x => x.Delete(), Times.Once());
        }

        [TestMethod]
        public async Task TestNotSuccessMembershipDeltaStatusAsync()
        {
            _membershipSubOrchestratorResponse.MembershipDeltaStatus = Entities.MembershipDeltaStatus.Error;

            var orchestratorFunction = new OrchestratorFunction(_configuration.Object, _loggingRepository.Object);
            await orchestratorFunction.RunOrchestratorAsync(_durableContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message == "Calling GraphUpdater"), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("GraphUpdater response Code")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _syncJobRepository.Verify(x => x.UpdateSyncJobsAsync(It.IsAny<IEnumerable<SyncJob>>(), It.IsAny<SyncStatus>()), Times.Never());
            _jobTrackerEntity.Verify(x => x.Delete(), Times.Once());
        }

        [TestMethod]
        public async Task HandleBadRequestResponseAsync()
        {
            _durableHttpResponse = new DurableHttpResponse(System.Net.HttpStatusCode.BadRequest);

            var orchestratorFunction = new OrchestratorFunction(_configuration.Object, _loggingRepository.Object);
            await orchestratorFunction.RunOrchestratorAsync(_durableContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message == "Calling GraphUpdater"), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()));
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("GraphUpdater response Code")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()));
            _syncJobRepository.Verify(x => x.UpdateSyncJobsAsync(It.IsAny<IEnumerable<SyncJob>>(), It.Is<SyncStatus>(s => s == SyncStatus.Error)), Times.Once());
            _jobTrackerEntity.Verify(x => x.Delete(), Times.Once());
        }

        [TestMethod]
        public async Task HandleFileNotFoundAsync()
        {
            _durableContext.Setup(x => x.CallSubOrchestratorAsync<MembershipSubOrchestratorResponse>
                                               (
                                                   It.Is<string>(x => x == nameof(MembershipSubOrchestratorFunction)),
                                                   It.IsAny<MembershipSubOrchestratorRequest>())
                                               )
                            .Throws<FileNotFoundException>();

            var orchestratorFunction = new OrchestratorFunction(_configuration.Object, _loggingRepository.Object);
            await Assert.ThrowsExceptionAsync<FileNotFoundException>(async () => await orchestratorFunction.RunOrchestratorAsync(_durableContext.Object));

            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message == "Calling GraphUpdater"), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("GraphUpdater response Code")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            _durableContext.Verify(x => x.CallActivityAsync(
                                                            It.Is<string>(x => x == nameof(JobStatusUpdaterFunction)),
                                                            It.Is<JobStatusUpdaterRequest>(x => x.Status == SyncStatus.FileNotFound)
                                                           )
                                            , Times.Once());

            _jobTrackerEntity.Verify(x => x.Delete(), Times.Once());
        }

        [TestMethod]
        public async Task HandleUnexpectedExceptionAsync()
        {
            _durableContext.Setup(x => x.CallSubOrchestratorAsync<MembershipSubOrchestratorResponse>
                                               (
                                                   It.Is<string>(x => x == nameof(MembershipSubOrchestratorFunction)),
                                                   It.IsAny<MembershipSubOrchestratorRequest>())
                                               )
                            .Throws<Exception>();

            var orchestratorFunction = new OrchestratorFunction(_configuration.Object, _loggingRepository.Object);
            await Assert.ThrowsExceptionAsync<Exception>(async () => await orchestratorFunction.RunOrchestratorAsync(_durableContext.Object));

            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message == "Calling GraphUpdater"), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("GraphUpdater response Code")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("Unexpected exception")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Once());
            _durableContext.Verify(x => x.CallActivityAsync(
                                                            It.Is<string>(x => x == nameof(JobStatusUpdaterFunction)),
                                                            It.Is<JobStatusUpdaterRequest>(x => x.Status == SyncStatus.Error)
                                                           )
                                            , Times.Once());

            _jobTrackerEntity.Verify(x => x.Delete(), Times.Once());
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
    }
}
