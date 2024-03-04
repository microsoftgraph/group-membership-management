// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Models.Notifications;
using System;

namespace Hosts.GroupMembershipObtainer
{
    public class EmailSenderRequest
    {
        public Guid RunId { get; set; }
        public SyncJob SyncJob { get; set; }
        public NotificationMessageType NotificationType { get; set; }
        public string[] AdditionalContentParams { get; set; }
    }
}