// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;
using System.Collections.Generic;

namespace Hosts.GroupMembershipObtainer
{
    public class SubsequentDeltaLinkUserReaderRequest
    {
        public Guid GroupId { get; set; }
        public Guid TargetGroupId { get; set; }
        public int CurrentPart { get; set; }
        public string NextPageUrl { get; set; }
        public int PageCount { get; set; }
        public int TotalParts { get; set; }
        public SyncJob SyncJob { get; set; }
        public List<string> IdentityTypes { get; set; }
        public GroupMembershipReadKind? ReadKind { get; set; }
    }
}