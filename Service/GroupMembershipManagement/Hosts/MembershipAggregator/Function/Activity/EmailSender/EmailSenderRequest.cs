// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Models.Notifications;

namespace MembershipAggregator.Activity.EmailSender
{
    public class EmailSenderRequest
    {
        public required SyncJob SyncJob { get; init; }
        public required NotificationMessageType NotificationType { get; init; }
        public required string[] AdditionalContentParams { get; init; }
    }
}
