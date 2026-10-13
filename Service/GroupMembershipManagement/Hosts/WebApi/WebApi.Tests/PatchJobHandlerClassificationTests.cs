// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.AspNetCore.JsonPatch;
using Microsoft.Extensions.Logging.Abstractions;
using Models;
using Models.Notifications;
using Models.ServiceBus;
using Models.SyncJobChange;
using Moq;
using Repositories.Contracts;
using Services.Messages.Requests;
using Services.WebApi;
using Services.WebApi.Contracts;
using System.Net;
using System.Text;
using System.Text.Json;
using WebApi.Models.DTOs;
using SyncJob = Models.SyncJob;
using SyncJobChange = Models.SyncJobChange.SyncJobChange;

namespace WebApi.Tests
{
    /// <summary>
    /// Behavioral contract tests for the split Update path in <see cref="PatchJobHandler"/>
    /// (config-only fast path vs. query-changing AutoApprover path), per
    /// specs/20260818-100537-config-only-auto-approval/contracts/patch-update-classification.md.
    /// </summary>
    [TestClass]
    public class PatchJobHandlerClassificationTests
    {
        private const string StoredQuery = "[{\"type\":\"GroupMembership\",\"source\":\"11111111-1111-1111-1111-111111111111\"}]";

        private Mock<IGraphGroupRepository> _graph = null!;
        private Mock<IDatabaseSyncJobsRepository> _jobs = null!;
        private Mock<ISyncJobChangeRepository> _changes = null!;
        private Mock<IDatabaseTitlesRepository> _titles = null!;
        private Mock<IDatabaseSettingsRepository> _settings = null!;
        private Mock<INotificationService> _notifications = null!;
        private Mock<IServiceBusQueueRepository> _autoApproverQueue = null!;
        private SyncJob _syncJob = null!;
        private Guid _groupId;

        private readonly List<SyncJobChange> _savedChanges = new();
        private readonly List<ServiceBusMessage> _enqueued = new();

        [TestInitialize]
        public void Initialize()
        {
            _graph = new Mock<IGraphGroupRepository>();
            _jobs = new Mock<IDatabaseSyncJobsRepository>();
            _changes = new Mock<ISyncJobChangeRepository>();
            _titles = new Mock<IDatabaseTitlesRepository>();
            _settings = new Mock<IDatabaseSettingsRepository>();
            _notifications = new Mock<INotificationService>();
            _autoApproverQueue = new Mock<IServiceBusQueueRepository>();

            _groupId = Guid.NewGuid();
            _syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                Status = SyncStatus.Idle.ToString(),
                Requestor = "owner@contoso.com",
                Query = StoredQuery,
                Period = 24,
                ThresholdPercentageForAdditions = 10,
                ThresholdPercentageForRemovals = 10,
                TargetOfficeGroupId = _groupId,
                Group = new Group { GroupId = _groupId },
                MembershipType = MembershipTypes.GroupMembership.ToString()
            };

