// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using DIConcreteTypes;
using GraphUpdater.Activity.JobTracker;
using Hosts.GraphUpdater;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Identity.Client;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.ServiceBus;
using Moq;
using Repositories.Contracts;
using Repositories.Mocks;
using Services.Contracts;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Group = Microsoft.Graph.Models.Group;

namespace Services.Tests
{
    [TestClass]
    public class OrchestratorMultiLaneTests
    {
        GMMResources _gmmResources = new GMMResources
        {
            LearnMoreAboutGMMUrl = "http://learn-more-url"
        };

        MockLoggingRepository _mockLoggingRepo;
        Mock<IDurableOrchestrationContext> _context;
        //Mock<TaskOrchestrationEntityFeature> _entitiesMock;

        SyncJob _syncJob;
        JobState _jobState;
        JobTrackerEntity _jobTrackerEntity;
        GroupMembership _groupMembership;
        EmailSenderRecipient _mailSenders;
        TelemetryClient _telemetryClient;
        JobStatusUpdaterRequest _updateJobRequest;
        MockDeltaCachingConfig _mockDeltaCachingConfig;
        MockGraphUpdaterService _mockGraphUpdaterService;
        OrchestratorMultiLaneRequest _orchestratorMultiLaneRequest;
        Mock<IServiceBusQueueRepository> _mockServiceBusQueueRepository;
        GroupUpdaterResponse _groupUpdaterFunctionResponse;

        int _membersAdded = 1;
        int _membersRemoved = 0;
        GraphUpdaterStatus _graphUpdaterStatus = GraphUpdaterStatus.Ok;

        [TestInitialize]
        public void SetupTest()
        {
            _groupMembership = GetGroupMembership();
            _syncJob = new SyncJob
            {
                Id = _groupMembership.SyncJobId,
                MembershipType = "GroupMembership",
                ThresholdPercentageForAdditions = -1,
                ThresholdPercentageForRemovals = -1,
                LastRunTime = DateTime.UtcNow.AddDays(-1),
                Requestor = "user@domail.com",
                Query = "[{ \"type\": \"GroupMembership\", \"sources\": [\"da144736-962b-4879-a304-acd9f5221e78\"]}]",
                RunId = _groupMembership.RunId,
                Status = SyncStatus.InProgress.ToString(),
                Destination = $"[{{\"value\":{{\"objectId\":\"{Guid.NewGuid()}\"}},\"type\":\"GroupMembership\"}}]"
            };

            _groupMembership.SyncJob = _syncJob;

            _jobState = new JobState
            {
                IsValidGroup = true,
                TotalMembersAdded = 0,
                TotalMembersRemoved = 0,
            };

            _jobTrackerEntity = new JobTrackerEntity();
            _jobTrackerEntity.JobState = _jobState;

            _orchestratorMultiLaneRequest = new OrchestratorMultiLaneRequest
            {
                GroupMembership = _groupMembership,
                RunId = _groupMembership.RunId,
                SubscriptionName = "GraphUpdater_small_1",
                TopicName = "membershipupdaters"
            };

            _mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com");

            _telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());

            _mockLoggingRepo = new MockLoggingRepository();
            _mockDeltaCachingConfig = new MockDeltaCachingConfig();
            _mockServiceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            _mockGraphUpdaterService = new MockGraphUpdaterService(_mockServiceBusQueueRepository.Object);

            _context = new Mock<IDurableOrchestrationContext>();
            _context.Setup(x => x.CreateEntityProxy<IJobTracker>(It.IsAny<EntityId>())).Returns(_jobTrackerEntity);
            _context.Setup(x => x.GetInput<OrchestratorMultiLaneRequest>()).Returns(() => _orchestratorMultiLaneRequest);
            _context.Setup(x => x.CallActivityAsync<SyncJob>(It.IsAny<string>(), It.IsAny<JobReaderRequest>())).ReturnsAsync(() => _syncJob);
            _context.Setup(x => x.CallActivityAsync(It.IsAny<string>(), It.IsAny<LoggerRequest>()))
                    .Callback<string, object>(async (name, request) => await CallLogMessageFunctionAsync((LoggerRequest)request, _mockLoggingRepo));

