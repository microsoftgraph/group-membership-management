// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.GroupOwnershipObtainer;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Graph;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.ServiceBus;
using Moq;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using Services.Entities;

namespace Services.Tests
{
    [TestClass]
    public class OrchestratorTests
    {
        private Mock<IDryRunValue> _dryRunSettings = null!;
        private Mock<IConfiguration> _configuration = null!;
        private Mock<ILoggingRepository> _loggingRepository = null!;
        private Mock<IDatabaseSyncJobsRepository> _syncJobRepository = null!;
        private Mock<IGraphGroupRepository> _graphGroupRepository = null!;
        private Mock<IBlobStorageRepository> _blobStorageRepository = null!;
        private Mock<IGroupOwnershipObtainerService> _groupOwnershipObtainerService = null!;
        private Mock<IServiceBusQueueRepository> _serviceBusQueueRepository = null!;
        private Mock<IDurableOrchestrationContext> _durableOrchestrationContext = null!;
        private Mock<IConfigurationRefresherProvider> _configurationRefresherProvider = null!;

        private List<SyncJob> _sampleSyncJobs = null!;
        private SyncJob _groupOwnershipObtainerSyncJob = null!;
        private TelemetryClient _telemetryClient = null!;
        private TelemetryTrackerRequest? _telemetryTrackerRequest = null;
        private OrchestratorRequest _orchestratorRequest = null!;
        private GroupOwnershipObtainerService _realGroupOwnershipObtainerService = null!;
        [TestInitialize]
        public void Setup()
        {
            _dryRunSettings = new Mock<IDryRunValue>();
            _configuration = new Mock<IConfiguration>();
            _loggingRepository = new Mock<ILoggingRepository>();
            _syncJobRepository = new Mock<IDatabaseSyncJobsRepository>();
            _graphGroupRepository = new Mock<IGraphGroupRepository>();
            _blobStorageRepository = new Mock<IBlobStorageRepository>();
            _groupOwnershipObtainerService = new Mock<IGroupOwnershipObtainerService>();
            _serviceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            _durableOrchestrationContext = new Mock<IDurableOrchestrationContext>();
            _configurationRefresherProvider = new Mock<IConfigurationRefresherProvider>();

            var telemetryConfiguration = new TelemetryConfiguration();
            _telemetryClient = new TelemetryClient(telemetryConfiguration);

            _realGroupOwnershipObtainerService = new GroupOwnershipObtainerService(
                        _dryRunSettings.Object,
                        _loggingRepository.Object,
                        _syncJobRepository.Object,
                        _graphGroupRepository.Object,
                        _blobStorageRepository.Object);

            _groupOwnershipObtainerSyncJob = new SyncJob
            {
                RowKey = Guid.NewGuid().ToString(),
                PartitionKey = "00-00-0000",
                TargetOfficeGroupId = Guid.NewGuid(),
                Query = "[{\"type\":\"GroupOwnership\",\"source\":[\"GroupMembership\"]}]",
                Status = "InProgress",
                Period = 6
            };

            _sampleSyncJobs = new List<SyncJob>
            {
                new SyncJob
                {
                    Id = Guid.NewGuid(),
                    TargetOfficeGroupId = Guid.NewGuid(),
                    Query = "[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-0000-0000-000000000000\"}]",
                    Status = "InProgress",
                    Period = 6
                },
                new SyncJob
                {
                    Id = Guid.NewGuid(),
                    TargetOfficeGroupId = Guid.NewGuid(),
                    Query = "[{\"type\":\"CustomType1\",\"source\":\"00000000-0000-0000-0000-000000000001\"}]",
                    Status = "InProgress",
                    Period = 6
                },
                new SyncJob
                {
                    Id = Guid.NewGuid(),
                    TargetOfficeGroupId = Guid.NewGuid(),
                    Query = "[{\"type\":\"CustomType2\",\"source\":\"00000000-0000-0000-0000-000000000002\"}]",
                    Status = "InProgress",
                    Period = 6
                }
            };

            _orchestratorRequest = new OrchestratorRequest
            {
                CurrentPart = 1,
                TotalParts = 1,
                SyncJob = _groupOwnershipObtainerSyncJob
            };

            List<Guid> ownerIds = new List<Guid>();
            for (int i = 0; i < 10; i++)
            {
                ownerIds.Add(Guid.NewGuid());
            }

            _durableOrchestrationContext.Setup(x => x.GetInput<OrchestratorRequest>())
                                        .Returns(() => _orchestratorRequest);

            _durableOrchestrationContext.Setup(x => x.CurrentUtcDateTime)
                                        .Returns(DateTime.UtcNow);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync(nameof(LoggerFunction), It.IsAny<LoggerRequest>()))
                                        .Callback<string, object>(async (name, request) =>
                                        {
                                            await CallLoggerFunctionAsync((LoggerRequest)request);
                                        });

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync(nameof(JobStatusUpdaterFunction), It.IsAny<JobStatusUpdaterRequest>()))
                                        .Callback<string, object>(async (name, request) =>
                                        {
                                            await CallJobStatusUpdaterFunctionAsync((JobStatusUpdaterRequest)request);
                                        });

