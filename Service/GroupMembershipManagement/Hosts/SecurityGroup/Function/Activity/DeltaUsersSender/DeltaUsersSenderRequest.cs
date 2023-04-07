// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;

namespace Hosts.SecurityGroup
{
    public class DeltaUsersSenderRequest
    {
        public Guid RunId { get; set; }
        public SyncJob SyncJob { get; set; }
        public Guid ObjectId { get; set; }
        public string CompressedUsers { get; set; }
        public string DeltaLink { get; set; }
    }
}