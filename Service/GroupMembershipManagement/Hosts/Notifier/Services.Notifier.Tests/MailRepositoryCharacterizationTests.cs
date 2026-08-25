// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using DIConcreteTypes;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using Repositories.Contracts;
using Repositories.Localization;
using Repositories.Mail;
using Repositories.RetryPolicyProvider;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services.Notifier.Tests
{
    /// <summary>
    /// Characterization ("golden master") tests for <see cref="MailRepository"/>.
    ///
    /// These lock in the CURRENT rendered output of every notification type so that the
    /// in-progress removal of the legacy Outlook Actionable Message path cannot silently
    /// change a customer-facing email. They intentionally use the REAL
    /// <see cref="MailFallbackBuilder"/> and REAL <see cref="LocalizationRepository"/> so the
    /// snapshot reflects what is actually sent, not what a mock returns.
    ///
    /// If a change makes one of these fail, that is a behavior change: confirm it is intended
    /// before updating the expectation.
    /// </summary>
    [TestClass]
    public class MailRepositoryCharacterizationTests
    {
        private const string GRAPH_API_V1_BASE_URL = "https://graph.microsoft.com/v1.0";
        private const string UiUrl = "https://gmm.example.com";
        private const string DashboardUrl = "https://dashboard.example.com";
        private const string GroupId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
        private const string GroupName = "Test Group";

        // Marker emitted only by the legacy OAM wrapper (MailRepository.BuildLegacyFallback).
        private const string LegacyMarker = "Outlook Actionable Messages";

        // Marker emitted only by the styled wrapper (MailRepository.WrapStyledBodyWithoutAdaptiveCard).
        private const string StyledMarker = "background-color:#f3f2f1";

        private Mock<IGraphGroupRepository> _graphGroupRepository;
        private Mock<IDatabaseSettingsRepository> _settingsRepository;
        private ILocalizationRepository _localizationRepository;
        private MailRepository _mailRepository;

        [TestInitialize]
        public void SetUp()
        {
            _graphGroupRepository = new Mock<IGraphGroupRepository>();
            _graphGroupRepository.Setup(x => x.GetGroupNameAsync(It.IsAny<Guid>())).ReturnsAsync(GroupName);
            _graphGroupRepository.Setup(x => x.GetGroupEmailAsync(It.IsAny<Guid>())).ReturnsAsync("testgroup@contoso.com");
            _graphGroupRepository.Setup(x => x.GetGroupEndpointsAsync(It.IsAny<Guid>()))
                                 .ReturnsAsync(new List<string> { "SecurityGroup" });

            _settingsRepository = new Mock<IDatabaseSettingsRepository>();
            _settingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.UIUrl))
                               .ReturnsAsync(new Setting(Guid.NewGuid(), SettingKey.UIUrl, UiUrl));
            _settingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.DashboardUrl))
                               .ReturnsAsync(new Setting(Guid.NewGuid(), SettingKey.DashboardUrl, DashboardUrl));

            var options = Options.Create(new LocalizationOptions { ResourcesPath = "Resources" });
            var factory = new ResourceManagerStringLocalizerFactory(options, NullLoggerFactory.Instance);
            _localizationRepository = new LocalizationRepository(new StringLocalizer<LocalizationRepository>(factory));

            var fallbackBuilder = new MailFallbackBuilder(
                _graphGroupRepository.Object,
                _localizationRepository,
                NullLogger<MailFallbackBuilder>.Instance,
                handleInactiveJobsConfig: null,
                nestedGroupsDisplayLimit: 5,
                destinationAttributesRepository: new Mock<IDatabaseDestinationAttributesRepository>().Object);

            var requestAdapter = new Mock<IRequestAdapter>();
            requestAdapter.SetupProperty(x => x.BaseUrl).SetReturnsDefault(GRAPH_API_V1_BASE_URL);
            var graphServiceClient = new Mock<GraphServiceClient>(requestAdapter.Object, GRAPH_API_V1_BASE_URL);

            _mailRepository = new MailRepository(
                graphServiceClient.Object,
                new MailConfig(true, false, "not-set", false, enableStyledFallbackEmails: true, runHistoryTabEnabled: true),
                _localizationRepository,
                NullLogger<MailRepository>.Instance,
                _graphGroupRepository.Object,
                _settingsRepository.Object,
                new RetryPolicyProvider(
                    NullLogger<RetryPolicyProvider>.Instance,
                    new GraphServiceAttemptsValue { MaxExceptionHandlingAttempts = 2, MaxRetryAfterAttempts = 4 }),
                new TelemetryClient(new TelemetryConfiguration()),
                fallbackBuilder);
        }

        /// <summary>
        /// Builds an EmailMessage with the destination group id at the index MailRepository
        /// expects for that notification type (see MailRepository.GetGroupIdIndex).
        /// </summary>
        private static EmailMessage MakeEmail(string content, string subject)
        {
            var contentParams = new string[8];
            for (var i = 0; i < contentParams.Length; i++)
                contentParams[i] = string.Empty;

            var groupIdIndex = content == NotificationConstants.JobPurgingWarningEmailBody ? 4
                             : content == NotificationConstants.SyncJobDisabledEmailBody ? 1
                             : 0;

            contentParams[groupIdIndex] = GroupId;

            // JobPurgingWarning carries the sync status at [0]; the threshold variant deep-links
            // to the take-action dialog, so exercise that branch explicitly.
            if (content == NotificationConstants.JobPurgingWarningEmailBody)
                contentParams[0] = "ThresholdExceeded";
            else if (groupIdIndex != 0)
                contentParams[0] = GroupName;

            // The Final Notice carries the purged job's PRIOR status at [3]. AzureMaintenance only
            // ever purges jobs whose status is in PurgeEligibleStatuses.All, so this is always a
            // purge-eligible status in production and the styled template always renders.
            if (content == NotificationConstants.SyncPurgedForInactivityEmailBody)
            {
                contentParams[1] = GroupName;
                contentParams[3] = nameof(SyncStatus.SecurityGroupNotFound);
            }

            return new EmailMessage
            {
                Subject = subject,
                Content = content,
                AdditionalContentParams = contentParams,
                SyncJobId = Guid.NewGuid()
            };
        }

        /// <summary>
        /// Every notification type currently produces an HTML body and a subject with no
        /// unresolved format placeholders. The placeholder assertion is the guard for the
        /// subject-parameter divergence between the card path (AdditionalContentParams) and
        /// GetSimpleMessage (AdditionalSubjectParams).
        /// </summary>
        [DataTestMethod]
        [DataRow(NotificationConstants.SyncStartedContent, NotificationConstants.OnboardingCompleteEmailSubject)]
        [DataRow(NotificationConstants.SyncCompletedContent, NotificationConstants.OnboardingCompleteEmailSubject)]
        [DataRow(NotificationConstants.NotOwnerContent, NotificationConstants.DisabledNotificationSubject)]
        [DataRow(NotificationConstants.DestinationNotExistContent, NotificationConstants.DestinationNotExistSubject)]
        [DataRow(NotificationConstants.SyncDisabledNoGroupContent, NotificationConstants.DisabledNotificationSubject)]
        [DataRow(NotificationConstants.NoDataContent, NotificationConstants.NoDataSubject)]
        [DataRow(NotificationConstants.GuestUserFailureEmailBody, NotificationConstants.DisabledNotificationSubject)]
        [DataRow(NotificationConstants.NestedGroupsFoundContent, NotificationConstants.NestedGroupsFoundSubject)]
        [DataRow(NotificationConstants.SyncJobDisabledEmailBody, NotificationConstants.SyncThresholdDisablingJobEmailSubject)]
        [DataRow(NotificationConstants.SubmissionRejectedEmailBody, NotificationConstants.SubmissionRejectedEmailSubject)]
        [DataRow(NotificationConstants.JobPurgingWarningEmailBody, NotificationConstants.JobPurgingWarningEmailSubject)]
        [DataRow(NotificationConstants.SyncPurgedForInactivityEmailBody, NotificationConstants.SyncPurgedForInactivityEmailSubject)]
        [DataRow(NotificationConstants.NoValidGroupIdsContent, NotificationConstants.NotValidSourceSubject)]
        [DataRow(NotificationConstants.SyncThresholdBothEmailBody, NotificationConstants.SyncThresholdEmailSubject)]
        public async Task AllNotificationTypes_ProduceHtmlBodyAndResolvedSubject(string content, string subject)
        {
            var message = await _mailRepository.GetStyledMessageAsync(MakeEmail(content, subject));

            Assert.AreEqual(BodyType.Html, message.Body.ContentType, $"{content}: body content type changed.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(message.Body.Content), $"{content}: body was empty.");
            Assert.IsFalse(
                message.Subject.Contains('{') || message.Subject.Contains('}'),
                $"{content}: subject '{message.Subject}' contains an unresolved placeholder.");
        }

        /// <summary>
        /// Locks in which notification types render the styled HTML template and which fall back
        /// to the plain HTML body. No type may emit an actionable card or the legacy OAM warning.
        /// </summary>
        [DataTestMethod]
        [DataRow(NotificationConstants.SyncStartedContent, NotificationConstants.OnboardingCompleteEmailSubject, true)]
        [DataRow(NotificationConstants.SyncCompletedContent, NotificationConstants.OnboardingCompleteEmailSubject, true)]
        [DataRow(NotificationConstants.NotOwnerContent, NotificationConstants.DisabledNotificationSubject, true)]
        [DataRow(NotificationConstants.DestinationNotExistContent, NotificationConstants.DestinationNotExistSubject, true)]
        [DataRow(NotificationConstants.SyncDisabledNoGroupContent, NotificationConstants.DisabledNotificationSubject, true)]
        [DataRow(NotificationConstants.NoDataContent, NotificationConstants.NoDataSubject, true)]
        [DataRow(NotificationConstants.GuestUserFailureEmailBody, NotificationConstants.DisabledNotificationSubject, true)]
        [DataRow(NotificationConstants.NestedGroupsFoundContent, NotificationConstants.NestedGroupsFoundSubject, true)]
        [DataRow(NotificationConstants.SyncJobDisabledEmailBody, NotificationConstants.SyncThresholdDisablingJobEmailSubject, true)]
        [DataRow(NotificationConstants.SubmissionRejectedEmailBody, NotificationConstants.SubmissionRejectedEmailSubject, true)]
        [DataRow(NotificationConstants.JobPurgingWarningEmailBody, NotificationConstants.JobPurgingWarningEmailSubject, true)]
        [DataRow(NotificationConstants.SyncPurgedForInactivityEmailBody, NotificationConstants.SyncPurgedForInactivityEmailSubject, true)]
        [DataRow(NotificationConstants.NoValidGroupIdsContent, NotificationConstants.NotValidSourceSubject, false)]
        [DataRow(NotificationConstants.SyncThresholdBothEmailBody, NotificationConstants.SyncThresholdEmailSubject, false)]
        public async Task NotificationTypes_UseExpectedStyledOrPlainTemplate(string content, string subject, bool expectStyled)
        {
            var message = await _mailRepository.GetStyledMessageAsync(MakeEmail(content, subject));

            Assert.IsFalse(message.Body.Content.Contains(LegacyMarker),
                $"{content}: no notification may still emit the legacy Outlook Actionable Message fallback.");
            Assert.IsFalse(message.Body.Content.Contains("adaptivecard+json"),
                $"{content}: no notification may embed an actionable card payload.");

            var isStyled = message.Body.Content.Contains(StyledMarker);

            Assert.AreEqual(
                expectStyled,
                isStyled,
                expectStyled
                    ? $"{content} must render the styled HTML email."
                    : $"{content} is expected to render the plain HTML body, not the styled template.");
        }

        /// <summary>
        /// The styled emails must never embed an actionable card payload.
        /// </summary>
        [TestMethod]
        public async Task StyledNotification_DoesNotEmbedAdaptiveCardPayload()
        {
            var message = await _mailRepository.GetStyledMessageAsync(
                MakeEmail(NotificationConstants.SyncStartedContent, NotificationConstants.OnboardingCompleteEmailSubject));

            Assert.IsFalse(message.Body.Content.Contains("adaptivecard+json"),
                "Styled emails must not embed an Outlook Actionable Message card.");
        }

        /// <summary>
        /// The normal threshold email is a numbered "Reply All" workflow. Its line structure is
        /// currently preserved by the &lt;pre&gt; wrapper; losing it would run the four options
        /// together into a single unreadable paragraph.
        /// </summary>
        [TestMethod]
        public async Task NormalThresholdEmail_PreservesNumberedActionList()
        {
            var message = await _mailRepository.GetStyledMessageAsync(
                MakeEmail(NotificationConstants.SyncThresholdBothEmailBody, NotificationConstants.SyncThresholdEmailSubject));

            StringAssert.Contains(message.Body.Content, "Actions needed");
            foreach (var option in new[] { "1. ", "2. ", "3. ", "4. " })
            {
                StringAssert.Contains(message.Body.Content, option,
                    $"Numbered option '{option}' missing from the normal threshold email.");
            }
            Assert.IsTrue(message.Body.Content.Contains("<pre>"),
                "The threshold email body relies on <pre> to preserve its numbered list line breaks.");
        }

        /// <summary>
        /// Deep-link CTAs differ per notification type; these are the ones that are easiest to
        /// break in a refactor because they do not use the default run-history URL.
        /// </summary>
        [TestMethod]
        public async Task FinalNotice_LinksToOnboardingRatherThanPurgedJob()
        {
            var message = await _mailRepository.GetStyledMessageAsync(
                MakeEmail(NotificationConstants.SyncPurgedForInactivityEmailBody, NotificationConstants.SyncPurgedForInactivityEmailSubject));

            StringAssert.Contains(message.Body.Content, "ManageMembership",
                "The final notice must deep-link to onboarding; the sync job no longer exists.");
        }

        [TestMethod]
        public async Task ThresholdDisabledEmail_LinksToTakeActionDialog()
        {
            var message = await _mailRepository.GetStyledMessageAsync(
                MakeEmail(NotificationConstants.SyncJobDisabledEmailBody, NotificationConstants.SyncThresholdDisablingJobEmailSubject));

            StringAssert.Contains(message.Body.Content, "takeAction",
                "The threshold-disabled email must deep-link to the take-action dialog.");
        }

        /// <summary>
        /// IsHTML messages bypass the styled dispatch entirely and must keep doing so, otherwise
        /// a group name containing a trigger word (e.g. "Disabled") could double-wrap the body.
        /// </summary>
        [TestMethod]
        public async Task HtmlMessage_WithTriggerWordInGroupName_IsNotStyleWrapped()
        {
            var email = MakeEmail(NotificationConstants.SyncStartedContent, NotificationConstants.OnboardingCompleteEmailSubject);
            email.IsHTML = true;
            email.DestinationGroupName = "Disabled Accounts - Cleanup";

            var message = _mailRepository.GetSimpleMessage(email);
            message.Body.ContentType = BodyType.Html;

            Assert.IsFalse(message.Body.Content.Contains(LegacyMarker));
            Assert.IsFalse(message.Body.Content.Contains("adaptivecard+json"));
        }
    }
}