            _jobs.Setup(x => x.GetSyncJobAsync(_syncJob.Id)).ReturnsAsync(_syncJob);
            _graph.Setup(x => x.IsEmailRecipientOwnerOfGroupAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<bool>()))
                  .ReturnsAsync(true);
            _jobs.Setup(x => x.UpdateSyncJobsAsync(It.IsAny<IEnumerable<SyncJob>>(), It.IsAny<SyncStatus?>()))
                 .Returns(Task.CompletedTask);
            _changes.Setup(x => x.Save(It.IsAny<SyncJobChange>()))
                    .Callback<SyncJobChange>(c => _savedChanges.Add(c))
                    .Returns(Task.CompletedTask);
            _autoApproverQueue.Setup(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>()))
                              .Callback<ServiceBusMessage>(m => _enqueued.Add(m))
                              .Returns(Task.CompletedTask);

        }

        private PatchJobHandler CreateHandler(bool withQueueRepository = true)
        {
            return new PatchJobHandler(
                NullLogger<PatchJobHandler>.Instance,
                _graph.Object,
                _jobs.Object,
                _changes.Object,
                _titles.Object,
                _settings.Object,
                _notifications.Object,
                withQueueRepository ? _autoApproverQueue.Object : null);
        }

        private PatchJobRequest UpdateRequest(JsonPatchDocument<SyncJobPatch> patch) =>
            new PatchJobRequest(
                isAllowed: true,
                isOnBehalfAllowed: false,
                userIdentity: Guid.NewGuid().ToString(),
                syncJobId: _syncJob.Id,
                patchDocument: patch,
                userDisplayName: "Owner",
                changeReason: SyncJobChangeReason.Update.ToString(),
                businessJustification: "config change",
                canApproveJob: false);

        private AutoApprovalQueueMessage DecodeEnqueued(ServiceBusMessage message) =>
            JsonSerializer.Deserialize<AutoApprovalQueueMessage>(Encoding.UTF8.GetString(message.Body))!;

        // ---------- User Story 1: configuration-only fast path ----------

        [TestMethod]
        public async Task Update_ConfigOnly_IdleJob_StaysIdleAuditsUpdateAutoApprovedAndNotifiesSubmitter()
        {
            var patch = new JsonPatchDocument<SyncJobPatch>();
            patch.Replace(x => x.ThresholdPercentageForAdditions, 42);

            var response = await CreateHandler().ExecuteAsync(UpdateRequest(patch));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(SyncStatus.Idle.ToString(), _syncJob.Status, "Config-only edit must preserve the pre-edit Idle status.");
            Assert.AreEqual(42, _syncJob.ThresholdPercentageForAdditions, "The submitted config field must be persisted.");
            Assert.AreEqual(1, _savedChanges.Count);
            Assert.AreEqual(SyncJobChangeReason.UpdateAutoApproved.ToString(), _savedChanges[0].ChangeReason);
            Assert.AreEqual(0, _enqueued.Count, "No queue message on the config-only path.");
            _notifications.Verify(x => x.SendSubmissionApprovedNotificationAsync(_syncJob, It.IsAny<SyncJobChange>()), Times.Once);
            _notifications.Verify(x => x.SendSubmissionRejectedNotificationAsync(It.IsAny<SyncJob>(), It.IsAny<SyncJobChange>()), Times.Never);
            _notifications.Verify(x => x.SendReviewStatusChangeNotificationAsync(It.IsAny<SyncJob>(), It.IsAny<SyncJobChange>(), It.IsAny<NotificationMessageType>()), Times.Never);
        }

        [TestMethod]
        public async Task Update_ConfigOnly_QueryAbsent_ClassifiedConfigOnly()
        {
            var patch = new JsonPatchDocument<SyncJobPatch>();
            patch.Replace(x => x.Period, 12);

            var response = await CreateHandler().ExecuteAsync(UpdateRequest(patch));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(SyncStatus.Idle.ToString(), _syncJob.Status);
            Assert.AreEqual(12, _syncJob.Period);
            Assert.AreEqual(SyncJobChangeReason.UpdateAutoApproved.ToString(), _savedChanges.Single().ChangeReason);
            Assert.AreEqual(0, _enqueued.Count);
        }

        [TestMethod]
        public async Task Update_ConfigOnly_CustomerPausedJob_StaysPaused()
        {
            _syncJob.Status = SyncStatus.CustomerPaused.ToString();

            var patch = new JsonPatchDocument<SyncJobPatch>();
            patch.Replace(x => x.ThresholdPercentageForRemovals, 25);

            var response = await CreateHandler().ExecuteAsync(UpdateRequest(patch));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(SyncStatus.CustomerPaused.ToString(), _syncJob.Status, "A config-only edit must not resume a paused job.");
            Assert.AreEqual(25, _syncJob.ThresholdPercentageForRemovals);
            Assert.AreEqual(SyncJobChangeReason.UpdateAutoApproved.ToString(), _savedChanges.Single().ChangeReason);
        }

        [TestMethod]
        public async Task Update_QueryDiffersOnlyByWhitespaceAndKeyOrder_ClassifiedConfigOnly()
        {
            var equivalentQuery = "[\n  { \"source\": \"11111111-1111-1111-1111-111111111111\", \"type\": \"GroupMembership\" }\n]";
            var patch = new JsonPatchDocument<SyncJobPatch>();
            patch.Replace(x => x.Query, equivalentQuery);
            patch.Replace(x => x.Period, 6);

            var response = await CreateHandler().ExecuteAsync(UpdateRequest(patch));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(SyncStatus.Idle.ToString(), _syncJob.Status);
            Assert.AreEqual(6, _syncJob.Period);
            Assert.AreEqual(SyncJobChangeReason.UpdateAutoApproved.ToString(), _savedChanges.Single().ChangeReason);
            Assert.AreEqual(0, _enqueued.Count, "A whitespace/key-order-only query change is configuration-only, not a query change.");
        }

        [TestMethod]
        public async Task Update_ConfigOnly_SubmissionRejectedJob_GoesToPendingReviewNotAutoApproved()
        {
            // A config-only edit to a non-operational job (previously rejected by a reviewer) must NOT be
            // auto-approved back into a stranded SubmissionRejected state; it falls back to PendingReview so a
            // human re-evaluates it (no UpdateAutoApproved audit, no "approved" notification).
            _syncJob.Status = SyncStatus.SubmissionRejected.ToString();

            var patch = new JsonPatchDocument<SyncJobPatch>();
            patch.Replace(x => x.ThresholdPercentageForAdditions, 15);

            var response = await CreateHandler().ExecuteAsync(UpdateRequest(patch));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(SyncStatus.PendingReview.ToString(), _syncJob.Status);
            Assert.AreEqual(0, _enqueued.Count);
            Assert.IsFalse(_savedChanges.Any(c => c.ChangeReason == SyncJobChangeReason.UpdateAutoApproved.ToString()),
                "A rejected job must not be auto-approved via the config-only fast path.");
            Assert.AreEqual(SyncJobChangeReason.Update.ToString(), _savedChanges.Single().ChangeReason);
            _notifications.Verify(x => x.SendSubmissionApprovedNotificationAsync(It.IsAny<SyncJob>(), It.IsAny<SyncJobChange>()), Times.Never);
        }

        // ---------- Security regression: non-query sensitive-field changes must not be auto-approved ----------

        [TestMethod]
        public async Task Update_DestinationChangeWithoutQueryChange_GoesToPendingReviewNotAutoApproved()
        {
            // A patch that changes /Destination (but not /Query) could redirect the sync to a different group.
            // Ownership above is authorized against the ORIGINAL group only, so this must go to human review and
            // must NOT be auto-approved via the config-only fast path.
            var patch = new JsonPatchDocument<SyncJobPatch>();
            patch.Replace(x => x.Destination, "[{\"type\":\"GroupMembership\",\"value\":{\"objectId\":\"99999999-9999-9999-9999-999999999999\"}}]");

            var response = await CreateHandler().ExecuteAsync(UpdateRequest(patch));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(SyncStatus.PendingReview.ToString(), _syncJob.Status,
                "A destination change must be routed to human review, not auto-approved.");
            Assert.AreEqual(0, _enqueued.Count, "A non-query field change must not be enqueued to the AutoApprover.");
            Assert.IsFalse(_savedChanges.Any(c => c.ChangeReason == SyncJobChangeReason.UpdateAutoApproved.ToString()),
                "A destination change must never be audited as UpdateAutoApproved.");
            Assert.AreEqual(SyncJobChangeReason.Update.ToString(), _savedChanges.Single().ChangeReason);
            _notifications.Verify(x => x.SendSubmissionApprovedNotificationAsync(It.IsAny<SyncJob>(), It.IsAny<SyncJobChange>()), Times.Never);
        }

        [TestMethod]
        public async Task Update_TargetOfficeGroupIdChange_GoesToPendingReviewNotAutoApproved()
        {
            // Changing /TargetOfficeGroupId redirects the sync to a group the requester may not own; authorization
            // above only validated the original group, so this must go to human review, not the fast path.
            var patch = new JsonPatchDocument<SyncJobPatch>();
            patch.Replace(x => x.TargetOfficeGroupId, Guid.NewGuid());

            var response = await CreateHandler().ExecuteAsync(UpdateRequest(patch));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(SyncStatus.PendingReview.ToString(), _syncJob.Status,
                "A target group change must be routed to human review, not auto-approved.");
            Assert.AreEqual(0, _enqueued.Count);
            Assert.IsFalse(_savedChanges.Any(c => c.ChangeReason == SyncJobChangeReason.UpdateAutoApproved.ToString()));
        }

        [TestMethod]
        public async Task Update_AllowlistedFieldAlongsideDestinationChange_StillGoesToPendingReview()
        {
            // Even when combined with an allowlisted field, a single non-allowlisted operation (here /Destination)
            // must poison the whole submission into human review; the presence of a safe field must not smuggle a
            // destination redirect through the auto-approval fast path.
            var patch = new JsonPatchDocument<SyncJobPatch>();
            patch.Replace(x => x.ThresholdPercentageForAdditions, 30);
            patch.Replace(x => x.Destination, "[{\"type\":\"GroupMembership\",\"value\":{\"objectId\":\"88888888-8888-8888-8888-888888888888\"}}]");

            var response = await CreateHandler().ExecuteAsync(UpdateRequest(patch));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(SyncStatus.PendingReview.ToString(), _syncJob.Status);
            Assert.AreEqual(0, _enqueued.Count);
            Assert.IsFalse(_savedChanges.Any(c => c.ChangeReason == SyncJobChangeReason.UpdateAutoApproved.ToString()));
        }

        [TestMethod]
        public async Task Update_QueryChangeAlongsideNonAllowlistedField_GoesToPendingReview()
        {
            var patch = new JsonPatchDocument<SyncJobPatch>();
            patch.Replace(x => x.Query, "[{\"type\":\"GroupMembership\",\"source\":\"77777777-7777-7777-7777-777777777777\"}]");
            patch.Replace(x => x.AllowEmptyDestination, true);

            var response = await CreateHandler().ExecuteAsync(UpdateRequest(patch));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(SyncStatus.PendingReview.ToString(), _syncJob.Status,
                "A query change must not let a non-allowlisted field bypass human review.");
            Assert.IsTrue(_syncJob.AllowEmptyDestination, "The submitted edit should be preserved for reviewer evaluation.");
            Assert.AreEqual(0, _enqueued.Count, "The AutoApprover evaluates only queries and must not receive this mixed edit.");
        }

        // ---------- User Story 2: query-changing AutoApprover path ----------

        [TestMethod]
        public async Task Update_QueryChanges_SetsPendingAutoApprovalAndEnqueuesUpdateOriginMessage()
        {
            var newQuery = "[{\"type\":\"GroupMembership\",\"source\":\"22222222-2222-2222-2222-222222222222\"}]";
            var patch = new JsonPatchDocument<SyncJobPatch>();
            patch.Replace(x => x.Query, newQuery);

            var request = UpdateRequest(patch);
            var response = await CreateHandler().ExecuteAsync(request);

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(SyncStatus.PendingAutoApproval.ToString(), _syncJob.Status);
            Assert.AreEqual(1, _enqueued.Count);

            var message = DecodeEnqueued(_enqueued.Single());
            Assert.AreEqual(AutoApprovalOrigin.Update, message.Origin);
            Assert.AreEqual(_syncJob.Id, message.SyncJobId);
            Assert.AreEqual(request.UserIdentity, message.RequestorObjectId);
            Assert.AreEqual("Owner", message.RequestorDisplayName);
            Assert.AreEqual("config change", message.BusinessJustification);
        }

        [TestMethod]
        public async Task Update_QueryChanges_WithUiStatusAndOnBehalfMetadata_SetsPendingAutoApprovalAndEnqueues()
        {
            var patch = new JsonPatchDocument<SyncJobPatch>();
            patch.Replace(x => x.Query, "[{\"type\":\"GroupMembership\",\"source\":\"22222222-2222-2222-2222-222222222222\"}]");
            patch.Replace(x => x.Status, SyncStatus.PendingReview.ToString());
            patch.Replace(x => x.LastModifiedOnBehalfOfDisplayName, string.Empty);
            patch.Replace(x => x.LastModifiedOnBehalfOfObjectId, string.Empty);

            var response = await CreateHandler().ExecuteAsync(UpdateRequest(patch));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(SyncStatus.PendingAutoApproval.ToString(), _syncJob.Status);
            Assert.AreEqual(1, _enqueued.Count);
            Assert.AreEqual(AutoApprovalOrigin.Update, DecodeEnqueued(_enqueued.Single()).Origin);
        }

        [TestMethod]
        public async Task Update_RepeatedQueryChanges_UseDistinctMessageIds()
        {
            var firstPatch = new JsonPatchDocument<SyncJobPatch>();
            firstPatch.Replace(x => x.Query, "[{\"type\":\"GroupMembership\",\"source\":\"22222222-2222-2222-2222-222222222222\"}]");
            await CreateHandler().ExecuteAsync(UpdateRequest(firstPatch));

            _syncJob.Status = SyncStatus.Idle.ToString();
            var secondPatch = new JsonPatchDocument<SyncJobPatch>();
            secondPatch.Replace(x => x.Query, "[{\"type\":\"GroupMembership\",\"source\":\"33333333-3333-3333-3333-333333333333\"}]");
            await CreateHandler().ExecuteAsync(UpdateRequest(secondPatch));

            Assert.AreEqual(2, _enqueued.Count);
            Assert.AreNotEqual(_enqueued[0].MessageId, _enqueued[1].MessageId,
                "Repeated edits for one job must not collide with Service Bus duplicate detection.");
            Assert.AreNotEqual(_syncJob.Id.ToString(), _enqueued[0].MessageId,
                "An update message must not collide with the onboarding message ID.");
        }

        [TestMethod]
        public async Task Update_QueryChanges_EnqueueThrows_RevertsToPendingReview()
        {
            _autoApproverQueue.Setup(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>()))
                              .ThrowsAsync(new InvalidOperationException("service bus down"));

            var newQuery = "[{\"type\":\"GroupMembership\",\"source\":\"22222222-2222-2222-2222-222222222222\"}]";
            var patch = new JsonPatchDocument<SyncJobPatch>();
            patch.Replace(x => x.Query, newQuery);

            var response = await CreateHandler().ExecuteAsync(UpdateRequest(patch));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(SyncStatus.PendingReview.ToString(), _syncJob.Status, "A failed enqueue must revert the job to PendingReview.");
            // The submitted query must be preserved through the revert.
            Assert.AreEqual(newQuery, _syncJob.Query);
        }

        [TestMethod]
        public async Task Update_UnparseableStoredQuery_FailsClosedToQueryChanging()
        {
            _syncJob.Query = "not-json";
            var newQuery = "[{\"type\":\"GroupMembership\",\"source\":\"22222222-2222-2222-2222-222222222222\"}]";
            var patch = new JsonPatchDocument<SyncJobPatch>();
            patch.Replace(x => x.Query, newQuery);

            var response = await CreateHandler().ExecuteAsync(UpdateRequest(patch));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(SyncStatus.PendingAutoApproval.ToString(), _syncJob.Status);
            Assert.AreEqual(1, _enqueued.Count, "An unparseable query must fail closed to the query-changing path.");
            Assert.AreEqual(AutoApprovalOrigin.Update, DecodeEnqueued(_enqueued.Single()).Origin);
        }

        [TestMethod]
        public async Task Update_MultipleQueryOps_LastMaterialChangeWins_ClassifiedQueryChanging()
        {
            // Guard against a classification-bypass: JSON Patch allows multiple ops on /Query and ApplyTo applies
            // them all in order. A first op equal to the stored query followed by a materially different second op
            // must be classified on the FINAL query (query-changing), not smuggled through as config-only.
            var patch = new JsonPatchDocument<SyncJobPatch>();
            patch.Replace(x => x.Query, StoredQuery); // equal to stored
            patch.Replace(x => x.Query, "[{\"type\":\"GroupMembership\",\"source\":\"55555555-5555-5555-5555-555555555555\"}]"); // real change

            var response = await CreateHandler().ExecuteAsync(UpdateRequest(patch));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(SyncStatus.PendingAutoApproval.ToString(), _syncJob.Status,
                "The final post-patch query differs, so the submission must be treated as query-changing.");
            Assert.AreEqual(1, _enqueued.Count);
            Assert.AreEqual(AutoApprovalOrigin.Update, DecodeEnqueued(_enqueued.Single()).Origin);
        }

        // ---------- User Story 3: never expedite ----------

        [TestMethod]
        public async Task Update_ConfigOnly_DoesNotTouchTimingFieldsOrUseScheduledNow()
        {
            var scheduledDate = new DateTime(2030, 5, 1, 8, 0, 0, DateTimeKind.Utc);
            var lastRun = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            var lastSuccess = new DateTime(2026, 1, 2, 3, 0, 0, DateTimeKind.Utc);
            var startDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
            _syncJob.ScheduledDate = scheduledDate;
            _syncJob.LastRunTime = lastRun;
            _syncJob.LastSuccessfulRunTime = lastSuccess;
            _syncJob.StartDate = startDate;

            var patch = new JsonPatchDocument<SyncJobPatch>();
            patch.Replace(x => x.ThresholdPercentageForAdditions, 33);

            var response = await CreateHandler().ExecuteAsync(UpdateRequest(patch));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(scheduledDate, _syncJob.ScheduledDate);
            Assert.AreEqual(lastRun, _syncJob.LastRunTime);
            Assert.AreEqual(lastSuccess, _syncJob.LastSuccessfulRunTime);
            Assert.AreEqual(startDate, _syncJob.StartDate);
            Assert.IsFalse(_savedChanges.Any(c => c.ChangeReason == SyncJobChangeReason.ScheduledNow.ToString()),
                "The config-only path must never use the ScheduledNow reason.");
        }

        [TestMethod]
        public async Task Update_ConfigOnly_ExplicitStartDateIsApplied()
        {
            var submittedStartDate = new DateTime(2031, 3, 3, 0, 0, 0, DateTimeKind.Utc);
            var patch = new JsonPatchDocument<SyncJobPatch>();
            patch.Replace(x => x.StartDate, submittedStartDate);

            var response = await CreateHandler().ExecuteAsync(UpdateRequest(patch));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(SyncStatus.Idle.ToString(), _syncJob.Status);
            Assert.AreEqual(submittedStartDate, _syncJob.StartDate, "A user-submitted StartDate is applied as-is; the handler forces nothing.");
        }

        // ---------- User Story 4: disabled AutoApprover ----------

        [TestMethod]
        public async Task Update_NullQueueRepository_ConfigOnlyAndQueryChanging_BothGoToPendingReview()
        {
            // Config-only submission with the AutoApprover host disabled.
            var configPatch = new JsonPatchDocument<SyncJobPatch>();
            configPatch.Replace(x => x.ThresholdPercentageForAdditions, 55);
            await CreateHandler(withQueueRepository: false).ExecuteAsync(UpdateRequest(configPatch));

            Assert.AreEqual(SyncStatus.PendingReview.ToString(), _syncJob.Status);
            Assert.AreEqual(0, _enqueued.Count);
            Assert.IsFalse(_savedChanges.Any(c => c.ChangeReason == SyncJobChangeReason.UpdateAutoApproved.ToString()),
                "With the AutoApprover host disabled there is no UpdateAutoApproved audit reason.");
            Assert.AreEqual(SyncJobChangeReason.Update.ToString(), _savedChanges.Single().ChangeReason);
            _notifications.Verify(x => x.SendSubmissionApprovedNotificationAsync(It.IsAny<SyncJob>(), It.IsAny<SyncJobChange>()), Times.Never);

            // Reset and repeat with a query-changing submission, still with the host disabled.
            _savedChanges.Clear();
            _syncJob.Status = SyncStatus.Idle.ToString();
            var queryPatch = new JsonPatchDocument<SyncJobPatch>();
            queryPatch.Replace(x => x.Query, "[{\"type\":\"GroupMembership\",\"source\":\"33333333-3333-3333-3333-333333333333\"}]");
            await CreateHandler(withQueueRepository: false).ExecuteAsync(UpdateRequest(queryPatch));

            Assert.AreEqual(SyncStatus.PendingReview.ToString(), _syncJob.Status);
            Assert.AreEqual(0, _enqueued.Count);
            Assert.AreEqual(SyncJobChangeReason.Update.ToString(), _savedChanges.Single().ChangeReason);
        }
    }
}
