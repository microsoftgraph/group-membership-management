// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;

namespace Hosts.GroupMembershipObtainer
{
    public class GetTransitiveGroupCountRequest
    {
        public Guid GroupId { get; set; }
        public int CurrentPart { get; set; }
        public int TotalParts { get; set; }
        public SyncJob SyncJob { get; set; }
    }
}