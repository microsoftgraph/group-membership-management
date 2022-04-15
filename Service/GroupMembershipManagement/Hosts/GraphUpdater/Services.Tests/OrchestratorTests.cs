// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using DIConcreteTypes;
using Entities;
using Entities.ServiceBus;
using Hosts.GraphUpdater;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Graph;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Newtonsoft.Json;
using Repositories.Contracts;
using Repositories.MembershipDifference;
using Repositories.Mocks;
using Services.Entities;
using Services.Tests.Mocks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class OrchestratorTests
    {
        GMMResources _gmmResources = new GMMResources
        {
            LearnMoreAboutGMMUrl = "http://learn-more-url"
        };

        [TestMethod]
        public async Task RunOrchestratorValidSyncTest()
        {
            MockLoggingRepository mockLoggingRepo;
            TelemetryClient mockTelemetryClient;
            MockMailRepository mockMailRepo;
            MockGraphUpdaterService mockGraphUpdaterService;
            DryRunValue dryRun;
            DeltaCalculatorService deltaCalculatorService;
            EmailSenderRecipient mailSenders;
            MockSyncJobRepository mockSyncJobRepo;
            MockGraphGroupRepository mockGroupRepo;
            MembershipDifferenceCalculator<AzureADUser> calculator;
            ThresholdConfig thresholdConfig;
            MockLocalizationRepository localizationRepository;
            MockBlobStorageRepository blobStorageRepository;

            mockLoggingRepo = new MockLoggingRepository();
            mockTelemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            mockMailRepo = new MockMailRepository();
            mockGraphUpdaterService = new MockGraphUpdaterService(mockMailRepo);
            dryRun = new DryRunValue(false);
            thresholdConfig = new ThresholdConfig(5, 3, 3, 10);
            mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass",
                                            "recipient@domain.com", "recipient@domain.com", "recipient@domain.com");

            calculator = new MembershipDifferenceCalculator<AzureADUser>();
            mockGroupRepo = new MockGraphGroupRepository();
            mockSyncJobRepo = new MockSyncJobRepository();
            localizationRepository = new MockLocalizationRepository();
            blobStorageRepository = new MockBlobStorageRepository();

            deltaCalculatorService = new DeltaCalculatorService(
                                            calculator,
                                            mockSyncJobRepo,
                                            mockLoggingRepo,
                                            mailSenders,
                                            mockGraphUpdaterService,
                                            dryRun,
                                            thresholdConfig,
                                            _gmmResources,
                                            localizationRepository);

            var groupMembership = GetGroupMembership();
            var input = new GraphUpdaterHttpRequest
            {
                FilePath = "/file/path/name.json",
                RunId = groupMembership.RunId,
                JobPartitionKey = groupMembership.SyncJobPartitionKey,
                JobRowKey = groupMembership.SyncJobRowKey
            };

            var destinationMembers = await GetDestinationMembersAsync(groupMembership, mockLoggingRepo);
            var syncJob = new SyncJob
            {
                PartitionKey = groupMembership.SyncJobPartitionKey,
                RowKey = groupMembership.SyncJobRowKey,
                TargetOfficeGroupId = groupMembership.Destination.ObjectId,
                ThresholdPercentageForAdditions = -1,
                ThresholdPercentageForRemovals = -1,
                LastRunTime = DateTime.UtcNow.AddDays(-1),
                Requestor = "user@domail.com",
                RunId = Guid.NewGuid()
            };

            var fileDownloaderRequest = new FileDownloaderRequest
            {
                FilePath = input.FilePath,
                RunId = input.RunId,
                SyncJob = syncJob
            };

            blobStorageRepository.Files.Add(input.FilePath, JsonConvert.SerializeObject(groupMembership));

            var context = new Mock<IDurableOrchestrationContext>();
            context.Setup(x => x.GetInput<GraphUpdaterHttpRequest>()).Returns(input);
            context.Setup(x => x.CallActivityAsync<SyncJob>(It.IsAny<string>(), It.IsAny<JobReaderRequest>())).ReturnsAsync(syncJob);
            context.Setup(x => x.CallActivityAsync<string>(It.IsAny<string>(), It.IsAny<FileDownloaderRequest>()))
                    .ReturnsAsync(await DownloadFileAsync(fileDownloaderRequest, mockLoggingRepo, blobStorageRepository));
            context.Setup(x => x.CallActivityAsync(It.IsAny<string>(), It.IsAny<LoggerRequest>()))
                    .Callback<string, object>(async (name, request) => await LogMessageAsync((LoggerRequest)request, mockLoggingRepo));
            context.Setup(x => x.CallActivityAsync<bool>(It.IsAny<string>(), It.IsAny<GroupValidatorRequest>()))
                    .Returns(async () => await CheckIfGroupExistsAsync(groupMembership, mockLoggingRepo, mockGraphUpdaterService, mailSenders));
            context.Setup(x => x.CallSubOrchestratorAsync<List<AzureADUser>>(It.IsAny<string>(), It.IsAny<UsersReaderRequest>()))
                    .ReturnsAsync(destinationMembers);
            context.Setup(x => x.CallActivityAsync<DeltaResponse>(It.IsAny<string>(), It.IsAny<DeltaCalculatorRequest>()))
                    .Returns(async () => await GetDeltaResponseAsync(groupMembership, destinationMembers, mockLoggingRepo, deltaCalculatorService));

            mockGraphUpdaterService.Groups.Add(groupMembership.Destination.ObjectId, new Group { Id = groupMembership.Destination.ObjectId.ToString() });
            mockSyncJobRepo.ExistingSyncJobs.Add((syncJob.PartitionKey, syncJob.RowKey), syncJob);

            var orchestrator = new OrchestratorFunction(mockLoggingRepo, mockTelemetryClient, mockGraphUpdaterService, dryRun, mailSenders, thresholdConfig, _gmmResources);
            var response = await orchestrator.RunOrchestratorAsync(context.Object);

            Assert.IsTrue(response == OrchestrationRuntimeStatus.Completed);
            Assert.IsTrue(mockLoggingRepo.MessagesLogged.Any(x => x.Message.Contains($"{nameof(DeltaCalculatorFunction)} function completed")));
            Assert.IsTrue(mockLoggingRepo.MessagesLogged.Any(x => x.Message == nameof(OrchestratorFunction) + " function completed"));

            context.Verify(x => x.CallSubOrchestratorAsync<GraphUpdaterStatus>(It.IsAny<string>(), It.IsAny<GroupUpdaterRequest>()), Times.Exactly(2));
            Assert.IsNotNull(mockLoggingRepo.SyncJobProperties);
            Assert.AreEqual(mockLoggingRepo.SyncJobProperties["RunId"], syncJob.RunId.ToString());
            Assert.AreEqual(mockLoggingRepo.SyncJobProperties["PartitionKey"], syncJob.PartitionKey);
            Assert.AreEqual(mockLoggingRepo.SyncJobProperties["RowKey"], syncJob.RowKey);
        }

        [TestMethod]
        public async Task RunOrchestratorInitialSyncTest()
        {
            MockLoggingRepository mockLoggingRepo;
            TelemetryClient mockTelemetryClient;
            MockMailRepository mockMailRepo;
            MockGraphUpdaterService mockGraphUpdaterService;
            DryRunValue dryRun;
            DeltaCalculatorService deltaCalculatorService;
            EmailSenderRecipient mailSenders;
            MockSyncJobRepository mockSyncJobRepo;
            MockGraphGroupRepository mockGroupRepo;
            MembershipDifferenceCalculator<AzureADUser> calculator;
            ThresholdConfig thresholdConfig;
            MockLocalizationRepository localizationRepository;

            mockLoggingRepo = new MockLoggingRepository();
            mockTelemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            mockMailRepo = new MockMailRepository();
            mockGraphUpdaterService = new MockGraphUpdaterService(mockMailRepo);
            dryRun = new DryRunValue(false);
            thresholdConfig = new ThresholdConfig(5, 3, 3, 10);
            mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass",
                                            "recipient@domain.com", "recipient@domain.com", "recipient@domain.com");

            calculator = new MembershipDifferenceCalculator<AzureADUser>();
            mockGroupRepo = new MockGraphGroupRepository();
            mockSyncJobRepo = new MockSyncJobRepository();
            localizationRepository = new MockLocalizationRepository();

            deltaCalculatorService = new DeltaCalculatorService(
                                            calculator,
                                            mockSyncJobRepo,
                                            mockLoggingRepo,
                                            mailSenders,
                                            mockGraphUpdaterService,
                                            dryRun,
                                            thresholdConfig,
                                            _gmmResources,
                                            localizationRepository);

            var groupMembership = GetGroupMembership();
            var input = new GraphUpdaterHttpRequest
            {
                FilePath = "/file/path/name.json",
                RunId = groupMembership.RunId,
                JobPartitionKey = groupMembership.SyncJobPartitionKey,
                JobRowKey = groupMembership.SyncJobRowKey
            };

            var destinationMembers = await GetDestinationMembersAsync(groupMembership, mockLoggingRepo);
            var syncJob = new SyncJob
            {
                PartitionKey = groupMembership.SyncJobPartitionKey,
                RowKey = groupMembership.SyncJobRowKey,
                TargetOfficeGroupId = groupMembership.Destination.ObjectId,
                ThresholdPercentageForAdditions = -1,
                ThresholdPercentageForRemovals = -1,
                LastRunTime = DateTime.FromFileTimeUtc(0),
                Requestor = "user@domail.com",
                RunId = Guid.NewGuid()
            };

            var owners = new List<User>();
            for (int i = 0; i < 10; i++)
            {
                owners.Add(new User
                {
                    Id = Guid.NewGuid().ToString(),
                    Mail = $"user{i}@mydomain.com"
                });
            }

            var ownerEmails = string.Join(";", owners.Where(x => !string.IsNullOrWhiteSpace(x.Mail)).Select(x => x.Mail));

            var context = new Mock<IDurableOrchestrationContext>();
            context.Setup(x => x.GetInput<GraphUpdaterHttpRequest>()).Returns(input);
            context.Setup(x => x.CallActivityAsync<SyncJob>(It.IsAny<string>(), It.IsAny<JobReaderRequest>())).ReturnsAsync(syncJob);
            context.Setup(x => x.CallActivityAsync<string>(It.IsAny<string>(), It.IsAny<FileDownloaderRequest>())).ReturnsAsync(JsonConvert.SerializeObject(groupMembership));
            context.Setup(x => x.CallActivityAsync(It.IsAny<string>(), It.IsAny<LoggerRequest>()))
                    .Callback<string, object>(async (name, request) => await LogMessageAsync((LoggerRequest)request, mockLoggingRepo));
            context.Setup(x => x.CallActivityAsync<bool>(It.IsAny<string>(), It.IsAny<GroupValidatorRequest>()))
                    .Returns(async () => await CheckIfGroupExistsAsync(groupMembership, mockLoggingRepo, mockGraphUpdaterService, mailSenders));
            context.Setup(x => x.CallSubOrchestratorAsync<List<AzureADUser>>(It.IsAny<string>(), It.IsAny<UsersReaderRequest>()))
                    .ReturnsAsync(destinationMembers);
            context.Setup(x => x.CallActivityAsync<DeltaResponse>(It.IsAny<string>(), It.IsAny<DeltaCalculatorRequest>()))
                    .Returns(async () => await GetDeltaResponseAsync(groupMembership, destinationMembers, mockLoggingRepo, deltaCalculatorService));
            context.Setup(x => x.CallActivityAsync<string>(It.IsAny<string>(), It.IsAny<GroupNameReaderRequest>()))
                    .ReturnsAsync("Target group");
            context.Setup(x => x.CallActivityAsync<List<User>>(It.IsAny<string>(), It.IsAny<GroupOwnersReaderRequest>()))
                    .ReturnsAsync(owners);
            context.Setup(x => x.CallActivityAsync(It.IsAny<string>(), It.IsAny<EmailSenderRequest>()))
                    .Callback<string, object>(async (name, request) =>
                    {
                        var emailSenderFunction = new EmailSenderFunction(mockLoggingRepo, mockGraphUpdaterService);
                        await emailSenderFunction.SendEmailAsync((EmailSenderRequest)request);
                    });

            mockGraphUpdaterService.Groups.Add(groupMembership.Destination.ObjectId, new Group { Id = groupMembership.Destination.ObjectId.ToString() });
            mockSyncJobRepo.ExistingSyncJobs.Add((syncJob.PartitionKey, syncJob.RowKey), syncJob);

            var orchestrator = new OrchestratorFunction(mockLoggingRepo, mockTelemetryClient, mockGraphUpdaterService, dryRun, mailSenders, thresholdConfig, _gmmResources);
            var response = await orchestrator.RunOrchestratorAsync(context.Object);

            Assert.IsTrue(response == OrchestrationRuntimeStatus.Completed);
            Assert.IsTrue(mockLoggingRepo.MessagesLogged.Any(x => x.Message.Contains($"{nameof(DeltaCalculatorFunction)} function completed")));
            Assert.IsTrue(mockLoggingRepo.MessagesLogged.Any(x => x.Message == nameof(OrchestratorFunction) + " function completed"));
            Assert.IsNotNull(mockLoggingRepo.SyncJobProperties);
            Assert.AreEqual(mockLoggingRepo.SyncJobProperties["RunId"], syncJob.RunId.ToString());
            Assert.AreEqual(mockLoggingRepo.SyncJobProperties["PartitionKey"], syncJob.PartitionKey);
            Assert.AreEqual(mockLoggingRepo.SyncJobProperties["RowKey"], syncJob.RowKey);
            Assert.AreEqual(1, mockMailRepo.SentEmails.Count);
            Assert.AreEqual(7, mockMailRepo.SentEmails[0].AdditionalContentParams.Length);
            Assert.AreEqual(ownerEmails, mockMailRepo.SentEmails[0].ToEmailAddresses);

            context.Verify(x => x.CallSubOrchestratorAsync<GraphUpdaterStatus>(It.IsAny<string>(), It.IsAny<GroupUpdaterRequest>()), Times.Exactly(2));
        }

        [TestMethod]
        public async Task RunOrchestratorExceptionTest()
        {
            MockLoggingRepository mockLoggingRepo;
            TelemetryClient mockTelemetryClient;
            MockMailRepository mockMailRepo;
            MockGraphUpdaterService mockGraphUpdaterService;
            DryRunValue dryRun;
            DeltaCalculatorService deltaCalculatorService;
            EmailSenderRecipient mailSenders;
            MockSyncJobRepository mockSyncJobRepo;
            MockGraphGroupRepository mockGroupRepo;
            MembershipDifferenceCalculator<AzureADUser> calculator;
            ThresholdConfig thresholdConfig;
            MockLocalizationRepository localizationRepository;
            MockBlobStorageRepository blobStorageRepository;

            blobStorageRepository = new MockBlobStorageRepository();
            mockLoggingRepo = new MockLoggingRepository();
            mockTelemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            mockMailRepo = new MockMailRepository();
            mockGraphUpdaterService = new MockGraphUpdaterService(mockMailRepo);
            dryRun = new DryRunValue(false);
            thresholdConfig = new ThresholdConfig(5, 3, 3, 10);
            mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass",
                                            "recipient@domain.com", "recipient@domain.com", "recipient@domain.com");

            calculator = new MembershipDifferenceCalculator<AzureADUser>();
            mockGroupRepo = new MockGraphGroupRepository();
            mockSyncJobRepo = new MockSyncJobRepository();
            localizationRepository = new MockLocalizationRepository();
            deltaCalculatorService = new DeltaCalculatorService(
                                            calculator,
                                            mockSyncJobRepo,
                                            mockLoggingRepo,
                                            mailSenders,
                                            mockGraphUpdaterService,
                                            dryRun,
                                            thresholdConfig,
                                            _gmmResources,
                                            localizationRepository);

            var groupMembership = GetGroupMembership();
            var input = new GraphUpdaterHttpRequest
            {
                FilePath = "/file/path/name.json",
                RunId = groupMembership.RunId,
                JobPartitionKey = groupMembership.SyncJobPartitionKey,
                JobRowKey = groupMembership.SyncJobRowKey
            };

            var destinationMembers = await GetDestinationMembersAsync(groupMembership, mockLoggingRepo);
            var syncJob = new SyncJob
            {
                PartitionKey = groupMembership.SyncJobPartitionKey,
                RowKey = groupMembership.SyncJobRowKey,
                TargetOfficeGroupId = groupMembership.Destination.ObjectId,
                ThresholdPercentageForAdditions = -1,
                ThresholdPercentageForRemovals = -1,
                LastRunTime = DateTime.FromFileTimeUtc(0),
                Requestor = "user@domail.com",
                RunId = Guid.NewGuid()
            };

            var fileDownloaderRequest = new FileDownloaderRequest
            {
                FilePath = input.FilePath,
                RunId = input.RunId,
                SyncJob = syncJob
            };

            blobStorageRepository.Files.Add(input.FilePath, JsonConvert.SerializeObject(groupMembership));

            var context = new Mock<IDurableOrchestrationContext>();
            context.Setup(x => x.GetInput<GraphUpdaterHttpRequest>()).Returns(input);
            context.Setup(x => x.CallActivityAsync<SyncJob>(It.IsAny<string>(), It.IsAny<JobReaderRequest>())).ReturnsAsync(syncJob);
            context.Setup(x => x.CallActivityAsync<string>(It.IsAny<string>(), It.IsAny<FileDownloaderRequest>()))
                    .ReturnsAsync(await DownloadFileAsync(fileDownloaderRequest, mockLoggingRepo, blobStorageRepository));
            context.Setup(x => x.CallActivityAsync(It.IsAny<string>(), It.IsAny<LoggerRequest>()))
                    .Callback<string, object>(async (name, request) => await LogMessageAsync((LoggerRequest)request, mockLoggingRepo));
            context.Setup(x => x.CallActivityAsync<bool>(It.IsAny<string>(), It.IsAny<GroupValidatorRequest>()))
                    .ReturnsAsync(true);
            context.Setup(x => x.CallSubOrchestratorAsync<List<AzureADUser>>(It.IsAny<string>(), It.IsAny<UsersReaderRequest>()))
                    .Throws(new Exception("Something went wrong!"));

            JobStatusUpdaterRequest updateJobRequest = null;
            context.Setup(x => x.CallActivityAsync(It.IsAny<string>(), It.IsAny<JobStatusUpdaterRequest>()))
                    .Callback<string, object>((name, request) =>
                    {
                        updateJobRequest = request as JobStatusUpdaterRequest;
                    });

            var orchestrator = new OrchestratorFunction(mockLoggingRepo, mockTelemetryClient, mockGraphUpdaterService, dryRun, mailSenders, thresholdConfig, _gmmResources);
            await Assert.ThrowsExceptionAsync<Exception>(async () => await orchestrator.RunOrchestratorAsync(context.Object));

            Assert.IsFalse(mockLoggingRepo.MessagesLogged.Any(x => x.Message == nameof(OrchestratorFunction) + " function completed"));
            Assert.IsTrue(mockLoggingRepo.MessagesLogged.Any(x => x.Message.Contains("Caught unexpected exception, marking sync job as errored.")));
            Assert.AreEqual(SyncStatus.Error, updateJobRequest.Status);
            Assert.IsNotNull(mockLoggingRepo.SyncJobProperties);
            Assert.AreEqual(mockLoggingRepo.SyncJobProperties["RunId"], syncJob.RunId.ToString());
            Assert.AreEqual(mockLoggingRepo.SyncJobProperties["PartitionKey"], syncJob.PartitionKey);
            Assert.AreEqual(mockLoggingRepo.SyncJobProperties["RowKey"], syncJob.RowKey);
        }

        [TestMethod]
        public async Task RunOrchestratorFileNotFoundExceptionTest()
        {
            MockLoggingRepository mockLoggingRepo;
            TelemetryClient mockTelemetryClient;
            MockMailRepository mockMailRepo;
            MockGraphUpdaterService mockGraphUpdaterService;
            DryRunValue dryRun;
            DeltaCalculatorService deltaCalculatorService;
            EmailSenderRecipient mailSenders;
            MockSyncJobRepository mockSyncJobRepo;
            MockGraphGroupRepository mockGroupRepo;
            MembershipDifferenceCalculator<AzureADUser> calculator;
            ThresholdConfig thresholdConfig;
            MockLocalizationRepository localizationRepository;
            MockBlobStorageRepository blobStorageRepository;

            mockLoggingRepo = new MockLoggingRepository();
            mockTelemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            mockMailRepo = new MockMailRepository();
            mockGraphUpdaterService = new MockGraphUpdaterService(mockMailRepo);
            dryRun = new DryRunValue(false);
            thresholdConfig = new ThresholdConfig(5, 3, 3, 10);
            mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass",
                                            "recipient@domain.com", "recipient@domain.com", "recipient@domain.com");

            calculator = new MembershipDifferenceCalculator<AzureADUser>();
            mockGroupRepo = new MockGraphGroupRepository();
            mockSyncJobRepo = new MockSyncJobRepository();
            localizationRepository = new MockLocalizationRepository();
            blobStorageRepository = new MockBlobStorageRepository();

            deltaCalculatorService = new DeltaCalculatorService(
                                            calculator,
                                            mockSyncJobRepo,
                                            mockLoggingRepo,
                                            mailSenders,
                                            mockGraphUpdaterService,
                                            dryRun,
                                            thresholdConfig,
                                            _gmmResources,
                                            localizationRepository);

            var groupMembership = GetGroupMembership();
            var input = new GraphUpdaterHttpRequest
            {
                FilePath = "/file/path/name.json",
                RunId = groupMembership.RunId,
                JobPartitionKey = groupMembership.SyncJobPartitionKey,
                JobRowKey = groupMembership.SyncJobRowKey
            };

            var destinationMembers = await GetDestinationMembersAsync(groupMembership, mockLoggingRepo);
            var syncJob = new SyncJob
            {
                PartitionKey = groupMembership.SyncJobPartitionKey,
                RowKey = groupMembership.SyncJobRowKey,
                TargetOfficeGroupId = groupMembership.Destination.ObjectId,
                ThresholdPercentageForAdditions = -1,
                ThresholdPercentageForRemovals = -1,
                LastRunTime = DateTime.UtcNow.AddDays(-1),
                Requestor = "user@domail.com",
                RunId = Guid.NewGuid()
            };

            var fileDownloaderRequest = new FileDownloaderRequest
            {
                FilePath = "some/invalid/path/file.json",
                RunId = input.RunId,
                SyncJob = syncJob
            };

            blobStorageRepository.Files.Add(input.FilePath, JsonConvert.SerializeObject(groupMembership));

            var context = new Mock<IDurableOrchestrationContext>();
            context.Setup(x => x.GetInput<GraphUpdaterHttpRequest>()).Returns(input);
            context.Setup(x => x.CallActivityAsync<SyncJob>(It.IsAny<string>(), It.IsAny<JobReaderRequest>())).ReturnsAsync(syncJob);
            context.Setup(x => x.CallActivityAsync<string>(It.IsAny<string>(), It.IsAny<FileDownloaderRequest>()))
                    .Returns(async () => await DownloadFileAsync(fileDownloaderRequest, mockLoggingRepo, blobStorageRepository));
            context.Setup(x => x.CallActivityAsync(It.IsAny<string>(), It.IsAny<LoggerRequest>()))
                    .Callback<string, object>(async (name, request) => await LogMessageAsync((LoggerRequest)request, mockLoggingRepo));
            context.Setup(x => x.CallActivityAsync<bool>(It.IsAny<string>(), It.IsAny<GroupValidatorRequest>()))
                    .Returns(async () => await CheckIfGroupExistsAsync(groupMembership, mockLoggingRepo, mockGraphUpdaterService, mailSenders));
            context.Setup(x => x.CallSubOrchestratorAsync<List<AzureADUser>>(It.IsAny<string>(), It.IsAny<UsersReaderRequest>()))
                    .ReturnsAsync(destinationMembers);
            context.Setup(x => x.CallActivityAsync<DeltaResponse>(It.IsAny<string>(), It.IsAny<DeltaCalculatorRequest>()))
                    .Returns(async () => await GetDeltaResponseAsync(groupMembership, destinationMembers, mockLoggingRepo, deltaCalculatorService));

            mockGraphUpdaterService.Groups.Add(groupMembership.Destination.ObjectId, new Group { Id = groupMembership.Destination.ObjectId.ToString() });
            mockSyncJobRepo.ExistingSyncJobs.Add((syncJob.PartitionKey, syncJob.RowKey), syncJob);

            var orchestrator = new OrchestratorFunction(mockLoggingRepo, mockTelemetryClient, mockGraphUpdaterService, dryRun, mailSenders, thresholdConfig, _gmmResources);
            await Assert.ThrowsExceptionAsync<FileNotFoundException>(async () => await orchestrator.RunOrchestratorAsync(context.Object));
        }

        [TestMethod]
        public async Task RunOrchestratorMissingGroupTest()
        {
            MockLoggingRepository mockLoggingRepo;
            TelemetryClient mockTelemetryClient;
            MockMailRepository mockMailRepo;
            MockGraphUpdaterService mockGraphUpdaterService;
            DryRunValue dryRun;
            DeltaCalculatorService deltaCalculatorService;
            EmailSenderRecipient mailSenders;
            MockSyncJobRepository mockSyncJobRepo;
            MockGraphGroupRepository mockGroupRepo;
            MembershipDifferenceCalculator<AzureADUser> calculator;
            ThresholdConfig thresholdConfig;
            MockLocalizationRepository localizationRepository;

            mockLoggingRepo = new MockLoggingRepository();
            mockTelemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            mockMailRepo = new MockMailRepository();
            mockGraphUpdaterService = new MockGraphUpdaterService(mockMailRepo);
            dryRun = new DryRunValue(false);
            thresholdConfig = new ThresholdConfig(5, 3, 3, 10);
            mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass",
                                            "recipient@domain.com", "recipient@domain.com", "recipient@domain.com");

            calculator = new MembershipDifferenceCalculator<AzureADUser>();
            mockGroupRepo = new MockGraphGroupRepository();
            mockSyncJobRepo = new MockSyncJobRepository();
            localizationRepository = new MockLocalizationRepository();
            deltaCalculatorService = new DeltaCalculatorService(
                                            calculator,
                                            mockSyncJobRepo,
                                            mockLoggingRepo,
                                            mailSenders,
                                            mockGraphUpdaterService,
                                            dryRun,
                                            thresholdConfig,
                                            _gmmResources,
                                            localizationRepository);

            var groupMembership = GetGroupMembership();
            var input = new GraphUpdaterHttpRequest
            {
                FilePath = "/file/path/name.json",
                RunId = groupMembership.RunId,
                JobPartitionKey = groupMembership.SyncJobPartitionKey,
                JobRowKey = groupMembership.SyncJobRowKey
            };

            var destinationMembers = await GetDestinationMembersAsync(groupMembership, mockLoggingRepo);
            var syncJob = new SyncJob
            {
                PartitionKey = groupMembership.SyncJobPartitionKey,
                RowKey = groupMembership.SyncJobRowKey,
                TargetOfficeGroupId = groupMembership.Destination.ObjectId,
                ThresholdPercentageForAdditions = -1,
                ThresholdPercentageForRemovals = -1,
                LastRunTime = DateTime.UtcNow.AddDays(-1),
                Requestor = "user@domail.com",
                RunId = Guid.NewGuid()
            };

            var context = new Mock<IDurableOrchestrationContext>();
            context.Setup(x => x.GetInput<GraphUpdaterHttpRequest>()).Returns(input);
            context.Setup(x => x.CallActivityAsync<SyncJob>(It.IsAny<string>(), It.IsAny<JobReaderRequest>())).ReturnsAsync(syncJob);
            context.Setup(x => x.CallActivityAsync<string>(It.IsAny<string>(), It.IsAny<FileDownloaderRequest>())).ReturnsAsync(JsonConvert.SerializeObject(groupMembership));
            context.Setup(x => x.CallActivityAsync(It.IsAny<string>(), It.IsAny<LoggerRequest>()))
                    .Callback<string, object>(async (name, request) => await LogMessageAsync((LoggerRequest)request, mockLoggingRepo));
            context.Setup(x => x.CallActivityAsync<bool>(It.IsAny<string>(), It.IsAny<GroupValidatorRequest>()))
                    .Returns(async () => await CheckIfGroupExistsAsync(groupMembership, mockLoggingRepo, mockGraphUpdaterService, mailSenders));

            JobStatusUpdaterRequest updateJobRequest = null;
            context.Setup(x => x.CallActivityAsync(It.IsAny<string>(), It.IsAny<JobStatusUpdaterRequest>()))
                    .Callback<string, object>((name, request) =>
                    {
                        updateJobRequest = request as JobStatusUpdaterRequest;
                    });

            var orchestrator = new OrchestratorFunction(mockLoggingRepo, mockTelemetryClient, mockGraphUpdaterService, dryRun, mailSenders, thresholdConfig, _gmmResources);
            var response = await orchestrator.RunOrchestratorAsync(context.Object);

            Assert.AreEqual(SyncStatus.DestinationGroupNotFound, updateJobRequest.Status);
            Assert.IsTrue(response == OrchestrationRuntimeStatus.Completed);
            Assert.IsTrue(mockLoggingRepo.MessagesLogged.Any(x => x.Message.Contains($"Group with ID {groupMembership.Destination.ObjectId} doesn't exist.")));
            Assert.IsTrue(mockLoggingRepo.MessagesLogged.Any(x => x.Message == nameof(OrchestratorFunction) + " function did not complete"));
            Assert.IsNotNull(mockLoggingRepo.SyncJobProperties);
            Assert.AreEqual(mockLoggingRepo.SyncJobProperties["RunId"], syncJob.RunId.ToString());
            Assert.AreEqual(mockLoggingRepo.SyncJobProperties["PartitionKey"], syncJob.PartitionKey);
            Assert.AreEqual(mockLoggingRepo.SyncJobProperties["RowKey"], syncJob.RowKey);
        }

        [TestMethod]
        public async Task RunOrchestratorThresholdExceededTest()
        {
            MockLoggingRepository mockLoggingRepo;
            TelemetryClient mockTelemetryClient;
            MockMailRepository mockMailRepo;
            MockGraphUpdaterService mockGraphUpdaterService;
            DryRunValue dryRun;
            DeltaCalculatorService deltaCalculatorService;
            EmailSenderRecipient mailSenders;
            MockSyncJobRepository mockSyncJobRepo;
            MockGraphGroupRepository mockGroupRepo;
            MembershipDifferenceCalculator<AzureADUser> calculator;
            ThresholdConfig thresholdConfig;
            MockLocalizationRepository localizationRepository;

            mockLoggingRepo = new MockLoggingRepository();
            mockTelemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            mockMailRepo = new MockMailRepository();
            mockGraphUpdaterService = new MockGraphUpdaterService(mockMailRepo);
            dryRun = new DryRunValue(false);
            thresholdConfig = new ThresholdConfig(5, 3, 3, 10);
            mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass",
                                            "recipient@domain.com", "recipient@domain.com", "recipient@domain.com");

            calculator = new MembershipDifferenceCalculator<AzureADUser>();
            mockGroupRepo = new MockGraphGroupRepository();
            mockSyncJobRepo = new MockSyncJobRepository();
            localizationRepository = new MockLocalizationRepository();
            deltaCalculatorService = new DeltaCalculatorService(
                                            calculator,
                                            mockSyncJobRepo,
                                            mockLoggingRepo,
                                            mailSenders,
                                            mockGraphUpdaterService,
                                            dryRun,
                                            thresholdConfig,
                                            _gmmResources,
                                            localizationRepository);

            var groupMembership = GetGroupMembership();
            var input = new GraphUpdaterHttpRequest
            {
                FilePath = "/file/path/name.json",
                RunId = groupMembership.RunId,
                JobPartitionKey = groupMembership.SyncJobPartitionKey,
                JobRowKey = groupMembership.SyncJobRowKey
            };

            var destinationMembers = await GetDestinationMembersAsync(groupMembership, mockLoggingRepo);
            var syncJob = new SyncJob
            {
                PartitionKey = groupMembership.SyncJobPartitionKey,
                RowKey = groupMembership.SyncJobRowKey,
                TargetOfficeGroupId = groupMembership.Destination.ObjectId,
                ThresholdPercentageForAdditions = 80,
                ThresholdPercentageForRemovals = 20,
                LastRunTime = DateTime.UtcNow.AddDays(-1),
                Requestor = "user@domail.com",
                RunId = Guid.NewGuid(),
                ThresholdViolations = 2
            };

            var context = new Mock<IDurableOrchestrationContext>();
            context.Setup(x => x.GetInput<GraphUpdaterHttpRequest>()).Returns(input);
            context.Setup(x => x.CallActivityAsync<SyncJob>(It.IsAny<string>(), It.IsAny<JobReaderRequest>())).ReturnsAsync(syncJob);
            context.Setup(x => x.CallActivityAsync<string>(It.IsAny<string>(), It.IsAny<FileDownloaderRequest>())).ReturnsAsync(JsonConvert.SerializeObject(groupMembership));
            context.Setup(x => x.CallActivityAsync(It.IsAny<string>(), It.IsAny<LoggerRequest>()))
                    .Callback<string, object>(async (name, request) => await LogMessageAsync((LoggerRequest)request, mockLoggingRepo));
            context.Setup(x => x.CallActivityAsync<bool>(It.IsAny<string>(), It.IsAny<GroupValidatorRequest>()))
                    .Returns(async () => await CheckIfGroupExistsAsync(groupMembership, mockLoggingRepo, mockGraphUpdaterService, mailSenders));
            context.Setup(x => x.CallSubOrchestratorAsync<List<AzureADUser>>(It.IsAny<string>(), It.IsAny<UsersReaderRequest>()))
                    .ReturnsAsync(destinationMembers);
            context.Setup(x => x.CallActivityAsync<DeltaResponse>(It.IsAny<string>(), It.IsAny<DeltaCalculatorRequest>()))
                    .Returns(async () => await GetDeltaResponseAsync(groupMembership, destinationMembers, mockLoggingRepo, deltaCalculatorService));

            JobStatusUpdaterRequest updateJobRequest = null;
            context.Setup(x => x.CallActivityAsync(It.IsAny<string>(), It.IsAny<JobStatusUpdaterRequest>()))
                    .Callback<string, object>((name, request) =>
                    {
                        updateJobRequest = request as JobStatusUpdaterRequest;
                    });

            mockGraphUpdaterService.Groups.Add(groupMembership.Destination.ObjectId, new Group { Id = groupMembership.Destination.ObjectId.ToString() });
            mockSyncJobRepo.ExistingSyncJobs.Add((syncJob.PartitionKey, syncJob.RowKey), syncJob);

            var orchestrator = new OrchestratorFunction(mockLoggingRepo, mockTelemetryClient, mockGraphUpdaterService, dryRun, mailSenders, thresholdConfig, _gmmResources);
            var response = await orchestrator.RunOrchestratorAsync(context.Object);

            Assert.IsTrue(mockLoggingRepo.MessagesLogged.Any(x => x.Message.Contains($"is lesser than threshold value {syncJob.ThresholdPercentageForRemovals}")));
            Assert.IsTrue(mockLoggingRepo.MessagesLogged.Any(x => x.Message.Contains($"Threshold exceeded, no changes made to group")));
            Assert.IsTrue(mockLoggingRepo.MessagesLogged.Any(x => x.Message.Contains($"{nameof(DeltaCalculatorFunction)} function completed")));
            Assert.IsTrue(mockLoggingRepo.MessagesLogged.Any(x => x.Message == nameof(OrchestratorFunction) + " function did not complete"));
            Assert.IsTrue(mockMailRepo.SentEmails.First().Content == "SyncThresholdBothEmailBody");
            Assert.AreEqual(SyncStatus.Idle, updateJobRequest.Status);
            Assert.IsTrue(response == OrchestrationRuntimeStatus.Terminated);
            Assert.IsNotNull(mockLoggingRepo.SyncJobProperties);
            Assert.AreEqual(mockLoggingRepo.SyncJobProperties["RunId"], syncJob.RunId.ToString());
            Assert.AreEqual(mockLoggingRepo.SyncJobProperties["PartitionKey"], syncJob.PartitionKey);
            Assert.AreEqual(mockLoggingRepo.SyncJobProperties["RowKey"], syncJob.RowKey);
        }

        [TestMethod]
        public async Task RunOrchestratorThresholdExceededMultipleTimesTest()
        {
            MockLoggingRepository mockLoggingRepo;
            TelemetryClient mockTelemetryClient;
            MockMailRepository mockMailRepo;
            MockGraphUpdaterService mockGraphUpdaterService;
            DryRunValue dryRun;
            DeltaCalculatorService deltaCalculatorService;
            EmailSenderRecipient mailSenders;
            MockSyncJobRepository mockSyncJobRepo;
            MockGraphGroupRepository mockGroupRepo;
            MembershipDifferenceCalculator<AzureADUser> calculator;
            ThresholdConfig thresholdConfig;
            MockLocalizationRepository localizationRepository;

            mockLoggingRepo = new MockLoggingRepository();
            mockTelemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            mockMailRepo = new MockMailRepository();
            mockGraphUpdaterService = new MockGraphUpdaterService(mockMailRepo);
            dryRun = new DryRunValue(false);
            thresholdConfig = new ThresholdConfig(5, 3, 3, 10);
            mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass",
                                            "recipient@domain.com", "recipient@domain.com", "recipient@domain.com");

            calculator = new MembershipDifferenceCalculator<AzureADUser>();
            mockGroupRepo = new MockGraphGroupRepository();
            mockSyncJobRepo = new MockSyncJobRepository();
            localizationRepository = new MockLocalizationRepository();
            deltaCalculatorService = new DeltaCalculatorService(
                                            calculator,
                                            mockSyncJobRepo,
                                            mockLoggingRepo,
                                            mailSenders,
                                            mockGraphUpdaterService,
                                            dryRun,
                                            thresholdConfig,
                                            _gmmResources,
                                            localizationRepository);

            var groupMembership = GetGroupMembership();
            var input = new GraphUpdaterHttpRequest
            {
                FilePath = "/file/path/name.json",
                RunId = groupMembership.RunId,
                JobPartitionKey = groupMembership.SyncJobPartitionKey,
                JobRowKey = groupMembership.SyncJobRowKey
            };

            var destinationMembers = await GetDestinationMembersAsync(groupMembership, mockLoggingRepo);
            var syncJob = new SyncJob
            {
                PartitionKey = groupMembership.SyncJobPartitionKey,
                RowKey = groupMembership.SyncJobRowKey,
                TargetOfficeGroupId = groupMembership.Destination.ObjectId,
                ThresholdPercentageForAdditions = 80,
                ThresholdPercentageForRemovals = 20,
                LastRunTime = DateTime.UtcNow.AddDays(-1),
                Requestor = "user@domail.com",
                RunId = Guid.NewGuid(),
                ThresholdViolations = 0
            };

            mockGraphUpdaterService.Jobs.Add((syncJob.PartitionKey, syncJob.RowKey), syncJob);
            mockGraphUpdaterService.Groups.Add(groupMembership.Destination.ObjectId, new Group { Id = groupMembership.Destination.ObjectId.ToString() });
            mockSyncJobRepo.ExistingSyncJobs.Add((syncJob.PartitionKey, syncJob.RowKey), syncJob);

            var context = new Mock<IDurableOrchestrationContext>();
            context.Setup(x => x.GetInput<GraphUpdaterHttpRequest>()).Returns(input);

            SyncJob syncJobResponse = null;
            context.Setup(x => x.CallActivityAsync<SyncJob>(It.IsAny<string>(), It.IsAny<JobReaderRequest>()))
                     .Callback<string, object>(async (name, request) =>
                     {
                         var getJobRequest = request as JobReaderRequest;
                         syncJobResponse = await RunJobReaderFunctionAsync(mockLoggingRepo, mockGraphUpdaterService, getJobRequest);
                     })
                     .ReturnsAsync(() => syncJobResponse);
            context.Setup(x => x.CallActivityAsync<string>(It.IsAny<string>(), It.IsAny<FileDownloaderRequest>())).ReturnsAsync(JsonConvert.SerializeObject(groupMembership));
            context.Setup(x => x.CallActivityAsync(It.IsAny<string>(), It.IsAny<LoggerRequest>()))
                    .Callback<string, object>(async (name, request) => await LogMessageAsync((LoggerRequest)request, mockLoggingRepo));
            context.Setup(x => x.CallActivityAsync<bool>(It.IsAny<string>(), It.IsAny<GroupValidatorRequest>()))
                    .Returns(async () => await CheckIfGroupExistsAsync(groupMembership, mockLoggingRepo, mockGraphUpdaterService, mailSenders));
            context.Setup(x => x.CallSubOrchestratorAsync<List<AzureADUser>>(It.IsAny<string>(), It.IsAny<UsersReaderRequest>()))
                    .ReturnsAsync(destinationMembers);
            context.Setup(x => x.CallActivityAsync<DeltaResponse>(It.IsAny<string>(), It.IsAny<DeltaCalculatorRequest>()))
                    .Returns(async () => await GetDeltaResponseAsync(groupMembership, destinationMembers, mockLoggingRepo, deltaCalculatorService));

            JobStatusUpdaterRequest updateJobRequest = null;
            context.Setup(x => x.CallActivityAsync(It.IsAny<string>(), It.IsAny<JobStatusUpdaterRequest>()))
                    .Callback<string, object>(async (name, request) =>
                    {
                        updateJobRequest = request as JobStatusUpdaterRequest;
                        await RunJobStatusUpdaterFunctionAsync(mockLoggingRepo, mockGraphUpdaterService, updateJobRequest);
                    });

            OrchestrationRuntimeStatus response = OrchestrationRuntimeStatus.Unknown;
            var thresholdViolationCountLimit = 3;
            for (var thresholdViolationCount = 1; thresholdViolationCount <= thresholdViolationCountLimit; thresholdViolationCount++)
            {
                var orchestrator = new OrchestratorFunction(mockLoggingRepo, mockTelemetryClient, mockGraphUpdaterService, dryRun, mailSenders, thresholdConfig, _gmmResources);
                response = await orchestrator.RunOrchestratorAsync(context.Object);
            }

            Assert.IsTrue(mockLoggingRepo.MessagesLogged.Any(x => x.Message.Contains($"is lesser than threshold value {syncJob.ThresholdPercentageForRemovals}")));
            Assert.IsTrue(mockLoggingRepo.MessagesLogged.Any(x => x.Message.Contains($"Threshold exceeded, no changes made to group")));
            Assert.IsTrue(mockLoggingRepo.MessagesLogged.Any(x => x.Message.Contains($"{nameof(DeltaCalculatorFunction)} function completed")));
            Assert.IsTrue(mockLoggingRepo.MessagesLogged.Any(x => x.Message == nameof(OrchestratorFunction) + " function did not complete"));
            Assert.IsTrue(mockMailRepo.SentEmails.First().Content == "SyncThresholdBothEmailBody");
            Assert.AreEqual(SyncStatus.Idle, updateJobRequest.Status);
            Assert.AreEqual(thresholdViolationCountLimit, updateJobRequest.ThresholdViolations);
            Assert.AreEqual(thresholdViolationCountLimit, mockGraphUpdaterService.Jobs[(syncJob.PartitionKey, syncJob.RowKey)].ThresholdViolations);
            Assert.IsTrue(response == OrchestrationRuntimeStatus.Terminated);
            Assert.IsNotNull(mockLoggingRepo.SyncJobProperties);
            Assert.AreEqual(mockLoggingRepo.SyncJobProperties["RunId"], syncJob.RunId.ToString());
            Assert.AreEqual(mockLoggingRepo.SyncJobProperties["PartitionKey"], syncJob.PartitionKey);
            Assert.AreEqual(mockLoggingRepo.SyncJobProperties["RowKey"], syncJob.RowKey);
        }

        private async Task<SyncJob> RunJobReaderFunctionAsync(MockLoggingRepository loggingRepository, MockGraphUpdaterService graphUpdaterService, JobReaderRequest request)
        {
            var jobReaderFunction = new JobReaderFunction(loggingRepository, graphUpdaterService);
            var syncJob = await jobReaderFunction.GetSyncJobAsync(request);
            return syncJob;
        }

        private async Task RunJobStatusUpdaterFunctionAsync(MockLoggingRepository loggingRepository, MockGraphUpdaterService graphUpdaterService, JobStatusUpdaterRequest request)
        {
            var jobStatusUpdaterFunction = new JobStatusUpdaterFunction(loggingRepository, graphUpdaterService);
            await jobStatusUpdaterFunction.UpdateJobStatusAsync(request);
        }

        private async Task<bool> CheckIfGroupExistsAsync(
                GroupMembership groupMembership,
                MockLoggingRepository mockLoggingRepo,
                MockGraphUpdaterService mockGraphUpdaterService,
                EmailSenderRecipient mailSenders)
        {
            var request = new GroupValidatorRequest
            {
                RunId = groupMembership.RunId,
                GroupId = groupMembership.Destination.ObjectId,
                JobPartitionKey = groupMembership.SyncJobPartitionKey,
                JobRowKey = groupMembership.SyncJobRowKey
            };
            var groupValidatorFunction = new GroupValidatorFunction(mockLoggingRepo, mockGraphUpdaterService, mailSenders);

            return await groupValidatorFunction.ValidateGroupAsync(request);
        }

        private async Task<List<AzureADUser>> GetDestinationMembersAsync(GroupMembership groupMembership, MockLoggingRepository mockLoggingRepo)
        {
            var syncJob = new SyncJob
            {
                PartitionKey = groupMembership.SyncJobPartitionKey,
                RowKey = groupMembership.SyncJobRowKey,
                TargetOfficeGroupId = groupMembership.Destination.ObjectId,
                ThresholdPercentageForAdditions = 80,
                ThresholdPercentageForRemovals = 20,
                LastRunTime = DateTime.UtcNow.AddDays(-1),
                Requestor = "user@domail.com",
                RunId = Guid.NewGuid()
            };

            var request = new UsersReaderRequest
            {
                SyncJob = syncJob
            };

            var context = new Mock<IDurableOrchestrationContext>();
            context.Setup(x => x.GetInput<UsersReaderRequest>()).Returns(request);
            context.Setup(x => x.CallActivityAsync<UsersPageResponse>(It.IsAny<string>(), It.IsAny<UsersReaderRequest>()))
                    .ReturnsAsync(GetUsersPageResponse(true));
            context.Setup(x => x.CallActivityAsync<UsersPageResponse>(It.IsAny<string>(), It.IsAny<SubsequentUsersReaderRequest>()))
                    .ReturnsAsync(GetUsersPageResponse(false));

            var usersReaderFunction = new UsersReaderSubOrchestratorFunction();
            var users = await usersReaderFunction.RunSubOrchestratorAsync(context.Object);
            return users;
        }

        private UsersPageResponse GetUsersPageResponse(bool hasNextPage)
        {
            var page = new Mock<IGroupTransitiveMembersCollectionWithReferencesPage>();
            var users = new List<AzureADUser>();
            var nonUserObjects = new Dictionary<string, int>();

            for (int i = 0; i < 10; i++)
            {
                users.Add(new AzureADUser { ObjectId = Guid.NewGuid() });
                if (i % 2 == 0)
                    nonUserObjects.Add($"object.type.{i}", i);
            }

            if (!hasNextPage)
                nonUserObjects.Add("unique.object.type", 5);


            return new UsersPageResponse
            {
                Members = users,
                MembersPage = page.Object,
                NextPageUrl = hasNextPage ? "http://next.page" : null,
                NonUserGraphObjects = nonUserObjects
            };
        }

        private async Task<DeltaResponse> GetDeltaResponseAsync(
                GroupMembership groupMembership,
                List<AzureADUser> membersFromDestinationGroup,
                MockLoggingRepository mockLoggingRepo,
                DeltaCalculatorService deltaCalculatorService)
        {
            var request = new DeltaCalculatorRequest
            {
                GroupMembership = groupMembership,
                MembersFromDestinationGroup = membersFromDestinationGroup,
                RunId = Guid.NewGuid(),
            };

            var calculatorFunction = new DeltaCalculatorFunction(mockLoggingRepo, deltaCalculatorService);
            return await calculatorFunction.CalculateDeltaAsync(request);
        }

        private async Task<string> DownloadFileAsync(FileDownloaderRequest request, MockLoggingRepository mockLoggingRepo, MockBlobStorageRepository blobStorageRepository)
        {
            var function = new FileDownloaderFunction(mockLoggingRepo, blobStorageRepository);
            var fileContent = await function.DownloadFileAsync(request);
            return fileContent;
        }

        private GroupMembership GetGroupMembership()
        {
            var json =
            "{" +
            "  'Sources': [" +
            "    {" +
            "      'ObjectId': '8032abf6-b4b1-45b1-8e7e-40b0bd16d6eb'" +
            "    }" +
            "  ]," +
            "  'Destination': {" +
            "    'ObjectId': 'dc04c21f-091a-44a9-a661-9211dd9ccf35'" +
            "  }," +
            "  'SourceMembers': []," +
            "  'RunId': '501f6c70-8fe1-496f-8446-befb15b5249a'," +
            "  'SyncJobRowKey': '0a4cc250-69a0-4019-8298-96bf492aca01'," +
            "  'SyncJobPartitionKey': '2021-01-01'," +
            "  'Errored': false," +
            "  'IsLastMessage': true" +
            "}";
            var groupMembership = JsonConvert.DeserializeObject<GroupMembership>(json);

            return groupMembership;
        }

        private async Task LogMessageAsync(LoggerRequest loggerRequest, MockLoggingRepository mockLoggingRepository)
        {
            var loggerFunction = new LoggerFunction(mockLoggingRepository);
            await loggerFunction.LogMessageAsync(loggerRequest);
        }
    }
}

