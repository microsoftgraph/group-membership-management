// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Models.ThresholdNotifications;
using Microsoft.Extensions.Localization;
using Repositories.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Services.Contracts.Notifications;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Models.Notifications;
using System.Linq;
using Services.Tests;
using Hosts.Notifier;
using System.Text.Json;
using DIConcreteTypes;
using Repositories.Logging;
using Models.Entities;

namespace Services.Notifier.Tests
{
    [TestClass]
    public class NotifierServiceTests
    {
        private const string GroupMembership = "GroupMembership";

        private Mock<ILoggingRepository> _loggerMock;
        private Mock<IEmailSenderRecipient> _mailAddresses;
        private Mock<IMailRepository> _mailRepository;
        private Mock<INotificationRepository> _notificationRepository;
        private Mock<IGraphGroupRepository> _graphGroupRepository;
        private Mock<IThresholdNotificationService> _thresholdNotificationService;
        private List<AzureADUser> _users;
        private Guid _targetOfficeGroupId;
        private ILocalizationRepository _localizationRepository;
        private NotifierService _notifierService;
        private ThresholdNotification _notification;
        private Mock<INotificationTypesRepository> _notificationTypesRepository;
        private Mock<IJobNotificationsRepository> _jobNotificationRepository;
        private TelemetryClient _telemetryClient;
        private Mock<IThresholdConfig> _thresholdConfig;
        private Mock<IGMMResources> _gmmResources;

        [TestInitialize]
        public void SetupTest()
        {
            _graphGroupRepository = new Mock<IGraphGroupRepository>();
            _mailRepository = new Mock<IMailRepository>();
            _notificationRepository = new Mock<INotificationRepository>();
            _loggerMock = new Mock<ILoggingRepository>();
            _mailAddresses = new Mock<IEmailSenderRecipient>();
            _thresholdNotificationService = new Mock<IThresholdNotificationService>();
            _users = new List<AzureADUser>();
            _notificationTypesRepository = new Mock<INotificationTypesRepository>();
            _jobNotificationRepository = new Mock<IJobNotificationsRepository>();
            _thresholdConfig = new Mock<IThresholdConfig>();
            _gmmResources = new Mock<IGMMResources>();
            _notification = new ThresholdNotification
            {
                Id = Guid.NewGuid(),
                ChangePercentageForAdditions = 1,
                ChangePercentageForRemovals = 1,
                CreatedTime = DateTime.UtcNow,
                Resolution = ThresholdNotificationResolution.Unresolved,
                ResolvedByUPN = string.Empty,
                ResolvedTime = DateTime.UtcNow,
                Status = ThresholdNotificationStatus.Unknown,
                CardState = ThresholdNotificationCardState.DefaultCard,
                TargetOfficeGroupId = _targetOfficeGroupId,
                ThresholdPercentageForAdditions = -1,
                ThresholdPercentageForRemovals = -1,
            };
            _loggerMock.Setup(x => x.LogMessageAsync(It.IsAny<LogMessage>(), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()));
            _telemetryClient = new TelemetryClient(new TelemetryConfiguration());

            for (int i = 0; i < 2; i++)
            {
                var user = new AzureADUser
                {
                    Mail = $"owner_{i}@email.com"
                };
                _users.Add(user);
            }
            _graphGroupRepository.Setup(x => x.GetGroupOwnersAsync(_targetOfficeGroupId, 0)).Returns(() => Task.FromResult(_users));
            _graphGroupRepository.Setup(x => x.GetGroupNameAsync(It.Is<Guid>(id => id == _targetOfficeGroupId))).ReturnsAsync($"Test Group with id {_targetOfficeGroupId}");
            _thresholdNotificationService.Setup(x => x.CreateNotificationCardAsync(It.IsAny<ThresholdNotification>())).ReturnsAsync(_notification.Id.ToString());

            var options = Options.Create(new LocalizationOptions { ResourcesPath = "Resources" });
            var factory = new ResourceManagerStringLocalizerFactory(options, NullLoggerFactory.Instance);
            var localizer = new StringLocalizer<LocalizationRepository>(factory);
            _localizationRepository = new LocalizationRepository(localizer);

            _notifierService = new NotifierService(_loggerMock.Object,
                                                _mailRepository.Object,
                                                _mailAddresses.Object,
                                                _localizationRepository,
                                                _thresholdNotificationService.Object,
                                                _notificationRepository.Object,
                                                _graphGroupRepository.Object,
                                                _notificationTypesRepository.Object,
                                                _jobNotificationRepository.Object,
                                                _thresholdConfig.Object,
                                                _gmmResources.Object,
                                                _telemetryClient
                                                );
        }

        [TestMethod]
        public async Task TestSendThresholdEmail()
        {
            await _notifierService.SendThresholdEmailAsync(_notification);
            _mailRepository.Verify(x => x.SendMailAsync(It.IsAny<EmailMessage>(), null), Times.Once());
        }

        [TestMethod]
        public async Task TestRetrieveQueuedNotifications()
        {
            await _notifierService.RetrieveQueuedNotificationsAsync();
            _notificationRepository.Verify(x => x.GetQueuedNotificationsAsync(), Times.Once());
        }

