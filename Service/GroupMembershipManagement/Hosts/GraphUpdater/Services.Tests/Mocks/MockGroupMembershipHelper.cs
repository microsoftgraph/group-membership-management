// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models.ServiceBus;
using Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Services.Tests.Mocks
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
				Destination = new AzureADGroup { ObjectId = Guid.NewGuid() },
				SyncJobPartitionKey = Guid.NewGuid().ToString(),
				SyncJobRowKey = Guid.NewGuid().ToString(),
				RunId = Guid.NewGuid(),
				SourceMembers = Enumerable.Range(0, UserCount).Select(
						x => new AzureADUser { ObjectId = Guid.NewGuid() }).ToList()
			};

		}


	}
}
