// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Requests;

namespace Services.Messages.Requests
{
    public class ResolveNotificationRequest : RequestBase
    {
        public ResolveNotificationRequest(Guid id, string? userEmail, string resolution)
        {
            ThresholdNotificationId = id;
            UserIdentifier = userEmail ?? string.Empty;
            Resolution = resolution;
        }

        public Guid ThresholdNotificationId { get; }
        public string UserIdentifier { get; }
        public string Resolution { get; }
    }
}