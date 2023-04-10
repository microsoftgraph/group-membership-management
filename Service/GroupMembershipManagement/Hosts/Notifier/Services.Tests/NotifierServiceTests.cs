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

namespace Services.Tests
{
    [TestClass]
    public class NotifierServiceTests
    {
        private Mock<ILoggingRepository> _loggerMock;
        private Mock<IEmailSenderRecipient> _mailAddresses;
        private Mock<IMailRepository> _mailRepository ;
        private Mock<INotificationRepository> _notificationRepository;
        private Mock<IGraphGroupRepository> _graphGroupRepository;
        private List<User> _users;
        private Guid _targetOfficeGroupId;
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
            _users = new List<User>();
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
                TargetOfficeGroupId = Guid.NewGuid(),
                ThresholdPercentageForAdditions = -1,
                ThresholdPercentageForRemovals = -1,
            };
            _loggerMock.Setup(x => x.LogMessageAsync(It.IsAny<LogMessage>(), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()));

            for (int i = 0; i < 2; i++)
            {
                var user = new User
                {
                    Mail = $"owner_{i}@email.com"
                };
                _users.Add(user);
            }
            _ = _graphGroupRepository.Setup(x => x.GetGroupOwnersAsync(_targetOfficeGroupId, 0)).ReturnsAsync(_users);

            _notifierService = new NotifierService(_loggerMock.Object,
                                                _mailRepository.Object,
                                                _mailAddresses.Object,
                                                _notificationRepository.Object,
                                                _graphGroupRepository.Object
                                                );
        }

        [TestMethod]
        public async Task TestSendEmail()
        {
            await _notifierService.SendEmailAsync(_targetOfficeGroupId);
            _mailRepository.Verify(x => x.SendMailAsync(It.IsAny<EmailMessage>(), null, It.IsAny<string>()), Times.Once());
        }

        [TestMethod]
        public async Task TestRetrieveQueuedNotifications()
        {
            await _notifierService.RetrieveQueuedNotifications();
            _notificationRepository.Verify(x => x.GetQueuedNotificationsAsync(), Times.Once());
        }

        [TestMethod]
        public async Task TestUpdateNotificationStatus()
        {
            await _notifierService.UpdateNotificationStatus(_notification, ThresholdNotificationStatus.Queued);
            _notificationRepository.Verify(x => x.UpdateNotificationStatusAsync(It.IsAny<ThresholdNotification>(), It.IsAny<ThresholdNotificationStatus>()), Times.Once());
        }

    }
}
