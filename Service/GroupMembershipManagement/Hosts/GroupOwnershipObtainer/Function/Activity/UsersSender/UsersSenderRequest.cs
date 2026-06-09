// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;
using System.Collections.Generic;

namespace Hosts.GroupOwnershipObtainer
{
    public class UsersSenderRequest
    {
        public SyncJob SyncJob { get; set; }
        public Guid GroupId { get; set; }
        public List<Guid> Users { get; set; }
        public int CurrentPart { get; set; }
        public int TotalParts { get; set; }
        public bool Exclusionary { get; set; }
    }
}