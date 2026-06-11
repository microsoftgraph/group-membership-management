// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System;

namespace Hosts.GroupMembershipObtainer
{
    public class CacheConverterRequest
    {
        public Guid ObjectId { get; set; }
        public string FilePath { get; set; }
        public int CurrentPart { get; set; }
        public int TotalParts { get; set; }
        public SyncJob SyncJob { get; set; }
    }
}