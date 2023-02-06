// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Models.Entities;
using System;

namespace Hosts.SecurityGroup
{
    public class SecurityGroupRequest
    {
        public SyncJob SyncJob { get; set; }
        public AzureADGroup SourceGroup { get; set; }
        public Guid RunId { get; set; }
    }
}