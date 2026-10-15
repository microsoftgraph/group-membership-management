// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.AspNetCore.JsonPatch;
using Models;
using Models.SyncJobChange;
using Moq;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using Services.Messages.Requests;
using Services.WebApi;
using Services.WebApi.Contracts;
using System.Data.SqlTypes;
using System.Net;
using WebApi.Models.DTOs;
using Microsoft.Extensions.Logging.Abstractions;
using SyncJob = Models.SyncJob;
using Setting = Models.Setting;

namespace WebApi.Tests
{
    [TestClass]
    public class PatchJobHandlerNotificationTests
    {
        private Mock<IGraphGroupRepository> _mockGraphGroupRepository = null!;
        private Mock<IDatabaseSyncJobsRepository> _mockSyncJobRepository = null!;
        private Mock<ISyncJobChangeRepository> _mockSyncJobChangeRepository = null!;
        private Mock<IDatabaseTitlesRepository> _mockTitle = null!;
        private Mock<IDatabaseSettingsRepository> _mockSettingsRepository = null!;
        private Mock<INotificationService> _mockNotificationService = null!;
        private CapturingTelemetryChannel _capturingTelemetryChannel = null!;
        private PatchJobHandler _patchJobHandler = null!;
        private SyncJob _testSyncJob = null!;
        private SyncJobChange _testSubmission = null!;

        [TestInitialize]
        public void Initialize()
        {
            _mockGraphGroupRepository = new Mock<IGraphGroupRepository>();
            _mockSyncJobRepository = new Mock<IDatabaseSyncJobsRepository>();
            _mockSyncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            _mockTitle = new Mock<IDatabaseTitlesRepository>();
            _mockSettingsRepository = new Mock<IDatabaseSettingsRepository>();
            _mockNotificationService = new Mock<INotificationService>();
            _capturingTelemetryChannel = new CapturingTelemetryChannel();

            _patchJobHandler = new PatchJobHandler(
                NullLogger<PatchJobHandler>.Instance,
                _mockGraphGroupRepository.Object,
                _mockSyncJobRepository.Object,
                _mockSyncJobChangeRepository.Object,
                _mockTitle.Object,
                _mockSettingsRepository.Object,
                _mockNotificationService.Object,
                _capturingTelemetryChannel.CreateClient());

            var groupId = Guid.NewGuid();
            _testSyncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                Status = SyncStatus.PendingReview.ToString(),
                Requestor = "test@contoso.com",
                Query = "TestQuery",
                Period = 24,
                TargetOfficeGroupId = groupId,
                Group = new Group { GroupId = groupId },
                MembershipType = MembershipTypes.GroupMembership.ToString()
            };

            _testSubmission = new SyncJobChange
            {
                Id = Guid.NewGuid(),
                SyncJobId = _testSyncJob.Id,
                ChangedByObjectId = Guid.NewGuid(),
                ChangedByDisplayName = "Test User",
                BusinessJustification = "Test business justification",
                ChangeReason = SyncJobChangeReason.Update.ToString(),
                ChangeTime = DateTime.UtcNow,
                ChangeSource = SyncJobChangeSource.WebApp
            };

            // Setup basic mocks
            _mockSyncJobRepository.Setup(x => x.GetSyncJobAsync(_testSyncJob.Id))
                .ReturnsAsync(_testSyncJob);

            _mockGraphGroupRepository.Setup(x => x.IsEmailRecipientOwnerOfGroupAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<bool>()))
                .ReturnsAsync(true);

            _mockSyncJobChangeRepository.Setup(x => x.GetLastSyncJobChangeBySyncJobIdAsync(_testSyncJob.Id))
                .ReturnsAsync(_testSubmission);

            _mockGraphGroupRepository.Setup(x => x.GetDestinationOwnersAsync(It.IsAny<List<Guid>>()))
                .ReturnsAsync(new Dictionary<Guid, List<Guid>> 
                { 
                    { groupId, new List<Guid> { _testSubmission.ChangedByObjectId.Value } } 
                });

