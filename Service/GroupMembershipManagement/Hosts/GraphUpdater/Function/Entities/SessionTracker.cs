// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;

namespace Hosts.GraphUpdater
{
    public class SessionTracker
    {
        public DateTime LastAccessTime { get; set; }
        public string LatestMessageId { get; set; }
        public Guid RunId { get; set; }
        public List<string> LockTokens { get; set; }
        public string JobPartitionKey { get; set; }
        public string JobRowKey { get; set; }
        public bool ReceivedLastMessage { get; set; }
    }
}
