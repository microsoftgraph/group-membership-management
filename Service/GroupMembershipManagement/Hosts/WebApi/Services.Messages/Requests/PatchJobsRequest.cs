// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Requests;

namespace Services.Messages.Requests
{
    public class PatchJobsRequest : RequestBase
    {
        public string[] SyncJobIds { get; }
        public string UserIdentity { get; }
        public string UserDisplayName { get; set; }

        public PatchJobsRequest(string[] syncJobIds, string userIdentity, string userDisplayName)
        {
            SyncJobIds = syncJobIds;
            UserIdentity = userIdentity;
            UserDisplayName = userDisplayName;
        }
    }
}