// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Models.Notifications;

namespace Hosts.GroupMembershipObtainer
{
    public class EmailSenderRequest
    {
        public SyncJob SyncJob { get; set; }
        public NotificationMessageType NotificationType { get; set; }
        public string[] AdditionalContentParams { get; set; }
        public int CurrentPart { get; set; }
        public int TotalParts { get; set; }
    }
}