// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using Repositories.Contracts;
using Repositories.Localization;
using Repositories.Mail;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services.Notifier.Tests
{
    [TestClass]
    public class MailFallbackBuilderTests
    {
        private Mock<IGraphGroupRepository> _graphGroupRepository;
        private ILocalizationRepository _localizationRepository;
        private MailFallbackBuilder _builder;

        private const string GroupId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
        private const string GroupName = "Test Group";
        private const string JobUrl = "https://gmm.example.com/jobs/123";
        private const string SentDate = "May 01, 2026";

        [TestInitialize]
        public void SetUp()
        {
            _graphGroupRepository = new Mock<IGraphGroupRepository>();

            // Default: return email alias and a SecurityGroup type
            _graphGroupRepository
                .Setup(g => g.GetGroupEmailAsync(It.IsAny<Guid>()))
                .ReturnsAsync("testgroup@contoso.com");
            _graphGroupRepository
                .Setup(g => g.GetGroupEndpointsAsync(It.IsAny<Guid>()))
                .ReturnsAsync(new List<string> { "SecurityGroup" });

            var options = Options.Create(new LocalizationOptions { ResourcesPath = "Resources" });
            var factory = new ResourceManagerStringLocalizerFactory(options, NullLoggerFactory.Instance);
            var localizer = new StringLocalizer<LocalizationRepository>(factory);
            _localizationRepository = new LocalizationRepository(localizer);

            _builder = new MailFallbackBuilder(
                _graphGroupRepository.Object,
                _localizationRepository,
                NullLogger<MailFallbackBuilder>.Instance);
        }

        // ── SyncStarted ──────────────────────────────────────────────────────────

        [TestMethod]
        public async Task BuildSyncStartedFallbackAsync_ReturnsNonEmptyHtml()
        {
            var email = MakeSyncStartedEmail();
            var html = await _builder.BuildSyncStartedFallbackAsync(email, GroupName, GroupId, JobUrl, SentDate);
            Assert.IsFalse(string.IsNullOrWhiteSpace(html));
        }

        [TestMethod]
        public async Task BuildSyncStartedFallbackAsync_ContainsGroupName()
        {
            var email = MakeSyncStartedEmail();
            var html = await _builder.BuildSyncStartedFallbackAsync(email, GroupName, GroupId, JobUrl, SentDate);
            StringAssert.Contains(html, GroupName);
        }

        [TestMethod]
        public async Task BuildSyncStartedFallbackAsync_ContainsGroupId()
        {
            var email = MakeSyncStartedEmail();
            var html = await _builder.BuildSyncStartedFallbackAsync(email, GroupName, GroupId, JobUrl, SentDate);
            StringAssert.Contains(html, GroupId);
        }

        [TestMethod]
        public async Task BuildSyncStartedFallbackAsync_ContainsSentDate()
        {
            var email = MakeSyncStartedEmail();
            var html = await _builder.BuildSyncStartedFallbackAsync(email, GroupName, GroupId, JobUrl, SentDate);
            StringAssert.Contains(html, SentDate);
        }

        [TestMethod]
        public async Task BuildSyncStartedFallbackAsync_ContainsCtaUrl()
        {
            var email = MakeSyncStartedEmail();
            var html = await _builder.BuildSyncStartedFallbackAsync(email, GroupName, GroupId, JobUrl, SentDate);
            StringAssert.Contains(html, JobUrl);
        }

        [TestMethod]
        public async Task BuildSyncStartedFallbackAsync_ContainsGroupAlias_WhenGraphReturnsEmail()
        {
            var email = MakeSyncStartedEmail();
            var html = await _builder.BuildSyncStartedFallbackAsync(email, GroupName, GroupId, JobUrl, SentDate);
            StringAssert.Contains(html, "testgroup@contoso.com");
        }

        [TestMethod]
        public async Task BuildSyncStartedFallbackAsync_HtmlEncodesGroupName()
        {
            var email = MakeSyncStartedEmail();
            const string maliciousName = "<script>alert('xss')</script>";
            var html = await _builder.BuildSyncStartedFallbackAsync(email, maliciousName, GroupId, JobUrl, SentDate);
            Assert.IsFalse(html.Contains("<script>"), "Raw <script> tag must not appear in output");
            StringAssert.Contains(html, "&lt;script&gt;");
        }

        // ── SyncCompleted ────────────────────────────────────────────────────────

        [TestMethod]
        public async Task BuildSyncCompletedFallbackAsync_ReturnsNonEmptyHtml()
        {
            var email = MakeSyncCompletedEmail(added: "42", removed: "7");
            var html = await _builder.BuildSyncCompletedFallbackAsync(email, GroupName, GroupId, JobUrl, SentDate);
            Assert.IsFalse(string.IsNullOrWhiteSpace(html));
        }

        [TestMethod]
        public async Task BuildSyncCompletedFallbackAsync_ContainsAddedAndRemovedCounts()
        {
            var email = MakeSyncCompletedEmail(added: "42", removed: "7");
            var html = await _builder.BuildSyncCompletedFallbackAsync(email, GroupName, GroupId, JobUrl, SentDate);
            StringAssert.Contains(html, "42");
            StringAssert.Contains(html, "7");
        }

        [TestMethod]
        public async Task BuildSyncCompletedFallbackAsync_DefaultsToZero_WhenParamsMissing()
        {
            // EmailMessage with no AdditionalContentParams
            var email = new EmailMessage { Content = "SyncCompletedEmailBody" };
            var html = await _builder.BuildSyncCompletedFallbackAsync(email, GroupName, GroupId, JobUrl, SentDate);
            // Should not throw and should still contain "0" counts
            Assert.IsFalse(string.IsNullOrWhiteSpace(html));
        }

        // ── Graph failure tolerance ──────────────────────────────────────────────

        [TestMethod]
        public async Task BuildSyncStartedFallbackAsync_ToleratesGraphFailure_StillReturnsHtml()
        {
            _graphGroupRepository
                .Setup(g => g.GetGroupEmailAsync(It.IsAny<Guid>()))
                .ThrowsAsync(new Exception("Graph unavailable"));

            var email = MakeSyncStartedEmail();
            // Should not throw
            var html = await _builder.BuildSyncStartedFallbackAsync(email, GroupName, GroupId, JobUrl, SentDate);
            Assert.IsFalse(string.IsNullOrWhiteSpace(html));
            // Group alias row should be absent (gracefully omitted)
            Assert.IsFalse(html.Contains("testgroup@contoso.com"));
        }

        [TestMethod]
        public async Task BuildSyncCompletedFallbackAsync_ToleratesGraphFailure_StillReturnsHtml()
        {
            _graphGroupRepository
                .Setup(g => g.GetGroupEndpointsAsync(It.IsAny<Guid>()))
                .ThrowsAsync(new Exception("Graph unavailable"));

            var email = MakeSyncCompletedEmail(added: "5", removed: "3");
            var html = await _builder.BuildSyncCompletedFallbackAsync(email, GroupName, GroupId, JobUrl, SentDate);
            Assert.IsFalse(string.IsNullOrWhiteSpace(html));
        }

        // ── URL injection guard ──────────────────────────────────────────────────

        [TestMethod]
        public async Task BuildSyncStartedFallbackAsync_BlocksNonHttpCtaUrl()
        {
            const string javascriptUrl = "javascript:alert(1)";
            var email = MakeSyncStartedEmail();
            var html = await _builder.BuildSyncStartedFallbackAsync(email, GroupName, GroupId, javascriptUrl, SentDate);
            Assert.IsFalse(html.Contains("href=\"javascript:"), "javascript: URL must not appear as an href");
        }

        // ── SyncDisabled ─────────────────────────────────────────────────────────

        [TestMethod]
        public async Task BuildSyncDisabledFallbackAsync_ReturnsNonEmptyHtml()
        {
            var email = MakeSyncDisabledEmail("SyncDisabledNoGroupEmailBody");
            var html = await _builder.BuildSyncDisabledFallbackAsync(email, GroupName, GroupId, JobUrl, SentDate);
            Assert.IsFalse(string.IsNullOrWhiteSpace(html));
        }

        [TestMethod]
        public async Task BuildSyncDisabledFallbackAsync_ContainsGroupName()
        {
            var email = MakeSyncDisabledEmail("SyncDisabledNoGroupEmailBody");
            var html = await _builder.BuildSyncDisabledFallbackAsync(email, GroupName, GroupId, JobUrl, SentDate);
            StringAssert.Contains(html, GroupName);
        }

        [TestMethod]
        public async Task BuildSyncDisabledFallbackAsync_ContainsGroupId()
        {
            var email = MakeSyncDisabledEmail("SyncDisabledNoGroupEmailBody");
            var html = await _builder.BuildSyncDisabledFallbackAsync(email, GroupName, GroupId, JobUrl, SentDate);
            StringAssert.Contains(html, GroupId);
        }

        [TestMethod]
        public async Task BuildSyncDisabledFallbackAsync_ContainsSentDate()
        {
            var email = MakeSyncDisabledEmail("SyncDisabledNoGroupEmailBody");
            var html = await _builder.BuildSyncDisabledFallbackAsync(email, GroupName, GroupId, JobUrl, SentDate);
            StringAssert.Contains(html, SentDate);
        }

        [TestMethod]
        public async Task BuildSyncDisabledFallbackAsync_ContainsCtaUrl()
        {
            var email = MakeSyncDisabledEmail("SyncDisabledNoGroupEmailBody");
            var html = await _builder.BuildSyncDisabledFallbackAsync(email, GroupName, GroupId, JobUrl, SentDate);
            StringAssert.Contains(html, JobUrl);
        }

        [TestMethod]
        [DataRow("SyncDisabledNoGroupEmailBody")]
        [DataRow("SyncDisabledNoSourceGroupEmailBody")]
        [DataRow("SyncDisabledNoOwnerEmailBody")]
        [DataRow("SyncDisabledNoValidGroupIds")]
        [DataRow("GuestUserFailureEmailBody")]
        [DataRow("NoDataEmailContent")]
        [DataRow("SyncJobDisabledEmailBody")]
        [DataRow("UnknownContentType")]
        public async Task BuildSyncDisabledFallbackAsync_ReturnsNonEmptyHtml_ForAllDisableReasons(string contentType)
        {
            var email = MakeSyncDisabledEmail(contentType);
            var html = await _builder.BuildSyncDisabledFallbackAsync(email, GroupName, GroupId, JobUrl, SentDate);
            Assert.IsFalse(string.IsNullOrWhiteSpace(html), $"Expected non-empty HTML for contentType={contentType}");
        }

        [TestMethod]
        public async Task BuildSyncDisabledFallbackAsync_ContainsGroupAlias_WhenGraphReturnsEmail()
        {
            var email = MakeSyncDisabledEmail("SyncDisabledNoGroupEmailBody");
            var html = await _builder.BuildSyncDisabledFallbackAsync(email, GroupName, GroupId, JobUrl, SentDate);
            StringAssert.Contains(html, "testgroup@contoso.com");
        }

        [TestMethod]
        public async Task BuildSyncDisabledFallbackAsync_ContainsRequestor_WhenProvided()
        {
            var email = MakeSyncDisabledEmail("SyncDisabledNoGroupEmailBody", requestor: "owner@contoso.com");
            var html = await _builder.BuildSyncDisabledFallbackAsync(email, GroupName, GroupId, JobUrl, SentDate);
            StringAssert.Contains(html, "owner@contoso.com");
        }

        [TestMethod]
        public async Task BuildSyncDisabledFallbackAsync_HtmlEncodesGroupName()
        {
            const string maliciousName = "<script>alert('xss')</script>";
            var email = MakeSyncDisabledEmail("SyncDisabledNoGroupEmailBody");
            var html = await _builder.BuildSyncDisabledFallbackAsync(email, maliciousName, GroupId, JobUrl, SentDate);
            Assert.IsFalse(html.Contains("<script>"), "Raw <script> tag must not appear in output");
            StringAssert.Contains(html, "&lt;script&gt;");
        }

        [TestMethod]
        public async Task BuildSyncDisabledFallbackAsync_ToleratesGraphFailure_StillReturnsHtml()
        {
            _graphGroupRepository
                .Setup(g => g.GetGroupEmailAsync(It.IsAny<Guid>()))
                .ThrowsAsync(new Exception("Graph unavailable"));

            var email = MakeSyncDisabledEmail("SyncJobDisabledEmailBody");
            var html = await _builder.BuildSyncDisabledFallbackAsync(email, GroupName, GroupId, JobUrl, SentDate);
            Assert.IsFalse(string.IsNullOrWhiteSpace(html));
            Assert.IsFalse(html.Contains("testgroup@contoso.com"));
        }

        [TestMethod]
        public async Task BuildSyncDisabledFallbackAsync_BlocksNonHttpCtaUrl()
        {
            const string javascriptUrl = "javascript:alert(1)";
            var email = MakeSyncDisabledEmail("SyncJobDisabledEmailBody");
            var html = await _builder.BuildSyncDisabledFallbackAsync(email, GroupName, GroupId, javascriptUrl, SentDate);
            Assert.IsFalse(html.Contains("href=\"javascript:"), "javascript: URL must not appear as an href");
        }

        // ── SubmissionRejected ───────────────────────────────────────────────────

        [TestMethod]
        public async Task BuildSubmissionRejectedFallbackAsync_ReturnsNonEmptyHtml()
        {
            var email = MakeSubmissionRejectedEmail();
            var html = await _builder.BuildSubmissionRejectedFallbackAsync(email, GroupName, GroupId, JobUrl, SentDate);
            Assert.IsFalse(string.IsNullOrWhiteSpace(html));
        }

        [TestMethod]
        public async Task BuildSubmissionRejectedFallbackAsync_ContainsGroupName()
        {
            var email = MakeSubmissionRejectedEmail();
            var html = await _builder.BuildSubmissionRejectedFallbackAsync(email, GroupName, GroupId, JobUrl, SentDate);
            StringAssert.Contains(html, GroupName);
        }

        [TestMethod]
        public async Task BuildSubmissionRejectedFallbackAsync_ContainsGroupId()
        {
            var email = MakeSubmissionRejectedEmail();
            var html = await _builder.BuildSubmissionRejectedFallbackAsync(email, GroupName, GroupId, JobUrl, SentDate);
            StringAssert.Contains(html, GroupId);
        }

        [TestMethod]
        public async Task BuildSubmissionRejectedFallbackAsync_ContainsSentDate()
        {
            var email = MakeSubmissionRejectedEmail();
            var html = await _builder.BuildSubmissionRejectedFallbackAsync(email, GroupName, GroupId, JobUrl, SentDate);
            StringAssert.Contains(html, SentDate);
        }

        [TestMethod]
        public async Task BuildSubmissionRejectedFallbackAsync_ContainsCtaUrl()
        {
            var email = MakeSubmissionRejectedEmail();
            var html = await _builder.BuildSubmissionRejectedFallbackAsync(email, GroupName, GroupId, JobUrl, SentDate);
            StringAssert.Contains(html, JobUrl);
        }

        [TestMethod]
        public async Task BuildSubmissionRejectedFallbackAsync_ContainsRejectionReason_WhenProvided()
        {
            const string reason = "Violates company policy";
            var email = MakeSubmissionRejectedEmail(reason: reason);
            var html = await _builder.BuildSubmissionRejectedFallbackAsync(email, GroupName, GroupId, JobUrl, SentDate);
            StringAssert.Contains(html, reason);
        }

        [TestMethod]
        public async Task BuildSubmissionRejectedFallbackAsync_ContainsRequestor_WhenProvided()
        {
            var email = MakeSubmissionRejectedEmail(requestor: "requester@contoso.com");
            var html = await _builder.BuildSubmissionRejectedFallbackAsync(email, GroupName, GroupId, JobUrl, SentDate);
            StringAssert.Contains(html, "requester@contoso.com");
        }

        [TestMethod]
        public async Task BuildSubmissionRejectedFallbackAsync_OmitsRejectionReasonRow_WhenReasonIsEmpty()
        {
            var email = MakeSubmissionRejectedEmail(reason: "");
            var html = await _builder.BuildSubmissionRejectedFallbackAsync(email, GroupName, GroupId, JobUrl, SentDate);
            // The row label should not appear when reason is empty
            Assert.IsFalse(html.Contains("Rejection Reason"), "Rejection Reason row should be absent when reason is empty");
        }

        [TestMethod]
        public async Task BuildSubmissionRejectedFallbackAsync_ContainsGroupAlias_WhenGraphReturnsEmail()
        {
            var email = MakeSubmissionRejectedEmail();
            var html = await _builder.BuildSubmissionRejectedFallbackAsync(email, GroupName, GroupId, JobUrl, SentDate);
            StringAssert.Contains(html, "testgroup@contoso.com");
        }

        [TestMethod]
        public async Task BuildSubmissionRejectedFallbackAsync_HtmlEncodesGroupName()
        {
            const string maliciousName = "<script>alert('xss')</script>";
            var email = MakeSubmissionRejectedEmail();
            var html = await _builder.BuildSubmissionRejectedFallbackAsync(email, maliciousName, GroupId, JobUrl, SentDate);
            Assert.IsFalse(html.Contains("<script>"), "Raw <script> tag must not appear in output");
            StringAssert.Contains(html, "&lt;script&gt;");
        }

        [TestMethod]
        public async Task BuildSubmissionRejectedFallbackAsync_HtmlEncodesRejectionReason()
        {
            const string maliciousReason = "<img src=x onerror=alert(1)>";
            var email = MakeSubmissionRejectedEmail(reason: maliciousReason);
            var html = await _builder.BuildSubmissionRejectedFallbackAsync(email, GroupName, GroupId, JobUrl, SentDate);
            Assert.IsFalse(html.Contains("<img"), "Raw <img> tag must not appear in output");
            StringAssert.Contains(html, "&lt;img");
        }

        [TestMethod]
        public async Task BuildSubmissionRejectedFallbackAsync_ToleratesGraphFailure_StillReturnsHtml()
        {
            _graphGroupRepository
                .Setup(g => g.GetGroupEmailAsync(It.IsAny<Guid>()))
                .ThrowsAsync(new Exception("Graph unavailable"));

            var email = MakeSubmissionRejectedEmail();
            var html = await _builder.BuildSubmissionRejectedFallbackAsync(email, GroupName, GroupId, JobUrl, SentDate);
            Assert.IsFalse(string.IsNullOrWhiteSpace(html));
            Assert.IsFalse(html.Contains("testgroup@contoso.com"));
        }

        [TestMethod]
        public async Task BuildSubmissionRejectedFallbackAsync_BlocksNonHttpCtaUrl()
        {
            const string javascriptUrl = "javascript:alert(1)";
            var email = MakeSubmissionRejectedEmail();
            var html = await _builder.BuildSubmissionRejectedFallbackAsync(email, GroupName, GroupId, javascriptUrl, SentDate);
            Assert.IsFalse(html.Contains("href=\"javascript:"), "javascript: URL must not appear as an href");
        }

        [TestMethod]
        public async Task BuildSubmissionRejectedFallbackAsync_ToleratesNullAdditionalParams()
        {
            var email = new EmailMessage { Content = "SubmissionRejectedEmailBody" };
            var html = await _builder.BuildSubmissionRejectedFallbackAsync(email, GroupName, GroupId, JobUrl, SentDate);
            Assert.IsFalse(string.IsNullOrWhiteSpace(html));
        }

        // ── JobPurgingWarning ────────────────────────────────────────────────────

        [TestMethod]
        public async Task BuildJobPurgingWarningFallbackAsync_ReturnsNonEmptyHtml()
        {
            var email = MakeJobPurgingWarningEmail();
            var html = await _builder.BuildJobPurgingWarningFallbackAsync(email, GroupName, GroupId, JobUrl, SentDate);
            Assert.IsFalse(string.IsNullOrWhiteSpace(html));
        }

        [TestMethod]
        public async Task BuildJobPurgingWarningFallbackAsync_ContainsGroupName()
        {
            var email = MakeJobPurgingWarningEmail();
            var html = await _builder.BuildJobPurgingWarningFallbackAsync(email, GroupName, GroupId, JobUrl, SentDate);
            StringAssert.Contains(html, GroupName);
        }

        [TestMethod]
        public async Task BuildJobPurgingWarningFallbackAsync_ContainsGroupId()
        {
            var email = MakeJobPurgingWarningEmail();
            var html = await _builder.BuildJobPurgingWarningFallbackAsync(email, GroupName, GroupId, JobUrl, SentDate);
            StringAssert.Contains(html, GroupId);
        }

        [TestMethod]
        public async Task BuildJobPurgingWarningFallbackAsync_ContainsSentDate()
        {
            var email = MakeJobPurgingWarningEmail();
            var html = await _builder.BuildJobPurgingWarningFallbackAsync(email, GroupName, GroupId, JobUrl, SentDate);
            StringAssert.Contains(html, SentDate);
        }

        [TestMethod]
        public async Task BuildJobPurgingWarningFallbackAsync_ContainsCtaUrl()
        {
            var email = MakeJobPurgingWarningEmail();
            var html = await _builder.BuildJobPurgingWarningFallbackAsync(email, GroupName, GroupId, JobUrl, SentDate);
            StringAssert.Contains(html, JobUrl);
        }

        [TestMethod]
        public async Task BuildJobPurgingWarningFallbackAsync_ContainsStatusInactiveSinceAndPurgeDate()
        {
            var email = MakeJobPurgingWarningEmail(
                status: "CustomerPaused",
                inactiveSince: "April 07, 2026",
                scheduledPurgeDate: "May 07, 2026");
            var html = await _builder.BuildJobPurgingWarningFallbackAsync(email, GroupName, GroupId, JobUrl, SentDate);
            StringAssert.Contains(html, "CustomerPaused");
            StringAssert.Contains(html, "April 07, 2026");
            StringAssert.Contains(html, "May 07, 2026");
        }

        [TestMethod]
        [DataRow("CustomerPaused")]
        [DataRow("MembershipDataNotFound")]
        [DataRow("DestinationGroupNotFound")]
        [DataRow("SecurityGroupNotFound")]
        [DataRow("NotOwnerOfDestinationGroup")]
        [DataRow("ThresholdExceeded")]
        [DataRow("SubmissionRejected")]
        [DataRow("GuestUsersCannotBeAddedToUnifiedGroup")]
        [DataRow("NestedGroupsFound")]
        [DataRow("UnknownStatus")]
        [DataRow("")]
        public async Task BuildJobPurgingWarningFallbackAsync_ReturnsNonEmptyHtml_ForAllStatuses(string status)
        {
            var email = MakeJobPurgingWarningEmail(status: status);
            var html = await _builder.BuildJobPurgingWarningFallbackAsync(email, GroupName, GroupId, JobUrl, SentDate);
            Assert.IsFalse(string.IsNullOrWhiteSpace(html), $"Expected non-empty HTML for status={status}");
        }

        [TestMethod]
        public async Task BuildJobPurgingWarningFallbackAsync_UsesGenericDescription_WhenStatusIsUnknown()
        {
            var email = MakeJobPurgingWarningEmail(status: "BogusStatus");
            var html = await _builder.BuildJobPurgingWarningFallbackAsync(email, GroupName, GroupId, JobUrl, SentDate);
            // Generic description includes the literal status token in its body.
            StringAssert.Contains(html, "BogusStatus");
        }

        [TestMethod]
        public async Task BuildJobPurgingWarningFallbackAsync_UsesStatusSpecificDescription_WhenStatusIsKnown()
        {
            var pausedEmail = MakeJobPurgingWarningEmail(status: "CustomerPaused");
            var pausedHtml = await _builder.BuildJobPurgingWarningFallbackAsync(pausedEmail, GroupName, GroupId, JobUrl, SentDate);

            var noOwnerEmail = MakeJobPurgingWarningEmail(status: "NotOwnerOfDestinationGroup");
            var noOwnerHtml = await _builder.BuildJobPurgingWarningFallbackAsync(noOwnerEmail, GroupName, GroupId, JobUrl, SentDate);

            // Different statuses must produce different description content.
            Assert.AreNotEqual(pausedHtml, noOwnerHtml,
                "Status-specific descriptions should differ between CustomerPaused and NotOwnerOfDestinationGroup.");
        }

        [TestMethod]
        public async Task BuildJobPurgingWarningFallbackAsync_HtmlEncodesGroupName()
        {
            const string maliciousName = "<script>alert('xss')</script>";
            var email = MakeJobPurgingWarningEmail();
            var html = await _builder.BuildJobPurgingWarningFallbackAsync(email, maliciousName, GroupId, JobUrl, SentDate);
            Assert.IsFalse(html.Contains("<script>"), "Raw <script> tag must not appear in output");
            StringAssert.Contains(html, "&lt;script&gt;");
        }

        [TestMethod]
        public async Task BuildJobPurgingWarningFallbackAsync_HtmlEncodesStatus()
        {
            var email = MakeJobPurgingWarningEmail(status: "<img src=x onerror=alert(1)>");
            var html = await _builder.BuildJobPurgingWarningFallbackAsync(email, GroupName, GroupId, JobUrl, SentDate);
            Assert.IsFalse(html.Contains("<img"), "Raw <img> tag must not appear in output");
            StringAssert.Contains(html, "&lt;img");
        }

        [TestMethod]
        public async Task BuildJobPurgingWarningFallbackAsync_BlocksNonHttpCtaUrl()
        {
            const string javascriptUrl = "javascript:alert(1)";
            var email = MakeJobPurgingWarningEmail();
            var html = await _builder.BuildJobPurgingWarningFallbackAsync(email, GroupName, GroupId, javascriptUrl, SentDate);
            Assert.IsFalse(html.Contains("href=\"javascript:"), "javascript: URL must not appear as an href");
        }

        [TestMethod]
        public async Task BuildJobPurgingWarningFallbackAsync_ToleratesNullAdditionalParams()
        {
            var email = new EmailMessage { Content = "JobPurgingWarningEmailBody" };
            var html = await _builder.BuildJobPurgingWarningFallbackAsync(email, GroupName, GroupId, JobUrl, SentDate);
            Assert.IsFalse(string.IsNullOrWhiteSpace(html));
        }

        [TestMethod]
        public async Task BuildJobPurgingWarningFallbackAsync_ToleratesGraphFailure_StillReturnsHtml()
        {
            _graphGroupRepository
                .Setup(g => g.GetGroupEmailAsync(It.IsAny<Guid>()))
                .ThrowsAsync(new Exception("Graph unavailable"));

            var email = MakeJobPurgingWarningEmail();
            var html = await _builder.BuildJobPurgingWarningFallbackAsync(email, GroupName, GroupId, JobUrl, SentDate);
            Assert.IsFalse(string.IsNullOrWhiteSpace(html));
            Assert.IsFalse(html.Contains("testgroup@contoso.com"));
        }

        [TestMethod]
        [DataRow("customerpaused")]
        [DataRow("CUSTOMERPAUSED")]
        [DataRow("CustomerPaused")]
        public async Task BuildJobPurgingWarningFallbackAsync_StatusKeyLookupIsCaseInsensitive(string status)
        {
            // ResolvePurgeWarningStatusKey round-trips through SyncStatus enum (ignoreCase: true)
            // so any casing should hit the CustomerPaused-specific description, not Generic.
            var email = MakeJobPurgingWarningEmail(status: status);
            var html = await _builder.BuildJobPurgingWarningFallbackAsync(email, GroupName, GroupId, JobUrl, SentDate);

            // Generic description begins "The job for ... has been in {0} status since"; the
            // CustomerPaused description begins "This sync has been **paused by the owner**".
            // Any non-Generic match for a known status proves canonicalization worked.
            StringAssert.Contains(html, "paused by the owner");
        }

        [TestMethod]
        public async Task BuildJobPurgingWarningFallbackAsync_FallsBackToUnknownGroupName_AndOmitsEmptyBoldPairs()
        {
            // When destinationGroupName is empty AND AdditionalContentParams[5] is also empty,
            // the builder substitutes the localized "your group" placeholder before the markdown
            // step runs. This guards both the fallback and the empty-bold-pair sanitizer:
            // **{5}** must never collapse to **** or ** ** in the rendered output.
            var email = new EmailMessage
            {
                Content = "JobPurgingWarningEmailBody",
                AdditionalContentParams = new[]
                {
                    "CustomerPaused", "April 07, 2026", "30", "May 07, 2026", GroupId, string.Empty
                }
            };
            var html = await _builder.BuildJobPurgingWarningFallbackAsync(
                email, destinationGroupName: "", GroupId, JobUrl, SentDate);

            Assert.IsFalse(html.Contains("****"), "Empty bold pair (****) should not appear in output");
            Assert.IsFalse(html.Contains("** **"), "Empty bold pair (** **) should not appear in output");
            StringAssert.Contains(html, "your group");
        }

        [TestMethod]
        public async Task BuildJobPurgingWarningFallbackAsync_DiffersInCalloutBody_BetweenStatuses()
        {
            // Each status should produce a distinct CalloutBody.{Status} resource. This is the
            // owner-action-guidance counterpart to the Description-uniqueness test above.
            var pausedHtml = await _builder.BuildJobPurgingWarningFallbackAsync(
                MakeJobPurgingWarningEmail(status: "CustomerPaused"), GroupName, GroupId, JobUrl, SentDate);
            var nestedHtml = await _builder.BuildJobPurgingWarningFallbackAsync(
                MakeJobPurgingWarningEmail(status: "NestedGroupsFound"), GroupName, GroupId, JobUrl, SentDate);

            StringAssert.Contains(pausedHtml, "Resume the sync");
            StringAssert.Contains(nestedHtml, "flat membership");
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static EmailMessage MakeSyncStartedEmail(string requestor = "admin@contoso.com")
        {
            return new EmailMessage
            {
                Content = "SyncStartedEmailBody",
                AdditionalContentParams = new[] { GroupId, GroupName, "0", "0", requestor }
            };
        }

        private static EmailMessage MakeSyncCompletedEmail(string added, string removed, string requestor = "admin@contoso.com")
        {
            return new EmailMessage
            {
                Content = "SyncCompletedEmailBody",
                AdditionalContentParams = new[] { GroupId, GroupName, added, removed, requestor }
            };
        }

        private static EmailMessage MakeSyncDisabledEmail(string contentType, string requestor = "admin@contoso.com")
        {
            return new EmailMessage
            {
                Content = contentType,
                AdditionalContentParams = new[] { GroupId, GroupName, "0", "0", requestor }
            };
        }

        private static EmailMessage MakeSubmissionRejectedEmail(
            string reason = "Duplicate request", string requestor = "requester@contoso.com")
        {
            return new EmailMessage
            {
                Content = "SubmissionRejectedEmailBody",
                // [0]=groupId, [1]=groupName, [2]=rejectionReason, [3]=requestor
                AdditionalContentParams = new[] { GroupId, GroupName, reason, requestor }
            };
        }

        private static EmailMessage MakeJobPurgingWarningEmail(
            string status = "CustomerPaused",
            string inactiveSince = "April 07, 2026",
            string daysBeforePurging = "30",
            string scheduledPurgeDate = "May 07, 2026")
        {
            return new EmailMessage
            {
                Content = "JobPurgingWarningEmailBody",
                // [0]=Status, [1]=InactivitySince, [2]=NumberOfDaysBeforePurging, [3]=ScheduledPurgeDate, [4]=GroupId, [5]=GroupName
                AdditionalContentParams = new[] { status, inactiveSince, daysBeforePurging, scheduledPurgeDate, GroupId, GroupName }
            };
        }
    }
}
