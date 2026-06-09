// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using DIConcreteTypes;
using Hosts.GraphUpdater;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Graph.Models;
using Microsoft.Identity.Client;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Notifications;
using Models.ServiceBus;
using Moq;
using Repositories.Contracts;
using Repositories.Mocks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Services.Contracts;
using Services.Tests.Mocks;
using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Group = Microsoft.Graph.Models.Group;

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
        public async Task TestMsalTransientExceptionAsync()
        {
            TelemetryClient mockTelemetryClient;
            Mock<IServiceBusQueueRepository> mockServiceBusQueueRepository;
            MockGraphUpdaterService mockGraphUpdaterService;
            DryRunValue dryRun;
            EmailSenderRecipient mailSenders;
            MockDatabaseSyncJobRepository mockSyncJobRepo;
            MockGraphGroupRepository mockGroupRepo;
            ThresholdConfig thresholdConfig;
            MockLocalizationRepository localizationRepository;
            MockBlobStorageRepository blobStorageRepository;
            MockDeltaCachingConfig mockDeltaCachingConfig;

            mockDeltaCachingConfig = new MockDeltaCachingConfig();
            blobStorageRepository = new MockBlobStorageRepository();
            mockTelemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            mockServiceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            mockGraphUpdaterService = new MockGraphUpdaterService(mockServiceBusQueueRepository.Object);
            dryRun = new DryRunValue(false);
            thresholdConfig = new ThresholdConfig(5, 3, 3, 10);
            mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com");


            mockGroupRepo = new MockGraphGroupRepository();
            mockSyncJobRepo = new MockDatabaseSyncJobRepository();
            localizationRepository = new MockLocalizationRepository();

            var groupMembership = GetGroupMembership();
            var destinationMembers = GetGroupMembership();
            var syncJob = new SyncJob
            {
                Id = groupMembership.SyncJobId,
                ThresholdPercentageForAdditions = -1,
                ThresholdPercentageForRemovals = -1,
                LastRunTime = SqlDateTime.MinValue.Value,
                Requestor = "user@domail.com",
                Query = "[{ \"type\": \"GroupMembership\", \"sources\": [\"da144736-962b-4879-a304-acd9f5221e78\"]}]",
                RunId = Guid.NewGuid(),
                MembershipType = "GroupMembership",
                Group = new Models.Group
                {
                    SyncJobId = groupMembership.SyncJobId,
                    GroupId = groupMembership.Destination.ObjectId
                }
            };


            var input = new MembershipHttpRequest
            {
                FilePath = "/file/path/name.json",
                SyncJob = syncJob,
                ProjectedMemberCount = 10,
                MembersToBeAdded = 5,
                MembersToBeRemoved = 0,
                GroupId = syncJob.Group.GroupId
            };

            var fileDownloaderRequest = new FileDownloaderRequest
            {
                FilePath = input.FilePath,
                SyncJob = syncJob
            };

            blobStorageRepository.Files.Add(input.FilePath, JsonSerializer.Serialize(groupMembership));

            var context = new Mock<TaskOrchestrationContext>();
            context.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(NullLogger.Instance);
            context.Setup(x => x.GetInput<MembershipHttpRequest>()).Returns(input);
            context.Setup(x => x.CallActivityAsync<Guid>(It.Is<TaskName>(x => x.Name == nameof(GetGroupFunction)), It.IsAny<GetGroupRequest>(), It.IsAny<TaskOptions>())).ReturnsAsync(syncJob.Group.GroupId);
            context.Setup(x => x.CallActivityAsync<SyncJob>(It.IsAny<TaskName>(), It.IsAny<JobReaderRequest>(), It.IsAny<TaskOptions>())).ReturnsAsync(syncJob);
            context.Setup(x => x.CallActivityAsync<string>(It.IsAny<TaskName>(), It.IsAny<FileDownloaderRequest>(), It.IsAny<TaskOptions>()))
                    .ReturnsAsync(await DownloadFileAsync(fileDownloaderRequest, blobStorageRepository));
            context.Setup(x => x.CallActivityAsync<bool>(It.IsAny<TaskName>(), It.IsAny<GroupValidatorRequest>(), It.IsAny<TaskOptions>()))
                    .ThrowsAsync(new MsalClientException("MULTIPLE_MATCHING_TOKENS_DETECTED", "MULTIPLE_MATCHING_TOKENS_DETECTED"));

            JobStatusUpdaterRequest updateJobRequest = null;
            context.Setup(x => x.CallActivityAsync(It.IsAny<TaskName>(), It.IsAny<JobStatusUpdaterRequest>(), It.IsAny<TaskOptions>()))
                    .Callback<TaskName, object, TaskOptions>((name, request, options) =>
                    {
                        updateJobRequest = request as JobStatusUpdaterRequest;
                    });

            var orchestrator = new OrchestratorFunction(mockTelemetryClient, mockGraphUpdaterService, mailSenders, _gmmResources, mockDeltaCachingConfig);
            await Assert.ThrowsExceptionAsync<MsalClientException>(async () => await orchestrator.RunOrchestratorAsync(context.Object));

            Assert.AreEqual(SyncStatus.TransientError, updateJobRequest.Status);
        }

        [TestMethod]
        public async Task RunOrchestratorValidSyncTest()
        {
            TelemetryClient mockTelemetryClient;
            MockDeltaCachingConfig mockDeltaCachingConfig;
            Mock<IServiceBusQueueRepository> mockServiceBusQueueRepository;
            MockGraphUpdaterService mockGraphUpdaterService;
            DryRunValue dryRun;
            EmailSenderRecipient mailSenders;
            MockDatabaseSyncJobRepository mockSyncJobRepo;
            MockGraphGroupRepository mockGroupRepo;
            ThresholdConfig thresholdConfig;
            MockLocalizationRepository localizationRepository;
            MockBlobStorageRepository blobStorageRepository;

            mockDeltaCachingConfig = new MockDeltaCachingConfig();
            mockTelemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            mockServiceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            mockGraphUpdaterService = new MockGraphUpdaterService(mockServiceBusQueueRepository.Object);
            dryRun = new DryRunValue(false);
            thresholdConfig = new ThresholdConfig(5, 3, 3, 10);
            mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com");

            mockGroupRepo = new MockGraphGroupRepository();
            mockSyncJobRepo = new MockDatabaseSyncJobRepository();
            localizationRepository = new MockLocalizationRepository();
            blobStorageRepository = new MockBlobStorageRepository();

            var groupMembership = GetGroupMembership();
            var destinationMembers = GetGroupMembership();
            var syncJob = new SyncJob
            {
                Id = groupMembership.SyncJobId,
                ThresholdPercentageForAdditions = -1,
                ThresholdPercentageForRemovals = -1,
                LastRunTime = DateTime.UtcNow.AddDays(-1),
                Requestor = "user@domail.com",
                Query = "[{ \"type\": \"GroupMembership\", \"sources\": [\"da144736-962b-4879-a304-acd9f5221e78\"]}]",
                RunId = groupMembership.RunId,
                MembershipType = "GroupMembership",
                Group = new Models.Group
                {
                    SyncJobId = groupMembership.SyncJobId,
                    GroupId = groupMembership.Destination.ObjectId
                }
            };


            var input = new MembershipHttpRequest
            {
                FilePath = "/file/path/name.json",
                SyncJob = syncJob,
                ProjectedMemberCount = 10,
                MembersToBeAdded = 5,
                MembersToBeRemoved = 0,
                GroupId = syncJob.Group.GroupId
            };

            var fileDownloaderRequest = new FileDownloaderRequest
            {
                FilePath = input.FilePath,
                SyncJob = syncJob
            };

            var jobReaderRequest = new JobReaderRequest
            {
                SyncJob = syncJob
            };

            _telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());

            mockGraphUpdaterService.Jobs.Add(syncJob);
            mockGraphUpdaterService.Groups.Add(groupMembership.Destination.ObjectId, new Group { Id = groupMembership.Destination.ObjectId.ToString() });
            blobStorageRepository.Files.Add(input.FilePath, JsonSerializer.Serialize(groupMembership));

            var context = new Mock<TaskOrchestrationContext>();
            context.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(NullLogger.Instance);
            context.Setup(x => x.GetInput<MembershipHttpRequest>()).Returns(input);
            context.Setup(x => x.CallActivityAsync(It.Is<TaskName>(x => x.Name == nameof(TelemetryTrackerFunction)), It.IsAny<TelemetryTrackerRequest>(), It.IsAny<TaskOptions>()))
                    .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                    {
                        var telemetryRequest = request as TelemetryTrackerRequest;
                        await CallTelemetryTrackerFunctionAsync(telemetryRequest);
                    });
            context.Setup(x => x.CallActivityAsync<SyncJob>(It.IsAny<TaskName>(), It.IsAny<JobReaderRequest>(), It.IsAny<TaskOptions>()))
                    .Returns(async () => await RunJobReaderFunctionAsync(mockGraphUpdaterService, jobReaderRequest));
            context.Setup(x => x.CallActivityAsync<Guid>(It.Is<TaskName>(x => x.Name == nameof(GetGroupFunction)), It.IsAny<GetGroupRequest>(), It.IsAny<TaskOptions>())).ReturnsAsync(syncJob.Group.GroupId);
            context.Setup(x => x.CallActivityAsync<string>(It.IsAny<TaskName>(), It.IsAny<FileDownloaderRequest>(), It.IsAny<TaskOptions>()))
                    .ReturnsAsync(await DownloadFileAsync(fileDownloaderRequest, blobStorageRepository));
            context.Setup(x => x.CallActivityAsync<bool>(It.IsAny<TaskName>(), It.IsAny<GroupValidatorRequest>(), It.IsAny<TaskOptions>()))
                    .Returns(async () => await CheckIfGroupExistsAsync(groupMembership, mockGraphUpdaterService, mailSenders));
            context.Setup(x => x.CallSubOrchestratorAsync<GroupUpdaterSubOrchestratorResponse>(It.IsAny<TaskName>(), It.IsAny<GroupUpdaterRequest>(), It.IsAny<TaskOptions>()))
                .Returns(() => Task.FromResult(new GroupUpdaterSubOrchestratorResponse() { SuccessCount = 1, UsersNotFound = new List<AzureADUser>(), UsersAlreadyExist = new List<AzureADUser>() }));
            context.Setup(x => x.CallActivityAsync(It.Is<TaskName>(x => x.Name == nameof(JobStatusUpdaterFunction)), It.IsAny<JobStatusUpdaterRequest>(), It.IsAny<TaskOptions>()))
                   .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                   {
                       await CallJobStatusUpdaterFunctionAsync(mockGraphUpdaterService, request as JobStatusUpdaterRequest);
                   });

            var orchestrator = new OrchestratorFunction(mockTelemetryClient, mockGraphUpdaterService, mailSenders, _gmmResources, mockDeltaCachingConfig);
            var response = await orchestrator.RunOrchestratorAsync(context.Object);

            Assert.IsTrue(response == OrchestrationRuntimeStatus.Completed);

            context.Verify(x => x.CallSubOrchestratorAsync<GroupUpdaterSubOrchestratorResponse>(It.IsAny<TaskName>(), It.IsAny<GroupUpdaterRequest>(), It.IsAny<TaskOptions>()), Times.Exactly(2));
            Assert.AreEqual(SyncStatus.Idle.ToString(), mockGraphUpdaterService.Jobs[0].Status);
        }

        [TestMethod]
        public async Task RunOrchestratorInitialSyncTest()
        {
            MockDeltaCachingConfig mockDeltaCachingConfig;
            TelemetryClient mockTelemetryClient;
            Mock<IServiceBusQueueRepository> mockServiceBusQueueRepository;
            MockGraphUpdaterService mockGraphUpdaterService;
            DryRunValue dryRun;
            EmailSenderRecipient mailSenders;
            MockDatabaseSyncJobRepository mockSyncJobRepo;
            MockGraphGroupRepository mockGroupRepo;
            ThresholdConfig thresholdConfig;
            MockLocalizationRepository localizationRepository;

            mockDeltaCachingConfig = new MockDeltaCachingConfig();
            mockTelemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            mockServiceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            mockGraphUpdaterService = new MockGraphUpdaterService(mockServiceBusQueueRepository.Object);
            dryRun = new DryRunValue(false);
            thresholdConfig = new ThresholdConfig(5, 3, 3, 10);
            mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com");


            mockGroupRepo = new MockGraphGroupRepository();
            mockSyncJobRepo = new MockDatabaseSyncJobRepository();
            localizationRepository = new MockLocalizationRepository();

            var groupMembership = GetGroupMembership();
            var destinationMembers = GetGroupMembership();
            var syncJob = new SyncJob
            {
                Id = groupMembership.SyncJobId,
                MembershipType = "GroupMembership",
                Group = new Models.Group
                {
                    SyncJobId = groupMembership.SyncJobId,
                    GroupId = groupMembership.Destination.ObjectId
                },
                ThresholdPercentageForAdditions = -1,
                ThresholdPercentageForRemovals = -1,
                LastRunTime = SqlDateTime.MinValue.Value,
                Requestor = "user@domail.com",
                Query = "[{ \"type\": \"GroupMembership\", \"sources\": [\"da144736-962b-4879-a304-acd9f5221e78\"]}]",
                RunId = Guid.NewGuid()
            };


            var input = new MembershipHttpRequest
            {
                FilePath = "/file/path/name.json",
                SyncJob = syncJob,
                ProjectedMemberCount = 10,
                MembersToBeAdded = 5,
                MembersToBeRemoved = 0,
                GroupId = syncJob.Group.GroupId
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

            var groupNameReaderRequest = new GroupNameReaderRequest { GroupId = syncJob.Group.GroupId };
            var groupOwnersReaderRequest = new GroupOwnersReaderRequest { GroupId = syncJob.Group.GroupId };

            mockGraphUpdaterService.Groups.Add(groupMembership.Destination.ObjectId,
                                                new Group
                                                {
                                                    Id = groupMembership.Destination.ObjectId.ToString(),
                                                    DisplayName = "Test Group"
                                                });

            await mockSyncJobRepo.AddSyncJobAsync(syncJob);

            Mock<IGraphUpdaterService> graphUpdaterService = new Mock<IGraphUpdaterService>();
            graphUpdaterService.Setup(x => x.GetGroupOwnersAsync(It.IsAny<Guid>(), It.IsAny<int>())).ReturnsAsync(owners);

            var context = new Mock<TaskOrchestrationContext>();
            context.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(NullLogger.Instance);
                        context.Setup(x => x.GetInput<MembershipHttpRequest>()).Returns(input);
            context.Setup(x => x.CallActivityAsync<SyncJob>(It.IsAny<TaskName>(), It.IsAny<JobReaderRequest>(), It.IsAny<TaskOptions>())).ReturnsAsync(syncJob);
            context.Setup(x => x.CallActivityAsync<Guid>(It.Is<TaskName>(x => x.Name == nameof(GetGroupFunction)), It.IsAny<GetGroupRequest>(), It.IsAny<TaskOptions>())).ReturnsAsync(syncJob.Group.GroupId);
            context.Setup(x => x.CallActivityAsync<string>(It.IsAny<TaskName>(), It.IsAny<FileDownloaderRequest>(), It.IsAny<TaskOptions>())).ReturnsAsync(JsonSerializer.Serialize(groupMembership));
            context.Setup(x => x.CallActivityAsync<bool>(It.IsAny<TaskName>(), It.IsAny<GroupValidatorRequest>(), It.IsAny<TaskOptions>()))
                    .Returns(async () => await CheckIfGroupExistsAsync(groupMembership, mockGraphUpdaterService, mailSenders));
            context.Setup(x => x.CallActivityAsync<string>(It.IsAny<TaskName>(), It.IsAny<GroupNameReaderRequest>(), It.IsAny<TaskOptions>()))
                    .Returns(async () => await CallGroupNameReaderFunctionAsync(mockGraphUpdaterService, groupNameReaderRequest));
            context.Setup(x => x.CallActivityAsync<List<AzureADUser>>(It.IsAny<TaskName>(), It.IsAny<GroupOwnersReaderRequest>(), It.IsAny<TaskOptions>()))
                    .Returns(async () => await CallGroupOwnersReaderFunctionAsync(graphUpdaterService.Object, groupOwnersReaderRequest));
            context.Setup(x => x.CallSubOrchestratorAsync<GroupUpdaterSubOrchestratorResponse>(It.IsAny<TaskName>(), It.IsAny<GroupUpdaterRequest>(), It.IsAny<TaskOptions>()))
                    .Returns(() => Task.FromResult(new GroupUpdaterSubOrchestratorResponse() { SuccessCount = 1, UsersNotFound = new List<AzureADUser>(), UsersAlreadyExist = new List<AzureADUser>() }));

            context.Setup(x => x.CallActivityAsync(It.IsAny<TaskName>(), It.IsAny<EmailSenderRequest>(), It.IsAny<TaskOptions>()))
                    .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                    {
                        var emailSenderFunction = new EmailSenderFunction(NullLogger<EmailSenderFunction>.Instance, mockGraphUpdaterService);
                        await emailSenderFunction.SendEmailAsync((EmailSenderRequest)request);
                    });

            var orchestrator = new OrchestratorFunction(mockTelemetryClient, mockGraphUpdaterService, mailSenders, _gmmResources, mockDeltaCachingConfig);
            var response = await orchestrator.RunOrchestratorAsync(context.Object);

            Assert.IsTrue(response == OrchestrationRuntimeStatus.Completed);

            mockServiceBusQueueRepository.Verify(x => x.SendMessageAsync(It.Is<ServiceBusMessage>(msg =>
                   msg.ApplicationProperties.ContainsKey("MessageType") &&
                   msg.ApplicationProperties["MessageType"].ToString() == NotificationMessageType.SyncCompletedNotification.ToString())),
                   Times.Exactly(1));

            context.Verify(x => x.CallSubOrchestratorAsync<GroupUpdaterSubOrchestratorResponse>(It.IsAny<TaskName>(), It.IsAny<GroupUpdaterRequest>(), It.IsAny<TaskOptions>()), Times.Exactly(2));
        }

        [TestMethod]
        public async Task TestHttpTransientExceptionAsync()
        {
            TelemetryClient mockTelemetryClient;
            Mock<IServiceBusQueueRepository> mockServiceBusQueueRepository;
            MockGraphUpdaterService mockGraphUpdaterService;
            DryRunValue dryRun;
            EmailSenderRecipient mailSenders;
            MockDatabaseSyncJobRepository mockSyncJobRepo;
            MockGraphGroupRepository mockGroupRepo;
            ThresholdConfig thresholdConfig;
            MockLocalizationRepository localizationRepository;
            MockBlobStorageRepository blobStorageRepository;
            MockDeltaCachingConfig mockDeltaCachingConfig;

            mockDeltaCachingConfig = new MockDeltaCachingConfig();
            blobStorageRepository = new MockBlobStorageRepository();
            mockTelemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            mockServiceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            mockGraphUpdaterService = new MockGraphUpdaterService(mockServiceBusQueueRepository.Object);
            dryRun = new DryRunValue(false);
            thresholdConfig = new ThresholdConfig(5, 3, 3, 10);
            mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com");


            mockGroupRepo = new MockGraphGroupRepository();
            mockSyncJobRepo = new MockDatabaseSyncJobRepository();
            localizationRepository = new MockLocalizationRepository();

            var groupMembership = GetGroupMembership();
            var destinationMembers = GetGroupMembership();
            var syncJob = new SyncJob
            {
                Id = groupMembership.SyncJobId,
                MembershipType = "GroupMembership",
                Group = new Models.Group
                {
                    SyncJobId = groupMembership.SyncJobId,
                    GroupId = groupMembership.Destination.ObjectId
                },
                ThresholdPercentageForAdditions = -1,
                ThresholdPercentageForRemovals = -1,
                LastRunTime = SqlDateTime.MinValue.Value,
                Requestor = "user@domail.com",
                Query = "[{ \"type\": \"GroupMembership\", \"sources\": [\"da144736-962b-4879-a304-acd9f5221e78\"]}]",
                RunId = Guid.NewGuid()
            };


            var input = new MembershipHttpRequest
            {
                FilePath = "/file/path/name.json",
                SyncJob = syncJob,
                ProjectedMemberCount = 10,
                MembersToBeAdded = 5,
                MembersToBeRemoved = 0,
                GroupId = syncJob.Group.GroupId
            };

            var fileDownloaderRequest = new FileDownloaderRequest
            {
                FilePath = input.FilePath,
                SyncJob = syncJob
            };

            blobStorageRepository.Files.Add(input.FilePath, JsonSerializer.Serialize(groupMembership));

            var context = new Mock<TaskOrchestrationContext>();
            context.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(NullLogger.Instance);
                        context.Setup(x => x.GetInput<MembershipHttpRequest>()).Returns(input);
            context.Setup(x => x.CallActivityAsync<SyncJob>(It.IsAny<TaskName>(), It.IsAny<JobReaderRequest>(), It.IsAny<TaskOptions>())).ReturnsAsync(syncJob);
            context.Setup(x => x.CallActivityAsync<Guid>(It.Is<TaskName>(x => x.Name == nameof(GetGroupFunction)), It.IsAny<GetGroupRequest>(), It.IsAny<TaskOptions>())).ReturnsAsync(syncJob.Group.GroupId);
            context.Setup(x => x.CallActivityAsync<string>(It.IsAny<TaskName>(), It.IsAny<FileDownloaderRequest>(), It.IsAny<TaskOptions>()))
                    .ReturnsAsync(await DownloadFileAsync(fileDownloaderRequest, blobStorageRepository));
            context.Setup(x => x.CallActivityAsync<bool>(It.IsAny<TaskName>(), It.IsAny<GroupValidatorRequest>(), It.IsAny<TaskOptions>()))
                    .ReturnsAsync(true);

            JobStatusUpdaterRequest updateJobRequest = null;
            context.Setup(x => x.CallActivityAsync(It.IsAny<TaskName>(), It.IsAny<JobStatusUpdaterRequest>(), It.IsAny<TaskOptions>()))
                    .Callback<TaskName, object, TaskOptions>((name, request, options) =>
                    {
                        updateJobRequest = request as JobStatusUpdaterRequest;
                    });

            context.Setup(x => x.CallSubOrchestratorAsync<GroupUpdaterSubOrchestratorResponse>(It.IsAny<TaskName>(), It.IsAny<GroupUpdaterRequest>(), It.IsAny<TaskOptions>()))
               .Throws<HttpRequestException>();

            var orchestrator = new OrchestratorFunction(mockTelemetryClient, mockGraphUpdaterService, mailSenders, _gmmResources, mockDeltaCachingConfig);
            await Assert.ThrowsExceptionAsync<HttpRequestException>(async () => await orchestrator.RunOrchestratorAsync(context.Object));

            Assert.AreEqual(SyncStatus.TransientError, updateJobRequest.Status);
        }

        [TestMethod]
        public async Task RunOrchestratorExceptionTest()
        {
            TelemetryClient mockTelemetryClient;
            Mock<IServiceBusQueueRepository> mockServiceBusQueueRepository;
            MockGraphUpdaterService mockGraphUpdaterService;
            DryRunValue dryRun;
            EmailSenderRecipient mailSenders;
            MockDatabaseSyncJobRepository mockSyncJobRepo;
            MockGraphGroupRepository mockGroupRepo;
            ThresholdConfig thresholdConfig;
            MockLocalizationRepository localizationRepository;
            MockBlobStorageRepository blobStorageRepository;
            MockDeltaCachingConfig mockDeltaCachingConfig;

            mockDeltaCachingConfig = new MockDeltaCachingConfig();
            blobStorageRepository = new MockBlobStorageRepository();
            mockTelemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            mockServiceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            mockGraphUpdaterService = new MockGraphUpdaterService(mockServiceBusQueueRepository.Object);
            dryRun = new DryRunValue(false);
            thresholdConfig = new ThresholdConfig(5, 3, 3, 10);
            mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com");


            mockGroupRepo = new MockGraphGroupRepository();
            mockSyncJobRepo = new MockDatabaseSyncJobRepository();
            localizationRepository = new MockLocalizationRepository();

            var groupMembership = GetGroupMembership();
            var destinationMembers = GetGroupMembership();
            var syncJob = new SyncJob
            {
                Id = groupMembership.SyncJobId,
                MembershipType = "GroupMembership",
                Group = new Models.Group
                {
                    SyncJobId = groupMembership.SyncJobId,
                    GroupId = groupMembership.Destination.ObjectId
                },
                ThresholdPercentageForAdditions = -1,
                ThresholdPercentageForRemovals = -1,
                LastRunTime = SqlDateTime.MinValue.Value,
                Requestor = "user@domail.com",
                Query = "[{ \"type\": \"GroupMembership\", \"sources\": [\"da144736-962b-4879-a304-acd9f5221e78\"]}]",
                RunId = Guid.NewGuid()
            };


            var input = new MembershipHttpRequest
            {
                FilePath = "/file/path/name.json",
                SyncJob = syncJob,
                ProjectedMemberCount = 10,
                MembersToBeAdded = 5,
                MembersToBeRemoved = 0,
                GroupId = syncJob.Group.GroupId
            };

            var fileDownloaderRequest = new FileDownloaderRequest
            {
                FilePath = input.FilePath,
                SyncJob = syncJob
            };

            blobStorageRepository.Files.Add(input.FilePath, JsonSerializer.Serialize(groupMembership));

            var context = new Mock<TaskOrchestrationContext>();
            context.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(NullLogger.Instance);
                        context.Setup(x => x.GetInput<MembershipHttpRequest>()).Returns(input);
            context.Setup(x => x.CallActivityAsync<SyncJob>(It.IsAny<TaskName>(), It.IsAny<JobReaderRequest>(), It.IsAny<TaskOptions>())).ReturnsAsync(syncJob);
            context.Setup(x => x.CallActivityAsync<Guid>(It.Is<TaskName>(x => x.Name == nameof(GetGroupFunction)), It.IsAny<GetGroupRequest>(), It.IsAny<TaskOptions>())).ReturnsAsync(syncJob.Group.GroupId);

            JobStatusUpdaterRequest updateJobRequest = null;
            context.Setup(x => x.CallActivityAsync(It.IsAny<TaskName>(), It.IsAny<JobStatusUpdaterRequest>(), It.IsAny<TaskOptions>()))
                    .Callback<TaskName, object, TaskOptions>((name, request, options) =>
                    {
                        updateJobRequest = request as JobStatusUpdaterRequest;
                    });

            var orchestrator = new OrchestratorFunction(mockTelemetryClient, mockGraphUpdaterService, mailSenders, _gmmResources, mockDeltaCachingConfig);
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(async () => await orchestrator.RunOrchestratorAsync(context.Object));

        }

        [TestMethod]
        public async Task RunSyncJobNotFoundTest()
        {
            TelemetryClient mockTelemetryClient;
            Mock<IServiceBusQueueRepository> mockServiceBusQueueRepository;
            MockGraphUpdaterService mockGraphUpdaterService;
            DryRunValue dryRun;
            EmailSenderRecipient mailSenders;
            MockDatabaseSyncJobRepository mockSyncJobRepo;
            MockGraphGroupRepository mockGroupRepo;
            ThresholdConfig thresholdConfig;
            MockLocalizationRepository localizationRepository;
            MockBlobStorageRepository blobStorageRepository;
            MockDeltaCachingConfig mockDeltaCachingConfig;

            mockDeltaCachingConfig = new MockDeltaCachingConfig();
            blobStorageRepository = new MockBlobStorageRepository();
            mockTelemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            mockServiceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            mockGraphUpdaterService = new MockGraphUpdaterService(mockServiceBusQueueRepository.Object);
            dryRun = new DryRunValue(false);
            thresholdConfig = new ThresholdConfig(5, 3, 3, 10);
            mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com");


            mockGroupRepo = new MockGraphGroupRepository();
            mockSyncJobRepo = new MockDatabaseSyncJobRepository();
            localizationRepository = new MockLocalizationRepository();

            var groupMembership = GetGroupMembership();
            var destinationMembers = GetGroupMembership();
            SyncJob syncJob = null;

            var input = new MembershipHttpRequest
            {
                FilePath = "/file/path/name.json",
                SyncJob = syncJob,
                ProjectedMemberCount = 10,
                MembersToBeAdded = 5,
                MembersToBeRemoved = 0,
                GroupId = groupMembership.Destination.ObjectId
            };

            blobStorageRepository.Files.Add(input.FilePath, JsonSerializer.Serialize(groupMembership));

            var context = new Mock<TaskOrchestrationContext>();
            context.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(NullLogger.Instance);
                        context.Setup(x => x.GetInput<MembershipHttpRequest>()).Returns(input);
            context.Setup(x => x.CallActivityAsync<SyncJob>(It.IsAny<TaskName>(), It.IsAny<JobReaderRequest>(), It.IsAny<TaskOptions>())).ReturnsAsync(syncJob);

            var orchestrator = new OrchestratorFunction(mockTelemetryClient, mockGraphUpdaterService, mailSenders, _gmmResources, mockDeltaCachingConfig);
            await orchestrator.RunOrchestratorAsync(context.Object);

        }

        [TestMethod]
        public async Task RunOrchestratorFileNotFoundExceptionTest()
        {
            TelemetryClient mockTelemetryClient;
            Mock<IServiceBusQueueRepository> mockServiceBusQueueRepository;
            MockGraphUpdaterService mockGraphUpdaterService;
            DryRunValue dryRun;

            EmailSenderRecipient mailSenders;
            MockDatabaseSyncJobRepository mockSyncJobRepo;
            MockGraphGroupRepository mockGroupRepo;

            ThresholdConfig thresholdConfig;
            MockLocalizationRepository localizationRepository;
            MockBlobStorageRepository blobStorageRepository;
            MockDeltaCachingConfig mockDeltaCachingConfig;

            mockDeltaCachingConfig = new MockDeltaCachingConfig();
            mockTelemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            mockServiceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            mockGraphUpdaterService = new MockGraphUpdaterService(mockServiceBusQueueRepository.Object);
            dryRun = new DryRunValue(false);
            thresholdConfig = new ThresholdConfig(5, 3, 3, 10);
            mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com");


            mockGroupRepo = new MockGraphGroupRepository();
            mockSyncJobRepo = new MockDatabaseSyncJobRepository();
            localizationRepository = new MockLocalizationRepository();
            blobStorageRepository = new MockBlobStorageRepository();

            var groupMembership = GetGroupMembership();
            var destinationMembers = GetGroupMembership();
            var syncJob = new SyncJob
            {
                Id = groupMembership.SyncJobId,
                MembershipType = "GroupMembership",
                Group = new Models.Group
                {
                    SyncJobId = groupMembership.SyncJobId,
                    GroupId = groupMembership.Destination.ObjectId
                },
                ThresholdPercentageForAdditions = -1,
                ThresholdPercentageForRemovals = -1,
                LastRunTime = DateTime.UtcNow.AddDays(-1),
                Requestor = "user@domail.com",
                Query = "[{ \"type\": \"GroupMembership\", \"sources\": [\"da144736-962b-4879-a304-acd9f5221e78\"]}]",
                RunId = Guid.NewGuid()
            };

            var input = new MembershipHttpRequest
            {
                FilePath = "/file/path/name.json",
                SyncJob = syncJob,
                ProjectedMemberCount = 10,
                MembersToBeAdded = 5,
                MembersToBeRemoved = 0,
                GroupId = syncJob.Group.GroupId
            };

            var fileDownloaderRequest = new FileDownloaderRequest
            {
                FilePath = "some/invalid/path/file.json",
                SyncJob = syncJob
            };

            blobStorageRepository.Files.Add(input.FilePath, JsonSerializer.Serialize(groupMembership));

            var context = new Mock<TaskOrchestrationContext>();
            context.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(NullLogger.Instance);
                        context.Setup(x => x.GetInput<MembershipHttpRequest>()).Returns(input);
            context.Setup(x => x.CallActivityAsync<SyncJob>(It.IsAny<TaskName>(), It.IsAny<JobReaderRequest>(), It.IsAny<TaskOptions>())).ReturnsAsync(syncJob);
            context.Setup(x => x.CallActivityAsync<Guid>(It.Is<TaskName>(x => x.Name == nameof(GetGroupFunction)), It.IsAny<GetGroupRequest>(), It.IsAny<TaskOptions>())).ReturnsAsync(syncJob.Group.GroupId);
            context.Setup(x => x.CallActivityAsync<string>(It.IsAny<TaskName>(), It.IsAny<FileDownloaderRequest>(), It.IsAny<TaskOptions>()))
                    .Returns(async () => await DownloadFileAsync(fileDownloaderRequest, blobStorageRepository));
            context.Setup(x => x.CallActivityAsync<bool>(It.IsAny<TaskName>(), It.IsAny<GroupValidatorRequest>(), It.IsAny<TaskOptions>()))
                    .Returns(async () => await CheckIfGroupExistsAsync(groupMembership, mockGraphUpdaterService, mailSenders));

            mockGraphUpdaterService.Groups.Add(groupMembership.Destination.ObjectId, new Group { Id = groupMembership.Destination.ObjectId.ToString() });
            mockSyncJobRepo.Jobs.Add(syncJob);

            var orchestrator = new OrchestratorFunction(mockTelemetryClient, mockGraphUpdaterService, mailSenders, _gmmResources, mockDeltaCachingConfig);
            await Assert.ThrowsExceptionAsync<FileNotFoundException>(async () => await orchestrator.RunOrchestratorAsync(context.Object));
        }

        [TestMethod]
        public async Task RunOrchestratorMissingGroupTest()
        {
            TelemetryClient mockTelemetryClient;
            Mock<IServiceBusQueueRepository> mockServiceBusQueueRepository;
            MockGraphUpdaterService mockGraphUpdaterService;
            DryRunValue dryRun;

            EmailSenderRecipient mailSenders;
            MockDatabaseSyncJobRepository mockSyncJobRepo;
            MockGraphGroupRepository mockGroupRepo;

            ThresholdConfig thresholdConfig;
            MockLocalizationRepository localizationRepository;
            MockDeltaCachingConfig mockDeltaCachingConfig;

            mockDeltaCachingConfig = new MockDeltaCachingConfig();
            mockTelemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            mockServiceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            mockGraphUpdaterService = new MockGraphUpdaterService(mockServiceBusQueueRepository.Object);
            dryRun = new DryRunValue(false);
            thresholdConfig = new ThresholdConfig(5, 3, 3, 10);
            mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com");

            mockGroupRepo = new MockGraphGroupRepository();
            mockSyncJobRepo = new MockDatabaseSyncJobRepository();
            localizationRepository = new MockLocalizationRepository();

            var groupMembership = GetGroupMembership();
            var destinationMembers = GetGroupMembership();
            var syncJob = new SyncJob
            {
                Id = groupMembership.SyncJobId,
                MembershipType = "GroupMembership",
                Group = new Models.Group
                {
                    SyncJobId = groupMembership.SyncJobId,
                    GroupId = groupMembership.Destination.ObjectId
                },
                ThresholdPercentageForAdditions = -1,
                ThresholdPercentageForRemovals = -1,
                LastRunTime = DateTime.UtcNow.AddDays(-1),
                Requestor = "user@domail.com",
                Query = "[{ \"type\": \"GroupMembership\", \"sources\": [\"da144736-962b-4879-a304-acd9f5221e78\"]}]",
                RunId = Guid.NewGuid()
            };

            groupMembership.SyncJob = syncJob;

            var input = new MembershipHttpRequest
            {
                FilePath = "/file/path/name.json",
                SyncJob = syncJob,
                ProjectedMemberCount = 10,
                MembersToBeAdded = 5,
                MembersToBeRemoved = 0,
                GroupId = syncJob.Group.GroupId
            };

            var context = new Mock<TaskOrchestrationContext>();
            context.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(NullLogger.Instance);
                        context.Setup(x => x.GetInput<MembershipHttpRequest>()).Returns(input);
            context.Setup(x => x.CallActivityAsync<SyncJob>(It.IsAny<TaskName>(), It.IsAny<JobReaderRequest>(), It.IsAny<TaskOptions>())).ReturnsAsync(syncJob);
            context.Setup(x => x.CallActivityAsync<Guid>(It.Is<TaskName>(x => x.Name == nameof(GetGroupFunction)), It.IsAny<GetGroupRequest>(), It.IsAny<TaskOptions>())).ReturnsAsync(syncJob.Group.GroupId);
            context.Setup(x => x.CallActivityAsync<string>(It.IsAny<TaskName>(), It.IsAny<FileDownloaderRequest>(), It.IsAny<TaskOptions>())).ReturnsAsync(JsonSerializer.Serialize(groupMembership));
            context.Setup(x => x.CallActivityAsync<bool>(It.IsAny<TaskName>(), It.IsAny<GroupValidatorRequest>(), It.IsAny<TaskOptions>()))
                    .Returns(async () => await CheckIfGroupExistsAsync(groupMembership, mockGraphUpdaterService, mailSenders));

            JobStatusUpdaterRequest updateJobRequest = null;
            context.Setup(x => x.CallActivityAsync(It.IsAny<TaskName>(), It.IsAny<JobStatusUpdaterRequest>(), It.IsAny<TaskOptions>()))
                    .Callback<TaskName, object, TaskOptions>((name, request, options) =>
                    {
                        updateJobRequest = request as JobStatusUpdaterRequest;
                    });

            var orchestrator = new OrchestratorFunction(mockTelemetryClient, mockGraphUpdaterService, mailSenders, _gmmResources, mockDeltaCachingConfig);
            var response = await orchestrator.RunOrchestratorAsync(context.Object);

            Assert.AreEqual(SyncStatus.DestinationGroupNotFound, updateJobRequest.Status);
            Assert.IsTrue(response == OrchestrationRuntimeStatus.Completed);
        }

        [TestMethod]
        public async Task RunOrchestratorGuestUserErrorTest()
        {
            MockDeltaCachingConfig mockDeltaCachingConfig;
            TelemetryClient mockTelemetryClient;
            Mock<IServiceBusQueueRepository> mockServiceBusQueueRepository;
            MockGraphUpdaterService mockGraphUpdaterService;
            DryRunValue dryRun;
            EmailSenderRecipient mailSenders;
            MockDatabaseSyncJobRepository mockSyncJobRepo;
            MockGraphGroupRepository mockGroupRepo;
            ThresholdConfig thresholdConfig;
            MockLocalizationRepository localizationRepository;

            mockDeltaCachingConfig = new MockDeltaCachingConfig();
            mockTelemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            mockServiceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            mockGraphUpdaterService = new MockGraphUpdaterService(mockServiceBusQueueRepository.Object);
            dryRun = new DryRunValue(false);
            thresholdConfig = new ThresholdConfig(5, 3, 3, 10);
            mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com");


            mockGroupRepo = new MockGraphGroupRepository();
            mockSyncJobRepo = new MockDatabaseSyncJobRepository();
            localizationRepository = new MockLocalizationRepository();

            var groupMembership = GetGroupMembership();
            var destinationMembers = GetGroupMembership();
            var syncJob = new SyncJob
            {
                Id = groupMembership.SyncJobId,
                MembershipType = "GroupMembership",
                Group = new Models.Group
                {
                    SyncJobId = groupMembership.SyncJobId,
                    GroupId = groupMembership.Destination.ObjectId
                },
                ThresholdPercentageForAdditions = -1,
                ThresholdPercentageForRemovals = -1,
                LastRunTime = SqlDateTime.MinValue.Value,
                Requestor = "user@domail.com",
                Query = "[{ \"type\": \"GroupMembership\", \"sources\": [\"da144736-962b-4879-a304-acd9f5221e78\"]}]",
                RunId = Guid.NewGuid()
            };


            var input = new MembershipHttpRequest
            {
                FilePath = "/file/path/name.json",
                SyncJob = syncJob,
                ProjectedMemberCount = 10,
                MembersToBeAdded = 5,
                MembersToBeRemoved = 0,
                GroupId = syncJob.Group.GroupId
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

            var groupNameReaderRequest = new GroupNameReaderRequest { GroupId = syncJob.Group.GroupId };
            var groupOwnersReaderRequest = new GroupOwnersReaderRequest { GroupId = syncJob.Group.GroupId };

            mockGraphUpdaterService.Groups.Add(groupMembership.Destination.ObjectId,
                                                new Group
                                                {
                                                    Id = groupMembership.Destination.ObjectId.ToString(),
                                                    DisplayName = "Test Group"
                                                });

            await mockSyncJobRepo.AddSyncJobAsync(syncJob);

            Mock<IGraphUpdaterService> graphUpdaterService = new Mock<IGraphUpdaterService>();
            graphUpdaterService.Setup(x => x.GetGroupOwnersAsync(It.IsAny<Guid>(), It.IsAny<int>())).ReturnsAsync(owners);
            graphUpdaterService.Setup(x => x.GetSyncJobAsync(It.IsAny<Guid>())).ReturnsAsync(new SyncJob());

            var context = new Mock<TaskOrchestrationContext>();
            context.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(NullLogger.Instance);
                        context.Setup(x => x.GetInput<MembershipHttpRequest>()).Returns(input);
            context.Setup(x => x.CallActivityAsync<SyncJob>(It.IsAny<TaskName>(), It.IsAny<JobReaderRequest>(), It.IsAny<TaskOptions>())).ReturnsAsync(syncJob);
            context.Setup(x => x.CallActivityAsync<Guid>(It.Is<TaskName>(x => x.Name == nameof(GetGroupFunction)), It.IsAny<GetGroupRequest>(), It.IsAny<TaskOptions>())).ReturnsAsync(syncJob.Group.GroupId);
            context.Setup(x => x.CallActivityAsync<string>(It.IsAny<TaskName>(), It.IsAny<FileDownloaderRequest>(), It.IsAny<TaskOptions>())).ReturnsAsync(JsonSerializer.Serialize(groupMembership));
            context.Setup(x => x.CallActivityAsync<bool>(It.IsAny<TaskName>(), It.IsAny<GroupValidatorRequest>(), It.IsAny<TaskOptions>()))
                    .Returns(async () => await CheckIfGroupExistsAsync(groupMembership, mockGraphUpdaterService, mailSenders));
            context.Setup(x => x.CallActivityAsync<string>(It.IsAny<TaskName>(), It.IsAny<GroupNameReaderRequest>(), It.IsAny<TaskOptions>()))
                    .Returns(async () => await CallGroupNameReaderFunctionAsync(mockGraphUpdaterService, groupNameReaderRequest));
            context.Setup(x => x.CallActivityAsync<List<AzureADUser>>(It.IsAny<TaskName>(), It.IsAny<GroupOwnersReaderRequest>(), It.IsAny<TaskOptions>()))
                    .Returns(async () => await CallGroupOwnersReaderFunctionAsync(graphUpdaterService.Object, groupOwnersReaderRequest));
            context.Setup(x => x.CallSubOrchestratorAsync<GroupUpdaterSubOrchestratorResponse>(It.IsAny<TaskName>(), It.IsAny<GroupUpdaterRequest>(), It.IsAny<TaskOptions>()))
                    .Returns(() => Task.FromResult(new GroupUpdaterSubOrchestratorResponse() { Status = Entities.GraphUpdaterStatus.GuestError, SuccessCount = 1, UsersNotFound = new List<AzureADUser>(), UsersAlreadyExist = new List<AzureADUser>() }));

            context.Setup(x => x.CallActivityAsync(It.Is<TaskName>(x => x.Name == nameof(JobStatusUpdaterFunction)), It.IsAny<JobStatusUpdaterRequest>(), It.IsAny<TaskOptions>()))
                   .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                   {
                       await CallJobStatusUpdaterFunctionAsync(graphUpdaterService.Object, request as JobStatusUpdaterRequest);
                   });

            var orchestrator = new OrchestratorFunction(mockTelemetryClient, mockGraphUpdaterService, mailSenders, _gmmResources, mockDeltaCachingConfig);
            var response = await orchestrator.RunOrchestratorAsync(context.Object);

            Assert.IsTrue(response == OrchestrationRuntimeStatus.Completed);

            graphUpdaterService.Verify(x => x.UpdateSyncJobStatusAsync(It.IsAny<SyncJob>(), SyncStatus.GuestUsersCannotBeAddedToUnifiedGroup, false, It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<int?>()));
            context.Verify(x => x.CallSubOrchestratorAsync<GroupUpdaterSubOrchestratorResponse>(It.IsAny<TaskName>(), It.IsAny<GroupUpdaterRequest>(), It.IsAny<TaskOptions>()), Times.Exactly(2));
        }

        [TestMethod]
        public async Task RunCacheUserUpdaterSubOrchestratorFunctionTest()
        {
            TelemetryClient mockTelemetryClient;
            Mock<IServiceBusQueueRepository> mockServiceBusQueueRepository;
            MockGraphUpdaterService mockGraphUpdaterService;
            DryRunValue dryRun;
            EmailSenderRecipient mailSenders;
            MockDatabaseSyncJobRepository mockSyncJobRepo;
            MockGraphGroupRepository mockGroupRepo;
            ThresholdConfig thresholdConfig;
            MockLocalizationRepository localizationRepository;
            MockBlobStorageRepository blobStorageRepository;
            MockDeltaCachingConfig mockDeltaCachingConfig;

            mockDeltaCachingConfig = new MockDeltaCachingConfig();
            mockTelemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            mockServiceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            mockGraphUpdaterService = new MockGraphUpdaterService(mockServiceBusQueueRepository.Object);
            dryRun = new DryRunValue(false);
            thresholdConfig = new ThresholdConfig(5, 3, 3, 10);
            mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com");

            mockGroupRepo = new MockGraphGroupRepository();
            mockSyncJobRepo = new MockDatabaseSyncJobRepository();
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
                MembershipType = "GroupMembership",
                Group = new Models.Group
                {
                    SyncJobId = groupMembership.SyncJobId,
                    GroupId = groupMembership.Destination.ObjectId
                },
                ThresholdPercentageForAdditions = -1,
                ThresholdPercentageForRemovals = -1,
                LastRunTime = DateTime.UtcNow.AddDays(-1),
                Requestor = "user@domail.com",
                Query = "[{ \"type\": \"GroupMembership\", \"sources\": [\"da144736-962b-4879-a304-acd9f5221e78\"]}]",
                RunId = Guid.NewGuid()
            };


            var input = new MembershipHttpRequest
            {
                FilePath = "/file/path/name.json",
                SyncJob = syncJob,
                ProjectedMemberCount = 10,
                MembersToBeAdded = 5,
                MembersToBeRemoved = 0,
                GroupId = syncJob.Group.GroupId
            };

            var fileDownloaderRequest = new FileDownloaderRequest
            {
                FilePath = input.FilePath,
                SyncJob = syncJob
            };

            var jobReaderRequest = new JobReaderRequest
            {
                SyncJob = syncJob
            };

            mockGraphUpdaterService.Jobs.Add(syncJob);
            mockGraphUpdaterService.Groups.Add(groupMembership.Destination.ObjectId, new Group { Id = groupMembership.Destination.ObjectId.ToString() });
            blobStorageRepository.Files.Add(input.FilePath, JsonSerializer.Serialize(groupMembership));

            var context = new Mock<TaskOrchestrationContext>();
            context.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(NullLogger.Instance);
                        context.Setup(x => x.GetInput<MembershipHttpRequest>()).Returns(input);
            context.Setup(x => x.CallActivityAsync<Guid>(It.Is<TaskName>(x => x.Name == nameof(GetGroupFunction)), It.IsAny<GetGroupRequest>(), It.IsAny<TaskOptions>())).ReturnsAsync(syncJob.Group.GroupId);
            context.Setup(x => x.CallActivityAsync<SyncJob>(It.IsAny<TaskName>(), It.IsAny<JobReaderRequest>(), It.IsAny<TaskOptions>()))
                    .Returns(async () => await RunJobReaderFunctionAsync(mockGraphUpdaterService, jobReaderRequest));
            context.Setup(x => x.CallActivityAsync<string>(It.IsAny<TaskName>(), It.IsAny<FileDownloaderRequest>(), It.IsAny<TaskOptions>()))
                    .ReturnsAsync(await DownloadFileAsync(fileDownloaderRequest, blobStorageRepository));
            context.Setup(x => x.CallActivityAsync<bool>(It.IsAny<TaskName>(), It.IsAny<GroupValidatorRequest>(), It.IsAny<TaskOptions>()))
                    .Returns(async () => await CheckIfGroupExistsAsync(groupMembership, mockGraphUpdaterService, mailSenders));

            var usersAlreadyExist = new List<AzureADUser>();

            var usersToRemove = new List<AzureADUser>
            {
                new AzureADUser { ObjectId = users[0].ObjectId, MembershipAction = MembershipAction.Remove }
            };
            context.Setup(x => x.CallSubOrchestratorAsync<GroupUpdaterSubOrchestratorResponse>(It.Is<TaskName>(x => x.Name == nameof(GroupUpdaterSubOrchestratorFunction)),
                                                                             It.IsAny<GroupUpdaterRequest>(), It.IsAny<TaskOptions>())).ReturnsAsync(() => new GroupUpdaterSubOrchestratorResponse
                                                                             {
                                                                                 Type = RequestType.Add,
                                                                                 SuccessCount = 1,
                                                                                 UsersNotFound = usersToRemove,
                                                                                 UsersAlreadyExist = usersAlreadyExist
                                                                             });

            var orchestrator = new OrchestratorFunction(mockTelemetryClient, mockGraphUpdaterService, mailSenders, _gmmResources, mockDeltaCachingConfig);
            await orchestrator.RunOrchestratorAsync(context.Object);

            context.Verify(x => x.CallSubOrchestratorAsync(It.Is<TaskName>(n => n.Name == nameof(CacheUserUpdaterSubOrchestratorFunction)), It.IsAny<CacheUserUpdaterRequest>(), It.IsAny<TaskOptions>()), Times.Exactly(3));
        }

        private async Task<SyncJob> RunJobReaderFunctionAsync(MockGraphUpdaterService graphUpdaterService, JobReaderRequest request)
        {
            var jobReaderFunction = new JobReaderFunction(NullLogger<JobReaderFunction>.Instance, graphUpdaterService);
            var syncJob = await jobReaderFunction.GetSyncJobAsync(request);
            return syncJob;
        }

        private async Task RunJobStatusUpdaterFunctionAsync(MockGraphUpdaterService graphUpdaterService, JobStatusUpdaterRequest request)
        {
            var jobStatusUpdaterFunction = new JobStatusUpdaterFunction(NullLogger<JobStatusUpdaterFunction>.Instance, graphUpdaterService);
            await jobStatusUpdaterFunction.UpdateJobStatusAsync(request);
        }

        private async Task<bool> CheckIfGroupExistsAsync(
                GroupMembership groupMembership,
                MockGraphUpdaterService mockGraphUpdaterService,
                EmailSenderRecipient mailSenders)
        {
            var request = new GroupValidatorRequest
            {
                SyncJob = groupMembership.SyncJob,
                GroupId = groupMembership.Destination.ObjectId
            };
            var groupValidatorFunction = new GroupValidatorFunction(NullLogger<GroupValidatorFunction>.Instance, mockGraphUpdaterService, mailSenders);

            return await groupValidatorFunction.ValidateGroupAsync(request);
        }

        private async Task<string> DownloadFileAsync(FileDownloaderRequest request, MockBlobStorageRepository blobStorageRepository)
        {
            var function = new FileDownloaderFunction(NullLogger<FileDownloaderFunction>.Instance, blobStorageRepository);
            var fileContent = await function.DownloadFileAsync(request);
            return fileContent;
        }

        private GroupMembership GetGroupMembership()
        {
            var json =
            "{" +
            "  \"Sources\": [" +
            "    {" +
            "      \"ObjectId\": \"8032abf6-b4b1-45b1-8e7e-40b0bd16d6eb\"" +
            "    }" +
            "  ]," +
            "  \"Destination\": {" +
            "    \"ObjectId\": \"dc04c21f-091a-44a9-a661-9211dd9ccf35\"" +
            "  }," +
            "  \"SyncJobId\": \"601f6c70-8fe1-496f-8446-befb15b5249a\"," +
            "  \"SourceMembers\": []," +
            "  \"RunId\": \"501f6c70-8fe1-496f-8446-befb15b5249a\"," +
            "  \"Errored\": false," +
            "  \"IsLastMessage\": true" +
            "}";
            var groupMembership = JsonSerializer.Deserialize<GroupMembership>(json);
            return groupMembership;
        }

        private async Task CallTelemetryTrackerFunctionAsync(TelemetryTrackerRequest request)
        {
            var telemetryTrackerFunction = new TelemetryTrackerFunction(NullLogger<TelemetryTrackerFunction>.Instance, _telemetryClient);
            await telemetryTrackerFunction.TrackEventAsync(request);
        }

        private async Task<string> CallGroupNameReaderFunctionAsync(
            MockGraphUpdaterService mockGraphUpdaterService,
            GroupNameReaderRequest request)
        {
            var function = new GroupNameReaderFunction(NullLogger<GroupNameReaderFunction>.Instance, mockGraphUpdaterService);
            return await function.GetGroupNameAsync(request);
        }

        private async Task<List<AzureADUser>> CallGroupOwnersReaderFunctionAsync(
            IGraphUpdaterService graphUpdaterService,
            GroupOwnersReaderRequest request)
        {
            var function = new GroupOwnersReaderFunction(NullLogger<GroupOwnersReaderFunction>.Instance, graphUpdaterService);
            return await function.GetGroupOwnersAsync(request);
        }

        private async Task CallJobStatusUpdaterFunctionAsync(
            IGraphUpdaterService graphUpdaterService,
            JobStatusUpdaterRequest request)
        {
            var function = new JobStatusUpdaterFunction(NullLogger<JobStatusUpdaterFunction>.Instance, graphUpdaterService);
            await function.UpdateJobStatusAsync(request);
        }
    }
}
