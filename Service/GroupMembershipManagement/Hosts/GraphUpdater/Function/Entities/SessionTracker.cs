// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.ServiceBus;
using System;
using System.Collections.Generic;

namespace Hosts.GraphUpdater
{
    public class SessionTracker
    {
        public DateTime LastAccessTime { get; set; }
        public List<Message> MessagesInSession { get; set; }
        public string SessionId { get; set; }
        public Guid RunId { get { return new Guid(SessionId); } }
        public bool ReceivedLastMessage { get; set; }
        public int TotalMessageCountExpected { get; set; }
        public string SyncJobPartitionKey { get; set; }
        public string SyncJobRowKey { get; set; }
    }
}
