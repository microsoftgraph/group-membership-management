// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using MembershipAggregator.Services.Entities;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.ServiceBus;
using Models.ThresholdNotifications;
using Moq;
using Polly;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.Mocks;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class DeltaCalculatorServiceTests
    {
        private Mock<IDatabaseSyncJobsRepository> _syncJobRepository = null!;
        private Mock<IDatabaseGroupsRepository> _databaseGroupsRepository = null!;
        private Mock<IDatabaseChannelsRepository> _databaseChannelsRepository = null!;
        private Mock<IGraphAPIService> _graphAPIService = null!;
        private Mock<INotificationRepository> _notificationRepository = null!;
        private Mock<IServiceBusQueueRepository> _notificationsQueueRepository = null!;
        private Mock<IDryRunValue> _dryRunValue = null!;

        private SyncJob _syncJob = null!;
        private Guid _targetGroupId;

        [TestInitialize]
        public void Setup()
        {
            _targetGroupId = Guid.NewGuid();

            _syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                MembershipType = MembershipTypes.GroupMembership.ToString(),
                Status = SyncStatus.InProgress.ToString(),
                // Not an initial sync — an initial sync skips threshold evaluation entirely.
                LastRunTime = DateTime.UtcNow.AddDays(-1),
                // Generous thresholds so the delta below stays under them.
                ThresholdPercentageForAdditions = 90,
                ThresholdPercentageForRemovals = 90,
                Group = new Group { GroupId = _targetGroupId }
            };

            _syncJobRepository = new Mock<IDatabaseSyncJobsRepository>();
            _syncJobRepository.Setup(x => x.GetSyncJobAsync(It.IsAny<Guid>())).ReturnsAsync(() => _syncJob);

            _databaseGroupsRepository = new Mock<IDatabaseGroupsRepository>();
            _databaseChannelsRepository = new Mock<IDatabaseChannelsRepository>();

            _graphAPIService = new Mock<IGraphAPIService>();
            _graphAPIService
                .Setup(x => x.GroupExistsAsync(It.IsAny<Guid>()))
                .ReturnsAsync(PolicyResult<bool>.Successful(true, new Context()));

            _notificationRepository = new Mock<INotificationRepository>();
            _notificationsQueueRepository = new Mock<IServiceBusQueueRepository>();

            _dryRunValue = new Mock<IDryRunValue>();
            _dryRunValue.Setup(x => x.DryRunEnabled).Returns(false);
        }

        private DeltaCalculatorService CreateService()
        {
            return new DeltaCalculatorService(
                _syncJobRepository.Object,
                new MockDestinationResolver(_databaseGroupsRepository.Object, _databaseChannelsRepository.Object),
                NullLogger<DeltaCalculatorService>.Instance,
                _graphAPIService.Object,
                _dryRunValue.Object,
                _notificationRepository.Object,
                _notificationsQueueRepository.Object,
                new TelemetryClient(new TelemetryConfiguration()));
        }

        /// <summary>
        /// Builds a source/destination pair whose delta is well under the job's thresholds,
        /// so the non-exceeded ("recovered") branch is taken.
        /// </summary>
        private (GroupMembership Source, GroupMembership Destination) BuildUnderThresholdMembership()
        {
            var shared = new List<AzureADUser>();
            for (var i = 0; i < 10; i++)
            {
                shared.Add(new AzureADUser { ObjectId = Guid.NewGuid() });
            }

            var source = new GroupMembership
            {
                SyncJobId = _syncJob.Id,
                RunId = _syncJob.RunId ?? Guid.NewGuid(),
                Destination = new AzureADGroup { ObjectId = _targetGroupId },
                SourceMembers = new List<AzureADUser>(shared)
            };

            var destination = new GroupMembership
            {
                SyncJobId = _syncJob.Id,
                RunId = _syncJob.RunId ?? Guid.NewGuid(),
                Destination = new AzureADGroup { ObjectId = _targetGroupId },
                SourceMembers = new List<AzureADUser>(shared)
            };

            return (source, destination);
        }

        /// <summary>
        /// FR-006 / SC-004 — a job that recovered from a threshold violation must have its lingering
        /// unresolved notification auto-closed as SelfCorrected on the next healthy run.
        ///
        /// The non-exceeded, non-initial branch now closes any lingering unresolved notification
        /// unconditionally (the close is self-guarding), so a recovered job self-corrects on its
        /// next healthy run. This test asserts that close happens.
        /// </summary>
        [TestMethod]
        public async Task CalculateDifferenceAsync_UnderThresholdWithUnresolvedNotification_ClosesNotificationAsSelfCorrected()
        {
            var unresolved = new ThresholdNotification
            {
                Id = Guid.NewGuid(),
                SyncJobId = _syncJob.Id,
                TargetOfficeGroupId = _targetGroupId,
                Status = ThresholdNotificationStatus.AwaitingResponse,
                Resolution = ThresholdNotificationResolution.Unresolved
            };

            _notificationRepository
                .Setup(x => x.GetThresholdNotificationBySyncJobIdAsync(_syncJob.Id))
                .ReturnsAsync(unresolved);

            var (source, destination) = BuildUnderThresholdMembership();

            var response = await CreateService().CalculateDifferenceAsync(source, destination);

            Assert.AreNotEqual(MembershipDeltaStatus.ThresholdExceeded, response.MembershipDeltaStatus);

            _notificationRepository.Verify(
                x => x.SaveNotificationAsync(It.Is<ThresholdNotification>(n =>
                    n.Status == ThresholdNotificationStatus.Resolved &&
                    n.Resolution == ThresholdNotificationResolution.SelfCorrected)),
                Times.Once());
        }

        /// <summary>
        /// FR-006 — a job with no unresolved notification must be unaffected. The close path
        /// self-guards on the notification lookup, so it no-ops without error.
        /// </summary>
        [TestMethod]
        public async Task CalculateDifferenceAsync_UnderThresholdWithNoNotification_DoesNotSaveAnyNotification()
        {
            _notificationRepository
                .Setup(x => x.GetThresholdNotificationBySyncJobIdAsync(_syncJob.Id))
                .ReturnsAsync((ThresholdNotification)null!);

            var (source, destination) = BuildUnderThresholdMembership();

            var response = await CreateService().CalculateDifferenceAsync(source, destination);

            Assert.AreNotEqual(MembershipDeltaStatus.ThresholdExceeded, response.MembershipDeltaStatus);

            _notificationRepository.Verify(
                x => x.SaveNotificationAsync(It.IsAny<ThresholdNotification>()),
                Times.Never());
        }

        /// <summary>
        /// FR-006 — an already-resolved notification must not be re-saved. Guards against the
        /// unconditional close introducing duplicate resolution writes on every healthy run.
        /// </summary>
        [TestMethod]
        public async Task CalculateDifferenceAsync_UnderThresholdWithResolvedNotification_DoesNotResaveNotification()
        {
            var alreadyResolved = new ThresholdNotification
            {
                Id = Guid.NewGuid(),
                SyncJobId = _syncJob.Id,
                TargetOfficeGroupId = _targetGroupId,
                Status = ThresholdNotificationStatus.Resolved,
                Resolution = ThresholdNotificationResolution.SelfCorrected
            };

            _notificationRepository
                .Setup(x => x.GetThresholdNotificationBySyncJobIdAsync(_syncJob.Id))
                .ReturnsAsync(alreadyResolved);

            var (source, destination) = BuildUnderThresholdMembership();

            await CreateService().CalculateDifferenceAsync(source, destination);

            _notificationRepository.Verify(
                x => x.SaveNotificationAsync(It.IsAny<ThresholdNotification>()),
                Times.Never());
        }
    }
}
