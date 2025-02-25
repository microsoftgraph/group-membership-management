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
    }
}