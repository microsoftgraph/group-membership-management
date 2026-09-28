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
        // US6: effective onboarding authorization computed by the controller; the handler re-checks it
        // (defense-in-depth). Defaults to true so existing callers/tests are unaffected.
        public bool IsOnboardingAuthorized { get; set; }

        public GetChannelOnboardingStatusRequest(Guid teamId, string channelId, string userIdentity, bool isJobTenantWriter, bool isOnboardingAuthorized = true)
        {
            TeamId = teamId;
            ChannelId = channelId;
            UserIdentity = userIdentity;
            IsJobTenantWriter = isJobTenantWriter;
            IsOnboardingAuthorized = isOnboardingAuthorized;
        }
    }
}