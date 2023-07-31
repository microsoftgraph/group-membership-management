// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System.Collections.Generic;

namespace Hosts.GroupMembershipObtainer
{
    public class DeltaUserReaderResponse
    {
        public List<AzureADUser> UsersToAdd { get; set; }
        public List<AzureADUser> UsersToRemove { get; set; }
        public string DeltaUrl { get; set; }
    }
}
