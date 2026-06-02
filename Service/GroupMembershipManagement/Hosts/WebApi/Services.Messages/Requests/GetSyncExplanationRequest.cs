// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Requests;

namespace Services.Messages.Requests
{
    public class GetSyncExplanationRequest : RequestBase
    {
        public GetSyncExplanationRequest(Guid syncJobId, Guid runId, Guid userObjectId, string userIdentity, bool hasAiSyncJobRole)
        {
            SyncJobId = syncJobId;
            RunId = runId;
            UserObjectId = userObjectId;
            UserIdentity = userIdentity;
            HasAiSyncJobRole = hasAiSyncJobRole;
        }

        public Guid SyncJobId { get; }
        public Guid RunId { get; }
        public Guid UserObjectId { get; }
        public string UserIdentity { get; }
        public bool HasAiSyncJobRole { get; }
    }
}
