// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using System;

namespace Hosts.AzureMembershipProvider
{
    public class SubOrchestratorRequest
    {
        public SyncJob SyncJob { get; set; }
        public string Url { get; set; }
        public Guid RunId { get; set; }
    }
}
