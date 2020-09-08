// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities.ServiceBus;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Tests.FunctionApps.Mocks
{
	class MockMembershipServiceBusRepository : IMembershipServiceBusRepository
	{
		public GroupMembership Sent { get; private set; } = null;
		public string SentFrom { get; private set; } = null;

		public Task SendMembership(GroupMembership groupMembership, string sentFrom = "")
		{
			// this should be called exactly once
			if (Sent != null || SentFrom != null) { throw new ArgumentException("SendMembership should only be called once."); }

			Sent = groupMembership ?? throw new ArgumentNullException("groupMembership can't be null.");
			SentFrom = sentFrom;
			return Task.CompletedTask;
		}
	}
}

