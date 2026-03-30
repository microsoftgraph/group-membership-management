// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;

namespace Hosts.GroupMembershipObtainer
{
    public class TransitiveAndDeltaUsersSenderRequest
    {
        public SyncJob SyncJob { get; set; }
        public Guid ObjectId { get; set; }
        public Guid GroupId { get; set; }
        public int CurrentPart { get; set; }
        public bool Exclusionary { get; set; }
        public int TotalParts { get; set; }
    }
}