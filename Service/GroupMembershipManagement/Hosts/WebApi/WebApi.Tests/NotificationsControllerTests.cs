// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using DIConcreteTypes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Models;
using Models.SyncJobChange;
using Models.ThresholdNotifications;
using Moq;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.WebApi;
using System.Security.Claims;
using WebApi.Controllers.v1.Notifications;
using WebApi.Models.Requests;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using WebApi.Models;
using WebApi.Configuration;
using System;

namespace Services.Tests
{
    [TestClass]
    public class NotificationsControllerTests
    {
        private int _notificationCount = 10;
        private Guid _nonExistantNotificationId = Guid.Empty;
        private string _userUPN = null!;
        private ThresholdNotification _thresholdNotification = null!;
        private Guid _groupId = Guid.Empty;
        private List<AzureADGroup> _groups = null!;
        private Dictionary<Guid, string> _groupNames = null!;
        private List<string> _groupTypes = null!;
        private Mock<IGraphGroupRepository> _graphGroupRepository = null!;
        private Mock<INotificationRepository> _notificationRepository = null!;
        private Mock<IDatabaseSyncJobsRepository> _syncJobRepository = null!;
        private Mock<ISyncJobChangeRepository> _syncJobChangeRepository = null!;
        private Mock<IHttpContextAccessor> _httpContextAccessor = null!;
        private ResolveNotificationHandler _resolveNotificationsHandler = null!;
        private NotificationsController _notificationsController = null!;
        private List<ThresholdNotification> _thresholdNotifications = null!;
        private ResolveNotification _resolveNotificationModel = null!;
        private TelemetryClient _telemetryClient = null!;

        [TestInitialize]
        public void Initialize()
        {
            _userUPN = "testuser@contoso.net";
            _nonExistantNotificationId = Guid.Empty;

            _groups = new List<AzureADGroup>();
            _resolveNotificationModel = new ResolveNotification()
            {
                Resolution = "Paused"
            };

            _graphGroupRepository = new Mock<IGraphGroupRepository>();
            _notificationRepository = new Mock<INotificationRepository>();
            _syncJobRepository = new Mock<IDatabaseSyncJobsRepository>();
            _syncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            _telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());

            _groupTypes = new List<string>
            {
                "Microsoft 365",
                "Security",
                "Mail enabled security",
                "Distribution"
            };

            _thresholdNotifications = new List<ThresholdNotification>();
            _groups = new List<AzureADGroup>();
            _groupNames = new Dictionary<Guid, string>();

            // create groups and notifications with random ids.
            foreach (var index in Enumerable.Range(0, _notificationCount))
            {

                var group = new AzureADGroup
                {
                    ObjectId = Guid.NewGuid(),
                    Type = _groupTypes[Random.Shared.Next(0, _groupTypes.Count)]
                };

                var groupName = $"Test Group {index}";

                _graphGroupRepository.Setup(x => x.IsEmailRecipientOwnerOfGroupAsync(
                    It.Is<string>(s => s == _userUPN), It.Is<Guid>(groupId => groupId == group.ObjectId), It.IsAny<bool>()))
                    .ReturnsAsync(true);

                _graphGroupRepository.Setup(x => x.GetGroupNameAsync(It.Is<Guid>(g => g == group.ObjectId)))
                    .ReturnsAsync(() => groupName);

                var notification = new ThresholdNotification
                {
                    ChangePercentageForAdditions = Random.Shared.NextDouble() * (100 - 51) + 51,
                    ChangePercentageForRemovals = Random.Shared.NextDouble() * (100 - 51) + 51,
                    ChangeQuantityForAdditions = Random.Shared.Next(50, 1000),
                    ChangeQuantityForRemovals = Random.Shared.Next(50, 1000),
                    CreatedTime = DateTime.UtcNow,
                    Resolution = ThresholdNotificationResolution.Unresolved,
                    Id = Guid.NewGuid(),
                    SyncJobId = Guid.NewGuid(),
                    ResolvedBy = string.Empty,
                    ResolvedTime = DateTime.UtcNow,
                    Status = ThresholdNotificationStatus.AwaitingResponse,
                    TargetOfficeGroupId = group.ObjectId,
                    ThresholdPercentageForAdditions = Random.Shared.Next(1, 50),
                    ThresholdPercentageForRemovals = Random.Shared.Next(1, 50),
                    CardState = ThresholdNotificationCardState.DisabledCard
                };

                _groups.Add(group);
                _groupNames.Add(group.ObjectId, groupName);
                _thresholdNotifications.Add(notification);
            }

