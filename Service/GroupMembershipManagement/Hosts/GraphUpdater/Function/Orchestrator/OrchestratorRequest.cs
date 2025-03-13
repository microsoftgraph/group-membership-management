// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Models.ServiceBus;

namespace Hosts.GraphUpdater
{
    public class OrchestratorRequest
    {
        public OrchestratorRequest()
        {

        }

        public OrchestratorRequest(MembershipHttpRequest request)
        {
            MembershipHttpRequest = request;
        }

        public OrchestratorRequest(GroupMembership request)
        {
            GroupMembership = request;
        }

        public MembershipHttpRequest MembershipHttpRequest { get; set; }
        public GroupMembership GroupMembership { get; set; }
        public SyncJob SyncJob => MembershipHttpRequest?.SyncJob ?? GroupMembership?.SyncJob;

    }
}
