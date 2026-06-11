// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Models.Notifications;
using Models.ServiceBus;
using Models.SyncJobChange;
using Moq;
using Repositories.Contracts;
using Services.WebApi;
using Services.WebApi.Contracts;

namespace WebApi.Tests
{
    [TestClass]
    public class NotificationServiceIntegrationTests
    {
        [TestMethod]
        public void NotificationService_ImplementsINotificationService()
        {
            // Arrange
            var mockServiceBusRepository = new Mock<IServiceBusQueueRepository>();
            var mockLoggingRepository = new Mock<ILoggingRepository>();
            var mockGraphGroupRepository = new Mock<IGraphGroupRepository>();

            // Act
            var notificationService = new NotificationService(
                mockServiceBusRepository.Object,
                mockLoggingRepository.Object,
                mockGraphGroupRepository.Object);

            // Assert
            Assert.IsInstanceOfType(notificationService, typeof(INotificationService));
        }

        [TestMethod]
        public async Task NotificationService_SendSubmissionRejectedNotificationAsync_UsesCorrectNotificationType()
        {
            // Arrange
            var mockServiceBusRepository = new Mock<IServiceBusQueueRepository>();
            var mockLoggingRepository = new Mock<ILoggingRepository>();
            var mockGraphGroupRepository = new Mock<IGraphGroupRepository>();

            ServiceBusMessage capturedMessage = null!;
            mockServiceBusRepository
                .Setup(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>()))
                .Callback<ServiceBusMessage>(msg => capturedMessage = msg)
                .Returns(Task.CompletedTask);

            var notificationService = new NotificationService(
                mockServiceBusRepository.Object,
                mockLoggingRepository.Object,
                mockGraphGroupRepository.Object);

            var syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid()
            };

            var submission = new SyncJobChange
            {
                ChangedByObjectId = Guid.NewGuid(),
                ChangedByDisplayName = "Test User",
                BusinessJustification = "Test justification"
            };

            // Act
            await notificationService.SendSubmissionRejectedNotificationAsync(syncJob, submission);

            // Assert
            Assert.IsNotNull(capturedMessage);
            Assert.AreEqual(NotificationMessageType.SubmissionRejectedNotification.ToString(), 
                capturedMessage.ApplicationProperties["MessageType"]);
        }

        [TestMethod]
        public void NotificationService_HasCorrectDependencies()
        {
            // This test ensures that the NotificationService correctly depends on the expected interfaces
            var mockServiceBusRepository = new Mock<IServiceBusQueueRepository>();
            var mockLoggingRepository = new Mock<ILoggingRepository>();
            var mockGraphGroupRepository = new Mock<IGraphGroupRepository>();

            // This should not throw any exceptions
            var notificationService = new NotificationService(
                mockServiceBusRepository.Object,
                mockLoggingRepository.Object,
                mockGraphGroupRepository.Object);

            Assert.IsNotNull(notificationService);
        }
    }
}
