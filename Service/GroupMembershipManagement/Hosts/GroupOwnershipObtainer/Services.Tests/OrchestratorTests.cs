// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.GroupOwnershipObtainer;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.DurableTask;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.ServiceBus;
using Moq;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services;
using Services.Contracts;
using Services.Entities;
using Services.Tests.Helpers;

namespace Services.Tests
{
    [TestClass]
    public class OrchestratorTests
    {
        private Mock<IDryRunValue> _dryRunSettings = null!;
        private Mock<IConfiguration> _configuration = null!;
        private Mock<IDatabaseSyncJobsRepository> _syncJobRepository = null!;
        private Mock<IDatabaseGroupsRepository> _groupsRepository = null!;
        private Mock<IDatabaseChannelsRepository> _channelsRepository = null!;
        private Mock<IGraphGroupRepository> _graphGroupRepository = null!;
        private Mock<IBlobStorageRepository> _blobStorageRepository = null!;
        private Mock<IGroupOwnershipObtainerService> _groupOwnershipObtainerService = null!;
        private Mock<IServiceBusQueueRepository> _serviceBusQueueRepository = null!;
        private Mock<ISyncJobStatusService> _syncJobStatusService = null!;
        private Mock<TaskOrchestrationContext> _durableOrchestrationContext = null!;
        private Mock<IConfigurationRefresherProvider> _configurationRefresherProvider = null!;
        SchemaProvider _schemaProvider = null!;
        private bool _isValid = true;
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
            _syncJobRepository = new Mock<IDatabaseSyncJobsRepository>();
            _groupsRepository = new Mock<IDatabaseGroupsRepository>();
            _channelsRepository = new Mock<IDatabaseChannelsRepository>();
            _graphGroupRepository = new Mock<IGraphGroupRepository>();
            _blobStorageRepository = new Mock<IBlobStorageRepository>();
            _groupOwnershipObtainerService = new Mock<IGroupOwnershipObtainerService>();
            _serviceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            _syncJobStatusService = new Mock<ISyncJobStatusService>();
            _durableOrchestrationContext = new Mock<TaskOrchestrationContext>();
            _configurationRefresherProvider = new Mock<IConfigurationRefresherProvider>();

            var telemetryConfiguration = new TelemetryConfiguration();
            _telemetryClient = new TelemetryClient(telemetryConfiguration);

            _realGroupOwnershipObtainerService = new GroupOwnershipObtainerService(
                        _dryRunSettings.Object,
                        NullLogger<GroupOwnershipObtainerService>.Instance,
                        _syncJobRepository.Object,
                        _groupsRepository.Object,
                        _channelsRepository.Object,
                        _graphGroupRepository.Object,
                        _blobStorageRepository.Object);

            _groupOwnershipObtainerSyncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                Query = "[{\"type\":\"GroupOwnership\",\"source\":[\"GroupMembership\"]}]",
                Status = "InProgress",
                Period = 6,
                MembershipType = "GroupMembership",
                Group = new Group
                {
                    GroupId = Guid.NewGuid()
                }
            };

            _sampleSyncJobs = new List<SyncJob>
            {
                new SyncJob
                {
                    Id = Guid.NewGuid(),
                    Query = "[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-0000-0000-000000000000\"}]",
                    Status = "InProgress",
                    Period = 6,
                    MembershipType = "GroupMembership",
                    Group = new Group
                    {
                        GroupId = Guid.NewGuid()
                    }
                },
                new SyncJob
                {
                    Id = Guid.NewGuid(),
                    Query = "[{\"type\":\"CustomType1\",\"source\":\"00000000-0000-0000-0000-000000000001\"}]",
                    Status = "InProgress",
                    Period = 6,
                    MembershipType = "GroupMembership",
                    Group = new Group
                    {
                        GroupId = Guid.NewGuid()
                    }
                },
                new SyncJob
                {
                    Id = Guid.NewGuid(),
                    Query = "[{\"type\":\"CustomType2\",\"source\":\"00000000-0000-0000-0000-000000000002\"}]",
                    Status = "InProgress",
                    Period = 6,
                    MembershipType = "GroupMembership",
                    Group = new Group
                    {
                        GroupId = Guid.NewGuid()
                    }
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

            _durableOrchestrationContext.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>()))
                                        .Returns(NullLogger.Instance);

