// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.JsonPatch.Operations;
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
        private CapturingTelemetryChannel _telemetry = null!;
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
            _telemetry = new CapturingTelemetryChannel();

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
                _telemetry.CreateClient(),
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
        [DataRow(SyncStatus.Idle)]
        [DataRow(SyncStatus.ThresholdExceeded)]
        public async Task Update_ConfigOnly_QueuesApprovalWithoutApprovingInWebApi(SyncStatus status)
        {
            _syncJob.Status = status.ToString();
            var patch = new JsonPatchDocument<SyncJobPatch>();
            patch.Replace(x => x.ThresholdPercentageForAdditions, 42);

            var response = await CreateHandler().ExecuteAsync(UpdateRequest(patch));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(SyncStatus.PendingAutoApproval.ToString(), _syncJob.Status);
            Assert.AreEqual(42, _syncJob.ThresholdPercentageForAdditions, "The submitted config field must be persisted.");
            Assert.AreEqual(1, _savedChanges.Count);
            Assert.AreEqual(SyncJobChangeReason.Update.ToString(), _savedChanges[0].ChangeReason);
            Assert.AreEqual(AutoApprovalOrigin.ConfigUpdate, DecodeEnqueued(_enqueued.Single()).Origin);
            Assert.AreEqual(_syncJob.Query, DecodeEnqueued(_enqueued.Single()).ConfigurationQuery);
            _notifications.Verify(x => x.SendSubmissionApprovedNotificationAsync(_syncJob, It.IsAny<SyncJobChange>()), Times.Never);
            _notifications.Verify(x => x.SendSubmissionRejectedNotificationAsync(It.IsAny<SyncJob>(), It.IsAny<SyncJobChange>()), Times.Never);
            _notifications.Verify(x => x.SendReviewStatusChangeNotificationAsync(It.IsAny<SyncJob>(), It.IsAny<SyncJobChange>(), It.IsAny<NotificationMessageType>()), Times.Never);

            Assert.AreEqual(0, _telemetry.GetMetrics("JobReviewed").Count);
        }

        [TestMethod]
        public async Task Update_RoutedToPendingReview_EmitsNoReviewMetric()
        {
            var patch = new JsonPatchDocument<SyncJobPatch>();
            patch.Replace(x => x.ThresholdPercentageForAdditions, 42);

            await CreateHandler(withQueueRepository: false).ExecuteAsync(UpdateRequest(patch));

            Assert.AreEqual(SyncStatus.PendingReview.ToString(), _syncJob.Status);

            // Routing to a human is not a review decision; the record is emitted once that human decides.
            Assert.AreEqual(0, _telemetry.GetMetrics("JobReviewed").Count);
        }

        [TestMethod]
        public async Task Update_QueryChanged_EnqueuedForAutoApprover_EmitsNoReviewMetric()
        {
            var patch = new JsonPatchDocument<SyncJobPatch>();
            patch.Replace(x => x.Query, "[{\"type\":\"GroupMembership\",\"source\":\"22222222-2222-2222-2222-222222222222\"}]");

            await CreateHandler().ExecuteAsync(UpdateRequest(patch));

            Assert.AreEqual(1, _enqueued.Count);

            // The AutoApprover emits the record once it actually approves, so emitting here would double-count.
            Assert.AreEqual(0, _telemetry.GetMetrics("JobReviewed").Count);
        }

        [TestMethod]
        public async Task Update_ConfigOnly_QueryAbsent_ClassifiedConfigOnly()
        {
            var patch = new JsonPatchDocument<SyncJobPatch>();
            patch.Replace(x => x.Period, 12);

            var response = await CreateHandler().ExecuteAsync(UpdateRequest(patch));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(SyncStatus.PendingAutoApproval.ToString(), _syncJob.Status);
            Assert.AreEqual(12, _syncJob.Period);
            Assert.AreEqual(SyncJobChangeReason.Update.ToString(), _savedChanges.Single().ChangeReason);
            Assert.AreEqual(AutoApprovalOrigin.ConfigUpdate, DecodeEnqueued(_enqueued.Single()).Origin);
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
            Assert.AreEqual(SyncJobChangeReason.Update.ToString(), _savedChanges.Single().ChangeReason);
            Assert.AreEqual(0, _enqueued.Count);
            _notifications.VerifyNoOtherCalls();
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
            Assert.AreEqual(SyncStatus.PendingAutoApproval.ToString(), _syncJob.Status);
            Assert.AreEqual(6, _syncJob.Period);
            Assert.AreEqual(SyncJobChangeReason.Update.ToString(), _savedChanges.Single().ChangeReason);
            Assert.AreEqual(AutoApprovalOrigin.ConfigUpdate, DecodeEnqueued(_enqueued.Single()).Origin);
        }

        [TestMethod]
        [DataRow(SyncStatus.Error)]
        [DataRow(SyncStatus.MembershipDataNotFound)]
        [DataRow(SyncStatus.DestinationGroupNotFound)]
        [DataRow(SyncStatus.NotOwnerOfDestinationGroup)]
        [DataRow(SyncStatus.SourceGroupNotFound)]
        [DataRow(SyncStatus.FilePathNotValid)]
        [DataRow(SyncStatus.QueryNotValid)]
        [DataRow(SyncStatus.FileNotFound)]
        [DataRow(SyncStatus.CustomerPaused)]
        [DataRow(SyncStatus.ErroredDueToStuckInProgress)]
        [DataRow(SyncStatus.TeamsChannelNotDestination)]
        [DataRow(SyncStatus.DestinationQueryNotValid)]
        [DataRow(SyncStatus.GuestUsersCannotBeAddedToUnifiedGroup)]
        [DataRow(SyncStatus.StandardTeamsChannel)]
        [DataRow(SyncStatus.TeamsChannelError)]
        [DataRow(SyncStatus.SchemaError)]
        [DataRow(SyncStatus.TransientError)]
        [DataRow(SyncStatus.DeveloperPaused)]
        [DataRow(SyncStatus.SubmissionRejected)]
        [DataRow(SyncStatus.NestedGroupsFound)]
        [DataRow(SyncStatus.ManagerNotFound)]
        [DataRow(SyncStatus.New)]
        [DataRow(SyncStatus.All)]
        public async Task Update_ConfigOnly_NonApprovedStatus_IsSavedWithoutApproval(SyncStatus status)
        {
            _syncJob.Status = status.ToString();

            var patch = new JsonPatchDocument<SyncJobPatch>();
            patch.Replace(x => x.ThresholdPercentageForAdditions, 15);

            var response = await CreateHandler().ExecuteAsync(UpdateRequest(patch));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            var expectedStatus = status is SyncStatus.New or SyncStatus.All
                ? SyncStatus.PendingReview.ToString() : status.ToString();
            Assert.AreEqual(expectedStatus, _syncJob.Status);
            Assert.AreEqual(15, _syncJob.ThresholdPercentageForAdditions);
            Assert.AreEqual(0, _enqueued.Count);
            Assert.IsFalse(_savedChanges.Any(c => c.ChangeReason == SyncJobChangeReason.UpdateAutoApproved.ToString()),
                "A rejected job must not be auto-approved via the config-only fast path.");
            Assert.AreEqual(SyncJobChangeReason.Update.ToString(), _savedChanges.Single().ChangeReason);
            Assert.AreEqual(0, _telemetry.GetMetrics("JobReviewed").Count);
            _notifications.VerifyNoOtherCalls();
        }

        // ---------- Non-UI fields are accepted but ignored ----------

        [TestMethod]
        [DataRow("/RunId")]
        [DataRow("/Requestor")]
        [DataRow("/AllowEmptyDestination")]
        [DataRow("/LastRunTime")]
        [DataRow("/LastSuccessfulRunTime")]
        [DataRow("/LastSuccessfulStartTime")]
        [DataRow("/IsDryRunEnabled")]
        [DataRow("/DryRunTimeStamp")]
        [DataRow("/ChangeReason")]
        [DataRow("/BusinessJustification")]
        [DataRow("/allowemptydestination")]
        [DataRow("/Requestor/value")]
        public async Task Update_NonUiField_IsIgnoredBeforeClassification(string path)
        {
            _syncJob.Status = SyncStatus.CustomerPaused.ToString();
            var before = JsonSerializer.Serialize(_syncJob);
            var patch = new JsonPatchDocument<SyncJobPatch>();
            patch.Operations.Add(new Operation<SyncJobPatch>
            {
                op = "replace",
                path = path,
                value = "ignored even when invalid for the field type"
            });

            var response = await CreateHandler().ExecuteAsync(UpdateRequest(patch));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(before, JsonSerializer.Serialize(_syncJob));
            Assert.AreEqual(0, _enqueued.Count);
            Assert.AreEqual("config change", _savedChanges.Single().BusinessJustification);
        }

        [TestMethod]
        public async Task Update_DestinationChangeWithoutQueryChange_IsRejected()
        {
            var patch = new JsonPatchDocument<SyncJobPatch>();
            patch.Replace(x => x.Destination, "ignored destination");

            var response = await CreateHandler().ExecuteAsync(UpdateRequest(patch));

            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.AreEqual("DestinationCannotBeChanged", response.ErrorCode);
            Assert.AreEqual(0, _enqueued.Count, "A non-query field change must not be enqueued to the AutoApprover.");
            Assert.IsFalse(_savedChanges.Any(c => c.ChangeReason == SyncJobChangeReason.UpdateAutoApproved.ToString()),
                "A destination change must never be audited as UpdateAutoApproved.");
            Assert.AreEqual(0, _savedChanges.Count);
            _notifications.Verify(x => x.SendSubmissionApprovedNotificationAsync(It.IsAny<SyncJob>(), It.IsAny<SyncJobChange>()), Times.Never);
        }

        [TestMethod]
        public async Task Update_TargetOfficeGroupIdChange_IsRejected()
        {
            var patch = new JsonPatchDocument<SyncJobPatch>();
            patch.Replace(x => x.TargetOfficeGroupId, Guid.NewGuid());

            var response = await CreateHandler().ExecuteAsync(UpdateRequest(patch));

            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.AreEqual("DestinationCannotBeChanged", response.ErrorCode);
            Assert.AreEqual(0, _savedChanges.Count);
            Assert.AreEqual(0, _enqueued.Count);
            Assert.IsFalse(_savedChanges.Any(c => c.ChangeReason == SyncJobChangeReason.UpdateAutoApproved.ToString()));
        }

        [TestMethod]
        public async Task Update_AllowlistedFieldAlongsideDestinationChange_IsRejected()
        {
            var patch = new JsonPatchDocument<SyncJobPatch>();
            patch.Replace(x => x.ThresholdPercentageForAdditions, 30);
            patch.Replace(x => x.Destination, "ignored destination");

            var response = await CreateHandler().ExecuteAsync(UpdateRequest(patch));

            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.AreEqual("DestinationCannotBeChanged", response.ErrorCode);
            Assert.AreEqual(0, _savedChanges.Count);
            Assert.AreEqual(0, _enqueued.Count);
            Assert.IsFalse(_savedChanges.Any(c => c.ChangeReason == SyncJobChangeReason.UpdateAutoApproved.ToString()));
        }

        [TestMethod]
        public async Task Update_QueryChangeAlongsideIgnoredField_QueuesQueryApproval()
        {
            var patch = new JsonPatchDocument<SyncJobPatch>();
            patch.Replace(x => x.Query, "[{\"type\":\"GroupMembership\",\"source\":\"77777777-7777-7777-7777-777777777777\"}]");
            patch.Replace(x => x.AllowEmptyDestination, true);

            var response = await CreateHandler().ExecuteAsync(UpdateRequest(patch));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(SyncStatus.PendingAutoApproval.ToString(), _syncJob.Status);
            Assert.IsFalse(_syncJob.AllowEmptyDestination);
            Assert.AreEqual(AutoApprovalOrigin.Update, DecodeEnqueued(_enqueued.Single()).Origin);
        }

        [TestMethod]
        [DataRow(SyncJobChangeReason.Update, false)]
        [DataRow(SyncJobChangeReason.ScheduledNow, false)]
        [DataRow(SyncJobChangeReason.ScheduledNow, true)]
        public async Task Patch_IgnoredFields_PreserveStoredValuesAcrossWorkflows(SyncJobChangeReason reason, bool withQueue)
        {
            _syncJob.RunId = Guid.NewGuid();
            _syncJob.Destination = "original destination";
            _syncJob.AllowEmptyDestination = true;
            _syncJob.IsDryRunEnabled = true;
            _syncJob.DryRunTimeStamp = new DateTime(2026, 1, 1);
            _syncJob.LastRunTime = new DateTime(2026, 1, 2);
            _syncJob.LastSuccessfulRunTime = new DateTime(2026, 1, 3);
            _syncJob.LastSuccessfulStartTime = new DateTime(2026, 1, 4);
            var before = JsonSerializer.SerializeToElement(_syncJob);
            var ignoredFields = new[]
            {
                "RunId", "Requestor", "AllowEmptyDestination",
                "LastRunTime", "LastSuccessfulRunTime", "LastSuccessfulStartTime",
                "IsDryRunEnabled", "DryRunTimeStamp", "ChangeReason", "BusinessJustification"
            };
            var patch = new JsonPatchDocument<SyncJobPatch>();
            foreach (var field in ignoredFields)
            {
                patch.Operations.Add(new Operation<SyncJobPatch>
                {
                    op = "remove",
                    path = "/" + field
                });
            }
            var scheduledDate = new DateTime(2026, 2, 1);
            patch.Replace(x => x.ScheduledDate, scheduledDate);
            var request = UpdateRequest(patch);
            request.ChangeReason = reason.ToString();

            var response = await CreateHandler(withQueue).ExecuteAsync(request);

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            var after = JsonSerializer.SerializeToElement(_syncJob);
            foreach (var field in ignoredFields.Take(8))
            {
                Assert.AreEqual(before.GetProperty(field).GetRawText(), after.GetProperty(field).GetRawText(), field);
            }
            Assert.AreEqual(scheduledDate, _syncJob.ScheduledDate);
            Assert.AreEqual(reason == SyncJobChangeReason.Update ? nameof(SyncStatus.PendingReview) : nameof(SyncStatus.Idle),
                _syncJob.Status);
            Assert.AreEqual(reason.ToString(), _savedChanges.Single().ChangeReason);
            Assert.AreEqual("config change", _savedChanges.Single().BusinessJustification);
            Assert.AreEqual(0, _enqueued.Count);
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
        [DataRow(false)]
        [DataRow(true)]
        public async Task Update_EnqueueThrows_RevertsToPendingReview(bool configOnly)
        {
            _autoApproverQueue.Setup(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>()))
                              .ThrowsAsync(new InvalidOperationException("service bus down"));

            var newQuery = "[{\"type\":\"GroupMembership\",\"source\":\"22222222-2222-2222-2222-222222222222\"}]";
            var patch = new JsonPatchDocument<SyncJobPatch>();
            patch.Replace(x => x.Query, configOnly ? StoredQuery : newQuery);

            var response = await CreateHandler().ExecuteAsync(UpdateRequest(patch));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(SyncStatus.PendingReview.ToString(), _syncJob.Status, "A failed enqueue must revert the job to PendingReview.");
            // The submitted query must be preserved through the revert.
            Assert.AreEqual(configOnly ? StoredQuery : newQuery, _syncJob.Query);
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
            Assert.AreEqual(SyncStatus.PendingAutoApproval.ToString(), _syncJob.Status);
            Assert.AreEqual(submittedStartDate, _syncJob.StartDate, "A user-submitted StartDate is applied as-is; the handler forces nothing.");
        }

        // ---------- User Story 4: disabled AutoApprover ----------

        [TestMethod]
        [DataRow(SyncStatus.InProgress, "JobInProgress")]
        [DataRow(SyncStatus.StuckInProgress, "JobInProgress")]
        [DataRow(SyncStatus.PendingReview, "JobInPendingReviewStateCannotBeUpdated")]
        [DataRow(SyncStatus.PendingConfiguration, "JobInPendingConfigurationStateCannotBeUpdated")]
        [DataRow(SyncStatus.PendingAutoApproval, "JobInPendingAutoApprovalStateCannotBeUpdated")]
        public async Task Update_ConfigOnly_BlockedStatus_RemainsBlocked(SyncStatus status, string errorCode)
        {
            _syncJob.Status = status.ToString();
            var patch = new JsonPatchDocument<SyncJobPatch>();
            patch.Replace(x => x.Period, 12);

            var response = await CreateHandler().ExecuteAsync(UpdateRequest(patch));

            Assert.AreEqual(HttpStatusCode.PreconditionFailed, response.StatusCode);
            Assert.AreEqual(errorCode, response.ErrorCode);
            Assert.AreEqual(status.ToString(), _syncJob.Status);
            Assert.AreEqual(0, _savedChanges.Count);
            Assert.AreEqual(0, _enqueued.Count);
            _notifications.VerifyNoOtherCalls();
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task Update_ConfigOnly_ErrorStatus_QueueBehavior(bool withQueue)
        {
            _syncJob.Status = SyncStatus.Error.ToString();
            var patch = new JsonPatchDocument<SyncJobPatch>();
            patch.Replace(x => x.Period, 12);

            var response = await CreateHandler(withQueue).ExecuteAsync(UpdateRequest(patch));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(withQueue ? SyncStatus.Error.ToString() : SyncStatus.PendingReview.ToString(), _syncJob.Status);
            Assert.AreEqual(12, _syncJob.Period);
            Assert.AreEqual(SyncJobChangeReason.Update.ToString(), _savedChanges.Single().ChangeReason);
            Assert.AreEqual(0, _enqueued.Count);
            _notifications.VerifyNoOtherCalls();
        }

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
        }

        [TestMethod]
        public async Task Update_NullQueueRepository_QueryAndConfigApprovalsGoToPendingReview()
        {
            // Query change with a null AutoApprover queue repository (host disabled) → PendingReview, no enqueue.
            var queryPatch = new JsonPatchDocument<SyncJobPatch>();
            queryPatch.Replace(x => x.Query, "[{\"type\":\"GroupMembership\",\"source\":\"44444444-4444-4444-4444-444444444444\"}]");
            await CreateHandler(withQueueRepository: false).ExecuteAsync(UpdateRequest(queryPatch));

            Assert.AreEqual(SyncStatus.PendingReview.ToString(), _syncJob.Status,
                "With a null queue repository a query change must go to PendingReview, never left in PendingAutoApproval.");
            Assert.AreEqual(0, _enqueued.Count);

            // Config-only approvals also require the AutoApprover host.
            _savedChanges.Clear();
            _syncJob.Status = SyncStatus.Idle.ToString();
            var configPatch = new JsonPatchDocument<SyncJobPatch>();
            configPatch.Replace(x => x.ThresholdPercentageForAdditions, 77);
            await CreateHandler(withQueueRepository: false).ExecuteAsync(UpdateRequest(configPatch));

            Assert.AreEqual(SyncStatus.PendingReview.ToString(), _syncJob.Status);
            Assert.AreEqual(SyncJobChangeReason.Update.ToString(), _savedChanges.Single().ChangeReason);
            _notifications.VerifyNoOtherCalls();
        }
    }
}
