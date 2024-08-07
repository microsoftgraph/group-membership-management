// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Requests;

namespace Services.Messages.Requests
{
    public class GetGroupOnboardingStatusRequest : RequestBase
    {
        public Guid GroupId { get; set; }
        public string UserIdentity { get; }
        public bool IsJobTenantWriter {  get; set; }

        public GetGroupOnboardingStatusRequest(Guid groupId, string userIdentity, bool isJobTenantWriter)
        {
            GroupId = groupId;
            UserIdentity = userIdentity;
            IsJobTenantWriter = isJobTenantWriter;
        }
    }
}