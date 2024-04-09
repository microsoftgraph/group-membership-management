// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Requests;

namespace Services.Messages.Requests
{
    public class NotificationCardRequest : RequestBase
    {
        public Guid ThresholdNotificationId { get; set; } = Guid.Empty;
        public string UserIdentifier { get; set; } = string.Empty;

        public NotificationCardRequest()
        { }

        public NotificationCardRequest(Guid id, string userIdentifier)
        {
            ThresholdNotificationId = id;
            UserIdentifier = userIdentifier;
        }
    }
}