            _context.Setup(x => x.CallActivityAsync<bool>(It.IsAny<string>(), It.IsAny<GroupValidatorRequest>()))
                    .Returns(async () => await CheckIfGroupExistsAsync(_groupMembership, _mockLoggingRepo, _mockGraphUpdaterService, _mailSenders));

            _context.Setup(x => x.CallActivityAsync<GroupUpdaterResponse>(It.IsAny<string>(), It.IsAny<GroupUpdaterRequest>()))
                .Callback<string, object>((name, request) =>
                {
                    var guRequest = request as GroupUpdaterRequest;

                    _groupUpdaterFunctionResponse = new GroupUpdaterResponse()
                    {
                        SuccessCount = guRequest.Type == RequestType.Add ? _membersAdded : _membersRemoved,
                        UsersNotFound = new List<AzureADUser>(),
                        UsersAlreadyExist = new List<AzureADUser>(),
                        Status = _graphUpdaterStatus
                    };
                })
                .ReturnsAsync(() => _groupUpdaterFunctionResponse);

            _context.Setup(x => x.CallActivityAsync(It.IsAny<string>(), It.IsAny<JobStatusUpdaterRequest>()))
                    .Callback<string, object>((name, request) =>
                    {
                        _updateJobRequest = request as JobStatusUpdaterRequest;
                    });
        }

        [TestMethod]
        public async Task TestMSALTransientExceptionAsync()
        {
            _mockLoggingRepo.SetSyncJobProperties(_syncJob.RunId.Value, _syncJob.ToDictionary());

            // triggers group validation
            _jobState.IsValidGroup = null;

            _context.Setup(x => x.CallActivityAsync<bool>(It.IsAny<string>(), It.IsAny<GroupValidatorRequest>()))
                .ThrowsAsync(new MsalClientException("MULTIPLE_MATCHING_TOKENS_DETECTED", "MULTIPLE_MATCHING_TOKENS_DETECTED"));

            var orchestrator = new OrchestratorMultiLaneFunction(_telemetryClient, _mockGraphUpdaterService, _mailSenders, _gmmResources, _mockLoggingRepo, _mockDeltaCachingConfig);
            await Assert.ThrowsExceptionAsync<MsalClientException>(async () => await orchestrator.RunOrchestratorAsync(_context.Object));

            Assert.IsFalse(_mockLoggingRepo.MessagesLogged.Any(x => x.Message == nameof(OrchestratorMultiLaneFunction) + " function completed"));
            Assert.IsTrue(_mockLoggingRepo.MessagesLogged.Any(x => x.Message.Contains("Caught MsalClientException, marking sync job status as transient error.")));
            Assert.AreEqual(SyncStatus.TransientError, _updateJobRequest.Status);

            var logProperties = _mockLoggingRepo.SyncJobPropertiesHistory[_syncJob.RunId.Value].Properties;

            Assert.IsNotNull(_mockLoggingRepo.SyncJobProperties);
            Assert.AreEqual(logProperties["RunId"], _syncJob.RunId.ToString());
            Assert.AreEqual(logProperties["Id"], _syncJob.Id.ToString());
        }

        [TestMethod]
        public async Task RunOrchestratorValidSyncTest()
        {
            _mockLoggingRepo.SetSyncJobProperties(_syncJob.RunId.Value, _syncJob.ToDictionary());
            _mockGraphUpdaterService.Jobs.Add(_syncJob);
            _mockGraphUpdaterService.Groups.Add(_groupMembership.Destination.ObjectId, new Group { Id = _groupMembership.Destination.ObjectId.ToString() });

            var orchestrator = new OrchestratorMultiLaneFunction(_telemetryClient, _mockGraphUpdaterService, _mailSenders, _gmmResources, _mockLoggingRepo, _mockDeltaCachingConfig);
            var response = await orchestrator.RunOrchestratorAsync(_context.Object);

            Assert.IsTrue(response == OrchestrationRuntimeStatus.Completed);
            Assert.IsTrue(_mockLoggingRepo.MessagesLogged.Any(x => x.Message == nameof(OrchestratorMultiLaneFunction) + " function completed"));

            _context.Verify(x => x.CallActivityAsync<GroupUpdaterResponse>(It.IsAny<string>(), It.IsAny<GroupUpdaterRequest>()), Times.Exactly(2));

            var logProperties = _mockLoggingRepo.SyncJobPropertiesHistory[_syncJob.RunId.Value].Properties;

            Assert.IsNotNull(_mockLoggingRepo.SyncJobProperties);
            Assert.AreEqual(logProperties["RunId"], _syncJob.RunId.ToString());
            Assert.AreEqual(SyncStatus.Idle, _updateJobRequest.Status);
        }

