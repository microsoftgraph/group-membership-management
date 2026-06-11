// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Models.Notifications;
using System;

namespace Hosts.AzureMaintenance
{
    public class WarningEmailSenderRequest
    {
        public Guid RunId { get; set; }
        public SyncJob SyncJob { get; set; }
        public NotificationMessageType NotificationType { get; set; }
    }
}