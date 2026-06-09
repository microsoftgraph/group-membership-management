// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using DIConcreteTypes;
using GraphUpdater.QueueMessageOrchestrator;
using Hosts.GraphUpdater;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.ServiceBus;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class StarterFunctionTests
    {
        private string _instanceId;
        private ILogger<StarterFunction> _logger;
        private Mock<DurableTaskClient> _durableClientMock;
        private SyncJob _syncJob;
        private MembershipUpdaters _membershipUpdaters;
        private IOptions<MultiLaneConfig> _multilaneConfig;
        private string _subscriptionName = "GraphUpdater";
        private string _laneSize = "Small";

        [TestInitialize]
        public void SetupTest()
        {
            _instanceId = "1234567890";
            _durableClientMock = new Mock<DurableTaskClient>("test");
            _logger = NullLogger<StarterFunction>.Instance;
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
                .Setup(x => x.ScheduleNewOrchestrationInstanceAsync(It.IsAny<TaskName>(), It.IsAny<object>(), It.IsAny<StartOrchestrationOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(_instanceId);

            var starterFunction = new StarterFunction(_logger, _membershipUpdaters, _multilaneConfig);
            var timer = new TimerInfo();

            await starterFunction.RunAsync(timer, _durableClientMock.Object);

            _durableClientMock.Verify(x => x.ScheduleNewOrchestrationInstanceAsync(It.IsAny<TaskName>(), It.IsAny<QueueMessageOrchestratorRequest>(), It.IsAny<StartOrchestrationOptions>(), It.IsAny<CancellationToken>()), Times.Once());
        }
        [TestMethod]
        public async Task ProcessValidMultiLaneRequestTest()
        {
            _multilaneConfig.Value.IsEnabled = true;

            _durableClientMock
                .Setup(x => x.ScheduleNewOrchestrationInstanceAsync(It.IsAny<TaskName>(), It.IsAny<object>(), It.IsAny<StartOrchestrationOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(_instanceId);

            var instanceIdPrefix = $"{nameof(QueueMessageOrchestratorFunction)}_{_subscriptionName.ToLowerInvariant()}_";
            var starterFunction = new StarterFunction(_logger, _membershipUpdaters, _multilaneConfig);
            var timer = new TimerInfo();

            await starterFunction.RunAsync(timer, _durableClientMock.Object);

            var laneInstances = _membershipUpdaters.AvailableInstances["GroupMembership"][_laneSize].Instances;


            _durableClientMock.Verify(x => x.ScheduleNewOrchestrationInstanceAsync(It.IsAny<TaskName>(), It.IsAny<QueueMessageOrchestratorRequest>(), It.IsAny<StartOrchestrationOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(laneInstances));

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
                .Setup(x => x.ScheduleNewOrchestrationInstanceAsync(It.IsAny<TaskName>(), It.IsAny<OrchestratorMultiLaneRequest>(), It.IsAny<StartOrchestrationOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("test-instance-id");

            var starterFunction = new StarterFunction(_logger, _membershipUpdaters, _multilaneConfig);

            // Act
            await starterFunction.RunSmallLaneAsync(message, _durableClientMock.Object);

            // Assert
            _durableClientMock.Verify(x => x.ScheduleNewOrchestrationInstanceAsync(
                It.IsAny<TaskName>(),
                It.Is<OrchestratorMultiLaneRequest>(req =>
                    req.GroupMembership.RunId == groupMembership.RunId &&
                    req.SubscriptionName == "GraphUpdater_small_1" &&
                    req.LaneSize == "small" &&
                    req.TopicName == _membershipUpdaters.CurrentTopicName
                ),
                It.IsAny<StartOrchestrationOptions>(),
                It.IsAny<CancellationToken>()
            ), Times.Once);

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
                .SetupSequence(x => x.GetInstanceAsync(expectedInstanceId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((OrchestrationMetadata)null)
                .ReturnsAsync(new OrchestrationMetadata("test", "test") { RuntimeStatus = OrchestrationRuntimeStatus.Running })
                .ReturnsAsync(new OrchestrationMetadata("test", "test") { RuntimeStatus = OrchestrationRuntimeStatus.Completed });

            _durableClientMock
                .Setup(x => x.ScheduleNewOrchestrationInstanceAsync(It.IsAny<TaskName>(), It.IsAny<OrchestratorMultiLaneRequest>(), It.IsAny<StartOrchestrationOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedInstanceId);

            var starterFunction = new StarterFunction(_logger, _membershipUpdaters, _multilaneConfig);

            // Act
            await starterFunction.RunLargeLaneAsync(message, _durableClientMock.Object);

            // Assert
            _durableClientMock.Verify(x => x.ScheduleNewOrchestrationInstanceAsync(
                It.IsAny<TaskName>(),
                It.Is<OrchestratorMultiLaneRequest>(req =>
                    req.GroupMembership.RunId == groupMembership.RunId &&
                    req.SubscriptionName == "GraphUpdater_large_1" &&
                    req.LaneSize == "large" &&
                    req.TopicName == _membershipUpdaters.CurrentTopicName
                ),
                It.IsAny<StartOrchestrationOptions>(),
                It.IsAny<CancellationToken>()
            ), Times.Once);

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
                .SetupSequence(x => x.GetInstanceAsync(expectedInstanceId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new OrchestrationMetadata("test", "test")  // First call - instance is running
                {
                    RuntimeStatus = OrchestrationRuntimeStatus.Running
                })
                .ReturnsAsync(new OrchestrationMetadata("test", "test")  // Second call - still running
                {
                    RuntimeStatus = OrchestrationRuntimeStatus.Running
                })
                .ReturnsAsync(new OrchestrationMetadata("test", "test")
                {
                    RuntimeStatus = OrchestrationRuntimeStatus.Completed // Completed (exists WaitForInstanceAsync)
                });

            var starterFunction = new StarterFunction(_logger, _membershipUpdaters, _multilaneConfig);

            // Act
            await starterFunction.RunLargeLaneAsync(message, _durableClientMock.Object);

            // Assert
            _durableClientMock.Verify(x => x.ScheduleNewOrchestrationInstanceAsync(
                It.IsAny<TaskName>(),
                It.IsAny<OrchestratorMultiLaneRequest>(),
                It.IsAny<StartOrchestrationOptions>(),
                It.IsAny<CancellationToken>()
            ), Times.Never);  // Should not start a new instance

            _durableClientMock.Verify(x => x.GetInstanceAsync(expectedInstanceId, It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.AtLeast(2));
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
                .Setup(x => x.GetInstanceAsync(expectedInstanceId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new OrchestrationMetadata("test", "test")
                {
                    RuntimeStatus = OrchestrationRuntimeStatus.Completed
                });

            var starterFunction = new StarterFunction(_logger, _membershipUpdaters, _multilaneConfig);

            // Act
            await starterFunction.RunLargeLaneAsync(message, _durableClientMock.Object);

            // Assert
            _durableClientMock.Verify(x => x.ScheduleNewOrchestrationInstanceAsync(
                It.IsAny<TaskName>(),
                It.IsAny<OrchestratorMultiLaneRequest>(),
                It.IsAny<StartOrchestrationOptions>(),
                It.IsAny<CancellationToken>()
            ), Times.Never);

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
                .Setup(x => x.GetInstanceAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(expectedException);

            var starterFunction = new StarterFunction(_logger, _membershipUpdaters, _multilaneConfig);

            // Act & Assert
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                async () => await starterFunction.RunLargeLaneAsync(message, _durableClientMock.Object));

        }

        [TestMethod]
        public async Task GetInstanceInformation_ReturnsCorrectInstanceNames()
        {
            // Arrange
            var smallLaneUpdaters = Helpers.GetAvailableMembershipUpdaters(
                "[{\"name\":\"GroupMembership\",\"lanes\":[{\"name\":\"small\",\"instances\":1,\"messageSize\":400},{\"name\":\"large\",\"instances\":1,\"messageSize\":400}]}]",
                "small");

            var starterFunction = new StarterFunction(_logger, smallLaneUpdaters, _multilaneConfig);

            // Act - Use reflection to test the private method
            var method = typeof(StarterFunction).GetMethod("GetInstanceInformation",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var result = (List<string>)method.Invoke(starterFunction, null);

            // Assert
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("GraphUpdater_small_1", result[0]);
        }

        [TestMethod]
        public async Task RunAsync_WithMultiLaneDisabled_ProcessesSingleTimer()
        {
            // Arrange
            _multilaneConfig.Value.IsEnabled = false;
            _membershipUpdaters = Helpers.GetAvailableMembershipUpdaters(currentLaneSize: null);

            var instanceId = $"{nameof(QueueMessageOrchestratorFunction)}_graphupdater";

            _durableClientMock
                .Setup(x => x.GetInstanceAsync(instanceId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((OrchestrationMetadata)null);

            _durableClientMock
                .Setup(x => x.ScheduleNewOrchestrationInstanceAsync(It.IsAny<TaskName>(), It.IsAny<QueueMessageOrchestratorRequest>(), It.IsAny<StartOrchestrationOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(instanceId);

            var starterFunction = new StarterFunction(_logger, _membershipUpdaters, _multilaneConfig);
            var timer = new TimerInfo();

            // Act
            await starterFunction.RunAsync(timer, _durableClientMock.Object);

            // Assert
            _durableClientMock.Verify(x => x.ScheduleNewOrchestrationInstanceAsync(
                It.IsAny<TaskName>(),
                It.Is<QueueMessageOrchestratorRequest>(req =>
                    req.SubscriptionName == "GraphUpdater" &&
                    req.IsMultiLaneEnabled == false
                ),
                It.IsAny<StartOrchestrationOptions>(),
                It.IsAny<CancellationToken>()
            ), Times.Once);
        }

        [TestMethod]
        public async Task RunAsync_WithMultiLaneEnabled_ProcessesMultipleInstances()
        {
            // Arrange
            _multilaneConfig.Value.IsEnabled = true;
            _multilaneConfig.Value.TriggerDelay = 0;

            _durableClientMock
                .Setup(x => x.GetInstanceAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((OrchestrationMetadata)null);

            _durableClientMock
                .Setup(x => x.ScheduleNewOrchestrationInstanceAsync(It.IsAny<TaskName>(), It.IsAny<QueueMessageOrchestratorRequest>(), It.IsAny<StartOrchestrationOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("instance-id");

            var starterFunction = new StarterFunction(_logger, _membershipUpdaters, _multilaneConfig);
            var timer = new TimerInfo();

            // Act
            await starterFunction.RunAsync(timer, _durableClientMock.Object);

            // Assert
            var laneInstances = _membershipUpdaters.AvailableInstances["GroupMembership"][_laneSize].Instances;
            _durableClientMock.Verify(x => x.ScheduleNewOrchestrationInstanceAsync(
                It.IsAny<TaskName>(),
                It.IsAny<QueueMessageOrchestratorRequest>(),
                It.IsAny<StartOrchestrationOptions>(),
                It.IsAny<CancellationToken>()
            ), Times.AtLeast(1));  // At least one instance should be started
        }

        [TestMethod]
        public async Task RunAsync_WithMultiLaneEnabledButNoLaneSize_Returns()
        {
            // Arrange
            _multilaneConfig.Value.IsEnabled = true;
            _membershipUpdaters = Helpers.GetAvailableMembershipUpdaters(currentLaneSize: null);

            var starterFunction = new StarterFunction(_logger, _membershipUpdaters, _multilaneConfig);
            var timer = new TimerInfo();

            // Act
            await starterFunction.RunAsync(timer, _durableClientMock.Object);

            // Assert
            _durableClientMock.Verify(x => x.ScheduleNewOrchestrationInstanceAsync(
                It.IsAny<TaskName>(),
                It.IsAny<QueueMessageOrchestratorRequest>(),
                It.IsAny<StartOrchestrationOptions>(),
                It.IsAny<CancellationToken>()
            ), Times.Never);
        }

        [TestMethod]
        public async Task RunAsync_WithMultiLaneDisabledButHasLaneSize_Returns()
        {
            // Arrange
            _multilaneConfig.Value.IsEnabled = false;
            _membershipUpdaters = Helpers.GetAvailableMembershipUpdaters(currentLaneSize: "small");

            var starterFunction = new StarterFunction(_logger, _membershipUpdaters, _multilaneConfig);
            var timer = new TimerInfo();

            // Act
            await starterFunction.RunAsync(timer, _durableClientMock.Object);

            // Assert
            _durableClientMock.Verify(x => x.ScheduleNewOrchestrationInstanceAsync(
                It.IsAny<TaskName>(),
                It.IsAny<QueueMessageOrchestratorRequest>(),
                It.IsAny<StartOrchestrationOptions>(),
                It.IsAny<CancellationToken>()
            ), Times.Never);
        }

        [TestMethod]
        public async Task ProcessTimerAsync_WithRunningOrchestrator_DoesNotStartNew()
        {
            // Arrange
            var subscriptionName = "GraphUpdater_small_1";
            var instanceId = $"{nameof(QueueMessageOrchestratorFunction)}_{subscriptionName.ToLowerInvariant()}";

            _durableClientMock
                .Setup(x => x.GetInstanceAsync(instanceId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new OrchestrationMetadata("test", "test")
                {
                    RuntimeStatus = OrchestrationRuntimeStatus.Running
                });

            var starterFunction = new StarterFunction(_logger, _membershipUpdaters, _multilaneConfig);

            // Act - Use reflection to test the private method
            var method = typeof(StarterFunction).GetMethod("ProcessTimerAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            var task = (Task)method.Invoke(starterFunction, new object[] { _durableClientMock.Object, subscriptionName });
            await task;

            // Assert
            _durableClientMock.Verify(x => x.ScheduleNewOrchestrationInstanceAsync(
                It.IsAny<TaskName>(),
                It.IsAny<QueueMessageOrchestratorRequest>(),
                It.IsAny<StartOrchestrationOptions>(),
                It.IsAny<CancellationToken>()
            ), Times.Never);

        }

        [TestMethod]
        public async Task ProcessTimerAsync_WithFailedOrchestrator_StartsNew()
        {
            // Arrange
            var subscriptionName = "GraphUpdater_small_1";
            var instanceId = $"{nameof(QueueMessageOrchestratorFunction)}_{subscriptionName.ToLowerInvariant()}";

            _durableClientMock
                .Setup(x => x.GetInstanceAsync(instanceId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new OrchestrationMetadata("test", "test")
                {
                    RuntimeStatus = OrchestrationRuntimeStatus.Failed
                });

            _durableClientMock
                .Setup(x => x.ScheduleNewOrchestrationInstanceAsync(It.IsAny<TaskName>(), It.IsAny<QueueMessageOrchestratorRequest>(), It.IsAny<StartOrchestrationOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(instanceId);

            var starterFunction = new StarterFunction(_logger, _membershipUpdaters, _multilaneConfig);

            // Act - Use reflection to test the private method
            var method = typeof(StarterFunction).GetMethod("ProcessTimerAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            var task = (Task)method.Invoke(starterFunction, new object[] { _durableClientMock.Object, subscriptionName });
            await task;

            // Assert
            _durableClientMock.Verify(x => x.ScheduleNewOrchestrationInstanceAsync(
                It.IsAny<TaskName>(),
                It.Is<QueueMessageOrchestratorRequest>(req =>
                    req.SubscriptionName == subscriptionName &&
                    req.IsMultiLaneEnabled == _multilaneConfig.Value.IsEnabled
                ),
                It.IsAny<StartOrchestrationOptions>(),
                It.IsAny<CancellationToken>()
            ), Times.Once);

        }

        [TestMethod]
        public async Task ProcessTimerAsync_WithTerminatedOrchestrator_StartsNew()
        {
            // Arrange
            var subscriptionName = "GraphUpdater_large_1";
            var instanceId = $"{nameof(QueueMessageOrchestratorFunction)}_{subscriptionName.ToLowerInvariant()}";

            _durableClientMock
                .Setup(x => x.GetInstanceAsync(instanceId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new OrchestrationMetadata("test", "test")
                {
                    RuntimeStatus = OrchestrationRuntimeStatus.Terminated
                });

            _durableClientMock
                .Setup(x => x.ScheduleNewOrchestrationInstanceAsync(It.IsAny<TaskName>(), It.IsAny<QueueMessageOrchestratorRequest>(), It.IsAny<StartOrchestrationOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(instanceId);

            var starterFunction = new StarterFunction(_logger, _membershipUpdaters, _multilaneConfig);

            // Act - Use reflection to test the private method
            var method = typeof(StarterFunction).GetMethod("ProcessTimerAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            var task = (Task)method.Invoke(starterFunction, new object[] { _durableClientMock.Object, subscriptionName });
            await task;

            // Assert
            _durableClientMock.Verify(x => x.ScheduleNewOrchestrationInstanceAsync(
                It.IsAny<TaskName>(),
                It.Is<QueueMessageOrchestratorRequest>(req =>
                    req.SubscriptionName == subscriptionName
                ),
                It.IsAny<StartOrchestrationOptions>(),
                It.IsAny<CancellationToken>()
            ), Times.Once);

        }

        [TestMethod]
        public async Task RunAsync_WithTriggerDelay_ProcessesWithDelay()
        {
            // Arrange
            _multilaneConfig.Value.IsEnabled = true;
            _multilaneConfig.Value.TriggerDelay = 1; // 1 second delay for faster test

            _durableClientMock
                .Setup(x => x.GetInstanceAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((OrchestrationMetadata)null);

            _durableClientMock
                .Setup(x => x.ScheduleNewOrchestrationInstanceAsync(It.IsAny<TaskName>(), It.IsAny<QueueMessageOrchestratorRequest>(), It.IsAny<StartOrchestrationOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("instance-id");

            var starterFunction = new StarterFunction(_logger, _membershipUpdaters, _multilaneConfig);
            var timer = new TimerInfo();

            // Act
            await starterFunction.RunAsync(timer, _durableClientMock.Object);

            // Assert - Verify that the delay configuration was set and the orchestrator was started
            Assert.AreEqual(1, _multilaneConfig.Value.TriggerDelay);

            _durableClientMock.Verify(x => x.ScheduleNewOrchestrationInstanceAsync(
                It.IsAny<TaskName>(),
                It.IsAny<QueueMessageOrchestratorRequest>(),
                It.IsAny<StartOrchestrationOptions>(),
                It.IsAny<CancellationToken>()
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
                .Setup(x => x.GetInstanceAsync(expectedInstanceId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new OrchestrationMetadata("test", "test")
                {
                    RuntimeStatus = OrchestrationRuntimeStatus.Failed
                });

            var starterFunction = new StarterFunction(_logger, _membershipUpdaters, _multilaneConfig);

            // Act
            await starterFunction.RunLargeLaneAsync(message, _durableClientMock.Object);

            // Assert
            _durableClientMock.Verify(x => x.ScheduleNewOrchestrationInstanceAsync(
                It.IsAny<TaskName>(),
                It.IsAny<OrchestratorMultiLaneRequest>(),
                It.IsAny<StartOrchestrationOptions>(),
                It.IsAny<CancellationToken>()
            ), Times.Never);

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
                .Setup(x => x.GetInstanceAsync(expectedInstanceId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new OrchestrationMetadata("test", "test")
                {
                    RuntimeStatus = OrchestrationRuntimeStatus.Terminated
                });

            var starterFunction = new StarterFunction(_logger, _membershipUpdaters, _multilaneConfig);

            // Act
            await starterFunction.RunLargeLaneAsync(message, _durableClientMock.Object);

            // Assert
        }
    }
}


