// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Entities.ServiceBus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Tests.FunctionApps.Mocks
{
	static class MockGroupMembershipHelper
	{
		public const int UserCount = 250001;
		public static AzureADGroup[] CreateMockGroups()
		{
			return Enumerable.Range(0, 10).Select(x => new AzureADGroup { ObjectId = Guid.NewGuid() }).ToArray();
		}
		
		public static GroupMembership MockGroupMembership()
		{
			return new GroupMembership()
			{
				Sources = CreateMockGroups(),
				Destination = new AzureADGroup { ObjectId = Guid.NewGuid() },
				SyncJobPartitionKey = Guid.NewGuid().ToString(),
				SyncJobRowKey = Guid.NewGuid().ToString(),
				SourceMembers = Enumerable.Range(0, UserCount).Select(
						x => new AzureADUser { ObjectId = Guid.NewGuid() }).ToList()
			};

		}


	}
}
