// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;

namespace Hosts.GroupMembershipObtainer
{
    public class SubsequentMembersReaderRequest
    {
        public Guid GroupId { get; set; }
        public Guid TargetGroupId { get; set; }
        public int CurrentPart { get; set; }
        public string NextPageUrl { get; set; }
        public int TotalParts { get; set; }
        public SyncJob SyncJob { get; set; }
    }
}