// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System;

namespace Hosts.MembershipAggregator
{
    public class GroupNameReaderRequest
    {
        public SyncJob SyncJob { get; set; }
        public Guid GroupId { get; set; }
    }
}