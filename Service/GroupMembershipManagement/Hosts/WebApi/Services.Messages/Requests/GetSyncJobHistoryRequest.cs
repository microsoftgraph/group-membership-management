// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Requests;

namespace Services.Messages.Requests
{
    public class GetSyncJobHistoryRequest : RequestBase
    {
        public GetSyncJobHistoryRequest(Guid syncJobId)
        {
            SyncJobId = syncJobId;
        }

        public Guid SyncJobId { get; }
        public int PageSize { get; set; } = 50;
        public int PageNumber { get; set; } = 1;
    }
}