            _thresholdNotification = new ThresholdNotification()
            {
                ChangePercentageForAdditions = 0,
                ChangePercentageForRemovals = 0,
                CreatedTime = DateTime.UtcNow,
                Resolution = ThresholdNotificationResolution.Paused,
                Id = Guid.NewGuid(),
                ResolvedBy = _userUPN,
                ResolvedTime = DateTime.UtcNow,
                Status = ThresholdNotificationStatus.AwaitingResponse
            };

            _notificationRepository.Setup(x => x.GetThresholdNotificationByIdAsync(It.IsAny<Guid>()))
                .Returns<Guid>((id) => Task.FromResult(_thresholdNotifications.FirstOrDefault(notification => notification.Id == id)));

            var syncJob = new SyncJob();
            _syncJobRepository.Setup(x => x.GetSyncJobAsync(It.IsAny<Guid>()))
                .ReturnsAsync(() => syncJob);

            // Items for testing
            _thresholdNotification = _thresholdNotifications[Random.Shared.Next(0, _notificationCount)];
            _groupId = _thresholdNotification.TargetOfficeGroupId;

            _httpContextAccessor = new Mock<IHttpContextAccessor>();

            _resolveNotificationsHandler = new ResolveNotificationHandler(NullLogger<ResolveNotificationHandler>.Instance,
                _notificationRepository.Object,
                _syncJobRepository.Object,
                _syncJobChangeRepository.Object,
                _graphGroupRepository.Object,
                _telemetryClient,
                _httpContextAccessor.Object);

            var claims = new List<Claim>
            {
                new Claim("upn", _userUPN),
            };

            _notificationsController = new NotificationsController(_resolveNotificationsHandler);
            _notificationsController.ControllerContext = CreateControllerContext(claims, "mockBearerToken");
        }
        /// <summary>
        /// /notifications/{id}/resolve - Resolve notification with Ignore Once
        /// </summary>
        [TestMethod]
        public async Task ResolveNotification_WithIgnoreOnceTestAsync()
        {
            _resolveNotificationModel.Resolution = $"{ThresholdNotificationResolution.IgnoreOnce}";
            var response = await _notificationsController.ResolveNotificationAsync(_thresholdNotification.Id, _resolveNotificationModel);

            _notificationRepository.Verify(x => x.SaveNotificationAsync(_thresholdNotification), Times.Once);

            Assert.IsInstanceOfType(response, typeof(NoContentResult));
        }

        /// <summary>
        /// /notifications/{id}/resolve - Resolve notification with Pause sync job
        /// </summary>
        [TestMethod]
        public async Task ResolveNotification_WithPauseTestAsync()
        {
            _resolveNotificationModel.Resolution = $"{ThresholdNotificationResolution.Paused}";
            var response = await _notificationsController.ResolveNotificationAsync(_thresholdNotification.Id, _resolveNotificationModel);

            _notificationRepository.Verify(x => x.SaveNotificationAsync(_thresholdNotification), Times.Once);

            Assert.IsInstanceOfType(response, typeof(NoContentResult));
        }

        /// <summary>
        /// /notifications/{id}/resolve - Resolve notification that does not exist
        /// </summary>
        [TestMethod]
        public async Task ResolveNotification_HandleNotFoundTestAsync()
        {
            var response = await _notificationsController.ResolveNotificationAsync(_nonExistantNotificationId, _resolveNotificationModel);

            _notificationRepository.Verify(x => x.SaveNotificationAsync(_thresholdNotification), Times.Never);

            Assert.IsInstanceOfType(response, typeof(NotFoundResult));
        }

        /// <summary>
        /// /notifications/{id}/resolve - Resolve notification when user is not an owner
        /// </summary>
        [TestMethod]
        public async Task ResolveNotification_HandleUserNotGroupOwnerTestAsync()
        {
            var claims = new List<Claim>
            {
                new Claim("upn", "notAnOwner@contoso.net")
            };
            _notificationsController.ControllerContext = CreateControllerContext(claims, "mockBearerToken");

            var response = await _notificationsController.ResolveNotificationAsync(_thresholdNotification.Id, _resolveNotificationModel);

            _notificationRepository.Verify(x => x.SaveNotificationAsync(_thresholdNotification), Times.Never);

            Assert.IsInstanceOfType(response, typeof(ForbidResult));
        }

        /// <summary>
        /// /notifications/{id}/resolve - Resolve notification that is already resolved
        /// </summary>
        [TestMethod]
        public async Task ResolveNotification_HandleIsAlreadyResolvedTestAsync()
        {
            var resolvedTime = DateTime.UtcNow.AddDays(Random.Shared.Next(-30, -1));
            _thresholdNotification.Status = ThresholdNotificationStatus.Resolved;
            _thresholdNotification.Resolution = ThresholdNotificationResolution.IgnoreOnce;
            _thresholdNotification.ResolvedBy = _userUPN;
            _thresholdNotification.ResolvedTime = resolvedTime;

            var response = await _notificationsController.ResolveNotificationAsync(_thresholdNotification.Id, _resolveNotificationModel);

            _notificationRepository.Verify(x => x.SaveNotificationAsync(_thresholdNotification), Times.Never);

            Assert.IsInstanceOfType(response, typeof(NoContentResult));
        }

