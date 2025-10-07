// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using DIConcreteTypes;
using Hosts.MembershipAggregator;
using MembershipAggregator.Activity.EmailSender;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Helpers;
using Models.Notifications;
using Models.ServiceBus;
using Moq;
using Polly;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class MembershipSubOrchestratorTests
    {
        private SyncJob _syncJob;
        private Group _group;
        private Channel _channel;
        private JobState _jobState;
        private BlobResult _blobResult;
        private PolicyResult<bool> _groupExists;
        private int _numberOfUsersForSourcePart;
        private int _numberOfUsersForSourcePartOne;
        private int _numberOfUsersForSourcePartTwo;
        private JobTrackerEntity _jobTrackerEntity;
        private int _numberOfUsersForDestinationPart;
        private Dictionary<string, int> _membersPerFile;
        private DeltaCalculatorService _deltaCalculatorService;
        private DeltaCalculatorResponse _deltaCalculatorResponse;
        private (string FilePath, string Content) _downloaderResponse;
        private MembershipExtractionResponse _membershipExtractionResponse;
        private MembershipSubOrchestratorRequest _membershipSubOrchestratorRequest;
        private TelemetryClient _telemetryClient;
        private SyncJobGroup _groupInformation;
        private MultiLaneConfig _multiLaneConfig;

        private Mock<IDryRunValue> _dryRun;
        private Mock<IGMMResources> _gmmResources;
        private Mock<IGraphAPIService> _graphAPIService;
        private Mock<IThresholdConfig> _thresholdConfig;
        private Mock<IThresholdNotificationConfig> _thresholdNotificationConfig;
        private Mock<ILoggingRepository> _loggingRepository;
        private Mock<IDatabaseSyncJobsRepository> _syncJobRepository;
        private Mock<IDatabaseGroupsRepository> _groupsRepository;
        private Mock<IDatabaseChannelsRepository> _channelsRepository;
        private Mock<IEmailSenderRecipient> _emailSenderRecipient;
        private Mock<IDurableOrchestrationContext> _durableContext;
        private Mock<IBlobStorageRepository> _blobStorageRepository;
        private Mock<ILocalizationRepository> _localizationRepository;
        private Mock<INotificationRepository> _notificationRepository;
        private Mock<IServiceBusQueueRepository> _serviceBusQueueRepository;
        private Mock<IServiceBusQueueRepository> _notificationsQueueRepository;
        [TestInitialize]
        public void SetupTest()
        {
            _thresholdConfig = new Mock<IThresholdConfig>();
            _thresholdNotificationConfig = new Mock<IThresholdNotificationConfig>();
            _loggingRepository = new Mock<ILoggingRepository>();
            _syncJobRepository = new Mock<IDatabaseSyncJobsRepository>();
            _groupsRepository = new Mock<IDatabaseGroupsRepository>();
            _channelsRepository = new Mock<IDatabaseChannelsRepository>();
            _durableContext = new Mock<IDurableOrchestrationContext>();
            _blobStorageRepository = new Mock<IBlobStorageRepository>();
            _graphAPIService = new Mock<IGraphAPIService>();
            _notificationRepository = new Mock<INotificationRepository>();
            _serviceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            _notificationsQueueRepository = new Mock<IServiceBusQueueRepository>();
            _multiLaneConfig = new MultiLaneConfig
            {
                IsEnabled = true,
                Small = 400
            };

            _dryRun = new Mock<IDryRunValue>();
            _telemetryClient = new TelemetryClient(new TelemetryConfiguration());

            _deltaCalculatorService = new DeltaCalculatorService
                                            (
                                                _syncJobRepository.Object,
                                                _groupsRepository.Object,
                                                _channelsRepository.Object,
                                                _loggingRepository.Object,
                                                _graphAPIService.Object,
                                                _dryRun.Object,
                                                _thresholdConfig.Object,
                                                _thresholdNotificationConfig.Object,
                                                _notificationRepository.Object,
                                                _notificationsQueueRepository.Object,
                                                _telemetryClient
                                            );


            _deltaCalculatorResponse = null;
            _membershipExtractionResponse = null;
            _numberOfUsersForSourcePart = 10;
            _numberOfUsersForSourcePartOne = 10;
            _numberOfUsersForSourcePartTwo = 10;
            _numberOfUsersForDestinationPart = 10;
            _membersPerFile = new Dictionary<string, int>();
            _groupExists = PolicyResult<bool>.Successful(true, new Context());

            var sourceGroupIdOne = Guid.NewGuid();
            var sourceGroupIdTwo = Guid.NewGuid();
            var targetGroupId = Guid.NewGuid();

			_syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                ThresholdPercentageForAdditions = 80,
                ThresholdPercentageForRemovals = 20,
                LastRunTime = DateTime.UtcNow.AddDays(-1),
                Requestor = "user@domail.com",
                RunId = Guid.NewGuid(),
                ThresholdViolations = 0,
                MembershipType = "GroupMembership",
                Query = $"[{{\"type\":\"GroupMembership\",\"source\":\"{sourceGroupIdOne}\"}},{{\"type\":\"GroupMembership\",\"source\":\"{sourceGroupIdTwo}\"}}]"
            };
            _group = new Group
            {
                GroupId = targetGroupId,
                SyncJobId = _syncJob.Id
            };
            _channel = new Channel
            {
                ChannelId = "some-channel",
                GroupId = targetGroupId,
                SyncJobId = _syncJob.Id
            };
            _groupInformation = new SyncJobGroup
            {
                SyncJob = _syncJob,
                Name = "groupName"

            };
            _membershipSubOrchestratorRequest = new MembershipSubOrchestratorRequest
            {
                EntityId = new EntityId(),
                SyncJob = _syncJob,
                GroupId = targetGroupId
            };

            _jobState = new JobState
            {
                CompletedParts = new List<string>
                {
                    "http://file-path-1",
                    "http://file-path-2",
                    "http://file-path-3"
                },
                DestinationPart = "http://file-path-3",
                TotalParts = 3
            };

            _jobTrackerEntity = new JobTrackerEntity
            {
                JobState = _jobState
            };

            _downloaderResponse = (null, null);

            _blobStorageRepository.Setup(x => x.DownloadFileAsync(It.Is<string>(x => x.StartsWith("http://file-path"))))
                                    .Callback<string>(path =>
                                    {
                                        var userCount = path == _jobState.DestinationPart
                                                                ? _numberOfUsersForDestinationPart
                                                                : _numberOfUsersForSourcePart;

                                        var content = new GroupMembership
                                        {
                                            SyncJobId = _syncJob?.Id ?? Guid.Empty,
                                            MembershipObtainerDryRunEnabled = false,
                                            RunId = _syncJob?.RunId.Value ?? Guid.Empty,
                                            Exclusionary = false,
                                            SourceMembers = Enumerable.Range(0, userCount)
                                                                         .Select(x => new AzureADUser { ObjectId = Guid.NewGuid() })
                                                                         .ToList(),
                                            Destination = new AzureADGroup
                                            {
                                                ObjectId = _syncJob != null
                                                                ? _group.GroupId
                                                                : Guid.Empty
                                            }
                                        };

                                        _blobResult = new BlobResult
                                        {
                                            BlobStatus = BlobStatus.Found,
                                            Content = TextCompressor.Compress(JsonSerializer.Serialize(content))
                                        };
                                    })
                                    .ReturnsAsync(() => _blobResult);

            _blobStorageRepository.Setup(x => x.DownloadFileAsync(It.Is<string>(x => !x.StartsWith("http://file-path"))))
                        .Callback<string>(path =>
                        {
                            var userCount = _membersPerFile[path];
                            var content = new GroupMembership
                            {
                                SyncJobId = _syncJob?.Id ?? Guid.Empty,
                                MembershipObtainerDryRunEnabled = false,
                                RunId = _syncJob?.RunId.Value ?? Guid.Empty,
                                Exclusionary = false,
                                SourceMembers = Enumerable.Range(0, userCount)
                                                             .Select(x => new AzureADUser { ObjectId = Guid.NewGuid() })
                                                             .ToList(),
                                Destination = new AzureADGroup
                                {
                                    ObjectId = _syncJob != null
                                                    ? _group.GroupId
                                                    : Guid.Empty
                                }
                            };

                            _blobResult = new BlobResult
                            {
                                BlobStatus = BlobStatus.Found,
                                Content = TextCompressor.Compress(JsonSerializer.Serialize(content))
                            };
                        })
                        .ReturnsAsync(() => _blobResult);

            var owners = new List<AzureADUser>
            {
                { new AzureADUser { ObjectId = Guid.NewGuid(), Mail = "mail_1@mail.com" } },
                { new AzureADUser { ObjectId = Guid.NewGuid(), Mail = "mail_2@mail.com" } },
                { new AzureADUser { ObjectId = Guid.NewGuid(), Mail = "mail_3@mail.com" } }
            };

            _syncJobRepository.Setup(x => x.GetSyncJobAsync(It.IsAny<Guid>()))
                              .ReturnsAsync(() => _syncJob);

            _groupsRepository.Setup(x => x.GetGroupUsingSyncJobIdAsync(It.IsAny<Guid>()))
                              .ReturnsAsync(() => _group);

            _channelsRepository.Setup(x => x.GetChannelUsingSyncJobIdAsync(It.IsAny<Guid>()))
                              .ReturnsAsync(() => _channel);

            _graphAPIService.Setup(x => x.GroupExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                            .ReturnsAsync(() => _groupExists);

            _graphAPIService.Setup(x => x.GetGroupOwnersAsync(It.IsAny<Guid>(), It.IsAny<int>()))
                            .ReturnsAsync(owners);

            _graphAPIService.Setup(x => x.GetGroupNameAsync(It.IsAny<Guid>()))
                            .ReturnsAsync(() => "GroupName");

			_durableContext.Setup(x => x.CallActivityAsync<SyncJobGroup>(It.Is<string>(s => s == nameof(GroupNameReaderFunction)), It.IsAny<GroupNameReaderRequest>()))
			   .ReturnsAsync(_groupInformation);

			_durableContext.Setup(x => x.GetInput<MembershipSubOrchestratorRequest>())
                            .Returns(() => _membershipSubOrchestratorRequest);

            _durableContext.Setup(x => x.CreateEntityProxy<IJobTracker>(It.IsAny<EntityId>()))
                            .Returns(() => _jobTrackerEntity);

            _durableContext.Setup(x => x.CallActivityAsync<MembershipExtractionResponse>(It.Is<string>(x => x == nameof(MembershipExtractionFunction)), It.IsAny<MembershipExtractionRequest>()))
                            .Returns<string, object>(async (name, request) =>
                            {
                                _membershipExtractionResponse = await CallMembershipExtractionFunctionAsync(request as MembershipExtractionRequest);
                                return _membershipExtractionResponse;
                            });

            _durableContext.Setup(x => x.CallActivityAsync<DeltaCalculatorResponse>(It.Is<string>(x => x == nameof(DeltaCalculatorFunction)), It.IsAny<DeltaCalculatorRequest>()))
                            .Returns<string, object>(async (name, request) =>
                            {
                                _deltaCalculatorResponse = await CallDeltaCalculatorFunctionAsync(request as DeltaCalculatorRequest);
                                return _deltaCalculatorResponse;
                            });

            _durableContext.Setup(x => x.CallActivityAsync(It.Is<string>(x => x == nameof(FileUploaderFunction)), It.IsAny<FileUploaderRequest>()))
                            .Callback<string, object>(async (name, request) =>
                            {
                                await CallFileUploaderFunctionAsync(request as FileUploaderRequest);
                            });

            _durableContext.Setup(x => x.CallActivityAsync(It.Is<string>(x => x == nameof(FileDeleterFunction)), It.IsAny<FileDeleterRequest>()))
                            .Callback<string, object>(async (name, request) =>
                            {
                                await CallFileDeleterFunctionAsync(request as FileDeleterRequest);
                            });

            _durableContext.Setup(x => x.CallActivityAsync(It.Is<string>(x => x == nameof(LoggerFunction)), It.IsAny<LoggerRequest>()))
                            .Callback<string, object>(async (name, request) =>
                            {
                                await CallLoggerFunctionAsync(request as LoggerRequest);
                            });

            _durableContext.Setup(x => x.CallActivityAsync(It.Is<string>(x => x == nameof(JobStatusUpdaterFunction)), It.IsAny<JobStatusUpdaterRequest>()))
                            .Callback<string, object>(async (name, request) =>
                            {
                                await CallJobStatusUpdaterFunctionAsync(request as JobStatusUpdaterRequest);
                            });
			_durableContext.Setup(x => x.CallActivityAsync(It.Is<string>(x => x == nameof(EmailSenderFunction)), It.IsAny<EmailSenderRequest>()))
				.Callback<string, object>(async (name, request) =>
				{
					await CallEmailSenderFunctionAsync(request as EmailSenderRequest);
				});

			_durableContext.Setup(x => x.CallActivityAsync<SyncJob>(nameof(JobReaderFunction), It.IsAny<JobReaderRequest>())).ReturnsAsync(() => _syncJob);
        }

        [TestMethod]
        public async Task ProcessInitialJobSyncAsync()
        {
            _syncJob.LastRunTime = SqlDateTime.MinValue.Value;

            var orchestratorFunction = new MembershipSubOrchestratorFunction(_thresholdConfig.Object, _graphAPIService.Object, _telemetryClient, _multiLaneConfig);
            var response = await orchestratorFunction.RunMembershipSubOrchestratorFunctionAsync(_durableContext.Object);

            Assert.IsNotNull(response.FilePath);
            Assert.AreEqual(MembershipDeltaStatus.Ok, response.MembershipDeltaStatus);

            _blobStorageRepository.Verify(x => x.DownloadFileAsync(It.IsAny<string>()), Times.Exactly(3));
            _blobStorageRepository.Verify(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()), Times.Once());
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("Uploaded membership file")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Once());
            _syncJobRepository.Verify(x => x.UpdateSyncJobsAsync(It.IsAny<IEnumerable<SyncJob>>(), It.IsAny<SyncStatus>()), Times.Never());
        }

        [TestMethod]
        public async Task HitAdditionsThresholdTestAsync()
        {
            var currentThresholdViolations = 0;
            _thresholdConfig.Setup(x => x.NumberOfThresholdViolationsToDisableJob).Returns(5);
            _thresholdConfig.Setup(x => x.NumberOfThresholdViolationsToNotify).Returns(2);
            _numberOfUsersForDestinationPart = 5;
            _syncJob.ThresholdViolations = currentThresholdViolations;


            var orchestratorFunction = new MembershipSubOrchestratorFunction(_thresholdConfig.Object, _graphAPIService.Object, _telemetryClient, _multiLaneConfig);
            var response = await orchestratorFunction.RunMembershipSubOrchestratorFunctionAsync(_durableContext.Object);

            Assert.IsNull(response.FilePath);
            Assert.AreEqual(MembershipDeltaStatus.ThresholdExceeded, response.MembershipDeltaStatus);

            _blobStorageRepository.Verify(x => x.DownloadFileAsync(It.IsAny<string>()), Times.Exactly(3));
            _blobStorageRepository.Verify(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()), Times.Once());
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("Membership increase in")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Once());
            _syncJobRepository.Verify(x => x.UpdateSyncJobsAsync(
                                                                    It.IsAny<IEnumerable<SyncJob>>(),
                                                                    It.Is<SyncStatus?>(x => x == SyncStatus.Idle)
                                                                )
                                                                    , Times.Once());
            _notificationsQueueRepository.Verify(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>()), Times.Exactly(0));

        }

        [TestMethod]
        public async Task IgnoreThresholdOnceTestAsync()
        {
            var currentThresholdViolations = 1;
            _thresholdConfig.Setup(x => x.NumberOfThresholdViolationsToDisableJob).Returns(5);
            _numberOfUsersForDestinationPart = 5;
            _syncJob.ThresholdViolations = currentThresholdViolations;
            _syncJob.IgnoreThresholdOnce = true;

            var orchestratorFunction = new MembershipSubOrchestratorFunction(_thresholdConfig.Object, _graphAPIService.Object, _telemetryClient, _multiLaneConfig);
            var response = await orchestratorFunction.RunMembershipSubOrchestratorFunctionAsync(_durableContext.Object);

            Assert.AreEqual(MembershipDeltaStatus.Ok, response.MembershipDeltaStatus);
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("Going to sync the job")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Once());
        }

        [TestMethod]
        public async Task AllowEmptyDestinationTestAsync()
        {
            _numberOfUsersForDestinationPart = 0;
            _syncJob.AllowEmptyDestination = true;

            var orchestratorFunction = new MembershipSubOrchestratorFunction(_thresholdConfig.Object, _graphAPIService.Object, _telemetryClient, _multiLaneConfig);
            var response = await orchestratorFunction.RunMembershipSubOrchestratorFunctionAsync(_durableContext.Object);

            Assert.AreEqual(MembershipDeltaStatus.Ok, response.MembershipDeltaStatus);
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("Going to sync the job")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Once());
        }

        [TestMethod]
        public async Task HitRemovalThresholdTestAsync()
        {
            _thresholdConfig.Setup(x => x.NumberOfThresholdViolationsToDisableJob).Returns(5);
            _thresholdConfig.Setup(x => x.NumberOfThresholdViolationsToNotify).Returns(2);

            _numberOfUsersForSourcePart = 5;
            _syncJob.ThresholdViolations = 1;

            var orchestratorFunction = new MembershipSubOrchestratorFunction(_thresholdConfig.Object, _graphAPIService.Object, _telemetryClient, _multiLaneConfig);
            var response = await orchestratorFunction.RunMembershipSubOrchestratorFunctionAsync(_durableContext.Object);

            Assert.IsNull(response.FilePath);
            Assert.AreEqual(MembershipDeltaStatus.ThresholdExceeded, response.MembershipDeltaStatus);

            _blobStorageRepository.Verify(x => x.DownloadFileAsync(It.IsAny<string>()), Times.Exactly(3));
            _blobStorageRepository.Verify(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()), Times.Once());
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("Membership decrease in")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Once());
            _syncJobRepository.Verify(x => x.UpdateSyncJobsAsync(
                                                                    It.IsAny<IEnumerable<SyncJob>>(),
                                                                    It.Is<SyncStatus?>(x => x == SyncStatus.Idle)
                                                                )
                                                                    , Times.Once());

            _notificationsQueueRepository.Verify(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>()), Times.Exactly(1));
        }

        [TestMethod]
        public async Task HitMaxAddsRemovesThresholdViolationsTestAsync()
        {
            _thresholdConfig.Setup(x => x.NumberOfThresholdViolationsToDisableJob).Returns(5);
            _thresholdConfig.Setup(x => x.NumberOfThresholdViolationsToNotify).Returns(5);

            _syncJob.ThresholdViolations = 4;

            var orchestratorFunction = new MembershipSubOrchestratorFunction(_thresholdConfig.Object, _graphAPIService.Object, _telemetryClient, _multiLaneConfig);
            var response = await orchestratorFunction.RunMembershipSubOrchestratorFunctionAsync(_durableContext.Object);

            Assert.IsNull(response.FilePath);
            Assert.AreEqual(MembershipDeltaStatus.ThresholdExceeded, response.MembershipDeltaStatus);

            _blobStorageRepository.Verify(x => x.DownloadFileAsync(It.IsAny<string>()), Times.Exactly(3));
            _blobStorageRepository.Verify(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()), Times.Once());
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("Membership decrease in")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Once());
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("Membership increase in")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Once());
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("Threshold exceeded")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Once());

            _notificationsQueueRepository.Verify(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>()), Times.Once());

            _syncJobRepository.Verify(x => x.UpdateSyncJobsAsync(
                                                                    It.IsAny<IEnumerable<SyncJob>>(),
                                                                    It.Is<SyncStatus?>(x => x == SyncStatus.ThresholdExceeded)
                                                                )
                                                                    , Times.Once());
        }

        [TestMethod]
        public async Task HitMaxAddsThresholdViolationsTestAsync()
        {
            _thresholdConfig.Setup(x => x.NumberOfThresholdViolationsToDisableJob).Returns(5);
            _thresholdConfig.Setup(x => x.NumberOfThresholdViolationsToNotify).Returns(5);

            _syncJob.ThresholdViolations = 4;
            _numberOfUsersForDestinationPart = 0;

            var orchestratorFunction = new MembershipSubOrchestratorFunction(_thresholdConfig.Object, _graphAPIService.Object, _telemetryClient, _multiLaneConfig);
            var response = await orchestratorFunction.RunMembershipSubOrchestratorFunctionAsync(_durableContext.Object);

            Assert.IsNull(response.FilePath);
            Assert.AreEqual(MembershipDeltaStatus.ThresholdExceeded, response.MembershipDeltaStatus);

            _blobStorageRepository.Verify(x => x.DownloadFileAsync(It.IsAny<string>()), Times.Exactly(3));
            _blobStorageRepository.Verify(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()), Times.Once());
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("Membership increase in")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Once());
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("Threshold exceeded")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Once());

            _notificationsQueueRepository.Verify(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>()), Times.Once());

            _syncJobRepository.Verify(x => x.UpdateSyncJobsAsync(
                                                                    It.IsAny<IEnumerable<SyncJob>>(),
                                                                    It.Is<SyncStatus?>(x => x == SyncStatus.ThresholdExceeded)
                                                                )
                                                                    , Times.Once());
        }

        [TestMethod]
        public async Task HitMaxRemovesThresholdViolationsTestAsync()
        {
            _thresholdConfig.Setup(x => x.NumberOfThresholdViolationsToDisableJob).Returns(5);
            _thresholdConfig.Setup(x => x.NumberOfThresholdViolationsToNotify).Returns(5);

            _syncJob.ThresholdViolations = 4;
            _numberOfUsersForSourcePart = 1;

            var orchestratorFunction = new MembershipSubOrchestratorFunction(_thresholdConfig.Object, _graphAPIService.Object, _telemetryClient, _multiLaneConfig);
            var response = await orchestratorFunction.RunMembershipSubOrchestratorFunctionAsync(_durableContext.Object);

            Assert.IsNull(response.FilePath);
            Assert.AreEqual(MembershipDeltaStatus.ThresholdExceeded, response.MembershipDeltaStatus);

            _blobStorageRepository.Verify(x => x.DownloadFileAsync(It.IsAny<string>()), Times.Exactly(3));
            _blobStorageRepository.Verify(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()), Times.Once());
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("Membership decrease in")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Once());
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("Threshold exceeded")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Once());

            _notificationsQueueRepository.Verify(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>()), Times.Once());

            _syncJobRepository.Verify(x => x.UpdateSyncJobsAsync(
                                                                    It.IsAny<IEnumerable<SyncJob>>(),
                                                                    It.Is<SyncStatus?>(x => x == SyncStatus.ThresholdExceeded)
                                                                )
                                                                    , Times.Once());
        }

        [TestMethod]
        public async Task DryRunTestAsync()
        {
            _syncJob.LastRunTime = SqlDateTime.MinValue.Value;

            _dryRun.Setup(x => x.DryRunEnabled).Returns(true);
            _deltaCalculatorService = new DeltaCalculatorService
                                (
                                    _syncJobRepository.Object,
                                    _groupsRepository.Object,
                                    _channelsRepository.Object,
                                    _loggingRepository.Object,
                                    _graphAPIService.Object,
                                    _dryRun.Object,
                                    _thresholdConfig.Object,
                                    _thresholdNotificationConfig.Object,
                                    _notificationRepository.Object,
                                    _notificationsQueueRepository.Object,
                                    _telemetryClient
                                );

            var orchestratorFunction = new MembershipSubOrchestratorFunction(_thresholdConfig.Object, _graphAPIService.Object, _telemetryClient, _multiLaneConfig);
            var response = await orchestratorFunction.RunMembershipSubOrchestratorFunctionAsync(_durableContext.Object);

            Assert.IsNull(response.FilePath);
            Assert.AreEqual(MembershipDeltaStatus.DryRun, response.MembershipDeltaStatus);

            _blobStorageRepository.Verify(x => x.DownloadFileAsync(It.IsAny<string>()), Times.Exactly(3));
            _blobStorageRepository.Verify(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()), Times.Never());
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("A Dry Run Synchronization for")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Once());
            _syncJobRepository.Verify(x => x.UpdateSyncJobsAsync(
                                                                    It.IsAny<IEnumerable<SyncJob>>(),
                                                                    It.Is<SyncStatus?>(x => x == SyncStatus.Idle)
                                                                )
                                                                    , Times.Once());
        }

        [TestMethod]
        public async Task TestDeltaCalculatorJobNotFoundErrorAsync()
        {
            _syncJob = null;
            _deltaCalculatorService = new DeltaCalculatorService
                                (
                                    _syncJobRepository.Object,
                                    _groupsRepository.Object,
                                    _channelsRepository.Object,
                                    _loggingRepository.Object,
                                    _graphAPIService.Object,
                                    _dryRun.Object,
                                    _thresholdConfig.Object,
                                    _thresholdNotificationConfig.Object,
                                    _notificationRepository.Object,
                                    _notificationsQueueRepository.Object,
                                    _telemetryClient
                                );

            var orchestratorFunction = new MembershipSubOrchestratorFunction(_thresholdConfig.Object, _graphAPIService.Object, _telemetryClient, _multiLaneConfig);
            var response = await orchestratorFunction.RunMembershipSubOrchestratorFunctionAsync(_durableContext.Object);

            Assert.IsNull(response.FilePath);
            Assert.AreEqual(MembershipDeltaStatus.Error, response.MembershipDeltaStatus);

            _blobStorageRepository.Verify(x => x.DownloadFileAsync(It.IsAny<string>()), Times.Exactly(3));
            _blobStorageRepository.Verify(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()), Times.Never());
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("Sync job : Id")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Once());
        }

        [TestMethod]
        public async Task TestDeltaCalculatorGroupNotFoundErrorAsync()
        {
            _groupExists = PolicyResult<bool>.Successful(false, new Context());
            _deltaCalculatorService = new DeltaCalculatorService
                                (
                                    _syncJobRepository.Object,
                                    _groupsRepository.Object,
                                    _channelsRepository.Object,
                                    _loggingRepository.Object,
                                    _graphAPIService.Object,
                                    _dryRun.Object,
                                    _thresholdConfig.Object,
                                    _thresholdNotificationConfig.Object,
                                    _notificationRepository.Object,
                                    _notificationsQueueRepository.Object,
                                    _telemetryClient
                                );

            var orchestratorFunction = new MembershipSubOrchestratorFunction(_thresholdConfig.Object, _graphAPIService.Object, _telemetryClient, _multiLaneConfig);
            var response = await orchestratorFunction.RunMembershipSubOrchestratorFunctionAsync(_durableContext.Object);

            Assert.IsNull(response.FilePath);
            Assert.AreEqual(MembershipDeltaStatus.Error, response.MembershipDeltaStatus);

            _blobStorageRepository.Verify(x => x.DownloadFileAsync(It.IsAny<string>()), Times.Exactly(3));
            _blobStorageRepository.Verify(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()), Times.Never());
            _loggingRepository.Verify(x => x.LogMessageAsync(
                                                            It.Is<LogMessage>(m => m.Message.Contains($"destination group") && m.Message.Contains("doesn't exist")),
                                                            VerbosityLevel.INFO,
                                                            It.IsAny<string>(), It.IsAny<string>())
                                                        , Times.Once());
        }

        [TestMethod]
        public async Task ProcessMembershipFromFilesForLargeSyncsAsync()
        {
            _syncJob.ThresholdPercentageForAdditions = -1;
            _syncJob.ThresholdPercentageForRemovals = -1;
            _numberOfUsersForSourcePart = 50000;

            _membersPerFile.Add(GenerateFileName(_syncJob, _group.GroupId, "SourceMembership", _durableContext.Object), 100000);
            _membersPerFile.Add(GenerateFileName(_syncJob, _group.GroupId, "DestinationMembership", _durableContext.Object), 0);

            var orchestratorFunction = new MembershipSubOrchestratorFunction(_thresholdConfig.Object, _graphAPIService.Object, _telemetryClient, _multiLaneConfig);
            var response = await orchestratorFunction.RunMembershipSubOrchestratorFunctionAsync(_durableContext.Object);

            _blobStorageRepository.Verify(x => x.UploadFileAsync(It.Is<string>(x => x.Contains("SourceMembership")),
                                                                 It.IsAny<string>(),
                                                                 It.IsAny<Dictionary<string, string>>()), Times.Once());

            _blobStorageRepository.Verify(x => x.UploadFileAsync(It.Is<string>(x => x.Contains("DestinationMembership")),
                                                                 It.IsAny<string>(),
                                                                 It.IsAny<Dictionary<string, string>>()), Times.Once());

            _blobStorageRepository.Verify(x => x.DownloadFileAsync(It.Is<string>(x => x.Contains("SourceMembership"))), Times.Once());
            _blobStorageRepository.Verify(x => x.DownloadFileAsync(It.Is<string>(x => x.Contains("DestinationMembership"))), Times.Once());


            _blobStorageRepository.Verify(x => x.DeleteFileAsync(It.Is<string>(x => x.Contains("SourceMembership"))), Times.Once());
            _blobStorageRepository.Verify(x => x.DeleteFileAsync(It.Is<string>(x => x.Contains("DestinationMembership"))), Times.Once());

            _blobStorageRepository.Verify(x => x.UploadFileAsync(It.Is<string>(x => x.Contains("Aggregated")),
                                                                 It.IsAny<string>(),
                                                                 It.IsAny<Dictionary<string, string>>()), Times.Once());

            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("Reading from blobs")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Once());
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("Uploaded membership file")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Once());
            _syncJobRepository.Verify(x => x.UpdateSyncJobsAsync(It.IsAny<IEnumerable<SyncJob>>(), It.IsAny<SyncStatus?>()), Times.Never());

            Assert.IsNotNull(response.FilePath);
            Assert.AreEqual(MembershipDeltaStatus.Ok, response.MembershipDeltaStatus);
        }

        [TestMethod]
        public async Task ProcessExclusionaryMembershipAsync()
        {
            _syncJob.ThresholdPercentageForAdditions = -1;
            _syncJob.ThresholdPercentageForRemovals = -1;
            _numberOfUsersForSourcePart = 50000;

            _blobStorageRepository.Setup(x => x.DownloadFileAsync(It.Is<string>(x => x.StartsWith("http://file-path"))))
                                    .Callback<string>(path =>
                                    {
                                        var userCount = path == _jobState.DestinationPart
                                                                ? _numberOfUsersForDestinationPart
                                                                : _numberOfUsersForSourcePart;

                                        var content = new GroupMembership
                                        {
                                            SyncJobId = _syncJob?.Id ?? Guid.Empty,
                                            MembershipObtainerDryRunEnabled = false,
                                            RunId = _syncJob?.RunId.Value ?? Guid.Empty,
                                            Exclusionary = true,
                                            SourceMembers = Enumerable.Range(0, userCount)
                                                                         .Select(x => new AzureADUser { ObjectId = Guid.NewGuid() })
                                                                         .ToList(),
                                            Destination = new AzureADGroup
                                            {
                                                ObjectId = _syncJob != null
                                                                ? _group.GroupId
                                                                : Guid.Empty
                                            }
                                        };

                                        _blobResult = new BlobResult
                                        {
                                            BlobStatus = BlobStatus.Found,
                                            Content = TextCompressor.Compress(JsonSerializer.Serialize(content))
                                        };
                                    })
                                    .ReturnsAsync(() => _blobResult);

            var orchestratorFunction = new MembershipSubOrchestratorFunction(_thresholdConfig.Object, _graphAPIService.Object, _telemetryClient, _multiLaneConfig);
            var response = await orchestratorFunction.RunMembershipSubOrchestratorFunctionAsync(_durableContext.Object);
            Assert.AreEqual(0, response.ProjectedMemberCount);
        }

        [TestMethod]
        public async Task ProcessExclusionaryAndInclusionaryMembershipAsync()
        {
            _syncJob.ThresholdPercentageForAdditions = -1;
            _syncJob.ThresholdPercentageForRemovals = -1;
            _numberOfUsersForSourcePartOne = 50000;
            _numberOfUsersForSourcePartTwo = 25000;

            _blobStorageRepository.Setup(x => x.DownloadFileAsync(It.Is<string>(x => x.StartsWith("http://file-path-1"))))
                                    .Callback<string>(path =>
                                    {
                                        var userCount = path == _jobState.DestinationPart
                                                                ? _numberOfUsersForDestinationPart
                                                                : _numberOfUsersForSourcePartOne;

                                        var content = new GroupMembership
                                        {
                                            SyncJobId = _syncJob?.Id ?? Guid.Empty,
                                            MembershipObtainerDryRunEnabled = false,
                                            RunId = _syncJob?.RunId.Value ?? Guid.Empty,
                                            Exclusionary = false,
                                            SourceMembers = Enumerable.Range(0, userCount)
                                                                         .Select(x => new AzureADUser { ObjectId = Guid.NewGuid() })
                                                                         .ToList(),
                                            Destination = new AzureADGroup
                                            {
                                                ObjectId = _syncJob != null
                                                                ? _group.GroupId
                                                                : Guid.Empty
                                            }
                                        };

                                        _blobResult = new BlobResult
                                        {
                                            BlobStatus = BlobStatus.Found,
                                            Content = TextCompressor.Compress(JsonSerializer.Serialize(content))
                                        };
                                    })
                                    .ReturnsAsync(() => _blobResult);

            _blobStorageRepository.Setup(x => x.DownloadFileAsync(It.Is<string>(x => x.StartsWith("http://file-path-2"))))
                                   .Callback<string>(path =>
                                   {
                                       var userCount = path == _jobState.DestinationPart
                                                               ? _numberOfUsersForDestinationPart
                                                               : _numberOfUsersForSourcePartTwo;

                                       var content = new GroupMembership
                                       {
                                           SyncJobId = _syncJob?.Id ?? Guid.Empty,
                                           MembershipObtainerDryRunEnabled = false,
                                           RunId = _syncJob?.RunId.Value ?? Guid.Empty,
                                           Exclusionary = true,
                                           SourceMembers = Enumerable.Range(0, userCount)
                                                                        .Select(x => new AzureADUser { ObjectId = Guid.NewGuid() })
                                                                        .ToList(),
                                           Destination = new AzureADGroup
                                           {
                                               ObjectId = _syncJob != null
                                                               ? _group.GroupId
                                                               : Guid.Empty
                                           }
                                       };

                                       _blobResult = new BlobResult
                                       {
                                           BlobStatus = BlobStatus.Found,
                                           Content = TextCompressor.Compress(JsonSerializer.Serialize(content))
                                       };
                                   })
                                   .ReturnsAsync(() => _blobResult);

            var orchestratorFunction = new MembershipSubOrchestratorFunction(_thresholdConfig.Object, _graphAPIService.Object, _telemetryClient, _multiLaneConfig);
            var response = await orchestratorFunction.RunMembershipSubOrchestratorFunctionAsync(_durableContext.Object);
            Assert.AreEqual(50000, response.ProjectedMemberCount);
        }

        [TestMethod]
        public async Task SendThresholdNotificationIfEnabledAsync()
        {
            _syncJob.ThresholdViolations = 2;
            _thresholdNotificationConfig.Setup(x => x.IsThresholdNotificationEnabled).Returns(true);
            _thresholdConfig.Setup(x => x.NumberOfThresholdViolationsFollowUps).Returns(3);
            _notificationsQueueRepository.Setup(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>()))
            .Returns(Task.CompletedTask);

            var orchestratorFunction = new MembershipSubOrchestratorFunction(_thresholdConfig.Object, _graphAPIService.Object, _telemetryClient, _multiLaneConfig);
            var response = await orchestratorFunction.RunMembershipSubOrchestratorFunctionAsync(_durableContext.Object);

            Assert.AreEqual(MembershipDeltaStatus.ThresholdExceeded, response.MembershipDeltaStatus);

            _notificationsQueueRepository.Verify(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>()), Times.Once());
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.Contains("Sent message")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Once());
        }

        [TestMethod]
        public async Task TestAllowEmptyDestinationIsFalseAsync()
        {
            _syncJob.AllowEmptyDestination = false;
            _numberOfUsersForSourcePart = 0;

            var orchestratorFunction = new MembershipSubOrchestratorFunction(_thresholdConfig.Object, _graphAPIService.Object, _telemetryClient, _multiLaneConfig);
            var response = await orchestratorFunction.RunMembershipSubOrchestratorFunctionAsync(_durableContext.Object);

            Assert.IsNull(response.FilePath);
            Assert.AreEqual(MembershipDeltaStatus.Error, response.MembershipDeltaStatus);
            _graphAPIService.Verify(api => api.SendEmailAsync(
                          _syncJob, NotificationMessageType.NoDataNotification, It.IsAny<string[]>()),
                          Times.Once());
        }

        [TestMethod]
        public async Task TestAllowEmptyDestinationIsTrueInitialSyncAsync()
        {
            _syncJob.AllowEmptyDestination = true;
            _numberOfUsersForSourcePart = 0;
            _numberOfUsersForDestinationPart = 0;

            var orchestratorFunction = new MembershipSubOrchestratorFunction(_thresholdConfig.Object, _graphAPIService.Object, _telemetryClient, _multiLaneConfig);
            var response = await orchestratorFunction.RunMembershipSubOrchestratorFunctionAsync(_durableContext.Object);

            Assert.AreEqual(MembershipDeltaStatus.NoChanges, response.MembershipDeltaStatus);
        }

        [TestMethod]
        public async Task TestNoMembershipChangesAsync()
        {
            _numberOfUsersForSourcePart = 5;
            _numberOfUsersForDestinationPart = 10;

            var usersFromQueryPart1 = Enumerable.Range(0, _numberOfUsersForSourcePart)
                                                .Select(x => new AzureADUser { ObjectId = Guid.NewGuid() })
                                                .ToList();

            var usersFromQueryPart2 = Enumerable.Range(0, _numberOfUsersForSourcePart)
                                                .Select(x => new AzureADUser { ObjectId = Guid.NewGuid() })
                                                .ToList();

            var usersFromDestination = usersFromQueryPart1.Concat(usersFromQueryPart2).ToList();

            _blobStorageRepository.Setup(x => x.DownloadFileAsync(It.IsAny<string>()))
                        .Callback<string>(path =>
                        {
                            List<AzureADUser> users = null;
                            if (path == _jobState.CompletedParts[0])
                                users = usersFromQueryPart1;
                            else if (path == _jobState.CompletedParts[1])
                                users = usersFromQueryPart2;
                            else if (path == _jobState.DestinationPart)
                                users = usersFromDestination;

                            var content = new GroupMembership
                            {
                                SyncJobId = _syncJob?.Id ?? Guid.Empty,
                                MembershipObtainerDryRunEnabled = false,
                                RunId = _syncJob?.RunId.Value ?? Guid.Empty,
                                Exclusionary = false,
                                SourceMembers = users,
                                Destination = new AzureADGroup
                                {
                                    ObjectId = _syncJob != null
                                                    ? _group.GroupId
                                                    : Guid.Empty
                                }
                            };

                            _blobResult = new BlobResult
                            {
                                BlobStatus = BlobStatus.Found,
                                Content = TextCompressor.Compress(JsonSerializer.Serialize(content))
                            };
                        })
                        .ReturnsAsync(() => _blobResult);

            var orchestratorFunction = new MembershipSubOrchestratorFunction(_thresholdConfig.Object, _graphAPIService.Object, _telemetryClient, _multiLaneConfig);
            var response = await orchestratorFunction.RunMembershipSubOrchestratorFunctionAsync(_durableContext.Object);

            Assert.AreEqual(MembershipDeltaStatus.NoChanges, response.MembershipDeltaStatus);
        }

        [TestMethod]
        public async Task MembershipSubOrchestrator_WithFailedMembershipExtraction_ReturnsError()
        {
            // Arrange - Setup specific failed extraction response 
            // Reset any previous setup first
            _membershipExtractionResponse = null;
            
            // Configure the mock to return a failed response specifically for this test
            _durableContext.Setup(x => x.CallActivityAsync<MembershipExtractionResponse>(
                It.Is<string>(s => s == nameof(MembershipExtractionFunction)), 
                It.IsAny<MembershipExtractionRequest>()))
                .ReturnsAsync(new MembershipExtractionResponse
                {
                    IsSuccessful = false,
                    ErrorMessage = "Failed to extract membership data"
                });

            var orchestratorFunction = new MembershipSubOrchestratorFunction(_thresholdConfig.Object, _graphAPIService.Object, _telemetryClient, _multiLaneConfig);

            // Act
            var response = await orchestratorFunction.RunMembershipSubOrchestratorFunctionAsync(_durableContext.Object);

            // Assert
            Assert.AreEqual(MembershipDeltaStatus.Error, response.MembershipDeltaStatus);
        }

        private async Task<MembershipExtractionResponse> CallMembershipExtractionFunctionAsync(MembershipExtractionRequest request)
        {
            var function = new MembershipExtractionFunction(_loggingRepository.Object, _blobStorageRepository.Object);
            return await function.ExtractMembershipAsync(request);
        }

        private async Task CallFileUploaderFunctionAsync(FileUploaderRequest request)
        {
            var function = new FileUploaderFunction(_loggingRepository.Object, _blobStorageRepository.Object);
            await function.UploadFileAsync(request);
        }

        private async Task CallFileDeleterFunctionAsync(FileDeleterRequest request)
        {
            var function = new FileDeleterFunction(_loggingRepository.Object, _blobStorageRepository.Object);
            await function.DeleteFileAsync(request);
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

		private async Task CallEmailSenderFunctionAsync(EmailSenderRequest request)
		{
			var function = new EmailSenderFunction(_loggingRepository.Object, _graphAPIService.Object);
			await function.SendEmailAsync(request);
		}

		private async Task<DeltaCalculatorResponse> CallDeltaCalculatorFunctionAsync(DeltaCalculatorRequest request)
        {
            var function = new DeltaCalculatorFunction(_loggingRepository.Object, _blobStorageRepository.Object, _deltaCalculatorService);
            return await function.CalculateDeltaAsync(request);
        }

        private string GenerateFileName(SyncJob syncJob, Guid groupId, string suffix, IDurableOrchestrationContext context)
        {
            var timeStamp = context.CurrentUtcDateTime.ToString("MMddyyyy-HHmm");
            return $"/{groupId}/{timeStamp}_{syncJob.RunId}_{suffix}.json";
        }
    }
}