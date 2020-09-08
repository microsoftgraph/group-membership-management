// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
	public interface IGraphGroupRepository
	{
		// Only the circular reference checker uses this, it can be removed when we get rid of the circular reference checker.
		Task<IEnumerable<IAzureADObject>> GetChildrenOfGroup(Guid objectId);
		Task<List<AzureADUser>> GetUsersInGroupTransitively(Guid objectId);
		Task AddUsersToGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup);
		Task RemoveUsersFromGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup);
	}
}