        [TestMethod]
        public async Task RunOrchestratorInitialSyncTest()
        {
            _groupMembership.SyncJob.LastRunTime = SqlDateTime.MinValue.Value;
            _jobState.TotalMembersToAdd = _membersAdded;
            _mockLoggingRepo.SetSyncJobProperties(_syncJob.RunId.Value, _syncJob.ToDictionary());

            var orchestrator = new OrchestratorMultiLaneFunction(_telemetryClient, _mockGraphUpdaterService, _mailSenders, _gmmResources, _mockLoggingRepo, _mockDeltaCachingConfig);
            var response = await orchestrator.RunOrchestratorAsync(_context.Object);

            Assert.IsTrue(response == OrchestrationRuntimeStatus.Completed);
            Assert.IsTrue(_mockLoggingRepo.MessagesLogged.Any(x => x.Message == nameof(OrchestratorMultiLaneFunction) + " function completed"));

            var logProperties = _mockLoggingRepo.SyncJobPropertiesHistory[_syncJob.RunId.Value].Properties;

            Assert.IsNotNull(_mockLoggingRepo.SyncJobProperties);
            Assert.AreEqual(logProperties["RunId"], _syncJob.RunId.ToString());
            Assert.AreEqual(logProperties["Id"], _syncJob.Id.ToString());

            _context.Verify(x => x.CallActivityAsync(nameof(EmailSenderFunction), It.IsAny<EmailSenderRequest>()), Times.Once);
            _context.Verify(x => x.CallActivityAsync<GroupUpdaterResponse>(It.IsAny<string>(), It.IsAny<GroupUpdaterRequest>()), Times.Exactly(2));
            _context.Verify(x => x.CallActivityAsync(nameof(JobStatusUpdaterFunction), It.Is<JobStatusUpdaterRequest>(y => y.Status == SyncStatus.Idle)), Times.Once);
        }

        [TestMethod]
        public async Task RunOrchestratorWithJobOnErrorStatusTest()
        {
            _syncJob.Status = SyncStatus.Error.ToString();
            _groupMembership.TotalMessageCount = 10;

            _mockLoggingRepo.SetSyncJobProperties(_syncJob.RunId.Value, _syncJob.ToDictionary());
            _mockGraphUpdaterService.Jobs.Add(_syncJob);
            _mockGraphUpdaterService.Groups.Add(_groupMembership.Destination.ObjectId, new Group { Id = _groupMembership.Destination.ObjectId.ToString() });

            var orchestrator = new OrchestratorMultiLaneFunction(_telemetryClient, _mockGraphUpdaterService, _mailSenders, _gmmResources, _mockLoggingRepo, _mockDeltaCachingConfig);
            var response = await orchestrator.RunOrchestratorAsync(_context.Object);

            Assert.IsTrue(response == OrchestrationRuntimeStatus.Completed);
            Assert.IsTrue(_mockLoggingRepo.MessagesLogged.Any(x => x.Message.Contains("Skipping additional messages if any")));

            _context.Verify(x => x.CallActivityAsync<GroupUpdaterResponse>(It.IsAny<string>(), It.IsAny<GroupUpdaterRequest>()), Times.Never());
            _context.Verify(x => x.CallActivityAsync(nameof(MessageRemoverFunction), It.IsAny<GroupUpdaterRequest>()), Times.Never());

            var logProperties = _mockLoggingRepo.SyncJobPropertiesHistory[_syncJob.RunId.Value].Properties;

            Assert.IsNotNull(_mockLoggingRepo.SyncJobProperties);
            Assert.AreEqual(logProperties["RunId"], _syncJob.RunId.ToString());
        }

