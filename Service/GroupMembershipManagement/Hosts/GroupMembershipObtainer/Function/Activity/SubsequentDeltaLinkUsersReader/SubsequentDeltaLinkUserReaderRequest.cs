// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace Hosts.GroupMembershipObtainer
{
    public class SubsequentDeltaLinkUserReaderRequest
    {
        public Guid RunId { get; set; }
        public Guid GroupId { get; set; }
        public Guid TargetGroupId { get; set; }
        public int CurrentPart { get; set; }
        public string NextPageUrl { get; set; }
        public int PageCount { get; set; }
    }
}