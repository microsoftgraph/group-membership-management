// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using DIConcreteTypes;
using GraphUpdater.Activity.JobTracker;
using Hosts.GraphUpdater;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Entities;
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
using System.Threading;
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

        Mock<TaskOrchestrationContext> _context;
        //Mock<TaskOrchestrationEntityFeature> _entitiesMock;

        SyncJob _syncJob;
        JobState _jobState;
        GroupMembership _groupMembership;
        EmailSenderRecipient _mailSenders;
        TelemetryClient _telemetryClient;
        JobStatusUpdaterRequest _updateJobRequest;
        MockDeltaCachingConfig _mockDeltaCachingConfig;
        MockGraphUpdaterService _mockGraphUpdaterService;
        OrchestratorMultiLaneRequest _orchestratorMultiLaneRequest;
        Mock<IServiceBusQueueRepository> _mockServiceBusQueueRepository;
        GroupUpdaterResponse _groupUpdaterFunctionResponse;
        RunLimiterSettings _runLimiterSettings;
        TestCapturingLogger _capturingLogger;

        int _membersAdded = 1;
        int _membersRemoved = 0;
        bool _isComplete = true;
        bool _completionClaimed = true;
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
                Group = new Models.Group
                {
                    SyncJobId = _groupMembership.SyncJobId,
                    GroupId = _groupMembership.Destination.ObjectId
                }
            };

            _groupMembership.SyncJob = _syncJob;

            _jobState = new JobState
            {
                IsValidGroup = true,
                TotalMembersAdded = 0,
                TotalMembersRemoved = 0,
            };

            _orchestratorMultiLaneRequest = new OrchestratorMultiLaneRequest
            {
                GroupMembership = _groupMembership,
                RunId = _groupMembership.RunId,
                SubscriptionName = "GraphUpdater_small_1",
                TopicName = "membershipupdaters",
                LaneSize = "Small"
            };

            _mailSenders = new EmailSenderRecipient("sender@domain.com", "fake_pass", "recipient@domain.com");

            _telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());

            _mockDeltaCachingConfig = new MockDeltaCachingConfig();
            _mockServiceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            _mockGraphUpdaterService = new MockGraphUpdaterService(_mockServiceBusQueueRepository.Object);

            _runLimiterSettings = new RunLimiterSettings
            {
                IsEnabled = true,
                LeaseTimeoutMinutes = 15,
                HeartbeatIntervalMinutes = 3
            };

            _context = new Mock<TaskOrchestrationContext>();
            _capturingLogger = new TestCapturingLogger();
            _context.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(_capturingLogger);
            var mockEntities = new Mock<TaskOrchestrationEntityFeature>();
            mockEntities.Setup(x => x.CallEntityAsync<JobState>(It.IsAny<EntityInstanceId>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(() => _jobState);
            mockEntities.Setup(x => x.CallEntityAsync<bool?>(It.IsAny<EntityInstanceId>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(() => _jobState.IsValidGroup);
            mockEntities.Setup(x => x.CallEntityAsync<bool>(It.IsAny<EntityInstanceId>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CallEntityOptions>()))
                .ReturnsAsync((EntityInstanceId id, string operationName, object operationInput, CallEntityOptions options) =>
                    _jobState.IsValidGroup ?? (operationInput is bool requested && requested));
            mockEntities.Setup(x => x.CallEntityAsync<bool>(It.IsAny<EntityInstanceId>(), It.Is<string>(op => op == nameof(JobTrackerEntity.TryMarkCompletionSent)), It.IsAny<object>(), It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(() => _completionClaimed);
            mockEntities.Setup(x => x.CallEntityAsync<JobTrackerUpdateResult>(It.IsAny<EntityInstanceId>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(() => new JobTrackerUpdateResult
                {
                    IsComplete = _isComplete,
                    MessagesProcessed = _jobState.MessagesProcessed,
                    TotalMessageCount = _groupMembership.TotalMessageCount,
                    State = _jobState
                });
            mockEntities.Setup(x => x.CallEntityAsync(It.IsAny<EntityInstanceId>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CallEntityOptions>()))
                .Returns(Task.CompletedTask);
            mockEntities.Setup(x => x.LockEntitiesAsync(It.IsAny<EntityInstanceId[]>()))
                .ReturnsAsync(new Mock<IAsyncDisposable>().Object);
            _context.Setup(x => x.Entities).Returns(mockEntities.Object);
            _context.Setup(x => x.GetInput<OrchestratorMultiLaneRequest>()).Returns(() => _orchestratorMultiLaneRequest);
            _context.Setup(x => x.CallActivityAsync<SyncJob>(It.IsAny<TaskName>(), It.IsAny<JobReaderRequest>(), It.IsAny<TaskOptions>())).ReturnsAsync(() => _syncJob);
            _context.Setup(x => x.CallActivityAsync<Guid>(It.Is<TaskName>(x => x.Name == nameof(GetGroupFunction)), It.IsAny<GetGroupRequest>(), It.IsAny<TaskOptions>())).ReturnsAsync(_syncJob.Group.GroupId);
            _context.Setup(x => x.CallActivityAsync<bool>(It.IsAny<TaskName>(), It.IsAny<GroupValidatorRequest>(), It.IsAny<TaskOptions>()))
                    .Returns(async () => await CheckIfGroupExistsAsync(_groupMembership, _mockGraphUpdaterService, _mailSenders));

            _context.Setup(x => x.CallActivityAsync<GroupUpdaterResponse>(It.IsAny<TaskName>(), It.IsAny<GroupUpdaterRequest>(), It.IsAny<TaskOptions>()))
                .Callback<TaskName, object, TaskOptions>((name, request, options) =>
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

            _context.Setup(x => x.CallActivityAsync(It.IsAny<TaskName>(), It.IsAny<JobStatusUpdaterRequest>(), It.IsAny<TaskOptions>()))
                    .Callback<TaskName, object, TaskOptions>((name, request, options) =>
                    {
                        _updateJobRequest = request as JobStatusUpdaterRequest;
                    });

                _context.Setup(x => x.CallActivityAsync(It.Is<TaskName>(n => n.Name == nameof(MessageSplitterCompletionSenderFunction)), It.IsAny<MessageSplitterCompletionSignal>(), It.IsAny<TaskOptions>()))
                    .Returns(Task.CompletedTask);

            _context.Setup(x => x.CallActivityAsync(It.Is<TaskName>(n => n.Name == nameof(MessageSplitterLeaseRenewSenderFunction)), It.IsAny<MessageSplitterLeaseRenewSignal>(), It.IsAny<TaskOptions>()))
                .Returns(Task.CompletedTask);

            // Park the heartbeat timer until the orchestrator cancels it (via heartbeatCts in its
            // finally). Returning a completed task here would make the large-lane while(true)
            // heartbeat loop iterate synchronously forever and hang any test that runs it.
            _context.Setup(x => x.CreateTimer(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .Returns((DateTime _, CancellationToken ct) => Task.Delay(Timeout.Infinite, ct));
        }

        [TestMethod]
        public async Task RunOrchestratorAsync_WithSmallLane_DoesNotSendLeaseRenew()
        {
            _orchestratorMultiLaneRequest.LaneSize = "Small";

            var orchestrator = new OrchestratorMultiLaneFunction(_telemetryClient, _mockGraphUpdaterService, _mailSenders, _gmmResources, _mockDeltaCachingConfig, _runLimiterSettings);
            await orchestrator.RunOrchestratorAsync(_context.Object);

            _context.Verify(
                x => x.CallActivityAsync(
                    It.Is<TaskName>(n => n.Name == nameof(MessageSplitterLeaseRenewSenderFunction)),
                    It.IsAny<MessageSplitterLeaseRenewSignal>(),
                    It.IsAny<TaskOptions>()),
                Times.Never);
        }

        [TestMethod]
        public async Task RunOrchestratorAsync_WithLargeLane_WhenRunLimiterDisabled_DoesNotSendLeaseRenew()
        {
            _orchestratorMultiLaneRequest.LaneSize = "Large";

            _mockGraphUpdaterService.Jobs.Add(_syncJob);
            _mockGraphUpdaterService.Groups.Add(_groupMembership.Destination.ObjectId, new Group { Id = _groupMembership.Destination.ObjectId.ToString() });

            var disabledRunLimiterSettings = new RunLimiterSettings
            {
                IsEnabled = false,
                HeartbeatIntervalMinutes = 0,
                LeaseTimeoutMinutes = 0
            };

            var orchestrator = new OrchestratorMultiLaneFunction(_telemetryClient, _mockGraphUpdaterService, _mailSenders, _gmmResources, _mockDeltaCachingConfig, disabledRunLimiterSettings);
            var response = await orchestrator.RunOrchestratorAsync(_context.Object);

            Assert.IsTrue(response == OrchestrationRuntimeStatus.Completed);

            _context.Verify(
                x => x.CallActivityAsync(
                    It.Is<TaskName>(n => n.Name == nameof(MessageSplitterLeaseRenewSenderFunction)),
                    It.IsAny<MessageSplitterLeaseRenewSignal>(),
                    It.IsAny<TaskOptions>()),
                Times.Never);

            _context.Verify(
                x => x.CallActivityAsync(
                    It.Is<TaskName>(n => n.Name == nameof(MessageSplitterCompletionSenderFunction)),
                    It.IsAny<MessageSplitterCompletionSignal>(),
                    It.IsAny<TaskOptions>()),
                Times.Never);
        }

        [TestMethod]
        public async Task TestMSALTransientExceptionAsync()
        {
            // triggers group validation
            _jobState.IsValidGroup = null;

            _context.Setup(x => x.CallActivityAsync<bool>(It.IsAny<TaskName>(), It.IsAny<GroupValidatorRequest>(), It.IsAny<TaskOptions>()))
                .ThrowsAsync(new MsalClientException("MULTIPLE_MATCHING_TOKENS_DETECTED", "MULTIPLE_MATCHING_TOKENS_DETECTED"));

            var orchestrator = new OrchestratorMultiLaneFunction(_telemetryClient, _mockGraphUpdaterService, _mailSenders, _gmmResources, _mockDeltaCachingConfig, _runLimiterSettings);
            await Assert.ThrowsExceptionAsync<MsalClientException>(async () => await orchestrator.RunOrchestratorAsync(_context.Object));

            _context.Verify(x => x.CallActivityAsync(It.Is<TaskName>(n => n.Name == nameof(MessageSplitterCompletionSenderFunction)), It.IsAny<MessageSplitterCompletionSignal>(), It.IsAny<TaskOptions>()), Times.Never);

            Assert.AreEqual(SyncStatus.TransientError, _updateJobRequest.Status);
        }

        [TestMethod]
        public async Task RunOrchestratorValidSyncTest()
        {
            _mockGraphUpdaterService.Jobs.Add(_syncJob);
            _mockGraphUpdaterService.Groups.Add(_groupMembership.Destination.ObjectId, new Group { Id = _groupMembership.Destination.ObjectId.ToString() });

            var orchestrator = new OrchestratorMultiLaneFunction(_telemetryClient, _mockGraphUpdaterService, _mailSenders, _gmmResources, _mockDeltaCachingConfig, _runLimiterSettings);
            var response = await orchestrator.RunOrchestratorAsync(_context.Object);

            Assert.IsTrue(response == OrchestrationRuntimeStatus.Completed);

            _context.Verify(x => x.CallActivityAsync(It.Is<TaskName>(n => n.Name == nameof(MessageSplitterCompletionSenderFunction)), It.IsAny<MessageSplitterCompletionSignal>(), It.IsAny<TaskOptions>()), Times.Once);

            _context.Verify(x => x.CallActivityAsync<GroupUpdaterResponse>(It.IsAny<TaskName>(), It.IsAny<GroupUpdaterRequest>(), It.IsAny<TaskOptions>()), Times.Exactly(2));

            Assert.AreEqual(SyncStatus.Idle, _updateJobRequest.Status);
        }

        [TestMethod]
        public async Task RunOrchestratorInitialSyncTest()
        {
            _groupMembership.SyncJob.LastRunTime = SqlDateTime.MinValue.Value;
            _jobState.TotalMembersToAdd = _membersAdded;

            var orchestrator = new OrchestratorMultiLaneFunction(_telemetryClient, _mockGraphUpdaterService, _mailSenders, _gmmResources, _mockDeltaCachingConfig, _runLimiterSettings);
            var response = await orchestrator.RunOrchestratorAsync(_context.Object);

            Assert.IsTrue(response == OrchestrationRuntimeStatus.Completed);

            _context.Verify(x => x.CallActivityAsync(It.Is<TaskName>(n => n.Name == nameof(EmailSenderFunction)), It.IsAny<EmailSenderRequest>(), It.IsAny<TaskOptions>()), Times.Once);
            _context.Verify(x => x.CallActivityAsync<GroupUpdaterResponse>(It.IsAny<TaskName>(), It.IsAny<GroupUpdaterRequest>(), It.IsAny<TaskOptions>()), Times.Exactly(2));
            _context.Verify(x => x.CallActivityAsync(It.Is<TaskName>(n => n.Name == nameof(JobStatusUpdaterFunction)), It.Is<JobStatusUpdaterRequest>(y => y.Status == SyncStatus.Idle), It.IsAny<TaskOptions>()), Times.Once);
            _context.Verify(x => x.CallActivityAsync(It.Is<TaskName>(n => n.Name == nameof(MessageSplitterCompletionSenderFunction)), It.IsAny<MessageSplitterCompletionSignal>(), It.IsAny<TaskOptions>()), Times.Once);
        }

        [TestMethod]
        public async Task RunOrchestratorGroupIdEmptyEmitsCompletionTest()
        {
            _context.Setup(x => x.CallActivityAsync<Guid>(It.Is<TaskName>(x => x.Name == nameof(GetGroupFunction)), It.IsAny<GetGroupRequest>(), It.IsAny<TaskOptions>()))
                    .ReturnsAsync(Guid.Empty);

            var orchestrator = new OrchestratorMultiLaneFunction(_telemetryClient, _mockGraphUpdaterService, _mailSenders, _gmmResources, _mockDeltaCachingConfig, _runLimiterSettings);
            var response = await orchestrator.RunOrchestratorAsync(_context.Object);

            Assert.AreEqual(OrchestrationRuntimeStatus.Failed, response);
            Assert.AreEqual(SyncStatus.Error, _updateJobRequest.Status);

            _context.Verify(x => x.CallActivityAsync(It.Is<TaskName>(n => n.Name == nameof(MessageSplitterCompletionSenderFunction)), It.IsAny<MessageSplitterCompletionSignal>(), It.IsAny<TaskOptions>()), Times.Once);
        }

        [TestMethod]
        public async Task RunOrchestratorWithJobOnErrorStatusTest()
        {
            _syncJob.Status = SyncStatus.Error.ToString();
            _groupMembership.TotalMessageCount = 10;

            _mockGraphUpdaterService.Jobs.Add(_syncJob);
            _mockGraphUpdaterService.Groups.Add(_groupMembership.Destination.ObjectId, new Group { Id = _groupMembership.Destination.ObjectId.ToString() });

            var orchestrator = new OrchestratorMultiLaneFunction(_telemetryClient, _mockGraphUpdaterService, _mailSenders, _gmmResources, _mockDeltaCachingConfig, _runLimiterSettings);
            var response = await orchestrator.RunOrchestratorAsync(_context.Object);

            Assert.IsTrue(response == OrchestrationRuntimeStatus.Completed);

            _context.Verify(x => x.CallActivityAsync(It.Is<TaskName>(n => n.Name == nameof(MessageSplitterCompletionSenderFunction)), It.IsAny<MessageSplitterCompletionSignal>(), It.IsAny<TaskOptions>()), Times.Once);

            _context.Verify(x => x.CallActivityAsync<GroupUpdaterResponse>(It.IsAny<TaskName>(), It.IsAny<GroupUpdaterRequest>(), It.IsAny<TaskOptions>()), Times.Never());
            _context.Verify(x => x.CallActivityAsync(It.Is<TaskName>(n => n.Name == nameof(MessageRemoverFunction)), It.IsAny<GroupUpdaterRequest>(), It.IsAny<TaskOptions>()), Times.Never());
        }

        [TestMethod]
        public async Task RunOrchestratorNotAllMessagesProcessedSetsErrorTest()
        {
            _mockGraphUpdaterService.Jobs.Add(_syncJob);
            _mockGraphUpdaterService.Groups.Add(_groupMembership.Destination.ObjectId, new Group { Id = _groupMembership.Destination.ObjectId.ToString() });

            // Terminal chunk of a multi-part run on the session-ordered LARGE lane, with the entity
            // reporting an ACTUAL shortfall (2 of 3 parts). On the ordered large lane every earlier
            // part must have registered before the last message, so this is a genuine lost part and
            // the job must be stamped Error (mirroring pre-fix behavior).
            _orchestratorMultiLaneRequest.LaneSize = "Large";
            _isComplete = false;
            _groupMembership.TotalMessageCount = 3;
            _jobState.MessagesProcessed = 2;

            var orchestrator = new OrchestratorMultiLaneFunction(_telemetryClient, _mockGraphUpdaterService, _mailSenders, _gmmResources, _mockDeltaCachingConfig, _runLimiterSettings);
            var response = await orchestrator.RunOrchestratorAsync(_context.Object);

            Assert.IsTrue(response == OrchestrationRuntimeStatus.Completed);

            // This message's Graph adds/removes still happen.
            _context.Verify(x => x.CallActivityAsync<GroupUpdaterResponse>(It.IsAny<TaskName>(), It.IsAny<GroupUpdaterRequest>(), It.IsAny<TaskOptions>()), Times.Exactly(2));

            // The genuine incompletion is stamped Error exactly once.
            _context.Verify(x => x.CallActivityAsync(It.Is<TaskName>(n => n.Name == nameof(JobStatusUpdaterFunction)), It.Is<JobStatusUpdaterRequest>(y => y.Status == SyncStatus.Error), It.IsAny<TaskOptions>()), Times.Once);
            Assert.AreEqual(SyncStatus.Error, _updateJobRequest.Status);

            // The run is finalized (as Error), so the MessageSplitter completion signal is emitted.
            _context.Verify(x => x.CallActivityAsync(It.Is<TaskName>(n => n.Name == nameof(MessageSplitterCompletionSenderFunction)), It.IsAny<MessageSplitterCompletionSignal>(), It.IsAny<TaskOptions>()), Times.Once);

            // The detection signal must survive the fix: a terminal message arriving with the run
            // still incomplete emits NotAllMessagesProcessed (EventId 20054) — the same signal that
            // originally surfaced this incident.
            Assert.IsTrue(_capturingLogger.LoggedEvents.Any(e => e.Id == 20054),
                "Terminal-but-incomplete run must still emit NotAllMessagesProcessed (EventId 20054) for detection.");
        }

        [TestMethod]
        public async Task RunOrchestratorDuplicateTerminalMessageOnLargeLaneDoesNotErrorTest()
        {
            _mockGraphUpdaterService.Jobs.Add(_syncJob);
            _mockGraphUpdaterService.Groups.Add(_groupMembership.Destination.ObjectId, new Group { Id = _groupMembership.Destination.ObjectId.ToString() });

            // Isolates the shortfall guard on the LARGE lane. A duplicate/replayed terminal message has
            // every part present (MessagesProcessed == TotalMessageCount) but returns IsComplete=false
            // because the completion claim was already taken. Since it is NOT a shortfall, it is an
            // already-finalized run being re-seen and must fall through to a no-op, not a false Error.
            _orchestratorMultiLaneRequest.LaneSize = "Large";
            _isComplete = false;
            _groupMembership.TotalMessageCount = 3;
            _jobState.MessagesProcessed = 3;

            var orchestrator = new OrchestratorMultiLaneFunction(_telemetryClient, _mockGraphUpdaterService, _mailSenders, _gmmResources, _mockDeltaCachingConfig, _runLimiterSettings);
            var response = await orchestrator.RunOrchestratorAsync(_context.Object);

            Assert.IsTrue(response == OrchestrationRuntimeStatus.Completed);

            // No false Error: a duplicate terminal message on a complete run must not stamp Error.
            _context.Verify(x => x.CallActivityAsync(It.Is<TaskName>(n => n.Name == nameof(JobStatusUpdaterFunction)), It.Is<JobStatusUpdaterRequest>(y => y.Status == SyncStatus.Error), It.IsAny<TaskOptions>()), Times.Never);

            // No detection signal: this is not an incompletion.
            Assert.IsFalse(_capturingLogger.LoggedEvents.Any(e => e.Id == 20054),
                "A duplicate terminal message on a complete run must not emit NotAllMessagesProcessed (EventId 20054).");

            // The run is not re-finalized, so no completion signal is re-emitted from this duplicate.
            _context.Verify(x => x.CallActivityAsync(It.Is<TaskName>(n => n.Name == nameof(MessageSplitterCompletionSenderFunction)), It.IsAny<MessageSplitterCompletionSignal>(), It.IsAny<TaskOptions>()), Times.Never);
        }

        [TestMethod]
        public async Task RunOrchestratorSmallLaneTerminalIncompleteDoesNotErrorTest()
        {
            _mockGraphUpdaterService.Jobs.Add(_syncJob);
            _mockGraphUpdaterService.Groups.Add(_groupMembership.Destination.ObjectId, new Group { Id = _groupMembership.Destination.ObjectId.ToString() });

            // Isolates the lane gate. The SMALL lane is non-session/unordered, so a terminal message can
            // arrive before earlier parts — a shortfall at this instant (2 of 3) does NOT imply a lost
            // part (a later message completes the run). The Error inference must never fire here.
            _orchestratorMultiLaneRequest.LaneSize = "Small";
            _isComplete = false;
            _groupMembership.TotalMessageCount = 3;
            _jobState.MessagesProcessed = 2;

            var orchestrator = new OrchestratorMultiLaneFunction(_telemetryClient, _mockGraphUpdaterService, _mailSenders, _gmmResources, _mockDeltaCachingConfig, _runLimiterSettings);
            var response = await orchestrator.RunOrchestratorAsync(_context.Object);

            Assert.IsTrue(response == OrchestrationRuntimeStatus.Completed);

            // No Error: the unordered small lane must not infer incompletion from a terminal message.
            _context.Verify(x => x.CallActivityAsync(It.Is<TaskName>(n => n.Name == nameof(JobStatusUpdaterFunction)), It.Is<JobStatusUpdaterRequest>(y => y.Status == SyncStatus.Error), It.IsAny<TaskOptions>()), Times.Never);

            Assert.IsFalse(_capturingLogger.LoggedEvents.Any(e => e.Id == 20054),
                "A small-lane terminal message must not emit NotAllMessagesProcessed (EventId 20054).");
        }

        [TestMethod]
        public async Task RunOrchestratorAsync_WhenCompletionAlreadyClaimed_DoesNotResendCompletionTest()
        {
            _mockGraphUpdaterService.Jobs.Add(_syncJob);
            _mockGraphUpdaterService.Groups.Add(_groupMembership.Destination.ObjectId, new Group { Id = _groupMembership.Destination.ObjectId.ToString() });

            // The run finalizes (Idle) and attempts to emit the completion signal, but another
            // finalizer already claimed the send: the atomic TryMarkCompletionSent returns false, so
            // this orchestration must NOT re-send the MessageSplitter completion signal. This is the
            // exactly-once guarantee that replaced the old check-then-send-then-mark race.
            _isComplete = true;
            _completionClaimed = false;

            var orchestrator = new OrchestratorMultiLaneFunction(_telemetryClient, _mockGraphUpdaterService, _mailSenders, _gmmResources, _mockDeltaCachingConfig, _runLimiterSettings);
            var response = await orchestrator.RunOrchestratorAsync(_context.Object);

            Assert.IsTrue(response == OrchestrationRuntimeStatus.Completed);

            // The job still finalizes as Idle (the completion-send claim is separate from the status update).
            _context.Verify(x => x.CallActivityAsync(It.Is<TaskName>(n => n.Name == nameof(JobStatusUpdaterFunction)), It.Is<JobStatusUpdaterRequest>(y => y.Status == SyncStatus.Idle), It.IsAny<TaskOptions>()), Times.Once);

            // But the completion signal is NOT re-sent, because another finalizer already claimed it.
            _context.Verify(x => x.CallActivityAsync(It.Is<TaskName>(n => n.Name == nameof(MessageSplitterCompletionSenderFunction)), It.IsAny<MessageSplitterCompletionSignal>(), It.IsAny<TaskOptions>()), Times.Never);
        }

        [TestMethod]
        public async Task RunOrchestratorCompletingMessageFinalizesAsIdleTest()        {
            _mockGraphUpdaterService.Jobs.Add(_syncJob);
            _mockGraphUpdaterService.Groups.Add(_groupMembership.Destination.ObjectId, new Group { Id = _groupMembership.Destination.ObjectId.ToString() });

            // The atomic entity op reports the run is complete (single-writer claim). The job must
            // finalize exactly once as Idle and emit the completion signal.
            _isComplete = true;

            var orchestrator = new OrchestratorMultiLaneFunction(_telemetryClient, _mockGraphUpdaterService, _mailSenders, _gmmResources, _mockDeltaCachingConfig, _runLimiterSettings);
            var response = await orchestrator.RunOrchestratorAsync(_context.Object);

            Assert.IsTrue(response == OrchestrationRuntimeStatus.Completed);

            _context.Verify(x => x.CallActivityAsync(It.Is<TaskName>(n => n.Name == nameof(JobStatusUpdaterFunction)), It.Is<JobStatusUpdaterRequest>(y => y.Status == SyncStatus.Idle), It.IsAny<TaskOptions>()), Times.Once);
            _context.Verify(x => x.CallActivityAsync(It.Is<TaskName>(n => n.Name == nameof(MessageSplitterCompletionSenderFunction)), It.IsAny<MessageSplitterCompletionSignal>(), It.IsAny<TaskOptions>()), Times.Once);

            Assert.AreEqual(SyncStatus.Idle, _updateJobRequest.Status);

            // The completing path must NOT emit the incomplete-run signal.
            Assert.IsFalse(_capturingLogger.LoggedEvents.Any(e => e.Id == 20054),
                "The completing message must not emit NotAllMessagesProcessed (EventId 20054).");
        }

        [TestMethod]
        public async Task TestHttpTransientExceptionAsync()
        {
            _context.Setup(x => x.CallActivityAsync<GroupUpdaterResponse>(It.IsAny<TaskName>(), It.IsAny<GroupUpdaterRequest>(), It.IsAny<TaskOptions>()))
                    .Throws<HttpRequestException>();

                var orchestrator = new OrchestratorMultiLaneFunction(_telemetryClient, _mockGraphUpdaterService, _mailSenders, _gmmResources, _mockDeltaCachingConfig, _runLimiterSettings);
            await Assert.ThrowsExceptionAsync<HttpRequestException>(async () => await orchestrator.RunOrchestratorAsync(_context.Object));

            _context.Verify(x => x.CallActivityAsync(It.Is<TaskName>(n => n.Name == nameof(MessageSplitterCompletionSenderFunction)), It.IsAny<MessageSplitterCompletionSignal>(), It.IsAny<TaskOptions>()), Times.Never);

            Assert.AreEqual(SyncStatus.TransientError, _updateJobRequest.Status);
        }

        [TestMethod]
        public async Task RunOrchestratorExceptionTest()
        {
            _context.Setup(x => x.CallActivityAsync<GroupUpdaterResponse>(It.IsAny<TaskName>(), It.IsAny<GroupUpdaterRequest>(), It.IsAny<TaskOptions>()))
                    .Throws<Exception>();

                var orchestrator = new OrchestratorMultiLaneFunction(_telemetryClient, _mockGraphUpdaterService, _mailSenders, _gmmResources, _mockDeltaCachingConfig, _runLimiterSettings);
            await Assert.ThrowsExceptionAsync<Exception>(async () => await orchestrator.RunOrchestratorAsync(_context.Object));

            _context.Verify(x => x.CallActivityAsync(It.Is<TaskName>(n => n.Name == nameof(MessageSplitterCompletionSenderFunction)), It.IsAny<MessageSplitterCompletionSignal>(), It.IsAny<TaskOptions>()), Times.Once);
        }

        [TestMethod]
        public async Task RunSyncJobNotFoundTest()
        {
            _syncJob = null;

            var orchestrator = new OrchestratorMultiLaneFunction(_telemetryClient, _mockGraphUpdaterService, _mailSenders, _gmmResources, _mockDeltaCachingConfig, _runLimiterSettings);
            await orchestrator.RunOrchestratorAsync(_context.Object);

            _context.Verify(x => x.CallActivityAsync(It.Is<TaskName>(n => n.Name == nameof(MessageSplitterCompletionSenderFunction)), It.IsAny<MessageSplitterCompletionSignal>(), It.IsAny<TaskOptions>()), Times.Once);
        }

        [TestMethod]
        public async Task RunOrchestratorMissingGroupTest()
        {
            _jobState.IsValidGroup = null;

            var orchestrator = new OrchestratorMultiLaneFunction(_telemetryClient, _mockGraphUpdaterService, _mailSenders, _gmmResources, _mockDeltaCachingConfig, _runLimiterSettings);
            var response = await orchestrator.RunOrchestratorAsync(_context.Object);

            _context.Verify(x => x.CallActivityAsync(It.Is<TaskName>(n => n.Name == nameof(MessageSplitterCompletionSenderFunction)), It.IsAny<MessageSplitterCompletionSignal>(), It.IsAny<TaskOptions>()), Times.Once);

            Assert.AreEqual(SyncStatus.DestinationGroupNotFound, _updateJobRequest.Status);
            Assert.IsTrue(response == OrchestrationRuntimeStatus.Completed);
        }

        [TestMethod]
        public async Task RunOrchestratorGuestUserErrorTest()
        {
            _graphUpdaterStatus = GraphUpdaterStatus.GuestError;

            var graphUpdaterService = new Mock<IGraphUpdaterService>();
            graphUpdaterService.Setup(x => x.GetSyncJobAsync(It.IsAny<Guid>())).ReturnsAsync(() => _syncJob);

            _context.Setup(x => x.CallActivityAsync(It.Is<TaskName>(n => n.Name == nameof(JobStatusUpdaterFunction)), It.IsAny<JobStatusUpdaterRequest>(), It.IsAny<TaskOptions>()))
                    .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                    {
                        await CallJobStatusUpdaterFunctionAsync(graphUpdaterService.Object, request as JobStatusUpdaterRequest);
                    });

            var orchestrator = new OrchestratorMultiLaneFunction(_telemetryClient, graphUpdaterService.Object, _mailSenders, _gmmResources, _mockDeltaCachingConfig, _runLimiterSettings);
            var response = await orchestrator.RunOrchestratorAsync(_context.Object);

            _context.Verify(x => x.CallActivityAsync(It.Is<TaskName>(n => n.Name == nameof(MessageSplitterCompletionSenderFunction)), It.IsAny<MessageSplitterCompletionSignal>(), It.IsAny<TaskOptions>()), Times.Once);

            Assert.IsTrue(response == OrchestrationRuntimeStatus.Completed);

            graphUpdaterService.Verify(x => x.UpdateSyncJobStatusAsync(It.IsAny<SyncJob>(), SyncStatus.GuestUsersCannotBeAddedToUnifiedGroup, false, It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<int?>()));
            _context.Verify(x => x.CallActivityAsync<GroupUpdaterResponse>(It.IsAny<TaskName>(), It.IsAny<GroupUpdaterRequest>(), It.IsAny<TaskOptions>()), Times.Exactly(2));
        }

        [TestMethod]
        public async Task RunCacheUserUpdaterSubOrchestratorFunctionTest()
        {
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
            _context.Setup(x => x.CallActivityAsync<GroupUpdaterResponse>(It.Is<TaskName>(n => n.Name == nameof(GroupUpdaterFunction)),
                                                                             It.IsAny<GroupUpdaterRequest>(), It.IsAny<TaskOptions>())).ReturnsAsync(() => new GroupUpdaterResponse
                                                                             {
                                                                                 SuccessCount = 1,
                                                                                 UsersNotFound = usersToRemove,
                                                                                 UsersAlreadyExist = usersAlreadyExist
                                                                             });

            var orchestrator = new OrchestratorMultiLaneFunction(_telemetryClient, _mockGraphUpdaterService, _mailSenders, _gmmResources, _mockDeltaCachingConfig, _runLimiterSettings);
            await orchestrator.RunOrchestratorAsync(_context.Object);

            _context.Verify(x => x.CallSubOrchestratorAsync(It.Is<TaskName>(n => n.Name == nameof(CacheUserUpdaterSubOrchestratorFunction)), It.IsAny<CacheUserUpdaterRequest>(), It.IsAny<TaskOptions>()), Times.Exactly(2));
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

        private async Task CallJobStatusUpdaterFunctionAsync(
            IGraphUpdaterService graphUpdaterService,
            JobStatusUpdaterRequest request)
        {
            var function = new JobStatusUpdaterFunction(NullLogger<JobStatusUpdaterFunction>.Instance, graphUpdaterService);
            await function.UpdateJobStatusAsync(request);
        }

        /// <summary>
        /// Minimal ILogger that records the EventIds it is asked to log, so tests can assert a specific
        /// signal (e.g. NotAllMessagesProcessed / EventId 20054) is or is not emitted. IsEnabled returns
        /// true so source-generated LoggerMessage calls are not short-circuited before Log runs.
        /// </summary>
        private sealed class TestCapturingLogger : ILogger
        {
            public List<EventId> LoggedEvents { get; } = new List<EventId>();

            public IDisposable BeginScope<TState>(TState state) => NullScope.Instance;

            public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

            public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
                => LoggedEvents.Add(eventId);

            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new NullScope();
                public void Dispose() { }
            }
        }
    }
}