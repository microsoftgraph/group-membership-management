// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models.ThresholdNotifications;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Services.Notifier.Contracts
{
    public interface INotifierService
    {
        public Task SendThresholdEmailAsync(ThresholdNotification notification);
        public Task<List<ThresholdNotification>> RetrieveQueuedNotificationsAsync();
        public Task UpdateNotificationStatusAsync(ThresholdNotification notification, ThresholdNotificationStatus status);
        public Task<Models.ThresholdNotifications.ThresholdNotification> CreateActionableNotificationFromContentAsync(string messageBody);
        public Task SendEmailAsync(string messageType, string messageBody, string subjectTemplate, string contentTemplate);
        public Task SendNormalThresholdEmailAsync(string messageBody);

    }
}
