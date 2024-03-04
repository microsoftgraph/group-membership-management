// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Models.Notifications;

namespace MembershipAggregator.Activity.EmailSender
{
    public class EmailSenderRequest
    {
        public SyncJob SyncJob { get; set; }
        public NotificationMessageType NotificationType { get; set; }
        public string[] AdditionalContentParams { get; set; }
    }
}
