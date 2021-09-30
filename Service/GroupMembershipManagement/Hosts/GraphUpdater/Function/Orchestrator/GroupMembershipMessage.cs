// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities.ServiceBus;

namespace Hosts.GraphUpdater
{
	public class GroupMembershipMessage
	{
		public GroupMembership Body { get; set; }
		public string LockToken { get; set; }
        public bool IsCancelationMessage { get; set; }
    }
}
