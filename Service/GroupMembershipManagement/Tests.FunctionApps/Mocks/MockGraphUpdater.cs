// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities.ServiceBus;
using Hosts.GraphUpdater;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Tests.FunctionApps.Mocks
{
	class MockGraphUpdater : IGraphUpdater
	{
		public List<GroupMembership> Actual { get; private set; } = new List<GroupMembership>();
		public Task CalculateDifference(GroupMembership groupMembership)
		{
			Actual.Add(groupMembership);
			return Task.CompletedTask;
		}
	}
}

