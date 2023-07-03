// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using DIConcreteTypes;
using Hosts.GraphUpdater;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Graph.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.ServiceBus;
using Moq;
using Newtonsoft.Json;
using Repositories.Mocks;
using Services.Contracts;
using Services.Tests.Mocks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ExecutionContext = Microsoft.Azure.WebJobs.ExecutionContext;

namespace Services.Tests
{
    [TestClass]
    public class OrchestratorTests
    {
        TelemetryClient _telemetryClient;

        GMMResources _gmmResources = new GMMResources
        {
            LearnMoreAboutGMMUrl = "http://learn-more-url"
        };

        [TestMethod]
        public async Task RunOrchestratorValidSyncTest()
        {
            MockLoggingRepository mockLoggingRepo;
            TelemetryClient mockTelemetryClient;
            MockDeltaCachingConfig mockDeltaCachingConfig;
            MockMailRepository mockMailRepo;
            MockGraphUpdaterService mockGraphUpdaterService;
            DryRunValue dryRun;
            EmailSenderRecipient mailSenders;
            MockSyncJobRepository mockSyncJobRepo;
            MockGraphGroupRepository mockGroupRepo;
            ThresholdConfig thresholdConfig;
            MockLocalizationRepository localizationRepository;
            MockBlobStorageRepository blobStorageRepository;

            mockDeltaCachingConfig = new MockDeltaCachingConfig();
            mockLoggingRepo = new MockLoggingRepository();
            mockTelemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            mockMailRepo = new MockMailRepository();
            mockGraphUpdaterService = new MockGraphUpdaterService(mockMailRepo);
            dryRun = new DryRunValue(false);
            thresholdConfig = new ThresholdConfig(5, 3, 3, 10);
            mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass",
                                            "recipient@domain.com", "recipient@domain.com", "recipient@domain.com");

            mockGroupRepo = new MockGraphGroupRepository();
            mockSyncJobRepo = new MockSyncJobRepository();
            localizationRepository = new MockLocalizationRepository();
            blobStorageRepository = new MockBlobStorageRepository();

            var groupMembership = GetGroupMembership();
            var destinationMembers = GetGroupMembership();
            var syncJob = new SyncJob
            {
                Id = groupMembership.SyncJobId,   
                TargetOfficeGroupId = groupMembership.Destination.ObjectId,
                Destination = $"[{{\"value\":\"{groupMembership.Destination.ObjectId}\",\"type\":\"GraphUpdater\"}}]",
                ThresholdPercentageForAdditions = -1,
                ThresholdPercentageForRemovals = -1,
                LastRunTime = DateTime.UtcNow.AddDays(-1),
                Requestor = "user@domail.com",
                Query = "[{ \"type\": \"SecurityGroup\", \"sources\": [\"da144736-962b-4879-a304-acd9f5221e78\"]}]",
                RunId = groupMembership.RunId
            };

            mockLoggingRepo.SetSyncJobProperties(syncJob.RunId.Value, syncJob.ToDictionary());

            var input = new MembershipHttpRequest
            {
                FilePath = "/file/path/name.json",
                SyncJob = syncJob
            };

            var fileDownloaderRequest = new FileDownloaderRequest
            {
                FilePath = input.FilePath,
                SyncJob = syncJob
            };

            var jobReaderRequest = new JobReaderRequest
            {
                JobId = syncJob.Id,                
                RunId = syncJob.RunId.Value
            };

            _telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());

            mockGraphUpdaterService.Jobs.Add((syncJob.PartitionKey, syncJob.RowKey), syncJob);
            mockGraphUpdaterService.Groups.Add(groupMembership.Destination.ObjectId, new Group { Id = groupMembership.Destination.ObjectId.ToString() });
            blobStorageRepository.Files.Add(input.FilePath, JsonConvert.SerializeObject(groupMembership));

            var context = new Mock<IDurableOrchestrationContext>();
            var executionContext = new Mock<ExecutionContext>();
            context.Setup(x => x.GetInput<MembershipHttpRequest>()).Returns(input);
            context.Setup(x => x.CallActivityAsync(It.Is<string>(x => x == nameof(TelemetryTrackerFunction)), It.IsAny<TelemetryTrackerRequest>()))
                    .Callback<string, object>(async (name, request) =>
                    {
                        var telemetryRequest = request as TelemetryTrackerRequest;
                        await CallTelemetryTrackerFunctionAsync(telemetryRequest, mockLoggingRepo);
                    });
            context.Setup(x => x.CallActivityAsync<SyncJob>(It.IsAny<string>(), It.IsAny<JobReaderRequest>()))
                    .Returns(async () => await RunJobReaderFunctionAsync(mockLoggingRepo, mockGraphUpdaterService, jobReaderRequest));
            context.Setup(x => x.CallActivityAsync<string>(It.IsAny<string>(), It.IsAny<FileDownloaderRequest>()))
                    .ReturnsAsync(await DownloadFileAsync(fileDownloaderRequest, mockLoggingRepo, blobStorageRepository));
            context.Setup(x => x.CallActivityAsync(It.IsAny<string>(), It.IsAny<LoggerRequest>()))
                    .Callback<string, object>(async (name, request) => await CallLogMessageFunctionAsync((LoggerRequest)request, mockLoggingRepo));
            context.Setup(x => x.CallActivityAsync<bool>(It.IsAny<string>(), It.IsAny<GroupValidatorRequest>()))
                    .Returns(async () => await CheckIfGroupExistsAsync(groupMembership, mockLoggingRepo, mockGraphUpdaterService, mailSenders));
            context.Setup(x => x.CallSubOrchestratorAsync<GroupUpdaterSubOrchestratorResponse>(It.IsAny<string>(), It.IsAny<GroupUpdaterRequest>()))
                .Returns(() => Task.FromResult(new GroupUpdaterSubOrchestratorResponse() { SuccessCount = 1, UsersNotFound = new List<AzureADUser>(), UsersAlreadyExist = new List<AzureADUser>() }));
            context.Setup(x => x.CallActivityAsync(nameof(JobStatusUpdaterFunction), It.IsAny<JobStatusUpdaterRequest>()))
                   .Callback<string, object>(async (name, request) =>
                   {
                       await CallJobStatusUpdaterFunctionAsync(mockLoggingRepo, mockGraphUpdaterService, request as JobStatusUpdaterRequest);
                   });

