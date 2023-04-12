// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Models;
using Models.ServiceBus;
using Hosts.MembershipAggregator;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Graph;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Polly;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Models.ThresholdNotifications;
using Newtonsoft.Json;

namespace Services.Tests
{
    [TestClass]
    public class MembershipSubOrchestratorTests
    {
        private SyncJob _syncJob;
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
        private MembershipSubOrchestratorRequest _membershipSubOrchestratorRequest;

        private Mock<IDryRunValue> _dryRun;
        private Mock<IGMMResources> _gmmResources;
        private Mock<IGraphAPIService> _graphAPIService;
        private Mock<IThresholdConfig> _thresholdConfig;
        private Mock<IThresholdNotificationConfig> _thresholdNotificationConfig;
        private Mock<ILoggingRepository> _loggingRepository;
        private Mock<ISyncJobRepository> _syncJobRepository;
        private Mock<IEmailSenderRecipient> _emailSenderRecipient;
        private Mock<IDurableOrchestrationContext> _durableContext;
        private Mock<IBlobStorageRepository> _blobStorageRepository;
        private Mock<ILocalizationRepository> _localizationRepository;
        private Mock<INotificationRepository> _notificationRepository;

        [TestInitialize]
        public void SetupTest()
        {
            _thresholdConfig = new Mock<IThresholdConfig>();
            _thresholdNotificationConfig = new Mock<IThresholdNotificationConfig>();
            _loggingRepository = new Mock<ILoggingRepository>();
            _syncJobRepository = new Mock<ISyncJobRepository>();
            _durableContext = new Mock<IDurableOrchestrationContext>();
            _blobStorageRepository = new Mock<IBlobStorageRepository>();
            _emailSenderRecipient = new Mock<IEmailSenderRecipient>();
            _graphAPIService = new Mock<IGraphAPIService>();
            _gmmResources = new Mock<IGMMResources>();
            _localizationRepository = new Mock<ILocalizationRepository>();
            _notificationRepository = new Mock<INotificationRepository>();
            _dryRun = new Mock<IDryRunValue>();

            _deltaCalculatorService = new DeltaCalculatorService
                                            (
                                                _syncJobRepository.Object,
                                                _loggingRepository.Object,
                                                _emailSenderRecipient.Object,
                                                _graphAPIService.Object,
                                                _dryRun.Object,
                                                _thresholdConfig.Object,
                                                _thresholdNotificationConfig.Object,
                                                _gmmResources.Object,
                                                _localizationRepository.Object,
                                                _notificationRepository.Object
                                            );


            _deltaCalculatorResponse = null;
            _numberOfUsersForSourcePart = 10;
            _numberOfUsersForSourcePartOne = 10;
            _numberOfUsersForSourcePartTwo = 10;
            _numberOfUsersForDestinationPart = 10;
            _membersPerFile = new Dictionary<string, int>();
            _groupExists = PolicyResult<bool>.Successful(true, new Context());

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

            _membershipSubOrchestratorRequest = new MembershipSubOrchestratorRequest
            {
                EntityId = new EntityId(),
                SyncJob = _syncJob
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
                                            SyncJobPartitionKey = _syncJob?.PartitionKey,
                                            SyncJobRowKey = _syncJob?.RowKey,
                                            MembershipObtainerDryRunEnabled = false,
                                            RunId = _syncJob?.RunId.Value ?? Guid.Empty,
                                            Exclusionary = false,
                                            SourceMembers = Enumerable.Range(0, userCount)
                                                                         .Select(x => new AzureADUser { ObjectId = Guid.NewGuid() })
                                                                         .ToList(),
                                            Destination = new AzureADGroup
                                            {
                                                ObjectId = _syncJob != null
                                                                ? _syncJob.TargetOfficeGroupId
                                                                : Guid.Empty
                                            }
                                        };

                                        _blobResult = new BlobResult
                                        {
                                            BlobStatus = BlobStatus.Found,
                                            Content = JsonConvert.SerializeObject(content)
                                        };
                                    })
                                    .ReturnsAsync(() => _blobResult);

