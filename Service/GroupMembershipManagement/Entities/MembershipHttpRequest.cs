// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Entities
{
    public class MembershipHttpRequest
    {
        public string FilePath { get; set; }
        public SyncJob SyncJob { get; set; }
        public int? ProjectedMemberCount { get; set; }
    }
}