            var orchestrator = new OrchestratorFunction(mockTelemetryClient, mockGraphUpdaterService, mailSenders, _gmmResources, mockLoggingRepo, mockDeltaCachingConfig);
            var response = await orchestrator.RunOrchestratorAsync(context.Object, executionContext.Object);

            Assert.IsTrue(response == OrchestrationRuntimeStatus.Completed);
            Assert.IsTrue(mockLoggingRepo.MessagesLogged.Any(x => x.Message == nameof(OrchestratorFunction) + " function completed"));

            context.Verify(x => x.CallSubOrchestratorAsync<GroupUpdaterSubOrchestratorResponse>(It.IsAny<string>(), It.IsAny<GroupUpdaterRequest>()), Times.Exactly(2));

            var logProperties = mockLoggingRepo.SyncJobPropertiesHistory[syncJob.RunId.Value].Properties;

            Assert.IsNotNull(mockLoggingRepo.SyncJobProperties);
            Assert.AreEqual(logProperties["RunId"], syncJob.RunId.ToString());
            Assert.AreEqual(logProperties["PartitionKey"], syncJob.PartitionKey);
            Assert.AreEqual(logProperties["RowKey"], syncJob.RowKey);
            Assert.AreEqual(SyncStatus.Idle.ToString(), mockGraphUpdaterService.Jobs[(syncJob.PartitionKey, syncJob.RowKey)].Status);
        }

        [TestMethod]
        public async Task RunOrchestratorInitialSyncTest()
        {
            MockDeltaCachingConfig mockDeltaCachingConfig;
            MockLoggingRepository mockLoggingRepo;
            TelemetryClient mockTelemetryClient;
            MockMailRepository mockMailRepo;
            MockGraphUpdaterService mockGraphUpdaterService;
            DryRunValue dryRun;
            EmailSenderRecipient mailSenders;
            MockSyncJobRepository mockSyncJobRepo;
            MockGraphGroupRepository mockGroupRepo;
            ThresholdConfig thresholdConfig;
            MockLocalizationRepository localizationRepository;

            mockDeltaCachingConfig = new MockDeltaCachingConfig();
            mockLoggingRepo = new MockLoggingRepository();
            mockTelemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            mockMailRepo = new MockMailRepository();
            mockGraphUpdaterService = new MockGraphUpdaterService(mockMailRepo);
            dryRun = new DryRunValue(false);
            thresholdConfig = new ThresholdConfig(5, 3, 3, 10);
            mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass",
                                            "recipient@domain.com", "recipient@domain.com", "recipient@domain.com");


            mockGroupRepo = new MockGraphGroupRepository();
            mockSyncJobRepo = new MockSyncJobRepository();
            localizationRepository = new MockLocalizationRepository();

            var groupMembership = GetGroupMembership();
            var destinationMembers = GetGroupMembership();
            var syncJob = new SyncJob
            {
                Id = groupMembership.SyncJobId,
                TargetOfficeGroupId = groupMembership.Destination.ObjectId,
                Destination = $"[{{\"value\":\"{groupMembership.Destination.ObjectId}\",\"type\":\"GraphUpdater\"}}]",
                ThresholdPercentageForAdditions = -1,
                ThresholdPercentageForRemovals = -1,
                LastRunTime = DateTime.FromFileTimeUtc(0),
                Requestor = "user@domail.com",
                Query = "[{ \"type\": \"SecurityGroup\", \"sources\": [\"da144736-962b-4879-a304-acd9f5221e78\"]}]",
                RunId = Guid.NewGuid()
            };

            mockLoggingRepo.SetSyncJobProperties(syncJob.RunId.Value, syncJob.ToDictionary());

            var input = new MembershipHttpRequest
            {
                FilePath = "/file/path/name.json",
                SyncJob = syncJob
            };

            var owners = new List<AzureADUser>();
            for (int i = 0; i < 10; i++)
            {
                owners.Add(new AzureADUser
                {
                    ObjectId = Guid.NewGuid(),
                    Mail = $"user{i}@mydomain.com"
                });
            }

            var ownerEmails = string.Join(";", owners.Where(x => !string.IsNullOrWhiteSpace(x.Mail)).Select(x => x.Mail));

            var groupNameReaderRequest = new GroupNameReaderRequest { GroupId = syncJob.TargetOfficeGroupId };
            var groupOwnersReaderRequest = new GroupOwnersReaderRequest { GroupId = syncJob.TargetOfficeGroupId };

            mockGraphUpdaterService.Groups.Add(groupMembership.Destination.ObjectId,
                                                new Group
                                                {
                                                    Id = groupMembership.Destination.ObjectId.ToString(),
                                                    DisplayName = "Test Group"
                                                });


            mockSyncJobRepo.ExistingSyncJobs.Add((syncJob.PartitionKey, syncJob.RowKey), syncJob);

            Mock<IGraphUpdaterService> graphUpdaterService = new Mock<IGraphUpdaterService>();
            graphUpdaterService.Setup(x => x.GetGroupOwnersAsync(It.IsAny<Guid>(), It.IsAny<int>())).ReturnsAsync(owners);

            var context = new Mock<IDurableOrchestrationContext>();
            var executionContext = new Mock<ExecutionContext>();
            context.Setup(x => x.GetInput<MembershipHttpRequest>()).Returns(input);
            context.Setup(x => x.CallActivityAsync<SyncJob>(It.IsAny<string>(), It.IsAny<JobReaderRequest>())).ReturnsAsync(syncJob);
            context.Setup(x => x.CallActivityAsync<string>(It.IsAny<string>(), It.IsAny<FileDownloaderRequest>())).ReturnsAsync(JsonConvert.SerializeObject(groupMembership));
            context.Setup(x => x.CallActivityAsync(It.IsAny<string>(), It.IsAny<LoggerRequest>()))
                    .Callback<string, object>(async (name, request) => await CallLogMessageFunctionAsync((LoggerRequest)request, mockLoggingRepo));
            context.Setup(x => x.CallActivityAsync<bool>(It.IsAny<string>(), It.IsAny<GroupValidatorRequest>()))
                    .Returns(async () => await CheckIfGroupExistsAsync(groupMembership, mockLoggingRepo, mockGraphUpdaterService, mailSenders));
            context.Setup(x => x.CallActivityAsync<string>(It.IsAny<string>(), It.IsAny<GroupNameReaderRequest>()))
                    .Returns(async () => await CallGroupNameReaderFunctionAsync(mockLoggingRepo, mockGraphUpdaterService, groupNameReaderRequest));
            context.Setup(x => x.CallActivityAsync<List<AzureADUser>>(It.IsAny<string>(), It.IsAny<GroupOwnersReaderRequest>()))
                    .Returns(async () => await CallGroupOwnersReaderFunctionAsync(mockLoggingRepo, graphUpdaterService.Object, groupOwnersReaderRequest));
            context.Setup(x => x.CallSubOrchestratorAsync<GroupUpdaterSubOrchestratorResponse>(It.IsAny<string>(), It.IsAny<GroupUpdaterRequest>()))
                    .Returns(() => Task.FromResult(new GroupUpdaterSubOrchestratorResponse() { SuccessCount = 1, UsersNotFound = new List<AzureADUser>(), UsersAlreadyExist = new List<AzureADUser>() }));

            context.Setup(x => x.CallActivityAsync(It.IsAny<string>(), It.IsAny<EmailSenderRequest>()))
                    .Callback<string, object>(async (name, request) =>
                    {
                        var emailSenderFunction = new EmailSenderFunction(mockLoggingRepo, mockGraphUpdaterService);
                        await emailSenderFunction.SendEmailAsync((EmailSenderRequest)request);
                    });

            var orchestrator = new OrchestratorFunction(mockTelemetryClient, mockGraphUpdaterService, mailSenders, _gmmResources, mockLoggingRepo, mockDeltaCachingConfig);
            var response = await orchestrator.RunOrchestratorAsync(context.Object, executionContext.Object);

            Assert.IsTrue(response == OrchestrationRuntimeStatus.Completed);
            Assert.IsTrue(mockLoggingRepo.MessagesLogged.Any(x => x.Message == nameof(OrchestratorFunction) + " function completed"));

            var logProperties = mockLoggingRepo.SyncJobPropertiesHistory[syncJob.RunId.Value].Properties;

            Assert.IsNotNull(mockLoggingRepo.SyncJobProperties);
            Assert.AreEqual(logProperties["RunId"], syncJob.RunId.ToString());
            Assert.AreEqual(logProperties["PartitionKey"], syncJob.PartitionKey);
            Assert.AreEqual(logProperties["RowKey"], syncJob.RowKey);
            Assert.AreEqual(1, mockMailRepo.SentEmails.Count);
            Assert.AreEqual(7, mockMailRepo.SentEmails[0].AdditionalContentParams.Length);
            Assert.AreEqual(ownerEmails, mockMailRepo.SentEmails[0].ToEmailAddresses);

            context.Verify(x => x.CallSubOrchestratorAsync<GroupUpdaterSubOrchestratorResponse>(It.IsAny<string>(), It.IsAny<GroupUpdaterRequest>()), Times.Exactly(2));
        }

        [TestMethod]
        public async Task RunOrchestratorExceptionTest()
        {
            MockLoggingRepository mockLoggingRepo;
            TelemetryClient mockTelemetryClient;
            MockMailRepository mockMailRepo;
            MockGraphUpdaterService mockGraphUpdaterService;
            DryRunValue dryRun;
            EmailSenderRecipient mailSenders;
            MockSyncJobRepository mockSyncJobRepo;
            MockGraphGroupRepository mockGroupRepo;
            ThresholdConfig thresholdConfig;
            MockLocalizationRepository localizationRepository;
            MockBlobStorageRepository blobStorageRepository;
            MockDeltaCachingConfig mockDeltaCachingConfig;

            mockDeltaCachingConfig = new MockDeltaCachingConfig();
            blobStorageRepository = new MockBlobStorageRepository();
            mockLoggingRepo = new MockLoggingRepository();
            mockTelemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            mockMailRepo = new MockMailRepository();
            mockGraphUpdaterService = new MockGraphUpdaterService(mockMailRepo);
            dryRun = new DryRunValue(false);
            thresholdConfig = new ThresholdConfig(5, 3, 3, 10);
            mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass",
                                            "recipient@domain.com", "recipient@domain.com", "recipient@domain.com");


            mockGroupRepo = new MockGraphGroupRepository();
            mockSyncJobRepo = new MockSyncJobRepository();
            localizationRepository = new MockLocalizationRepository();

            var groupMembership = GetGroupMembership();
            var destinationMembers = GetGroupMembership();
            var syncJob = new SyncJob
            {
                Id = groupMembership.SyncJobId,
                TargetOfficeGroupId = groupMembership.Destination.ObjectId,
                Destination = $"[{{\"value\":\"{groupMembership.Destination.ObjectId}\",\"type\":\"GraphUpdater\"}}]",
                ThresholdPercentageForAdditions = -1,
                ThresholdPercentageForRemovals = -1,
                LastRunTime = DateTime.FromFileTimeUtc(0),
                Requestor = "user@domail.com",
                Query = "[{ \"type\": \"SecurityGroup\", \"sources\": [\"da144736-962b-4879-a304-acd9f5221e78\"]}]",
                RunId = Guid.NewGuid()
            };

            mockLoggingRepo.SetSyncJobProperties(syncJob.RunId.Value, syncJob.ToDictionary());

            var input = new MembershipHttpRequest
            {
                FilePath = "/file/path/name.json",
                SyncJob = syncJob
            };

            var fileDownloaderRequest = new FileDownloaderRequest
            {
                FilePath = input.FilePath,
                SyncJob = syncJob
            };

            blobStorageRepository.Files.Add(input.FilePath, JsonConvert.SerializeObject(groupMembership));

            var context = new Mock<IDurableOrchestrationContext>();
            var executionContext = new Mock<ExecutionContext>();
            context.Setup(x => x.GetInput<MembershipHttpRequest>()).Returns(input);
            context.Setup(x => x.CallActivityAsync<SyncJob>(It.IsAny<string>(), It.IsAny<JobReaderRequest>())).ReturnsAsync(syncJob);
            context.Setup(x => x.CallActivityAsync<string>(It.IsAny<string>(), It.IsAny<FileDownloaderRequest>()))
                    .ReturnsAsync(await DownloadFileAsync(fileDownloaderRequest, mockLoggingRepo, blobStorageRepository));
            context.Setup(x => x.CallActivityAsync(It.IsAny<string>(), It.IsAny<LoggerRequest>()))
                    .Callback<string, object>(async (name, request) => await CallLogMessageFunctionAsync((LoggerRequest)request, mockLoggingRepo));
            context.Setup(x => x.CallActivityAsync<bool>(It.IsAny<string>(), It.IsAny<GroupValidatorRequest>()))
                    .ReturnsAsync(true);

            JobStatusUpdaterRequest updateJobRequest = null;
            context.Setup(x => x.CallActivityAsync(It.IsAny<string>(), It.IsAny<JobStatusUpdaterRequest>()))
                    .Callback<string, object>((name, request) =>
                    {
                        updateJobRequest = request as JobStatusUpdaterRequest;
                    });

            context.Setup(x => x.CallSubOrchestratorAsync<GroupUpdaterSubOrchestratorResponse>(It.IsAny<string>(), It.IsAny<GroupUpdaterRequest>()))
                .Returns(() => Task.FromResult(new GroupUpdaterSubOrchestratorResponse() { SuccessCount = 1, UsersNotFound = new List<AzureADUser>(), UsersAlreadyExist = new List<AzureADUser>() }));

            var orchestrator = new OrchestratorFunction(mockTelemetryClient, mockGraphUpdaterService, mailSenders, _gmmResources, mockLoggingRepo, mockDeltaCachingConfig);
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(async () => await orchestrator.RunOrchestratorAsync(context.Object, executionContext.Object));

            Assert.IsFalse(mockLoggingRepo.MessagesLogged.Any(x => x.Message == nameof(OrchestratorFunction) + " function completed"));
            Assert.IsTrue(mockLoggingRepo.MessagesLogged.Any(x => x.Message.Contains("Caught unexpected exception, marking sync job as errored.")));
            Assert.AreEqual(SyncStatus.Error, updateJobRequest.Status);

            var logProperties = mockLoggingRepo.SyncJobPropertiesHistory[syncJob.RunId.Value].Properties;

            Assert.IsNotNull(mockLoggingRepo.SyncJobProperties);
            Assert.AreEqual(logProperties["RunId"], syncJob.RunId.ToString());
            Assert.AreEqual(logProperties["PartitionKey"], syncJob.PartitionKey);
            Assert.AreEqual(logProperties["RowKey"], syncJob.RowKey);
        }

        [TestMethod]
        public async Task RunSyncJobNotFoundTest()
        {
            MockLoggingRepository mockLoggingRepo;
            TelemetryClient mockTelemetryClient;
            MockMailRepository mockMailRepo;
            MockGraphUpdaterService mockGraphUpdaterService;
            DryRunValue dryRun;
            EmailSenderRecipient mailSenders;
            MockSyncJobRepository mockSyncJobRepo;
            MockGraphGroupRepository mockGroupRepo;
            ThresholdConfig thresholdConfig;
            MockLocalizationRepository localizationRepository;
            MockBlobStorageRepository blobStorageRepository;
            MockDeltaCachingConfig mockDeltaCachingConfig;

            mockDeltaCachingConfig = new MockDeltaCachingConfig();
            blobStorageRepository = new MockBlobStorageRepository();
            mockLoggingRepo = new MockLoggingRepository();
            mockTelemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            mockMailRepo = new MockMailRepository();
            mockGraphUpdaterService = new MockGraphUpdaterService(mockMailRepo);
            dryRun = new DryRunValue(false);
            thresholdConfig = new ThresholdConfig(5, 3, 3, 10);
            mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass",
                                            "recipient@domain.com", "recipient@domain.com", "recipient@domain.com");


            mockGroupRepo = new MockGraphGroupRepository();
            mockSyncJobRepo = new MockSyncJobRepository();
            localizationRepository = new MockLocalizationRepository();

            var groupMembership = GetGroupMembership();
            var destinationMembers = GetGroupMembership();
            SyncJob syncJob = null;

            var input = new MembershipHttpRequest
            {
                FilePath = "/file/path/name.json",
                SyncJob = syncJob
            };

            blobStorageRepository.Files.Add(input.FilePath, JsonConvert.SerializeObject(groupMembership));

            var context = new Mock<IDurableOrchestrationContext>();
            var executionContext = new Mock<ExecutionContext>();
            context.Setup(x => x.GetInput<MembershipHttpRequest>()).Returns(input);
            context.Setup(x => x.CallActivityAsync<SyncJob>(It.IsAny<string>(), It.IsAny<JobReaderRequest>())).ReturnsAsync(syncJob);
            context.Setup(x => x.CallActivityAsync(It.IsAny<string>(), It.IsAny<LoggerRequest>()))
                    .Callback<string, object>(async (name, request) => await CallLogMessageFunctionAsync((LoggerRequest)request, mockLoggingRepo));

            var orchestrator = new OrchestratorFunction(mockTelemetryClient, mockGraphUpdaterService, mailSenders, _gmmResources, mockLoggingRepo, mockDeltaCachingConfig);
            await orchestrator.RunOrchestratorAsync(context.Object, executionContext.Object);

            Assert.IsFalse(mockLoggingRepo.MessagesLogged.Any(x => x.Message == nameof(OrchestratorFunction) + " function completed"));
            Assert.IsTrue(mockLoggingRepo.MessagesLogged.Any(x => x.Message.Contains("Caught unexpected exception, marking sync job as errored.")));
            Assert.IsTrue(mockLoggingRepo.MessagesLogged.Any(x => x.Message.Contains("SyncJob is null")));
        }

        [TestMethod]
        public async Task RunOrchestratorFileNotFoundExceptionTest()
        {
            MockLoggingRepository mockLoggingRepo;
            TelemetryClient mockTelemetryClient;
            MockMailRepository mockMailRepo;
            MockGraphUpdaterService mockGraphUpdaterService;
            DryRunValue dryRun;

            EmailSenderRecipient mailSenders;
            MockSyncJobRepository mockSyncJobRepo;
            MockGraphGroupRepository mockGroupRepo;

            ThresholdConfig thresholdConfig;
            MockLocalizationRepository localizationRepository;
            MockBlobStorageRepository blobStorageRepository;
            MockDeltaCachingConfig mockDeltaCachingConfig;

            mockDeltaCachingConfig = new MockDeltaCachingConfig();
            mockLoggingRepo = new MockLoggingRepository();
            mockTelemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            mockMailRepo = new MockMailRepository();
            mockGraphUpdaterService = new MockGraphUpdaterService(mockMailRepo);
            dryRun = new DryRunValue(false);
            thresholdConfig = new ThresholdConfig(5, 3, 3, 10);
            mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass",
                                            "recipient@domain.com", "recipient@domain.com", "recipient@domain.com");


            mockGroupRepo = new MockGraphGroupRepository();
            mockSyncJobRepo = new MockSyncJobRepository();
            localizationRepository = new MockLocalizationRepository();
            blobStorageRepository = new MockBlobStorageRepository();

            var groupMembership = GetGroupMembership();
            var destinationMembers = GetGroupMembership();
            var syncJob = new SyncJob
            {
                Id = groupMembership.SyncJobId,
                TargetOfficeGroupId = groupMembership.Destination.ObjectId,
                Destination = $"[{{\"value\":\"{groupMembership.Destination.ObjectId}\",\"type\":\"GraphUpdater\"}}]",
                ThresholdPercentageForAdditions = -1,
                ThresholdPercentageForRemovals = -1,
                LastRunTime = DateTime.UtcNow.AddDays(-1),
                Requestor = "user@domail.com",
                Query = "[{ \"type\": \"SecurityGroup\", \"sources\": [\"da144736-962b-4879-a304-acd9f5221e78\"]}]",
                RunId = Guid.NewGuid()
            };

            var input = new MembershipHttpRequest
            {
                FilePath = "/file/path/name.json",
                SyncJob = syncJob
            };

            var fileDownloaderRequest = new FileDownloaderRequest
            {
                FilePath = "some/invalid/path/file.json",
                SyncJob = syncJob
            };

            blobStorageRepository.Files.Add(input.FilePath, JsonConvert.SerializeObject(groupMembership));

            var context = new Mock<IDurableOrchestrationContext>();
            var executionContext = new Mock<ExecutionContext>();
            context.Setup(x => x.GetInput<MembershipHttpRequest>()).Returns(input);
            context.Setup(x => x.CallActivityAsync<SyncJob>(It.IsAny<string>(), It.IsAny<JobReaderRequest>())).ReturnsAsync(syncJob);
            context.Setup(x => x.CallActivityAsync<string>(It.IsAny<string>(), It.IsAny<FileDownloaderRequest>()))
                    .Returns(async () => await DownloadFileAsync(fileDownloaderRequest, mockLoggingRepo, blobStorageRepository));
            context.Setup(x => x.CallActivityAsync(It.IsAny<string>(), It.IsAny<LoggerRequest>()))
                    .Callback<string, object>(async (name, request) => await CallLogMessageFunctionAsync((LoggerRequest)request, mockLoggingRepo));
            context.Setup(x => x.CallActivityAsync<bool>(It.IsAny<string>(), It.IsAny<GroupValidatorRequest>()))
                    .Returns(async () => await CheckIfGroupExistsAsync(groupMembership, mockLoggingRepo, mockGraphUpdaterService, mailSenders));

            mockGraphUpdaterService.Groups.Add(groupMembership.Destination.ObjectId, new Group { Id = groupMembership.Destination.ObjectId.ToString() });
            mockSyncJobRepo.ExistingSyncJobs.Add((syncJob.PartitionKey, syncJob.RowKey), syncJob);

            var orchestrator = new OrchestratorFunction(mockTelemetryClient, mockGraphUpdaterService, mailSenders, _gmmResources, mockLoggingRepo, mockDeltaCachingConfig);
            await Assert.ThrowsExceptionAsync<FileNotFoundException>(async () => await orchestrator.RunOrchestratorAsync(context.Object, executionContext.Object));
        }

        [TestMethod]
        public async Task RunOrchestratorMissingGroupTest()
        {
            MockLoggingRepository mockLoggingRepo;
            TelemetryClient mockTelemetryClient;
            MockMailRepository mockMailRepo;
            MockGraphUpdaterService mockGraphUpdaterService;
            DryRunValue dryRun;

            EmailSenderRecipient mailSenders;
            MockSyncJobRepository mockSyncJobRepo;
            MockGraphGroupRepository mockGroupRepo;

            ThresholdConfig thresholdConfig;
            MockLocalizationRepository localizationRepository;
            MockDeltaCachingConfig mockDeltaCachingConfig;

            mockDeltaCachingConfig = new MockDeltaCachingConfig();
            mockLoggingRepo = new MockLoggingRepository();
            mockTelemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            mockMailRepo = new MockMailRepository();
            mockGraphUpdaterService = new MockGraphUpdaterService(mockMailRepo);
            dryRun = new DryRunValue(false);
            thresholdConfig = new ThresholdConfig(5, 3, 3, 10);
            mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass",
                                            "recipient@domain.com", "recipient@domain.com", "recipient@domain.com");

            mockGroupRepo = new MockGraphGroupRepository();
            mockSyncJobRepo = new MockSyncJobRepository();
            localizationRepository = new MockLocalizationRepository();

            var groupMembership = GetGroupMembership();
            var destinationMembers = GetGroupMembership();
            var syncJob = new SyncJob
            {
                Id = groupMembership.SyncJobId,
                TargetOfficeGroupId = groupMembership.Destination.ObjectId,
                Destination = $"[{{\"value\":\"{groupMembership.Destination.ObjectId}\",\"type\":\"GraphUpdater\"}}]",
                ThresholdPercentageForAdditions = -1,
                ThresholdPercentageForRemovals = -1,
                LastRunTime = DateTime.UtcNow.AddDays(-1),
                Requestor = "user@domail.com",
                Query = "[{ \"type\": \"SecurityGroup\", \"sources\": [\"da144736-962b-4879-a304-acd9f5221e78\"]}]",
                RunId = Guid.NewGuid()
            };

            mockLoggingRepo.SetSyncJobProperties(syncJob.RunId.Value, syncJob.ToDictionary());

            var input = new MembershipHttpRequest
            {
                FilePath = "/file/path/name.json",
                SyncJob = syncJob
            };

            var context = new Mock<IDurableOrchestrationContext>();
            var executionContext = new Mock<ExecutionContext>();
            context.Setup(x => x.GetInput<MembershipHttpRequest>()).Returns(input);
            context.Setup(x => x.CallActivityAsync<SyncJob>(It.IsAny<string>(), It.IsAny<JobReaderRequest>())).ReturnsAsync(syncJob);
            context.Setup(x => x.CallActivityAsync<string>(It.IsAny<string>(), It.IsAny<FileDownloaderRequest>())).ReturnsAsync(JsonConvert.SerializeObject(groupMembership));
            context.Setup(x => x.CallActivityAsync(It.IsAny<string>(), It.IsAny<LoggerRequest>()))
                    .Callback<string, object>(async (name, request) => await CallLogMessageFunctionAsync((LoggerRequest)request, mockLoggingRepo));
            context.Setup(x => x.CallActivityAsync<bool>(It.IsAny<string>(), It.IsAny<GroupValidatorRequest>()))
                    .Returns(async () => await CheckIfGroupExistsAsync(groupMembership, mockLoggingRepo, mockGraphUpdaterService, mailSenders));

            JobStatusUpdaterRequest updateJobRequest = null;
            context.Setup(x => x.CallActivityAsync(It.IsAny<string>(), It.IsAny<JobStatusUpdaterRequest>()))
                    .Callback<string, object>((name, request) =>
                    {
                        updateJobRequest = request as JobStatusUpdaterRequest;
                    });

            var orchestrator = new OrchestratorFunction(mockTelemetryClient, mockGraphUpdaterService, mailSenders, _gmmResources, mockLoggingRepo, mockDeltaCachingConfig);
            var response = await orchestrator.RunOrchestratorAsync(context.Object, executionContext.Object);

            Assert.AreEqual(SyncStatus.DestinationGroupNotFound, updateJobRequest.Status);
            Assert.IsTrue(response == OrchestrationRuntimeStatus.Completed);
            Assert.IsTrue(mockLoggingRepo.MessagesLogged.Any(x => x.Message.Contains($"Group with ID {groupMembership.Destination.ObjectId} doesn't exist.")));
            Assert.IsTrue(mockLoggingRepo.MessagesLogged.Any(x => x.Message == nameof(OrchestratorFunction) + " function did not complete"));

            var logProperties = mockLoggingRepo.SyncJobPropertiesHistory[syncJob.RunId.Value].Properties;

            Assert.IsNotNull(mockLoggingRepo.SyncJobProperties);
            Assert.AreEqual(logProperties["RunId"], syncJob.RunId.ToString());
            Assert.AreEqual(logProperties["PartitionKey"], syncJob.PartitionKey);
            Assert.AreEqual(logProperties["RowKey"], syncJob.RowKey);
        }

        [TestMethod]
        public async Task RunCacheUserUpdaterSubOrchestratorFunctionTest()
        {
            MockLoggingRepository mockLoggingRepo;
            TelemetryClient mockTelemetryClient;
            MockMailRepository mockMailRepo;
            MockGraphUpdaterService mockGraphUpdaterService;
            DryRunValue dryRun;
            EmailSenderRecipient mailSenders;
            MockSyncJobRepository mockSyncJobRepo;
            MockGraphGroupRepository mockGroupRepo;
            ThresholdConfig thresholdConfig;
            MockLocalizationRepository localizationRepository;
            MockBlobStorageRepository blobStorageRepository;
            MockDeltaCachingConfig mockDeltaCachingConfig;

            mockDeltaCachingConfig = new MockDeltaCachingConfig();
            mockLoggingRepo = new MockLoggingRepository();
            mockTelemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            mockMailRepo = new MockMailRepository();
            mockGraphUpdaterService = new MockGraphUpdaterService(mockMailRepo);
            dryRun = new DryRunValue(false);
            thresholdConfig = new ThresholdConfig(5, 3, 3, 10);
            mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass",
                                            "recipient@domain.com", "recipient@domain.com", "recipient@domain.com");

            mockGroupRepo = new MockGraphGroupRepository();
            mockSyncJobRepo = new MockSyncJobRepository();
            localizationRepository = new MockLocalizationRepository();
            blobStorageRepository = new MockBlobStorageRepository();

            var groupIds = new List<Guid>();
            for (int i = 0; i < 2; i++)
            {
                groupIds.Add(Guid.NewGuid());
            }

            var users = new List<AzureADUser>();
            for (int i = 0; i < 10; i++)
            {
                users.Add(new AzureADUser
                {
                    ObjectId = Guid.NewGuid(),
                    SourceGroups = groupIds
                });
            }

            var groupMembership = GetGroupMembership();
            groupMembership.SourceMembers = users;
            var destinationMembers = GetGroupMembership();
            var syncJob = new SyncJob
            {
                Id = groupMembership.SyncJobId,
                TargetOfficeGroupId = groupMembership.Destination.ObjectId,
                Destination = $"[{{\"value\":\"{groupMembership.Destination.ObjectId}\",\"type\":\"GraphUpdater\"}}]",
                ThresholdPercentageForAdditions = -1,
                ThresholdPercentageForRemovals = -1,
                LastRunTime = DateTime.UtcNow.AddDays(-1),
                Requestor = "user@domail.com",
                Query = "[{ \"type\": \"SecurityGroup\", \"sources\": [\"da144736-962b-4879-a304-acd9f5221e78\"]}]",
                RunId = Guid.NewGuid()
            };

            mockLoggingRepo.SetSyncJobProperties(syncJob.RunId.Value, syncJob.ToDictionary());

            var input = new MembershipHttpRequest
            {
                FilePath = "/file/path/name.json",
                SyncJob = syncJob
            };

            var fileDownloaderRequest = new FileDownloaderRequest
            {
                FilePath = input.FilePath,
                SyncJob = syncJob
            };

            var jobReaderRequest = new JobReaderRequest
            {
                JobId = syncJob.Id,
                RunId = syncJob.RunId.Value
            };

            mockGraphUpdaterService.Jobs.Add((syncJob.PartitionKey, syncJob.RowKey), syncJob);
            mockGraphUpdaterService.Groups.Add(groupMembership.Destination.ObjectId, new Group { Id = groupMembership.Destination.ObjectId.ToString() });
            blobStorageRepository.Files.Add(input.FilePath, JsonConvert.SerializeObject(groupMembership));

            var context = new Mock<IDurableOrchestrationContext>();
            var executionContext = new Mock<ExecutionContext>();
            context.Setup(x => x.GetInput<MembershipHttpRequest>()).Returns(input);
            context.Setup(x => x.CallActivityAsync<SyncJob>(It.IsAny<string>(), It.IsAny<JobReaderRequest>()))
                    .Returns(async () => await RunJobReaderFunctionAsync(mockLoggingRepo, mockGraphUpdaterService, jobReaderRequest));
            context.Setup(x => x.CallActivityAsync<string>(It.IsAny<string>(), It.IsAny<FileDownloaderRequest>()))
                    .ReturnsAsync(await DownloadFileAsync(fileDownloaderRequest, mockLoggingRepo, blobStorageRepository));
            context.Setup(x => x.CallActivityAsync(It.IsAny<string>(), It.IsAny<LoggerRequest>()))
                    .Callback<string, object>(async (name, request) => await CallLogMessageFunctionAsync((LoggerRequest)request, mockLoggingRepo));
            context.Setup(x => x.CallActivityAsync<bool>(It.IsAny<string>(), It.IsAny<GroupValidatorRequest>()))
                    .Returns(async () => await CheckIfGroupExistsAsync(groupMembership, mockLoggingRepo, mockGraphUpdaterService, mailSenders));

            var usersAlreadyExist = new List<AzureADUser>();

            var usersToRemove = new List<AzureADUser>
            {
                new AzureADUser { ObjectId = users[0].ObjectId, MembershipAction = MembershipAction.Remove }
            };
            context.Setup(x => x.CallSubOrchestratorAsync<GroupUpdaterSubOrchestratorResponse>(It.Is<string>(x => x == nameof(GroupUpdaterSubOrchestratorFunction)),
                                                                             It.IsAny<GroupUpdaterRequest>())).ReturnsAsync(() => new GroupUpdaterSubOrchestratorResponse
                                                                             {
                                                                                 Type = RequestType.Add,
                                                                                 SuccessCount = 1,
                                                                                 UsersNotFound = usersToRemove,
                                                                                 UsersAlreadyExist = usersAlreadyExist
                                                                             });

            var orchestrator = new OrchestratorFunction(mockTelemetryClient, mockGraphUpdaterService, mailSenders, _gmmResources, mockLoggingRepo, mockDeltaCachingConfig);
            await orchestrator.RunOrchestratorAsync(context.Object, executionContext.Object);

            context.Verify(x => x.CallSubOrchestratorAsync(nameof(CacheUserUpdaterSubOrchestratorFunction), It.IsAny<CacheUserUpdaterRequest>()), Times.Exactly(3));
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
                JobId = groupMembership.SyncJobId
            };
            var groupValidatorFunction = new GroupValidatorFunction(mockLoggingRepo, mockGraphUpdaterService, mailSenders);

            return await groupValidatorFunction.ValidateGroupAsync(request);
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

        private async Task CallTelemetryTrackerFunctionAsync(TelemetryTrackerRequest request, MockLoggingRepository mockLoggingRepository)
        {
            var telemetryTrackerFunction = new TelemetryTrackerFunction(mockLoggingRepository, _telemetryClient);
            await telemetryTrackerFunction.TrackEventAsync(request);
        }

        private async Task CallLogMessageFunctionAsync(LoggerRequest loggerRequest, MockLoggingRepository mockLoggingRepository)
        {
            var function = new LoggerFunction(mockLoggingRepository);
            await function.LogMessageAsync(loggerRequest);
        }

        private async Task<string> CallGroupNameReaderFunctionAsync(
            MockLoggingRepository mockLoggingRepository,
            MockGraphUpdaterService mockGraphUpdaterService,
            GroupNameReaderRequest request)
        {
            var function = new GroupNameReaderFunction(mockLoggingRepository, mockGraphUpdaterService);
            return await function.GetGroupNameAsync(request);
        }

        private async Task<List<AzureADUser>> CallGroupOwnersReaderFunctionAsync(
            MockLoggingRepository mockLoggingRepository,
            IGraphUpdaterService graphUpdaterService,
            GroupOwnersReaderRequest request)
        {
            var function = new GroupOwnersReaderFunction(mockLoggingRepository, graphUpdaterService);
            return await function.GetGroupOwnersAsync(request);
        }

        private async Task CallJobStatusUpdaterFunctionAsync(
            MockLoggingRepository mockLoggingRepository,
            MockGraphUpdaterService mockGraphUpdaterService,
            JobStatusUpdaterRequest request)
        {
            var function = new JobStatusUpdaterFunction(mockLoggingRepository, mockGraphUpdaterService);
            await function.UpdateJobStatusAsync(request);
        }
    }
}

