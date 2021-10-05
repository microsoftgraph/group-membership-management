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
using Repositories.MembershipDifference;
using Repositories.Mocks;
using Services.Entities;
using Services.Tests.Mocks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class OrchestratorTests
    {


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

            mockLoggingRepo = new MockLoggingRepository();
            mockTelemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            mockMailRepo = new MockMailRepository();
            mockGraphUpdaterService = new MockGraphUpdaterService(mockMailRepo);
            dryRun = new DryRunValue(false);
            mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass",
                                            "recipient@domain.com", "recipient@domain.com");

            calculator = new MembershipDifferenceCalculator<AzureADUser>();
            mockGroupRepo = new MockGraphGroupRepository();
            mockSyncJobRepo = new MockSyncJobRepository();
            deltaCalculatorService = new DeltaCalculatorService(
                                            calculator,
                                            mockSyncJobRepo,
                                            mockLoggingRepo,
                                            mailSenders,
                                            mockGraphUpdaterService,
                                            dryRun);


            var graphUpdaterRequest = new GraphUpdaterFunctionRequest
            {
                Message = GetMessageBody(),
                MessageLockToken = Guid.NewGuid().ToString(),
                MessageSessionId = "dc04c21f-091a-44a9-a661-9211dd9ccf35",
                RunId = Guid.NewGuid()
            };
            var groupMembership = JsonConvert.DeserializeObject<GroupMembership>(graphUpdaterRequest.Message);
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
            context.Setup(x => x.GetInput<GraphUpdaterFunctionRequest>()).Returns(graphUpdaterRequest);
            context.Setup(x => x.CallActivityAsync<SyncJob>(It.IsAny<string>(), It.IsAny<JobReaderRequest>())).ReturnsAsync(syncJob);
            context.Setup(x => x.CallActivityAsync(It.IsAny<string>(), It.IsAny<LoggerRequest>()))
                    .Callback<string, object>(async (name, request) => await LogMessageAsync((LoggerRequest)request, mockLoggingRepo));
            context.Setup(x => x.CallActivityAsync<GroupMembershipMessageResponse>(It.IsAny<string>(), It.IsAny<GraphUpdaterFunctionRequest>()))
                    .Returns(async () => await GetGroupMembershipMessageResponseAsync(graphUpdaterRequest, mockLoggingRepo));
            context.Setup(x => x.CallActivityAsync<bool>(It.IsAny<string>(), It.IsAny<GroupValidatorRequest>()))
                    .Returns(async () => await CheckIfGroupExistsAsync(groupMembership, mockLoggingRepo, mockGraphUpdaterService));
            context.Setup(x => x.CallSubOrchestratorAsync<List<AzureADUser>>(It.IsAny<string>(), It.IsAny<UsersReaderRequest>()))
                    .ReturnsAsync(destinationMembers);
            context.Setup(x => x.CallActivityAsync<DeltaResponse>(It.IsAny<string>(), It.IsAny<DeltaCalculatorRequest>()))
                    .Returns(async () => await GetDeltaResponseAsync(groupMembership, destinationMembers, mockLoggingRepo, deltaCalculatorService));

            mockGraphUpdaterService.Groups.Add(groupMembership.Destination.ObjectId, new Group { Id = groupMembership.Destination.ObjectId.ToString() });
            mockSyncJobRepo.ExistingSyncJobs.Add((syncJob.PartitionKey, syncJob.RowKey), syncJob);

            var orchestrator = new OrchestratorFunction(mockLoggingRepo, mockTelemetryClient, mockGraphUpdaterService, dryRun, mailSenders);
            var response = await orchestrator.RunOrchestratorAsync(context.Object);

            Assert.IsTrue(response.ShouldCompleteMessage);
            Assert.IsTrue(mockLoggingRepo.MessagesLogged.Any(x => x.Message.Contains($"{nameof(DeltaCalculatorFunction)} function completed")));
            Assert.IsTrue(mockLoggingRepo.MessagesLogged.Any(x => x.Message == nameof(OrchestratorFunction) + " function completed"));
            Assert.AreEqual(graphUpdaterRequest.MessageLockToken, response.CompletedGroupMembershipMessages.Single().LockToken);

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

            mockLoggingRepo = new MockLoggingRepository();
            mockTelemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            mockMailRepo = new MockMailRepository();
            mockGraphUpdaterService = new MockGraphUpdaterService(mockMailRepo);
            dryRun = new DryRunValue(false);
            mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass",
                                            "recipient@domain.com", "recipient@domain.com");

            calculator = new MembershipDifferenceCalculator<AzureADUser>();
            mockGroupRepo = new MockGraphGroupRepository();
            mockSyncJobRepo = new MockSyncJobRepository();
            deltaCalculatorService = new DeltaCalculatorService(
                                            calculator,
                                            mockSyncJobRepo,
                                            mockLoggingRepo,
                                            mailSenders,
                                            mockGraphUpdaterService,
                                            dryRun);

            var graphUpdaterRequest = new GraphUpdaterFunctionRequest
            {
                Message = GetMessageBody(),
                MessageLockToken = Guid.NewGuid().ToString(),
                MessageSessionId = "dc04c21f-091a-44a9-a661-9211dd9ccf35",
                RunId = Guid.NewGuid()
            };
            var groupMembership = JsonConvert.DeserializeObject<GroupMembership>(graphUpdaterRequest.Message);
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

            var context = new Mock<IDurableOrchestrationContext>();
            context.Setup(x => x.GetInput<GraphUpdaterFunctionRequest>()).Returns(graphUpdaterRequest);
            context.Setup(x => x.CallActivityAsync<SyncJob>(It.IsAny<string>(), It.IsAny<JobReaderRequest>())).ReturnsAsync(syncJob);
            context.Setup(x => x.CallActivityAsync(It.IsAny<string>(), It.IsAny<LoggerRequest>()))
                    .Callback<string, object>(async (name, request) => await LogMessageAsync((LoggerRequest)request, mockLoggingRepo));
            context.Setup(x => x.CallActivityAsync<GroupMembershipMessageResponse>(It.IsAny<string>(), It.IsAny<GraphUpdaterFunctionRequest>()))
                    .Returns(async () => await GetGroupMembershipMessageResponseAsync(graphUpdaterRequest, mockLoggingRepo));
            context.Setup(x => x.CallActivityAsync<bool>(It.IsAny<string>(), It.IsAny<GroupValidatorRequest>()))
                    .Returns(async () => await CheckIfGroupExistsAsync(groupMembership, mockLoggingRepo, mockGraphUpdaterService));
            context.Setup(x => x.CallSubOrchestratorAsync<List<AzureADUser>>(It.IsAny<string>(), It.IsAny<UsersReaderRequest>()))
                    .ReturnsAsync(destinationMembers);
            context.Setup(x => x.CallActivityAsync<DeltaResponse>(It.IsAny<string>(), It.IsAny<DeltaCalculatorRequest>()))
                    .Returns(async () => await GetDeltaResponseAsync(groupMembership, destinationMembers, mockLoggingRepo, deltaCalculatorService));
            context.Setup(x => x.CallActivityAsync<string>(It.IsAny<string>(), It.IsAny<GroupNameReaderRequest>()))
                    .ReturnsAsync("Target group");

            mockGraphUpdaterService.Groups.Add(groupMembership.Destination.ObjectId, new Group { Id = groupMembership.Destination.ObjectId.ToString() });
            mockSyncJobRepo.ExistingSyncJobs.Add((syncJob.PartitionKey, syncJob.RowKey), syncJob);

            var orchestrator = new OrchestratorFunction(mockLoggingRepo, mockTelemetryClient, mockGraphUpdaterService, dryRun, mailSenders);
            var response = await orchestrator.RunOrchestratorAsync(context.Object);

            Assert.IsTrue(response.ShouldCompleteMessage);
            Assert.IsTrue(mockLoggingRepo.MessagesLogged.Any(x => x.Message.Contains($"{nameof(DeltaCalculatorFunction)} function completed")));
            Assert.IsTrue(mockLoggingRepo.MessagesLogged.Any(x => x.Message == nameof(OrchestratorFunction) + " function completed"));
            Assert.AreEqual(graphUpdaterRequest.MessageLockToken, response.CompletedGroupMembershipMessages.Single().LockToken);
            Assert.IsNotNull(mockLoggingRepo.SyncJobProperties);
            Assert.AreEqual(mockLoggingRepo.SyncJobProperties["RunId"], syncJob.RunId.ToString());
            Assert.AreEqual(mockLoggingRepo.SyncJobProperties["PartitionKey"], syncJob.PartitionKey);
            Assert.AreEqual(mockLoggingRepo.SyncJobProperties["RowKey"], syncJob.RowKey);

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

            mockLoggingRepo = new MockLoggingRepository();
            mockTelemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            mockMailRepo = new MockMailRepository();
            mockGraphUpdaterService = new MockGraphUpdaterService(mockMailRepo);
            dryRun = new DryRunValue(false);
            mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass",
                                            "recipient@domain.com", "recipient@domain.com");

            calculator = new MembershipDifferenceCalculator<AzureADUser>();
            mockGroupRepo = new MockGraphGroupRepository();
            mockSyncJobRepo = new MockSyncJobRepository();
            deltaCalculatorService = new DeltaCalculatorService(
                                            calculator,
                                            mockSyncJobRepo,
                                            mockLoggingRepo,
                                            mailSenders,
                                            mockGraphUpdaterService,
                                            dryRun);

            var graphUpdaterRequest = new GraphUpdaterFunctionRequest
            {
                Message = GetMessageBody(),
                MessageLockToken = Guid.NewGuid().ToString(),
                MessageSessionId = "dc04c21f-091a-44a9-a661-9211dd9ccf35",
                RunId = Guid.NewGuid()
            };
            var groupMembership = JsonConvert.DeserializeObject<GroupMembership>(graphUpdaterRequest.Message);
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

            var context = new Mock<IDurableOrchestrationContext>();
            context.Setup(x => x.GetInput<GraphUpdaterFunctionRequest>()).Returns(graphUpdaterRequest);
            context.Setup(x => x.CallActivityAsync<SyncJob>(It.IsAny<string>(), It.IsAny<JobReaderRequest>())).ReturnsAsync(syncJob);
            context.Setup(x => x.CallActivityAsync(It.IsAny<string>(), It.IsAny<LoggerRequest>()))
                    .Callback<string, object>(async (name, request) => await LogMessageAsync((LoggerRequest)request, mockLoggingRepo));
            context.Setup(x => x.CallActivityAsync<GroupMembershipMessageResponse>(It.IsAny<string>(), It.IsAny<GraphUpdaterFunctionRequest>()))
                    .Throws(new Exception("Something went wrong!"));

            JobStatusUpdaterRequest updateJobRequest = null;
            context.Setup(x => x.CallActivityAsync(It.IsAny<string>(), It.IsAny<JobStatusUpdaterRequest>()))
                    .Callback<string, object>((name, request) =>
                    {
                        updateJobRequest = request as JobStatusUpdaterRequest;
                    });

            var orchestrator = new OrchestratorFunction(mockLoggingRepo, mockTelemetryClient, mockGraphUpdaterService, dryRun, mailSenders);
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

            mockLoggingRepo = new MockLoggingRepository();
            mockTelemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            mockMailRepo = new MockMailRepository();
            mockGraphUpdaterService = new MockGraphUpdaterService(mockMailRepo);
            dryRun = new DryRunValue(false);
            mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass",
                                            "recipient@domain.com", "recipient@domain.com");

            calculator = new MembershipDifferenceCalculator<AzureADUser>();
            mockGroupRepo = new MockGraphGroupRepository();
            mockSyncJobRepo = new MockSyncJobRepository();
            deltaCalculatorService = new DeltaCalculatorService(
                                            calculator,
                                            mockSyncJobRepo,
                                            mockLoggingRepo,
                                            mailSenders,
                                            mockGraphUpdaterService,
                                            dryRun);

            var graphUpdaterRequest = new GraphUpdaterFunctionRequest
            {
                Message = GetMessageBody(),
                MessageLockToken = Guid.NewGuid().ToString(),
                MessageSessionId = "dc04c21f-091a-44a9-a661-9211dd9ccf35",
                RunId = Guid.NewGuid()
            };
            var groupMembership = JsonConvert.DeserializeObject<GroupMembership>(graphUpdaterRequest.Message);
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
            context.Setup(x => x.GetInput<GraphUpdaterFunctionRequest>()).Returns(graphUpdaterRequest);
            context.Setup(x => x.CallActivityAsync<SyncJob>(It.IsAny<string>(), It.IsAny<JobReaderRequest>())).ReturnsAsync(syncJob);
            context.Setup(x => x.CallActivityAsync(It.IsAny<string>(), It.IsAny<LoggerRequest>()))
                    .Callback<string, object>(async (name, request) => await LogMessageAsync((LoggerRequest)request, mockLoggingRepo));
            context.Setup(x => x.CallActivityAsync<GroupMembershipMessageResponse>(It.IsAny<string>(), It.IsAny<GraphUpdaterFunctionRequest>()))
                    .Returns(async () => await GetGroupMembershipMessageResponseAsync(graphUpdaterRequest, mockLoggingRepo));
            context.Setup(x => x.CallActivityAsync<bool>(It.IsAny<string>(), It.IsAny<GroupValidatorRequest>()))
                    .Returns(async () => await CheckIfGroupExistsAsync(groupMembership, mockLoggingRepo, mockGraphUpdaterService));

            JobStatusUpdaterRequest updateJobRequest = null;
            context.Setup(x => x.CallActivityAsync(It.IsAny<string>(), It.IsAny<JobStatusUpdaterRequest>()))
                    .Callback<string, object>((name, request) =>
                    {
                        updateJobRequest = request as JobStatusUpdaterRequest;
                    });

            var orchestrator = new OrchestratorFunction(mockLoggingRepo, mockTelemetryClient, mockGraphUpdaterService, dryRun, mailSenders);
            var response = await orchestrator.RunOrchestratorAsync(context.Object);

            Assert.AreEqual(SyncStatus.Error, updateJobRequest.Status);
            Assert.IsTrue(response.ShouldCompleteMessage);
            Assert.IsTrue(mockLoggingRepo.MessagesLogged.Any(x => x.Message.Contains($"Group with ID {groupMembership.Destination.ObjectId} doesn't exist.")));
            Assert.IsTrue(mockLoggingRepo.MessagesLogged.Any(x => x.Message == nameof(OrchestratorFunction) + " function did not complete"));
            Assert.AreEqual(graphUpdaterRequest.MessageLockToken, response.CompletedGroupMembershipMessages.Single().LockToken);
            Assert.IsNotNull(mockLoggingRepo.SyncJobProperties);
            Assert.AreEqual(mockLoggingRepo.SyncJobProperties["RunId"], syncJob.RunId.ToString());
            Assert.AreEqual(mockLoggingRepo.SyncJobProperties["PartitionKey"], syncJob.PartitionKey);
            Assert.AreEqual(mockLoggingRepo.SyncJobProperties["RowKey"], syncJob.RowKey);
        }

        [TestMethod]
        public async Task RunOrchestratorCancelationRequest()
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

            mockLoggingRepo = new MockLoggingRepository();
            mockTelemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            mockMailRepo = new MockMailRepository();
            mockGraphUpdaterService = new MockGraphUpdaterService(mockMailRepo);
            dryRun = new DryRunValue(false);
            mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass",
                                            "recipient@domain.com", "recipient@domain.com");

            calculator = new MembershipDifferenceCalculator<AzureADUser>();
            mockGroupRepo = new MockGraphGroupRepository();
            mockSyncJobRepo = new MockSyncJobRepository();
            deltaCalculatorService = new DeltaCalculatorService(
                                            calculator,
                                            mockSyncJobRepo,
                                            mockLoggingRepo,
                                            mailSenders,
                                            mockGraphUpdaterService,
                                            dryRun);

            var graphUpdaterRequest = new GraphUpdaterFunctionRequest
            {
                Message = GetMessageBody(),
                MessageLockToken = Guid.NewGuid().ToString(),
                MessageSessionId = "dc04c21f-091a-44a9-a661-9211dd9ccf35",
                RunId = Guid.NewGuid(),
                IsCancelationRequest = true
            };

            var groupMembership = JsonConvert.DeserializeObject<GroupMembership>(graphUpdaterRequest.Message);
            groupMembership.IsLastMessage = false;

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
            var jobUpdateRequest = default(JobStatusUpdaterRequest);

            context.Setup(x => x.GetInput<GraphUpdaterFunctionRequest>()).Returns(graphUpdaterRequest);
            context.Setup(x => x.CallActivityAsync<SyncJob>(It.IsAny<string>(), It.IsAny<JobReaderRequest>())).ReturnsAsync(syncJob);
            context.Setup(x => x.CallActivityAsync(It.IsAny<string>(), It.IsAny<LoggerRequest>()))
                    .Callback<string, object>(async (name, request) => await LogMessageAsync((LoggerRequest)request, mockLoggingRepo));
            context.Setup(x => x.CallActivityAsync<GroupMembershipMessageResponse>(It.IsAny<string>(), It.IsAny<GraphUpdaterFunctionRequest>()))
                    .ReturnsAsync(new GroupMembershipMessageResponse());
            context.Setup(x => x.CallActivityAsync(It.IsAny<string>(), It.IsAny<JobStatusUpdaterRequest>()))
                    .Callback<string, object>((name, request) =>
                    {
                        jobUpdateRequest = request as JobStatusUpdaterRequest;
                    });

            var orchestrator = new OrchestratorFunction(mockLoggingRepo, mockTelemetryClient, mockGraphUpdaterService, dryRun, mailSenders);
            var response = await orchestrator.RunOrchestratorAsync(context.Object);

            Assert.IsFalse(response.ShouldCompleteMessage);
            Assert.IsTrue(mockLoggingRepo.MessagesLogged.Any(x => x.Message.Contains($"Canceling session")));
            Assert.IsNotNull(jobUpdateRequest);
            Assert.AreEqual(SyncStatus.Error, jobUpdateRequest.Status);
            Assert.IsNotNull(mockLoggingRepo.SyncJobProperties);
            Assert.AreEqual(mockLoggingRepo.SyncJobProperties["RunId"], syncJob.RunId.ToString());
            Assert.AreEqual(mockLoggingRepo.SyncJobProperties["PartitionKey"], syncJob.PartitionKey);
            Assert.AreEqual(mockLoggingRepo.SyncJobProperties["RowKey"], syncJob.RowKey);

            context.Verify(x => x.CallActivityAsync(It.IsAny<string>(), It.IsAny<JobStatusUpdaterRequest>()), Times.Once());
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

            mockLoggingRepo = new MockLoggingRepository();
            mockTelemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            mockMailRepo = new MockMailRepository();
            mockGraphUpdaterService = new MockGraphUpdaterService(mockMailRepo);
            dryRun = new DryRunValue(false);
            mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass",
                                            "recipient@domain.com", "recipient@domain.com");

            calculator = new MembershipDifferenceCalculator<AzureADUser>();
            mockGroupRepo = new MockGraphGroupRepository();
            mockSyncJobRepo = new MockSyncJobRepository();
            deltaCalculatorService = new DeltaCalculatorService(
                                            calculator,
                                            mockSyncJobRepo,
                                            mockLoggingRepo,
                                            mailSenders,
                                            mockGraphUpdaterService,
                                            dryRun);

            var graphUpdaterRequest = new GraphUpdaterFunctionRequest
            {
                Message = GetMessageBody(),
                MessageLockToken = Guid.NewGuid().ToString(),
                MessageSessionId = "dc04c21f-091a-44a9-a661-9211dd9ccf35",
                RunId = Guid.NewGuid()
            };
            var groupMembership = JsonConvert.DeserializeObject<GroupMembership>(graphUpdaterRequest.Message);
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
                RunId = Guid.NewGuid()
            };

            var context = new Mock<IDurableOrchestrationContext>();
            context.Setup(x => x.GetInput<GraphUpdaterFunctionRequest>()).Returns(graphUpdaterRequest);
            context.Setup(x => x.CallActivityAsync<SyncJob>(It.IsAny<string>(), It.IsAny<JobReaderRequest>())).ReturnsAsync(syncJob);
            context.Setup(x => x.CallActivityAsync(It.IsAny<string>(), It.IsAny<LoggerRequest>()))
                    .Callback<string, object>(async (name, request) => await LogMessageAsync((LoggerRequest)request, mockLoggingRepo));
            context.Setup(x => x.CallActivityAsync<GroupMembershipMessageResponse>(It.IsAny<string>(), It.IsAny<GraphUpdaterFunctionRequest>()))
                    .Returns(async () => await GetGroupMembershipMessageResponseAsync(graphUpdaterRequest, mockLoggingRepo));
            context.Setup(x => x.CallActivityAsync<bool>(It.IsAny<string>(), It.IsAny<GroupValidatorRequest>()))
                    .Returns(async () => await CheckIfGroupExistsAsync(groupMembership, mockLoggingRepo, mockGraphUpdaterService));
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

            var orchestrator = new OrchestratorFunction(mockLoggingRepo, mockTelemetryClient, mockGraphUpdaterService, dryRun, mailSenders);
            var response = await orchestrator.RunOrchestratorAsync(context.Object);

            Assert.IsTrue(mockLoggingRepo.MessagesLogged.Any(x => x.Message.Contains($"is lesser than threshold value {syncJob.ThresholdPercentageForRemovals}")));
            Assert.IsTrue(mockLoggingRepo.MessagesLogged.Any(x => x.Message.Contains($"Threshold exceeded, no changes made to group")));
            Assert.IsTrue(mockLoggingRepo.MessagesLogged.Any(x => x.Message.Contains($"{nameof(DeltaCalculatorFunction)} function completed")));
            Assert.IsTrue(mockLoggingRepo.MessagesLogged.Any(x => x.Message == nameof(OrchestratorFunction) + " function did not complete"));
            Assert.IsTrue(mockMailRepo.SentEmails.First().Content == "SyncThresholdDecreaseEmailBody");
            Assert.AreEqual(SyncStatus.Idle, updateJobRequest.Status);
            Assert.IsTrue(response.ShouldCompleteMessage);
            Assert.AreEqual(graphUpdaterRequest.MessageLockToken, response.CompletedGroupMembershipMessages.Single().LockToken);
            Assert.IsNotNull(mockLoggingRepo.SyncJobProperties);
            Assert.AreEqual(mockLoggingRepo.SyncJobProperties["RunId"], syncJob.RunId.ToString());
            Assert.AreEqual(mockLoggingRepo.SyncJobProperties["PartitionKey"], syncJob.PartitionKey);
            Assert.AreEqual(mockLoggingRepo.SyncJobProperties["RowKey"], syncJob.RowKey);
        }

        private async Task<GroupMembershipMessageResponse> GetGroupMembershipMessageResponseAsync(GraphUpdaterFunctionRequest request, MockLoggingRepository mockLoggingRepo)
        {
            var messageCollector = new MessageCollector(mockLoggingRepo);
            var messageCollectorFunction = new MessageCollectorFunction(messageCollector, mockLoggingRepo);
            var response = await messageCollectorFunction.CollectMessagesAsync(request);
            return response;
        }

        private async Task<bool> CheckIfGroupExistsAsync(
                GroupMembership groupMembership,
                MockLoggingRepository mockLoggingRepo,
                MockGraphUpdaterService mockGraphUpdaterService)
        {
            var request = new GroupValidatorRequest
            {
                RunId = groupMembership.RunId,
                GroupId = groupMembership.Destination.ObjectId,
                JobPartitionKey = groupMembership.SyncJobPartitionKey,
                JobRowKey = groupMembership.SyncJobRowKey
            };
            var groupValidatorFunction = new GroupValidatorFunction(mockLoggingRepo, mockGraphUpdaterService);

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

        private string GetMessageBody()
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

            return json;
        }

        private async Task LogMessageAsync(LoggerRequest loggerRequest, MockLoggingRepository mockLoggingRepository)
        {
            var loggerFunction = new LoggerFunction(mockLoggingRepository);
            await loggerFunction.LogMessageAsync(loggerRequest);
        }
    }
}