        /// <summary>
        /// /notifications/{id}/resolve - Audit entry attributes the resolution to the web UI and records the resolver (FR-013)
        /// </summary>
        [TestMethod]
        public async Task ResolveNotification_RecordsWebAppSourceAndResolverAsync()
        {
            _resolveNotificationModel.Resolution = $"{ThresholdNotificationResolution.IgnoreOnce}";
            SyncJobChange savedChange = null!;
            _syncJobChangeRepository.Setup(x => x.Save(It.IsAny<SyncJobChange>()))
                .Callback<SyncJobChange>(change => savedChange = change)
                .Returns(Task.CompletedTask);

            await _notificationsController.ResolveNotificationAsync(_thresholdNotification.Id, _resolveNotificationModel);

            _syncJobChangeRepository.Verify(x => x.Save(It.IsAny<SyncJobChange>()), Times.Once);
            Assert.IsNotNull(savedChange);
            Assert.AreEqual(SyncJobChangeSource.WebApp, savedChange.ChangeSource);
            Assert.AreEqual(_userUPN, savedChange.ChangedByDisplayName);
            Assert.AreEqual(_thresholdNotification.SyncJobId, savedChange.SyncJobId);
        }

        /// <summary>
        /// /notifications/{id}/resolve - A tenant writer who does not own the group can resolve (FR-022)
        /// </summary>
        [TestMethod]
        public async Task ResolveNotification_TenantWriterWhoIsNotOwnerCanResolveAsync()
        {
            var claims = new List<Claim>
            {
                new Claim("upn", "notAnOwner@contoso.net"),
                new Claim(ClaimTypes.Role, Roles.JOB_TENANT_WRITER)
            };
            _notificationsController.ControllerContext = CreateControllerContext(claims, "mockBearerToken");

            var response = await _notificationsController.ResolveNotificationAsync(_thresholdNotification.Id, _resolveNotificationModel);

            _notificationRepository.Verify(x => x.SaveNotificationAsync(_thresholdNotification), Times.Once);

            Assert.IsInstanceOfType(response, typeof(NoContentResult));
        }

        /// <summary>
        /// /notifications/{id}/resolve - A non-owner without the tenant writer role is denied (FR-022)
        /// </summary>
        [TestMethod]
        public async Task ResolveNotification_NonOwnerWithoutTenantWriterIsForbiddenAsync()
        {
            var claims = new List<Claim>
            {
                new Claim("upn", "notAnOwner@contoso.net"),
                new Claim(ClaimTypes.Role, Roles.JOB_TENANT_READER)
            };
            _notificationsController.ControllerContext = CreateControllerContext(claims, "mockBearerToken");

            var response = await _notificationsController.ResolveNotificationAsync(_thresholdNotification.Id, _resolveNotificationModel);

            _notificationRepository.Verify(x => x.SaveNotificationAsync(_thresholdNotification), Times.Never);

            Assert.IsInstanceOfType(response, typeof(ForbidResult));
        }

        /// <summary>
        /// /notifications/{id}/resolve - The resolution is attributed to the acting user, never a support group (FR-022)
        /// </summary>
        [TestMethod]
        public async Task ResolveNotification_AttributesResolutionToActingUserAsync()
        {
            var claims = new List<Claim>
            {
                new Claim("upn", "notAnOwner@contoso.net"),
                new Claim(ClaimTypes.Role, Roles.JOB_TENANT_WRITER)
            };
            _notificationsController.ControllerContext = CreateControllerContext(claims, "mockBearerToken");

            await _notificationsController.ResolveNotificationAsync(_thresholdNotification.Id, _resolveNotificationModel);

            Assert.AreEqual("notAnOwner@contoso.net", _thresholdNotification.ResolvedBy);
            _graphGroupRepository.Verify(x => x.GetGroupNameAsync(It.IsAny<Guid>()), Times.Never);
        }

        private ControllerContext CreateControllerContext(List<Claim> claims, string mockBearerToken)
        {
            var identity = new ClaimsIdentity(claims, "TestAuthType");
            var principal = new ClaimsPrincipal(identity);
            var httpContext = new DefaultHttpContext();

            httpContext.Request.Headers["Authorization"] = "Bearer " + mockBearerToken;
            httpContext.User = principal;

            // The handler reads roles through IHttpContextAccessor, so keep it on the same context.
            _httpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);

            return new ControllerContext { HttpContext = httpContext };
        }
    }
}