            _durableOrchestrationContext.Setup(x => x.GetInput<OrchestratorRequest>())
                                        .Returns(() => _orchestratorRequest);

            _durableOrchestrationContext.Setup(x => x.CurrentUtcDateTime)
                                        .Returns(() => DateTime.UtcNow);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<Guid>(It.Is<TaskName>(x => x == nameof(GetGroupFunction)), It.IsAny<GetGroupRequest>(), It.IsAny<TaskOptions>()))
                                        .ReturnsAsync(Guid.NewGuid());

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync(It.Is<TaskName>(x => x == nameof(JobStatusUpdaterFunction)), It.IsAny<JobStatusUpdaterRequest>(), It.IsAny<TaskOptions>()))
                                        .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                        {
                                            await CallJobStatusUpdaterFunctionAsync((JobStatusUpdaterRequest)request);
                                        });

            List<SyncJob> getJobsSegmentedResponse = new List<SyncJob>();
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<List<SyncJob>>(It.Is<TaskName>(x => x == nameof(GetJobsSegmentedFunction)), It.IsAny<GetJobsSegmentedRequest>(), It.IsAny<TaskOptions>()))
                                        .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                        {
                                            getJobsSegmentedResponse = await CallGetJobsSegmentedFunctionAsync((GetJobsSegmentedRequest)request);
                                        })
                                        .ReturnsAsync(() => getJobsSegmentedResponse);

            List<Guid> filteredGroupIds = new List<Guid>();
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<List<Guid>>(It.Is<TaskName>(x => x == nameof(JobsFilterFunction)), It.IsAny<JobsFilterRequest>(), It.IsAny<TaskOptions>()))
                                        .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                        {
                                            filteredGroupIds = await CallJobsFilterFunctionAsync((JobsFilterRequest)request);
                                        })
                                        .ReturnsAsync(() => filteredGroupIds);

            string filePath = string.Empty;
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<string>(It.Is<TaskName>(x => x == nameof(UsersSenderFunction)), It.IsAny<UsersSenderRequest>(), It.IsAny<TaskOptions>()))
                                        .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                        {
                                            filePath = await CallUsersSenderFunctionAsync((UsersSenderRequest)request);
                                        })
                                        .ReturnsAsync(() => filePath);

            List<Guid> ownerIdsResponse = new List<Guid>();
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<List<Guid>>(It.Is<TaskName>(x => x == nameof(GetGroupOwnersFunction)), It.IsAny<GetGroupOwnersRequest>(), It.IsAny<TaskOptions>()))
                            .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                            {
                                ownerIdsResponse = await CallGetGroupOwnersFunctionAsync((GetGroupOwnersRequest)request);
                            })
                            .ReturnsAsync(() => ownerIdsResponse);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync(It.Is<TaskName>(x => x == nameof(TelemetryTrackerFunction)), It.IsAny<TelemetryTrackerRequest>(), It.IsAny<TaskOptions>()))
                                        .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
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

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync(It.Is<TaskName>(x => x == nameof(QueueMessageSenderFunction)), It.IsAny<MembershipAggregatorHttpRequest>(), It.IsAny<TaskOptions>()))
                                        .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                        {
                                            await CallQueueMessageSenderFunctionAsync((MembershipAggregatorHttpRequest)request);
                                        });
            _schemaProvider = SchemaProviderFactory.CreateJsonSchemaProvider();

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<bool>(It.Is<TaskName>(x => x == nameof(SchemaValidatorFunction)), It.IsAny<SchemaValidatorRequest>(), It.IsAny<TaskOptions>()))
                    .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                    {
                        await CallSchemaValidatorFunctionAsync((SchemaValidatorRequest)request);
                    })
                    .ReturnsAsync(() => _isValid);
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

            // Verify orchestrator completed successfully by sending a queue message via QueueMessageSenderFunction
            _durableOrchestrationContext.Verify(x => x.CallActivityAsync(
                                    It.Is<TaskName>(n => n.Name == nameof(QueueMessageSenderFunction)),
                                    It.IsAny<MembershipAggregatorHttpRequest>(),
                                    It.IsAny<TaskOptions>()), Times.Once);
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

            _durableOrchestrationContext.Verify(x => x.CallActivityAsync(
                                    It.Is<TaskName>(n => n.Name == nameof(JobStatusUpdaterFunction)),
                                    It.Is<JobStatusUpdaterRequest>(r => r.Status == SyncStatus.MembershipDataNotFound),
                                    It.IsAny<TaskOptions>()), Times.Once);

            _syncJobStatusService.Verify(x => x.UpdateJobStatusAsync(
                        It.IsAny<SyncJob>(),
                        SyncStatus.MembershipDataNotFound,
                        It.IsAny<Models.SyncJobHistory.SyncJobHistory>(),
                        It.IsAny<string?>()), Times.Once);
        }

        [TestMethod]
        public async Task TestInvalidCurrentPartAsync()
        {
            _orchestratorRequest.CurrentPart = 0;

            var orchestratorFunction = new OrchestratorFunction(_configuration.Object);

            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object);

            _durableOrchestrationContext.Verify(x => x.CallActivityAsync(
                                    It.Is<TaskName>(n => n.Name == nameof(JobStatusUpdaterFunction)),
                                    It.Is<JobStatusUpdaterRequest>(r => r.Status == SyncStatus.Error),
                                    It.IsAny<TaskOptions>()), Times.Once);

            _syncJobStatusService.Verify(x => x.UpdateJobStatusAsync(
                                                It.IsAny<SyncJob>(),
                                                SyncStatus.Error,
                                                It.IsAny<Models.SyncJobHistory.SyncJobHistory>(),
                                                It.IsAny<string?>()
                                            ), Times.Once);

            _durableOrchestrationContext.Verify(x => x.CallActivityAsync(
                                    It.Is<TaskName>(n => n.Name == nameof(TelemetryTrackerFunction)),
                                    It.IsAny<TelemetryTrackerRequest>(),
                                    It.IsAny<TaskOptions>()), Times.Once);

            Assert.IsNotNull(_telemetryTrackerRequest);
            Assert.AreEqual(SyncStatus.Error, _telemetryTrackerRequest.JobStatus);
        }

        [TestMethod]
        public async Task TestEmptyQueryAsync()
        {
            _orchestratorRequest.SyncJob.Query = string.Empty;

            var orchestratorFunction = new OrchestratorFunction(_configuration.Object);

            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object);

            // Orchestrator should not send message to aggregator when query is invalid.
            _durableOrchestrationContext.Verify(x => x.CallActivityAsync(
                                    It.Is<TaskName>(n => n.Name == nameof(QueueMessageSenderFunction)),
                                    It.IsAny<MembershipAggregatorHttpRequest>(),
                                    It.IsAny<TaskOptions>()), Times.Never);

            _durableOrchestrationContext.Verify(x => x.CallActivityAsync(
                                    It.Is<TaskName>(n => n.Name == nameof(JobStatusUpdaterFunction)),
                                    It.Is<JobStatusUpdaterRequest>(r => r.Status == SyncStatus.QueryNotValid),
                                    It.IsAny<TaskOptions>()), Times.Once);

            _syncJobStatusService.Verify(x => x.UpdateJobStatusAsync(
                                    It.IsAny<SyncJob>(),
                                    SyncStatus.QueryNotValid,
                                    It.IsAny<Models.SyncJobHistory.SyncJobHistory>(),
                                    It.IsAny<string?>()), Times.Once);
        }

        [TestMethod]
        public async Task TestEmptySourcesInQueryAsync()
        {
            _orchestratorRequest.SyncJob.Query = "[{\"type\":\"GroupOwnership\",\"source\":[]}]";

            var orchestratorFunction = new OrchestratorFunction(_configuration.Object);

            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object);

            _durableOrchestrationContext.Verify(x => x.CallActivityAsync(
                                    It.Is<TaskName>(n => n.Name == nameof(QueueMessageSenderFunction)),
                                    It.IsAny<MembershipAggregatorHttpRequest>(),
                                    It.IsAny<TaskOptions>()), Times.Never);

            _durableOrchestrationContext.Verify(x => x.CallActivityAsync(
                                    It.Is<TaskName>(n => n.Name == nameof(JobStatusUpdaterFunction)),
                                    It.Is<JobStatusUpdaterRequest>(r => r.Status == SyncStatus.QueryNotValid),
                                    It.IsAny<TaskOptions>()), Times.Once);

            _syncJobStatusService.Verify(x => x.UpdateJobStatusAsync(
                                    It.IsAny<SyncJob>(),
                                    SyncStatus.QueryNotValid,
                                    It.IsAny<Models.SyncJobHistory.SyncJobHistory>(),
                                    It.IsAny<string?>()), Times.Once);
        }

        [TestMethod]
        public async Task TestGraphAPITimeoutExceptionAsync()
        {
            var exception = new Exception("The request timed out");

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<List<Guid>>(It.Is<TaskName>(x => x == nameof(GetGroupOwnersFunction)), It.IsAny<GetGroupOwnersRequest>(), It.IsAny<TaskOptions>()))
                                        .Throws(exception);

            List<Guid> filteredGroupIds = new List<Guid>();
            _groupOwnershipObtainerService.Setup(x => x.FilterSyncJobsBySourceTypes(It.IsAny<HashSet<string>>(), It.IsAny<List<JobsFilterSyncJob>>()))
                                   .Callback<HashSet<string>, List<JobsFilterSyncJob>>((requestedSourceTypes, syncJobs) =>
                                   {
                                       filteredGroupIds = _realGroupOwnershipObtainerService.FilterSyncJobsBySourceTypes(requestedSourceTypes, syncJobs);
                                   }).
                                   Returns(() => filteredGroupIds);

            var orchestratorFunction = new OrchestratorFunction(_configuration.Object);

            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object);

            var currentUtcDate = _durableOrchestrationContext.Object.CurrentUtcDateTime;
            _syncJobStatusService.Verify(x => x.UpdateJobStatusAsync(
                                                It.Is<SyncJob>(job => job.StartDate != default && Math.Abs((job.StartDate - currentUtcDate.AddMinutes(30)).TotalSeconds) < 1),
                                                SyncStatus.Idle,
                                                It.IsAny<Models.SyncJobHistory.SyncJobHistory>(),
                                                It.IsAny<string?>()), Times.Once);
        }

        [TestMethod]
        public async Task TestInvalidSchemaAsync()
        {
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<bool>(It.Is<TaskName>(x => x == nameof(SchemaValidatorFunction)), It.IsAny<SchemaValidatorRequest>(), It.IsAny<TaskOptions>()))
                    .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                    {
                        await CallSchemaValidatorFunctionAsync((SchemaValidatorRequest)request);
                    })
                    .ReturnsAsync(() => false);

            var orchestratorFunction = new OrchestratorFunction(_configuration.Object);

            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object);

            _syncJobStatusService.Verify(x => x.UpdateJobStatusAsync(
                                    It.IsAny<SyncJob>(),
                                    SyncStatus.SchemaError,
                                    It.IsAny<Models.SyncJobHistory.SyncJobHistory>(),
                                    It.IsAny<string?>()), Times.Once);
        }

        [TestMethod]
        public async Task TestMissingSchemasAsync()
        {
            _schemaProvider = new SchemaProvider();

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<bool>(It.Is<TaskName>(x => x == nameof(SchemaValidatorFunction)), It.IsAny<SchemaValidatorRequest>(), It.IsAny<TaskOptions>()))
                    .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                    {
                        await CallSchemaValidatorFunctionAsync((SchemaValidatorRequest)request);
                    })
                    .ReturnsAsync(() => _isValid);

            List<Guid> filteredGroupIds = new List<Guid>();
            _groupOwnershipObtainerService.Setup(x => x.FilterSyncJobsBySourceTypes(It.IsAny<HashSet<string>>(), It.IsAny<List<JobsFilterSyncJob>>()))
                                   .Callback<HashSet<string>, List<JobsFilterSyncJob>>((requestedSourceTypes, syncJobs) =>
                                   {
                                       filteredGroupIds = _realGroupOwnershipObtainerService.FilterSyncJobsBySourceTypes(requestedSourceTypes, syncJobs);
                                   }).
                                   Returns(() => filteredGroupIds);

            var orchestratorFunction = new OrchestratorFunction(_configuration.Object);

            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object);

            // With the empty SchemaProvider, validation short-circuits to true and the orchestrator
            // proceeds to produce a membership aggregator message.
            _durableOrchestrationContext.Verify(x => x.CallActivityAsync(
                                    It.Is<TaskName>(n => n.Name == nameof(QueueMessageSenderFunction)),
                                    It.IsAny<MembershipAggregatorHttpRequest>(),
                                    It.IsAny<TaskOptions>()), Times.Once);
        }

        private async Task CallJobStatusUpdaterFunctionAsync(JobStatusUpdaterRequest request)
        {
            var function = new JobStatusUpdaterFunction(
                NullLogger<JobStatusUpdaterFunction>.Instance,
                _syncJobStatusService.Object);
            await function.UpdateJobStatusAsync(request);
        }

        private async Task<List<SyncJob>> CallGetJobsSegmentedFunctionAsync(GetJobsSegmentedRequest request)
        {
            var function = new GetJobsSegmentedFunction(
                NullLogger<GetJobsSegmentedFunction>.Instance,
                _groupOwnershipObtainerService.Object);
            return await function.GetJobsAsync(request);
        }

        private async Task<List<Guid>> CallJobsFilterFunctionAsync(JobsFilterRequest request)
        {
            var function = new JobsFilterFunction(
                NullLogger<JobsFilterFunction>.Instance,
                _groupOwnershipObtainerService.Object);
            return await function.GetJobsAsync(request);
        }

        private async Task<string> CallUsersSenderFunctionAsync(UsersSenderRequest request)
        {
            var function = new UsersSenderFunction(
                NullLogger<UsersSenderFunction>.Instance,
                _groupOwnershipObtainerService.Object);
            return await function.SendUsersAsync(request);
        }

        private async Task<List<Guid>> CallGetGroupOwnersFunctionAsync(GetGroupOwnersRequest request)
        {
            var function = new GetGroupOwnersFunction(
                NullLogger<GetGroupOwnersFunction>.Instance,
                _groupOwnershipObtainerService.Object);
            return await function.GetGroupOwnersAsync(request);
        }

        private async Task CallTelemetryTrackerFunctionAsync(TelemetryTrackerRequest request)
        {
            var function = new TelemetryTrackerFunction(
                NullLogger<TelemetryTrackerFunction>.Instance,
                _telemetryClient);
            await function.TrackEventAsync(request);
        }

        private async Task CallQueueMessageSenderFunctionAsync(MembershipAggregatorHttpRequest request)
        {
            var function = new QueueMessageSenderFunction(
                NullLogger<QueueMessageSenderFunction>.Instance,
                _serviceBusQueueRepository.Object);
            await function.SendMessageAsync(request);
        }

        private async Task<bool> CallSchemaValidatorFunctionAsync(SchemaValidatorRequest request)
        {
            var function = new SchemaValidatorFunction(
                NullLogger<SchemaValidatorFunction>.Instance,
                _schemaProvider);
            return await function.ValidateSchemasAsync(request);
        }
    }
}
