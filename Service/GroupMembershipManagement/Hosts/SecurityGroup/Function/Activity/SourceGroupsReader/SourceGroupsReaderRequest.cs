// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using System;

namespace Hosts.SecurityGroup
{
    public class SourceGroupsReaderRequest
    {
        public SyncJob SyncJob { get; set; }
        public Guid RunId { get; set; }
    }
}