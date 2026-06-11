// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Requests;

namespace Services.Messages.Requests
{
    public class GetMembershipDownloadRequest : RequestBase
    {
        public GetMembershipDownloadRequest(Guid syncJobId, Guid runId)
        {
            SyncJobId = syncJobId;
            RunId = runId;
        }

        public Guid SyncJobId { get; }
        public Guid RunId { get; }
    }
}
