// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System.Collections.Generic;

namespace Hosts.GroupMembershipObtainer
{
    public class MembersReaderResponse
    {
        public List<AzureADUser> Users { get; set; }
        public Dictionary<string, int> NonUserGraphObjects { get; set; }
    }
}
