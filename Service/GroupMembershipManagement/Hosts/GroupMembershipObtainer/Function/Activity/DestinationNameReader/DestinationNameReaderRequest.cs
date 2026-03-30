// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;

namespace Hosts.GroupMembershipObtainer
{
    public class DestinationNameReaderRequest
    {
        public SyncJob SyncJob { get; set; }
        public int CurrentPart { get; set; }
        public int TotalParts { get; set; }
    }
}
