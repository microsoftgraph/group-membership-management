// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;

namespace Hosts.GroupMembershipObtainer
{
    public class GroupMembershipRequest
    {
        public SyncJob SyncJob { get; set; }
        public AzureADGroup SourceGroup { get; set; }
        public Guid GroupId { get; set; }
        public int CurrentPart { get; set; }
        public QueryType QueryType { get; set; }
        public bool Exclusionary { get; set; }
        public int TotalParts { get; set; }
    }
}