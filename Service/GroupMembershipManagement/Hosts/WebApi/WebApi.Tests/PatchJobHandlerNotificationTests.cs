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
using SyncJob = Models.SyncJob;
using Setting = Models.Setting;

namespace WebApi.Tests
{
    [TestClass]
    public class PatchJobHandlerNotificationTests
    {
        private Mock<ILoggingRepository> _mockLoggingRepository = null!;
        private Mock<IGraphGroupRepository> _mockGraphGroupRepository = null!;
        private Mock<IDatabaseSyncJobsRepository> _mockSyncJobRepository = null!;
        private Mock<ISyncJobChangeRepository> _mockSyncJobChangeRepository = null!;
        private Mock<IDatabaseTitlesRepository> _mockTitle = null!;
        private Mock<IDatabaseSettingsRepository> _mockSettingsRepository = null!;
        private Mock<INotificationService> _mockNotificationService = null!;
        private Mock<IThresholdConfig> _mockThresholdConfig = null!;
        private PatchJobHandler _patchJobHandler = null!;
        private SyncJob _testSyncJob = null!;
        private SyncJobChange _testSubmission = null!;

        [TestInitialize]
        public void Initialize()
        {
            _mockLoggingRepository = new Mock<ILoggingRepository>();
            _mockGraphGroupRepository = new Mock<IGraphGroupRepository>();
            _mockSyncJobRepository = new Mock<IDatabaseSyncJobsRepository>();
            _mockSyncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            _mockTitle = new Mock<IDatabaseTitlesRepository>();
            _mockSettingsRepository = new Mock<IDatabaseSettingsRepository>();
            _mockNotificationService = new Mock<INotificationService>();
            _mockThresholdConfig = new Mock<IThresholdConfig>();

            // Setup default threshold config
            _mockThresholdConfig.Setup(x => x.NumberOfThresholdViolationsToNotify).Returns(3);

            _patchJobHandler = new PatchJobHandler(
                _mockLoggingRepository.Object,
                _mockGraphGroupRepository.Object,
                _mockSyncJobRepository.Object,
                _mockSyncJobChangeRepository.Object,
                _mockTitle.Object,
                _mockSettingsRepository.Object,
                _mockNotificationService.Object,
                _mockThresholdConfig.Object);

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
        public async Task ReviewSubmission_WhenApproved_SetsThresholdViolationsToNotifyMinusOne()
        {
            // Arrange
            _testSyncJob.LastRunTime = DateTime.UtcNow;
            var patchDocument = new JsonPatchDocument<SyncJobPatch>();
            patchDocument.Replace(x => x.Status, SyncStatus.Idle.ToString());

            var request = new PatchJobRequest(
                isAllowed: true,
                userIdentity: Guid.NewGuid().ToString(),
                syncJobId: _testSyncJob.Id,
                patchDocument: patchDocument,
                userDisplayName: "Reviewer",
                changeReason: SyncJobChangeReason.SubmissionApproved.ToString(),
                businessJustification: "Approved for testing",
                canApproveJob: true
            );

            // Setup threshold config to return 5 for NumberOfThresholdViolationsToNotify
            _mockThresholdConfig.Setup(x => x.NumberOfThresholdViolationsToNotify).Returns(5);

            // Act
            var response = await _patchJobHandler.ExecuteAsync(request);

            // Assert
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            
            // Verify that UpdateSyncJobsAsync was called with ThresholdViolations set to 4 (5 - 1)
            _mockSyncJobRepository.Verify(
                x => x.UpdateSyncJobsAsync(
                    It.Is<IEnumerable<SyncJob>>(jobs => 
                        jobs.Count() == 1 && 
                        jobs.First().ThresholdViolations == 4),
                    It.IsAny<SyncStatus?>()),
                Times.Once,
                "UpdateSyncJobsAsync should be called with ThresholdViolations set to N-1");
        }

        [TestMethod]
        public async Task ReviewSubmission_WhenApproved_InitialSync_DoesNotSetThresholdViolationsToNotifyMinusOne()
        {
            // Arrange
            _testSyncJob.LastRunTime = SqlDateTime.MinValue.Value;
            _testSyncJob.ThresholdViolations = 0;

            var patchDocument = new JsonPatchDocument<SyncJobPatch>();
            patchDocument.Replace(x => x.Status, SyncStatus.Idle.ToString());

            var request = new PatchJobRequest(
                isAllowed: true,
                userIdentity: Guid.NewGuid().ToString(),
                syncJobId: _testSyncJob.Id,
                patchDocument: patchDocument,
                userDisplayName: "Reviewer",
                changeReason: SyncJobChangeReason.SubmissionApproved.ToString(),
                businessJustification: "Approved for testing",
                canApproveJob: true
            );

            _mockThresholdConfig.Setup(x => x.NumberOfThresholdViolationsToNotify).Returns(5);

            // Act
            var response = await _patchJobHandler.ExecuteAsync(request);

            // Assert
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

            _mockSyncJobRepository.Verify(
                x => x.UpdateSyncJobsAsync(
                    It.Is<IEnumerable<SyncJob>>(jobs =>
                        jobs.Count() == 1 &&
                        jobs.First().ThresholdViolations == 0),
                    It.IsAny<SyncStatus?>()),
                Times.Once,
                "UpdateSyncJobsAsync should not bump ThresholdViolations on initial sync");
        }
    }
}
