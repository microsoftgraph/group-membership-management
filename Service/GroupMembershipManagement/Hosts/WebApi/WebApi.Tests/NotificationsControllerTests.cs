// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using DIConcreteTypes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Models;
using Models.SyncJobChange;
using Models.ThresholdNotifications;
using Moq;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.Localization;
using Services.Contracts.Notifications;
using Services.Notifications;
using Services.WebApi;
using System.Security.Claims;
using WebApi.Controllers.v1.Notifications;
using WebApi.Models.Requests;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.O365.ActionableMessages.Utilities;
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
        private string _hostname = null!;
        private Guid _providerId = Guid.Empty;
        private ThresholdNotification _thresholdNotification = null!;
        private Guid _groupId = Guid.Empty;
        private string _groupName = null!;
        private List<AzureADGroup> _groups = null!;
        private Dictionary<Guid, string> _groupNames = null!;
        private List<string> _groupTypes = null!;
        private Mock<IGraphGroupRepository> _graphGroupRepository = null!;
        private Mock<INotificationRepository> _notificationRepository = null!;
        private Mock<IDatabaseSyncJobsRepository> _syncJobRepository = null!;
        private Mock<ISyncJobChangeRepository> _syncJobChangeRepository = null!;
        private ILocalizationRepository _localizationRepository = null!;
        private IThresholdNotificationService _thresholdNotificationService = null!;
        private IGMMEmailReceivers _gmmEmailReceivers = null!;
        private IHandleInactiveJobsConfig _handleInactiveJobsConfig = null!;
        private ThresholdNotificationServiceConfig _thresholdNotificationServiceConfig = null!;
        private ResolveNotificationHandler _resolveNotificationsHandler = null!;
        private NotificationsController _notificationsController = null!;
        private List<ThresholdNotification> _thresholdNotifications = null!;
        private ResolveNotification _resolveNotificationModel = null!;
        private TelemetryClient _telemetryClient = null!;

        [TestInitialize]
        public void Initialize()
        {
            _hostname = "api.test.gmm.microsoft.com";
            _providerId = Guid.NewGuid();
            _userUPN = "testuser@contoso.net";
            _nonExistantNotificationId = Guid.Empty;

            _groups = new List<AzureADGroup>();
            _resolveNotificationModel = new ResolveNotification()
            {
                Resolution = "Paused"
            };

            var options = Options.Create(new LocalizationOptions { ResourcesPath = "Resources" });
            var factory = new ResourceManagerStringLocalizerFactory(options, NullLoggerFactory.Instance);
            var localizer = new StringLocalizer<LocalizationRepository>(factory);
            _localizationRepository = new LocalizationRepository(localizer);

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
            _groupName = _groupNames[_groupId];

            _thresholdNotificationServiceConfig = new ThresholdNotificationServiceConfig
            {
                ApiHostname = _hostname,
                ActionableEmailProviderId = _providerId
            };

            _handleInactiveJobsConfig = new HandleInactiveJobsConfig
            {
                HandleInactiveJobsEnabled = true,
                NumberOfDaysBeforeDeletion = 30
            };

            _thresholdNotificationService = new ThresholdNotificationService(Options.Create(_thresholdNotificationServiceConfig), _graphGroupRepository.Object, _localizationRepository, _handleInactiveJobsConfig);
            _gmmEmailReceivers = new GMMEmailReceivers(Guid.NewGuid());

            _resolveNotificationsHandler = new ResolveNotificationHandler(NullLogger<ResolveNotificationHandler>.Instance,
                _notificationRepository.Object,
                _syncJobRepository.Object,
                _syncJobChangeRepository.Object,
                _graphGroupRepository.Object,
                _telemetryClient,
                _thresholdNotificationService,
                _gmmEmailReceivers);

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
            var result = response.Result as ContentResult;

            _notificationRepository.Verify(x => x.SaveNotificationAsync(_thresholdNotification), Times.Once);

            Assert.IsNotNull(response);
            Assert.IsNotNull(result?.Content);
            Assert.AreEqual("application/json", result.ContentType);
            ValidateResolvedCard(result.Content);
        }

        /// <summary>
        /// /notifications/{id}/resolve - Resolve notification with Pause sync job
        /// </summary>
        [TestMethod]
        public async Task ResolveNotification_WithPauseTestAsync()
        {
            _resolveNotificationModel.Resolution = $"{ThresholdNotificationResolution.Paused}";
            var response = await _notificationsController.ResolveNotificationAsync(_thresholdNotification.Id, _resolveNotificationModel);
            var result = response.Result as ContentResult;

            _notificationRepository.Verify(x => x.SaveNotificationAsync(_thresholdNotification), Times.Once);

            Assert.IsNotNull(response);
            Assert.IsNotNull(result?.Content);
            Assert.AreEqual("application/json", result.ContentType);
            ValidateResolvedCard(result.Content);
        }

        /// <summary>
        /// /notifications/{id}/resolve - Resolve notification that does not exist
        /// </summary>
        [TestMethod]
        public async Task ResolveNotification_HandleNotFoundTestAsync()
        {
            var response = await _notificationsController.ResolveNotificationAsync(_nonExistantNotificationId, _resolveNotificationModel);
            var result = response.Result as ContentResult;

            _notificationRepository.Verify(x => x.SaveNotificationAsync(_thresholdNotification), Times.Never);

            Assert.IsNotNull(response);
            Assert.IsNotNull(result?.Content);
            Assert.AreEqual("application/json", result.ContentType);
            ValidateNotFoundCard(result.Content);
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
            var result = response.Result as ContentResult;

            _notificationRepository.Verify(x => x.SaveNotificationAsync(_thresholdNotification), Times.Never);

            Assert.IsNotNull(response);
            Assert.IsNotNull(result?.Content);
            Assert.AreEqual("application/json", result.ContentType);
            ValidateUnauthorizedCard(result.Content);
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
            var result = response.Result as ContentResult;

            _notificationRepository.Verify(x => x.SaveNotificationAsync(_thresholdNotification), Times.Never);

            Assert.IsNotNull(response);
            Assert.IsNotNull(result?.Content);
            Assert.AreEqual("application/json", result.ContentType);
            ValidateResolvedCard(result.Content);
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

        private void ValidateResolvedCard(string cardJson)
        {
            var resolutionString = _localizationRepository.TranslateSetting(_thresholdNotification.Resolution);
            Assert.IsTrue(cardJson.Contains($"Notification Resolved"));
            Assert.IsTrue(cardJson.Contains($"This notification was resolved by **{_userUPN}** on **{_thresholdNotification.ResolvedTime:U}** UTC."));
            Assert.IsTrue(cardJson.Contains($"Action taken: **{resolutionString}**."));
            Assert.IsTrue(cardJson.Contains($"{_thresholdNotification.Id}"));
            Assert.IsTrue(cardJson.Contains($"\"originator\":\"{_providerId}\""));
        }

        private void ValidateUnauthorizedCard(string cardJson)
        {
            Assert.IsTrue(cardJson.Contains($"Error: You are no longer authorized to view notifications for **{_groupName}**"));
            Assert.IsTrue(cardJson.Contains($"{_thresholdNotification.Id}"));
            Assert.IsTrue(cardJson.Contains($"\"originator\":\"{_providerId}\""));
        }

        private void ValidateNotFoundCard(string cardJson)
        {
            Assert.IsTrue(cardJson.Contains("Notification Not Found"));
            Assert.IsTrue(cardJson.Contains($"{_nonExistantNotificationId}"));
            Assert.IsTrue(cardJson.Contains($"\"originator\":\"{_providerId}\""));
        }

        private ControllerContext CreateControllerContext(List<Claim> claims, string mockBearerToken)
        {
            var identity = new ClaimsIdentity(claims, "TestAuthType");
            var principal = new ClaimsPrincipal(identity);
            var httpContext = new DefaultHttpContext();

            httpContext.Request.Headers["Authorization"] = "Bearer " + mockBearerToken;
            httpContext.User = principal;

            return new ControllerContext { HttpContext = httpContext };
        }
    }
}
