// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models.Entities;
using System;
using TeamsChannelMembershipObtainer.Service.Contracts;

namespace Hosts.TeamsChannelMembershipObtainer
{
    public class UserReaderRequest
    {
        public Guid RunId { get; set; }
        public AzureADTeamsChannel Channel { get; set; }
        public ChannelSyncInfo ChannelSyncInfo { get; set; }

    }
}
