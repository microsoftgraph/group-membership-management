// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;
using System.Collections.Generic;

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
        public List<string> IdentityTypes { get; set; }

        /// <summary>
        /// Whether this part is the job's destination part, copied from the orchestrator request. Readers set
        /// <c>SourceGroup</c> on every member unless this is true. Null in inputs recorded before the field existed.
        /// </summary>
        public bool? IsDestinationPart { get; set; }
    }
}