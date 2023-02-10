// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Hosts.JobTrigger;
using JobTrigger.Activity.EmailSender;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Models;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.ServiceBusTopics;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class SubOrchestratorFunctionTests
    {
        Mock<IJobTriggerService> _jobTriggerService;
        Mock<ILoggingRepository> _loggingRespository;
        Mock<IDurableOrchestrationContext> _context;
        Mock<IEmailSenderRecipient> _emailSenderAndRecipients;
        Mock<IGMMResources> _gmmResources;
        SyncStatus _syncStatus = SyncStatus.Idle;
        bool _canWriteToGroups;
        SyncJob _syncJob;
        SyncJobGroup _syncJobGroup;
        TelemetryClient _telemetryClient;
        List<string> _endpoints;
        int _frequency;

        [TestInitialize]
        public void Setup()
        {
            _syncJob = SampleDataHelper.CreateSampleSyncJobs(1, "SecurityGroup").First();
            _canWriteToGroups = true;
            _gmmResources = new Mock<IGMMResources>();
            _jobTriggerService = new Mock<IJobTriggerService>();
            _context = new Mock<IDurableOrchestrationContext>();
            _loggingRespository = new Mock<ILoggingRepository>();
            _emailSenderAndRecipients = new Mock<IEmailSenderRecipient>();
            _telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            _endpoints = new List<string> { "Yammer", "Teams" };
            _frequency = 0;

            _jobTriggerService.Setup(x => x.GroupExistsAndGMMCanWriteToGroupAsync(It.IsAny<SyncJob>())).ReturnsAsync(() => _canWriteToGroups);
            _jobTriggerService.Setup(x => x.GetGroupNameAsync(It.IsAny<Guid>())).ReturnsAsync(() => "Test Group");
            _jobTriggerService.Setup(x => x.GetGroupEndpointsAsync(It.IsAny<Guid>())).ReturnsAsync(() => _endpoints);

            _context.Setup(x => x.CallActivityAsync<int>(It.IsAny<string>(), It.IsAny<SyncJob>()))
                                        .Callback<string, object>(async (name, request) =>
                                        {
                                            _frequency = await CallJobTrackerFunctionAsync(request as SyncJob, DateTime.UtcNow);
                                        })
                                        .ReturnsAsync(() => _frequency);

            _context.Setup(x => x.CallActivityAsync(It.Is<string>(x => x == nameof(TelemetryTrackerFunction)), It.IsAny<TelemetryTrackerRequest>()))
                    .Callback<string, object>(async (name, request) =>
                    {
                        var telemetryRequest = request as TelemetryTrackerRequest;
                        await CallTelemetryTrackerFunctionAsync(telemetryRequest);
                    });

            _context.Setup(x => x.CallActivityAsync(It.Is<string>(x => x == nameof(JobStatusUpdaterFunction)), It.IsAny<JobStatusUpdaterRequest>()))
                    .Callback<string, object>(async (name, request) =>
                    {
                        var updateRequest = request as JobStatusUpdaterRequest;
                        await CallJobStatusUpdaterFunctionAsync(updateRequest);
                        _syncStatus = updateRequest.Status;
                    });

            _context.Setup(x => x.CallActivityAsync<bool>(It.Is<string>(x => x == nameof(GroupVerifierFunction)), It.IsAny<SyncJob>()))
                    .Returns(async () => await CallGroupVerifierFunctionAsync());

            _context.Setup(x => x.CallActivityAsync<SyncJobGroup>(It.Is<string>(x => x == nameof(GroupNameReaderFunction)), It.IsAny<SyncJob>()))
                    .Callback<string, object>(async (name, request) =>
                    {
                        _syncJobGroup = await CallGroupNameReaderFunctionAsync();
                    })
                    .ReturnsAsync(() => _syncJobGroup);

            _context.Setup(x => x.CallActivityAsync(It.Is<string>(x => x == nameof(EmailSenderFunction)), It.IsAny<EmailSenderRequest>()))
                     .Callback<string, object>(async (name, request) =>
                     {
                         await CallEmailSenderFunctionAsync(request as EmailSenderRequest);
                     });

            _context.Setup(x => x.CallActivityAsync(It.Is<string>(x => x == nameof(TopicMessageSenderFunction)), It.IsAny<SyncJob>()))
                    .Callback<string, object>(async (name, request) =>
                    {
                        await CallTopicMessageSenderFunctionAsync();
                    });
        }

        [TestMethod]
        public async Task HandleInvalidJSONQuery()
        {
            _syncJob.Query = "{invalid json query}";
            _context.Setup(x => x.GetInput<SyncJob>()).Returns(_syncJob);

            var suborchrestrator = new SubOrchestratorFunction(_loggingRespository.Object,
                                                                _telemetryClient,
                                                                _emailSenderAndRecipients.Object,
                                                                _gmmResources.Object);
            await suborchrestrator.RunSubOrchestratorAsync(_context.Object);

            _jobTriggerService.Verify(x => x.UpdateSyncJobStatusAsync(It.IsAny<SyncStatus>(), It.IsAny<SyncJob>()), Times.Once());
            _jobTriggerService.Verify(x => x.UpdateSyncJobStatusAsync(It.Is<SyncStatus>(s => s == SyncStatus.QueryNotValid), It.IsAny<SyncJob>()), Times.Once());

            _loggingRespository.Verify(x => x.LogMessageAsync(
                It.Is<LogMessage>(m => m.Message.Contains("JSON query is not valid")),
                It.IsAny<VerbosityLevel>(),
                It.IsAny<string>(),
                It.IsAny<string>()));
        }

        [TestMethod]
        public async Task HandleStuckInProgressJobs()
        {
            _syncJob.Status = SyncStatus.StuckInProgress.ToString();
            _context.Setup(x => x.GetInput<SyncJob>()).Returns(_syncJob);

            var suborchrestrator = new SubOrchestratorFunction(_loggingRespository.Object,
                                                                _telemetryClient,
                                                                _emailSenderAndRecipients.Object,
                                                                _gmmResources.Object);
            await suborchrestrator.RunSubOrchestratorAsync(_context.Object);
            _jobTriggerService.Verify(x => x.UpdateSyncJobStatusAsync(SyncStatus.ErroredDueToStuckInProgress, It.IsAny<SyncJob>()), Times.Once());
        }

        [TestMethod]
        public async Task HandleEmptyJSONQuery()
        {
            _syncJob.Query = null;
            _context.Setup(x => x.GetInput<SyncJob>()).Returns(_syncJob);

            var suborchrestrator = new SubOrchestratorFunction(_loggingRespository.Object,
                                                    _telemetryClient,
                                                    _emailSenderAndRecipients.Object,
                                                    _gmmResources.Object);
            await suborchrestrator.RunSubOrchestratorAsync(_context.Object);

            _jobTriggerService.Verify(x => x.UpdateSyncJobStatusAsync(It.IsAny<SyncStatus>(), It.IsAny<SyncJob>()), Times.Once());
            _jobTriggerService.Verify(x => x.UpdateSyncJobStatusAsync(It.Is<SyncStatus>(s => s == SyncStatus.QueryNotValid), It.IsAny<SyncJob>()), Times.Once());

            _loggingRespository.Verify(x => x.LogMessageAsync(
                It.Is<LogMessage>(m => m.Message.Contains("Job query is empty for job")),
                It.IsAny<VerbosityLevel>(),
                It.IsAny<string>(),
                It.IsAny<string>()));
        }

        [TestMethod]
        public async Task ProcessValidJSONQuery()
        {
            _context.Setup(x => x.GetInput<SyncJob>()).Returns(_syncJob);

            _context.Setup(x => x.CallActivityAsync<int>(It.IsAny<string>(), It.IsAny<SyncJob>()))
                                        .Callback<string, object>(async (name, request) =>
                                        {
                                            _frequency = await CallJobTrackerFunctionAsync(request as SyncJob, DateTime.FromFileTimeUtc(0));
                                        })
                                        .ReturnsAsync(() => _frequency);

            var suborchrestrator = new SubOrchestratorFunction(_loggingRespository.Object,
                                                    _telemetryClient,
                                                    _emailSenderAndRecipients.Object,
                                                    _gmmResources.Object);
            await suborchrestrator.RunSubOrchestratorAsync(_context.Object);

            _loggingRespository.Verify(x => x.LogMessageAsync(
                It.Is<LogMessage>(m => !m.Message.Contains("JSON query is not valid") && !m.Message.Contains("Job query is empty for job")),
                It.IsAny<VerbosityLevel>(),
                It.IsAny<string>(),
                It.IsAny<string>()));

            _context.Verify(x => x.CallActivityAsync<int>(It.Is<string>(x => x == nameof(JobTrackerFunction)), It.IsAny<SyncJob>()), Times.Once());
            _context.Verify(x => x.CallActivityAsync<SyncJobGroup>(It.Is<string>(x => x == nameof(GroupNameReaderFunction)), It.IsAny<SyncJob>()), Times.Once());
            _context.Verify(x => x.CallActivityAsync(It.Is<string>(x => x == nameof(EmailSenderFunction)), It.IsAny<EmailSenderRequest>()), Times.Once());
            _context.Verify(x => x.CallActivityAsync(It.Is<string>(x => x == nameof(TopicMessageSenderFunction)), It.IsAny<SyncJob>()), Times.Once());

            _jobTriggerService.Verify(x => x.GetGroupNameAsync(It.IsAny<Guid>()), Times.Once());
            _jobTriggerService.Verify(x => x.SendEmailAsync(It.IsAny<SyncJob>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string[]>()), Times.Once());
            _jobTriggerService.Verify(x => x.SendMessageAsync(It.IsAny<SyncJob>()), Times.Once());
            _jobTriggerService.Verify(x => x.UpdateSyncJobStatusAsync(It.IsAny<SyncStatus>(), It.IsAny<SyncJob>()), Times.Once());
            _jobTriggerService.Verify(x => x.UpdateSyncJobStatusAsync(It.Is<SyncStatus>(s => s == SyncStatus.InProgress), It.IsAny<SyncJob>()), Times.Once());

            Assert.AreEqual(SyncStatus.InProgress, _syncStatus);
        }

        [TestMethod]
        public async Task ProcessIdleJob()
        {
            var topicClient = new Mock<ITopicClient>();

            _context.Setup(x => x.GetInput<SyncJob>()).Returns(_syncJob);
            _context.Setup(x => x.CallActivityAsync(It.Is<string>(x => x == nameof(TopicMessageSenderFunction)), It.IsAny<SyncJob>()))
                    .Callback<string, object>(async (name, request) =>
                    {
                        var gmmResources = new Mock<IGMMResources>();
                        var mailRepository = new Mock<IMailRepository>();
                        var jobTriggerConfig = new Mock<IJobTriggerConfig>();
                        var syncJobRepository = new Mock<ISyncJobRepository>();
                        var graphGroupRepository = new Mock<IGraphGroupRepository>();
                        var gmmAppId = new Mock<IKeyVaultSecret<IJobTriggerService>>();
                        var emailSenderAndRecipients = new Mock<IEmailSenderRecipient>();
                        var serviceBusTopicsRepository = new ServiceBusTopicsRepository(topicClient.Object);
                        var jobTriggerService = new JobTriggerService(
                                                        _loggingRespository.Object,
                                                        syncJobRepository.Object,
                                                        serviceBusTopicsRepository,
                                                        graphGroupRepository.Object,
                                                        gmmAppId.Object,
                                                        mailRepository.Object,
                                                        emailSenderAndRecipients.Object,
                                                        gmmResources.Object,
                                                        jobTriggerConfig.Object);

                        await CallTopicMessageSenderFunctionAsync(jobTriggerService: jobTriggerService);
                    });

            _context.Setup(x => x.CallActivityAsync<int>(nameof(JobTrackerFunction), It.IsAny<SyncJob>())).ReturnsAsync(2);
            var suborchrestrator = new SubOrchestratorFunction(_loggingRespository.Object,
                                                                _telemetryClient,
                                                                _emailSenderAndRecipients.Object,
                                                                _gmmResources.Object);
            await suborchrestrator.RunSubOrchestratorAsync(_context.Object);

            _loggingRespository.Verify(x => x.LogMessageAsync(
                It.Is<LogMessage>(m => !m.Message.Contains("JSON query is not valid") && !m.Message.Contains("Job query is empty for job")),
                It.IsAny<VerbosityLevel>(),
                It.IsAny<string>(),
                It.IsAny<string>()));

            _context.Verify(x => x.CallActivityAsync<int>(It.Is<string>(x => x == nameof(JobTrackerFunction)), It.IsAny<SyncJob>()), Times.Once());
            _context.Verify(x => x.CallActivityAsync<SyncJobGroup>(It.Is<string>(x => x == nameof(GroupNameReaderFunction)), It.IsAny<SyncJob>()), Times.Once());
            _context.Verify(x => x.CallActivityAsync(It.Is<string>(x => x == nameof(EmailSenderFunction)), It.IsAny<EmailSenderRequest>()), Times.Once());
            _context.Verify(x => x.CallActivityAsync(It.Is<string>(x => x == nameof(TopicMessageSenderFunction)), It.IsAny<SyncJob>()), Times.Once());

            _jobTriggerService.Verify(x => x.GetGroupNameAsync(It.IsAny<Guid>()), Times.Once());
            _jobTriggerService.Verify(x => x.SendEmailAsync(It.IsAny<SyncJob>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string[]>()), Times.Once());
            _jobTriggerService.Verify(x => x.UpdateSyncJobStatusAsync(It.IsAny<SyncStatus>(), It.IsAny<SyncJob>()), Times.Once());
            _jobTriggerService.Verify(x => x.UpdateSyncJobStatusAsync(It.Is<SyncStatus>(s => s == SyncStatus.InProgress), It.IsAny<SyncJob>()), Times.Once());

            topicClient.Verify(x => x.SendAsync(It.IsAny<Message>()), Times.Exactly(2));
            topicClient.Verify(x => x.SendAsync(It.Is<Message>(m => (string)m.UserProperties["Type"] == "SecurityGroup")), Times.Exactly(2));
            topicClient.Verify(x => x.SendAsync(It.Is<Message>(m => m.UserProperties.ContainsKey("IsDestinationPart")
                                                                    && (bool)m.UserProperties["IsDestinationPart"] == true)), Times.Once());

            Assert.AreEqual(SyncStatus.InProgress, _syncStatus);
        }
        [TestMethod]
        public async Task ProcessInProgressJob()
        {
            var topicClient = new Mock<ITopicClient>();

            var inProgressSyncJob = SampleDataHelper.CreateSampleSyncJobs(1, "SecurityGroup").First();
            inProgressSyncJob.Status = SyncStatus.InProgress.ToString();

            _context.Setup(x => x.GetInput<SyncJob>()).Returns(inProgressSyncJob);
            _context.Setup(x => x.CallActivityAsync(It.Is<string>(x => x == nameof(TopicMessageSenderFunction)), It.IsAny<SyncJob>()))
                    .Callback<string, object>(async (name, request) =>
                    {
                        var gmmResources = new Mock<IGMMResources>();
                        var mailRepository = new Mock<IMailRepository>();
                        var jobTriggerConfig = new Mock<IJobTriggerConfig>();
                        var syncJobRepository = new Mock<ISyncJobRepository>();
                        var graphGroupRepository = new Mock<IGraphGroupRepository>();
                        var gmmAppId = new Mock<IKeyVaultSecret<IJobTriggerService>>();
                        var emailSenderAndRecipients = new Mock<IEmailSenderRecipient>();
                        var serviceBusTopicsRepository = new ServiceBusTopicsRepository(topicClient.Object);
                        var jobTriggerService = new JobTriggerService(
                                                        _loggingRespository.Object,
                                                        syncJobRepository.Object,
                                                        serviceBusTopicsRepository,
                                                        graphGroupRepository.Object,
                                                        gmmAppId.Object,
                                                        mailRepository.Object,
                                                        emailSenderAndRecipients.Object,
                                                        gmmResources.Object,
                                                        jobTriggerConfig.Object);

                        await CallTopicMessageSenderFunctionAsync(jobTriggerService: jobTriggerService);
                    });

            _context.Setup(x => x.CallActivityAsync<int>(nameof(JobTrackerFunction), It.IsAny<SyncJob>())).ReturnsAsync(2);
            var suborchestrator = new SubOrchestratorFunction(_loggingRespository.Object,
                                                                _telemetryClient,
                                                                _emailSenderAndRecipients.Object,
                                                                _gmmResources.Object);
            await suborchestrator.RunSubOrchestratorAsync(_context.Object);

            _loggingRespository.Verify(x => x.LogMessageAsync(
                It.Is<LogMessage>(m => !m.Message.Contains("JSON query is not valid") && !m.Message.Contains("Job query is empty for job")),
                It.IsAny<VerbosityLevel>(),
                It.IsAny<string>(),
                It.IsAny<string>()));

            _context.Verify(x => x.CallActivityAsync<int>(It.Is<string>(x => x == nameof(JobTrackerFunction)), It.IsAny<SyncJob>()), Times.Once());
            _context.Verify(x => x.CallActivityAsync<SyncJobGroup>(It.Is<string>(x => x == nameof(GroupNameReaderFunction)), It.IsAny<SyncJob>()), Times.Once());
            _context.Verify(x => x.CallActivityAsync(It.Is<string>(x => x == nameof(EmailSenderFunction)), It.IsAny<EmailSenderRequest>()), Times.Once());
            _context.Verify(x => x.CallActivityAsync(It.Is<string>(x => x == nameof(TopicMessageSenderFunction)), It.IsAny<SyncJob>()), Times.Once());

            _jobTriggerService.Verify(x => x.GetGroupNameAsync(It.IsAny<Guid>()), Times.Once());
            _jobTriggerService.Verify(x => x.SendEmailAsync(It.IsAny<SyncJob>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string[]>()), Times.Once());
            _jobTriggerService.Verify(x => x.UpdateSyncJobStatusAsync(It.IsAny<SyncStatus>(), It.IsAny<SyncJob>()), Times.Once());
            _jobTriggerService.Verify(x => x.UpdateSyncJobStatusAsync(It.Is<SyncStatus>(s => s == SyncStatus.StuckInProgress), It.IsAny<SyncJob>()), Times.Once());

            topicClient.Verify(x => x.SendAsync(It.IsAny<Message>()), Times.Exactly(2));
            topicClient.Verify(x => x.SendAsync(It.Is<Message>(m => (string)m.UserProperties["Type"] == "SecurityGroup")), Times.Exactly(2));
            topicClient.Verify(x => x.SendAsync(It.Is<Message>(m => m.UserProperties.ContainsKey("IsDestinationPart")
                                                                    && (bool)m.UserProperties["IsDestinationPart"] == true)), Times.Once());

            Assert.AreEqual(SyncStatus.StuckInProgress, _syncStatus);
        }

        [TestMethod]
        public async Task DestinationGroupNotFound()
        {
            _context.Setup(x => x.GetInput<SyncJob>()).Returns(_syncJob);
            _context.Setup(x => x.CallActivityAsync<SyncJobGroup>(It.Is<string>(x => x == nameof(GroupNameReaderFunction)), It.IsAny<SyncJob>()))
                    .ReturnsAsync(new SyncJobGroup { Name = null, SyncJob = _syncJob });

            var suborchrestrator = new SubOrchestratorFunction(_loggingRespository.Object,
                                        _telemetryClient,
                                        _emailSenderAndRecipients.Object,
                                        _gmmResources.Object);
            await suborchrestrator.RunSubOrchestratorAsync(_context.Object);
            _context.Verify(x => x.CallActivityAsync(It.Is<string>(x => x == nameof(TelemetryTrackerFunction)), It.IsAny<TelemetryTrackerRequest>()), Times.Once());
            Assert.AreEqual(SyncStatus.DestinationGroupNotFound, _syncStatus);
        }

        [TestMethod]
        public async Task TrackTelemetry()
        {
            _context.Setup(x => x.GetInput<SyncJob>()).Returns(_syncJob);
            _context.Setup(x => x.CallActivityAsync<int>(It.IsAny<string>(), It.IsAny<SyncJob>()))
                                        .Callback<string, object>(async (name, request) =>
                                        {
                                            _frequency = await CallJobTrackerFunctionAsync(request as SyncJob, DateTime.UtcNow.AddDays(-1));
                                        })
                                        .ReturnsAsync(() => _frequency);

            _context.Setup(x => x.CallActivityAsync<bool>(It.Is<string>(x => x == nameof(GroupVerifierFunction)), It.IsAny<SyncJob>()))
                    .ReturnsAsync(false);

            var suborchrestrator = new SubOrchestratorFunction(_loggingRespository.Object,
                            _telemetryClient,
                            _emailSenderAndRecipients.Object,
                            _gmmResources.Object);
            await suborchrestrator.RunSubOrchestratorAsync(_context.Object);
            _context.Verify(x => x.CallActivityAsync(It.Is<string>(x => x == nameof(TelemetryTrackerFunction)), It.IsAny<TelemetryTrackerRequest>()), Times.Once());
            Assert.AreEqual(SyncStatus.NotOwnerOfDestinationGroup, _syncStatus);

            _syncJob.Query = "";
            _context.Setup(x => x.GetInput<SyncJob>()).Returns(_syncJob);
            suborchrestrator = new SubOrchestratorFunction(_loggingRespository.Object,
                            _telemetryClient,
                            _emailSenderAndRecipients.Object,
                            _gmmResources.Object);
            await suborchrestrator.RunSubOrchestratorAsync(_context.Object);
            _context.Verify(x => x.CallActivityAsync(It.Is<string>(x => x == nameof(TelemetryTrackerFunction)), It.IsAny<TelemetryTrackerRequest>()), Times.Exactly(2));
            Assert.AreEqual(SyncStatus.QueryNotValid, _syncStatus);
        }

        private async Task<int> CallIdleJobsTrackerFunctionAsync(SyncJob syncJob)
        {
            var jobTrackerFunction = new JobTrackerFunction(_loggingRespository.Object);
            var frequency = await jobTrackerFunction.TrackJobFrequencyAsync(syncJob);
            return frequency;
        }

        private async Task<int> CallJobTrackerFunctionAsync(SyncJob syncJob, DateTime dateTime)
        {
            syncJob.LastSuccessfulRunTime = dateTime;
            var jobTrackerFunction = new JobTrackerFunction(_loggingRespository.Object);
            return await jobTrackerFunction.TrackJobFrequencyAsync(syncJob);
        }

        private async Task CallTelemetryTrackerFunctionAsync(TelemetryTrackerRequest request)
        {
            var telemetryTrackerFunction = new TelemetryTrackerFunction(_loggingRespository.Object, _telemetryClient);
            await telemetryTrackerFunction.TrackEventAsync(request);
        }

        private async Task CallJobStatusUpdaterFunctionAsync(JobStatusUpdaterRequest request)
        {
            var jobStatusUpdaterFunction = new JobStatusUpdaterFunction(_loggingRespository.Object, _jobTriggerService.Object);
            await jobStatusUpdaterFunction.UpdateJobStatusAsync(request);
        }

        private async Task<bool> CallGroupVerifierFunctionAsync()
        {
            var groupVerifierFunction = new GroupVerifierFunction(_loggingRespository.Object, _jobTriggerService.Object, _telemetryClient);
            return await groupVerifierFunction.VerifyGroupAsync(_syncJob);
        }

        private async Task<SyncJobGroup> CallGroupNameReaderFunctionAsync()
        {
            var groupNameReaderFunction = new GroupNameReaderFunction(_loggingRespository.Object, _jobTriggerService.Object);
            return await groupNameReaderFunction.GetGroupNameAsync(_syncJob);
        }

        private async Task CallEmailSenderFunctionAsync(EmailSenderRequest request)
        {
            var emailSenderFunction = new EmailSenderFunction(_loggingRespository.Object, _jobTriggerService.Object);
            await emailSenderFunction.SendEmailAsync(request);
        }

        private async Task CallTopicMessageSenderFunctionAsync(ILoggingRepository loggingRepository = null, IJobTriggerService jobTriggerService = null)
        {
            var topicMessageSenderFunction = new TopicMessageSenderFunction(loggingRepository ?? _loggingRespository.Object, jobTriggerService ?? _jobTriggerService.Object);
            await topicMessageSenderFunction.SendMessageAsync(_syncJob);
        }
    }
}
