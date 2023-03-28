// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.AspNetCore.Mvc;
using Models;
using Models.ThresholdNotifications;
using Moq;
using Repositories.Contracts;
using WebApi.Models.Responses;
using WebApi.Controllers.v1.Notifications;
using Azure;
using WebApi.Models.Requests;
using Services.Messages.Responses;
using Microsoft.Graph;
using Services.Messages.Requests;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using Microsoft.Azure.Amqp.Transaction;

namespace Services.Tests
{
    [TestClass]
    public class NotificationsControllerTests
    {
        private int _notificationCount = 10;
        private Guid _testGuid = Guid.NewGuid();
        private List<AzureADGroup> _groups = null!;
        private List<string> _groupTypes = null!;
        private Mock<ILoggingRepository> _loggingRepository = null!;
        private Mock<IGraphGroupRepository> _graphGroupRepository = null!;
        private Mock<INotificationRepository> _notificationRepository = null!;  
        private ResolveNotificationHandler _resolveNotificationsHandler = null!;
        private NotificationsController _notificationsController = null!;
        private List<ThresholdNotification> _thresholdNotifications = null!;
        private ResolveNotification _model = null!;

        [TestInitialize]
        public void Initialize()
        {
            _groups = new List<AzureADGroup>();
            _model = new ResolveNotification() { 
                Resolution = "Paused"
            };

            _loggingRepository = new Mock<ILoggingRepository>();
            _graphGroupRepository = new Mock<IGraphGroupRepository>();
            _notificationRepository = new Mock<INotificationRepository>();

            _graphGroupRepository.Setup(x => x.GetGroupsAsync(It.IsAny<List<Guid>>()))
                                    .ReturnsAsync(() => _groups);

            _thresholdNotifications = Enumerable.Range(0, _notificationCount).Select(x => new ThresholdNotification
            {
                ChangePercentageForAdditions = 0,
                ChangePercentageForRemovals = 0,
                CreatedTime = DateTime.UtcNow,
                Resolution = ThresholdNotificationResolution.Unresolved,
                Id = Guid.NewGuid(),
                ResolvedByUPN = string.Empty,
                ResolvedTime = DateTime.UtcNow,
                Status = ThresholdNotificationStatus.AwaitingResponse,
                TargetOfficeGroupId = Guid.NewGuid(),
                ThresholdPercentageForAdditions = 0,
                ThresholdPercentageForRemovals = 0
            }).ToList();

            _groupTypes = new List<string>
            {
                "Microsoft 365",
                "Security",
                "Mail enabled security",
                "Distribution"
            };

            _thresholdNotifications.ForEach(x =>
            {
                _groups.Add(new AzureADGroup
                {
                    ObjectId = x.TargetOfficeGroupId,
                    Type = _groupTypes[Random.Shared.Next(0, _groupTypes.Count)]
                });
            });

            _notificationRepository.Setup(x => x.GetThresholdNotificationByIdAsync(It.IsAny<Guid>()))
                .Returns(() => {
                    var notification = new ThresholdNotification()
                    {
                        ChangePercentageForAdditions = 0,
                        ChangePercentageForRemovals = 0,
                        CreatedTime = DateTime.UtcNow,
                        Resolution = ThresholdNotificationResolution.Paused,
                        Id = Guid.NewGuid(),
                        ResolvedByUPN = "testuser@test.com",
                        ResolvedTime = DateTime.UtcNow,
                    };
                    return Task.FromResult(notification);
                });

            _resolveNotificationsHandler = new ResolveNotificationHandler(_loggingRepository.Object,
                _notificationRepository.Object,
                _graphGroupRepository.Object);
            _notificationsController = new NotificationsController(_resolveNotificationsHandler);
            var identity = new ClaimsIdentity(new List<Claim> { new Claim(ClaimTypes.Name, "testuser") }, "test");
            var principal = new ClaimsPrincipal(identity);
            _notificationsController.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } };
        }

        [TestMethod]
        public async Task ResolveNotificationTestAsync()
        {
            var response = await _notificationsController.ResolveNotificationAsync(_testGuid, _model);
            var result = response.Result as ContentResult;

            Assert.IsNotNull(response);
            Assert.IsNotNull(result);
            Assert.AreEqual("application/json", result.ContentType);
        }
    }
}
