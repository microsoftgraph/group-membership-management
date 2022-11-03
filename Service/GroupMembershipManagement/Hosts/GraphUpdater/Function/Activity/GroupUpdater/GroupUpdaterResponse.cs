// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using System.Collections.Generic;

namespace Hosts.GraphUpdater
{
    public class GroupUpdaterResponse
    {
        public int SuccessCount { get; set; }
        public List<AzureADUser> UsersNotFound { get; set; }
    }
}