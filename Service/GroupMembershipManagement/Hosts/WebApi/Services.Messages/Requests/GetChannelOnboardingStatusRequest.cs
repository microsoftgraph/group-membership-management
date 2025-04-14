// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Requests;

namespace Services.Messages.Requests
{
    public class GetChannelOnboardingStatusRequest : RequestBase
    {
        public Guid TeamId { get; set; }
        public string ChannelId { get; set; }
        public string UserIdentity { get; set; }
        public bool IsJobTenantWriter { get; set; }

        public GetChannelOnboardingStatusRequest(Guid teamId, string channelId, string userIdentity, bool isJobTenantWriter)
        {
            TeamId = teamId;
            ChannelId = channelId;
            UserIdentity = userIdentity;
            IsJobTenantWriter = isJobTenantWriter;
        }
    }
}