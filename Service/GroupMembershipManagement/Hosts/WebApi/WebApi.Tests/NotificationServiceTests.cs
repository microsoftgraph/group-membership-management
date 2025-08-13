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
using System.Text.Json;

namespace WebApi.Tests
{
    [TestClass]
    public class NotificationServiceTests
    {
        private Mock<IServiceBusQueueRepository> _mockServiceBusQueueRepository = null!;
        private Mock<ILoggingRepository> _mockLoggingRepository = null!;
        private NotificationService _notificationService = null!;
        private SyncJob _testSyncJob = null!;
        private SyncJobChange _testSubmission = null!;

        [TestInitialize]
        public void Initialize()
        {
            _mockServiceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            _mockLoggingRepository = new Mock<ILoggingRepository>();
            
            _notificationService = new NotificationService(
                _mockServiceBusQueueRepository.Object,
                _mockLoggingRepository.Object);

            _testSyncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                Status = SyncStatus.PendingReview.ToString(),
                Requestor = "test@contoso.com",
                Query = "TestQuery",
                Period = 24,
                TargetOfficeGroupId = Guid.NewGuid()
            };

            _testSubmission = new SyncJobChange
            {
                Id = Guid.NewGuid(),
                SyncJobId = _testSyncJob.Id,
                ChangedByObjectId = Guid.NewGuid(),
                ChangedByDisplayName = "Test User",
                BusinessJustification = "Test business justification",
                ChangeReason = SyncJobChangeReason.SubmissionRejected.ToString(),
                ChangeTime = DateTime.UtcNow,
                ChangeSource = SyncJobChangeSource.WebApp
            };
        }

        [TestMethod]
        public async Task SendSubmissionRejectedNotificationAsync_Success_SendsCorrectMessage()
        {
            // Arrange
            ServiceBusMessage capturedMessage = null!;
            _mockServiceBusQueueRepository
                .Setup(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>()))
                .Callback<ServiceBusMessage>(msg => capturedMessage = msg)
                .Returns(Task.CompletedTask);

            // Act
            await _notificationService.SendSubmissionRejectedNotificationAsync(_testSyncJob, _testSubmission);

