// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace Hosts.GroupMembershipObtainer
{
    public class DeltaUserReaderRequest
    {
        public Guid RunId { get; set; }
        public Guid ObjectId { get; set; }
        public Guid TargetGroupId { get; set; }
        public int CurrentPart { get; set; }
        public int PageCount { get; set; }
    }
}