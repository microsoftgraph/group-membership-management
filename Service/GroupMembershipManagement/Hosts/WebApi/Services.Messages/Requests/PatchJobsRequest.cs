// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Requests;

namespace Services.Messages.Requests
{
    public class PatchJobsRequest : RequestBase
    {
        public string[] SyncJobIds { get; }

        public PatchJobsRequest(string[] syncJobIds)
        {
            SyncJobIds = syncJobIds;
        }
    }
}