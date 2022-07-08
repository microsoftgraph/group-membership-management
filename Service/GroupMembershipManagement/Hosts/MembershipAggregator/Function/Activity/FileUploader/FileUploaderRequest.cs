// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;

namespace Hosts.MembershipAggregator
{
    public class FileUploaderRequest
    {
        public string FilePath { get; set; }
        public string Content { get; set; }
        public SyncJob SyncJob { get; set; }
    }
}
