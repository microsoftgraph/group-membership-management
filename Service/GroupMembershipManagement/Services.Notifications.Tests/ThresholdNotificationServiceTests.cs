// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Contracts.Notifications;
using Models.ThresholdNotifications;

namespace Services.Notifications.Tests
{
    [TestClass]
    public class ThresholdNotificationServiceTests
    {
        private IThresholdNotificationService _thresholdNotificationService;

        [TestInitialize]
        public void InitializeTest()
        {
            _thresholdNotificationService = new ThresholdNotificationService();
        }

        [TestMethod]
        public async Task CreateUnresolvedNotificationHTML()
        {
            var testNotification = new ThresholdNotification();

            var unresolvedHTML = await _thresholdNotificationService.CreateNotificationHTMLAsync(testNotification);
            Assert.AreEqual("Threshold Notification template", unresolvedHTML);
        }

        [TestMethod]
        public async Task CreateResolvedNotificationHTML()
        {
            var testNotification = new ThresholdNotification
            {
                Status = ThresholdNotificationStatus.Resolved
            };

            var resolvedHTML = await _thresholdNotificationService.CreateNotificationHTMLAsync(testNotification);
            Assert.AreEqual("Threshold Notification Resolved template", resolvedHTML);
        }

        [TestMethod]
        public async Task GetExistingNotification()
        {
            var testGuid = Guid.Parse("12340000-0000-0000-0000-00000000abcd");

            var notification = await _thresholdNotificationService.GetNotificationAsync(testGuid);
            Assert.IsNotNull(notification);
            Assert.AreEqual(testGuid, notification.Id);
        }

        [TestMethod]
        public async Task GetNonexistantNotification()
        {
            var testGuid = Guid.Empty;

            var notification = await _thresholdNotificationService.GetNotificationAsync(testGuid);
            Assert.IsNull(notification);
        }

        [TestMethod]
        public async Task GetRecipientEmails()
        {
            var testNotification = new ThresholdNotification();

            var emails = await _thresholdNotificationService.GetRecipientEmailAddressesAsync(testNotification);
            Assert.AreEqual(3, emails.Count);
            Assert.AreEqual("user1@contoso.net", emails[0]);
            Assert.AreEqual("user2@contoso.net", emails[1]);
            Assert.AreEqual("user3@contoso.net", emails[2]);
        }

        [TestMethod]
        public async Task SaveNotification()
        {
            var testNotification = new ThresholdNotification();

            await Assert.ThrowsExceptionAsync<NotImplementedException>(async () =>
            {
                await _thresholdNotificationService.SaveNotificationAsync(testNotification);
            });

        }
    }
}