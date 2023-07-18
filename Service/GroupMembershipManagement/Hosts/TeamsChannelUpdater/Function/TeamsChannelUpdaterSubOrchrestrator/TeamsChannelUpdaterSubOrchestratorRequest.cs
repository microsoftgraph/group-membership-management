// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Models.Entities;
using System;
using System.Collections.Generic;

namespace Hosts.TeamsChannelUpdater
{
    public class TeamsChannelUpdaterSubOrchestratorRequest
    {
        public bool IsInitialSync { get; set; }
        public RequestType Type { get; set; }
        public ICollection<AzureADTeamsUser> Members { get; set; }
        public AzureADTeamsChannel TeamsChannelInfo { get; set; }
        public Guid RunId { get; set; }
    }
}