        [TestMethod]
        public async Task RunOrchestratorIncompleteMessagesTest()
        {
            _mockLoggingRepo.SetSyncJobProperties(_syncJob.RunId.Value, _syncJob.ToDictionary());
            _mockGraphUpdaterService.Jobs.Add(_syncJob);
            _mockGraphUpdaterService.Groups.Add(_groupMembership.Destination.ObjectId, new Group { Id = _groupMembership.Destination.ObjectId.ToString() });

            // Set additional expected message
            _groupMembership.TotalMessageCount++;

            var orchestrator = new OrchestratorMultiLaneFunction(_telemetryClient, _mockGraphUpdaterService, _mailSenders, _gmmResources, _mockLoggingRepo, _mockDeltaCachingConfig);
            var response = await orchestrator.RunOrchestratorAsync(_context.Object);

            Assert.IsTrue(response == OrchestrationRuntimeStatus.Completed);
            Assert.IsTrue(_mockLoggingRepo.MessagesLogged.Any(x => x.Message == nameof(OrchestratorMultiLaneFunction) + " function completed"));

            _context.Verify(x => x.CallActivityAsync<GroupUpdaterResponse>(It.IsAny<string>(), It.IsAny<GroupUpdaterRequest>()), Times.Exactly(2));
            _context.Verify(x => x.CallActivityAsync(nameof(JobStatusUpdaterFunction), It.Is<JobStatusUpdaterRequest>(y => y.Status == SyncStatus.Error)), Times.Once);

            var logProperties = _mockLoggingRepo.SyncJobPropertiesHistory[_syncJob.RunId.Value].Properties;

            Assert.IsTrue(_mockLoggingRepo.MessagesLogged.Any(x => x.Message.StartsWith("Not all messages were processed")));
            Assert.IsNotNull(_mockLoggingRepo.SyncJobProperties);
            Assert.AreEqual(logProperties["RunId"], _syncJob.RunId.ToString());
            Assert.AreEqual(SyncStatus.Error, _updateJobRequest.Status);
        }

        [TestMethod]
        public async Task TestHttpTransientExceptionAsync()
        {
            _mockLoggingRepo.SetSyncJobProperties(_syncJob.RunId.Value, _syncJob.ToDictionary());

            _context.Setup(x => x.CallActivityAsync<GroupUpdaterResponse>(It.IsAny<string>(), It.IsAny<GroupUpdaterRequest>()))
                    .Throws<HttpRequestException>();

            var orchestrator = new OrchestratorMultiLaneFunction(_telemetryClient, _mockGraphUpdaterService, _mailSenders, _gmmResources, _mockLoggingRepo, _mockDeltaCachingConfig);
            await Assert.ThrowsExceptionAsync<HttpRequestException>(async () => await orchestrator.RunOrchestratorAsync(_context.Object));

            Assert.IsFalse(_mockLoggingRepo.MessagesLogged.Any(x => x.Message == nameof(OrchestratorMultiLaneFunction) + " function completed"));
            Assert.IsTrue(_mockLoggingRepo.MessagesLogged.Any(x => x.Message.Contains("Caught HttpRequestException, marking sync job status as transient error.")));
            Assert.AreEqual(SyncStatus.TransientError, _updateJobRequest.Status);

            var logProperties = _mockLoggingRepo.SyncJobPropertiesHistory[_syncJob.RunId.Value].Properties;

            Assert.IsNotNull(_mockLoggingRepo.SyncJobProperties);
            Assert.AreEqual(logProperties["RunId"], _syncJob.RunId.ToString());
            Assert.AreEqual(logProperties["Id"], _syncJob.Id.ToString());
        }

