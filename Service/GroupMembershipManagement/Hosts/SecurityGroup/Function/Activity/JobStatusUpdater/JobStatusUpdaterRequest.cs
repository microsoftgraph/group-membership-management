// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Models.Entities;
using System;

namespace Hosts.SecurityGroup
{
    public class JobStatusUpdaterRequest
    {
        public SyncJob SyncJob { get; set; }
        public SyncStatus Status { get; set; }
    }
}