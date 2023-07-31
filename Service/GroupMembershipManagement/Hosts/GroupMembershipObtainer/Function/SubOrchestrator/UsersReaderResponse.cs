// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System.Collections.Generic;

namespace Hosts.GroupMembershipObtainer
{
    public class UsersReaderResponse
    {
        public List<AzureADUser> Users { get; set; }
        public string DeltaUrl { get; set; }
    }
}
