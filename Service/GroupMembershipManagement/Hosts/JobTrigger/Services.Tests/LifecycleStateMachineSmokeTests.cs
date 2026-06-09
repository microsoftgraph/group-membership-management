// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
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
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using Services.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Services.Tests
{
    /// <summary>
    /// Smoke tests validating the FULL job lifecycle state machine for SubOrchestratorFunction.
    /// These tests document the CURRENT (baseline) behavior before any refactoring.
    /// Each scenario maps to a specific status transition in the state machine.
    /// </summary>
    [TestClass]
    public class SubOrchestratorLifecycleSmokeTests
    {
        private Mock<IJobTriggerService> _jobTriggerService;
        private Mock<TaskOrchestrationContext> _context;
        private Mock<IEmailSenderRecipient> _emailSenderAndRecipients;
        private Mock<IGMMResources> _gmmResources;
        private SyncJob _syncJob;
        private TelemetryClient _telemetryClient;
        private int _frequency;
        private JsonSchemaProvider _jsonSchemaProvider;
        private bool _jsonValidationResult;
        private string _destinationName;

        // Enhanced tracking for lifecycle verification
        private List<SyncStatus?> _statusTransitions;
        private int _topicMessageSenderCallCount;
        private List<SyncStatus> _claimStatuses;

        [TestInitialize]
        public void Setup()
        {
            _statusTransitions = new List<SyncStatus?>();
            _topicMessageSenderCallCount = 0;
            _claimStatuses = new List<SyncStatus>();

            _syncJob = SampleDataHelper.CreateSampleSyncJobs(1, "GroupMembership").First();
            _gmmResources = new Mock<IGMMResources>();
            _jobTriggerService = new Mock<IJobTriggerService>();
            _context = new Mock<TaskOrchestrationContext>();
            _context.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(NullLogger.Instance);
            _emailSenderAndRecipients = new Mock<IEmailSenderRecipient>();
            _telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            _frequency = 0;
            _jsonSchemaProvider = new JsonSchemaProvider();

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

            var group = new Group { GroupId = Guid.NewGuid(), SyncJobId = Guid.NewGuid() };
            var channel = new Channel { ChannelId = "some-string", GroupId = Guid.NewGuid(), SyncJobId = Guid.NewGuid() };

            var options = new JsonSerializerOptions { Converters = { new DestinationValueConverter() } };
            var serializedDestinationObject = JsonSerializer.Serialize(destinationObject, options);

            _jobTriggerService.Setup(x => x.ParseAndValidateDestinationAsync(It.IsAny<SyncJob>()))
                .ReturnsAsync(new ParsedAndValidateDestinationResponse { IsValid = true, DestinationObject = serializedDestinationObject });

            _jobTriggerService.Setup(x => x.UpdateSyncJobAsync(It.IsAny<SyncStatus?>(), It.IsAny<SyncJob>()))
                .Callback<SyncStatus?, SyncJob>((status, job) => { });

            _context.Setup(x => x.CallActivityAsync<int>(It.IsAny<TaskName>(), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()))
                .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                {
                    _frequency = await CallJobTrackerFunctionAsync(request as SyncJob, DateTime.UtcNow);
                })
                .ReturnsAsync(() => _frequency);

            _context.Setup(x => x.CallActivityAsync<ParsedAndValidateDestinationResponse>(
                    It.Is<TaskName>(x => x == nameof(ParseAndValidateDestinationFunction)),
                    It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()))
                .Returns(async () => await CallParseAndValidateDestinationFunction());

            _context.Setup(x => x.CallActivityAsync<Group>(
                    It.Is<TaskName>(x => x == nameof(GetGroupFunction)),
                    It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()))
                .ReturnsAsync(group);

            _context.Setup(x => x.CallActivityAsync<Channel>(
                    It.Is<TaskName>(x => x == nameof(GetChannelFunction)),
                    It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()))
                .ReturnsAsync(channel);

            _context.Setup(x => x.CallActivityAsync(
                    It.Is<TaskName>(x => x == nameof(TelemetryTrackerFunction)),
                    It.IsAny<TelemetryTrackerRequest>(), It.IsAny<TaskOptions>()))
                .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                {
                    await CallTelemetryTrackerFunctionAsync(request as TelemetryTrackerRequest);
                });

            // Enhanced JobUpdaterFunction mock — tracks ALL status transitions in order
            _context.Setup(x => x.CallActivityAsync(
                    It.Is<TaskName>(x => x == nameof(JobUpdaterFunction)),
                    It.IsAny<JobUpdaterRequest>(), It.IsAny<TaskOptions>()))
                .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                {
                    var updateRequest = request as JobUpdaterRequest;
                    _statusTransitions.Add(updateRequest.Status);
                    await CallJobStatusUpdaterFunctionAsync(updateRequest);
                });

            _context.Setup(x => x.CallActivityAsync<DestinationVerifierResult>(
                    It.Is<TaskName>(x => x == nameof(DestinationVerifierFunction)),
                    It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()))
                .Returns(async () => await CallDestinationVerifierFunctionAsync());

            _context.Setup(x => x.CallActivityAsync<string>(
                    It.Is<TaskName>(x => x == nameof(DestinationNameReaderFunction)),
                    It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()))
                .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                {
                    _destinationName = await CallDestinationNameReaderFunctionAsync();
                })
                .ReturnsAsync(() => _destinationName);

            _context.Setup(x => x.CallActivityAsync(
                    It.Is<TaskName>(x => x == nameof(EmailSenderFunction)),
                    It.IsAny<EmailSenderRequest>(), It.IsAny<TaskOptions>()))
                .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                {
                    await CallEmailSenderFunctionAsync(request as EmailSenderRequest);
                });

            _context.Setup(x => x.CallActivityAsync<SyncJob>(
                    It.Is<TaskName>(x => x == nameof(GetSyncJobFunction)),
                    It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()))
                .ReturnsAsync((TaskName name, object syncJobId, TaskOptions options) =>
                {
                    var refreshedJob = _syncJob;
                    refreshedJob.RunId = _syncJob.RunId;
                    return refreshedJob;
                });

            // Enhanced TopicMessageSenderFunction mock — counts invocations
            _context.Setup(x => x.CallActivityAsync(
                    It.Is<TaskName>(x => x == nameof(TopicMessageSenderFunction)),
                    It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()))
                .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                {
                    _topicMessageSenderCallCount++;
                    await CallTopicMessageSenderFunctionAsync();
                });

            _context.Setup(x => x.CallActivityAsync<bool>(nameof(SchemaValidatorFunction),
                    It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()))
                .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                {
                    _jsonValidationResult = await CallSchemaValidatorFunctionAsync(request as SyncJob);
                })
                .ReturnsAsync(() => _jsonValidationResult);

            _context.Setup(x => x.CallActivityAsync<SyncJob?>(
                    It.Is<TaskName>(x => x == nameof(ClaimJobFunction)),
                    It.IsAny<ClaimJobRequest>(),
                    It.IsAny<TaskOptions>()))
                .Callback<TaskName, object, TaskOptions>((name, request, options) =>
                {
                    var claimRequest = request as ClaimJobRequest;
                    _claimStatuses.Add(claimRequest.Status);
                    _statusTransitions.Add(claimRequest.Status);
                    // Simulate DB claim behavior
                    _syncJob.LastSuccessfulStartTime = DateTime.UtcNow;
                    if (claimRequest.Status == SyncStatus.StuckInProgress)
                        _syncJob.LastRunTime = DateTime.UtcNow;
                })
                .ReturnsAsync(() => _syncJob);

            _jsonSchemaProvider = SchemaProviderFactory.CreateJsonSchemaProvider();
        }

        #region Scenario 1: Idle → InProgress (Normal job start)

        [TestMethod]
        [TestCategory("SmokeTest")]
        public async Task Scenario1_IdleJob_TransitionsToInProgress_AndSendsTopicMessage()
        {
            // Arrange: Job with Status=Idle, Period=24 (default from SampleDataHelper is Idle)
            _syncJob.Status = SyncStatus.Idle.ToString();
            _syncJob.Period = 24;
            _context.Setup(x => x.GetInput<SyncJob>()).Returns(_syncJob);

            // Act
            var subOrchestrator = new SubOrchestratorFunction(
                _telemetryClient, _emailSenderAndRecipients.Object, _gmmResources.Object);
            await subOrchestrator.RunSubOrchestratorAsync(_context.Object);

            // Assert: ClaimJobFunction called with InProgress
            Assert.IsTrue(_claimStatuses.Contains(SyncStatus.InProgress),
                "Idle job should claim with InProgress");

            // Assert: TopicMessageSenderFunction was called (job proceeds to message sending)
            Assert.AreEqual(1, _topicMessageSenderCallCount,
                "TopicMessageSenderFunction should be called exactly once for a successful job");

            // Verify via context mock
            _context.Verify(x => x.CallActivityAsync<SyncJob?>(
                It.Is<TaskName>(t => t == nameof(ClaimJobFunction)),
                It.Is<ClaimJobRequest>(r => r.Status == SyncStatus.InProgress),
                It.IsAny<TaskOptions>()), Times.Once());
        }

        #endregion

        #region Scenario 2: InProgress → StuckInProgress (Job stuck past 1x Period)

        [TestMethod]
        [TestCategory("SmokeTest")]
        public async Task Scenario2_InProgressJob_PastPeriod_TransitionsToStuckInProgress()
        {
            // Arrange: Job with Status=InProgress, started 25 hours ago, Period=24
            // This job was selected by ApplyJobTriggerFilters because it's past its period.
            // ClaimJobFunction checks: Status == Idle ? InProgress : StuckInProgress
            // Since InProgress != Idle, it claims with StuckInProgress.
            _syncJob.Status = SyncStatus.InProgress.ToString();
            _syncJob.Period = 24;
            _syncJob.LastSuccessfulStartTime = DateTime.UtcNow.AddHours(-25);
            _context.Setup(x => x.GetInput<SyncJob>()).Returns(_syncJob);

            // Act
            var subOrchestrator = new SubOrchestratorFunction(
                _telemetryClient, _emailSenderAndRecipients.Object, _gmmResources.Object);
            await subOrchestrator.RunSubOrchestratorAsync(_context.Object);

            // Assert: ClaimJobFunction called with StuckInProgress (not InProgress)
            Assert.IsTrue(_claimStatuses.Contains(SyncStatus.StuckInProgress),
                "InProgress job should claim with StuckInProgress when Status != Idle");

            // Assert: TopicMessageSenderFunction was called (job proceeds with restart)
            Assert.AreEqual(1, _topicMessageSenderCallCount,
                "TopicMessageSenderFunction should be called for stuck-in-progress job restart");

            _context.Verify(x => x.CallActivityAsync<SyncJob?>(
                It.Is<TaskName>(t => t == nameof(ClaimJobFunction)),
                It.Is<ClaimJobRequest>(r => r.Status == SyncStatus.StuckInProgress),
                It.IsAny<TaskOptions>()), Times.Once());
        }

        #endregion

        #region Scenario 3: StuckInProgress → ErroredDueToStuckInProgress (Job stuck past 2x Period)

        [TestMethod]
        [TestCategory("SmokeTest")]
        public async Task Scenario3_StuckInProgressJob_TransitionsToErroredDueToStuckInProgress_ReturnsEarly()
        {
            // Arrange: Job with Status=StuckInProgress
            // The SubOrchestrator checks at line 47: if Status == StuckInProgress → ErroredDueToStuckInProgress
            _syncJob.Status = SyncStatus.StuckInProgress.ToString();
            _context.Setup(x => x.GetInput<SyncJob>()).Returns(_syncJob);

            // Act
            var subOrchestrator = new SubOrchestratorFunction(
                _telemetryClient, _emailSenderAndRecipients.Object, _gmmResources.Object);
            await subOrchestrator.RunSubOrchestratorAsync(_context.Object);

            // Assert: JobUpdaterFunction called with ErroredDueToStuckInProgress
            Assert.IsTrue(_statusTransitions.Contains(SyncStatus.ErroredDueToStuckInProgress),
                "StuckInProgress job should transition to ErroredDueToStuckInProgress");
            Assert.AreEqual(1, _statusTransitions.Count,
                "Only one JobUpdaterFunction call should happen (early return at line 52)");

            // Assert: TopicMessageSenderFunction was NOT called (early return)
            Assert.AreEqual(0, _topicMessageSenderCallCount,
                "TopicMessageSenderFunction should NOT be called for errored-due-to-stuck job");

            // Verify no further processing occurred (JobTrackerFunction should not be called)
            _context.Verify(x => x.CallActivityAsync<int>(
                It.Is<TaskName>(t => t == nameof(JobTrackerFunction)),
                It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()), Times.Never(),
                "No further activity functions should be called after early return");
        }

        #endregion

        #region Scenario 6: Duplicate concurrent run (atomic claim prevents duplicate processing)

        [TestMethod]
        [TestCategory("SmokeTest")]
        public async Task Scenario6_DuplicateConcurrentRun_SecondRunBlockedByAtomicClaim()
        {
            // With atomic claim, the first SubOrchestrator claims the job via ClaimJobFunction,
            // and the second one gets false — preventing duplicate processing.

            var sharedJobId = Guid.NewGuid();
            var claimCallCount = 0;

            // Override ClaimJobFunction: first call returns true, second returns false
            _context.Setup(x => x.CallActivityAsync<SyncJob?>(
                    It.Is<TaskName>(t => t == nameof(ClaimJobFunction)),
                    It.IsAny<ClaimJobRequest>(),
                    It.IsAny<TaskOptions>()))
                .ReturnsAsync(() =>
                {
                    claimCallCount++;
                    if (claimCallCount <= 1)
                    {
                        _syncJob.LastSuccessfulStartTime = DateTime.UtcNow;
                        return _syncJob;
                    }
                    return (SyncJob?)null;
                });

            // --- First run ---
            var job1 = SampleDataHelper.CreateSampleSyncJobs(1, "GroupMembership").First();
            job1.Id = sharedJobId;
            job1.RunId = Guid.NewGuid();
            job1.Status = SyncStatus.Idle.ToString();
            _syncJob = job1;
            _context.Setup(x => x.GetInput<SyncJob>()).Returns(job1);

            var subOrchestrator1 = new SubOrchestratorFunction(
                _telemetryClient, _emailSenderAndRecipients.Object, _gmmResources.Object);
            await subOrchestrator1.RunSubOrchestratorAsync(_context.Object);

            var run1TopicMessageCount = _topicMessageSenderCallCount;

            // Reset tracking for second run
            _topicMessageSenderCallCount = 0;

            // --- Second run --- same job ID, different RunId
            var job2 = SampleDataHelper.CreateSampleSyncJobs(1, "GroupMembership").First();
            job2.Id = sharedJobId;
            job2.RunId = Guid.NewGuid();
            job2.Status = SyncStatus.Idle.ToString();
            _syncJob = job2;
            _context.Setup(x => x.GetInput<SyncJob>()).Returns(job2);

            var subOrchestrator2 = new SubOrchestratorFunction(
                _telemetryClient, _emailSenderAndRecipients.Object, _gmmResources.Object);
            await subOrchestrator2.RunSubOrchestratorAsync(_context.Object);

            // Assert: First run proceeded, second was blocked
            Assert.AreEqual(1, run1TopicMessageCount,
                "First concurrent run should send topic message (claim succeeded)");
            Assert.AreEqual(0, _topicMessageSenderCallCount,
                "Second concurrent run should NOT send topic message (claim returned false)");
            Assert.AreEqual(2, claimCallCount,
                "ClaimJobFunction should have been called exactly twice");
        }

        #endregion

        #region Scenario 7: TransientError → StuckInProgress (not InProgress)

        [TestMethod]
        [TestCategory("SmokeTest")]
        public async Task Scenario7_TransientErrorJob_TransitionsToStuckInProgress_NotInProgress()
        {
            // Arrange: Job with Status=TransientError
            // The SubOrchestrator at line 246 checks: Status == Idle ? InProgress : StuckInProgress
            // TransientError != Idle, so it sets StuckInProgress.
            // This may be unexpected — a TransientError retry gets StuckInProgress rather than InProgress.
            _syncJob.Status = SyncStatus.TransientError.ToString();
            _context.Setup(x => x.GetInput<SyncJob>()).Returns(_syncJob);

            // Act
            var subOrchestrator = new SubOrchestratorFunction(
                _telemetryClient, _emailSenderAndRecipients.Object, _gmmResources.Object);
            await subOrchestrator.RunSubOrchestratorAsync(_context.Object);

            // Assert: ClaimJobFunction claims with StuckInProgress (NOT InProgress)
            Assert.IsTrue(_claimStatuses.Contains(SyncStatus.StuckInProgress),
                "TransientError job gets StuckInProgress (not InProgress) because ternary only checks for Idle");

            // Assert: Job still proceeds to TopicMessageSender
            Assert.AreEqual(1, _topicMessageSenderCallCount,
                "TransientError job should still proceed to send topic message");

            _context.Verify(x => x.CallActivityAsync<SyncJob?>(
                It.Is<TaskName>(t => t == nameof(ClaimJobFunction)),
                It.Is<ClaimJobRequest>(r => r.Status == SyncStatus.StuckInProgress),
                It.IsAny<TaskOptions>()), Times.Once());
        }

        #endregion

        #region Scenario 8: Error during validation — InProgress IS set before validation via ClaimJobFunction

        [TestMethod]
        [TestCategory("SmokeTest")]
        public async Task Scenario8_IdleJob_DestinationNotFound_InProgressSetBeforeValidation()
        {
            // Arrange: Idle job where DestinationVerifier returns NotFound
            // WITH ATOMIC CLAIM: InProgress is set FIRST via ClaimJobFunction, then DestinationGroupNotFound
            // overwrites it. InProgress IS now in the status transitions (from ClaimJobFunction).
            _syncJob.Status = SyncStatus.Idle.ToString();
            _context.Setup(x => x.GetInput<SyncJob>()).Returns(_syncJob);

            // Override DestinationVerifier to return NotFound
            _context.Setup(x => x.CallActivityAsync<DestinationVerifierResult>(
                    It.Is<TaskName>(t => t == nameof(DestinationVerifierFunction)),
                    It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()))
                .ReturnsAsync(DestinationVerifierResult.NotFound);

            // Act
            var subOrchestrator = new SubOrchestratorFunction(
                _telemetryClient, _emailSenderAndRecipients.Object, _gmmResources.Object);
            await subOrchestrator.RunSubOrchestratorAsync(_context.Object);

            // Assert: InProgress WAS set via ClaimJobFunction (before validation)
            Assert.IsTrue(_claimStatuses.Contains(SyncStatus.InProgress),
                "InProgress is now set via ClaimJobFunction before validation runs");

            // Assert: DestinationGroupNotFound was set
            Assert.IsTrue(_statusTransitions.Contains(SyncStatus.DestinationGroupNotFound),
                "DestinationGroupNotFound should be set when destination doesn't exist");

            // Assert: TopicMessageSenderFunction was NOT called
            Assert.AreEqual(0, _topicMessageSenderCallCount,
                "TopicMessageSenderFunction should NOT be called when destination is not found");

            // Assert: Two non-null statuses were set (InProgress from claim, then DestinationGroupNotFound)
            var nonNullStatuses = _statusTransitions.Where(s => s.HasValue).ToList();
            Assert.AreEqual(2, nonNullStatuses.Count,
                "Two non-null status transitions should occur (InProgress from claim + DestinationGroupNotFound)");
            Assert.AreEqual(SyncStatus.InProgress, nonNullStatuses[0]);
            Assert.AreEqual(SyncStatus.DestinationGroupNotFound, nonNullStatuses[1]);
        }

        #endregion

        #region Scenario 9: Exception during processing → Error status (InProgress IS set via claim)

        [TestMethod]
        [TestCategory("SmokeTest")]
        public async Task Scenario9_ExceptionDuringProcessing_SetsErrorStatus_InProgressSetViaClaim()
        {
            // Arrange: Job where JobTrackerFunction (first activity after claim) throws
            // WITH ATOMIC CLAIM: InProgress is set FIRST via ClaimJobFunction,
            // then exception triggers catch block which sets Error.
            _syncJob.Status = SyncStatus.Idle.ToString();
            _context.Setup(x => x.GetInput<SyncJob>()).Returns(_syncJob);

            // Override JobTrackerFunction to throw
            _context.Setup(x => x.CallActivityAsync<int>(
                    It.IsAny<TaskName>(), It.IsAny<SyncJob>(), It.IsAny<TaskOptions>()))
                .Throws(new Exception("Simulated infrastructure failure"));

            // Act
            var subOrchestrator = new SubOrchestratorFunction(
                _telemetryClient, _emailSenderAndRecipients.Object, _gmmResources.Object);
            await subOrchestrator.RunSubOrchestratorAsync(_context.Object);

            // Assert: Error status was set by catch block
            Assert.IsTrue(_statusTransitions.Contains(SyncStatus.Error),
                "Exception should result in Error status via catch block");

            // Assert: InProgress WAS set via ClaimJobFunction (before the exception)
            Assert.IsTrue(_claimStatuses.Contains(SyncStatus.InProgress),
                "InProgress is now set via ClaimJobFunction before the exception occurs");

            // Assert: TopicMessageSenderFunction was NOT called
            Assert.AreEqual(0, _topicMessageSenderCallCount,
                "TopicMessageSenderFunction should NOT be called when exception occurs");

            // Verify error telemetry was tracked
            _context.Verify(x => x.CallActivityAsync(
                It.Is<TaskName>(t => t == nameof(TelemetryTrackerFunction)),
                It.Is<TelemetryTrackerRequest>(r =>
                    r.JobStatus == SyncStatus.Error && r.ResultStatus == ResultStatus.Failure),
                It.IsAny<TaskOptions>()), Times.Once());
        }

        #endregion

        #region Helper methods (same pattern as SubOrchestratorFunctionTests)

        private async Task<ParsedAndValidateDestinationResponse> CallParseAndValidateDestinationFunction()
        {
            var function = new ParseAndValidateDestinationFunction(
                NullLogger<ParseAndValidateDestinationFunction>.Instance, _jobTriggerService.Object);
            return await function.ParseAndValidateDestinationAsync(new SyncJob());
        }

        private async Task<int> CallJobTrackerFunctionAsync(SyncJob syncJob, DateTime dateTime)
        {
            syncJob.LastSuccessfulRunTime = dateTime;
            var function = new JobTrackerFunction(NullLogger<JobTrackerFunction>.Instance);
            return await function.TrackJobFrequencyAsync(syncJob);
        }

        private async Task CallTelemetryTrackerFunctionAsync(TelemetryTrackerRequest request)
        {
            var function = new TelemetryTrackerFunction(
                NullLogger<TelemetryTrackerFunction>.Instance, _telemetryClient);
            await function.TrackEventAsync(request);
        }

        private async Task CallJobStatusUpdaterFunctionAsync(JobUpdaterRequest request)
        {
            var function = new JobUpdaterFunction(
                NullLogger<JobUpdaterFunction>.Instance, _jobTriggerService.Object);
            await function.UpdateJobAsync(request);
        }

        private async Task<DestinationVerifierResult> CallDestinationVerifierFunctionAsync()
        {
            var function = new DestinationVerifierFunction(
                NullLogger<DestinationVerifierFunction>.Instance, _jobTriggerService.Object);
            return await function.VerifyDestinationAsync(_syncJob);
        }

        private async Task<string> CallDestinationNameReaderFunctionAsync()
        {
            var function = new DestinationNameReaderFunction(
                NullLogger<DestinationNameReaderFunction>.Instance, _jobTriggerService.Object);
            return await function.GetDestinationNameAsync(_syncJob);
        }

        private async Task CallEmailSenderFunctionAsync(EmailSenderRequest request)
        {
            var function = new EmailSenderFunction(
                NullLogger<EmailSenderFunction>.Instance, _jobTriggerService.Object);
            await function.SendEmailAsync(request);
        }

        private async Task CallTopicMessageSenderFunctionAsync()
        {
            var function = new TopicMessageSenderFunction(
                NullLogger<TopicMessageSenderFunction>.Instance, _jobTriggerService.Object);
            await function.SendMessageAsync(_syncJob);
        }

        private async Task<bool> CallSchemaValidatorFunctionAsync(SyncJob job)
        {
            var function = new SchemaValidatorFunction(
                NullLogger<SchemaValidatorFunction>.Instance, _jobTriggerService.Object, _jsonSchemaProvider);
            return await function.ValidateSchemasAsync(job);
        }

        #endregion
    }

    /// <summary>
    /// Smoke tests for the ApplyJobTriggerFilters logic in JobTriggerService.
    /// These tests verify which jobs are selected for processing based on status,
    /// period, and LastSuccessfulStartTime.
    /// </summary>
    [TestClass]
    public class ApplyJobTriggerFiltersSmokeTests
    {
        private Mock<IDatabaseSyncJobsRepository> _mockSyncJobRepo;
        private Mock<IJobTriggerConfig> _mockJobTriggerConfig;
        private JobTriggerService _jobTriggerService;
        private TelemetryClient _telemetryClient;

        [TestInitialize]
        public void Setup()
        {
            _telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            _mockSyncJobRepo = new Mock<IDatabaseSyncJobsRepository>();
            _mockJobTriggerConfig = new Mock<IJobTriggerConfig>();

            // Set threshold very high so it doesn't interfere with filter results
            _mockJobTriggerConfig.Setup(x => x.JobCountThreshold).Returns(int.MaxValue);
            _mockJobTriggerConfig.Setup(x => x.JobPerMilleThreshold).Returns(int.MaxValue);

            // Mock GetSyncJobCountAsync (used for threshold calculation)
            _mockSyncJobRepo.Setup(x => x.GetSyncJobCountAsync(It.IsAny<SyncStatus[]>()))
                .ReturnsAsync(1000);

            // Build the service with all required mocked dependencies
            var mockGroupsRepo = new Mock<IDatabaseGroupsRepository>();
            var mockChannelsRepo = new Mock<IDatabaseChannelsRepository>();
            var mockDestAttrRepo = new Mock<IDatabaseDestinationAttributesRepository>();
            var mockNotifTypesRepo = new Mock<INotificationTypesRepository>();
            var mockJobNotifRepo = new Mock<IJobNotificationsRepository>();
            var mockSbTopicsRepo = new Mock<IServiceBusTopicsRepository>();
            var mockGraphRepo = new Mock<IGraphGroupRepository>();
            var mockTeamsRepo = new Mock<ITeamsChannelRepository>();
            var mockGmmAppId = new Mock<IKeyVaultSecret<IJobTriggerService>>();
            mockGmmAppId.Setup(x => x.Secret).Returns("");
            var mockTeamsServiceAccountId = new Mock<IKeyVaultSecret<IJobTriggerService, Guid>>();
            mockTeamsServiceAccountId.Setup(x => x.Secret).Returns(Guid.Empty);
            var mockEmailRecipients = new Mock<IEmailSenderRecipient>();
            var mockSbQueueRepo = new Mock<IServiceBusQueueRepository>();
            var mockGmmResources = new Mock<IGMMResources>();
            var mockSyncJobStatusService = new Mock<ISyncJobStatusService>();

            _jobTriggerService = new JobTriggerService(
                NullLogger<JobTriggerService>.Instance,
                _mockSyncJobRepo.Object,
                mockGroupsRepo.Object,
                mockChannelsRepo.Object,
                mockDestAttrRepo.Object,
                mockNotifTypesRepo.Object,
                mockJobNotifRepo.Object,
                mockSbTopicsRepo.Object,
                mockGraphRepo.Object,
                mockTeamsRepo.Object,
                mockGmmAppId.Object,
                mockTeamsServiceAccountId.Object,
                mockEmailRecipients.Object,
                mockSbQueueRepo.Object,
                mockGmmResources.Object,
                _mockJobTriggerConfig.Object,
                _telemetryClient,
                mockSyncJobStatusService.Object
            );
        }

        private SyncJob CreateJob(
            SyncStatus status,
            int period = 24,
            DateTime? lastSuccessfulStartTime = null,
            bool isDryRun = false)
        {
            var job = SampleDataHelper.CreateSampleSyncJobs(1, "GroupMembership", period).First();
            job.Status = status.ToString();
            job.LastSuccessfulStartTime = lastSuccessfulStartTime ?? DateTime.UtcNow.AddHours(-1);
            job.IsDryRunEnabled = isDryRun;
            return job;
        }

        #region Scenario 4: InProgress within Period (NOT stuck — should NOT be picked up)

        [TestMethod]
        [TestCategory("SmokeTest")]
        public async Task Scenario4_InProgressWithinPeriod_NotPickedUp()
        {
            // Arrange: InProgress job started 1 hour ago with 24-hour period
            // ApplyJobTriggerFilters splits into:
            //   allDueToRunJobs: Status != InProgress → EXCLUDES this job
            //   inProgressSyncJobs: (UtcNow - LSS) > Period hours → 1h > 24h → FALSE
            // Result: job is NOT returned
            var job = CreateJob(SyncStatus.InProgress, period: 24,
                lastSuccessfulStartTime: DateTime.UtcNow.AddHours(-1));

            _mockSyncJobRepo.Setup(x => x.GetSyncJobsAsync(It.IsAny<bool>(), It.IsAny<SyncStatus[]>()))
                .ReturnsAsync(new List<SyncJob> { job }.AsEnumerable());

            // Act
            var result = await _jobTriggerService.GetSyncJobsAsync();

            // Assert
            Assert.AreEqual(0, result.Count,
                "InProgress job within its period (1h elapsed vs 24h period) should NOT be picked up");
        }

        #endregion

        #region Scenario 5: InProgress exactly at Period boundary (strict > comparison)

        [TestMethod]
        [TestCategory("SmokeTest")]
        public async Task Scenario5a_InProgressJustUnderBoundary_NotPickedUp()
        {
            // Arrange: InProgress job started 23h59m ago with 24-hour period
            // The filter uses strict ">": (UtcNow - LSS) > TimeSpan.FromHours(Period)
            // 23h59m > 24h → FALSE → not picked up
            var job = CreateJob(SyncStatus.InProgress, period: 24,
                lastSuccessfulStartTime: DateTime.UtcNow.AddHours(-24).AddMinutes(1));

            _mockSyncJobRepo.Setup(x => x.GetSyncJobsAsync(It.IsAny<bool>(), It.IsAny<SyncStatus[]>()))
                .ReturnsAsync(new List<SyncJob> { job }.AsEnumerable());

            // Act
            var result = await _jobTriggerService.GetSyncJobsAsync();

            // Assert
            Assert.AreEqual(0, result.Count,
                "InProgress job just under boundary (23h59m vs 24h period) should NOT be picked up (strict > comparison)");
        }

        [TestMethod]
        [TestCategory("SmokeTest")]
        public async Task Scenario5b_InProgressJustPastBoundary_IsPickedUp()
        {
            // Arrange: InProgress job started 24h01m ago with 24-hour period
            // 24h01m > 24h → TRUE → picked up
            var job = CreateJob(SyncStatus.InProgress, period: 24,
                lastSuccessfulStartTime: DateTime.UtcNow.AddHours(-24).AddMinutes(-1));

            _mockSyncJobRepo.Setup(x => x.GetSyncJobsAsync(It.IsAny<bool>(), It.IsAny<SyncStatus[]>()))
                .ReturnsAsync(new List<SyncJob> { job }.AsEnumerable());

            // Act
            var result = await _jobTriggerService.GetSyncJobsAsync();

            // Assert
            Assert.AreEqual(1, result.Count,
                "InProgress job just past boundary (24h01m vs 24h period) SHOULD be picked up");
            Assert.AreEqual(job.Id, result[0].Id);
        }

        #endregion

        #region Scenario 10: Comprehensive ApplyJobTriggerFilters test

        [TestMethod]
        [TestCategory("SmokeTest")]
        public async Task Scenario10_ComprehensiveFilterTest_VariousStatuses()
        {
            // Arrange: Mix of jobs with various statuses
            // ApplyJobTriggerFilters logic:
            //   allDueToRunJobs = !IsDryRunEnabled && Status != InProgress
            //   inProgressSyncJobs = Status == InProgress && (UtcNow - LSS) > Period
            //   Result = allDueToRunJobs ∪ inProgressSyncJobs
            var idleJob = CreateJob(SyncStatus.Idle, period: 24);
            var inProgressWithinPeriod = CreateJob(SyncStatus.InProgress, period: 24,
                lastSuccessfulStartTime: DateTime.UtcNow.AddHours(-1));
            var inProgressPastPeriod = CreateJob(SyncStatus.InProgress, period: 24,
                lastSuccessfulStartTime: DateTime.UtcNow.AddHours(-25));
            var stuckInProgressJob = CreateJob(SyncStatus.StuckInProgress, period: 24);
            var transientErrorJob = CreateJob(SyncStatus.TransientError, period: 24);

            var allJobs = new List<SyncJob>
            {
                idleJob,
                inProgressWithinPeriod,
                inProgressPastPeriod,
                stuckInProgressJob,
                transientErrorJob
            };

            _mockSyncJobRepo.Setup(x => x.GetSyncJobsAsync(It.IsAny<bool>(), It.IsAny<SyncStatus[]>()))
                .ReturnsAsync(allJobs.AsEnumerable());

            // Act
            var result = await _jobTriggerService.GetSyncJobsAsync();

            // Assert: Expected results from ApplyJobTriggerFilters:
            // 1. Idle → INCLUDED (allDueToRunJobs: !DryRun, Status != InProgress)
            // 2. InProgress within period → EXCLUDED (not in allDueToRunJobs because Status==InProgress,
            //    not in inProgressSyncJobs because 1h < 24h period)
            // 3. InProgress past period → INCLUDED (inProgressSyncJobs: 25h > 24h AND Status==InProgress)
            // 4. StuckInProgress → INCLUDED (allDueToRunJobs: Status != InProgress)
            // 5. TransientError → INCLUDED (allDueToRunJobs: Status != InProgress)
            Assert.AreEqual(4, result.Count,
                "Should return 4 jobs: Idle, InProgress(past period), StuckInProgress, TransientError");

            Assert.IsTrue(result.Any(j => j.Id == idleJob.Id),
                "Idle job should be included");
            Assert.IsFalse(result.Any(j => j.Id == inProgressWithinPeriod.Id),
                "InProgress job within period should be EXCLUDED");
            Assert.IsTrue(result.Any(j => j.Id == inProgressPastPeriod.Id),
                "InProgress job past period should be included");
            Assert.IsTrue(result.Any(j => j.Id == stuckInProgressJob.Id),
                "StuckInProgress job should be included");
            Assert.IsTrue(result.Any(j => j.Id == transientErrorJob.Id),
                "TransientError job should be included");
        }

        [TestMethod]
        [TestCategory("SmokeTest")]
        public async Task Scenario10b_DryRunJobs_AreExcluded()
        {
            // Arrange: DryRun job should be excluded by ApplyJobTriggerFilters
            // allDueToRunJobs = !IsDryRunEnabled && Status != InProgress
            var dryRunJob = CreateJob(SyncStatus.Idle, period: 24, isDryRun: true);
            var normalJob = CreateJob(SyncStatus.Idle, period: 24, isDryRun: false);

            _mockSyncJobRepo.Setup(x => x.GetSyncJobsAsync(It.IsAny<bool>(), It.IsAny<SyncStatus[]>()))
                .ReturnsAsync(new List<SyncJob> { dryRunJob, normalJob }.AsEnumerable());

            // Act
            var result = await _jobTriggerService.GetSyncJobsAsync();

            // Assert
            Assert.AreEqual(1, result.Count, "Only non-dry-run job should be returned");
            Assert.IsFalse(result.Any(j => j.Id == dryRunJob.Id),
                "DryRun job should be excluded by ApplyJobTriggerFilters");
            Assert.IsTrue(result.Any(j => j.Id == normalJob.Id),
                "Normal (non-DryRun) job should be included");
        }

        #endregion
    }
}
