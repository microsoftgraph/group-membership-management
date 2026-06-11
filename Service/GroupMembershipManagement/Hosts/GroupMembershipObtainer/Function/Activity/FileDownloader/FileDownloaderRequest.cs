// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;

namespace Hosts.GroupMembershipObtainer
{
    public class FileDownloaderRequest
    {
        public string FilePath { get; set; }
        public SyncJob SyncJob { get; set; }
        public bool CheckFileAge { get; set; }
        public int CurrentPart { get; set; }
        public int TotalParts { get; set; }
    }
}
