// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging.Abstractions;
using Models.ThresholdNotifications;
using Moq;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services;
using Services.Messages.Requests;
using System;
using System.Net;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class GetThresholdNotificationHandlerTests
    {
        private Mock<INotificationRepository> _notificationRepository = null!;
        private Mock<IHandleInactiveJobsConfig> _handleInactiveJobsConfig = null!;
        private GetThresholdNotificationHandler _handler = null!;
        private Guid _syncJobId;

        [TestInitialize]
        public void Initialize()
        {
            _syncJobId = Guid.NewGuid();
            _notificationRepository = new Mock<INotificationRepository>();
            _handleInactiveJobsConfig = new Mock<IHandleInactiveJobsConfig>();
            _handleInactiveJobsConfig.Setup(x => x.NumberOfDaysBeforePurging).Returns(30);

            _handler = new GetThresholdNotificationHandler(
                NullLogger<GetThresholdNotificationHandler>.Instance,
                _notificationRepository.Object,
                _handleInactiveJobsConfig.Object);
        }

        /// <summary>
        /// When an unresolved notification exists, it is returned and IsResolved is false. The
        /// already-resolved fallback lookup is not performed.
        /// </summary>
        [TestMethod]
        public async Task ExecuteCoreAsync_ReturnsUnresolvedNotificationAsync()
        {
            var notification = new ThresholdNotification
            {
                Id = Guid.NewGuid(),
                SyncJobId = _syncJobId,
                ChangeQuantityForAdditions = 42,
                ChangePercentageForAdditions = 60,
                ThresholdPercentageForAdditions = 10,
                Status = ThresholdNotificationStatus.AwaitingResponse,
                Resolution = ThresholdNotificationResolution.Unresolved
            };
            _notificationRepository.Setup(x => x.GetThresholdNotificationBySyncJobIdAsync(_syncJobId))
                .ReturnsAsync(notification);

            var response = await _handler.ExecuteAsync(new GetThresholdNotificationRequest(_syncJobId));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(notification.Id, response.NotificationId);
            Assert.AreEqual(42, response.ChangeQuantityForAdditions);
            Assert.IsFalse(response.IsResolved);
            Assert.IsNull(response.ResolvedBy);
            _notificationRepository.Verify(x => x.GetLatestThresholdNotificationBySyncJobIdAsync(It.IsAny<Guid>()), Times.Never);
        }

        /// <summary>
        /// When no unresolved notification exists but a resolved one does, the handler surfaces the
        /// current resolved state so the Web UI can show who resolved it (FR-014).
        /// </summary>
        [TestMethod]
        public async Task ExecuteCoreAsync_ReturnsResolvedStateWhenAlreadyResolvedAsync()
        {
            var resolvedTime = DateTime.UtcNow.AddHours(-2);
            var notification = new ThresholdNotification
            {
                Id = Guid.NewGuid(),
                SyncJobId = _syncJobId,
                Status = ThresholdNotificationStatus.Resolved,
                Resolution = ThresholdNotificationResolution.IgnoreOnce,
                ResolvedBy = "owner@contoso.net",
                ResolvedTime = resolvedTime
            };
            _notificationRepository.Setup(x => x.GetThresholdNotificationBySyncJobIdAsync(_syncJobId))
                .ReturnsAsync((ThresholdNotification)null!);
            _notificationRepository.Setup(x => x.GetLatestThresholdNotificationBySyncJobIdAsync(_syncJobId))
                .ReturnsAsync(notification);

            var response = await _handler.ExecuteAsync(new GetThresholdNotificationRequest(_syncJobId));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsTrue(response.IsResolved);
            Assert.AreEqual("owner@contoso.net", response.ResolvedBy);
            Assert.AreEqual(resolvedTime, response.ResolvedTime);
            Assert.AreEqual(ThresholdNotificationResolution.IgnoreOnce.ToString(), response.Resolution);
        }

        /// <summary>
        /// When neither an unresolved nor a resolved notification exists, the handler returns 404.
        /// </summary>
        [TestMethod]
        public async Task ExecuteCoreAsync_ReturnsNotFoundWhenNoNotificationAsync()
        {
            _notificationRepository.Setup(x => x.GetThresholdNotificationBySyncJobIdAsync(_syncJobId))
                .ReturnsAsync((ThresholdNotification)null!);
            _notificationRepository.Setup(x => x.GetLatestThresholdNotificationBySyncJobIdAsync(_syncJobId))
                .ReturnsAsync((ThresholdNotification)null!);

            var response = await _handler.ExecuteAsync(new GetThresholdNotificationRequest(_syncJobId));

            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        }
    }
}
