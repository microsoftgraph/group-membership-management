// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace Hosts.GroupMembershipObtainer
{
    public class SubsequentDeltaUserReaderRequest
    {
        public Guid RunId { get; set; }
        public string NextPageUrl { get; set; }
        public int PageCount { get; set; }
        public Guid ObjectId { get; set; }
        public Guid TargetGroupId { get; set; }
        public int CurrentPart { get; set; }
    }
}