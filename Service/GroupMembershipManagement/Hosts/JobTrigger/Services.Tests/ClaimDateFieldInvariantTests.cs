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
using Models.Helpers;
using Models.Notifications;
using Moq;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using Services.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Services.Tests
{
    /// <summary>
    /// Tests that validate date field invariants after atomic claim.
    /// Each test creates a distinct input job and a distinct claimed job to ensure
    /// downstream operations receive the DB-refreshed values, not stale input values.
    /// </summary>
    [TestClass]
    public class ClaimDateFieldInvariantTests
    {
        private Mock<IJobTriggerService> _jobTriggerService;
        private Mock<TaskOrchestrationContext> _context;
        private Mock<IEmailSenderRecipient> _emailSenderAndRecipients;
        private Mock<IGMMResources> _gmmResources;
        private TelemetryClient _telemetryClient;
        private JsonSchemaProvider _jsonSchemaProvider;

        // Capture what downstream activities actually receive
        private SyncJob _capturedJobUpdaterJob;
        private SyncStatus? _capturedJobUpdaterStatus;
        private EmailSenderRequest _capturedEmailRequest;
        private SyncJob _capturedTopicMessageJob;
        private int _jobUpdaterCallCount;
        private int _emailSenderCallCount;

        private static readonly DateTime ClaimTime = new DateTime(2026, 3, 30, 12, 0, 0, DateTimeKind.Utc);

        [TestInitialize]
        public void Setup()
        {
            _jobTriggerService = new Mock<IJobTriggerService>();
            _context = new Mock<TaskOrchestrationContext>();
            _emailSenderAndRecipients = new Mock<IEmailSenderRecipient>();
            _gmmResources = new Mock<IGMMResources>();
            _telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            _jsonSchemaProvider = SchemaProviderFactory.CreateJsonSchemaProvider();

            _capturedJobUpdaterJob = null;
            _capturedJobUpdaterStatus = null;
            _capturedEmailRequest = null;
            _capturedTopicMessageJob = null;
            _jobUpdaterCallCount = 0;
            _emailSenderCallCount = 0;

            _context.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(NullLogger.Instance);
            _context.Setup(x => x.CurrentUtcDateTime).Returns(ClaimTime);

            _jobTriggerService.Setup(x => x.DestinationExistsAndGMMCanWriteToItAsync(It.IsAny<SyncJob>()))
                .ReturnsAsync(DestinationVerifierResult.Success);
            _jobTriggerService.Setup(x => x.GetDestinationNameAsync(It.IsAny<SyncJob>()))
                .ReturnsAsync("Test Group");
            _jobTriggerService.Setup(x => x.GetGroupEndpointsAsync(It.IsAny<SyncJob>()))
                .ReturnsAsync(new List<string> { "Yammer", "Teams" });

            var destinationObject = new DestinationObject
            {
                Type = "GroupMembership",
                Value = new GroupDestinationValue { ObjectId = Guid.NewGuid() }
            };
            var options = new JsonSerializerOptions { Converters = { new DestinationValueConverter() } };
            var serializedDestination = JsonSerializer.Serialize(destinationObject, options);

            _jobTriggerService.Setup(x => x.ParseAndValidateDestinationAsync(It.IsAny<SyncJob>()))
                .ReturnsAsync(new ParsedAndValidateDestinationResponse { IsValid = true, DestinationObject = serializedDestination });

            var group = new Group { GroupId = Guid.NewGuid(), SyncJobId = Guid.NewGuid() };

            _context.Setup(x => x.CallActivityAsync<int>(It.IsAny<TaskName>(), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()))
                .ReturnsAsync(0);

            _context.Setup(x => x.CallActivityAsync<ParsedAndValidateDestinationResponse>(
                    It.Is<TaskName>(t => t == nameof(ParseAndValidateDestinationFunction)), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()))
                .ReturnsAsync(new ParsedAndValidateDestinationResponse { IsValid = true, DestinationObject = serializedDestination });

            _context.Setup(x => x.CallActivityAsync<Group>(
                    It.Is<TaskName>(t => t == nameof(GetGroupFunction)), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()))
                .ReturnsAsync(group);

            _context.Setup(x => x.CallActivityAsync<DestinationVerifierResult>(
                    It.Is<TaskName>(t => t == nameof(DestinationVerifierFunction)), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()))
                .ReturnsAsync(DestinationVerifierResult.Success);

            _context.Setup(x => x.CallActivityAsync<string>(
                    It.Is<TaskName>(t => t == nameof(DestinationNameReaderFunction)), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()))
                .ReturnsAsync("Test Group");

            _context.Setup(x => x.CallActivityAsync<bool>(nameof(SchemaValidatorFunction), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()))
                .ReturnsAsync(true);

            _context.Setup(x => x.CallActivityAsync(
                    It.Is<TaskName>(t => t == nameof(TelemetryTrackerFunction)), It.IsAny<TelemetryTrackerRequest>(), It.IsAny<TaskOptions>()))
                .Returns(Task.CompletedTask);

            _context.Setup(x => x.CallActivityAsync(
                    It.Is<TaskName>(t => t == nameof(DestinationUpdaterFunction)), It.IsAny<object>(), It.IsAny<TaskOptions>()))
                .Returns(Task.CompletedTask);

            // Capture JobUpdaterFunction calls — this is the critical assertion point
            _context.Setup(x => x.CallActivityAsync(
                    It.Is<TaskName>(t => t == nameof(JobUpdaterFunction)), It.IsAny<JobUpdaterRequest>(), It.IsAny<TaskOptions>()))
                .Callback<TaskName, object, TaskOptions>((name, request, opts) =>
                {
                    var req = request as JobUpdaterRequest;
                    _capturedJobUpdaterJob = req.SyncJob;
                    _capturedJobUpdaterStatus = req.Status;
                    _jobUpdaterCallCount++;
                })
                .Returns(Task.CompletedTask);

            // Capture EmailSenderFunction calls
            _context.Setup(x => x.CallActivityAsync(
                    It.Is<TaskName>(t => t == nameof(EmailSenderFunction)), It.IsAny<EmailSenderRequest>(), It.IsAny<TaskOptions>()))
                .Callback<TaskName, object, TaskOptions>((name, request, opts) =>
                {
                    _capturedEmailRequest = request as EmailSenderRequest;
                    _emailSenderCallCount++;
                })
                .Returns(Task.CompletedTask);

            // Capture TopicMessageSenderFunction calls
            _context.Setup(x => x.CallActivityAsync(
                    It.Is<TaskName>(t => t == nameof(TopicMessageSenderFunction)), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()))
                .Callback<TaskName, object, TaskOptions>((name, request, opts) =>
                {
                    _capturedTopicMessageJob = request as SyncJob;
                })
                .Returns(Task.CompletedTask);
        }

        /// <summary>
        /// Creates an input SyncJob with the given pre-claim state and a separate claimed SyncJob
        /// with the expected post-claim DB values. Returns both as distinct object references.
        /// </summary>
        private (SyncJob inputJob, SyncJob claimedJob) CreateJobPair(
            SyncStatus inputStatus,
            DateTime inputLastSuccessfulStartTime,
            DateTime inputLastRunTime,
            SyncStatus claimedStatus,
            DateTime claimedLastSuccessfulStartTime,
            DateTime claimedLastRunTime)
        {
            var jobId = Guid.NewGuid();
            var runId = Guid.NewGuid();
            var query = "[{\"type\":\"GroupMembership\",\"source\": \"" + Guid.NewGuid() + "\"}]";

            var inputJob = new SyncJob
            {
                Id = jobId,
                RunId = runId,
                Period = 24,
                Status = inputStatus.ToString(),
                LastSuccessfulStartTime = inputLastSuccessfulStartTime,
                LastRunTime = inputLastRunTime,
                Query = query,
                Requestor = "owner@email.com",
                StartDate = DateTime.UtcNow.AddDays(-30),
                ScheduledDate = DateTime.UtcNow.AddDays(-1),
                MembershipType = MembershipTypes.GroupMembership.ToString()
            };

            // The claimed job is a SEPARATE object simulating what the DB returns after the atomic claim
            var claimedJob = new SyncJob
            {
                Id = jobId,
                RunId = runId,
                Period = 24,
                Status = claimedStatus.ToString(),
                LastSuccessfulStartTime = claimedLastSuccessfulStartTime,
                LastRunTime = claimedLastRunTime,
                Query = query,
                Requestor = "owner@email.com",
                StartDate = DateTime.UtcNow.AddDays(-30),
                ScheduledDate = DateTime.UtcNow.AddDays(-1),
                MembershipType = MembershipTypes.GroupMembership.ToString()
            };

            return (inputJob, claimedJob);
        }

        private void SetupClaimReturns(SyncJob inputJob, SyncJob claimedJob)
        {
            _context.Setup(x => x.GetInput<SyncJob>()).Returns(inputJob);

            _context.Setup(x => x.CallActivityAsync<SyncJob?>(
                    It.Is<TaskName>(t => t == nameof(ClaimJobFunction)),
                    It.IsAny<ClaimJobRequest>(),
                    It.IsAny<TaskOptions>()))
                .ReturnsAsync(claimedJob);

            // GetSyncJobFunction re-reads from DB before TopicMessageSender — return the claimed version
            _context.Setup(x => x.CallActivityAsync<SyncJob>(
                    It.Is<TaskName>(t => t == nameof(GetSyncJobFunction)),
                    It.IsAny<SyncJob>(),
                    It.IsAny<TaskOptions>()))
                .ReturnsAsync(claimedJob);
        }

        private async Task RunSubOrchestrator()
        {
            var suborchestrator = new SubOrchestratorFunction(
                _telemetryClient, _emailSenderAndRecipients.Object, _gmmResources.Object);
            await suborchestrator.RunSubOrchestratorAsync(_context.Object);
        }

        #region Scenario 1: Brand new job, never run

        [TestMethod]
        public async Task Scenario1_BrandNewJob_ClaimSetsLSST_PreservesMinValueLRT()
        {
            var (inputJob, claimedJob) = CreateJobPair(
                inputStatus: SyncStatus.Idle,
                inputLastSuccessfulStartTime: SqlDateTime.MinValue.Value,
                inputLastRunTime: SqlDateTime.MinValue.Value,
                claimedStatus: SyncStatus.InProgress,
                claimedLastSuccessfulStartTime: ClaimTime,
                claimedLastRunTime: SqlDateTime.MinValue.Value);

            SetupClaimReturns(inputJob, claimedJob);
            await RunSubOrchestrator();

            // TopicMessageSender must receive the claimed values, not the stale input
            Assert.IsNotNull(_capturedTopicMessageJob, "TopicMessageSender should be called on success path");
            Assert.AreEqual(SyncStatus.InProgress.ToString(), _capturedTopicMessageJob.Status,
                "TopicMessageSender must receive InProgress status from claimed job, not Idle from input");
            Assert.AreEqual(ClaimTime, _capturedTopicMessageJob.LastSuccessfulStartTime,
                "TopicMessageSender must receive refreshed LSST from claim, not MinValue from input");
            Assert.AreEqual(SqlDateTime.MinValue.Value, _capturedTopicMessageJob.LastRunTime,
                "LRT should remain MinValue for Idle->InProgress claim");
        }

        [TestMethod]
        public async Task Scenario1_BrandNewJob_SyncStartedEmailIsSent()
        {
            var (inputJob, claimedJob) = CreateJobPair(
                inputStatus: SyncStatus.Idle,
                inputLastSuccessfulStartTime: SqlDateTime.MinValue.Value,
                inputLastRunTime: SqlDateTime.MinValue.Value,
                claimedStatus: SyncStatus.InProgress,
                claimedLastSuccessfulStartTime: ClaimTime,
                claimedLastRunTime: SqlDateTime.MinValue.Value);

            SetupClaimReturns(inputJob, claimedJob);
            await RunSubOrchestrator();

            Assert.AreEqual(1, _emailSenderCallCount,
                "SyncStartedNotification should be sent for brand new job (LRT == MinValue)");
            Assert.AreEqual(NotificationMessageType.SyncStartedNotification, _capturedEmailRequest.NotificationType);
        }

        #endregion

        #region Scenario 2: Normal recurring job

        [TestMethod]
        public async Task Scenario2_RecurringJob_ClaimRefreshesLSST_PreservesLRT()
        {
            var twoDaysAgo = DateTime.UtcNow.AddDays(-2);
            var (inputJob, claimedJob) = CreateJobPair(
                inputStatus: SyncStatus.Idle,
                inputLastSuccessfulStartTime: twoDaysAgo,
                inputLastRunTime: twoDaysAgo,
                claimedStatus: SyncStatus.InProgress,
                claimedLastSuccessfulStartTime: ClaimTime,
                claimedLastRunTime: twoDaysAgo);

            SetupClaimReturns(inputJob, claimedJob);
            await RunSubOrchestrator();

            Assert.IsNotNull(_capturedTopicMessageJob, "TopicMessageSender should be called on success path");
            Assert.AreEqual(ClaimTime, _capturedTopicMessageJob.LastSuccessfulStartTime,
                "LSST must be refreshed to claim time");
            Assert.AreEqual(twoDaysAgo, _capturedTopicMessageJob.LastRunTime,
                "LRT should be preserved (not updated) for Idle claim");
        }

        [TestMethod]
        public async Task Scenario2_RecurringJob_NoSyncStartedEmail()
        {
            var twoDaysAgo = DateTime.UtcNow.AddDays(-2);
            var (inputJob, claimedJob) = CreateJobPair(
                inputStatus: SyncStatus.Idle,
                inputLastSuccessfulStartTime: twoDaysAgo,
                inputLastRunTime: twoDaysAgo,
                claimedStatus: SyncStatus.InProgress,
                claimedLastSuccessfulStartTime: ClaimTime,
                claimedLastRunTime: twoDaysAgo);

            SetupClaimReturns(inputJob, claimedJob);
            await RunSubOrchestrator();

            Assert.AreEqual(0, _emailSenderCallCount,
                "SyncStartedNotification should NOT be sent for recurring job (LRT != MinValue)");
        }

        #endregion

        #region Scenario 3: Error for a week, owner fixed it

        [TestMethod]
        public async Task Scenario3_FixedAfterWeekLongError_ClaimRefreshesLSST()
        {
            var sevenDaysAgo = DateTime.UtcNow.AddDays(-7);
            var (inputJob, claimedJob) = CreateJobPair(
                inputStatus: SyncStatus.Idle,
                inputLastSuccessfulStartTime: sevenDaysAgo,
                inputLastRunTime: sevenDaysAgo,
                claimedStatus: SyncStatus.InProgress,
                claimedLastSuccessfulStartTime: ClaimTime,
                claimedLastRunTime: sevenDaysAgo);

            SetupClaimReturns(inputJob, claimedJob);
            await RunSubOrchestrator();

            Assert.IsNotNull(_capturedTopicMessageJob, "TopicMessageSender should be called on success path");
            Assert.AreEqual(ClaimTime, _capturedTopicMessageJob.LastSuccessfulStartTime,
                "LSST must be refreshed even though job was in Error for a week");
            Assert.AreEqual(SyncStatus.InProgress.ToString(), _capturedTopicMessageJob.Status);
        }

        #endregion

        #region Scenario 4: Error, never completed a full run (LRT still MinValue)

        [TestMethod]
        public async Task Scenario4_NeverCompletedRun_SyncStartedEmailIsSent()
        {
            var threeDaysAgo = DateTime.UtcNow.AddDays(-3);
            var (inputJob, claimedJob) = CreateJobPair(
                inputStatus: SyncStatus.Idle,
                inputLastSuccessfulStartTime: threeDaysAgo,
                inputLastRunTime: SqlDateTime.MinValue.Value,
                claimedStatus: SyncStatus.InProgress,
                claimedLastSuccessfulStartTime: ClaimTime,
                claimedLastRunTime: SqlDateTime.MinValue.Value);

            SetupClaimReturns(inputJob, claimedJob);
            await RunSubOrchestrator();

            Assert.AreEqual(1, _emailSenderCallCount,
                "Email should be sent because LRT == MinValue means the job never completed a full run");
            Assert.AreEqual(NotificationMessageType.SyncStartedNotification, _capturedEmailRequest.NotificationType);
        }

        #endregion

        #region Scenario 5: TransientError, auto-retry

        [TestMethod]
        public async Task Scenario5_TransientError_ClaimsSuccessfully_PreservesLRT()
        {
            var oneDayAgo = DateTime.UtcNow.AddDays(-1);
            var (inputJob, claimedJob) = CreateJobPair(
                inputStatus: SyncStatus.TransientError,
                inputLastSuccessfulStartTime: oneDayAgo,
                inputLastRunTime: oneDayAgo,
                claimedStatus: SyncStatus.InProgress,
                claimedLastSuccessfulStartTime: ClaimTime,
                claimedLastRunTime: oneDayAgo);

            SetupClaimReturns(inputJob, claimedJob);
            await RunSubOrchestrator();

            Assert.IsNotNull(_capturedTopicMessageJob, "TopicMessageSender should be called on success path");
            Assert.AreEqual(SyncStatus.InProgress.ToString(), _capturedTopicMessageJob.Status);
            Assert.AreEqual(ClaimTime, _capturedTopicMessageJob.LastSuccessfulStartTime);
            Assert.AreEqual(oneDayAgo, _capturedTopicMessageJob.LastRunTime,
                "LRT should be preserved for TransientError->InProgress claim");
            Assert.AreEqual(0, _emailSenderCallCount,
                "No email for TransientError retry (LRT != MinValue)");
        }

        #endregion

        #region Scenario 6: Stuck 48h, needs reclaim

        [TestMethod]
        public async Task Scenario6_Stuck48h_ClaimUpdatesLRT()
        {
            var fortyEightHoursAgo = DateTime.UtcNow.AddHours(-48);
            var fiveDaysAgo = DateTime.UtcNow.AddDays(-5);
            var (inputJob, claimedJob) = CreateJobPair(
                inputStatus: SyncStatus.InProgress,
                inputLastSuccessfulStartTime: fortyEightHoursAgo,
                inputLastRunTime: fiveDaysAgo,
                claimedStatus: SyncStatus.StuckInProgress,
                claimedLastSuccessfulStartTime: ClaimTime,
                claimedLastRunTime: ClaimTime);

            SetupClaimReturns(inputJob, claimedJob);
            await RunSubOrchestrator();

            Assert.IsNotNull(_capturedTopicMessageJob, "TopicMessageSender should be called on success path");
            Assert.AreEqual(SyncStatus.StuckInProgress.ToString(), _capturedTopicMessageJob.Status,
                "Status must be StuckInProgress from claim, not InProgress from input");
            Assert.AreEqual(ClaimTime, _capturedTopicMessageJob.LastSuccessfulStartTime,
                "LSST must be refreshed");
            Assert.AreEqual(ClaimTime, _capturedTopicMessageJob.LastRunTime,
                "LRT must be updated to ClaimTime for StuckInProgress reclaim");
        }

        [TestMethod]
        public async Task Scenario6_Stuck48h_NoSyncStartedEmail()
        {
            var fortyEightHoursAgo = DateTime.UtcNow.AddHours(-48);
            var fiveDaysAgo = DateTime.UtcNow.AddDays(-5);
            var (inputJob, claimedJob) = CreateJobPair(
                inputStatus: SyncStatus.InProgress,
                inputLastSuccessfulStartTime: fortyEightHoursAgo,
                inputLastRunTime: fiveDaysAgo,
                claimedStatus: SyncStatus.StuckInProgress,
                claimedLastSuccessfulStartTime: ClaimTime,
                claimedLastRunTime: ClaimTime);

            SetupClaimReturns(inputJob, claimedJob);
            await RunSubOrchestrator();

            Assert.AreEqual(0, _emailSenderCallCount,
                "No SyncStartedNotification for reclaimed stuck job (LRT updated to now, not MinValue)");
        }

        #endregion

        #region Scenario 7: Stuck just past period boundary

        [TestMethod]
        public async Task Scenario7_StuckJustPastPeriod_ClaimUpdatesLRT()
        {
            var justPastPeriod = DateTime.UtcNow.AddHours(-25);
            var tenDaysAgo = DateTime.UtcNow.AddDays(-10);
            var (inputJob, claimedJob) = CreateJobPair(
                inputStatus: SyncStatus.InProgress,
                inputLastSuccessfulStartTime: justPastPeriod,
                inputLastRunTime: tenDaysAgo,
                claimedStatus: SyncStatus.StuckInProgress,
                claimedLastSuccessfulStartTime: ClaimTime,
                claimedLastRunTime: ClaimTime);

            SetupClaimReturns(inputJob, claimedJob);
            await RunSubOrchestrator();

            Assert.IsNotNull(_capturedTopicMessageJob, "TopicMessageSender should be called on success path");
            Assert.AreEqual(ClaimTime, _capturedTopicMessageJob.LastRunTime,
                "LRT must be updated for StuckInProgress claim at period boundary");
            Assert.AreEqual(ClaimTime, _capturedTopicMessageJob.LastSuccessfulStartTime);
        }

        #endregion

        #region Scenario 8: InProgress but NOT stuck (rejected)

        [TestMethod]
        public async Task Scenario8_FreshInProgress_ClaimRejected_NoDownstreamCalls()
        {
            var oneHourAgo = DateTime.UtcNow.AddHours(-1);
            var inputJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                Period = 24,
                Status = SyncStatus.InProgress.ToString(),
                LastSuccessfulStartTime = oneHourAgo,
                LastRunTime = oneHourAgo,
                Query = "[{\"type\":\"GroupMembership\",\"source\": \"" + Guid.NewGuid() + "\"}]",
                MembershipType = MembershipTypes.GroupMembership.ToString()
            };

            _context.Setup(x => x.GetInput<SyncJob>()).Returns(inputJob);
            _context.Setup(x => x.CallActivityAsync<SyncJob?>(
                    It.Is<TaskName>(t => t == nameof(ClaimJobFunction)),
                    It.IsAny<ClaimJobRequest>(),
                    It.IsAny<TaskOptions>()))
                .ReturnsAsync((SyncJob)null);

            await RunSubOrchestrator();

            Assert.AreEqual(0, _jobUpdaterCallCount, "No JobUpdater calls when claim is rejected");
            Assert.AreEqual(0, _emailSenderCallCount, "No email when claim is rejected");
            Assert.IsNull(_capturedTopicMessageJob, "No TopicMessageSender when claim is rejected");
        }

        #endregion

        #region Scenario 9: Concurrent claim (already claimed by another instance)

        [TestMethod]
        public async Task Scenario9_AlreadyClaimed_ReturnsNull_BailsOut()
        {
            var inputJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                Period = 24,
                Status = SyncStatus.Idle.ToString(),
                LastSuccessfulStartTime = DateTime.UtcNow.AddDays(-1),
                LastRunTime = DateTime.UtcNow.AddDays(-1),
                Query = "[{\"type\":\"GroupMembership\",\"source\": \"" + Guid.NewGuid() + "\"}]",
                MembershipType = MembershipTypes.GroupMembership.ToString()
            };

            _context.Setup(x => x.GetInput<SyncJob>()).Returns(inputJob);
            _context.Setup(x => x.CallActivityAsync<SyncJob?>(
                    It.Is<TaskName>(t => t == nameof(ClaimJobFunction)),
                    It.IsAny<ClaimJobRequest>(),
                    It.IsAny<TaskOptions>()))
                .ReturnsAsync((SyncJob)null);

            await RunSubOrchestrator();

            Assert.AreEqual(0, _jobUpdaterCallCount);
            Assert.AreEqual(0, _emailSenderCallCount);
            Assert.IsNull(_capturedTopicMessageJob);
        }

        #endregion

        #region Scenario 10: Error status (not claimable)

        [TestMethod]
        public async Task Scenario10_ErrorStatus_ClaimRejected()
        {
            var threeDaysAgo = DateTime.UtcNow.AddDays(-3);
            var inputJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                Period = 24,
                Status = SyncStatus.Error.ToString(),
                LastSuccessfulStartTime = threeDaysAgo,
                LastRunTime = threeDaysAgo,
                Query = "[{\"type\":\"GroupMembership\",\"source\": \"" + Guid.NewGuid() + "\"}]",
                MembershipType = MembershipTypes.GroupMembership.ToString()
            };

            _context.Setup(x => x.GetInput<SyncJob>()).Returns(inputJob);
            _context.Setup(x => x.CallActivityAsync<SyncJob?>(
                    It.Is<TaskName>(t => t == nameof(ClaimJobFunction)),
                    It.IsAny<ClaimJobRequest>(),
                    It.IsAny<TaskOptions>()))
                .ReturnsAsync((SyncJob)null);

            await RunSubOrchestrator();

            Assert.AreEqual(0, _jobUpdaterCallCount);
        }

        #endregion

        #region Cross-cutting invariants

        [TestMethod]
        public async Task Invariant_TopicMessageNeverReceivesIdleStatus_AfterSuccessfulClaim()
        {
            // This is THE clobber regression test:
            // Input has Status=Idle, claim returns Status=InProgress.
            // TopicMessageSenderFunction must see InProgress, never Idle.
            var (inputJob, claimedJob) = CreateJobPair(
                inputStatus: SyncStatus.Idle,
                inputLastSuccessfulStartTime: DateTime.UtcNow.AddDays(-2),
                inputLastRunTime: DateTime.UtcNow.AddDays(-2),
                claimedStatus: SyncStatus.InProgress,
                claimedLastSuccessfulStartTime: ClaimTime,
                claimedLastRunTime: DateTime.UtcNow.AddDays(-2));

            SetupClaimReturns(inputJob, claimedJob);
            await RunSubOrchestrator();

            Assert.IsNotNull(_capturedTopicMessageJob, "TopicMessageSender should be called on success path");
            Assert.AreNotEqual(SyncStatus.Idle.ToString(), _capturedTopicMessageJob.Status,
                "REGRESSION: TopicMessageSender received Idle status — stale input clobbered the atomic claim!");
            Assert.AreEqual(SyncStatus.InProgress.ToString(), _capturedTopicMessageJob.Status);
        }

        [TestMethod]
        public async Task Invariant_LSSTPropagatedToTopicMessage_NotStaleValue()
        {
            // Input LSST is 7 days ago, claim refreshes to ClaimTime.
            // TopicMessageSender must receive ClaimTime, not the 7-day-old value.
            var sevenDaysAgo = DateTime.UtcNow.AddDays(-7);
            var (inputJob, claimedJob) = CreateJobPair(
                inputStatus: SyncStatus.Idle,
                inputLastSuccessfulStartTime: sevenDaysAgo,
                inputLastRunTime: sevenDaysAgo,
                claimedStatus: SyncStatus.InProgress,
                claimedLastSuccessfulStartTime: ClaimTime,
                claimedLastRunTime: sevenDaysAgo);

            SetupClaimReturns(inputJob, claimedJob);
            await RunSubOrchestrator();

            Assert.IsNotNull(_capturedTopicMessageJob, "TopicMessageSender should be called on success path");
            Assert.AreNotEqual(sevenDaysAgo, _capturedTopicMessageJob.LastSuccessfulStartTime,
                "REGRESSION: TopicMessageSender received stale LSST from input, not refreshed value from claim!");
            Assert.AreEqual(ClaimTime, _capturedTopicMessageJob.LastSuccessfulStartTime);
        }

        [TestMethod]
        public async Task Invariant_StuckReclaim_LRTUpdated_NotStaleValue()
        {
            // Input LRT is 5 days ago, StuckInProgress claim sets LRT to ClaimTime.
            // TopicMessageSender must receive ClaimTime, not the 5-day-old value.
            var fiveDaysAgo = DateTime.UtcNow.AddDays(-5);
            var fortyEightHoursAgo = DateTime.UtcNow.AddHours(-48);
            var (inputJob, claimedJob) = CreateJobPair(
                inputStatus: SyncStatus.InProgress,
                inputLastSuccessfulStartTime: fortyEightHoursAgo,
                inputLastRunTime: fiveDaysAgo,
                claimedStatus: SyncStatus.StuckInProgress,
                claimedLastSuccessfulStartTime: ClaimTime,
                claimedLastRunTime: ClaimTime);

            SetupClaimReturns(inputJob, claimedJob);
            await RunSubOrchestrator();

            Assert.IsNotNull(_capturedTopicMessageJob, "TopicMessageSender should be called on success path");
            Assert.AreNotEqual(fiveDaysAgo, _capturedTopicMessageJob.LastRunTime,
                "REGRESSION: TopicMessageSender received stale LRT from input!");
            Assert.AreEqual(ClaimTime, _capturedTopicMessageJob.LastRunTime);
        }

        [TestMethod]
        public async Task Invariant_TopicMessageSender_ReceivesClaimedRunId()
        {
            var (inputJob, claimedJob) = CreateJobPair(
                inputStatus: SyncStatus.Idle,
                inputLastSuccessfulStartTime: DateTime.UtcNow.AddDays(-1),
                inputLastRunTime: DateTime.UtcNow.AddDays(-1),
                claimedStatus: SyncStatus.InProgress,
                claimedLastSuccessfulStartTime: ClaimTime,
                claimedLastRunTime: DateTime.UtcNow.AddDays(-1));

            SetupClaimReturns(inputJob, claimedJob);
            await RunSubOrchestrator();

            Assert.IsNotNull(_capturedTopicMessageJob, "TopicMessageSender should be called on happy path");
            Assert.AreEqual(claimedJob.RunId, _capturedTopicMessageJob.RunId,
                "TopicMessageSender must receive the RunId from the claimed job");
        }

        [TestMethod]
        public async Task Invariant_ErrorPath_StillReceivesClaimedJob()
        {
            // After successful claim, if destination not found → error path.
            // JobUpdater on error path must still receive the claimed job values.
            var (inputJob, claimedJob) = CreateJobPair(
                inputStatus: SyncStatus.Idle,
                inputLastSuccessfulStartTime: DateTime.UtcNow.AddDays(-5),
                inputLastRunTime: DateTime.UtcNow.AddDays(-5),
                claimedStatus: SyncStatus.InProgress,
                claimedLastSuccessfulStartTime: ClaimTime,
                claimedLastRunTime: DateTime.UtcNow.AddDays(-5));

            SetupClaimReturns(inputJob, claimedJob);

            // Override destination verifier to return NotFound
            _context.Setup(x => x.CallActivityAsync<DestinationVerifierResult>(
                    It.Is<TaskName>(t => t == nameof(DestinationVerifierFunction)), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()))
                .ReturnsAsync(DestinationVerifierResult.NotFound);

            await RunSubOrchestrator();

            // JobUpdater is called with DestinationGroupNotFound status, but the syncJob should still have claimed values
            Assert.AreEqual(SyncStatus.DestinationGroupNotFound, _capturedJobUpdaterStatus);
            Assert.AreEqual(ClaimTime, _capturedJobUpdaterJob.LastSuccessfulStartTime,
                "Even on error path, LSST must come from claimed job, not stale input");
        }

        #endregion
    }
}
