// Licensed under the MIT license.
using Hosts.GroupMembershipObtainer;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.DurableTask;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Notifications;
using Models.ServiceBus;
using Moq;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Tests.Helpers;

namespace Tests.Services
{
    [TestClass]
    public class OrchestratorTests
    {
        private Mock<IDryRunValue> _dryRunValue;
        private Mock<IConfiguration> _configuration;
        private Mock<ILoggingRepository> _loggingRepository;
        private Mock<IDatabaseSyncJobsRepository> _syncJobRepository;
        private Mock<IDatabaseGroupsRepository> _groupsRepository;
        private Mock<IDatabaseChannelsRepository> _channelsRepository;
        private Mock<IGraphGroupRepository> _graphGroupRepository;
        private Mock<IEmailSenderRecipient> _emailSenderRecipient;
        private Mock<IBlobStorageRepository> _blobStorageRepository;
        private Mock<IServiceBusQueueRepository> _serviceBusQueueRepository;
        private Mock<TaskOrchestrationContext> _durableOrchestrationContext;
        private Mock<IConfigurationRefresherProvider> _configurationRefresherProvider;
        private Mock<IDatabaseDestinationAttributesRepository> _databaseDestinationAttributesRepository;
        private Mock<Microsoft.Azure.WebJobs.ExecutionContext> _executionContext;
        private int _usersToReturn;
        private QuerySample _querySample;
        private OrchestratorRequest _orchestratorRequest;
        private SyncStatus _subOrchestratorResponseStatus;
        private SGMembershipCalculator _membershipCalculator;
        private TelemetryClient _telemetryClient;
        SchemaProvider _schemaProvider;
        private bool _isValid = true;
        private GroupMembershipFileResult _membershipFileResult;

        [TestInitialize]
        public void Setup()
        {
            _dryRunValue = new Mock<IDryRunValue>();
            _configuration = new Mock<IConfiguration>();
            _loggingRepository = new Mock<ILoggingRepository>();
            _syncJobRepository = new Mock<IDatabaseSyncJobsRepository>();
            _groupsRepository = new Mock<IDatabaseGroupsRepository>();
            _channelsRepository = new Mock<IDatabaseChannelsRepository>();
            _graphGroupRepository = new Mock<IGraphGroupRepository>();
            _emailSenderRecipient = new Mock<IEmailSenderRecipient>();
            _blobStorageRepository = new Mock<IBlobStorageRepository>();
            _durableOrchestrationContext = new Mock<TaskOrchestrationContext>();
            _configurationRefresherProvider = new Mock<IConfigurationRefresherProvider>();
            _executionContext = new Mock<Microsoft.Azure.WebJobs.ExecutionContext>();
            _telemetryClient = new TelemetryClient(new TelemetryConfiguration());
            _serviceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            _databaseDestinationAttributesRepository = new Mock<IDatabaseDestinationAttributesRepository>();
            _usersToReturn = 10;
            _querySample = QuerySample.GenerateQuerySample("GroupMembership");

            var syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                Query = _querySample.GetQuery(),
                Status = "InProgress",
                Period = 6
            };

            var group = new Group
            {
                SyncJobId = syncJob.Id,
                GroupId = Guid.NewGuid()
            };

            _orchestratorRequest = new OrchestratorRequest
            {
                CurrentPart = 1,
                TotalParts = _querySample.QueryParts.Count + 1,
                SyncJob = syncJob,
                IsDestinationPart = false
            };

            _membershipCalculator = new SGMembershipCalculator(
                                    _graphGroupRepository.Object,
                                    _blobStorageRepository.Object,
                                    _syncJobRepository.Object,
                                    _groupsRepository.Object,
                                    _channelsRepository.Object,
                                    _serviceBusQueueRepository.Object,
                                    _databaseDestinationAttributesRepository.Object,
                                    _loggingRepository.Object,
                                    _dryRunValue.Object
                                    );

