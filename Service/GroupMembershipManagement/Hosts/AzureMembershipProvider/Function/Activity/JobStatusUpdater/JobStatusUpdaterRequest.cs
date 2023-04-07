// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;

namespace Hosts.AzureMembershipProvider
{
    public class JobStatusUpdaterRequest
    {
        public SyncJob SyncJob { get; set; }
        public SyncStatus Status { get; set; }
    }
}