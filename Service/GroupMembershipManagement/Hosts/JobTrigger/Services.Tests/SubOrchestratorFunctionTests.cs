// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using Hosts.JobTrigger;
using JobTrigger.Activity.EmailSender;
using JobTrigger.Activity.SchemaValidator;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.ServiceBusTopics;
using Services.Contracts;
using Services.Tests.Helpers;
using Repositories.ServiceBusQueue;
using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Models.Notifications;
using Models.Helpers;
using System.Text.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Services.Tests
{
    [TestClass]
    public class SubOrchestratorFunctionTests
    {
        Mock<IJobTriggerService> _jobTriggerService;
        Mock<TaskOrchestrationContext> _context;
        Mock<IEmailSenderRecipient> _emailSenderAndRecipients;
        Mock<IGMMResources> _gmmResources;
        SyncStatus? _syncStatus = SyncStatus.Idle;
        DestinationVerifierResult _destinationVerifierResult;
        Mock<IServiceBusQueueRepository> _serviceBusQueueRepository;
        SyncJob _syncJob;
        string _destinationName;
        TelemetryClient _telemetryClient;
        List<string> _endpoints;
        int _frequency;
        JsonSchemaProvider _jsonSchemaProvider;
        bool _jsonValidationResult;

        [TestInitialize]
        public void Setup()
        {
            _syncJob = SampleDataHelper.CreateSampleSyncJobs(1, "GroupMembership").First();
            _destinationVerifierResult = DestinationVerifierResult.Success;
            _gmmResources = new Mock<IGMMResources>();
            _jobTriggerService = new Mock<IJobTriggerService>();
            _context = new Mock<TaskOrchestrationContext>();
            _context.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(NullLogger.Instance);
            _emailSenderAndRecipients = new Mock<IEmailSenderRecipient>();
            _telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            _endpoints = new List<string> { "Yammer", "Teams" };
            _frequency = 0;
            _jsonSchemaProvider = new JsonSchemaProvider();

            _jobTriggerService.Setup(x => x.DestinationExistsAndGMMCanWriteToItAsync(It.IsAny<SyncJob>())).ReturnsAsync(() => _destinationVerifierResult);
            _jobTriggerService.Setup(x => x.GetDestinationNameAsync(It.IsAny<SyncJob>())).ReturnsAsync(() => "Test Group");
            _jobTriggerService.Setup(x => x.GetGroupEndpointsAsync(It.IsAny<SyncJob>())).ReturnsAsync(() => _endpoints);

            var destinationObject = new DestinationObject
            {
                Type = "GroupMembership",
                Value = new GroupDestinationValue
                {
                    ObjectId = Guid.NewGuid()
                }
            };

            var group = new Group
            {
                GroupId = Guid.NewGuid(),
                SyncJobId = Guid.NewGuid()
            };

            var channel = new Channel
            {
                ChannelId = "some-string",
                GroupId = Guid.NewGuid(),
                SyncJobId = Guid.NewGuid()
            };

            var options = new JsonSerializerOptions { Converters = { new DestinationValueConverter() } };
            var serializedDestinationObject = JsonSerializer.Serialize(destinationObject, options);

            _jobTriggerService.Setup(x => x.ParseAndValidateDestinationAsync(It.IsAny<SyncJob>())).ReturnsAsync(() => new ParsedAndValidateDestinationResponse { IsValid = true, DestinationObject = serializedDestinationObject });

            _jobTriggerService.Setup(x => x.UpdateSyncJobAsync(It.IsAny<SyncStatus?>(), It.IsAny<SyncJob>()))
                              .Callback<SyncStatus?, SyncJob>((status, job) =>
                              {
                                  _syncStatus = status;
                              });

            _context.Setup(x => x.CallActivityAsync<int>(It.IsAny<TaskName>(), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()))
                                        .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                        {
                                            _frequency = await CallJobTrackerFunctionAsync(request as SyncJob, DateTime.UtcNow);
                                        })
                                        .ReturnsAsync(() => _frequency);

            _context.Setup(x => x.CallActivityAsync<ParsedAndValidateDestinationResponse>(It.Is<TaskName>(x => x == nameof(ParseAndValidateDestinationFunction)), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()))
                   .Returns(async () => await CallParseAndValidateDestinationFunction());

            _context.Setup(x => x.CallActivityAsync<Group>(It.Is<TaskName>(x => x == nameof(GetGroupFunction)), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()))
                  .ReturnsAsync(group);
            _context.Setup(x => x.CallActivityAsync<Channel>(It.Is<TaskName>(x => x == nameof(GetChannelFunction)), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()))
                  .ReturnsAsync(channel);

            _context.Setup(x => x.CallActivityAsync(It.Is<TaskName>(x => x == nameof(TelemetryTrackerFunction)), It.IsAny<TelemetryTrackerRequest>(), It.IsAny<TaskOptions>()))
                    .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                    {
                        var telemetryRequest = request as TelemetryTrackerRequest;
                        await CallTelemetryTrackerFunctionAsync(telemetryRequest);
                    });

            _context.Setup(x => x.CallActivityAsync(It.Is<TaskName>(x => x == nameof(JobUpdaterFunction)), It.IsAny<JobUpdaterRequest>(), It.IsAny<TaskOptions>()))
                    .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                    {
                        var updateRequest = request as JobUpdaterRequest;
                        await CallJobStatusUpdaterFunctionAsync(updateRequest);
                        _syncStatus = updateRequest.Status;
                    });

            _context.Setup(x => x.CallActivityAsync<DestinationVerifierResult>(It.Is<TaskName>(x => x == nameof(DestinationVerifierFunction)), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()))
                    .Returns(async () => await CallDestinationVerifierFunctionAsync());

            _context.Setup(x => x.CallActivityAsync<string>(It.Is<TaskName>(x => x == nameof(DestinationNameReaderFunction)), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()))
                    .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                    {
                        _destinationName = await CallDestinationNameReaderFunctionAsync();
                    })
                    .ReturnsAsync(() => _destinationName);

            _context.Setup(x => x.CallActivityAsync(It.Is<TaskName>(x => x == nameof(EmailSenderFunction)), It.IsAny<EmailSenderRequest>(), It.IsAny<TaskOptions>()))
                     .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                     {
                         await CallEmailSenderFunctionAsync(request as EmailSenderRequest);
                     });

            _context.Setup(x => x.CallActivityAsync<SyncJob>(It.Is<TaskName>(x => x == nameof(GetSyncJobFunction)), It.IsAny<Guid>(), It.IsAny<TaskOptions>()))
                    .ReturnsAsync((TaskName name, object syncJobId, TaskOptions options) =>
                    {
                        var refreshedJob = _syncJob;
                        refreshedJob.RunId = _syncJob.RunId; // Preserve RunId
                        return refreshedJob;
                    });

            _context.Setup(x => x.CallActivityAsync(It.Is<TaskName>(x => x == nameof(TopicMessageSenderFunction)), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()))
                    .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                    {
                        await CallTopicMessageSenderFunctionAsync();
                    });

            _context.Setup(x => x.CallActivityAsync<bool>(nameof(SchemaValidatorFunction), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()))
                    .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                    {
                        _jsonValidationResult = await CallSchemaValidatorFunctionAsync(request as SyncJob);
                    }).ReturnsAsync(() => _jsonValidationResult);

            _context.Setup(x => x.CallActivityAsync<bool>(
                        It.Is<TaskName>(x => x == nameof(ClaimJobFunction)),
                        It.IsAny<ClaimJobRequest>(),
                        It.IsAny<TaskOptions>()))
                    .ReturnsAsync(true);

            _jsonSchemaProvider = SchemaProviderFactory.CreateJsonSchemaProvider();
        }

        [TestMethod]
        public async Task HandleInvalidDestinationQueryException()
        {
            _context.Setup(x => x.GetInput<SyncJob>()).Returns(_syncJob);
            _jobTriggerService.Setup(x => x.ParseAndValidateDestinationAsync(It.IsAny<SyncJob>())).Throws<JsonException>();

            var suborchrestrator = new SubOrchestratorFunction(
                                                                _telemetryClient,
                                                                _emailSenderAndRecipients.Object,
                                                                _gmmResources.Object);
            await suborchrestrator.RunSubOrchestratorAsync(_context.Object);

            _jobTriggerService.Verify(x => x.UpdateSyncJobAsync(It.IsAny<SyncStatus>(), It.IsAny<SyncJob>()), Times.Once());
            _jobTriggerService.Verify(x => x.UpdateSyncJobAsync(It.Is<SyncStatus>(s => s == SyncStatus.DestinationQueryNotValid), It.IsAny<SyncJob>()), Times.Once());

        }

        [TestMethod]
        public async Task HandleInvalidDestinationQuery()
        {
            _syncJob.MembershipType = null;
            _context.Setup(x => x.GetInput<SyncJob>()).Returns(_syncJob);
            _jobTriggerService.Setup(x => x.ParseAndValidateDestinationAsync(It.IsAny<SyncJob>())).ReturnsAsync(() => new ParsedAndValidateDestinationResponse
            {
                IsValid = false,
                DestinationObject = null
            });

            var suborchrestrator = new SubOrchestratorFunction(
                                                                _telemetryClient,
                                                                _emailSenderAndRecipients.Object,
                                                                _gmmResources.Object);
            await suborchrestrator.RunSubOrchestratorAsync(_context.Object);

            _jobTriggerService.Verify(x => x.UpdateSyncJobAsync(It.IsAny<SyncStatus>(), It.IsAny<SyncJob>()), Times.Once());
            _jobTriggerService.Verify(x => x.UpdateSyncJobAsync(It.Is<SyncStatus>(s => s == SyncStatus.DestinationQueryNotValid), It.IsAny<SyncJob>()), Times.Once());

        }

        [TestMethod]
        public async Task HandleInvalidQueryJson()
        {
            _syncJob.Query = "{invalid json query}";
            _context.Setup(x => x.GetInput<SyncJob>()).Returns(_syncJob);

            var suborchrestrator = new SubOrchestratorFunction(
                                                                _telemetryClient,
                                                                _emailSenderAndRecipients.Object,
                                                                _gmmResources.Object);
            await suborchrestrator.RunSubOrchestratorAsync(_context.Object);

            _jobTriggerService.Verify(x => x.UpdateSyncJobAsync(It.IsAny<SyncStatus>(), It.IsAny<SyncJob>()), Times.Once());
            _jobTriggerService.Verify(x => x.UpdateSyncJobAsync(It.Is<SyncStatus>(s => s == SyncStatus.QueryNotValid), It.IsAny<SyncJob>()), Times.Once());

        }

        [TestMethod]
        public async Task HandleInvalidJson()
        {
            _context.Setup(x => x.CallActivityAsync<bool>(nameof(SchemaValidatorFunction), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()))
                    .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                    {
                        var job = request as SyncJob;
                        job.Query = "{invalid-json}";
                        _jsonValidationResult = await CallSchemaValidatorFunctionAsync(request as SyncJob);
                    }).ReturnsAsync(() => _jsonValidationResult);

            _context.Setup(x => x.GetInput<SyncJob>()).Returns(_syncJob);

            var suborchrestrator = new SubOrchestratorFunction(
                                                                _telemetryClient,
                                                                _emailSenderAndRecipients.Object,
                                                                _gmmResources.Object);
            await suborchrestrator.RunSubOrchestratorAsync(_context.Object);

            _jobTriggerService.Verify(x => x.UpdateSyncJobAsync(It.IsAny<SyncStatus>(), It.IsAny<SyncJob>()), Times.Once());
            _jobTriggerService.Verify(x => x.UpdateSyncJobAsync(It.Is<SyncStatus>(s => s == SyncStatus.SchemaError), It.IsAny<SyncJob>()), Times.Once());

        }

        [TestMethod]
        public async Task HandleNoSchemasLoaded()
        {
            _jsonSchemaProvider.Schemas.Clear();

            _context.Setup(x => x.GetInput<SyncJob>()).Returns(_syncJob);

            var suborchrestrator = new SubOrchestratorFunction(
                                                                _telemetryClient,
                                                                _emailSenderAndRecipients.Object,
                                                                _gmmResources.Object);
            await suborchrestrator.RunSubOrchestratorAsync(_context.Object);

        }

        [TestMethod]
        public async Task HandleSchemasForUnknownProperty()
        {
            _jsonSchemaProvider.Schemas.Clear();
            _jsonSchemaProvider.Schemas.Add("Test", "");

            _context.Setup(x => x.GetInput<SyncJob>()).Returns(_syncJob);

            var suborchrestrator = new SubOrchestratorFunction(
                                                                _telemetryClient,
                                                                _emailSenderAndRecipients.Object,
                                                                _gmmResources.Object);
            await suborchrestrator.RunSubOrchestratorAsync(_context.Object);

        }

        [TestMethod]
        public async Task HandleValidQueryJsonButWrongSchema()
        {
            // invalid property -> sources
            _syncJob.Query = $"[{{\"type\":\"GroupMembership\",\"sources\": \"{Guid.NewGuid()}\"}}]";
            _context.Setup(x => x.GetInput<SyncJob>()).Returns(_syncJob);

            var suborchrestrator = new SubOrchestratorFunction(
                                                                _telemetryClient,
                                                                _emailSenderAndRecipients.Object,
                                                                _gmmResources.Object);
            await suborchrestrator.RunSubOrchestratorAsync(_context.Object);

            _jobTriggerService.Verify(x => x.UpdateSyncJobAsync(It.IsAny<SyncStatus>(), It.IsAny<SyncJob>()), Times.Once());
            _jobTriggerService.Verify(x => x.UpdateSyncJobAsync(SyncStatus.SchemaError, It.IsAny<SyncJob>()), Times.Once());

        }

        [TestMethod]
        public async Task HandleStuckInProgressJobs()
        {
            _syncJob.Status = SyncStatus.StuckInProgress.ToString();
            _context.Setup(x => x.GetInput<SyncJob>()).Returns(_syncJob);

            var suborchrestrator = new SubOrchestratorFunction(
                                                                _telemetryClient,
                                                                _emailSenderAndRecipients.Object,
                                                                _gmmResources.Object);
            await suborchrestrator.RunSubOrchestratorAsync(_context.Object);
            _jobTriggerService.Verify(x => x.UpdateSyncJobAsync(SyncStatus.ErroredDueToStuckInProgress, It.IsAny<SyncJob>()), Times.Once());
        }

        [TestMethod]
        public async Task HandleEmptyGroupId()
        {
            _syncJob.Group = null;
            _syncJob.Status = SyncStatus.Idle.ToString();
            _context.Setup(x => x.GetInput<SyncJob>()).Returns(_syncJob);
            _context.Setup(x => x.CallActivityAsync<Group>(It.Is<TaskName>(x => x == nameof(GetGroupFunction)), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()))
                 .ReturnsAsync(_syncJob.Group);

            var suborchrestrator = new SubOrchestratorFunction(
                                                                _telemetryClient,
                                                                _emailSenderAndRecipients.Object,
                                                                _gmmResources.Object);
            await suborchrestrator.RunSubOrchestratorAsync(_context.Object);
            _jobTriggerService.Verify(x => x.UpdateSyncJobAsync(SyncStatus.Error, It.IsAny<SyncJob>()), Times.Once());
        }

        [TestMethod]

        public async Task HandleEmptySourceQuery()
        {
            _syncJob.Query = null;
            _context.Setup(x => x.GetInput<SyncJob>()).Returns(_syncJob);

            var suborchrestrator = new SubOrchestratorFunction(
                                                    _telemetryClient,
                                                    _emailSenderAndRecipients.Object,
                                                    _gmmResources.Object);
            await suborchrestrator.RunSubOrchestratorAsync(_context.Object);

            _jobTriggerService.Verify(x => x.UpdateSyncJobAsync(It.IsAny<SyncStatus>(), It.IsAny<SyncJob>()), Times.Once());
            _jobTriggerService.Verify(x => x.UpdateSyncJobAsync(It.Is<SyncStatus>(s => s == SyncStatus.QueryNotValid), It.IsAny<SyncJob>()), Times.Once());

        }

        [TestMethod]
        public async Task ProcessValidSourceAndDestinationQueries()
        {

            _context.Setup(x => x.GetInput<SyncJob>()).Returns(_syncJob);

            _context.Setup(x => x.CallActivityAsync<int>(It.IsAny<TaskName>(), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()))
                                        .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                        {
                                            _frequency = await CallJobTrackerFunctionAsync(request as SyncJob, SqlDateTime.MinValue.Value);
                                        })
                                        .ReturnsAsync(() => _frequency);

            var suborchrestrator = new SubOrchestratorFunction(
                                                    _telemetryClient,
                                                    _emailSenderAndRecipients.Object,
                                                    _gmmResources.Object);
            await suborchrestrator.RunSubOrchestratorAsync(_context.Object);



            _context.Verify(x => x.CallActivityAsync<int>(It.Is<TaskName>(x => x == nameof(JobTrackerFunction)), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()), Times.Once());
            _context.Verify(x => x.CallActivityAsync<string>(It.Is<TaskName>(x => x == nameof(DestinationNameReaderFunction)), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()), Times.Once());
            _context.Verify(x => x.CallActivityAsync(It.Is<TaskName>(x => x == nameof(EmailSenderFunction)), It.IsAny<EmailSenderRequest>(), It.IsAny<TaskOptions>()), Times.Once());
            _context.Verify(x => x.CallActivityAsync(It.Is<TaskName>(x => x == nameof(TopicMessageSenderFunction)), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()), Times.Once());

            _jobTriggerService.Verify(x => x.GetDestinationNameAsync(It.IsAny<SyncJob>()), Times.Once());
            _jobTriggerService.Verify(x => x.SendEmailAsync(It.IsAny<SyncJob>(), It.IsAny<NotificationMessageType>(), It.IsAny<string[]>()), Times.Once());
            _jobTriggerService.Verify(x => x.SendMessageAsync(It.IsAny<SyncJob>()), Times.Once());

            _context.Verify(x => x.CallActivityAsync<bool>(
                It.Is<TaskName>(t => t == nameof(ClaimJobFunction)),
                It.Is<ClaimJobRequest>(r => r.Status == SyncStatus.InProgress),
                It.IsAny<TaskOptions>()), Times.Once());
        }

        [TestMethod]
        public async Task ProcessIdleJob()
        {
            var serviceBusSender = new Mock<ServiceBusSender>();

            _context.Setup(x => x.GetInput<SyncJob>()).Returns(_syncJob);
            _context.Setup(x => x.CallActivityAsync(It.Is<TaskName>(x => x == nameof(TopicMessageSenderFunction)), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()))
                    .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                    {
                        var gmmResources = new Mock<IGMMResources>();
                        var jobTriggerConfig = new Mock<IJobTriggerConfig>();
                        var syncJobRepository = new Mock<IDatabaseSyncJobsRepository>();
                        var groupsRepository = new Mock<IDatabaseGroupsRepository>();
                        var channelsRepository = new Mock<IDatabaseChannelsRepository>();
                        var destinationAttributesRepository = new Mock<IDatabaseDestinationAttributesRepository>();
                        var notificationTypesRepository = new Mock<INotificationTypesRepository>();
                        var iJobNotificationRepository = new Mock<IJobNotificationsRepository>();
                        var graphGroupRepository = new Mock<IGraphGroupRepository>();
                        var teamsChannelRepository = new Mock<ITeamsChannelRepository>();
                        var gmmAppId = new Mock<IKeyVaultSecret<IJobTriggerService>>();
                        var teamsChannelServiceAccountObjectId = new Mock<IKeyVaultSecret<IJobTriggerService, Guid>>();
                        var emailSenderAndRecipients = new Mock<IEmailSenderRecipient>();
                        var serviceBusTopicsRepository = new ServiceBusTopicsRepository(serviceBusSender.Object);
                        var serviceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
                        var syncJobStatusService = new Mock<ISyncJobStatusService>();
                        var jobTriggerService = new JobTriggerService(
                                                        NullLogger<JobTriggerService>.Instance,
                                                        syncJobRepository.Object,
                                                        groupsRepository.Object,
                                                        channelsRepository.Object,
                                                        destinationAttributesRepository.Object,
                                                        notificationTypesRepository.Object,
                                                        iJobNotificationRepository.Object,
                                                        serviceBusTopicsRepository,
                                                        graphGroupRepository.Object,
                                                        teamsChannelRepository.Object,
                                                        gmmAppId.Object,
                                                        teamsChannelServiceAccountObjectId.Object,
                                                        emailSenderAndRecipients.Object,
                                                        serviceBusQueueRepository.Object,
                                                        gmmResources.Object,
                                                        jobTriggerConfig.Object,
                                                        _telemetryClient,
                                                        syncJobStatusService.Object
                                                        );

                        await CallTopicMessageSenderFunctionAsync(jobTriggerService: jobTriggerService);
                    });

            _context.Setup(x => x.CallActivityAsync<int>(nameof(JobTrackerFunction), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>())).ReturnsAsync(2);
            var suborchestrator = new SubOrchestratorFunction(
                                                                _telemetryClient,
                                                                _emailSenderAndRecipients.Object,
                                                                _gmmResources.Object);
            await suborchestrator.RunSubOrchestratorAsync(_context.Object);


            _context.Verify(x => x.CallActivityAsync<int>(It.Is<TaskName>(x => x == nameof(JobTrackerFunction)), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()), Times.Once());
            _context.Verify(x => x.CallActivityAsync<string>(It.Is<TaskName>(x => x == nameof(DestinationNameReaderFunction)), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()), Times.Once());
            _context.Verify(x => x.CallActivityAsync(It.Is<TaskName>(x => x == nameof(EmailSenderFunction)), It.IsAny<EmailSenderRequest>(), It.IsAny<TaskOptions>()), Times.Once());
            _context.Verify(x => x.CallActivityAsync(It.Is<TaskName>(x => x == nameof(TopicMessageSenderFunction)), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()), Times.Once());

            _jobTriggerService.Verify(x => x.GetDestinationNameAsync(It.IsAny<SyncJob>()), Times.Once());
            _jobTriggerService.Verify(x => x.SendEmailAsync(It.IsAny<SyncJob>(), It.IsAny<NotificationMessageType>(), It.IsAny<string[]>()), Times.Once());

            _context.Verify(x => x.CallActivityAsync<bool>(
                It.Is<TaskName>(t => t == nameof(ClaimJobFunction)),
                It.Is<ClaimJobRequest>(r => r.Status == SyncStatus.InProgress),
                It.IsAny<TaskOptions>()), Times.Once());

            serviceBusSender.Verify(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
            serviceBusSender.Verify(x => x.SendMessageAsync(
                It.Is<ServiceBusMessage>(m => (string)m.ApplicationProperties["Type"] == "GroupMembership"),
                It.IsAny<CancellationToken>()),
                Times.Exactly(2));

            serviceBusSender.Verify(x => x.SendMessageAsync(
                It.Is<ServiceBusMessage>(m => m.ApplicationProperties.ContainsKey("IsDestinationPart")
                                              && (bool)m.ApplicationProperties["IsDestinationPart"]),
                It.IsAny<CancellationToken>()),
                Times.Once());
        }
        [TestMethod]
        public async Task ProcessInProgressJob()
        {
            var serviceBusSender = new Mock<ServiceBusSender>();

            _syncJob.Status = SyncStatus.InProgress.ToString();

            _context.Setup(x => x.GetInput<SyncJob>()).Returns(_syncJob);
            _context.Setup(x => x.CallActivityAsync(It.Is<TaskName>(x => x == nameof(TopicMessageSenderFunction)), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()))
                    .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                    {
                        var gmmResources = new Mock<IGMMResources>();
                        var jobTriggerConfig = new Mock<IJobTriggerConfig>();
                        var syncJobRepository = new Mock<IDatabaseSyncJobsRepository>();
                        var groupsRepository = new Mock<IDatabaseGroupsRepository>();
                        var channelsRepository = new Mock<IDatabaseChannelsRepository>();
                        var destinationAttributesRepository = new Mock<IDatabaseDestinationAttributesRepository>();
                        var emailTypeRepository = new Mock<INotificationTypesRepository>();
                        var iJobNotificationRepository = new Mock<IJobNotificationsRepository>();
                        var graphGroupRepository = new Mock<IGraphGroupRepository>();
                        var teamsChannelRepository = new Mock<ITeamsChannelRepository>();
                        var serviceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
                        var gmmAppId = new Mock<IKeyVaultSecret<IJobTriggerService>>();
                        var teamsChannelServiceAccountObjectId = new Mock<IKeyVaultSecret<IJobTriggerService, Guid>>();
                        var emailSenderAndRecipients = new Mock<IEmailSenderRecipient>();
                        var serviceBusTopicsRepository = new ServiceBusTopicsRepository(serviceBusSender.Object);
                        var syncJobStatusService = new Mock<ISyncJobStatusService>();
                        var jobTriggerService = new JobTriggerService(
                                                        NullLogger<JobTriggerService>.Instance,
                                                        syncJobRepository.Object,
                                                        groupsRepository.Object,
                                                        channelsRepository.Object,
                                                        destinationAttributesRepository.Object,
                                                        emailTypeRepository.Object,
                                                        iJobNotificationRepository.Object,
                                                        serviceBusTopicsRepository,
                                                        graphGroupRepository.Object,
                                                        teamsChannelRepository.Object,
                                                        gmmAppId.Object,
                                                        teamsChannelServiceAccountObjectId.Object,
                                                        emailSenderAndRecipients.Object,
                                                        serviceBusQueueRepository.Object,
                                                        gmmResources.Object,
                                                        jobTriggerConfig.Object,
                                                        _telemetryClient,
                                                        syncJobStatusService.Object
                                                        );

                        await CallTopicMessageSenderFunctionAsync(jobTriggerService: jobTriggerService);
                    });

            _context.Setup(x => x.CallActivityAsync<int>(nameof(JobTrackerFunction), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>())).ReturnsAsync(2);
            var suborchestrator = new SubOrchestratorFunction(
                                                                _telemetryClient,
                                                                _emailSenderAndRecipients.Object,
                                                                _gmmResources.Object);
            await suborchestrator.RunSubOrchestratorAsync(_context.Object);


            _context.Verify(x => x.CallActivityAsync<int>(It.Is<TaskName>(x => x == nameof(JobTrackerFunction)), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()), Times.Once());
            _context.Verify(x => x.CallActivityAsync<string>(It.Is<TaskName>(x => x == nameof(DestinationNameReaderFunction)), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()), Times.Once());
            _context.Verify(x => x.CallActivityAsync(It.Is<TaskName>(x => x == nameof(EmailSenderFunction)), It.IsAny<EmailSenderRequest>(), It.IsAny<TaskOptions>()), Times.Once());
            _context.Verify(x => x.CallActivityAsync(It.Is<TaskName>(x => x == nameof(TopicMessageSenderFunction)), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()), Times.Once());

            _jobTriggerService.Verify(x => x.GetDestinationNameAsync(It.IsAny<SyncJob>()), Times.Once());
            _jobTriggerService.Verify(x => x.SendEmailAsync(It.IsAny<SyncJob>(), It.IsAny<NotificationMessageType>(), It.IsAny<string[]>()), Times.Once());

            _context.Verify(x => x.CallActivityAsync<bool>(
                It.Is<TaskName>(t => t == nameof(ClaimJobFunction)),
                It.Is<ClaimJobRequest>(r => r.Status == SyncStatus.StuckInProgress),
                It.IsAny<TaskOptions>()), Times.Once());

            serviceBusSender.Verify(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
            serviceBusSender.Verify(x => x.SendMessageAsync(
                It.Is<ServiceBusMessage>(m => (string)m.ApplicationProperties["Type"] == "GroupMembership"),
                It.IsAny<CancellationToken>()), Times.Exactly(2));

            serviceBusSender.Verify(x => x.SendMessageAsync(
                It.Is<ServiceBusMessage>(m => m.ApplicationProperties.ContainsKey("IsDestinationPart")
                                              && (bool)m.ApplicationProperties["IsDestinationPart"]),
                It.IsAny<CancellationToken>()), Times.Once());
        }

        [TestMethod]
        public async Task DestinationNotFound()
        {
            _context.Setup(x => x.GetInput<SyncJob>()).Returns(_syncJob);
            _context.Setup(x => x.CallActivityAsync<DestinationVerifierResult>(It.Is<TaskName>(x => x == nameof(DestinationVerifierFunction)), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()))
                    .ReturnsAsync(DestinationVerifierResult.NotFound);

            var suborchrestrator = new SubOrchestratorFunction(
                                        _telemetryClient,
                                        _emailSenderAndRecipients.Object,
                                        _gmmResources.Object);
            await suborchrestrator.RunSubOrchestratorAsync(_context.Object);
            _context.Verify(x => x.CallActivityAsync(It.Is<TaskName>(x => x == nameof(TelemetryTrackerFunction)), It.IsAny<TelemetryTrackerRequest>(), It.IsAny<TaskOptions>()), Times.Once());
            Assert.AreEqual(SyncStatus.DestinationGroupNotFound, _syncStatus);
            _context.Verify(x => x.CallActivityAsync(It.Is<TaskName>(x => x == nameof(EmailSenderFunction)), It.Is<EmailSenderRequest>(r => r.NotificationType == NotificationMessageType.DestinationNotExistNotification), It.IsAny<TaskOptions>()), Times.Once());
        }

        [TestMethod]
        public async Task DestinationNotOwnedByGMM()
        {
            _context.Setup(x => x.GetInput<SyncJob>()).Returns(_syncJob);
            _context.Setup(x => x.CallActivityAsync<DestinationVerifierResult>(It.Is<TaskName>(x => x == nameof(DestinationVerifierFunction)), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()))
                    .ReturnsAsync(DestinationVerifierResult.NotOwnedByGMM);

            var suborchrestrator = new SubOrchestratorFunction(
                                        _telemetryClient,
                                        _emailSenderAndRecipients.Object,
                                        _gmmResources.Object);
            await suborchrestrator.RunSubOrchestratorAsync(_context.Object);
            _context.Verify(x => x.CallActivityAsync(It.Is<TaskName>(x => x == nameof(TelemetryTrackerFunction)), It.IsAny<TelemetryTrackerRequest>(), It.IsAny<TaskOptions>()), Times.Once());
            Assert.AreEqual(SyncStatus.NotOwnerOfDestinationGroup, _syncStatus);
            _context.Verify(x => x.CallActivityAsync(It.Is<TaskName>(x => x == nameof(EmailSenderFunction)), It.Is<EmailSenderRequest>(r => r.NotificationType == NotificationMessageType.NotOwnerNotification), It.IsAny<TaskOptions>()), Times.Once());
        }

        [TestMethod]
        public async Task TrackTelemetry()
        {
            _context.Setup(x => x.GetInput<SyncJob>()).Returns(_syncJob);
            _context.Setup(x => x.CallActivityAsync<int>(It.IsAny<TaskName>(), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()))
                                        .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                        {
                                            _frequency = await CallJobTrackerFunctionAsync(request as SyncJob, DateTime.UtcNow.AddDays(-1));
                                        })
                                        .ReturnsAsync(() => _frequency);

            _destinationVerifierResult = DestinationVerifierResult.NotOwnedByGMM;

            var suborchrestrator = new SubOrchestratorFunction(
                            _telemetryClient,
                            _emailSenderAndRecipients.Object,
                            _gmmResources.Object);
            await suborchrestrator.RunSubOrchestratorAsync(_context.Object);
            _context.Verify(x => x.CallActivityAsync(It.Is<TaskName>(x => x == nameof(TelemetryTrackerFunction)), It.IsAny<TelemetryTrackerRequest>(), It.IsAny<TaskOptions>()), Times.Once());
            Assert.AreEqual(SyncStatus.NotOwnerOfDestinationGroup, _syncStatus);

            _destinationVerifierResult = DestinationVerifierResult.NotOwnedByGMM;
            _syncJob.Query = "";
            _context.Setup(x => x.GetInput<SyncJob>()).Returns(_syncJob);
            suborchrestrator = new SubOrchestratorFunction(
                            _telemetryClient,
                            _emailSenderAndRecipients.Object,
                            _gmmResources.Object);
            await suborchrestrator.RunSubOrchestratorAsync(_context.Object);
            _context.Verify(x => x.CallActivityAsync(It.Is<TaskName>(x => x == nameof(TelemetryTrackerFunction)), It.IsAny<TelemetryTrackerRequest>(), It.IsAny<TaskOptions>()), Times.Exactly(2));
            Assert.AreEqual(SyncStatus.QueryNotValid, _syncStatus);
        }

        [TestMethod]
        public async Task HandleUnexpectedExceptionInSubOrchestrator()
        {
            _context.Setup(x => x.GetInput<SyncJob>()).Returns(_syncJob);
            _context.Setup(x => x.CallActivityAsync<int>(It.IsAny<TaskName>(), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()))
					.Throws(new Exception("Unexpected exception triggered for testing"));

            var suborchrestrator = new SubOrchestratorFunction(
                                                                _telemetryClient,
                                                                _emailSenderAndRecipients.Object,
                                                                _gmmResources.Object);
            await suborchrestrator.RunSubOrchestratorAsync(_context.Object);

			_context.Verify(x => x.CallActivityAsync(It.Is<TaskName>(x => x == nameof(JobUpdaterFunction)),
				It.Is<JobUpdaterRequest>(req => req.Status == SyncStatus.Error), It.IsAny<TaskOptions>()), Times.Once());
			_context.Verify(x => x.CallActivityAsync(It.Is<TaskName>(x => x == nameof(TelemetryTrackerFunction)),
				It.Is<TelemetryTrackerRequest>(req => req.JobStatus == SyncStatus.Error && req.ResultStatus == ResultStatus.Failure), It.IsAny<TaskOptions>()), Times.Once());
        }

        [TestMethod]
        public async Task ClaimJob_ReturnsFalse_SubOrchestratorBailsOut()
        {
            _context.Setup(x => x.GetInput<SyncJob>()).Returns(_syncJob);

            _context.Setup(x => x.CallActivityAsync<bool>(
                        It.Is<TaskName>(t => t == nameof(ClaimJobFunction)),
                        It.IsAny<ClaimJobRequest>(),
                        It.IsAny<TaskOptions>()))
                    .ReturnsAsync(false);

            var suborchestrator = new SubOrchestratorFunction(
                _telemetryClient, _emailSenderAndRecipients.Object, _gmmResources.Object);
            await suborchestrator.RunSubOrchestratorAsync(_context.Object);

            _context.Verify(x => x.CallActivityAsync<int>(
                It.Is<TaskName>(t => t == nameof(JobTrackerFunction)),
                It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()), Times.Never());
            _context.Verify(x => x.CallActivityAsync<ParsedAndValidateDestinationResponse>(
                It.Is<TaskName>(t => t == nameof(ParseAndValidateDestinationFunction)),
                It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()), Times.Never());
            _context.Verify(x => x.CallActivityAsync(
                It.Is<TaskName>(t => t == nameof(TopicMessageSenderFunction)),
                It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()), Times.Never());
        }

        [TestMethod]
        public async Task ClaimJob_ReturnsTrue_SubOrchestratorProceeds()
        {
            _context.Setup(x => x.GetInput<SyncJob>()).Returns(_syncJob);

            var suborchestrator = new SubOrchestratorFunction(
                _telemetryClient, _emailSenderAndRecipients.Object, _gmmResources.Object);
            await suborchestrator.RunSubOrchestratorAsync(_context.Object);

            _context.Verify(x => x.CallActivityAsync<bool>(
                It.Is<TaskName>(t => t == nameof(ClaimJobFunction)),
                It.Is<ClaimJobRequest>(r => r.Status == SyncStatus.InProgress),
                It.IsAny<TaskOptions>()), Times.Once());
            _context.Verify(x => x.CallActivityAsync(
                It.Is<TaskName>(t => t == nameof(TopicMessageSenderFunction)),
                It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()), Times.Once());
        }

        [TestMethod]
        public async Task ClaimJob_CalledWithCorrectStatus_WhenIdle()
        {
            _syncJob.Status = SyncStatus.Idle.ToString();
            _context.Setup(x => x.GetInput<SyncJob>()).Returns(_syncJob);

            var suborchestrator = new SubOrchestratorFunction(
                _telemetryClient, _emailSenderAndRecipients.Object, _gmmResources.Object);
            await suborchestrator.RunSubOrchestratorAsync(_context.Object);

            _context.Verify(x => x.CallActivityAsync<bool>(
                It.Is<TaskName>(t => t == nameof(ClaimJobFunction)),
                It.Is<ClaimJobRequest>(r => r.Status == SyncStatus.InProgress),
                It.IsAny<TaskOptions>()), Times.Once());
        }

        [TestMethod]
        public async Task ClaimJob_CalledWithCorrectStatus_WhenInProgress()
        {
            _syncJob.Status = SyncStatus.InProgress.ToString();
            _context.Setup(x => x.GetInput<SyncJob>()).Returns(_syncJob);

            var suborchestrator = new SubOrchestratorFunction(
                _telemetryClient, _emailSenderAndRecipients.Object, _gmmResources.Object);
            await suborchestrator.RunSubOrchestratorAsync(_context.Object);

            _context.Verify(x => x.CallActivityAsync<bool>(
                It.Is<TaskName>(t => t == nameof(ClaimJobFunction)),
                It.Is<ClaimJobRequest>(r => r.Status == SyncStatus.StuckInProgress),
                It.IsAny<TaskOptions>()), Times.Once());
        }

        private async Task<ParsedAndValidateDestinationResponse> CallParseAndValidateDestinationFunction()
        {
            var parseAndValidateDestinationFunction = new ParseAndValidateDestinationFunction(NullLogger<ParseAndValidateDestinationFunction>.Instance, _jobTriggerService.Object);
            return await parseAndValidateDestinationFunction.ParseAndValidateDestinationAsync(new SyncJob());
        }
        private async Task<int> CallIdleJobsTrackerFunctionAsync(SyncJob syncJob)
        {
            var jobTrackerFunction = new JobTrackerFunction(NullLogger<JobTrackerFunction>.Instance);
            var frequency = await jobTrackerFunction.TrackJobFrequencyAsync(syncJob);
            return frequency;
        }

        private async Task<int> CallJobTrackerFunctionAsync(SyncJob syncJob, DateTime dateTime)
        {
            syncJob.LastSuccessfulRunTime = dateTime;
            var jobTrackerFunction = new JobTrackerFunction(NullLogger<JobTrackerFunction>.Instance);
            return await jobTrackerFunction.TrackJobFrequencyAsync(syncJob);
        }

        private async Task CallTelemetryTrackerFunctionAsync(TelemetryTrackerRequest request)
        {
            var telemetryTrackerFunction = new TelemetryTrackerFunction(NullLogger<TelemetryTrackerFunction>.Instance, _telemetryClient);
            await telemetryTrackerFunction.TrackEventAsync(request);
        }

        private async Task CallJobStatusUpdaterFunctionAsync(JobUpdaterRequest request)
        {
            var jobStatusUpdaterFunction = new JobUpdaterFunction(NullLogger<JobUpdaterFunction>.Instance, _jobTriggerService.Object);
            await jobStatusUpdaterFunction.UpdateJobAsync(request);
        }

        private async Task<DestinationVerifierResult> CallDestinationVerifierFunctionAsync()
        {
            var groupVerifierFunction = new DestinationVerifierFunction(NullLogger<DestinationVerifierFunction>.Instance, _jobTriggerService.Object);
            return await groupVerifierFunction.VerifyDestinationAsync(_syncJob);
        }

        private async Task<string> CallDestinationNameReaderFunctionAsync()
        {
            var groupNameReaderFunction = new DestinationNameReaderFunction(NullLogger<DestinationNameReaderFunction>.Instance, _jobTriggerService.Object);
            return await groupNameReaderFunction.GetDestinationNameAsync(_syncJob);
        }

        private async Task CallEmailSenderFunctionAsync(EmailSenderRequest request)
        {
            var emailSenderFunction = new EmailSenderFunction(NullLogger<EmailSenderFunction>.Instance, _jobTriggerService.Object);
            await emailSenderFunction.SendEmailAsync(request);
        }

        private async Task CallTopicMessageSenderFunctionAsync(IJobTriggerService jobTriggerService = null)
        {
            var topicMessageSenderFunction = new TopicMessageSenderFunction(NullLogger<TopicMessageSenderFunction>.Instance, jobTriggerService ?? _jobTriggerService.Object);
            await topicMessageSenderFunction.SendMessageAsync(_syncJob);
        }

        private async Task<bool> CallSchemaValidatorFunctionAsync(SyncJob job)
        {
            var validatorFunction = new SchemaValidatorFunction(NullLogger<SchemaValidatorFunction>.Instance, _jobTriggerService.Object, _jsonSchemaProvider);
            return await validatorFunction.ValidateSchemasAsync(job);
        }

        [TestMethod]
        public async Task GetSyncJobFunction_IsCalled_BeforeTopicMessageSender()
        {
            // Arrange
            _context.Setup(x => x.GetInput<SyncJob>()).Returns(_syncJob);
            var getSyncJobCalled = false;
            var topicMessageSenderCalled = false;
            var getSyncJobCalledFirst = false;

            _context.Setup(x => x.CallActivityAsync<SyncJob>(It.Is<TaskName>(x => x == nameof(GetSyncJobFunction)), It.IsAny<Guid>(), It.IsAny<TaskOptions>()))
                    .Callback<TaskName, object, TaskOptions>((name, input, options) =>
                    {
                        var syncJobId = (Guid)input; // Cast object to Guid
                        getSyncJobCalled = true;
                        if (!topicMessageSenderCalled)
                            getSyncJobCalledFirst = true;
                    })
                    .ReturnsAsync(_syncJob);

            _context.Setup(x => x.CallActivityAsync(It.Is<TaskName>(x => x == nameof(TopicMessageSenderFunction)), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()))
                    .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                    {
                        topicMessageSenderCalled = true;
                        await CallTopicMessageSenderFunctionAsync();
                    });

            // Act
            var suborchestrator = new SubOrchestratorFunction(
                                                                _telemetryClient,
                                                                _emailSenderAndRecipients.Object,
                                                                _gmmResources.Object);
            await suborchestrator.RunSubOrchestratorAsync(_context.Object);

            // Assert
            Assert.IsTrue(getSyncJobCalled, "GetSyncJobFunction should be called");
            Assert.IsTrue(topicMessageSenderCalled, "TopicMessageSenderFunction should be called");
            Assert.IsTrue(getSyncJobCalledFirst, "GetSyncJobFunction should be called before TopicMessageSenderFunction");
            _context.Verify(x => x.CallActivityAsync<SyncJob>(It.Is<TaskName>(x => x == nameof(GetSyncJobFunction)), It.IsAny<Guid>(), It.IsAny<TaskOptions>()), Times.Once());
        }

        [TestMethod]
        public async Task TopicMessageSender_ReceivesRefreshedJob_WithPreservedRunId()
        {
            // Arrange
            _context.Setup(x => x.GetInput<SyncJob>()).Returns(_syncJob);
            var originalRunId = _syncJob.RunId;
            SyncJob jobSentToTopicMessageSender = null;

            var refreshedJob = SampleDataHelper.CreateSampleSyncJobs(1, "GroupMembership").First();
            refreshedJob.Id = _syncJob.Id;
            refreshedJob.Status = SyncStatus.InProgress.ToString();
            refreshedJob.RunId = null; // Simulating database fetch without RunId

            _context.Setup(x => x.CallActivityAsync<SyncJob>(It.Is<TaskName>(x => x == nameof(GetSyncJobFunction)), It.IsAny<Guid>(), It.IsAny<TaskOptions>()))
                    .ReturnsAsync(refreshedJob);

            _context.Setup(x => x.CallActivityAsync(It.Is<TaskName>(x => x == nameof(TopicMessageSenderFunction)), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()))
                    .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                    {
                        jobSentToTopicMessageSender = request as SyncJob;
                        await CallTopicMessageSenderFunctionAsync();
                    });

            // Act
            var suborchestrator = new SubOrchestratorFunction(
                                                                _telemetryClient,
                                                                _emailSenderAndRecipients.Object,
                                                                _gmmResources.Object);
            await suborchestrator.RunSubOrchestratorAsync(_context.Object);

            // Assert
            Assert.IsNotNull(jobSentToTopicMessageSender, "Job should be sent to TopicMessageSenderFunction");
            Assert.AreEqual(originalRunId, jobSentToTopicMessageSender.RunId, "RunId should be preserved from orchestration context");
            Assert.AreEqual(SyncStatus.InProgress.ToString(), jobSentToTopicMessageSender.Status, "Should have refreshed status");
        }

        [TestMethod]
        public async Task TopicMessageSender_UsesFallback_WhenGetSyncJobReturnsNull()
        {
            // Arrange
            _context.Setup(x => x.GetInput<SyncJob>()).Returns(_syncJob);
            SyncJob jobSentToTopicMessageSender = null;

            _context.Setup(x => x.CallActivityAsync<SyncJob>(It.Is<TaskName>(x => x == nameof(GetSyncJobFunction)), It.IsAny<Guid>(), It.IsAny<TaskOptions>()))
                    .ReturnsAsync((SyncJob)null);

            _context.Setup(x => x.CallActivityAsync(It.Is<TaskName>(x => x == nameof(TopicMessageSenderFunction)), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()))
                    .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                    {
                        jobSentToTopicMessageSender = request as SyncJob;
                        await CallTopicMessageSenderFunctionAsync();
                    });

            // Act
            var suborchestrator = new SubOrchestratorFunction(
                                                                _telemetryClient,
                                                                _emailSenderAndRecipients.Object,
                                                                _gmmResources.Object);
            await suborchestrator.RunSubOrchestratorAsync(_context.Object);

            // Assert
            Assert.IsNotNull(jobSentToTopicMessageSender, "Job should still be sent to TopicMessageSenderFunction");
            Assert.AreEqual(_syncJob.Id, jobSentToTopicMessageSender.Id, "Should use in-memory job as fallback");
        }
    }
}
