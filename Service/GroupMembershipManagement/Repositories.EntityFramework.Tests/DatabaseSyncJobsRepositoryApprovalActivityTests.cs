// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models.Dashboard;
using Models.SyncJobChange;
using Repositories.EntityFramework.Contexts;

namespace Repositories.EntityFramework.Tests
{
    [TestClass]
    public class DatabaseSyncJobsRepositoryApprovalActivityTests
    {
        [DataTestMethod]
        [DataRow(SyncJobChangeReason.OnboardingAutoApproved, false)]
        [DataRow(SyncJobChangeReason.OnboardingAutoApproved, true)]
        [DataRow(SyncJobChangeReason.UpdateAutoApproved, false)]
        [DataRow(SyncJobChangeReason.UpdateAutoApproved, true)]
        public async Task CountsAutoApprovalsWithoutCreatingReviewerSeries(
            SyncJobChangeReason reason, bool hasActor)
        {
            var date = DateTime.UtcNow.Date.AddDays(-1);
            Guid? actorId = hasActor ? Guid.NewGuid() : null;

            var activity = await GetActivityAsync(NewChange(reason, date, actorId, "Automation"));

            Assert.AreEqual(1, activity.AutomatedTotal);
            Assert.AreEqual(1, activity.Days.Single(day => day.Date == date).Automated);
            Assert.AreEqual(1, activity.Days.Sum(day => day.Automated));
            Assert.AreEqual(0, activity.ManualTotal);
            Assert.AreEqual(0, activity.RejectedTotal);
            Assert.AreEqual(0, activity.Reviewers.Count);
            Assert.IsTrue(activity.Days.All(day => day.Reviewers.Count == 0));
        }

        [TestMethod]
        public async Task CombinesAutoApprovalReasonsWhilePreservingManualCountsAndUtcBuckets()
        {
            var recentDate = DateTime.UtcNow.Date.AddDays(-1);
            var earlierDate = recentDate.AddDays(-1);
            var reviewerId = Guid.NewGuid();
            var automationId = Guid.NewGuid();
            var activity = await GetActivityAsync(
                NewChange(SyncJobChangeReason.OnboardingAutoApproved, recentDate.AddHours(2)),
                NewChange(SyncJobChangeReason.UpdateAutoApproved, recentDate.AddHours(18), automationId, "Automation"),
                NewChange(SyncJobChangeReason.UpdateAutoApproved, earlierDate.AddHours(4)),
                NewChange(SyncJobChangeReason.SubmissionApproved, recentDate.AddHours(8), reviewerId, "Contoso Reviewer"),
                NewChange(SyncJobChangeReason.SubmissionRejected, earlierDate.AddHours(9), reviewerId, "Contoso Reviewer"),
                NewChange(SyncJobChangeReason.StatusUpdate, recentDate, Guid.NewGuid(), "Unrelated actor"),
                NewChange(SyncJobChangeReason.UpdateAutoApproved, recentDate.AddDays(-30)));

            Assert.AreEqual(7, activity.WindowDays);
            Assert.AreEqual(7, activity.Days.Count);
            Assert.AreEqual(activity.QueriedAtUtc.Date.AddDays(-6), activity.SinceUtc);
            Assert.AreEqual(3, activity.AutomatedTotal);
            Assert.AreEqual(1, activity.ManualTotal);
            Assert.AreEqual(1, activity.RejectedTotal);
            Assert.AreEqual(1, activity.Reviewers.Count);
            var reviewer = activity.Reviewers.Single();
            Assert.AreEqual(reviewerId.ToString(), reviewer.ReviewerKey);
            Assert.AreEqual("Contoso Reviewer", reviewer.DisplayName);
            Assert.AreEqual(1, reviewer.Approved);
            Assert.AreEqual(1, reviewer.Rejected);
            Assert.IsFalse(reviewer.IsOther);

            var recent = activity.Days.Single(day => day.Date == recentDate);
            var earlier = activity.Days.Single(day => day.Date == earlierDate);
            Assert.AreEqual(2, recent.Automated);
            Assert.AreEqual(1, earlier.Automated);
            Assert.AreEqual(1, recent.Reviewers.Single().Approved);
            Assert.AreEqual(0, recent.Reviewers.Single().Rejected);
            Assert.AreEqual(0, earlier.Reviewers.Single().Approved);
            Assert.AreEqual(1, earlier.Reviewers.Single().Rejected);
            Assert.AreEqual(activity.AutomatedTotal, activity.Days.Sum(day => day.Automated));
            Assert.AreEqual(activity.ManualTotal, activity.Days.Sum(day => day.Reviewers.Sum(row => row.Approved)));
            Assert.AreEqual(activity.RejectedTotal, activity.Days.Sum(day => day.Reviewers.Sum(row => row.Rejected)));
        }

        private static Entities.SyncJobChange NewChange(
            SyncJobChangeReason reason, DateTime time, Guid? actorId = null, string? actorName = null) => new()
        {
            Id = Guid.NewGuid(),
            SyncJobId = Guid.NewGuid(),
            ChangeTime = time,
            ChangeReason = reason.ToString(),
            ChangedByObjectId = actorId,
            ChangedByDisplayName = actorName,
            BusinessJustification = string.Empty
        };

        private static async Task<ApprovalActivity> GetActivityAsync(params Entities.SyncJobChange[] changes)
        {
            var options = new DbContextOptionsBuilder<GMMContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var writeContext = new GMMContext(options);
            await using var readContext = new GMMReadContext(options);
            writeContext.SyncJobChanges.AddRange(changes);
            await writeContext.SaveChangesAsync();
            var repository = new DatabaseSyncJobsRepository(writeContext, readContext);
            return await repository.GetApprovalActivityAsync(7);
        }
    }
}
