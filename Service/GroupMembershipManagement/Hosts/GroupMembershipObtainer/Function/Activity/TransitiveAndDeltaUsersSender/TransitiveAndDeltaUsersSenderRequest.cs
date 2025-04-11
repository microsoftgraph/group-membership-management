// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;

namespace Hosts.GroupMembershipObtainer
{
    public class TransitiveAndDeltaUsersSenderRequest
    {
        public SyncJob SyncJob { get; set; }
        public Guid GroupId { get; set; }
        public Guid RunId { get; set; }
        public int CurrentPart { get; set; }
        public bool Exclusionary { get; set; }
    }
}