        [TestMethod]
        public async Task RunOrchestratorExceptionTest()
        {
            _mockLoggingRepo.SetSyncJobProperties(_syncJob.RunId.Value, _syncJob.ToDictionary());

            _context.Setup(x => x.CallActivityAsync<GroupUpdaterResponse>(It.IsAny<string>(), It.IsAny<GroupUpdaterRequest>()))
                    .Throws<Exception>();

            var orchestrator = new OrchestratorMultiLaneFunction(_telemetryClient, _mockGraphUpdaterService, _mailSenders, _gmmResources, _mockLoggingRepo, _mockDeltaCachingConfig);
            await Assert.ThrowsExceptionAsync<Exception>(async () => await orchestrator.RunOrchestratorAsync(_context.Object));

            Assert.IsFalse(_mockLoggingRepo.MessagesLogged.Any(x => x.Message == nameof(OrchestratorMultiLaneFunction) + " function completed"));
            Assert.IsTrue(_mockLoggingRepo.MessagesLogged.Any(x => x.Message.Contains("Caught unexpected exception, marking sync job as errored.")));

            var logProperties = _mockLoggingRepo.SyncJobPropertiesHistory[_syncJob.RunId.Value].Properties;

            Assert.IsNotNull(_mockLoggingRepo.SyncJobProperties);
            Assert.AreEqual(logProperties["RunId"], _syncJob.RunId.ToString());
            Assert.AreEqual(logProperties["Id"], _syncJob.Id.ToString());
        }

        [TestMethod]
        public async Task RunSyncJobNotFoundTest()
        {
            _syncJob = null;

            var orchestrator = new OrchestratorMultiLaneFunction(_telemetryClient, _mockGraphUpdaterService, _mailSenders, _gmmResources, _mockLoggingRepo, _mockDeltaCachingConfig);
            await orchestrator.RunOrchestratorAsync(_context.Object);

            Assert.IsFalse(_mockLoggingRepo.MessagesLogged.Any(x => x.Message == nameof(OrchestratorMultiLaneFunction) + " function completed"));
            Assert.IsTrue(_mockLoggingRepo.MessagesLogged.Any(x => x.Message.Contains("Caught unexpected exception, marking sync job as errored.")));
            Assert.IsTrue(_mockLoggingRepo.MessagesLogged.Any(x => x.Message.Contains("SyncJob is null")));
        }

        [TestMethod]
        public async Task RunOrchestratorMissingGroupTest()
        {
            _jobState.IsValidGroup = null;
            _mockLoggingRepo.SetSyncJobProperties(_syncJob.RunId.Value, _syncJob.ToDictionary());

            var orchestrator = new OrchestratorMultiLaneFunction(_telemetryClient, _mockGraphUpdaterService, _mailSenders, _gmmResources, _mockLoggingRepo, _mockDeltaCachingConfig);
            var response = await orchestrator.RunOrchestratorAsync(_context.Object);

            Assert.AreEqual(SyncStatus.DestinationGroupNotFound, _updateJobRequest.Status);
            Assert.IsTrue(response == OrchestrationRuntimeStatus.Completed);
            Assert.IsTrue(_mockLoggingRepo.MessagesLogged.Any(x => x.Message.Contains($"Group with ID {_groupMembership.Destination.ObjectId} doesn't exist.")));
            Assert.IsTrue(_mockLoggingRepo.MessagesLogged.Any(x => x.Message == nameof(OrchestratorMultiLaneFunction) + " function did not complete"));

            var logProperties = _mockLoggingRepo.SyncJobPropertiesHistory[_syncJob.RunId.Value].Properties;

            Assert.IsNotNull(_mockLoggingRepo.SyncJobProperties);
            Assert.AreEqual(logProperties["RunId"], _syncJob.RunId.ToString());
            Assert.AreEqual(logProperties["Id"], _syncJob.Id.ToString());
        }

