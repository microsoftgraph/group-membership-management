// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;

namespace Hosts.GroupMembershipObtainer
{
    public class CacheUploaderRequest
    {
        public Guid RunId { get; set; }
        public Guid ObjectId { get; set; }
        public string FilePath { get; set; }
    }
}