            List<SyncJob> getJobsSegmentedResponse = new List<SyncJob>();
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<List<SyncJob>>(nameof(GetJobsSegmentedFunction), It.IsAny<GetJobsSegmentedRequest>()))
                                        .Callback<string, object>(async (name, request) =>
                                        {
                                            getJobsSegmentedResponse = await CallGetJobsSegmentedFunctionAsync((GetJobsSegmentedRequest)request);
                                        })
                                        .ReturnsAsync(() => getJobsSegmentedResponse);

            List<Guid> filteredGroupIds = new List<Guid>();
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<List<Guid>>(nameof(JobsFilterFunction), It.IsAny<JobsFilterRequest>()))
                                        .Callback<string, object>(async (name, request) =>
                                        {
                                            filteredGroupIds = await CallJobsFilterFunctionAsync((JobsFilterRequest)request);
                                        })
                                        .ReturnsAsync(() => filteredGroupIds);

            string filePath = string.Empty;
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<string>(nameof(UsersSenderFunction), It.IsAny<UsersSenderRequest>()))
                                        .Callback<string, object>(async (name, request) =>
                                        {
                                            filePath = await CallUsersSenderFunctionAsync((UsersSenderRequest)request);
                                        })
                                        .ReturnsAsync(() => filePath);

            List<Guid> ownerIdsResponse = new List<Guid>();
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<List<Guid>>(nameof(GetGroupOwnersFunction), It.IsAny<GetGroupOwnersRequest>()))
                            .Callback<string, object>(async (name, request) =>
                            {
                                ownerIdsResponse = await CallGetGroupOwnersFunctionAsync((GetGroupOwnersRequest)request);
                            })
                            .ReturnsAsync(() => ownerIdsResponse);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync(nameof(TelemetryTrackerFunction), It.IsAny<TelemetryTrackerRequest>()))
                                        .Callback<string, object>(async (name, request) =>
                                        {
                                            _telemetryTrackerRequest = (TelemetryTrackerRequest)request;
                                            await CallTelemetryTrackerFunctionAsync(_telemetryTrackerRequest);
                                        });

            _groupOwnershipObtainerService.Setup(x => x.GetSyncJobsSegmentAsync())
                                   .ReturnsAsync(() => _sampleSyncJobs);

            _groupOwnershipObtainerService.Setup(x => x.GetGroupOwnersAsync(It.IsAny<Guid>()))
                                   .ReturnsAsync(() => ownerIds);

            var configurationRefresher = new Mock<IConfigurationRefresher>();
            configurationRefresher.Setup(x => x.TryRefreshAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

            _configurationRefresherProvider.Setup(x => x.Refreshers)
                                            .Returns(() => new List<IConfigurationRefresher> { configurationRefresher.Object });

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync(nameof(QueueMessageSenderFunction), It.IsAny<MembershipAggregatorHttpRequest>()))
                                        .Callback<string, object>(async (name, request) =>
                                        {
                                            await CallQueueMessageSenderFunctionAsync((MembershipAggregatorHttpRequest)request);
                                        });
        }

        [TestMethod]
        public async Task TestFindRequestedTypesAsync()
        {
            List<Guid> filteredGroupIds = new List<Guid>();
            _groupOwnershipObtainerService.Setup(x => x.FilterSyncJobsBySourceTypes(It.IsAny<HashSet<string>>(), It.IsAny<List<JobsFilterSyncJob>>()))
                                   .Callback<HashSet<string>, List<JobsFilterSyncJob>>((requestedSourceTypes, syncJobs) =>
                                   {
                                       filteredGroupIds = _realGroupOwnershipObtainerService.FilterSyncJobsBySourceTypes(requestedSourceTypes, syncJobs);
                                   }).
                                   Returns(() => filteredGroupIds);

            var orchestratorFunction = new OrchestratorFunction(_configuration.Object);
            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object);

            _serviceBusQueueRepository.Verify(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>()), Times.Once);
            
            _loggingRepository.Verify(x => x.LogMessageAsync(
                    It.Is<LogMessage>(m => m.Message.StartsWith($"{nameof(OrchestratorFunction)} function completed")),
                    It.IsAny<VerbosityLevel>(),
                    It.IsAny<string>(),
                    It.IsAny<string>()), Times.Once());
        }

        [TestMethod]
        public async Task TestDoesNotFindRequestedTypesAsync()
        {
            _orchestratorRequest.SyncJob.Query = "[{\"type\":\"GroupOwnership\",\"source\":[\"CustomTypeX\"]}]";

            List<Guid> filteredGroupIds = new List<Guid>();
            _groupOwnershipObtainerService.Setup(x => x.FilterSyncJobsBySourceTypes(It.IsAny<HashSet<string>>(), It.IsAny<List<JobsFilterSyncJob>>()))
                                   .Callback<HashSet<string>, List<JobsFilterSyncJob>>((requestedSourceTypes, syncJobs) =>
                                   {
                                       filteredGroupIds = _realGroupOwnershipObtainerService.FilterSyncJobsBySourceTypes(requestedSourceTypes, syncJobs);
                                   }).
                                   Returns(() => filteredGroupIds);

            var orchestratorFunction = new OrchestratorFunction(_configuration.Object);
            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object);


            _loggingRepository.Verify(x => x.LogMessageAsync(
                                    It.Is<LogMessage>(m => m.Message.StartsWith($"There are no jobs matching the requested sources")),
                                    It.IsAny<VerbosityLevel>(),
                                    It.IsAny<string>(),
                                    It.IsAny<string>()), Times.Once);

            _syncJobRepository.Verify(x => x.UpdateSyncJobStatusAsync(
                        It.IsAny<IEnumerable<SyncJob>>(),
                        SyncStatus.MembershipDataNotFound), Times.Once);

        }

        [TestMethod]
        public async Task TestInvalidCurrentPartAsync()
        {
            _orchestratorRequest.CurrentPart = 0;

            var orchestratorFunction = new OrchestratorFunction(_configuration.Object);

            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                                It.Is<LogMessage>(m => m.Message.Contains("Found invalid value for CurrentPart or TotalParts")),
                                                It.IsAny<VerbosityLevel>(),
                                                It.IsAny<string>(),
                                                It.IsAny<string>()
                                            ), Times.Once);

            _syncJobRepository.Verify(x => x.UpdateSyncJobStatusAsync(
                                                It.IsAny<IEnumerable<SyncJob>>(),
                                                SyncStatus.Error
                                            ), Times.Once);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                                It.Is<LogMessage>(m => m.Message == $"{nameof(TelemetryTrackerFunction)} function completed"),
                                                It.IsAny<VerbosityLevel>(),
                                                It.IsAny<string>(),
                                                It.IsAny<string>()
                                            ), Times.Once);

            Assert.IsNotNull(_telemetryTrackerRequest);
            Assert.AreEqual(SyncStatus.Error, _telemetryTrackerRequest.JobStatus);
        }

        [TestMethod]
        public async Task TestEmptyQueryAsync()
        {
            _orchestratorRequest.SyncJob.Query = string.Empty;

            var orchestratorFunction = new OrchestratorFunction(_configuration.Object);

            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                               It.Is<LogMessage>(m => m.Message.Contains($"The job RowKey:{_groupOwnershipObtainerSyncJob.RowKey} Part#{_orchestratorRequest.CurrentPart} does not have a valid query")),
                               It.IsAny<VerbosityLevel>(),
                               It.IsAny<string>(),
                               It.IsAny<string>()), Times.Once());

            _loggingRepository.Verify(x => x.LogMessageAsync(
                               It.Is<LogMessage>(m => m.Message.StartsWith($"Calling MembershipAggregator")),
                               It.IsAny<VerbosityLevel>(),
                               It.IsAny<string>(),
                               It.IsAny<string>()), Times.Never());

            _loggingRepository.Verify(x => x.LogMessageAsync(
                               It.Is<LogMessage>(m => m.Message == $"{nameof(OrchestratorFunction)} function completed"),
                               It.IsAny<VerbosityLevel>(),
                               It.IsAny<string>(),
                               It.IsAny<string>()), Times.Once());

            _syncJobRepository.Verify(x => x.UpdateSyncJobStatusAsync(
                                    It.IsAny<IEnumerable<SyncJob>>(),
                                    SyncStatus.QueryNotValid), Times.Once);
        }

        [TestMethod]
        public async Task TestEmptySourcesInQueryAsync()
        {
            _orchestratorRequest.SyncJob.Query = "[{\"type\":\"GroupOwnership\",\"source\":[]}]";

            var orchestratorFunction = new OrchestratorFunction(_configuration.Object);

            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                               It.Is<LogMessage>(m => m.Message.Contains($"The job RowKey:{_groupOwnershipObtainerSyncJob.RowKey} Part#{_orchestratorRequest.CurrentPart} does not have a valid query")),
                               It.IsAny<VerbosityLevel>(),
                               It.IsAny<string>(),
                               It.IsAny<string>()), Times.Once());

            _loggingRepository.Verify(x => x.LogMessageAsync(
                               It.Is<LogMessage>(m => m.Message.StartsWith($"Calling MembershipAggregator")),
                               It.IsAny<VerbosityLevel>(),
                               It.IsAny<string>(),
                               It.IsAny<string>()), Times.Never());

            _syncJobRepository.Verify(x => x.UpdateSyncJobStatusAsync(
                                    It.IsAny<IEnumerable<SyncJob>>(),
                                    SyncStatus.QueryNotValid), Times.Once);

        }

        [TestMethod]
        public async Task TestGraphAPITimeoutExceptionAsync()
        {
            var exception = new Exception("The request timed out");

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<List<Guid>>(nameof(GetGroupOwnersFunction), It.IsAny<GetGroupOwnersRequest>()))
                                        .Throws(exception);


            var realService = new GroupOwnershipObtainerService(
                                    _dryRunSettings.Object,
                                    _loggingRepository.Object,
                                    _syncJobRepository.Object,
                                    _graphGroupRepository.Object,
                                    _blobStorageRepository.Object);

            List<Guid> filteredGroupIds = new List<Guid>();
            _groupOwnershipObtainerService.Setup(x => x.FilterSyncJobsBySourceTypes(It.IsAny<HashSet<string>>(), It.IsAny<List<JobsFilterSyncJob>>()))
                                   .Callback<HashSet<string>, List<JobsFilterSyncJob>>((requestedSourceTypes, syncJobs) =>
                                   {
                                       filteredGroupIds = realService.FilterSyncJobsBySourceTypes(requestedSourceTypes, syncJobs);
                                   }).
                                   Returns(() => filteredGroupIds);

            var orchestratorFunction = new OrchestratorFunction(_configuration.Object);

            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                        It.Is<LogMessage>(m => m.Message.StartsWith("Rescheduling job at")),
                        It.IsAny<VerbosityLevel>(),
                        It.IsAny<string>(),
                        It.IsAny<string>()
                    ), Times.Once);

            var currentUtcDate = _durableOrchestrationContext.Object.CurrentUtcDateTime;
            _syncJobRepository.Verify(x => x.UpdateSyncJobStatusAsync(
                                                It.Is<IEnumerable<SyncJob>>(x => x.All(y => y.StartDate == currentUtcDate.AddMinutes(30))),
                                                It.Is<SyncStatus>(s => s == SyncStatus.Idle)), Times.Once);
        }

        private async Task CallLoggerFunctionAsync(LoggerRequest request)
        {
            var function = new LoggerFunction(_loggingRepository.Object);
            await function.LogMessageAsync(request);
        }

        private async Task CallJobStatusUpdaterFunctionAsync(JobStatusUpdaterRequest request)
        {
            var function = new JobStatusUpdaterFunction(_loggingRepository.Object, _syncJobRepository.Object);
            await function.UpdateJobStatusAsync(request);
        }

        private async Task<List<SyncJob>> CallGetJobsSegmentedFunctionAsync(GetJobsSegmentedRequest request)
        {
            var function = new GetJobsSegmentedFunction(_loggingRepository.Object, _groupOwnershipObtainerService.Object);
            return await function.GetJobsAsync(request);
        }

        private async Task<List<Guid>> CallJobsFilterFunctionAsync(JobsFilterRequest request)
        {
            var function = new JobsFilterFunction(_loggingRepository.Object, _groupOwnershipObtainerService.Object);
            return await function.GetJobsAsync(request);
        }

        private async Task<string> CallUsersSenderFunctionAsync(UsersSenderRequest request)
        {
            var function = new UsersSenderFunction(_loggingRepository.Object, _groupOwnershipObtainerService.Object);
            return await function.SendUsersAsync(request);
        }

        private async Task<List<Guid>> CallGetGroupOwnersFunctionAsync(GetGroupOwnersRequest request)
        {
            var function = new GetGroupOwnersFunction(_loggingRepository.Object, _groupOwnershipObtainerService.Object);
            return await function.GetGroupOwnersAsync(request);
        }

        private async Task CallTelemetryTrackerFunctionAsync(TelemetryTrackerRequest request)
        {
            var function = new TelemetryTrackerFunction(_loggingRepository.Object, _telemetryClient);
            await function.TrackEventAsync(request);
        }

        private async Task CallQueueMessageSenderFunctionAsync(MembershipAggregatorHttpRequest request)
        {
            var function = new QueueMessageSenderFunction(_loggingRepository.Object, _serviceBusQueueRepository.Object);
            await function.SendMessageAsync(request);
        }
    }
}