        [TestMethod]
        public async Task RunOrchestratorGuestUserErrorTest()
        {
            _mockLoggingRepo.SetSyncJobProperties(_syncJob.RunId.Value, _syncJob.ToDictionary());
            _graphUpdaterStatus = GraphUpdaterStatus.GuestError;

            var graphUpdaterService = new Mock<IGraphUpdaterService>();
            graphUpdaterService.Setup(x => x.GetSyncJobAsync(It.IsAny<Guid>())).ReturnsAsync(() => _syncJob);

            _context.Setup(x => x.CallActivityAsync(nameof(JobStatusUpdaterFunction), It.IsAny<JobStatusUpdaterRequest>()))
                    .Callback<string, object>(async (name, request) =>
                    {
                        await CallJobStatusUpdaterFunctionAsync(_mockLoggingRepo, graphUpdaterService.Object, request as JobStatusUpdaterRequest);
                    });

            var orchestrator = new OrchestratorMultiLaneFunction(_telemetryClient, graphUpdaterService.Object, _mailSenders, _gmmResources, _mockLoggingRepo, _mockDeltaCachingConfig);
            var response = await orchestrator.RunOrchestratorAsync(_context.Object);

            Assert.IsTrue(response == OrchestrationRuntimeStatus.Completed);
            Assert.IsTrue(_mockLoggingRepo.MessagesLogged.Any(x => x.Message == nameof(OrchestratorMultiLaneFunction) + " function completed"));

            var logProperties = _mockLoggingRepo.SyncJobPropertiesHistory[_syncJob.RunId.Value].Properties;

            Assert.IsNotNull(_mockLoggingRepo.SyncJobProperties);
            Assert.AreEqual(logProperties["RunId"], _syncJob.RunId.ToString());
            Assert.AreEqual(logProperties["Id"], _syncJob.Id.ToString());

            graphUpdaterService.Verify(x => x.UpdateSyncJobStatusAsync(It.IsAny<SyncJob>(), SyncStatus.GuestUsersCannotBeAddedToUnifiedGroup, false, It.IsAny<Guid>()));
            _context.Verify(x => x.CallActivityAsync<GroupUpdaterResponse>(It.IsAny<string>(), It.IsAny<GroupUpdaterRequest>()), Times.Exactly(2));
        }

        [TestMethod]
        public async Task RunCacheUserUpdaterSubOrchestratorFunctionTest()
        {
            _mockLoggingRepo.SetSyncJobProperties(_syncJob.RunId.Value, _syncJob.ToDictionary());
            _mockGraphUpdaterService.Jobs.Add(_syncJob);
            _mockGraphUpdaterService.Groups.Add(_groupMembership.Destination.ObjectId, new Group { Id = _groupMembership.Destination.ObjectId.ToString() });

            var usersAlreadyExist = new List<AzureADUser>();
            var usersToRemove = new List<AzureADUser>
            {
                new AzureADUser {
                    ObjectId = Guid.NewGuid(),
                    MembershipAction = MembershipAction.Remove,
                    SourceGroups = new List<Guid> { Guid.NewGuid() }
                }
            };

            _groupMembership.SourceMembers.Add(usersToRemove.First());
            _context.Setup(x => x.CallActivityAsync<GroupUpdaterResponse>(nameof(GroupUpdaterFunction),
                                                                             It.IsAny<GroupUpdaterRequest>())).ReturnsAsync(() => new GroupUpdaterResponse
                                                                             {
                                                                                 SuccessCount = 1,
                                                                                 UsersNotFound = usersToRemove,
                                                                                 UsersAlreadyExist = usersAlreadyExist
                                                                             });

            var orchestrator = new OrchestratorMultiLaneFunction(_telemetryClient, _mockGraphUpdaterService, _mailSenders, _gmmResources, _mockLoggingRepo, _mockDeltaCachingConfig);
            await orchestrator.RunOrchestratorAsync(_context.Object);

            _context.Verify(x => x.CallSubOrchestratorAsync(nameof(CacheUserUpdaterSubOrchestratorFunction), It.IsAny<CacheUserUpdaterRequest>()), Times.Exactly(2));
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
            "  \"IsLastMessage\": true," +
            "  \"TotalMessageCount\": 1," +
            "  \"TotalMembersToAdd\": 1," +
            "  \"TotalMembersToRemove\": 0" +
            "}";
            var groupMembership = JsonSerializer.Deserialize<GroupMembership>(json);
            return groupMembership;
        }

        private async Task CallLogMessageFunctionAsync(LoggerRequest loggerRequest, MockLoggingRepository mockLoggingRepository)
        {
            var function = new LoggerFunction(mockLoggingRepository);
            await function.LogMessageAsync(loggerRequest);
        }

        private async Task CallJobStatusUpdaterFunctionAsync(
            MockLoggingRepository mockLoggingRepository,
            IGraphUpdaterService graphUpdaterService,
            JobStatusUpdaterRequest request)
        {
            var function = new JobStatusUpdaterFunction(mockLoggingRepository, graphUpdaterService);
            await function.UpdateJobStatusAsync(request);
        }
    }
}

