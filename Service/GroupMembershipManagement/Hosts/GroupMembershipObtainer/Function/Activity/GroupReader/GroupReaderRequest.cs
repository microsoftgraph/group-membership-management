// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;

namespace Hosts.GroupMembershipObtainer
{
    public class GroupReaderRequest
    {
        public SyncJob SyncJob { get; set; }
        public Guid RunId { get; set; }
        public int CurrentPart { get; set; }
        public bool IsDestinationPart { get; set; }
    }
}