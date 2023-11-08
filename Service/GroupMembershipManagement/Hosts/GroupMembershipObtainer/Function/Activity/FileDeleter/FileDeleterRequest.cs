// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;

namespace Hosts.GroupMembershipObtainer
{
    public class FileDeleterRequest
    {
        public string FilePath { get; set; }
        public SyncJob SyncJob { get; set; }
    }
}