            // Assert
            _mockServiceBusQueueRepository.Verify(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>()), Times.Once);
            
            Assert.IsNotNull(capturedMessage);
            Assert.AreEqual($"{_testSyncJob.Id}_{_testSyncJob.RunId}_{NotificationMessageType.SubmissionRejectedNotification}", capturedMessage.MessageId);
            Assert.IsTrue(capturedMessage.ApplicationProperties.ContainsKey("MessageType"));
            Assert.AreEqual(NotificationMessageType.SubmissionRejectedNotification.ToString(), capturedMessage.ApplicationProperties["MessageType"]);

            // Verify the message body contains the expected data
            var messageBodyString = System.Text.Encoding.UTF8.GetString(capturedMessage.Body);
            var messageContent = JsonSerializer.Deserialize<Dictionary<string, object>>(messageBodyString);
            
            Assert.IsNotNull(messageContent);
            Assert.IsTrue(messageContent.ContainsKey("SyncJob"));
            Assert.IsTrue(messageContent.ContainsKey("SubmitterObjectId"));
            Assert.IsTrue(messageContent.ContainsKey("SubmitterDisplayName"));
            Assert.IsTrue(messageContent.ContainsKey("BusinessJustification"));
        }

        [TestMethod]
        public async Task SendSubmissionRejectedNotificationAsync_WithNullSubmitterData_HandlesGracefully()
        {
            // Arrange
            var submissionWithNulls = new SyncJobChange
            {
                Id = Guid.NewGuid(),
                SyncJobId = _testSyncJob.Id,
                ChangedByObjectId = null,
                ChangedByDisplayName = null,
                BusinessJustification = null,
                ChangeReason = SyncJobChangeReason.SubmissionRejected.ToString(),
                ChangeTime = DateTime.UtcNow,
                ChangeSource = SyncJobChangeSource.WebApp
            };

            ServiceBusMessage capturedMessage = null!;
            _mockServiceBusQueueRepository
                .Setup(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>()))
                .Callback<ServiceBusMessage>(msg => capturedMessage = msg)
                .Returns(Task.CompletedTask);

            // Act
            await _notificationService.SendSubmissionRejectedNotificationAsync(_testSyncJob, submissionWithNulls);

            // Assert
            _mockServiceBusQueueRepository.Verify(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>()), Times.Once);
            
            // Verify the message body contains empty strings for null values, except BusinessJustification which defaults to "No reason provided"
            var messageBodyString = System.Text.Encoding.UTF8.GetString(capturedMessage.Body);
            var messageContent = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(messageBodyString);
            
            Assert.IsNotNull(messageContent);
            Assert.AreEqual(string.Empty, messageContent["SubmitterObjectId"].GetString());
            Assert.AreEqual(string.Empty, messageContent["SubmitterDisplayName"].GetString());
            Assert.AreEqual("No reason provided", messageContent["BusinessJustification"].GetString());
        }

        [TestMethod]
        public async Task SendNotificationAsync_Success_SendsCorrectMessage()
        {
            // Arrange
            var customProperties = new Dictionary<string, object>
            {
                { "TestProperty1", "TestValue1" },
                { "TestProperty2", 42 }
            };

            ServiceBusMessage capturedMessage = null!;
            _mockServiceBusQueueRepository
                .Setup(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>()))
                .Callback<ServiceBusMessage>(msg => capturedMessage = msg)
                .Returns(Task.CompletedTask);            // Act
            await _notificationService.SendNotificationAsync(_testSyncJob, NotificationMessageType.SyncCompletedNotification, customProperties);

            // Assert
            _mockServiceBusQueueRepository.Verify(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>()), Times.Once);
            
            Assert.IsNotNull(capturedMessage);
            Assert.AreEqual($"{_testSyncJob.Id}_{_testSyncJob.RunId}_{NotificationMessageType.SyncCompletedNotification}", capturedMessage.MessageId);
            Assert.AreEqual(NotificationMessageType.SyncCompletedNotification.ToString(), capturedMessage.ApplicationProperties["MessageType"]);

            // Verify custom properties are included
            var messageBodyString = System.Text.Encoding.UTF8.GetString(capturedMessage.Body);
            var messageContent = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(messageBodyString);
            
            Assert.IsNotNull(messageContent);
            Assert.IsTrue(messageContent.ContainsKey("SyncJob"));
            Assert.IsTrue(messageContent.ContainsKey("TestProperty1"));
            Assert.IsTrue(messageContent.ContainsKey("TestProperty2"));
            Assert.AreEqual("TestValue1", messageContent["TestProperty1"].GetString());
            Assert.AreEqual(42, messageContent["TestProperty2"].GetInt32());
        }

        [TestMethod]
        public async Task SendNotificationAsync_WithoutCustomProperties_SendsBasicMessage()
        {
            // Arrange
            ServiceBusMessage capturedMessage = null!;
            _mockServiceBusQueueRepository
                .Setup(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>()))
                .Callback<ServiceBusMessage>(msg => capturedMessage = msg)
                .Returns(Task.CompletedTask);            // Act
            await _notificationService.SendNotificationAsync(_testSyncJob, NotificationMessageType.ThresholdNotification);

            // Assert
            _mockServiceBusQueueRepository.Verify(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>()), Times.Once);
            
            var messageBodyString = System.Text.Encoding.UTF8.GetString(capturedMessage.Body);
            var messageContent = JsonSerializer.Deserialize<Dictionary<string, object>>(messageBodyString);
            
            Assert.IsNotNull(messageContent);
            Assert.IsTrue(messageContent.ContainsKey("SyncJob"));
            Assert.AreEqual(1, messageContent.Count); // Only SyncJob should be present
        }

        [TestMethod]
        public void Constructor_WithNullServiceBusRepository_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(
                () => new NotificationService(null!, _mockLoggingRepository.Object));
        }

        [TestMethod]
        public void Constructor_WithNullLoggingRepository_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(
                () => new NotificationService(_mockServiceBusQueueRepository.Object, null!));
        }

    }
}
