// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models.Entities;
using System;
using System.Collections.Generic;
using TeamsChannelMembershipObtainer.Service.Contracts;

namespace Hosts.TeamsChannelMembershipObtainer
{
    public class FileUploaderRequest
    {
        public ChannelSyncInfo ChannelSyncInfo { get; set; }
        public List<AzureADTeamsUser> Users { get; set; }
        public Boolean IsDryRunEnabled { get; set; }

    }
}
