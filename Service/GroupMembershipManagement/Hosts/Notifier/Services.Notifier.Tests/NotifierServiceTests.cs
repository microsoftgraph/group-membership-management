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
using Microsoft.Graph;
using Models.ThresholdNotifications;
using Microsoft.Extensions.Localization;
using Repositories.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Services.Contracts.Notifications;

namespace Services.Notifier.Tests
{
    [TestClass]
    public class NotifierServiceTests
    {
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
                TargetOfficeGroupId = _targetOfficeGroupId,
                ThresholdPercentageForAdditions = -1,
                ThresholdPercentageForRemovals = -1,
            };
            _loggerMock.Setup(x => x.LogMessageAsync(It.IsAny<LogMessage>(), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()));

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
                                                _graphGroupRepository.Object
                                                );
        }

        [TestMethod]
        public async Task TestSendEmail()
        {
            await _notifierService.SendEmailAsync(_notification);
            _mailRepository.Verify(x => x.SendMailAsync(It.IsAny<EmailMessage>(), null, It.IsAny<string>()), Times.Once());
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

    }
}