            _mockSettingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.CanReviewOwnSubmissions))
                .ReturnsAsync(new Setting { SettingValue = "true" });

            _mockSyncJobRepository.Setup(x => x.UpdateSyncJobsAsync(It.IsAny<IEnumerable<SyncJob>>(), It.IsAny<SyncStatus?>()))
                .Returns(Task.CompletedTask);            
            _mockSyncJobChangeRepository.Setup(x => x.Save(It.IsAny<SyncJobChange>()))
                .Returns(Task.CompletedTask);
        }

        [TestMethod]
        public async Task ReviewSubmission_WhenRejected_CallsNotificationService()
        {
            // Arrange
            var patchDocument = new JsonPatchDocument<SyncJobPatch>();
            patchDocument.Replace(x => x.Status, SyncStatus.SubmissionRejected.ToString());

            var request = new PatchJobRequest(
                isAllowed: true,
                isOnBehalfAllowed: true,
                userIdentity: Guid.NewGuid().ToString(),
                syncJobId: _testSyncJob.Id,
                patchDocument: patchDocument,
                userDisplayName: "Reviewer",
                changeReason: SyncJobChangeReason.SubmissionRejected.ToString(),
                businessJustification: "Rejected for testing",
                canApproveJob: true
            );

            // Act
            var response = await _patchJobHandler.ExecuteAsync(request);

            // Assert
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            
            // Verify notification service was called with the NEW SyncJobChange containing the rejection feedback
            _mockNotificationService.Verify(
                x => x.SendSubmissionRejectedNotificationAsync(
                    _testSyncJob, 
                    It.Is<SyncJobChange>(sjc => 
                        sjc.BusinessJustification == "Rejected for testing" &&
                        sjc.ChangeReason == SyncJobChangeReason.SubmissionRejected.ToString())),
                Times.Once,
                "Notification service should be called with the new SyncJobChange containing rejection feedback");
        }

        [TestMethod]
        public async Task ReviewSubmission_WhenApproved_CallsNotificationService()
        {
            // Arrange
            var patchDocument = new JsonPatchDocument<SyncJobPatch>();
            patchDocument.Replace(x => x.Status, SyncStatus.Idle.ToString());

            var request = new PatchJobRequest(
                isAllowed: true,
                isOnBehalfAllowed: true,
                userIdentity: Guid.NewGuid().ToString(),
                syncJobId: _testSyncJob.Id,
                patchDocument: patchDocument,
                userDisplayName: "Reviewer",
                changeReason: SyncJobChangeReason.SubmissionApproved.ToString(),
                businessJustification: "Approved for testing",
                canApproveJob: true
            );

            // Act
            var response = await _patchJobHandler.ExecuteAsync(request);

            // Assert
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            
            // Verify notification service was called with the NEW SyncJobChange containing the approval feedback
            _mockNotificationService.Verify(
                x => x.SendSubmissionApprovedNotificationAsync(
                    _testSyncJob, 
                    It.Is<SyncJobChange>(sjc => 
                        sjc.BusinessJustification == "Approved for testing" &&
                        sjc.ChangeReason == SyncJobChangeReason.SubmissionApproved.ToString())),
                Times.Once,
                "Notification service should be called with the new SyncJobChange containing approval feedback");

            _mockNotificationService.Verify(
                x => x.SendSubmissionRejectedNotificationAsync(It.IsAny<SyncJob>(), It.IsAny<SyncJobChange>()),
                Times.Never,
                "Rejection notification service should not be called when submission is approved");
        }

        [TestMethod]
        public async Task ReviewSubmission_WhenNotificationFails_StillCompletesSuccessfully()
        {
            // Arrange
            var patchDocument = new JsonPatchDocument<SyncJobPatch>();
            patchDocument.Replace(x => x.Status, SyncStatus.SubmissionRejected.ToString());

            var request = new PatchJobRequest(
                isAllowed: true,
                isOnBehalfAllowed: true,
                userIdentity: Guid.NewGuid().ToString(),
                syncJobId: _testSyncJob.Id,
                patchDocument: patchDocument,
                userDisplayName: "Reviewer",
                changeReason: SyncJobChangeReason.SubmissionRejected.ToString(),
                businessJustification: "Rejected for testing",
                canApproveJob: true
            );            
            
            // Setup notification service to throw an exception
            _mockNotificationService.Setup(x => x.SendSubmissionRejectedNotificationAsync(It.IsAny<SyncJob>(), It.IsAny<SyncJobChange>()))
                .ThrowsAsync(new InvalidOperationException("Notification failed"));

            // Act & Assert
            // The handler should let the notification exception bubble up
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => _patchJobHandler.ExecuteAsync(request));

            // Verify that the notification was attempted with the NEW SyncJobChange containing rejection feedback
            _mockNotificationService.Verify(
                x => x.SendSubmissionRejectedNotificationAsync(
                    _testSyncJob, 
                    It.Is<SyncJobChange>(sjc => 
                        sjc.BusinessJustification == "Rejected for testing" &&
                        sjc.ChangeReason == SyncJobChangeReason.SubmissionRejected.ToString())),
                Times.Once);
        }

        [TestMethod]
        public async Task ReviewSubmission_WhenApproved_InitialSync_SetsJobToIdleWithSingleUpdate()
        {
            // Arrange
            _testSyncJob.LastRunTime = SqlDateTime.MinValue.Value;

            var patchDocument = new JsonPatchDocument<SyncJobPatch>();
            patchDocument.Replace(x => x.Status, SyncStatus.Idle.ToString());

            var request = new PatchJobRequest(
                isAllowed: true,
                isOnBehalfAllowed: true,
                userIdentity: Guid.NewGuid().ToString(),
                syncJobId: _testSyncJob.Id,
                patchDocument: patchDocument,
                userDisplayName: "Reviewer",
                changeReason: SyncJobChangeReason.SubmissionApproved.ToString(),
                businessJustification: "Approved for testing",
                canApproveJob: true
            );

            // Act
            var response = await _patchJobHandler.ExecuteAsync(request);

            // Assert
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

            _mockSyncJobRepository.Verify(
                x => x.UpdateSyncJobsAsync(
                    It.Is<IEnumerable<SyncJob>>(jobs =>
                        jobs.Count() == 1 &&
                        jobs.First().Status == SyncStatus.Idle.ToString()),
                    It.IsAny<SyncStatus?>()),
                Times.Once,
                "Approval on an initial sync should update the single job to Idle exactly once");
        }

        // ── JobReviewed telemetry ──

        private PatchJobRequest ReviewRequest(string newStatus, string changeReason, string userIdentity, bool canApproveJob = true)
        {
            var patchDocument = new JsonPatchDocument<SyncJobPatch>();
            patchDocument.Replace(x => x.Status, newStatus);

            return new PatchJobRequest(
                isAllowed: true,
                isOnBehalfAllowed: true,
                userIdentity: userIdentity,
                syncJobId: _testSyncJob.Id,
                patchDocument: patchDocument,
                userDisplayName: "Ada Lovelace",
                changeReason: changeReason,
                businessJustification: "Reviewed for testing",
                canApproveJob: canApproveJob
            );
        }

        [TestMethod]
        public async Task JobReviewed_WhenApproved_EmitsOneManualMetric()
        {
            var reviewerId = Guid.NewGuid().ToString();

            var response = await _patchJobHandler.ExecuteAsync(
                ReviewRequest(SyncStatus.Idle.ToString(), SyncJobChangeReason.SubmissionApproved.ToString(), reviewerId));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

            var metrics = _capturingTelemetryChannel.GetMetrics("JobReviewed");
            Assert.AreEqual(1, metrics.Count, "Exactly one JobReviewed metric is expected per persisted approval.");

            // One approval per item is what lets the dashboard sum valueSum for an exact count.
            Assert.AreEqual(1, metrics[0].Sum);
            Assert.AreEqual(1, metrics[0].Count);

            // Recorded as a metric rather than a custom event because custom events are sampled.
            Assert.AreEqual(0, _capturingTelemetryChannel.GetEvents("JobReviewed").Count, "JobReviewed must not be emitted as a sampled custom event.");

            var properties = metrics[0].Properties;
            Assert.AreEqual(_testSyncJob.Id.ToString(), properties["SyncJobId"]);
            Assert.AreEqual(_testSyncJob.Group!.GroupId.ToString(), properties["TargetOfficeGroupId"]);
            Assert.AreEqual(MembershipTypes.GroupMembership.ToString(), properties["MembershipType"]);
            Assert.AreEqual("Manual", properties["ReviewalType"]);

            // The fixture submission under review is an Update, so this approval must not land in the
            // onboarding figures.
            Assert.AreEqual("Update", properties["RequestType"]);
            Assert.AreEqual("Approved", properties["Outcome"]);

            // The reviewer's object id, which is what makes the per-reviewer panel reconcile against
            // SyncJobChanges.ChangedByObjectId.
            Assert.AreEqual(reviewerId, properties["ReviewerId"]);
        }

        [TestMethod]
        public async Task JobReviewed_WhenApprovedSubmissionIsAnOnboarding_EmitsOnboardingRequestType()
        {
            _testSubmission.ChangeReason = SyncJobChangeReason.Onboarding.ToString();

            await _patchJobHandler.ExecuteAsync(
                ReviewRequest(SyncStatus.Idle.ToString(), SyncJobChangeReason.SubmissionApproved.ToString(), Guid.NewGuid().ToString()));

            // The patch itself carries SubmissionApproved either way, so only the submission under review
            // can tell an onboarding from an edit.
            var properties = _capturingTelemetryChannel.GetMetrics("JobReviewed").Single().Properties;
            Assert.AreEqual("Onboarding", properties["RequestType"]);
            Assert.AreEqual("Approved", properties["Outcome"]);
        }

        [TestMethod]
        public async Task JobReviewed_WhenApproved_CarriesNoPersonallyIdentifyingAttribute()
        {
            await _patchJobHandler.ExecuteAsync(
                ReviewRequest(SyncStatus.Idle.ToString(), SyncJobChangeReason.SubmissionApproved.ToString(), Guid.NewGuid().ToString()));

            var properties = _capturingTelemetryChannel.GetMetrics("JobReviewed").Single().Properties;

            CollectionAssert.AreEquivalent(
                new[] { "SyncJobId", "TargetOfficeGroupId", "MembershipType", "ReviewalType", "ReviewerId", "RequestType", "Outcome" },
                properties.Keys.ToArray(),
                "The metric must carry exactly the seven contracted dimensions.");

            foreach (var value in properties.Values)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(value), "No dimension may be emitted empty.");
                Assert.IsFalse(value.Contains('@'), $"'{value}' looks like a UPN or email address.");
                Assert.IsFalse(value.Contains(' '), $"'{value}' looks like a display name.");
            }
        }

        [TestMethod]
        public async Task JobReviewed_WhenApprovalNotificationFails_MetricIsStillEmitted()
        {
            _mockNotificationService
                .Setup(x => x.SendSubmissionApprovedNotificationAsync(It.IsAny<SyncJob>(), It.IsAny<SyncJobChange>()))
                .ThrowsAsync(new InvalidOperationException("Notification failed"));

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => _patchJobHandler.ExecuteAsync(
                ReviewRequest(SyncStatus.Idle.ToString(), SyncJobChangeReason.SubmissionApproved.ToString(), Guid.NewGuid().ToString())));

            // The approval was already persisted, so a notification failure must not suppress the record.
            Assert.AreEqual(1, _capturingTelemetryChannel.GetMetrics("JobReviewed").Count);
        }

        [TestMethod]
        public async Task JobReviewed_WhenRejected_EmitsOneManualRejection()
        {
            var reviewerId = Guid.NewGuid().ToString();

            var response = await _patchJobHandler.ExecuteAsync(
                ReviewRequest(SyncStatus.SubmissionRejected.ToString(), SyncJobChangeReason.SubmissionRejected.ToString(), reviewerId));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

            var metrics = _capturingTelemetryChannel.GetMetrics("JobReviewed");
            Assert.AreEqual(1, metrics.Count, "Exactly one JobReviewed metric is expected per persisted rejection.");

            var properties = metrics[0].Properties;
            Assert.AreEqual("Rejected", properties["Outcome"]);
            Assert.AreEqual("Manual", properties["ReviewalType"]);
            Assert.AreEqual("Update", properties["RequestType"]);

            // A rejection the reviewer chose is attributed to that reviewer, unlike one a precheck forced.
            Assert.AreEqual(reviewerId, properties["ReviewerId"]);
        }

        [TestMethod]
        public async Task JobReviewed_WhenRejectionNotificationFails_MetricIsStillEmitted()
        {
            _mockNotificationService
                .Setup(x => x.SendSubmissionRejectedNotificationAsync(It.IsAny<SyncJob>(), It.IsAny<SyncJobChange>()))
                .ThrowsAsync(new InvalidOperationException("Notification failed"));

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => _patchJobHandler.ExecuteAsync(
                ReviewRequest(SyncStatus.SubmissionRejected.ToString(), SyncJobChangeReason.SubmissionRejected.ToString(), Guid.NewGuid().ToString())));

            // The rejection was already persisted, so a notification failure must not suppress the record.
            Assert.AreEqual(1, _capturingTelemetryChannel.GetMetrics("JobReviewed").Count);
        }

        [TestMethod]
        public async Task JobReviewed_WhenCallerIsNotASubmissionReviewer_EmitsNothing()
        {
            var response = await _patchJobHandler.ExecuteAsync(
                ReviewRequest(SyncStatus.Idle.ToString(), SyncJobChangeReason.SubmissionApproved.ToString(), Guid.NewGuid().ToString(), canApproveJob: false));

            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.AreEqual("OnlySubmissionReviewerCanApproveSubmission", response.ErrorCode);
            Assert.AreEqual(0, _capturingTelemetryChannel.GetMetrics("JobReviewed").Count);
        }

        [TestMethod]
        public async Task JobReviewed_WhenReviewerIsTheSubmitterAndSelfReviewIsDisabled_EmitsNothing()
        {
            _mockSettingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.CanReviewOwnSubmissions))
                .ReturnsAsync(new Setting { SettingValue = "false" });

            var response = await _patchJobHandler.ExecuteAsync(
                ReviewRequest(SyncStatus.Idle.ToString(), SyncJobChangeReason.SubmissionApproved.ToString(), _testSubmission.ChangedByObjectId!.Value.ToString()));

            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.AreEqual("ReviewerCannotReviewOwnSubmission", response.ErrorCode);
            Assert.AreEqual(0, _capturingTelemetryChannel.GetMetrics("JobReviewed").Count);
        }

        [TestMethod]
        public async Task JobReviewed_WhenSubmitterIsNoLongerAnOwner_EmitsSystemRejection()
        {
            _mockGraphGroupRepository.Setup(x => x.GetDestinationOwnersAsync(It.IsAny<List<Guid>>()))
                .ReturnsAsync(new Dictionary<Guid, List<Guid>> { { _testSyncJob.TargetOfficeGroupId, new List<Guid>() } });

            var reviewerId = Guid.NewGuid().ToString();
            var response = await _patchJobHandler.ExecuteAsync(
                ReviewRequest(SyncStatus.Idle.ToString(), SyncJobChangeReason.SubmissionApproved.ToString(), reviewerId));

            Assert.AreEqual("SubmitterNotOwner", response.ErrorCode);

            // The submission was force-rejected, not approved.
            var properties = _capturingTelemetryChannel.GetMetrics("JobReviewed").Single().Properties;
            Assert.AreEqual("Rejected", properties["Outcome"]);
            Assert.AreEqual("Update", properties["RequestType"]);

            // The reviewer asked to approve and the precheck overruled them, so crediting them with this
            // rejection would misstate their manual review record.
            Assert.AreEqual("System", properties["ReviewerId"]);
            Assert.AreNotEqual(reviewerId, properties["ReviewerId"]);
            Assert.AreEqual("Auto", properties["ReviewalType"]);
        }

        [TestMethod]
        public async Task JobReviewed_WhenRequestedStatusIsNeitherIdleNorRejected_EmitsNothing()
        {
            var response = await _patchJobHandler.ExecuteAsync(
                ReviewRequest(SyncStatus.CustomerPaused.ToString(), SyncJobChangeReason.SubmissionApproved.ToString(), Guid.NewGuid().ToString()));

            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.AreEqual("ValidUpdateStatusIsRequired", response.ErrorCode);
            Assert.AreEqual(0, _capturingTelemetryChannel.GetMetrics("JobReviewed").Count);
        }
    }
}
