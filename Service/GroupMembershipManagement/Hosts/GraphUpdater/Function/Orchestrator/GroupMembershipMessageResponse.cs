// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;

namespace Hosts.GraphUpdater
{
	public class GroupMembershipMessageResponse
	{
		public IEnumerable<GroupMembershipMessage> CompletedGroupMembershipMessages { get; set; }
		public bool ShouldCompleteMessage { get; set; }
	}
}
