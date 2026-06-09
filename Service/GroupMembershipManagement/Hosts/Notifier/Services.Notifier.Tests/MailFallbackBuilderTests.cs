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
            // The href must be HTML-encoded and not contain the raw javascript: scheme as a link
            Assert.IsFalse(html.Contains("href=\"javascript:"), "javascript: URL must not appear as an href");
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
    }
}