            var configurationRefresher = new Mock<IConfigurationRefresher>();
            configurationRefresher.Setup(x => x.TryRefreshAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

            _configurationRefresherProvider.Setup(x => x.Refreshers)
                                    .Returns(() => new List<IConfigurationRefresher> { configurationRefresher.Object });

            _durableOrchestrationContext.Setup(x => x.GetInput<OrchestratorRequest>())
                                .Returns(() => _orchestratorRequest);

            _durableOrchestrationContext.Setup(x => x.CurrentUtcDateTime).Returns(DateTime.UtcNow);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<Guid>(nameof(GetGroupFunction), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>())).ReturnsAsync(group.GroupId);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync(It.IsAny<TaskName>(), It.IsAny<JobStatusUpdaterRequest>(), It.IsAny<TaskOptions>()))
                                .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                {
                                    await CallJobStatusUpdaterFunctionAsync(request as JobStatusUpdaterRequest);
                                });

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<string>(nameof(DestinationNameReaderFunction), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()))
                                .ReturnsAsync("ExpectedDestinationName");

            GroupReaderResponse groupReaderResponse = null;

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync(It.Is<TaskName>(x => x == nameof(TelemetryTrackerFunction)), It.IsAny<TelemetryTrackerRequest>(), It.IsAny<TaskOptions>()))
            .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
            {
                var telemetryRequest = request as TelemetryTrackerRequest;
                await CallTelemetryTrackerFunctionAsync(telemetryRequest);
            });

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<GroupReaderResponse>(It.IsAny<TaskName>(), It.IsAny<GroupReaderRequest>(), It.IsAny<TaskOptions>()))
                                .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                {
                                    groupReaderResponse = await CallSourceGroupsReaderFunctionAsync(request as GroupReaderRequest);
                                }).
                                ReturnsAsync(() => groupReaderResponse);

            _subOrchestratorResponseStatus = SyncStatus.InProgress;
            _durableOrchestrationContext.Setup(x => x.CallSubOrchestratorAsync<SubOrchestratorResponse>(It.IsAny<TaskName>(), It.IsAny<GroupMembershipRequest>(), It.IsAny<TaskOptions>()))
                                .ReturnsAsync(() =>
                                {
                                    return new SubOrchestratorResponse
                                    {
                                        Status = _subOrchestratorResponseStatus
                                    };

                                });

            string _filePath = null;
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<string>(It.IsAny<TaskName>(), It.IsAny<UsersSenderRequest>(), It.IsAny<TaskOptions>()))
                                .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                {
                                    _filePath = await CallUsersSenderFunctionAsync(request as UsersSenderRequest);
                                })
                                .ReturnsAsync(() => _filePath);

            _blobStorageRepository.Setup(x => x.ReadBlobsAsync(It.IsAny<string>())).ReturnsAsync(() =>
            {
                var users = new List<AzureADUser>();
                for (var i = 0; i < _usersToReturn; i++)
                {
                    users.Add(new AzureADUser { ObjectId = Guid.NewGuid() });
                }

                return users;
            });

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<GroupMembershipFileResult>(It.IsAny<TaskName>(), It.IsAny<TransitiveAndDeltaUsersSenderRequest>(), It.IsAny<TaskOptions>()))
                                .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                {
                                    _membershipFileResult = await CallTransitiveAndDeltaUsersSenderFunctionAsync(request as TransitiveAndDeltaUsersSenderRequest);
                                })
                                .ReturnsAsync(() => new GroupMembershipFileResult { FilePath = _filePath, MemberCount = _usersToReturn });

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<string>(It.IsAny<TaskName>(), It.IsAny<DeleteBlobRequest>(), It.IsAny<TaskOptions>()))
                                .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                {
                                    await CallDeleteBlobFunctionAsync(request as DeleteBlobRequest);
                                });

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<string>(It.IsAny<TaskName>(), It.IsAny<DeltaLinkUploaderRequest>(), It.IsAny<TaskOptions>()))
                               .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                               {
                                   await CallDeltaLinkUploaderFunctionAsync(request as DeltaLinkUploaderRequest);
                               });

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<string>(It.IsAny<TaskName>(), It.IsAny<CacheUploaderRequest>(), It.IsAny<TaskOptions>()))
                                .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                {
                                    await CallCacheUploaderFunctionAsync(request as CacheUploaderRequest);
                                });

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync(It.IsAny<TaskName>(), It.IsAny<EmailSenderRequest>(), It.IsAny<TaskOptions>()))
                                .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                {
                                    await CallEmailSenderFunctionAsync(request as EmailSenderRequest);
                                });

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync(nameof(QueueMessageSenderFunction), It.IsAny<MembershipAggregatorHttpRequest>(), It.IsAny<TaskOptions>()))
                                .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                {
                                    await CallQueueMessageSenderFunctionAsync(request as MembershipAggregatorHttpRequest);
                                });
            _schemaProvider = SchemaProviderFactory.CreateJsonSchemaProvider();

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<bool>(nameof(SchemaValidatorFunction), It.IsAny<SchemaValidatorRequest>(), It.IsAny<TaskOptions>()))
            .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
            {
                await CallSchemaValidatorFunctionAsync(request as SchemaValidatorRequest);
            })
            .ReturnsAsync(() => _isValid);

        }

        [TestMethod]
        public async Task TestInvalidCurrentPartAsync()
        {
            _orchestratorRequest.CurrentPart = 0;

            var orchestratorFunction = new OrchestratorFunction(
                                            _loggingRepository.Object,
                                            _membershipCalculator,
                                            _configuration.Object,
                                            _emailSenderRecipient.Object
                                            );

            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                                It.Is<LogMessage>(m => m.Message.Contains("Found invalid value for CurrentPart or TotalParts")),
                                                It.IsAny<VerbosityLevel>(),
                                                It.IsAny<string>(),
                                                It.IsAny<string>()
                                            ), Times.Once);

            _syncJobRepository.Verify(x => x.UpdateSyncJobStatusAsync(
                                                It.IsAny<IEnumerable<SyncJob>>(),
                                                It.Is<SyncStatus>(s => s == SyncStatus.Error)
                                            ), Times.Once);
        }

        [TestMethod]
        public async Task TestInvalidQuerySourceAsync()
        {
            var invalidSource = "[{ \"type\": \"GroupMembership\", \"source\": \"0fab28de-4d33-4bb7-17e46cf75200\"}]";

            _orchestratorRequest.SyncJob.Query = invalidSource;

            var orchestratorFunction = new OrchestratorFunction(
                                           _loggingRepository.Object,
                                           _membershipCalculator,
                                           _configuration.Object,
                                            _emailSenderRecipient.Object
                                           );

            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                                It.Is<LogMessage>(m => m.Message.Contains($"Marking job as {SyncStatus.QueryNotValid}")),
                                                It.IsAny<VerbosityLevel>(),
                                                It.IsAny<string>(),
                                                It.IsAny<string>()
                                            ), Times.Once);

            _syncJobRepository.Verify(x => x.UpdateSyncJobStatusAsync(
                                                It.IsAny<IEnumerable<SyncJob>>(),
                                                It.Is<SyncStatus>(s => s == SyncStatus.QueryNotValid)
                                            ), Times.Once);

            _serviceBusQueueRepository.Verify(x => x.SendMessageAsync(It.Is<ServiceBusMessage>(msg =>
                msg.ApplicationProperties.ContainsKey("MessageType") &&
                msg.ApplicationProperties["MessageType"].ToString() == NotificationMessageType.NotValidSourceNotification.ToString())),
                Times.Exactly(1));
        }

        [TestMethod]
        public async Task TestEmptySourceGroupsAsync()
        {
            _querySample.QueryParts.ForEach(x => x.SourceId = Guid.Empty);
            _orchestratorRequest.SyncJob.Query = _querySample.GetQuery();

            var orchestratorFunction = new OrchestratorFunction(
                                           _loggingRepository.Object,
                                           _membershipCalculator,
                                           _configuration.Object,
                                            _emailSenderRecipient.Object
                                           );

            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                                It.Is<LogMessage>(m => m.Message.Contains($"Marking job as {SyncStatus.QueryNotValid}")),
                                                It.IsAny<VerbosityLevel>(),
                                                It.IsAny<string>(),
                                                It.IsAny<string>()
                                            ), Times.Once);

            _syncJobRepository.Verify(x => x.UpdateSyncJobStatusAsync(
                                                It.IsAny<IEnumerable<SyncJob>>(),
                                                It.Is<SyncStatus>(s => s == SyncStatus.QueryNotValid)
                                            ), Times.Once);

            _serviceBusQueueRepository.Verify(x => x.SendMessageAsync(It.Is<ServiceBusMessage>(msg =>
                msg.ApplicationProperties.ContainsKey("MessageType") &&
                msg.ApplicationProperties["MessageType"].ToString() == NotificationMessageType.NotValidSourceNotification.ToString())),
                Times.Exactly(1));
        }
        [TestMethod]
        public async Task TestGetGroupsNameAsync()
        {

            _querySample.QueryParts.ForEach(x => x.SourceId = Guid.Empty);
            _orchestratorRequest.SyncJob.Query = _querySample.GetQuery();
            var orchestratorFunction = new OrchestratorFunction(
                               _loggingRepository.Object,
                               _membershipCalculator,
                               _configuration.Object,
                                _emailSenderRecipient.Object
                               );

            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object);
            _durableOrchestrationContext.Verify(x => x.CallActivityAsync<string>(nameof(DestinationNameReaderFunction), _orchestratorRequest.SyncJob, It.IsAny<TaskOptions>()), Times.Once());

        }

        [TestMethod]
        public async Task TestGroupMembershipNotFoundAsync()
        {
            _subOrchestratorResponseStatus = SyncStatus.SecurityGroupNotFound;

            var orchestratorFunction = new OrchestratorFunction(
                                            _loggingRepository.Object,
                                            _membershipCalculator,
                                            _configuration.Object,
                                            _emailSenderRecipient.Object
                                            );

            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object);

            _syncJobRepository.Verify(x => x.UpdateSyncJobStatusAsync(
                                                It.IsAny<IEnumerable<SyncJob>>(),
                                                It.Is<SyncStatus>(s => s == SyncStatus.SecurityGroupNotFound)
                                            ), Times.Once);
        }

        [TestMethod]
        public async Task TestUnhandledExceptionAsync()
        {
            _durableOrchestrationContext.Setup(x => x.CallSubOrchestratorAsync<SubOrchestratorResponse>(It.IsAny<TaskName>(), It.IsAny<GroupMembershipRequest>(), It.IsAny<TaskOptions>()))
                                .Throws<Exception>();

            var orchestratorFunction = new OrchestratorFunction(
                                    _loggingRepository.Object,
                                    _membershipCalculator,
                                    _configuration.Object,
                                    _emailSenderRecipient.Object
                                    );

            await Assert.ThrowsExceptionAsync<Exception>(async () => await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object));

            _loggingRepository.Verify(x => x.LogMessageAsync(
                It.Is<LogMessage>(m => m.Message.StartsWith("Caught unexpected exception")),
                It.IsAny<VerbosityLevel>(),
                It.IsAny<string>(),
                It.IsAny<string>()
            ), Times.Once);

            _syncJobRepository.Verify(x => x.UpdateSyncJobStatusAsync(
                                        It.IsAny<IEnumerable<SyncJob>>(),
                                        It.Is<SyncStatus>(s => s == SyncStatus.Error)
                                    ), Times.Once);
        }

        [TestMethod]
        public async Task TestGraphAPITimeoutExceptionAsync()
        {
            var exception = new Exception("The request timed out");

            _durableOrchestrationContext.Setup(x => x.CallSubOrchestratorAsync<SubOrchestratorResponse>(It.IsAny<TaskName>(), It.IsAny<GroupMembershipRequest>(), It.IsAny<TaskOptions>()))
                                .Throws(exception);

            var orchestratorFunction = new OrchestratorFunction(
                                    _loggingRepository.Object,
                                    _membershipCalculator,
                                    _configuration.Object,
                                    _emailSenderRecipient.Object
                                    );

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
                                        It.Is<SyncStatus>(s => s == SyncStatus.Idle)
                                    ), Times.Once);
        }

        [TestMethod]
        public async Task TestValidPartRequestAsync()
        {
            _durableOrchestrationContext.Setup(x => x.CallSubOrchestratorAsync<SubOrchestratorResponse>(It.IsAny<TaskName>(), It.IsAny<GroupMembershipRequest>(), It.IsAny<TaskOptions>()))
                               .ReturnsAsync(() =>
                               {
                                   return new SubOrchestratorResponse
                                   {
                                       Status = _subOrchestratorResponseStatus
                                   };

                               });

            var orchestratorFunction = new OrchestratorFunction(
                                    _loggingRepository.Object,
                                    _membershipCalculator,
                                    _configuration.Object,
                                    _emailSenderRecipient.Object
                                    );

            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                It.Is<LogMessage>(m => m.Message == $"{nameof(OrchestratorFunction)} function completed"),
                It.IsAny<VerbosityLevel>(),
                It.IsAny<string>(),
                It.IsAny<string>()
            ), Times.Once);

            _syncJobRepository.Verify(x => x.UpdateSyncJobStatusAsync(
                                        It.IsAny<IEnumerable<SyncJob>>(),
                                        It.IsAny<SyncStatus>()
                                    ), Times.Never);
        }

        [TestMethod]
        public async Task TestTransitiveCallAsync()
        {
            _durableOrchestrationContext.Setup(x => x.CallSubOrchestratorAsync<SubOrchestratorResponse>(It.IsAny<TaskName>(), It.IsAny<GroupMembershipRequest>(), It.IsAny<TaskOptions>()))
                               .ReturnsAsync(() =>
                               {
                                   return new SubOrchestratorResponse
                                   {
                                       Status = _subOrchestratorResponseStatus
                                   };
                               });

            var orchestratorFunction = new OrchestratorFunction(
                                    _loggingRepository.Object,
                                    _membershipCalculator,
                                    _configuration.Object,
                                    _emailSenderRecipient.Object
                                    );

            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object);
            _durableOrchestrationContext.Verify(x => x.CallActivityAsync<string>(nameof(UsersSenderFunction), It.IsAny<UsersSenderRequest>(), It.IsAny<TaskOptions>()), Times.Never);
            _loggingRepository.Verify(x => x.LogMessageAsync(
                It.Is<LogMessage>(m => m.Message == $"{nameof(OrchestratorFunction)} function completed"),
                It.IsAny<VerbosityLevel>(),
                It.IsAny<string>(),
                It.IsAny<string>()
            ), Times.Once);
        }

        [TestMethod]
        public async Task TestDeltaCallAsync()
        {

            _durableOrchestrationContext.Setup(x => x.CallSubOrchestratorAsync<SubOrchestratorResponse>(It.IsAny<TaskName>(), It.IsAny<GroupMembershipRequest>(), It.IsAny<TaskOptions>()))
                               .ReturnsAsync(() =>
                               {
                                   return new SubOrchestratorResponse
                                   {

                                       Status = _subOrchestratorResponseStatus
                                   };

                               });

            var orchestratorFunction = new OrchestratorFunction(
                                    _loggingRepository.Object,
                                    _membershipCalculator,
                                    _configuration.Object,
                                    _emailSenderRecipient.Object
                                    );

            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object);
            _durableOrchestrationContext.Verify(x => x.CallActivityAsync<string>(nameof(UsersSenderFunction), It.IsAny<UsersSenderRequest>(), It.IsAny<TaskOptions>()), Times.Never);
            _loggingRepository.Verify(x => x.LogMessageAsync(
                                        It.Is<LogMessage>(m => m.Message == $"{nameof(OrchestratorFunction)} function completed"),
                                        It.IsAny<VerbosityLevel>(),
                                        It.IsAny<string>(),
                                        It.IsAny<string>()
                                    ), Times.Once);
        }

        [TestMethod]
        public async Task TestValidPartForTransitiveRequestAsync()
        {
            _durableOrchestrationContext.Setup(x => x.CallSubOrchestratorAsync<SubOrchestratorResponse>(It.IsAny<TaskName>(), It.IsAny<GroupMembershipRequest>(), It.IsAny<TaskOptions>()))
                               .ReturnsAsync(() =>
                               {
                                   return new SubOrchestratorResponse
                                   {
                                       Status = _subOrchestratorResponseStatus
                                   };
                               });

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync(It.IsAny<TaskName>(), It.IsAny<DeleteBlobRequest>(), It.IsAny<TaskOptions>()))
                        .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                        {
                            await CallDeleteBlobFunctionAsync(request as DeleteBlobRequest);
                        });

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync(It.IsAny<TaskName>(), It.IsAny<TransitiveAndDeltaUsersSenderRequest>(), It.IsAny<TaskOptions>()))
                               .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                               {
                                   await CallTransitiveAndDeltaUsersSenderFunctionAsync(request as TransitiveAndDeltaUsersSenderRequest);
                               });

            var orchestratorFunction = new OrchestratorFunction(_loggingRepository.Object, _membershipCalculator, _configuration.Object, _emailSenderRecipient.Object);
            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object);

            _durableOrchestrationContext.Verify(x => x.CallSubOrchestratorAsync<SubOrchestratorResponse>(nameof(SubOrchestratorFunction), It.IsAny<GroupMembershipRequest>(), It.IsAny<TaskOptions>()), Times.Once);
            _durableOrchestrationContext.Verify(x => x.CallActivityAsync(nameof(QueueMessageSenderFunction), It.IsAny<MembershipAggregatorHttpRequest>(), It.IsAny<TaskOptions>()), Times.Once);

            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message == $"{nameof(OrchestratorFunction)} function completed"), It.IsAny<VerbosityLevel>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        [TestMethod]
        public async Task TestValidPartForDeltaRequestAsync()
        {
            _durableOrchestrationContext.Setup(x => x.CallSubOrchestratorAsync<SubOrchestratorResponse>(It.IsAny<TaskName>(), It.IsAny<GroupMembershipRequest>(), It.IsAny<TaskOptions>()))
                               .ReturnsAsync(() =>
                               {
                                   return new SubOrchestratorResponse
                                   {
                                       Status = _subOrchestratorResponseStatus
                                   };
                               });

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync(It.IsAny<TaskName>(), It.IsAny<TransitiveAndDeltaUsersSenderRequest>(), It.IsAny<TaskOptions>()))
                    .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                    {
                        await CallTransitiveAndDeltaUsersSenderFunctionAsync(request as TransitiveAndDeltaUsersSenderRequest);
                    });

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<bool>(nameof(DeleteBlobFunction), It.IsAny<DeleteBlobRequest>(), It.IsAny<TaskOptions>()))
                   .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                   {
                       await CallDeleteBlobFunctionAsync(request as DeleteBlobRequest);
                   });

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<bool>(nameof(CacheUploaderFunction), It.IsAny<CacheUploaderRequest>(), It.IsAny<TaskOptions>()))
                   .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                   {
                       await CallCacheUploaderFunctionAsync(request as CacheUploaderRequest);
                   });

            var orchestratorFunction = new OrchestratorFunction(_loggingRepository.Object, _membershipCalculator, _configuration.Object, _emailSenderRecipient.Object);
            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message == $"{nameof(OrchestratorFunction)} function completed"), It.IsAny<VerbosityLevel>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        [TestMethod]
        public async Task TestMissingSchemasAsync()
        {
            _schemaProvider = new SchemaProvider();

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<bool>(nameof(SchemaValidatorFunction), It.IsAny<SchemaValidatorRequest>(), It.IsAny<TaskOptions>()))
            .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
            {
                await CallSchemaValidatorFunctionAsync(request as SchemaValidatorRequest);
            })
            .ReturnsAsync(() => _isValid);

            var orchestratorFunction = new OrchestratorFunction(
                                            _loggingRepository.Object,
                                            _membershipCalculator,
                                            _configuration.Object,
                                            _emailSenderRecipient.Object
                                            );

            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                                It.Is<LogMessage>(m => m.Message == $"No json schemas have been loaded. Skipping schema validation."),
                                                It.IsAny<VerbosityLevel>(),
                                                It.IsAny<string>(),
                                                It.IsAny<string>()
                                            ), Times.Once);
        }

        [TestMethod]
        public async Task TestMissingGroupMembershipSchemaAsync()
        {
            _schemaProvider = SchemaProviderFactory.CreateMissingGroupMembershipSchemaProvider();

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<bool>(nameof(SchemaValidatorFunction), It.IsAny<SchemaValidatorRequest>(), It.IsAny<TaskOptions>()))
            .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
            {
                await CallSchemaValidatorFunctionAsync(request as SchemaValidatorRequest);
            })
            .ReturnsAsync(() => _isValid);

            var orchestratorFunction = new OrchestratorFunction(
                                            _loggingRepository.Object,
                                            _membershipCalculator,
                                            _configuration.Object,
                                            _emailSenderRecipient.Object
                                            );

            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                                It.Is<LogMessage>(m => m.Message == $"No GroupMembership schema has been loaded. Skipping schema validation."),
                                                It.IsAny<VerbosityLevel>(),
                                                It.IsAny<string>(),
                                                It.IsAny<string>()
                                            ), Times.Once);
        }

        [TestMethod]
        public async Task TestInvalidSchemaAsync()
        {
            var orchestratorFunction = new OrchestratorFunction(
                                            _loggingRepository.Object,
                                            _membershipCalculator,
                                            _configuration.Object,
                                            _emailSenderRecipient.Object
                                            );

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<bool>(nameof(SchemaValidatorFunction), It.IsAny<SchemaValidatorRequest>(), It.IsAny<TaskOptions>()))
           .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
           {
               await CallSchemaValidatorFunctionAsync(request as SchemaValidatorRequest);
           })
           .ReturnsAsync(() => false);

            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object);
            _durableOrchestrationContext.Verify(x => x.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                            It.Is<JobStatusUpdaterRequest>(x => x.Status == SyncStatus.SchemaError), It.IsAny<TaskOptions>()), Times.Once());
        }

        [TestMethod]
        public async Task TestJsonReaderExceptionAsync()
        {
            _schemaProvider = SchemaProviderFactory.CreateMissingGroupMembershipSchemaProvider();

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<bool>(nameof(SchemaValidatorFunction), It.IsAny<SchemaValidatorRequest>(), It.IsAny<TaskOptions>()))
                    .ThrowsAsync(new JsonException());

            var orchestratorFunction = new OrchestratorFunction(
                                            _loggingRepository.Object,
                                            _membershipCalculator,
                                            _configuration.Object,
                                            _emailSenderRecipient.Object
                                            );

            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                                It.Is<LogMessage>(m => m.Message.Contains("Source query is not valid for job")),
                                                It.IsAny<VerbosityLevel>(),
                                                It.IsAny<string>(),
                                                It.IsAny<string>()
                                            ), Times.Once);

            _durableOrchestrationContext.Verify(x => x.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                           It.Is<JobStatusUpdaterRequest>(x => x.Status == SyncStatus.QueryNotValid), It.IsAny<TaskOptions>()), Times.Once());
        }

        private async Task CallTelemetryTrackerFunctionAsync(TelemetryTrackerRequest request)
        {
            var telemetryTrackerFunction = new TelemetryTrackerFunction(_loggingRepository.Object, _telemetryClient);
            await telemetryTrackerFunction.TrackEventAsync(request);
        }

        private async Task CallJobStatusUpdaterFunctionAsync(JobStatusUpdaterRequest request)
        {
            var function = new JobStatusUpdaterFunction(_loggingRepository.Object, _membershipCalculator);
            await function.UpdateJobStatusAsync(request);
        }

        private async Task<GroupReaderResponse> CallSourceGroupsReaderFunctionAsync(GroupReaderRequest request)
        {
            var function = new GroupReaderFunction(_loggingRepository.Object, _membershipCalculator);
            var response = function.GetSourceGroup(request);
            AzureADGroup azureAdGroup = request.IsDestinationPart
                                ? azureAdGroup = new AzureADGroup { ObjectId = request.GroupId }
                                : azureAdGroup = response.SourceGroup;

            response.SourceGroup = azureAdGroup;
            return response;
        }

        private async Task<string> CallUsersSenderFunctionAsync(UsersSenderRequest request)
        {
            var function = new UsersSenderFunction(_loggingRepository.Object, _membershipCalculator);
            return await function.SendUsersAsync(request);
        }

        private async Task<GroupMembershipFileResult> CallTransitiveAndDeltaUsersSenderFunctionAsync(TransitiveAndDeltaUsersSenderRequest request)
        {
            var function = new TransitiveAndDeltaUsersSenderFunction(_loggingRepository.Object, _blobStorageRepository.Object, _membershipCalculator);
            return await function.SendUsersAsync(request);
        }

        private async Task CallCacheUploaderFunctionAsync(CacheUploaderRequest request)
        {
            var function = new CacheUploaderFunction(_loggingRepository.Object, _membershipCalculator);
            await function.SendUsersAsync(request);
        }

        private async Task CallDeltaLinkUploaderFunctionAsync(DeltaLinkUploaderRequest request)
        {
            var function = new DeltaLinkUploaderFunction(_loggingRepository.Object, _membershipCalculator);
            await function.SendDeltaLinkAsync(request);
        }

        private async Task CallDeleteBlobFunctionAsync(DeleteBlobRequest request)
        {
            var function = new DeleteBlobFunction(_loggingRepository.Object, _blobStorageRepository.Object);
            await function.DeleteAsync(request);
        }

        private async Task CallEmailSenderFunctionAsync(EmailSenderRequest request)
        {
            var function = new EmailSenderFunction(_loggingRepository.Object, _membershipCalculator);
            await function.SendEmailAsync(request);
        }

        private async Task CallQueueMessageSenderFunctionAsync(MembershipAggregatorHttpRequest request)
        {
            var function = new QueueMessageSenderFunction(_loggingRepository.Object, _serviceBusQueueRepository.Object);
            await function.SendMessageAsync(request);
        }

        private async Task<bool> CallSchemaValidatorFunctionAsync(SchemaValidatorRequest request)
        {
            var function = new SchemaValidatorFunction(_loggingRepository.Object, _schemaProvider);
            return await function.ValidateSchemasAsync(request);
        }
    }
}