            _blobStorageRepository.Setup(x => x.DownloadFileAsync(It.Is<string>(x => !x.StartsWith("http://file-path"))))
                        .Callback<string>(path =>
                        {
                            var userCount = _membersPerFile[path];
                            var content = new GroupMembership
                            {
                                SyncJobPartitionKey = _syncJob?.PartitionKey,
                                SyncJobRowKey = _syncJob?.RowKey,
                                MembershipObtainerDryRunEnabled = false,
                                RunId = _syncJob?.RunId.Value ?? Guid.Empty,
                                Exclusionary = false,
                                SourceMembers = Enumerable.Range(0, userCount)
                                                             .Select(x => new AzureADUser { ObjectId = Guid.NewGuid() })
                                                             .ToList(),
                                Destination = new AzureADGroup
                                {
                                    ObjectId = _syncJob != null
                                                    ? _syncJob.TargetOfficeGroupId
                                                    : Guid.Empty
                                }
                            };

                            _blobResult = new BlobResult
                            {
                                BlobStatus = BlobStatus.Found,
                                Content = JsonConvert.SerializeObject(content)
                            };
                        })
                        .ReturnsAsync(() => _blobResult);

            var owners = new List<User>
            {
                { new User { Id = Guid.NewGuid().ToString(), Mail = "mail_1@mail.com" } },
                { new User { Id = Guid.NewGuid().ToString(), Mail = "mail_2@mail.com" } },
                { new User { Id = Guid.NewGuid().ToString(), Mail = "mail_3@mail.com" } }
            };

            _syncJobRepository.Setup(x => x.GetSyncJobAsync(It.IsAny<string>(), It.IsAny<string>()))
                              .ReturnsAsync(() => _syncJob);

            _graphAPIService.Setup(x => x.GroupExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                            .ReturnsAsync(() => _groupExists);

            _graphAPIService.Setup(x => x.GetGroupOwnersAsync(It.IsAny<Guid>(), It.IsAny<int>()))
                            .ReturnsAsync(owners);

            _graphAPIService.Setup(x => x.GetGroupNameAsync(It.IsAny<Guid>()))
                            .ReturnsAsync(() => "GroupName");

            _durableContext.Setup(x => x.GetInput<MembershipSubOrchestratorRequest>())
                            .Returns(() => _membershipSubOrchestratorRequest);

            _durableContext.Setup(x => x.CreateEntityProxy<IJobTracker>(It.IsAny<EntityId>()))
                            .Returns(() => _jobTrackerEntity);

            _durableContext.Setup(x => x.CallActivityAsync<(string FilePath, string Content)>(It.Is<string>(x => x == nameof(FileDownloaderFunction)), It.IsAny<FileDownloaderRequest>()))
                            .Callback<string, object>(async (name, request) =>
                            {
                                _downloaderResponse = await CallFileDownloaderFunctionAsync(request as FileDownloaderRequest);
                            })
                            .ReturnsAsync(() => _downloaderResponse);

            _durableContext.Setup(x => x.CallActivityAsync<DeltaCalculatorResponse>(It.Is<string>(x => x == nameof(DeltaCalculatorFunction)), It.IsAny<DeltaCalculatorRequest>()))
                            .Callback<string, object>(async (name, request) =>
                            {
                                _deltaCalculatorResponse = await CallDeltaCalculatorFunctionAsync(request as DeltaCalculatorRequest);
                            })
                            .ReturnsAsync(() => _deltaCalculatorResponse);

            _durableContext.Setup(x => x.CallActivityAsync(It.Is<string>(x => x == nameof(FileUploaderFunction)), It.IsAny<FileUploaderRequest>()))
                            .Callback<string, object>(async (name, request) =>
                            {
                                await CallFileUploaderFunctionAsync(request as FileUploaderRequest);
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
        }

        [TestMethod]
        public async Task ProcessInitialJobSyncAsync()
        {
            _syncJob.LastRunTime = DateTime.FromFileTimeUtc(0);

            var orchestratorFunction = new MembershipSubOrchestratorFunction(_thresholdConfig.Object);
            var response = await orchestratorFunction.RunMembershipSubOrchestratorFunctionAsync(_durableContext.Object);

            Assert.IsNotNull(response.FilePath);
            Assert.AreEqual(MembershipDeltaStatus.Ok, response.MembershipDeltaStatus);

            _blobStorageRepository.Verify(x => x.DownloadFileAsync(It.IsAny<string>()), Times.Exactly(3));
            _blobStorageRepository.Verify(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()), Times.Once());
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("Uploaded membership file")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Once());
            _syncJobRepository.Verify(x => x.UpdateSyncJobStatusAsync(It.IsAny<IEnumerable<SyncJob>>(), It.IsAny<SyncStatus>()), Times.Never());
        }

