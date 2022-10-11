// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using System;

namespace Hosts.AzureMembershipProvider
{
    public class AzureMembershipProviderRequest
    {
        public SyncJob SyncJob { get; set; }
        public AzureADGroup SourceGroup { get; set; }
        public Guid RunId { get; set; }
    }
}