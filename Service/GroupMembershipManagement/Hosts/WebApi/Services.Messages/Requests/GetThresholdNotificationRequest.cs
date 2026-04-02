// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Requests;

namespace Services.Messages.Requests
{
    public class GetThresholdNotificationRequest : RequestBase
    {
        public GetThresholdNotificationRequest(Guid syncJobId)
        {
            SyncJobId = syncJobId;
        }

        public Guid SyncJobId { get; }
    }
}
