// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Requests;

namespace Services.Messages.Requests
{
    public class RemoveGMMRequest : RequestBase
    {
        public RemoveGMMRequest(string userIdentity, bool isJobTenantWriter, Guid syncJobId)
        {
            UserIdentity = userIdentity;
            IsJobTenantWriter = isJobTenantWriter;
            SyncJobId = syncJobId;
        }
        public string UserIdentity { get; }
        public bool IsJobTenantWriter { get; set; }
        public Guid SyncJobId { get; set; }

    }
}