// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Services.Entities;
using System.Collections.Generic;

namespace Hosts.GraphUpdater
{
    public class GroupUpdaterSubOrchestratorResponse
    {
        public GraphUpdaterStatus Status { get; set; }
        public RequestType Type { get; set; }
        public int SuccessCount { get; set; }
        public List<AzureADUser> UsersNotFound { get; set; }
        public List<AzureADUser> UsersAlreadyExist { get; set; }
    }
}