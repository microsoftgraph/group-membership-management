// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Messaging.ServiceBus;
using Hosts.Notifier;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Notifications;
using Moq;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Services.Notifier.Tests
{
    [TestClass]
    public class ReplayDeferredNotificationsFunctionTests
    {
        private Mock<INotificationTypesRepository> _notificationTypesRepository;
        private Mock<IDeferredNotificationsRepository> _deferredNotificationsRepository;
        private Mock<ServiceBusClient> _serviceBusClient;
        private Mock<ServiceBusReceiver> _serviceBusReceiver;
        private Mock<ServiceBusSender> _serviceBusSender;
        private Mock<IMailConfig> _mailConfig;
        private TelemetryClient _telemetryClient;
        private IConfiguration _configuration;
        private ReplayDeferredNotificationsFunction _replayFunction;

        [TestInitialize]
        public void SetupTest()
        {
            _notificationTypesRepository = new Mock<INotificationTypesRepository>();
            _deferredNotificationsRepository = new Mock<IDeferredNotificationsRepository>();
            _serviceBusClient = new Mock<ServiceBusClient>();
            _serviceBusReceiver = new Mock<ServiceBusReceiver>();
            _serviceBusSender = new Mock<ServiceBusSender>();
            _mailConfig = new Mock<IMailConfig>();
            _telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());

            _mailConfig.Setup(x => x.SkipEmailNotifications).Returns(false);

            _serviceBusClient
                .Setup(x => x.CreateReceiver(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ServiceBusReceiverOptions>()))
                .Returns(_serviceBusReceiver.Object);

            _serviceBusClient
                .Setup(x => x.CreateReceiver(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(_serviceBusReceiver.Object);

            _serviceBusClient
                .Setup(x => x.CreateSender(It.IsAny<string>()))
                .Returns(_serviceBusSender.Object);

            var configValues = new Dictionary<string, string>
            {
                { "serviceBusNotificationsTopic", "notifications" },
                { "serviceBusNotificationsSubscription", "notifier" }
            };

            _configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configValues)
                .Build();

            _replayFunction = new ReplayDeferredNotificationsFunction(
                NullLogger<ReplayDeferredNotificationsFunction>.Instance,
                _notificationTypesRepository.Object,
                _deferredNotificationsRepository.Object,
                _mailConfig.Object,
                _serviceBusClient.Object,
                _telemetryClient,
                _configuration);
        }

        [TestMethod]
        public async Task RunAsync_WhenTypeReEnabled_ReplaysAndCleansUp()
        {
            // Arrange
            var messageType = NotificationMessageType.ThresholdNotification;

            _notificationTypesRepository
                .Setup(x => x.GetNotificationTypeByNotificationTypeNameAsync(messageType))
                .ReturnsAsync(new NotificationType { Id = 1, Name = messageType, Disabled = false });

            var deferredMessages = new List<DeferredNotification>
            {
                new DeferredNotification { Id = 1, SequenceNumber = 100, MessageType = messageType, Status = DeferredNotificationStatus.Deferred, DeferredAt = DateTime.UtcNow.AddMinutes(-5), MessageExpiresAt = DateTime.UtcNow.AddDays(7) },
                new DeferredNotification { Id = 2, SequenceNumber = 101, MessageType = messageType, Status = DeferredNotificationStatus.Deferred, DeferredAt = DateTime.UtcNow.AddMinutes(-3), MessageExpiresAt = DateTime.UtcNow.AddDays(7) }
            };

            _deferredNotificationsRepository
                .Setup(x => x.GetDeferredNotificationsByTypeAndStatusAsync(messageType, DeferredNotificationStatus.Deferred))
                .ReturnsAsync(deferredMessages);

            // All other types return no deferred messages
            _deferredNotificationsRepository
                .Setup(x => x.GetDeferredNotificationsByTypeAndStatusAsync(It.Is<NotificationMessageType>(t => t != messageType), DeferredNotificationStatus.Deferred))
                .ReturnsAsync(new List<DeferredNotification>());

            var receivedMessages = new List<ServiceBusReceivedMessage>
            {
                ServiceBusModelFactory.ServiceBusReceivedMessage(new BinaryData("msg1"), sequenceNumber: 100),
                ServiceBusModelFactory.ServiceBusReceivedMessage(new BinaryData("msg2"), sequenceNumber: 101)
            };

            _serviceBusReceiver
                .Setup(x => x.ReceiveDeferredMessagesAsync(It.IsAny<long[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(receivedMessages);

            // Act
            await _replayFunction.RunAsync(CreateTimerInfo());

            // Assert
            _serviceBusReceiver.Verify(x => x.ReceiveDeferredMessagesAsync(
                It.Is<long[]>(seq => seq.Length == 2), It.IsAny<CancellationToken>()), Times.Once());
            _serviceBusSender.Verify(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
            _serviceBusReceiver.Verify(x => x.CompleteMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
            // Verify per-message status updates (Replaying batch + individual Replayed)
            _deferredNotificationsRepository.Verify(x => x.UpdateStatusBatchAsync(
                It.IsAny<IEnumerable<int>>(), DeferredNotificationStatus.Replaying), Times.Once());
            _deferredNotificationsRepository.Verify(x => x.UpdateStatusAsync(
                It.IsAny<int>(), DeferredNotificationStatus.Replayed), Times.Exactly(2));
            _deferredNotificationsRepository.Verify(x => x.RemoveDeferredNotificationsByTypeAndStatusAsync(
                messageType, DeferredNotificationStatus.Replayed), Times.Once());
        }

        [TestMethod]
        public async Task RunAsync_WhenTypeStillDisabled_DoesNotReplay()
        {
            // Arrange
            var messageType = NotificationMessageType.ThresholdNotification;

            _notificationTypesRepository
                .Setup(x => x.GetNotificationTypeByNotificationTypeNameAsync(messageType))
                .ReturnsAsync(new NotificationType { Id = 1, Name = messageType, Disabled = true });

            // All other types also disabled or not found
            _notificationTypesRepository
                .Setup(x => x.GetNotificationTypeByNotificationTypeNameAsync(It.Is<NotificationMessageType>(t => t != messageType)))
                .ReturnsAsync((NotificationType)null);

            _deferredNotificationsRepository
                .Setup(x => x.GetDeferredNotificationsByTypeAndStatusAsync(It.IsAny<NotificationMessageType>(), DeferredNotificationStatus.Deferred))
                .ReturnsAsync(new List<DeferredNotification>());

            // Act
            await _replayFunction.RunAsync(CreateTimerInfo());

            // Assert - no replay operations should occur
            _serviceBusReceiver.Verify(x => x.ReceiveDeferredMessagesAsync(It.IsAny<long[]>(), It.IsAny<CancellationToken>()), Times.Never());
            _deferredNotificationsRepository.Verify(x => x.UpdateStatusBatchAsync(
                It.IsAny<IEnumerable<int>>(), DeferredNotificationStatus.Replaying), Times.Never());
        }

        [TestMethod]
        public async Task RunAsync_WhenNoDeferredMessages_DoesNothing()
        {
            // Arrange
            _notificationTypesRepository
                .Setup(x => x.GetNotificationTypeByNotificationTypeNameAsync(It.IsAny<NotificationMessageType>()))
                .ReturnsAsync(new NotificationType { Id = 1, Name = NotificationMessageType.ThresholdNotification, Disabled = false });

            _deferredNotificationsRepository
                .Setup(x => x.GetDeferredNotificationsByTypeAndStatusAsync(It.IsAny<NotificationMessageType>(), DeferredNotificationStatus.Deferred))
                .ReturnsAsync(new List<DeferredNotification>());

            // Act
            await _replayFunction.RunAsync(CreateTimerInfo());

            // Assert
            _serviceBusClient.Verify(x => x.CreateReceiver(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ServiceBusReceiverOptions>()), Times.Never());
            _serviceBusClient.Verify(x => x.CreateReceiver(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [TestMethod]
        public async Task RunAsync_WhenMessagesExpired_MarksAsExpired()
        {
            // Arrange
            var messageType = NotificationMessageType.SyncStartedNotification;

            _notificationTypesRepository
                .Setup(x => x.GetNotificationTypeByNotificationTypeNameAsync(messageType))
                .ReturnsAsync(new NotificationType { Id = 2, Name = messageType, Disabled = false });

            _notificationTypesRepository
                .Setup(x => x.GetNotificationTypeByNotificationTypeNameAsync(It.Is<NotificationMessageType>(t => t != messageType)))
                .ReturnsAsync((NotificationType)null);

            // Message has already expired (MessageExpiresAt in the past)
            var deferredMessages = new List<DeferredNotification>
            {
                new DeferredNotification { Id = 1, SequenceNumber = 200, MessageType = messageType, Status = DeferredNotificationStatus.Deferred, DeferredAt = DateTime.UtcNow.AddDays(-15), MessageExpiresAt = DateTime.UtcNow.AddDays(-1) }
            };

            _deferredNotificationsRepository
                .Setup(x => x.GetDeferredNotificationsByTypeAndStatusAsync(messageType, DeferredNotificationStatus.Deferred))
                .ReturnsAsync(deferredMessages);

            _deferredNotificationsRepository
                .Setup(x => x.GetDeferredNotificationsByTypeAndStatusAsync(It.Is<NotificationMessageType>(t => t != messageType), DeferredNotificationStatus.Deferred))
                .ReturnsAsync(new List<DeferredNotification>());

            // Act
            await _replayFunction.RunAsync(CreateTimerInfo());

            // Assert - should mark as expired, not attempt to replay
            _deferredNotificationsRepository.Verify(x => x.UpdateStatusBatchAsync(
                It.Is<IEnumerable<int>>(ids => ids.Contains(1)), DeferredNotificationStatus.Expired), Times.Once());
            _serviceBusReceiver.Verify(x => x.ReceiveDeferredMessagesAsync(It.IsAny<long[]>(), It.IsAny<CancellationToken>()), Times.Never());
        }

        [TestMethod]
        public async Task RunAsync_WhenServiceBusMessageNotFound_MarksAsExpired()
        {
            // Arrange
            var messageType = NotificationMessageType.SyncStartedNotification;

            _notificationTypesRepository
                .Setup(x => x.GetNotificationTypeByNotificationTypeNameAsync(messageType))
                .ReturnsAsync(new NotificationType { Id = 2, Name = messageType, Disabled = false });

            _notificationTypesRepository
                .Setup(x => x.GetNotificationTypeByNotificationTypeNameAsync(It.Is<NotificationMessageType>(t => t != messageType)))
                .ReturnsAsync((NotificationType)null);

            // Message hasn't expired per DB tracking but Service Bus has purged it
            var deferredMessages = new List<DeferredNotification>
            {
                new DeferredNotification { Id = 1, SequenceNumber = 200, MessageType = messageType, Status = DeferredNotificationStatus.Deferred, DeferredAt = DateTime.UtcNow.AddDays(-1), MessageExpiresAt = DateTime.UtcNow.AddDays(5) }
            };

            _deferredNotificationsRepository
                .Setup(x => x.GetDeferredNotificationsByTypeAndStatusAsync(messageType, DeferredNotificationStatus.Deferred))
                .ReturnsAsync(deferredMessages);

            _deferredNotificationsRepository
                .Setup(x => x.GetDeferredNotificationsByTypeAndStatusAsync(It.Is<NotificationMessageType>(t => t != messageType), DeferredNotificationStatus.Deferred))
                .ReturnsAsync(new List<DeferredNotification>());

            _serviceBusReceiver
                .Setup(x => x.ReceiveDeferredMessagesAsync(It.IsAny<long[]>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceBusException("Message not found", ServiceBusFailureReason.MessageNotFound));

            // Act
            await _replayFunction.RunAsync(CreateTimerInfo());

            // Assert - should mark as expired via status update
            _deferredNotificationsRepository.Verify(x => x.UpdateStatusBatchAsync(
                It.IsAny<IEnumerable<int>>(), DeferredNotificationStatus.Expired), Times.Once());
        }

        [TestMethod]
        public async Task RunAsync_WhenSkipEmailNotificationsEnabled_SkipsReplay()
        {
            // Arrange
            _mailConfig.Setup(x => x.SkipEmailNotifications).Returns(true);

            _deferredNotificationsRepository
                .Setup(x => x.GetDeferredNotificationsByTypeAndStatusAsync(It.IsAny<NotificationMessageType>(), DeferredNotificationStatus.Deferred))
                .ReturnsAsync(new List<DeferredNotification>());

            // Act
            await _replayFunction.RunAsync(CreateTimerInfo());

            // Assert - should not attempt any replay when global suppression is active
            _notificationTypesRepository.Verify(x => x.GetNotificationTypeByNotificationTypeNameAsync(
                It.IsAny<NotificationMessageType>()), Times.Never());
            _serviceBusReceiver.Verify(x => x.ReceiveDeferredMessagesAsync(It.IsAny<long[]>(), It.IsAny<CancellationToken>()), Times.Never());
            _deferredNotificationsRepository.Verify(x => x.UpdateStatusBatchAsync(
                It.IsAny<IEnumerable<int>>(), It.IsAny<DeferredNotificationStatus>()), Times.Never());
        }

        private static TimerInfo CreateTimerInfo()
        {
            return new TimerInfo();
        }
    }
}
