// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Models.Notifications;
using System;

namespace Hosts.AzureMaintenance
{
    public class PurgingEmailSenderRequest
    {
        public Guid RunId { get; set; }
        public PurgedSyncJob SyncJob { get; set; }
        public NotificationMessageType NotificationType { get; set; }
    }
}