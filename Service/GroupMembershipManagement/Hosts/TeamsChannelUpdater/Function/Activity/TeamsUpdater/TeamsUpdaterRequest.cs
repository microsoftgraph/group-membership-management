// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models.Entities;
using System;
using System.Collections.Generic;

namespace Hosts.TeamsChannelUpdater
{
    public class TeamsUpdaterRequest
    {
        public RequestType Type { get; set; }
        public List<AzureADTeamsUser> Members { get; set; }
        public AzureADTeamsChannel TeamsChannelInfo { get; set; }
        public Guid RunId { get; set; }

    }
}