        [TestMethod]
        public async Task HitAdditionsThresholdTestAsync()
        {
            var currentThresholdViolations = 1;
            _thresholdConfig.Setup(x => x.NumberOfThresholdViolationsToDisableJob).Returns(5);
            _numberOfUsersForDestinationPart = 5;
            _syncJob.ThresholdViolations = currentThresholdViolations;


            var orchestratorFunction = new MembershipSubOrchestratorFunction(_thresholdConfig.Object);
            var response = await orchestratorFunction.RunMembershipSubOrchestratorFunctionAsync(_durableContext.Object);

            Assert.IsNull(response.FilePath);
            Assert.AreEqual(MembershipDeltaStatus.ThresholdExceeded, response.MembershipDeltaStatus);

            _blobStorageRepository.Verify(x => x.DownloadFileAsync(It.IsAny<string>()), Times.Exactly(3));
            _blobStorageRepository.Verify(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()), Times.Never());
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("Membership increase in")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Once());
            _syncJobRepository.Verify(x => x.UpdateSyncJobsAsync(
                                                                    It.Is<IEnumerable<SyncJob>>(y => y.All(z => z.ThresholdViolations > currentThresholdViolations)),
                                                                    It.Is<SyncStatus>(x => x == SyncStatus.Idle)
                                                                )
                                                                    , Times.Once());

            _graphAPIService.Verify(x => x.SendEmailAsync(
                                            It.IsAny<string>(),
                                            It.IsAny<string>(),
                                            It.IsAny<string[]>(),
                                            It.IsAny<Guid>(),
                                            It.IsAny<string>(),
                                            It.IsAny<string>(),
                                            It.IsAny<string[]>()
                                        )
                                            , Times.Never());
        }

        [TestMethod]
        public async Task IgnoreThresholdOnceTestAsync()
        {
            var currentThresholdViolations = 1;
            _thresholdConfig.Setup(x => x.NumberOfThresholdViolationsToDisableJob).Returns(5);
            _numberOfUsersForDestinationPart = 5;
            _syncJob.ThresholdViolations = currentThresholdViolations;
            _syncJob.IgnoreThresholdOnce = true;

            var orchestratorFunction = new MembershipSubOrchestratorFunction(_thresholdConfig.Object);
            var response = await orchestratorFunction.RunMembershipSubOrchestratorFunctionAsync(_durableContext.Object);

            Assert.AreEqual(MembershipDeltaStatus.Ok, response.MembershipDeltaStatus);
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("Going to sync the job")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Once());
        }

        [TestMethod]
        public async Task HitRemovalThresholdTestAsync()
        {
            _thresholdConfig.Setup(x => x.NumberOfThresholdViolationsToDisableJob).Returns(5);
            _thresholdConfig.Setup(x => x.NumberOfThresholdViolationsToNotify).Returns(2);
            _graphAPIService.Setup(x => x.IsEmailRecipientOwnerOfGroupAsync(It.IsAny<string>(), It.IsAny<Guid>())).ReturnsAsync(true);

            _numberOfUsersForSourcePart = 5;
            _syncJob.ThresholdViolations = 1;

            var orchestratorFunction = new MembershipSubOrchestratorFunction(_thresholdConfig.Object);
            var response = await orchestratorFunction.RunMembershipSubOrchestratorFunctionAsync(_durableContext.Object);

            Assert.IsNull(response.FilePath);
            Assert.AreEqual(MembershipDeltaStatus.ThresholdExceeded, response.MembershipDeltaStatus);

            _blobStorageRepository.Verify(x => x.DownloadFileAsync(It.IsAny<string>()), Times.Exactly(3));
            _blobStorageRepository.Verify(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()), Times.Never());
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("Membership decrease in")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Once());
            _syncJobRepository.Verify(x => x.UpdateSyncJobsAsync(
                                                                    It.IsAny<IEnumerable<SyncJob>>(),
                                                                    It.Is<SyncStatus?>(x => x == SyncStatus.Idle)
                                                                )
                                                                    , Times.Once());

            _graphAPIService.Verify(x => x.SendEmailAsync(
                                            It.IsAny<string>(),
                                            It.IsAny<string>(),
                                            It.IsAny<string[]>(),
                                            It.IsAny<Guid>(),
                                            It.IsAny<string>(),
                                            It.IsAny<string>(),
                                            It.IsAny<string[]>()
                                        )
                                            , Times.Once());
        }

        [TestMethod]
        public async Task HitMaxAddsRemovesThresholdViolationsTestAsync()
        {
            _thresholdConfig.Setup(x => x.NumberOfThresholdViolationsToDisableJob).Returns(5);
            _thresholdConfig.Setup(x => x.NumberOfThresholdViolationsToNotify).Returns(5);

            _syncJob.ThresholdViolations = 4;

            var orchestratorFunction = new MembershipSubOrchestratorFunction(_thresholdConfig.Object);
            var response = await orchestratorFunction.RunMembershipSubOrchestratorFunctionAsync(_durableContext.Object);

            Assert.IsNull(response.FilePath);
            Assert.AreEqual(MembershipDeltaStatus.ThresholdExceeded, response.MembershipDeltaStatus);

            _blobStorageRepository.Verify(x => x.DownloadFileAsync(It.IsAny<string>()), Times.Exactly(3));
            _blobStorageRepository.Verify(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()), Times.Never());
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("Membership decrease in")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Once());
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("Membership increase in")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Once());
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("Threshold exceeded")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Once());

            _graphAPIService.Verify(x => x.SendEmailAsync(
                                                        It.IsAny<string>(),
                                                        It.IsAny<string>(),
                                                        It.IsAny<string[]>(),
                                                        It.IsAny<Guid>(),
                                                        It.IsAny<string>(),
                                                        It.IsAny<string>(),
                                                        It.IsAny<string[]>()
                                                    )
                                                        , Times.Once());

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

            var orchestratorFunction = new MembershipSubOrchestratorFunction(_thresholdConfig.Object);
            var response = await orchestratorFunction.RunMembershipSubOrchestratorFunctionAsync(_durableContext.Object);

            Assert.IsNull(response.FilePath);
            Assert.AreEqual(MembershipDeltaStatus.ThresholdExceeded, response.MembershipDeltaStatus);

            _blobStorageRepository.Verify(x => x.DownloadFileAsync(It.IsAny<string>()), Times.Exactly(3));
            _blobStorageRepository.Verify(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()), Times.Never());
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("Membership increase in")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Once());
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("Threshold exceeded")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Once());

            _graphAPIService.Verify(x => x.SendEmailAsync(
                                                        It.IsAny<string>(),
                                                        It.IsAny<string>(),
                                                        It.IsAny<string[]>(),
                                                        It.IsAny<Guid>(),
                                                        It.IsAny<string>(),
                                                        It.IsAny<string>(),
                                                        It.IsAny<string[]>()
                                                    )
                                                        , Times.Once());

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
            _numberOfUsersForSourcePart = 0;

            var orchestratorFunction = new MembershipSubOrchestratorFunction(_thresholdConfig.Object);
            var response = await orchestratorFunction.RunMembershipSubOrchestratorFunctionAsync(_durableContext.Object);

            Assert.IsNull(response.FilePath);
            Assert.AreEqual(MembershipDeltaStatus.ThresholdExceeded, response.MembershipDeltaStatus);

            _blobStorageRepository.Verify(x => x.DownloadFileAsync(It.IsAny<string>()), Times.Exactly(3));
            _blobStorageRepository.Verify(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()), Times.Never());
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("Membership decrease in")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Once());
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("Threshold exceeded")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Once());

            _graphAPIService.Verify(x => x.SendEmailAsync(
                                                        It.IsAny<string>(),
                                                        It.IsAny<string>(),
                                                        It.IsAny<string[]>(),
                                                        It.IsAny<Guid>(),
                                                        It.IsAny<string>(),
                                                        It.IsAny<string>(),
                                                        It.IsAny<string[]>()
                                                    )
                                                        , Times.Once());

            _syncJobRepository.Verify(x => x.UpdateSyncJobsAsync(
                                                                    It.IsAny<IEnumerable<SyncJob>>(),
                                                                    It.Is<SyncStatus?>(x => x == SyncStatus.ThresholdExceeded)
                                                                )
                                                                    , Times.Once());
        }

        [TestMethod]
        public async Task DryRunTestAsync()
        {
            _syncJob.LastRunTime = DateTime.FromFileTimeUtc(0);

            _dryRun.Setup(x => x.DryRunEnabled).Returns(true);
            _deltaCalculatorService = new DeltaCalculatorService
                                (
                                    _syncJobRepository.Object,
                                    _loggingRepository.Object,
                                    _emailSenderRecipient.Object,
                                    _graphAPIService.Object,
                                    _dryRun.Object,
                                    _thresholdConfig.Object,
                                    _thresholdNotificationConfig.Object,
                                    _gmmResources.Object,
                                    _localizationRepository.Object,
                                    _notificationRepository.Object
                                );

            var orchestratorFunction = new MembershipSubOrchestratorFunction(_thresholdConfig.Object);
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
                                    _loggingRepository.Object,
                                    _emailSenderRecipient.Object,
                                    _graphAPIService.Object,
                                    _dryRun.Object,
                                    _thresholdConfig.Object,
                                    _thresholdNotificationConfig.Object,
                                    _gmmResources.Object,
                                    _localizationRepository.Object,
                                    _notificationRepository.Object
                                );

            var orchestratorFunction = new MembershipSubOrchestratorFunction(_thresholdConfig.Object);
            var response = await orchestratorFunction.RunMembershipSubOrchestratorFunctionAsync(_durableContext.Object);

            Assert.IsNull(response.FilePath);
            Assert.AreEqual(MembershipDeltaStatus.Error, response.MembershipDeltaStatus);

            _blobStorageRepository.Verify(x => x.DownloadFileAsync(It.IsAny<string>()), Times.Exactly(3));
            _blobStorageRepository.Verify(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()), Times.Never());
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("Sync job : Partition key")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Once());
        }

        [TestMethod]
        public async Task TestDeltaCalculatorGroupNotFoundErrorAsync()
        {
            _groupExists = PolicyResult<bool>.Successful(false, new Context());
            _deltaCalculatorService = new DeltaCalculatorService
                                (
                                    _syncJobRepository.Object,
                                    _loggingRepository.Object,
                                    _emailSenderRecipient.Object,
                                    _graphAPIService.Object,
                                    _dryRun.Object,
                                    _thresholdConfig.Object,
                                    _thresholdNotificationConfig.Object,
                                    _gmmResources.Object,
                                    _localizationRepository.Object,
                                    _notificationRepository.Object
                                );

            var orchestratorFunction = new MembershipSubOrchestratorFunction(_thresholdConfig.Object);
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

            _membersPerFile.Add(GenerateFileName(_syncJob, "SourceMembership"), 100000);
            _membersPerFile.Add(GenerateFileName(_syncJob, "DestinationMembership"), 0);

            var orchestratorFunction = new MembershipSubOrchestratorFunction(_thresholdConfig.Object);
            var response = await orchestratorFunction.RunMembershipSubOrchestratorFunctionAsync(_durableContext.Object);

            _blobStorageRepository.Verify(x => x.UploadFileAsync(It.Is<string>(x => x.Contains("SourceMembership")),
                                                                 It.IsAny<string>(),
                                                                 It.IsAny<Dictionary<string, string>>()), Times.Once());

            _blobStorageRepository.Verify(x => x.UploadFileAsync(It.Is<string>(x => x.Contains("DestinationMembership")),
                                                                 It.IsAny<string>(),
                                                                 It.IsAny<Dictionary<string, string>>()), Times.Once());

            _blobStorageRepository.Verify(x => x.DownloadFileAsync(It.Is<string>(x => x.Contains("SourceMembership"))), Times.Once());
            _blobStorageRepository.Verify(x => x.DownloadFileAsync(It.Is<string>(x => x.Contains("DestinationMembership"))), Times.Once());

            _blobStorageRepository.Verify(x => x.UploadFileAsync(It.Is<string>(x => x.Contains("Aggregated")),
                                                                 It.IsAny<string>(),
                                                                 It.IsAny<Dictionary<string, string>>()), Times.Once());

            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("Uploaded membership file")), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()), Times.Once());
            _syncJobRepository.Verify(x => x.UpdateSyncJobStatusAsync(It.IsAny<IEnumerable<SyncJob>>(), It.IsAny<SyncStatus>()), Times.Never());

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
                                            SyncJobPartitionKey = _syncJob?.PartitionKey,
                                            SyncJobRowKey = _syncJob?.RowKey,
                                            MembershipObtainerDryRunEnabled = false,
                                            RunId = _syncJob?.RunId.Value ?? Guid.Empty,
                                            Exclusionary = true,
                                            SourceMembers = Enumerable.Range(0, userCount)
                                                                         .Select(x => new AzureADUser { ObjectId = Guid.NewGuid() })
                                                                         .ToList(),
                                            Destination = new AzureADGroup
                                            {
                                                ObjectId = _syncJob != null
                                                                ? _syncJob.TargetOfficeGroupId
                                                                : Guid.Empty
                                            }
                                        };

                                        _blobResult = new BlobResult
                                        {
                                            BlobStatus = BlobStatus.Found,
                                            Content = JsonConvert.SerializeObject(content)
                                        };
                                    })
                                    .ReturnsAsync(() => _blobResult);

            var orchestratorFunction = new MembershipSubOrchestratorFunction(_thresholdConfig.Object);
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
                                            SyncJobPartitionKey = _syncJob?.PartitionKey,
                                            SyncJobRowKey = _syncJob?.RowKey,
                                            MembershipObtainerDryRunEnabled = false,
                                            RunId = _syncJob?.RunId.Value ?? Guid.Empty,
                                            Exclusionary = false,
                                            SourceMembers = Enumerable.Range(0, userCount)
                                                                         .Select(x => new AzureADUser { ObjectId = Guid.NewGuid() })
                                                                         .ToList(),
                                            Destination = new AzureADGroup
                                            {
                                                ObjectId = _syncJob != null
                                                                ? _syncJob.TargetOfficeGroupId
                                                                : Guid.Empty
                                            }
                                        };

                                        _blobResult = new BlobResult
                                        {
                                            BlobStatus = BlobStatus.Found,
                                            Content = JsonConvert.SerializeObject(content)
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
                                           SyncJobPartitionKey = _syncJob?.PartitionKey,
                                           SyncJobRowKey = _syncJob?.RowKey,
                                           MembershipObtainerDryRunEnabled = false,
                                           RunId = _syncJob?.RunId.Value ?? Guid.Empty,
                                           Exclusionary = true,
                                           SourceMembers = Enumerable.Range(0, userCount)
                                                                        .Select(x => new AzureADUser { ObjectId = Guid.NewGuid() })
                                                                        .ToList(),
                                           Destination = new AzureADGroup
                                           {
                                               ObjectId = _syncJob != null
                                                               ? _syncJob.TargetOfficeGroupId
                                                               : Guid.Empty
                                           }
                                       };

                                       _blobResult = new BlobResult
                                       {
                                           BlobStatus = BlobStatus.Found,
                                           Content = JsonConvert.SerializeObject(content)
                                       };
                                   })
                                   .ReturnsAsync(() => _blobResult);

            var orchestratorFunction = new MembershipSubOrchestratorFunction(_thresholdConfig.Object);
            var response = await orchestratorFunction.RunMembershipSubOrchestratorFunctionAsync(_durableContext.Object);
            Assert.AreEqual(50000, response.ProjectedMemberCount);
        }

        [TestMethod]
        public async Task SendThresholdNotificationIfEnabled()
        {
            _syncJob.ThresholdViolations = 2;
            _thresholdNotificationConfig.Setup(x => x.IsThresholdNotificationEnabled).Returns(true);
            _thresholdConfig.Setup(x => x.NumberOfThresholdViolationsFollowUps).Returns(3);

            var orchestratorFunction = new MembershipSubOrchestratorFunction(_thresholdConfig.Object);
            var response = await orchestratorFunction.RunMembershipSubOrchestratorFunctionAsync(_durableContext.Object);

            Assert.AreEqual(MembershipDeltaStatus.ThresholdExceeded, response.MembershipDeltaStatus);

            _notificationRepository.Verify(x => x.SaveNotificationAsync(It.IsAny<Models.ThresholdNotifications.ThresholdNotification>()), Times.Once());
            _notificationRepository.Verify(x => x.SaveNotificationAsync(
                It.Is<Models.ThresholdNotifications.ThresholdNotification>(n => n.Status.Equals(ThresholdNotificationStatus.Queued))));
        }

        private async Task<(string FilePath, string Content)> CallFileDownloaderFunctionAsync(FileDownloaderRequest request)
        {
            var function = new FileDownloaderFunction(_loggingRepository.Object, _blobStorageRepository.Object);
            return await function.DownloadFileAsync(request);
        }

        private async Task CallFileUploaderFunctionAsync(FileUploaderRequest request)
        {
            var function = new FileUploaderFunction(_loggingRepository.Object, _blobStorageRepository.Object);
            await function.UploadFileAsync(request);
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

        private async Task<DeltaCalculatorResponse> CallDeltaCalculatorFunctionAsync(DeltaCalculatorRequest request)
        {
            var function = new DeltaCalculatorFunction(_loggingRepository.Object, _blobStorageRepository.Object, _deltaCalculatorService);
            return await function.CalculateDeltaAsync(request);
        }

        private string GenerateFileName(SyncJob syncJob, string suffix)
        {
            var timeStamp = syncJob.Timestamp.GetValueOrDefault().ToString("MMddyyyy-HHmmss");
            return $"/{syncJob.TargetOfficeGroupId}/{timeStamp}_{syncJob.RunId}_{suffix}.json";
        }
    }
}