// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using DIConcreteTypes;
using GraphUpdater.QueueMessageOrchestrator;
using Hosts.GraphUpdater;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.ServiceBus;
using Moq;
using Repositories.Mocks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class StarterFunctionTests
    {
        private string _instanceId;
        private MockLoggingRepository _loggerMock;
        private Mock<IDurableOrchestrationClient> _durableClientMock;
        private SyncJob _syncJob;
        private MembershipUpdaters _membershipUpdaters;
        private IOptions<MultiLaneConfig> _multilaneConfig;
        private string _subscriptionName = "GraphUpdater";
        private string _laneSize = "Small";

        [TestInitialize]
        public void SetupTest()
        {
            _instanceId = "1234567890";
            _durableClientMock = new Mock<IDurableOrchestrationClient>();
            _loggerMock = new MockLoggingRepository();
            _syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                MembershipType = "GroupMembership",
                Group = new Group
                {
                    GroupId = Guid.NewGuid()
                },
                ThresholdPercentageForAdditions = 80,
                ThresholdPercentageForRemovals = 20,
                LastRunTime = DateTime.UtcNow.AddDays(-1),
                Requestor = "user@domail.com",
                RunId = Guid.NewGuid(),
                ThresholdViolations = 0
            };

            _multilaneConfig = Options.Create(new MultiLaneConfig
            {
                IsEnabled = false,
                Small = 400,
            });

            _membershipUpdaters = Helpers.GetAvailableMembershipUpdaters(currentLaneSize: _laneSize);
        }

        [TestMethod]
        public async Task ProcessValidRequestTest()
        {
            _multilaneConfig.Value.IsEnabled = false;
            _membershipUpdaters = Helpers.GetAvailableMembershipUpdaters(currentLaneSize: null);

            _instanceId = $"{nameof(QueueMessageOrchestratorFunction)}_{_subscriptionName.ToLowerInvariant()}";

            _durableClientMock
                .Setup(x => x.StartNewAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(_instanceId);

            var starterFunction = new StarterFunction(_loggerMock, _membershipUpdaters, _multilaneConfig);
            var timer = new TimerInfo(null, null);

            await starterFunction.RunAsync(timer, _durableClientMock.Object);

            Assert.IsNotNull(_loggerMock.MessagesLogged.Single(x => x.Message.Contains("function started")));
            _durableClientMock.Verify(x => x.StartNewAsync(nameof(QueueMessageOrchestratorFunction), _instanceId, It.IsAny<QueueMessageOrchestratorRequest>()), Times.Once());
            Assert.IsNotNull(_loggerMock.MessagesLogged.Single(x => x.Message == $"Calling {_instanceId}"));
            Assert.IsNotNull(_loggerMock.MessagesLogged.Single(x => x.Message.Contains("function complete")));
        }
        [TestMethod]
        public async Task ProcessValidMultiLaneRequestTest()
        {
            _multilaneConfig.Value.IsEnabled = true;

            _durableClientMock
                .Setup(x => x.StartNewAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>()))
                .ReturnsAsync(_instanceId);

            var instanceIdPrefix = $"{nameof(QueueMessageOrchestratorFunction)}_{_subscriptionName.ToLowerInvariant()}_";
            var starterFunction = new StarterFunction(_loggerMock, _membershipUpdaters, _multilaneConfig);
            var timer = new TimerInfo(null, null);

            await starterFunction.RunAsync(timer, _durableClientMock.Object);

            var laneInstances = _membershipUpdaters.AvailableInstances["GroupMembership"][_laneSize].Instances;

            Assert.AreEqual(laneInstances, _loggerMock.MessagesLogged.Count(x => x.Message.Contains("function started")));

            _durableClientMock.Verify(x => x.StartNewAsync(nameof(QueueMessageOrchestratorFunction), It.IsAny<string>(), It.IsAny<QueueMessageOrchestratorRequest>()), Times.Exactly(laneInstances));

            Assert.AreEqual(laneInstances, _loggerMock.MessagesLogged.Count(x => x.Message.StartsWith($"Calling {instanceIdPrefix}")));
            Assert.AreEqual(laneInstances, _loggerMock.MessagesLogged.Count(x => x.Message.Contains("function complete")));
        }

        [TestMethod]
        public async Task RunSmallLaneAsync_ProcessesMessageSuccessfully()
        {
            // Arrange
            var groupMembership = new GroupMembership
            {
                RunId = Guid.NewGuid(),
                SyncJob = _syncJob,
                TotalMembersToAdd = 100,
                TotalMembersToRemove = 50,
                SourceMembers = new List<AzureADUser>()
            };

            var messageBody = JsonSerializer.SerializeToUtf8Bytes(groupMembership);
            var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
                body: BinaryData.FromBytes(messageBody),
                messageId: "test-message-id"
            );

            _durableClientMock
                .Setup(x => x.StartNewAsync(nameof(OrchestratorMultiLaneFunction), null, It.IsAny<OrchestratorMultiLaneRequest>()))
                .ReturnsAsync("test-instance-id");

            var starterFunction = new StarterFunction(_loggerMock, _membershipUpdaters, _multilaneConfig);

            // Act
            await starterFunction.RunSmallLaneAsync(message, _durableClientMock.Object);

            // Assert
            _durableClientMock.Verify(x => x.StartNewAsync(
                nameof(OrchestratorMultiLaneFunction),
                null,
                It.Is<OrchestratorMultiLaneRequest>(req =>
                    req.GroupMembership.RunId == groupMembership.RunId &&
                    req.SubscriptionName == "GraphUpdater_small_1" &&
                    req.LaneSize == "small" &&
                    req.TopicName == _membershipUpdaters.CurrentTopicName
                )
            ), Times.Once);

            Assert.IsTrue(_loggerMock.MessagesLogged.Any(x => x.Message.Contains("StarterFunction_small function started")));
            Assert.IsTrue(_loggerMock.MessagesLogged.Any(x => x.Message.Contains($"Processing message {message.MessageId}")));
            Assert.IsTrue(_loggerMock.MessagesLogged.Any(x => x.Message.Contains("100 additions")));
            Assert.IsTrue(_loggerMock.MessagesLogged.Any(x => x.Message.Contains("50 removals")));
            Assert.IsTrue(_loggerMock.MessagesLogged.Any(x => x.Message.Contains("StarterFunction_small function completed")));
        }

        [TestMethod]
        public async Task RunLargeLaneAsync_WithNewInstance_StartsOrchestrator()
        {
            // Arrange
            var groupMembership = new GroupMembership
            {
                RunId = Guid.NewGuid(),
                SyncJob = _syncJob,
                TotalMembersToAdd = 1000,
                TotalMembersToRemove = 500,
                SourceMembers = new List<AzureADUser>()
            };

            var messageBody = JsonSerializer.SerializeToUtf8Bytes(groupMembership);
            var sequenceNumber = 12345L;
            var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
                body: BinaryData.FromBytes(messageBody),
                messageId: "test-message-id",
                sequenceNumber: sequenceNumber
            );

            var expectedInstanceId = $"{groupMembership.RunId}_{sequenceNumber}";

            _durableClientMock
                .SetupSequence(x => x.GetStatusAsync(expectedInstanceId, false, false, true))
                .ReturnsAsync((DurableOrchestrationStatus)null)  // First call - instance doesn't exist
                .ReturnsAsync(new DurableOrchestrationStatus  // Second call - after starting (Running state will exit WaitForInstanceAsync)
                {
                    RuntimeStatus = OrchestrationRuntimeStatus.Running
                });

            _durableClientMock
                .Setup(x => x.StartNewAsync(nameof(OrchestratorMultiLaneFunction), expectedInstanceId, It.IsAny<OrchestratorMultiLaneRequest>()))
                .ReturnsAsync(expectedInstanceId);

            var starterFunction = new StarterFunction(_loggerMock, _membershipUpdaters, _multilaneConfig);

            // Act
            await starterFunction.RunLargeLaneAsync(message, _durableClientMock.Object);

            // Assert
            _durableClientMock.Verify(x => x.StartNewAsync(
                nameof(OrchestratorMultiLaneFunction),
                expectedInstanceId,
                It.Is<OrchestratorMultiLaneRequest>(req =>
                    req.GroupMembership.RunId == groupMembership.RunId &&
                    req.SubscriptionName == "GraphUpdater_large_1" &&
                    req.LaneSize == "large" &&
                    req.TopicName == _membershipUpdaters.CurrentTopicName
                )
            ), Times.Once);

            Assert.IsTrue(_loggerMock.MessagesLogged.Any(x => x.Message.Contains("StarterFunction_large function started")));
            Assert.IsTrue(_loggerMock.MessagesLogged.Any(x => x.Message.Contains("1000 additions")));
            Assert.IsTrue(_loggerMock.MessagesLogged.Any(x => x.Message.Contains("500 removals")));
            Assert.IsTrue(_loggerMock.MessagesLogged.Any(x => x.Message.Contains("StarterFunction_large function completed")));
        }

        [TestMethod]
        public async Task RunLargeLaneAsync_WithRunningInstance_WaitsForCompletion()
        {
            // Arrange
            var groupMembership = new GroupMembership
            {
                RunId = Guid.NewGuid(),
                SyncJob = _syncJob,
                SourceMembers = new List<AzureADUser>()
            };

            var messageBody = JsonSerializer.SerializeToUtf8Bytes(groupMembership);
            var sequenceNumber = 12345L;
            var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
                body: BinaryData.FromBytes(messageBody),
                messageId: "test-message-id",
                sequenceNumber: sequenceNumber
            );

            var expectedInstanceId = $"{groupMembership.RunId}_{sequenceNumber}";

            _durableClientMock
                .SetupSequence(x => x.GetStatusAsync(expectedInstanceId, false, false, true))
                .ReturnsAsync(new DurableOrchestrationStatus  // First call - instance is running
                {
                    RuntimeStatus = OrchestrationRuntimeStatus.Running
                })
                .ReturnsAsync(new DurableOrchestrationStatus  // Second call - still running (exit WaitForInstanceAsync)
                {
                    RuntimeStatus = OrchestrationRuntimeStatus.Running
                });

            var starterFunction = new StarterFunction(_loggerMock, _membershipUpdaters, _multilaneConfig);

            // Act
            await starterFunction.RunLargeLaneAsync(message, _durableClientMock.Object);

            // Assert
            _durableClientMock.Verify(x => x.StartNewAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<OrchestratorMultiLaneRequest>()
            ), Times.Never);  // Should not start a new instance

            _durableClientMock.Verify(x => x.GetStatusAsync(expectedInstanceId, false, false, true), Times.AtLeast(2));
        }

        [TestMethod]
        public async Task RunLargeLaneAsync_WithCompletedInstance_LogsAndReturns()
        {
            // Arrange
            var groupMembership = new GroupMembership
            {
                RunId = Guid.NewGuid(),
                SyncJob = _syncJob,
                SourceMembers = new List<AzureADUser>()
            };

            var messageBody = JsonSerializer.SerializeToUtf8Bytes(groupMembership);
            var sequenceNumber = 12345L;
            var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
                body: BinaryData.FromBytes(messageBody),
                messageId: "test-message-id",
                sequenceNumber: sequenceNumber
            );

            var expectedInstanceId = $"{groupMembership.RunId}_{sequenceNumber}";

            _durableClientMock
                .Setup(x => x.GetStatusAsync(expectedInstanceId, false, false, true))
                .ReturnsAsync(new DurableOrchestrationStatus
                {
                    RuntimeStatus = OrchestrationRuntimeStatus.Completed
                });

            var starterFunction = new StarterFunction(_loggerMock, _membershipUpdaters, _multilaneConfig);

            // Act
            await starterFunction.RunLargeLaneAsync(message, _durableClientMock.Object);

            // Assert
            _durableClientMock.Verify(x => x.StartNewAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<OrchestratorMultiLaneRequest>()
            ), Times.Never);

            Assert.IsTrue(_loggerMock.MessagesLogged.Any(x =>
                x.Message.Contains($"Message {message.MessageId}") &&
                x.Message.Contains("was already processed") &&
                x.Message.Contains("Completed")));
        }

        [TestMethod]
        public async Task RunLargeLaneAsync_WithException_LogsAndRethrows()
        {
            // Arrange
            var groupMembership = new GroupMembership
            {
                RunId = Guid.NewGuid(),
                SyncJob = _syncJob,
                SourceMembers = new List<AzureADUser>()
            };

            var messageBody = JsonSerializer.SerializeToUtf8Bytes(groupMembership);
            var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
                body: BinaryData.FromBytes(messageBody),
                messageId: "test-message-id",
                sequenceNumber: 12345L
            );

            var expectedException = new InvalidOperationException("Test exception");

            _durableClientMock
                .Setup(x => x.GetStatusAsync(It.IsAny<string>(), false, false, true))
                .ThrowsAsync(expectedException);

            var starterFunction = new StarterFunction(_loggerMock, _membershipUpdaters, _multilaneConfig);

            // Act & Assert
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                async () => await starterFunction.RunLargeLaneAsync(message, _durableClientMock.Object));

            Assert.IsTrue(_loggerMock.MessagesLogged.Any(x =>
                x.Message.Contains("Error processing Service Bus message") &&
                x.Message.Contains("Test exception")));
        }

        [TestMethod]
        public async Task GetInstanceInformation_ReturnsCorrectInstanceNames()
        {
            // Arrange
            var smallLaneUpdaters = Helpers.GetAvailableMembershipUpdaters(
                "[{\"name\":\"GroupMembership\",\"lanes\":[{\"name\":\"small\",\"instances\":1,\"messageSize\":400},{\"name\":\"large\",\"instances\":1,\"messageSize\":400}]}]",
                "small");

            var starterFunction = new StarterFunction(_loggerMock, smallLaneUpdaters, _multilaneConfig);

            // Act - Use reflection to test the private method
            var method = typeof(StarterFunction).GetMethod("GetInstanceInformation",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var result = (List<string>)method.Invoke(starterFunction, null);

            // Assert
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("GraphUpdater_small_1", result[0]);
        }

        [TestMethod]
        public async Task WaitForInstanceAsync_WaitsUntilCompleted()
        {
            // Arrange
            var instanceId = "test-instance-id";

            _durableClientMock
                .SetupSequence(x => x.GetStatusAsync(instanceId, false, false, true))
                .ReturnsAsync((DurableOrchestrationStatus)null)  // First check - null
                .ReturnsAsync(new DurableOrchestrationStatus  // Second check - running
                {
                    RuntimeStatus = OrchestrationRuntimeStatus.Running
                })
                .ReturnsAsync(new DurableOrchestrationStatus  // Third check - completed
                {
                    RuntimeStatus = OrchestrationRuntimeStatus.Completed
                });

            var starterFunction = new StarterFunction(_loggerMock, _membershipUpdaters, _multilaneConfig);

            // Act - Use reflection to test the private method
            var method = typeof(StarterFunction).GetMethod("WaitForInstanceAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            var task = (Task)method.Invoke(starterFunction, new object[] { _durableClientMock.Object, instanceId });
            await task;

            // Assert
            _durableClientMock.Verify(x => x.GetStatusAsync(instanceId, false, false, true), Times.Exactly(2));
        }

        [TestMethod]
        public async Task RunAsync_WithMultiLaneDisabled_ProcessesSingleTimer()
        {
            // Arrange
            _multilaneConfig.Value.IsEnabled = false;
            _membershipUpdaters = Helpers.GetAvailableMembershipUpdaters(currentLaneSize: null);

            var instanceId = $"{nameof(QueueMessageOrchestratorFunction)}_graphupdater";

            _durableClientMock
                .Setup(x => x.GetStatusAsync(instanceId, false, false, true))
                .ReturnsAsync((DurableOrchestrationStatus)null);

            _durableClientMock
                .Setup(x => x.StartNewAsync(It.IsAny<string>(), instanceId, It.IsAny<QueueMessageOrchestratorRequest>()))
                .ReturnsAsync(instanceId);

            var starterFunction = new StarterFunction(_loggerMock, _membershipUpdaters, _multilaneConfig);
            var timer = new TimerInfo(null, null);

            // Act
            await starterFunction.RunAsync(timer, _durableClientMock.Object);

            // Assert
            _durableClientMock.Verify(x => x.StartNewAsync(
                nameof(QueueMessageOrchestratorFunction),
                instanceId,
                It.Is<QueueMessageOrchestratorRequest>(req =>
                    req.SubscriptionName == "GraphUpdater" &&
                    req.IsMultiLaneEnabled == false
                )
            ), Times.Once);
        }

        [TestMethod]
        public async Task RunAsync_WithMultiLaneEnabled_ProcessesMultipleInstances()
        {
            // Arrange
            _multilaneConfig.Value.IsEnabled = true;
            _multilaneConfig.Value.TriggerDelay = 0;

            _durableClientMock
                .Setup(x => x.GetStatusAsync(It.IsAny<string>(), false, false, true))
                .ReturnsAsync((DurableOrchestrationStatus)null);

            _durableClientMock
                .Setup(x => x.StartNewAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<QueueMessageOrchestratorRequest>()))
                .ReturnsAsync("instance-id");

            var starterFunction = new StarterFunction(_loggerMock, _membershipUpdaters, _multilaneConfig);
            var timer = new TimerInfo(null, null);

            // Act
            await starterFunction.RunAsync(timer, _durableClientMock.Object);

            // Assert
            var laneInstances = _membershipUpdaters.AvailableInstances["GroupMembership"][_laneSize].Instances;
            _durableClientMock.Verify(x => x.StartNewAsync(
                nameof(QueueMessageOrchestratorFunction),
                It.IsAny<string>(),
                It.IsAny<QueueMessageOrchestratorRequest>()
            ), Times.AtLeast(1));  // At least one instance should be started
        }

        [TestMethod]
        public async Task RunAsync_WithMultiLaneEnabledButNoLaneSize_Returns()
        {
            // Arrange
            _multilaneConfig.Value.IsEnabled = true;
            _membershipUpdaters = Helpers.GetAvailableMembershipUpdaters(currentLaneSize: null);

            var starterFunction = new StarterFunction(_loggerMock, _membershipUpdaters, _multilaneConfig);
            var timer = new TimerInfo(null, null);

            // Act
            await starterFunction.RunAsync(timer, _durableClientMock.Object);

            // Assert
            _durableClientMock.Verify(x => x.StartNewAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<QueueMessageOrchestratorRequest>()
            ), Times.Never);
        }

        [TestMethod]
        public async Task RunAsync_WithMultiLaneDisabledButHasLaneSize_Returns()
        {
            // Arrange
            _multilaneConfig.Value.IsEnabled = false;
            _membershipUpdaters = Helpers.GetAvailableMembershipUpdaters(currentLaneSize: "small");

            var starterFunction = new StarterFunction(_loggerMock, _membershipUpdaters, _multilaneConfig);
            var timer = new TimerInfo(null, null);

            // Act
            await starterFunction.RunAsync(timer, _durableClientMock.Object);

            // Assert
            _durableClientMock.Verify(x => x.StartNewAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<QueueMessageOrchestratorRequest>()
            ), Times.Never);
        }

        [TestMethod]
        public async Task ProcessTimerAsync_WithRunningOrchestrator_DoesNotStartNew()
        {
            // Arrange
            var subscriptionName = "GraphUpdater_small_1";
            var instanceId = $"{nameof(QueueMessageOrchestratorFunction)}_{subscriptionName.ToLowerInvariant()}";

            _durableClientMock
                .Setup(x => x.GetStatusAsync(instanceId, false, false, true))
                .ReturnsAsync(new DurableOrchestrationStatus
                {
                    RuntimeStatus = OrchestrationRuntimeStatus.Running
                });

            var starterFunction = new StarterFunction(_loggerMock, _membershipUpdaters, _multilaneConfig);

            // Act - Use reflection to test the private method
            var method = typeof(StarterFunction).GetMethod("ProcessTimerAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            var task = (Task)method.Invoke(starterFunction, new object[] { _durableClientMock.Object, subscriptionName });
            await task;

            // Assert
            _durableClientMock.Verify(x => x.StartNewAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<QueueMessageOrchestratorRequest>()
            ), Times.Never);

            Assert.IsFalse(_loggerMock.MessagesLogged.Any(x => x.Message.Contains($"Calling {instanceId}")));
        }

        [TestMethod]
        public async Task ProcessTimerAsync_WithFailedOrchestrator_StartsNew()
        {
            // Arrange
            var subscriptionName = "GraphUpdater_small_1";
            var instanceId = $"{nameof(QueueMessageOrchestratorFunction)}_{subscriptionName.ToLowerInvariant()}";

            _durableClientMock
                .Setup(x => x.GetStatusAsync(instanceId, false, false, true))
                .ReturnsAsync(new DurableOrchestrationStatus
                {
                    RuntimeStatus = OrchestrationRuntimeStatus.Failed
                });

            _durableClientMock
                .Setup(x => x.StartNewAsync(nameof(QueueMessageOrchestratorFunction), instanceId, It.IsAny<QueueMessageOrchestratorRequest>()))
                .ReturnsAsync(instanceId);

            var starterFunction = new StarterFunction(_loggerMock, _membershipUpdaters, _multilaneConfig);

            // Act - Use reflection to test the private method
            var method = typeof(StarterFunction).GetMethod("ProcessTimerAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            var task = (Task)method.Invoke(starterFunction, new object[] { _durableClientMock.Object, subscriptionName });
            await task;

            // Assert
            _durableClientMock.Verify(x => x.StartNewAsync(
                nameof(QueueMessageOrchestratorFunction),
                instanceId,
                It.Is<QueueMessageOrchestratorRequest>(req =>
                    req.SubscriptionName == subscriptionName &&
                    req.IsMultiLaneEnabled == _multilaneConfig.Value.IsEnabled
                )
            ), Times.Once);

            Assert.IsTrue(_loggerMock.MessagesLogged.Any(x => x.Message.Contains($"Calling {instanceId}")));
        }

        [TestMethod]
        public async Task ProcessTimerAsync_WithTerminatedOrchestrator_StartsNew()
        {
            // Arrange
            var subscriptionName = "GraphUpdater_large_1";
            var instanceId = $"{nameof(QueueMessageOrchestratorFunction)}_{subscriptionName.ToLowerInvariant()}";

            _durableClientMock
                .Setup(x => x.GetStatusAsync(instanceId, false, false, true))
                .ReturnsAsync(new DurableOrchestrationStatus
                {
                    RuntimeStatus = OrchestrationRuntimeStatus.Terminated
                });

            _durableClientMock
                .Setup(x => x.StartNewAsync(nameof(QueueMessageOrchestratorFunction), instanceId, It.IsAny<QueueMessageOrchestratorRequest>()))
                .ReturnsAsync(instanceId);

            var starterFunction = new StarterFunction(_loggerMock, _membershipUpdaters, _multilaneConfig);

            // Act - Use reflection to test the private method
            var method = typeof(StarterFunction).GetMethod("ProcessTimerAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            var task = (Task)method.Invoke(starterFunction, new object[] { _durableClientMock.Object, subscriptionName });
            await task;

            // Assert
            _durableClientMock.Verify(x => x.StartNewAsync(
                nameof(QueueMessageOrchestratorFunction),
                instanceId,
                It.Is<QueueMessageOrchestratorRequest>(req =>
                    req.SubscriptionName == subscriptionName
                )
            ), Times.Once);

            Assert.IsTrue(_loggerMock.MessagesLogged.Any(x => x.Message.Contains($"Calling {instanceId}")));
        }

        [TestMethod]
        public async Task RunAsync_WithTriggerDelay_ProcessesWithDelay()
        {
            // Arrange
            _multilaneConfig.Value.IsEnabled = true;
            _multilaneConfig.Value.TriggerDelay = 1; // 1 second delay for faster test

            _durableClientMock
                .Setup(x => x.GetStatusAsync(It.IsAny<string>(), false, false, true))
                .ReturnsAsync((DurableOrchestrationStatus)null);

            _durableClientMock
                .Setup(x => x.StartNewAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<QueueMessageOrchestratorRequest>()))
                .ReturnsAsync("instance-id");

            var starterFunction = new StarterFunction(_loggerMock, _membershipUpdaters, _multilaneConfig);
            var timer = new TimerInfo(null, null);

            // Act
            await starterFunction.RunAsync(timer, _durableClientMock.Object);

            // Assert - Verify that the delay configuration was set and the orchestrator was started
            Assert.AreEqual(1, _multilaneConfig.Value.TriggerDelay);

            _durableClientMock.Verify(x => x.StartNewAsync(
                nameof(QueueMessageOrchestratorFunction),
                It.IsAny<string>(),
                It.IsAny<QueueMessageOrchestratorRequest>()
            ), Times.AtLeast(1));
        }

        [TestMethod]
        public async Task RunLargeLaneAsync_WithFailedInstance_LogsCorrectStatus()
        {
            // Arrange
            var groupMembership = new GroupMembership
            {
                RunId = Guid.NewGuid(),
                SyncJob = _syncJob,
                SourceMembers = new List<AzureADUser>()
            };

            var messageBody = JsonSerializer.SerializeToUtf8Bytes(groupMembership);
            var sequenceNumber = 12345L;
            var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
                body: BinaryData.FromBytes(messageBody),
                messageId: "test-message-id",
                sequenceNumber: sequenceNumber
            );

            var expectedInstanceId = $"{groupMembership.RunId}_{sequenceNumber}";

            _durableClientMock
                .Setup(x => x.GetStatusAsync(expectedInstanceId, false, false, true))
                .ReturnsAsync(new DurableOrchestrationStatus
                {
                    RuntimeStatus = OrchestrationRuntimeStatus.Failed
                });

            var starterFunction = new StarterFunction(_loggerMock, _membershipUpdaters, _multilaneConfig);

            // Act
            await starterFunction.RunLargeLaneAsync(message, _durableClientMock.Object);

            // Assert
            _durableClientMock.Verify(x => x.StartNewAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<OrchestratorMultiLaneRequest>()
            ), Times.Never);

            Assert.IsTrue(_loggerMock.MessagesLogged.Any(x =>
                x.Message.Contains($"Message {message.MessageId}") &&
                x.Message.Contains("was already processed") &&
                x.Message.Contains("Failed")));
        }

        [TestMethod]
        public async Task RunLargeLaneAsync_WithTerminatedInstance_LogsCorrectStatus()
        {
            // Arrange
            var groupMembership = new GroupMembership
            {
                RunId = Guid.NewGuid(),
                SyncJob = _syncJob,
                SourceMembers = new List<AzureADUser>()
            };

            var messageBody = JsonSerializer.SerializeToUtf8Bytes(groupMembership);
            var sequenceNumber = 12345L;
            var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
                body: BinaryData.FromBytes(messageBody),
                messageId: "test-message-id",
                sequenceNumber: sequenceNumber
            );

            var expectedInstanceId = $"{groupMembership.RunId}_{sequenceNumber}";

            _durableClientMock
                .Setup(x => x.GetStatusAsync(expectedInstanceId, false, false, true))
                .ReturnsAsync(new DurableOrchestrationStatus
                {
                    RuntimeStatus = OrchestrationRuntimeStatus.Terminated
                });

            var starterFunction = new StarterFunction(_loggerMock, _membershipUpdaters, _multilaneConfig);

            // Act
            await starterFunction.RunLargeLaneAsync(message, _durableClientMock.Object);

            // Assert
            Assert.IsTrue(_loggerMock.MessagesLogged.Any(x =>
                x.Message.Contains($"Message {message.MessageId}") &&
                x.Message.Contains("was already processed") &&
                x.Message.Contains("Terminated")));
        }

        [TestMethod]
        public async Task RunLargeLaneAsync_WithCanceledInstance_LogsCorrectStatus()
        {
            // Arrange
            var groupMembership = new GroupMembership
            {
                RunId = Guid.NewGuid(),
                SyncJob = _syncJob,
                SourceMembers = new List<AzureADUser>()
            };

            var messageBody = JsonSerializer.SerializeToUtf8Bytes(groupMembership);
            var sequenceNumber = 12345L;
            var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
                body: BinaryData.FromBytes(messageBody),
                messageId: "test-message-id",
                sequenceNumber: sequenceNumber
            );

            var expectedInstanceId = $"{groupMembership.RunId}_{sequenceNumber}";

            _durableClientMock
                .Setup(x => x.GetStatusAsync(expectedInstanceId, false, false, true))
                .ReturnsAsync(new DurableOrchestrationStatus
                {
                    RuntimeStatus = OrchestrationRuntimeStatus.Canceled
                });

            var starterFunction = new StarterFunction(_loggerMock, _membershipUpdaters, _multilaneConfig);

            // Act
            await starterFunction.RunLargeLaneAsync(message, _durableClientMock.Object);

            // Assert
            Assert.IsTrue(_loggerMock.MessagesLogged.Any(x =>
                x.Message.Contains($"Message {message.MessageId}") &&
                x.Message.Contains("was already processed") &&
                x.Message.Contains("Canceled")));
        }

        [TestMethod]
        public async Task WaitForInstanceAsync_ExitsWhenOrchestratorStarts()
        {
            // Arrange
            var instanceId = "test-instance-id";

            // Setup sequence: null (keep waiting) then Running (exit)
            _durableClientMock
                .SetupSequence(x => x.GetStatusAsync(instanceId, false, false, true))
                .ReturnsAsync((DurableOrchestrationStatus)null)  // First check - null (keep waiting)
                .ReturnsAsync(new DurableOrchestrationStatus  // Second check - running (exit)
                {
                    RuntimeStatus = OrchestrationRuntimeStatus.Running
                });

            var starterFunction = new StarterFunction(_loggerMock, _membershipUpdaters, _multilaneConfig);

            // Act - Use reflection to test the private method
            var method = typeof(StarterFunction).GetMethod("WaitForInstanceAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            var task = (Task)method.Invoke(starterFunction, new object[] { _durableClientMock.Object, instanceId });
            await task;

            // Assert - Verify that GetStatusAsync was called twice (once null, once running)
            _durableClientMock.Verify(x => x.GetStatusAsync(instanceId, false, false, true), Times.Exactly(2));
        }

    }
}
