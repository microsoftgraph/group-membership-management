// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Requests;

namespace Services.Messages.Requests
{
    public class GetRunExplanationRequest : RequestBase
    {
        public GetRunExplanationRequest(Guid syncJobId, Guid runId, string userIdentity, bool hasAiSyncJobRole)
        {
            SyncJobId = syncJobId;
            RunId = runId;
            UserIdentity = userIdentity;
            HasAiSyncJobRole = hasAiSyncJobRole;
        }

        public Guid SyncJobId { get; }
        public Guid RunId { get; }
        public string UserIdentity { get; }
        public bool HasAiSyncJobRole { get; }
    }
}
