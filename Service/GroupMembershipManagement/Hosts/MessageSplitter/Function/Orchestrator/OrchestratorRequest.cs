// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;

namespace Hosts.MessageSplitter
{
    public class OrchestratorRequest
    {
        public MembershipHttpRequest MembershipRequest { get; set; }
        public string UpdaterType { get; set; }
        public string MessageId { get; set; }
        public string SubscriptionName { get; set; }
        public string CurrentLaneSize { get; set; }
    }
}
