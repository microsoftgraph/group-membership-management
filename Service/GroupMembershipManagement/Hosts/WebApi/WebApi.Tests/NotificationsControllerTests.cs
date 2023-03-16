// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.AspNetCore.Mvc;
using Models;
using Models.ThresholdNotifications;
using Moq;
using Repositories.Contracts;
using WebApi.Controllers.v1.Notifications;
using WebApi.Models.Requests;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using Services.WebApi;
using Services.Notifications;
using Services.Contracts.Notifications;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Repositories.Localization;
using DIConcreteTypes;

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
        private Mock<ILoggingRepository> _loggingRepository = null!;
        private Mock<IGraphGroupRepository> _graphGroupRepository = null!;
        private Mock<INotificationRepository> _notificationRepository = null!;
        private ILocalizationRepository _localizationRepository = null!;
        private IThresholdNotificationService _thresholdNotificationService = null!;
        private ThresholdNotificationServiceConfig _thresholdNotificationServiceConfig = null!;
        private NotificationCardHandler _notificationCardHandler = null!;
        private ResolveNotificationHandler _resolveNotificationsHandler = null!;
        private NotificationsController _notificationsController = null!;
        private List<ThresholdNotification> _thresholdNotifications = null!;
        private ResolveNotification _resolveNotificationModel = null!;

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

            _loggingRepository = new Mock<ILoggingRepository>();
            _graphGroupRepository = new Mock<IGraphGroupRepository>();
            _notificationRepository = new Mock<INotificationRepository>();

            _graphGroupRepository.Setup(x => x.GetGroupsAsync(It.IsAny<List<Guid>>()))
                                    .ReturnsAsync(() => _groups);

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
                    It.Is<string>(s => s == _userUPN), It.Is<Guid>(groupId => groupId == group.ObjectId)))
                    .ReturnsAsync(true);

                _graphGroupRepository.Setup(x => x.GetGroupNameAsync(It.Is<Guid>(g => g == group.ObjectId)))
                    .ReturnsAsync(() => groupName);

                var notification = new ThresholdNotification
                {
                    ChangePercentageForAdditions = Random.Shared.Next(51, 100),
                    ChangePercentageForRemovals = Random.Shared.Next(51, 100),
                    ChangeQuantityForAdditions = Random.Shared.Next(50, 1000),
                    ChangeQuantityForRemovals = Random.Shared.Next(50, 1000),
                    CreatedTime = DateTime.UtcNow,
                    Resolution = ThresholdNotificationResolution.Unresolved,
                    Id = Guid.NewGuid(),
                    ResolvedByUPN = string.Empty,
                    ResolvedTime = DateTime.UtcNow,
                    Status = ThresholdNotificationStatus.AwaitingResponse,
                    TargetOfficeGroupId = group.ObjectId,
                    ThresholdPercentageForAdditions = Random.Shared.Next(1, 50),
                    ThresholdPercentageForRemovals = Random.Shared.Next(1, 50)
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
                ResolvedByUPN = _userUPN,
                ResolvedTime = DateTime.UtcNow,
                Status = ThresholdNotificationStatus.AwaitingResponse
            };

            _notificationRepository.Setup(x => x.GetThresholdNotificationByIdAsync(It.IsAny<Guid>()))
                .Returns<Guid>((id) => Task.FromResult(_thresholdNotifications.FirstOrDefault(notification => notification.Id == id)));

            // Items for testing
            _thresholdNotification = _thresholdNotifications[Random.Shared.Next(0, _notificationCount)];
            _groupId = _thresholdNotification.TargetOfficeGroupId;
            _groupName = _groupNames[_groupId];

            _thresholdNotificationServiceConfig = new ThresholdNotificationServiceConfig
            {
                Hostname = _hostname,
                ActionableEmailProviderId = _providerId
            };

            _thresholdNotificationService = new ThresholdNotificationService(Options.Create(_thresholdNotificationServiceConfig), _graphGroupRepository.Object, _localizationRepository);

            _resolveNotificationsHandler = new ResolveNotificationHandler(_loggingRepository.Object,
                _notificationRepository.Object,
                _graphGroupRepository.Object,
                _thresholdNotificationService);
            _notificationCardHandler = new NotificationCardHandler(_loggingRepository.Object,
                _notificationRepository.Object,
                _graphGroupRepository.Object,
                _thresholdNotificationService);

            _notificationsController = new NotificationsController(_resolveNotificationsHandler, _notificationCardHandler);
            var identity = new ClaimsIdentity(new List<Claim> { new Claim(ClaimTypes.Upn, _userUPN) }, "test");
            var principal = new ClaimsPrincipal(identity);
            _notificationsController.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } };
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
            var identity = new ClaimsIdentity(new List<Claim> { new Claim(ClaimTypes.Upn, "not-an-owner@contoso.net") }, "test");
            var principal = new ClaimsPrincipal(identity);
            _notificationsController.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } };

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
            _thresholdNotification.ResolvedByUPN = _userUPN;
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
        /// /notifications/{id}/card - Get card for an unresolved notification
        /// </summary>
        [TestMethod]
        public async Task GetNotificationCard_HandleUnresolvedTestAsync()
        {
            var response = await _notificationsController.GetCardAsync(_thresholdNotification.Id);
            var result = response.Result as ContentResult;

            Assert.IsNotNull(response);
            Assert.IsNotNull(result?.Content);
            Assert.AreEqual("application/json", result.ContentType);
            ValidateUnresolvedCard(result.Content);
        }

        /// <summary>
        /// /notifications/{id}/card - Get card for a notification that no longer exists
        /// </summary>
        [TestMethod]
        public async Task GetNotificationCard_HandleNotFoundTestAsync()
        {
            var response = await _notificationsController.GetCardAsync(_nonExistantNotificationId);
            var result = response.Result as ContentResult;

            Assert.IsNotNull(response);
            Assert.IsNotNull(result?.Content);
            Assert.AreEqual("application/json", result.ContentType);
            ValidateNotFoundCard(result.Content);
        }

        /// <summary>
        /// /notifications/{id}/card - Get card for a notification that no longer exists
        /// </summary>
        [TestMethod]
        public async Task GetNotificationCard_HandleUserNotGroupOwnerTestAsync()
        {
            var identity = new ClaimsIdentity(new List<Claim> { new Claim(ClaimTypes.Upn, "not-an-owner@contoso.net") }, "test");
            var principal = new ClaimsPrincipal(identity);
            _notificationsController.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } };

            var response = await _notificationsController.GetCardAsync(_thresholdNotification.Id);
            var result = response.Result as ContentResult;

            Assert.IsNotNull(response);
            Assert.IsNotNull(result?.Content);
            Assert.AreEqual("application/json", result.ContentType);
            ValidateUnauthorizedCard(result.Content);
        }

        /// <summary>
        /// /notifications/{id}/card - Get card for a resolved notification
        /// </summary>
        [TestMethod]
        public async Task GetNotificationCard_HandleResolvedTestAsync()
        {
            var resolvedTime = DateTime.UtcNow.AddDays(Random.Shared.Next(-30, -1));
            _thresholdNotification.Status = ThresholdNotificationStatus.Resolved;
            _thresholdNotification.Resolution = ThresholdNotificationResolution.IgnoreOnce;
            _thresholdNotification.ResolvedByUPN = _userUPN;
            _thresholdNotification.ResolvedTime = resolvedTime;

            var response = await _notificationsController.GetCardAsync(_thresholdNotification.Id);
            var result = response.Result as ContentResult;

            Assert.IsNotNull(response);
            Assert.IsNotNull(result?.Content);
            Assert.AreEqual("application/json", result.ContentType);
            ValidateResolvedCard(result.Content);
        }

        private void ValidateUnresolvedCard(string cardJson)
        {
            Assert.IsTrue(cardJson.Contains($"A recent synchronization attempt of **{_groupName}**"));
            Assert.IsTrue(cardJson.Contains($"GMM has identified **{_thresholdNotification.ChangeQuantityForAdditions}** members to be added, increasing the group size by **{_thresholdNotification.ChangePercentageForAdditions}%**, which is more than the current additions threshold of **{_thresholdNotification.ThresholdPercentageForAdditions}%**."));
            Assert.IsTrue(cardJson.Contains($"GMM has identified **{_thresholdNotification.ChangeQuantityForRemovals}** members to be removed, decreasing the group size by **{_thresholdNotification.ChangePercentageForRemovals}%**, which is more than the current removals threshold of **{_thresholdNotification.ThresholdPercentageForRemovals}%**."));
            Assert.IsTrue(cardJson.Contains($"https://{_hostname}/api/v1/{_thresholdNotification.Id}/resolve"));
            Assert.IsTrue(cardJson.Contains($"\\\"resolution\\\":\\\"{ThresholdNotificationResolution.Paused}\\\""));
            Assert.IsTrue(cardJson.Contains($"\\\"resolution\\\":\\\"{ThresholdNotificationResolution.IgnoreOnce}\\\""));
            Assert.IsTrue(cardJson.Contains($"{_thresholdNotification.Id}"));
            Assert.IsTrue(cardJson.Contains($"\"originator\":\"{_providerId}\""));
        }

        private void ValidateResolvedCard(string cardJson)
        {
            var resolutionString = _localizationRepository.TranslateSetting(_thresholdNotification.Resolution);
            Assert.IsTrue(cardJson.Contains($"A recent synchronization attempt of **{_groupName}**"));
            Assert.IsTrue(cardJson.Contains($"This notification was resolved by **{_userUPN}** on **{_thresholdNotification.ResolvedTime:U}**."));
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
    }
}
