// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.GroupMembershipObtainer;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Helpers;
using Models.Notifications;
using Models.ServiceBus;
using Moq;
using Newtonsoft.Json;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.Services
{
    [TestClass]
    public class OrchestratorTests
    {
        private Mock<IDryRunValue> _dryRunValue;
        private Mock<IConfiguration> _configuration;
        private Mock<IMailRepository> _mailRepository;
        private Mock<ILoggingRepository> _loggingRepository;
        private Mock<IDatabaseSyncJobsRepository> _syncJobRepository;
        private Mock<IGraphGroupRepository> _graphGroupRepository;
        private Mock<IEmailSenderRecipient> _emailSenderRecipient;
        private Mock<IBlobStorageRepository> _blobStorageRepository;
        private Mock<IServiceBusQueueRepository> _serviceBusQueueRepository;
        private Mock<IDurableOrchestrationContext> _durableOrchestrationContext;
        private Mock<IConfigurationRefresherProvider> _configurationRefresherProvider;
        private Mock<IDatabaseDestinationAttributesRepository> _databaseDestinationAttributesRepository;
        private Mock<ITeamsChannelRepository> _teamsChannelRepository;
        private Mock<Microsoft.Azure.WebJobs.ExecutionContext> _executionContext;
        private int _usersToReturn;
        private QuerySample _querySample;
        private OrchestratorRequest _orchestratorRequest;
        private SyncStatus _subOrchestratorResponseStatus;
        private SGMembershipCalculator _membershipCalculator;
        private DurableHttpResponse _membershipAgregatorResponse;
        private TelemetryClient _telemetryClient;

        [TestInitialize]
        public void Setup()
        {
            _dryRunValue = new Mock<IDryRunValue>();
            _configuration = new Mock<IConfiguration>();
            _mailRepository = new Mock<IMailRepository>();
            _loggingRepository = new Mock<ILoggingRepository>();
            _syncJobRepository = new Mock<IDatabaseSyncJobsRepository>();
            _graphGroupRepository = new Mock<IGraphGroupRepository>();
            _emailSenderRecipient = new Mock<IEmailSenderRecipient>();
            _blobStorageRepository = new Mock<IBlobStorageRepository>();
            _durableOrchestrationContext = new Mock<IDurableOrchestrationContext>();
            _configurationRefresherProvider = new Mock<IConfigurationRefresherProvider>();
            _executionContext = new Mock<Microsoft.Azure.WebJobs.ExecutionContext>();
            _telemetryClient = new TelemetryClient(new TelemetryConfiguration());
            _serviceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            _databaseDestinationAttributesRepository = new Mock<IDatabaseDestinationAttributesRepository>();
            _teamsChannelRepository = new Mock<ITeamsChannelRepository>();
            _usersToReturn = 10;
            _querySample = QuerySample.GenerateQuerySample("GroupMembership");

            var syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                TargetOfficeGroupId = Guid.NewGuid(),
                Query = _querySample.GetQuery(),
                Status = "InProgress",
                Period = 6
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

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync(It.IsAny<string>(), It.IsAny<JobStatusUpdaterRequest>()))
                                        .Callback<string, object>(async (name, request) =>
                                        {
                                            await CallJobStatusUpdaterFunctionAsync(request as JobStatusUpdaterRequest);
                                        });

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<string>(nameof(DestinationNameReaderFunction), It.IsAny<SyncJob>()))
                                        .ReturnsAsync("ExpectedDestinationName");

            AzureADGroup sourceGroup = null;
            string id = null;

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync(It.Is<string>(x => x == nameof(TelemetryTrackerFunction)), It.IsAny<TelemetryTrackerRequest>()))
                    .Callback<string, object>(async (name, request) =>
                    {
                        var telemetryRequest = request as TelemetryTrackerRequest;
                        await CallTelemetryTrackerFunctionAsync(telemetryRequest);
                    });

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<(AzureADGroup, string)>(It.IsAny<string>(), It.IsAny<GroupReaderRequest>()))
                                        .Callback<string, object>(async (name, request) =>
                                        {
                                            (sourceGroup, id) = await CallSourceGroupsReaderFunctionAsync(request as GroupReaderRequest);
                                        }).
                                        ReturnsAsync(() => (sourceGroup, id));

            _subOrchestratorResponseStatus = SyncStatus.InProgress;
            _durableOrchestrationContext.Setup(x => x.CallSubOrchestratorAsync<string>(It.IsAny<string>(), It.IsAny<GroupMembershipRequest>()))
                                        .ReturnsAsync(() =>
                                        {
                                            var users = new List<AzureADUser>();
                                            for (var i = 0; i < _usersToReturn; i++)
                                            {
                                                users.Add(new AzureADUser { ObjectId = Guid.NewGuid() });
                                            }

                                            return TextCompressor.Compress(JsonConvert.SerializeObject(new SubOrchestratorResponse
                                            {
                                                Users = users,
                                                Status = _subOrchestratorResponseStatus
                                            }));

                                        });

            string _filePath = null;
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<string>(It.IsAny<string>(), It.IsAny<UsersSenderRequest>()))
                                        .Callback<string, object>(async (name, request) =>
                                        {
                                            _filePath = await CallUsersSenderFunctionAsync(request as UsersSenderRequest);
                                        })
                                        .ReturnsAsync(() => _filePath);

            _membershipAgregatorResponse = new DurableHttpResponse(System.Net.HttpStatusCode.NoContent);
            _durableOrchestrationContext.Setup(x => x.CallHttpAsync(It.IsAny<DurableHttpRequest>())).ReturnsAsync(() => _membershipAgregatorResponse);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync(It.IsAny<string>(), It.IsAny<EmailSenderRequest>()))
                                        .Callback<string, object>(async (name, request) =>
                                        {
                                            await CallEmailSenderFunctionAsync(request as EmailSenderRequest);
                                        });

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync(nameof(QueueMessageSenderFunction), It.IsAny<MembershipAggregatorHttpRequest>()))
                                        .Callback<string, object>(async (name, request) =>
                                        {
                                            await CallQueueMessageSenderFunctionAsync(request as MembershipAggregatorHttpRequest);
                                        });

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

            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object, _executionContext.Object);

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
            var invalidSource  = "[{ \"type\": \"GroupMembership\", \"source\": \"0fab28de-4d33-4bb7-17e46cf75200\"}]";

            _orchestratorRequest.SyncJob.Query = invalidSource;

            var orchestratorFunction = new OrchestratorFunction(
                                           _loggingRepository.Object,
                                           _membershipCalculator,
                                           _configuration.Object,
                                            _emailSenderRecipient.Object
                                           );

            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object, _executionContext.Object);

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

            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object, _executionContext.Object);

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

            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object, _executionContext.Object);
            _durableOrchestrationContext.Verify(x => x.CallActivityAsync<string>(nameof(DestinationNameReaderFunction), _orchestratorRequest.SyncJob), Times.Once());

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

            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object, _executionContext.Object);

            _syncJobRepository.Verify(x => x.UpdateSyncJobStatusAsync(
                                                It.IsAny<IEnumerable<SyncJob>>(),
                                                It.Is<SyncStatus>(s => s == SyncStatus.SecurityGroupNotFound)
                                            ), Times.Once);
        }

        [TestMethod]
        public async Task TestUnhandledExceptionAsync()
        {
            _durableOrchestrationContext.Setup(x => x.CallSubOrchestratorAsync<string>(It.IsAny<string>(), It.IsAny<GroupMembershipRequest>()))
                                        .Throws<Exception>();

            var orchestratorFunction = new OrchestratorFunction(
                                            _loggingRepository.Object,
                                            _membershipCalculator,
                                            _configuration.Object,
                                            _emailSenderRecipient.Object
                                            );

            await Assert.ThrowsExceptionAsync<Exception>(async () => await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object, _executionContext.Object));

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

            _durableOrchestrationContext.Setup(x => x.CallSubOrchestratorAsync<string>(It.IsAny<string>(), It.IsAny<GroupMembershipRequest>()))
                                        .Throws(exception);

            var orchestratorFunction = new OrchestratorFunction(
                                            _loggingRepository.Object,
                                            _membershipCalculator,
                                            _configuration.Object,
                                            _emailSenderRecipient.Object
                                            );

            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object, _executionContext.Object);

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
            _usersToReturn = 100000;

            var orchestratorFunction = new OrchestratorFunction(
                                            _loggingRepository.Object,
                                            _membershipCalculator,
                                            _configuration.Object,
                                            _emailSenderRecipient.Object
                                            );

            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object, _executionContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                                It.Is<LogMessage>(m => m.Message.Contains($"Read {_usersToReturn} users from source groups")),
                                                It.IsAny<VerbosityLevel>(),
                                                It.IsAny<string>(),
                                                It.IsAny<string>()
                                            ), Times.Once);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                    It.Is<LogMessage>(m => m.Message.Contains($"Successfully uploaded {_usersToReturn} users")),
                                    It.IsAny<VerbosityLevel>(),
                                    It.IsAny<string>(),
                                    It.IsAny<string>()
                                ), Times.Once);

            _blobStorageRepository.Verify(x => x.UploadFileAsync(
                                                It.IsAny<string>(),
                                                It.IsAny<string>(),
                                                It.IsAny<Dictionary<string, string>>()
                                            ), Times.Once);

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

        private async Task<(AzureADGroup,string)> CallSourceGroupsReaderFunctionAsync(GroupReaderRequest request)
        {
            var function = new GroupReaderFunction(_loggingRepository.Object, _membershipCalculator);
            var (group, groupId) = function.GetSourceGroup(request);
            AzureADGroup azureAdGroup = request.IsDestinationPart
                                ? azureAdGroup = new AzureADGroup { ObjectId = request.SyncJob.TargetOfficeGroupId }
                                : azureAdGroup = group;
            return (azureAdGroup, groupId);
        }

        private async Task<string> CallUsersSenderFunctionAsync(UsersSenderRequest request)
        {
            var function = new UsersSenderFunction(_loggingRepository.Object, _membershipCalculator);
            return await function.SendUsersAsync(request);
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

    }
}
