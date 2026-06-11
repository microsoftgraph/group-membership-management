// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Messaging.ServiceBus;
using Hosts.Notifier;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Notifications;
using Moq;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Services.Notifier.Tests
{
    [TestClass]
    public class StarterFunctionTests
    {
        private Mock<IMailConfig> _mailConfig;
        private Mock<IThresholdNotificationConfig> _thresholdNotificationConfig;
        private Mock<INotificationTypesRepository> _notificationTypesRepository;
        private Mock<IDeferredNotificationsRepository> _deferredNotificationsRepository;
        private Mock<ServiceBusMessageActions> _messageActions;
        private Mock<DurableTaskClient> _durableClient;
        private TelemetryClient _telemetryClient;
        private StarterFunction _starterFunction;
        private SyncJob _syncJob;

        [TestInitialize]
        public void SetupTest()
        {
            _mailConfig = new Mock<IMailConfig>();
            _thresholdNotificationConfig = new Mock<IThresholdNotificationConfig>();
            _notificationTypesRepository = new Mock<INotificationTypesRepository>();
            _deferredNotificationsRepository = new Mock<IDeferredNotificationsRepository>();
            _messageActions = new Mock<ServiceBusMessageActions>();
            _durableClient = new Mock<DurableTaskClient>("test");
            _telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());

            _syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                ThresholdPercentageForAdditions = 80,
                ThresholdPercentageForRemovals = 20,
                Requestor = "user@domain.com",
                TargetOfficeGroupId = Guid.NewGuid()
            };

            _starterFunction = new StarterFunction(
                NullLogger<StarterFunction>.Instance,
                _thresholdNotificationConfig.Object,
                _mailConfig.Object,
                _notificationTypesRepository.Object,
                _deferredNotificationsRepository.Object,
                _telemetryClient);
        }

        private ServiceBusReceivedMessage CreateMessage(NotificationMessageType messageType)
        {
            var messageContent = new Dictionary<string, object>
            {
                { "SyncJob", _syncJob },
                { "AdditionalContentParameters", new string[] { "param1" } }
            };

            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(messageContent));
            var properties = new Dictionary<string, object>
            {
                { "MessageType", messageType.ToString() }
            };

            return ServiceBusModelFactory.ServiceBusReceivedMessage(
                new BinaryData(body),
                properties: properties,
                sequenceNumber: 12345);
        }

        [TestMethod]
        public async Task RunAsync_WhenNotificationTypeDisabled_DefersMessage()
        {
            // Arrange
            var messageType = NotificationMessageType.ThresholdNotification;
            var message = CreateMessage(messageType);

            _notificationTypesRepository
                .Setup(x => x.GetNotificationTypeByNotificationTypeNameAsync(messageType))
                .ReturnsAsync(new NotificationType { Id = 1, Name = messageType, Disabled = true });

            _mailConfig.Setup(x => x.SkipEmailNotifications).Returns(false);

            // Act
            await _starterFunction.RunAsync(message, _messageActions.Object, _durableClient.Object);

            // Assert
            _messageActions.Verify(x => x.DeferMessageAsync(message, null, It.IsAny<CancellationToken>()), Times.Once());
            _messageActions.Verify(x => x.CompleteMessageAsync(message, It.IsAny<CancellationToken>()), Times.Never());
            _deferredNotificationsRepository.Verify(x => x.AddDeferredNotificationAsync(
                It.Is<DeferredNotification>(d =>
                    d.SequenceNumber == 12345 &&
                    d.MessageType == messageType &&
                    d.SyncJobId == _syncJob.Id)), Times.Once());
            _durableClient.Verify(x => x.ScheduleNewOrchestrationInstanceAsync(
                It.IsAny<TaskName>(), It.IsAny<object>(), It.IsAny<StartOrchestrationOptions>(), It.IsAny<CancellationToken>()), Times.Never());
        }

        [TestMethod]
        public async Task RunAsync_WhenNotificationTypeEnabled_ProcessesNormally()
        {
            // Arrange
            var messageType = NotificationMessageType.SyncStartedNotification;
            var message = CreateMessage(messageType);

            _notificationTypesRepository
                .Setup(x => x.GetNotificationTypeByNotificationTypeNameAsync(messageType))
                .ReturnsAsync(new NotificationType { Id = 2, Name = messageType, Disabled = false });

            _mailConfig.Setup(x => x.SkipEmailNotifications).Returns(false);

            // Act
            await _starterFunction.RunAsync(message, _messageActions.Object, _durableClient.Object);

            // Assert
            _messageActions.Verify(x => x.DeferMessageAsync(message, null, It.IsAny<CancellationToken>()), Times.Never());
            _messageActions.Verify(x => x.CompleteMessageAsync(message, It.IsAny<CancellationToken>()), Times.Once());
            _deferredNotificationsRepository.Verify(x => x.AddDeferredNotificationAsync(It.IsAny<DeferredNotification>()), Times.Never());
            _durableClient.Verify(x => x.ScheduleNewOrchestrationInstanceAsync(
                It.Is<TaskName>(t => t.Name == nameof(OrchestratorFunction)), It.IsAny<object>(), It.IsAny<StartOrchestrationOptions>(), It.IsAny<CancellationToken>()), Times.Once());
        }

        [TestMethod]
        public async Task RunAsync_WhenSkipEmailNotificationsEnabled_DefersMessage()
        {
            // Arrange
            var messageType = NotificationMessageType.SyncCompletedNotification;
            var message = CreateMessage(messageType);

            _notificationTypesRepository
                .Setup(x => x.GetNotificationTypeByNotificationTypeNameAsync(messageType))
                .ReturnsAsync(new NotificationType { Id = 3, Name = messageType, Disabled = false });

            _mailConfig.Setup(x => x.SkipEmailNotifications).Returns(true);

            // Act
            await _starterFunction.RunAsync(message, _messageActions.Object, _durableClient.Object);

            // Assert
            _messageActions.Verify(x => x.DeferMessageAsync(message, null, It.IsAny<CancellationToken>()), Times.Once());
            _messageActions.Verify(x => x.CompleteMessageAsync(message, It.IsAny<CancellationToken>()), Times.Never());
            _deferredNotificationsRepository.Verify(x => x.AddDeferredNotificationAsync(
                It.Is<DeferredNotification>(d => d.MessageType == messageType)), Times.Once());
            _durableClient.Verify(x => x.ScheduleNewOrchestrationInstanceAsync(
                It.IsAny<TaskName>(), It.IsAny<object>(), It.IsAny<StartOrchestrationOptions>(), It.IsAny<CancellationToken>()), Times.Never());
        }

        [TestMethod]
        public async Task RunAsync_WhenNotificationTypeNotFound_ProcessesNormally()
        {
            // Arrange
            var messageType = NotificationMessageType.ThresholdNotification;
            var message = CreateMessage(messageType);

            _notificationTypesRepository
                .Setup(x => x.GetNotificationTypeByNotificationTypeNameAsync(messageType))
                .ReturnsAsync((NotificationType)null);

            _mailConfig.Setup(x => x.SkipEmailNotifications).Returns(false);

            // Act
            await _starterFunction.RunAsync(message, _messageActions.Object, _durableClient.Object);

            // Assert
            _messageActions.Verify(x => x.DeferMessageAsync(message, null, It.IsAny<CancellationToken>()), Times.Never());
            _messageActions.Verify(x => x.CompleteMessageAsync(message, It.IsAny<CancellationToken>()), Times.Once());
            _durableClient.Verify(x => x.ScheduleNewOrchestrationInstanceAsync(
                It.Is<TaskName>(t => t.Name == nameof(OrchestratorFunction)), It.IsAny<object>(), It.IsAny<StartOrchestrationOptions>(), It.IsAny<CancellationToken>()), Times.Once());
        }

        [TestMethod]
        public async Task RunAsync_WhenUnknownMessageType_ProcessesNormally()
        {
            // Arrange - message with no MessageType property
            var messageContent = new Dictionary<string, object>
            {
                { "SyncJob", _syncJob },
                { "AdditionalContentParameters", new string[] { "param1" } }
            };

            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(messageContent));
            var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
                new BinaryData(body),
                sequenceNumber: 99999);

            _mailConfig.Setup(x => x.SkipEmailNotifications).Returns(false);

            // Act
            await _starterFunction.RunAsync(message, _messageActions.Object, _durableClient.Object);

            // Assert - should still process (unknown type is not suppressed)
            _messageActions.Verify(x => x.CompleteMessageAsync(message, It.IsAny<CancellationToken>()), Times.Once());
            _messageActions.Verify(x => x.DeferMessageAsync(message, null, It.IsAny<CancellationToken>()), Times.Never());
        }
    }
}
