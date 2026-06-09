// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models.Notifications;
using Services.Entities;

namespace Hosts.GraphUpdater
{
    public class EmailSenderRequest : GraphUpdaterRequestBase
    {
        public NotificationMessageType NotificationType { get; set; }
        public string[] AdditionalContentParams { get; set; }
    }
}