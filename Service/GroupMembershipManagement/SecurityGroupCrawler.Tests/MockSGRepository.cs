using Entities;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Threading.Tasks;

namespace Repositories.SecurityGroupCrawler.Tests
{
	class MockSGRepository : IGraphGroupRepository
	{
		private readonly Dictionary<AzureADGroup, IEnumerable<IAzureADObject>> _groupsToChildren;
		private readonly TimeSpan _delay;
		public MockSGRepository(Dictionary<AzureADGroup, IEnumerable<IAzureADObject>> groupsToChildren, TimeSpan delay)
		{
			_groupsToChildren = groupsToChildren;
			_delay = delay;
		}

		public Guid RunId { get; set; }

		public Task AddUsersToGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup)
		{
			throw new NotImplementedException();
		}

		public async Task<IEnumerable<IAzureADObject>> GetChildrenOfGroup(Guid objectId)
		{
			await Task.Delay(_delay);
			return _groupsToChildren[new AzureADGroup { ObjectId = objectId }];
		}

		public Task<List<AzureADUser>> GetUsersInGroupTransitively(Guid objectId)
		{
			throw new NotImplementedException();
		}

		public Task<bool> GroupExists(Guid objectId)
		{
			return Task.FromResult(_groupsToChildren.ContainsKey(new AzureADGroup { ObjectId = objectId }));
		}

		public Task<bool> IsAppIDOwnerOfGroup(string appId, Guid groupObjectId)
		{
			throw new NotImplementedException();
		}

		public Task RemoveUsersFromGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup)
		{
			throw new NotImplementedException();
		}
	}
}
