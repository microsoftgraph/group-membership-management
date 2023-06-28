// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Models.Entities;
using System.Collections.Generic;

namespace Hosts.TeamsChannelUpdater
{
    public class TeamsUpdaterResponse
    {
        public int SuccessCount { get; set; }
        public List<AzureADTeamsUser> UsersToRetry { get; set; }
        public List<AzureADTeamsUser> UsersNotFound { get; set; }
        public List<AzureADTeamsUser> UsersAlreadyExist { get; set; }
    }
}