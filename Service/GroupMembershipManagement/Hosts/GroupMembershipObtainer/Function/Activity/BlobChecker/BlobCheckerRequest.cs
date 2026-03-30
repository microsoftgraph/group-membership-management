// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;

namespace Hosts.GroupMembershipObtainer
{
    public class BlobCheckerRequest
    {
        public string Prefix { get; set; }
        public int CurrentPart { get; set; }
        public int TotalParts { get; set; }
        public SyncJob SyncJob { get; set; }
    }
}