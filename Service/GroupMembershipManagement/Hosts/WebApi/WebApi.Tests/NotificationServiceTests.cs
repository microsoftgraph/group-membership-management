// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging.Abstractions;
using Models;
using Models.Notifications;
using Models.ServiceBus;
using Models.SyncJobChange;
using Moq;
using Repositories.Contracts;
using Repositories.Contracts.DestinationResolution;
using Services.WebApi;
using Services.WebApi.Contracts;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace WebApi.Tests
{
    [TestClass]
    public class NotificationServiceTests
    {
        private Mock<IServiceBusQueueRepository> _mockServiceBusQueueRepository = null!;
        private Mock<IGraphGroupRepository> _mockGraphGroupRepository = null!;
        private Mock<IDestinationResolver> _mockDestinationResolver = null!;
        private NotificationService _notificationService = null!;
        private SyncJob _testSyncJob = null!;
        private SyncJobChange _testSubmission = null!;

        [TestInitialize]
        public void Initialize()
        {
            _mockServiceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            _mockGraphGroupRepository = new Mock<IGraphGroupRepository>();
            _mockDestinationResolver = new Mock<IDestinationResolver>();

            _notificationService = new NotificationService(
                _mockServiceBusQueueRepository.Object,
                NullLogger<NotificationService>.Instance,
                _mockGraphGroupRepository.Object,
                _mockDestinationResolver.Object);

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
            StringAssert.StartsWith(capturedMessage.MessageId, $"{_testSyncJob.Id}_{_testSyncJob.RunId}_{NotificationMessageType.SubmissionRejectedNotification}_");
            Assert.IsTrue(capturedMessage.MessageId.Length <= 128, "MessageId must stay within the Service Bus 128-char limit.");
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
        public async Task SendReviewStatusChangeNotificationAsync_UseBoundary_GroupDestinationResolved_UsesResolvedObjectId()
        {
            // T035/US2: group-name lookup and the {0} Group ID content parameter must use the resolved boundary
            // identity, not the legacy TargetOfficeGroupId scalar, once the boundary resolves a genuine destination.
            var resolvedObjectId = Guid.NewGuid();
            _mockDestinationResolver
                .Setup(x => x.ResolveAsync(It.IsAny<SyncJob>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ResolvedGroupDestination { SyncJobId = _testSyncJob.Id, ObjectId = resolvedObjectId });
            _mockGraphGroupRepository
                .Setup(x => x.GetGroupNameAsync(resolvedObjectId))
                .ReturnsAsync("Resolved Group Name");

            ServiceBusMessage capturedMessage = null!;
            _mockServiceBusQueueRepository
                .Setup(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>()))
                .Callback<ServiceBusMessage>(msg => capturedMessage = msg)
                .Returns(Task.CompletedTask);

            await _notificationService.SendSubmissionRejectedNotificationAsync(_testSyncJob, _testSubmission);

            _mockGraphGroupRepository.Verify(x => x.GetGroupNameAsync(resolvedObjectId), Times.Once);
            var messageBodyString = System.Text.Encoding.UTF8.GetString(capturedMessage.Body);
            var messageContent = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(messageBodyString);
            var additionalContentParameters = messageContent!["AdditionalContentParameters"]
                .EnumerateArray().Select(e => e.GetString()).ToArray();
            Assert.AreEqual(resolvedObjectId.ToString(), additionalContentParameters[0]);
            Assert.AreEqual("Resolved Group Name", additionalContentParameters[1]);
        }

        [TestMethod]
        public async Task SendReviewStatusChangeNotificationAsync_UseBoundary_TeamsChannelDestinationResolved_UsesTeamObjectId_NotChannelId()
        {
            // T036/US2: Teams-channel reroutes must key the group-name lookup and {0} content parameter off
            // TeamObjectId (historical TargetOfficeGroupId semantics), never leaking ChannelId into that slot.
            var resolvedTeamObjectId = Guid.NewGuid();
            _mockDestinationResolver
                .Setup(x => x.ResolveAsync(It.IsAny<SyncJob>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ResolvedTeamsChannelDestination
                {
                    SyncJobId = _testSyncJob.Id,
                    TeamObjectId = resolvedTeamObjectId,
                    ChannelId = "19:channel123@thread.tacv2"
                });
            _mockGraphGroupRepository
                .Setup(x => x.GetGroupNameAsync(resolvedTeamObjectId))
                .ReturnsAsync("Resolved Team Name");

            ServiceBusMessage capturedMessage = null!;
            _mockServiceBusQueueRepository
                .Setup(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>()))
                .Callback<ServiceBusMessage>(msg => capturedMessage = msg)
                .Returns(Task.CompletedTask);

            await _notificationService.SendSubmissionRejectedNotificationAsync(_testSyncJob, _testSubmission);

            var messageBodyString = System.Text.Encoding.UTF8.GetString(capturedMessage.Body);
            var messageContent = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(messageBodyString);
            var additionalContentParameters = messageContent!["AdditionalContentParameters"]
                .EnumerateArray().Select(e => e.GetString()).ToArray();
            Assert.AreEqual(resolvedTeamObjectId.ToString(), additionalContentParameters[0]);
            Assert.AreNotEqual("19:channel123@thread.tacv2", additionalContentParameters[0]);
        }

        [TestMethod]
        public async Task SendSubmissionRejectedNotificationAsync_UnsetChangeId_StillProducesDistinctMessageIds()
        {
            // Production path: SyncJobChange.Id is unset (Guid.Empty), but rejections must still get distinct MessageIds so neither email is dropped.
            var messageIds = new List<string>();
            _mockServiceBusQueueRepository
                .Setup(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>()))
                .Callback<ServiceBusMessage>(msg => messageIds.Add(msg.MessageId))
                .Returns(Task.CompletedTask);

            var firstRejection = new SyncJobChange { SyncJobId = _testSyncJob.Id, BusinessJustification = "First" };
            var secondRejection = new SyncJobChange { SyncJobId = _testSyncJob.Id, BusinessJustification = "Second" };

            await _notificationService.SendSubmissionRejectedNotificationAsync(_testSyncJob, firstRejection);
            await _notificationService.SendSubmissionRejectedNotificationAsync(_testSyncJob, secondRejection);

            Assert.AreEqual(2, messageIds.Count);
            Assert.AreNotEqual(messageIds[0], messageIds[1], "Rejections with an unset change id must still get distinct MessageIds.");
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
                () => new NotificationService(null!, NullLogger<NotificationService>.Instance, _mockGraphGroupRepository.Object, _mockDestinationResolver.Object));
        }

        [TestMethod]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(
                () => new NotificationService(_mockServiceBusQueueRepository.Object, null!, _mockGraphGroupRepository.Object, _mockDestinationResolver.Object));
        }

        [TestMethod]
        public async Task SendSubmissionApprovedNotificationAsync_Success_SendsCorrectMessage()
        {
            // Arrange
            var submission = new SyncJobChange
            {
                Id = Guid.NewGuid(),
                SyncJobId = _testSyncJob.Id,
                ChangedByObjectId = Guid.NewGuid(),
                ChangedByDisplayName = "Test User",
                ChangeReason = SyncJobChangeReason.SubmissionApproved.ToString(),
                ChangeTime = DateTime.UtcNow,
                ChangeSource = SyncJobChangeSource.WebApp
            };

            ServiceBusMessage capturedMessage = null!;
            _mockServiceBusQueueRepository
                .Setup(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>()))
                .Callback<ServiceBusMessage>(msg => capturedMessage = msg)
                .Returns(Task.CompletedTask);

            // Act
            await _notificationService.SendSubmissionApprovedNotificationAsync(_testSyncJob, submission);

            // Assert
            _mockServiceBusQueueRepository.Verify(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>()), Times.Once);
            
            Assert.IsNotNull(capturedMessage);
            Assert.AreEqual($"{_testSyncJob.Id}_{_testSyncJob.RunId}_{NotificationMessageType.SubmissionApprovedNotification}", capturedMessage.MessageId);
            Assert.IsTrue(capturedMessage.ApplicationProperties.ContainsKey("MessageType"));
            Assert.AreEqual(NotificationMessageType.SubmissionApprovedNotification.ToString(), capturedMessage.ApplicationProperties["MessageType"]);

            // Verify the message body contains the expected data
            var messageBodyString = System.Text.Encoding.UTF8.GetString(capturedMessage.Body);
            var messageContent = JsonSerializer.Deserialize<Dictionary<string, object>>(messageBodyString);
            
            Assert.IsNotNull(messageContent);
            Assert.IsTrue(messageContent.ContainsKey("SyncJob"));
            Assert.IsTrue(messageContent.ContainsKey("SubmitterObjectId"));
            Assert.IsTrue(messageContent.ContainsKey("SubmitterDisplayName"));
        }

        [TestMethod]
        public async Task SendSubmissionApprovedNotificationAsync_WithNullSubmitterData_HandlesGracefully()
        {
            // Arrange
            var submissionWithNulls = new SyncJobChange
            {
                Id = Guid.NewGuid(),
                SyncJobId = _testSyncJob.Id,
                ChangedByObjectId = null,
                ChangedByDisplayName = null,
                ChangeReason = SyncJobChangeReason.SubmissionApproved.ToString(),
                ChangeTime = DateTime.UtcNow,
                ChangeSource = SyncJobChangeSource.WebApp
            };

            ServiceBusMessage capturedMessage = null!;
            _mockServiceBusQueueRepository
                .Setup(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>()))
                .Callback<ServiceBusMessage>(msg => capturedMessage = msg)
                .Returns(Task.CompletedTask);

            // Act
            await _notificationService.SendSubmissionApprovedNotificationAsync(_testSyncJob, submissionWithNulls);

            // Assert
            _mockServiceBusQueueRepository.Verify(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>()), Times.Once);
            
            // Verify the message body contains empty strings for null values
            var messageBodyString = System.Text.Encoding.UTF8.GetString(capturedMessage.Body);
            var messageContent = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(messageBodyString);
            
            Assert.IsNotNull(messageContent);
            Assert.AreEqual(string.Empty, messageContent["SubmitterObjectId"].GetString());
            Assert.AreEqual(string.Empty, messageContent["SubmitterDisplayName"].GetString());
        }

    }
}
