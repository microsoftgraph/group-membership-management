// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;

namespace Hosts.GroupOwnershipObtainer
{
    public class GetGroupRequest
    {
        public SyncJob SyncJob { get; set; }
        public int CurrentPart { get; set; }
        public int TotalParts { get; set; }
    }
}