        [TestMethod]
        public async Task TestUpdateNotificationStatus()
        {
            await _notifierService.UpdateNotificationStatusAsync(_notification, ThresholdNotificationStatus.Queued);
            _notificationRepository.Verify(x => x.UpdateNotificationStatusAsync(It.IsAny<ThresholdNotification>(), It.IsAny<ThresholdNotificationStatus>()), Times.Once());
        }
        [TestMethod]
        public async Task CreateActionableNotificationFromContentAsync_ShouldCreateOrUpdateNotification()
        {
            SyncJob job = SampleDataHelper.CreateSampleSyncJobs(1, GroupMembership).First();

            var thresholdResult = new ThresholdResult
            {
                IncreaseThresholdPercentage = 10.0,
                DecreaseThresholdPercentage = 5.0,
                DeltaToAddCount = 5,
                DeltaToRemoveCount = 5,
                IsAdditionsThresholdExceeded = true,
                IsRemovalsThresholdExceeded = false
            };

            bool sendDisableJobNotification = true;

            var messageContent = new
            {
                ThresholdResult = thresholdResult,
                SyncJob = job,
                SendDisableJobNotification = sendDisableJobNotification
            };
            string serializedMessageContent = JsonSerializer.Serialize(messageContent);

            OrchestratorRequest request = new OrchestratorRequest
            {
                MessageType = nameof(NotificationMessageType.ThresholdNotification),
                MessageBody = serializedMessageContent
            };

            _notificationRepository.Setup(x => x.GetThresholdNotificationBySyncJobIdAsync(It.IsAny<Guid>()))
                .ReturnsAsync((ThresholdNotification)null);

            var result = await _notifierService.CreateActionableNotificationFromContentAsync(request.MessageBody);

            Assert.IsNotNull(result);
            _notificationRepository.Verify(x => x.SaveNotificationAsync(It.IsAny<ThresholdNotification>()), Times.Once);
        }
        [TestMethod]
        public async Task VerifyEmailNotSentIfDisabled()
        {
            var job = SampleDataHelper.CreateSampleSyncJobs(1, GroupMembership).First();
            var notificationTypeId = 1; 
            var notificationName = "SyncStartedEmailBody"; 

            _notificationTypesRepository.Setup(repo => repo.GetNotificationTypeByNotificationTypeNameAsync(notificationName))
                .ReturnsAsync(new NotificationType { Id = notificationTypeId, Name = notificationName, Disabled = false });

            _jobNotificationRepository.Setup(repo => repo.IsNotificationDisabledForJobAsync(job.Id, notificationTypeId))
                .ReturnsAsync(true);

            bool result = await _notifierService.IsNotificationDisabledAsync(job.Id, notificationName);
            Assert.IsTrue(result);

        }

        [TestMethod]
        public async Task VerifyEmailNotSentIfGloballyDisabled()
        {
            var job = SampleDataHelper.CreateSampleSyncJobs(1, GroupMembership).First();
            var notificationTypeId = 1;
            var notificationName = "SyncStartedEmailBody";

            _notificationTypesRepository.Setup(repo => repo.GetNotificationTypeByNotificationTypeNameAsync(notificationName))
                .ReturnsAsync(new NotificationType { Id = notificationTypeId, Name = notificationName, Disabled = true });

            _jobNotificationRepository.Setup(repo => repo.IsNotificationDisabledForJobAsync(job.Id, notificationTypeId))
                .ReturnsAsync(false);

            bool result = await _notifierService.IsNotificationDisabledAsync(job.Id, notificationName);
            Assert.IsTrue(result);

        }

        [TestMethod]
        public async Task TestSendEmailAsync()
        {
            SyncJob job = SampleDataHelper.CreateSampleSyncJobs(1, GroupMembership).First();
            var notificationTypeId = 1;

            string[] additionalContentParameters = new string[] { "Param1", "Param2" };
            var messageContent = new Dictionary<string, Object>
            {
                { "SyncJob", job },
                { "AdditionalContentParameters", additionalContentParameters }
            };
            string subjectTemplate = "DisabledJobEmailSubject";
            string contentTemplate = "SyncDisabledNoGroupEmailBody";
            _notificationTypesRepository.Setup(repo => repo.GetNotificationTypeByNotificationTypeNameAsync(contentTemplate))
                .ReturnsAsync(new NotificationType { Id = notificationTypeId, Name = contentTemplate, Disabled = false });

            _jobNotificationRepository.Setup(repo => repo.IsNotificationDisabledForJobAsync(job.Id, notificationTypeId))
                .ReturnsAsync(false);

            string serializedMessageContent = JsonSerializer.Serialize(messageContent);
            OrchestratorRequest request = new OrchestratorRequest
            {
                MessageType = nameof(NotificationMessageType.SyncStartedNotification),
                MessageBody = serializedMessageContent,
                SubjectTemplate = subjectTemplate,
                ContentTemplate = contentTemplate
            };

            await _notifierService.SendEmailAsync(request.MessageType, request.MessageBody, request.SubjectTemplate, request.ContentTemplate);
            _mailRepository.Verify(x => x.SendMailAsync(It.IsAny<EmailMessage>(), It.IsAny<Guid?>()), Times.Once());
        }

        [TestMethod]
        public async Task SendNormalThresholdEmailAsync_SendsEmail_WithExpectedParameters()
        {
            var messageBody = "{\"SyncJob\": {\"Id\": \"12345678-1234-1234-1234-1234567890ab\", \"TargetOfficeGroupId\": \"12345678-1234-1234-1234-1234567890ab\"}, \"ThresholdResult\": {\"IncreaseThresholdPercentage\": 10.0}, \"SendDisableJobNotification\": false, \"GroupName\": \"Test Group\"}";
            await _notifierService.SendNormalThresholdEmailAsync(messageBody);
            _mailRepository.Verify(x => x.SendMailAsync(It.IsAny<EmailMessage>(), It.IsAny<Guid?>()), Times.Once());

        }
    }
}
