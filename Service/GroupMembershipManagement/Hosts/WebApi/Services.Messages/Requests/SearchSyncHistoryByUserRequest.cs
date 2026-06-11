// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Requests;

namespace Services.Messages.Requests
{
    public class SearchSyncHistoryByUserRequest : RequestBase
    {
        public SearchSyncHistoryByUserRequest(Guid syncJobId, Guid userObjectId, string? requestId = null)
        {
            SyncJobId = syncJobId;
            UserObjectId = userObjectId;
            RequestId = requestId;
        }

        public Guid SyncJobId { get; }
        public Guid UserObjectId { get; }
        public string? RequestId { get; }
    }
}