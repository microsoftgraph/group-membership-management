// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.JsonPatch.Operations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.SyncJobChange;
using Moq;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.WebApi;
using Services.WebApi.Contracts;
using System.Net;
using WebApi.Models.DTOs;
using SyncJob = Models.SyncJob;

namespace WebApi.Tests
{
    [TestClass]
    public class PatchJobHandlerDestinationTests
    {
        [DataTestMethod]
        [DataRow("replace", "/Destination", null)]
        [DataRow("replace", "/destination", null)]
        [DataRow("add", "/TargetOfficeGroupId", null)]
        [DataRow("remove", "/targetofficegroupid", null)]
        [DataRow("replace", "/Destination/value", null)]
        [DataRow("copy", "/Destination", "/Query")]
        [DataRow("move", "/Query", "/Destination")]
        [DataRow("copy", "/Query", "/TargetOfficeGroupId")]
        [DataRow("replace", "", null)]
        public async Task DestinationOperation_IsRejectedBeforeAnyWrites(string op, string path, string? from)
        {
            var jobs = new Mock<IDatabaseSyncJobsRepository>(MockBehavior.Strict);
            var changes = new Mock<ISyncJobChangeRepository>(MockBehavior.Strict);
            var titles = new Mock<IDatabaseTitlesRepository>(MockBehavior.Strict);
            var settings = new Mock<IDatabaseSettingsRepository>(MockBehavior.Strict);
            var notifications = new Mock<INotificationService>(MockBehavior.Strict);
            var job = new SyncJob
            {
                Id = Guid.NewGuid(),
                Group = new Group { GroupId = Guid.NewGuid() },
                MembershipType = MembershipTypes.GroupMembership.ToString(),
                Status = SyncStatus.Idle.ToString()
            };
            jobs.Setup(x => x.GetSyncJobAsync(job.Id)).ReturnsAsync(job);
            var handler = new PatchJobHandler(
                NullLogger<PatchJobHandler>.Instance,
                Mock.Of<IGraphGroupRepository>(), jobs.Object, changes.Object,
                titles.Object, settings.Object, notifications.Object,
                new CapturingTelemetryChannel().CreateClient());
            var patch = new JsonPatchDocument<SyncJobPatch>();
            patch.Replace(x => x.Period, 12);
            patch.Operations.Add(new Operation<SyncJobPatch>(op, path, from, "new-destination"));
            var request = new PatchJobRequest(
                isAllowed: true, isOnBehalfAllowed: false,
                userIdentity: Guid.NewGuid().ToString(), syncJobId: job.Id,
                patchDocument: patch, userDisplayName: "Editor",
                changeReason: SyncJobChangeReason.Update.ToString(),
                businessJustification: "Edit", canApproveJob: false);

            var response = await handler.ExecuteAsync(request);

            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.AreEqual("DestinationCannotBeChanged", response.ErrorCode);
            jobs.Verify(x => x.GetSyncJobAsync(job.Id), Times.Once);
            jobs.VerifyNoOtherCalls();
            changes.VerifyNoOtherCalls();
            titles.VerifyNoOtherCalls();
            settings.VerifyNoOtherCalls();
            notifications.VerifyNoOtherCalls();
        }
    }
}
