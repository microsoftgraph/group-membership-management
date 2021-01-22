// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Repositories.Contracts;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;

namespace Repositories.SecurityGroupCrawler
{
	public delegate void FoundUser(AzureADUser user);
	public delegate void FoundGroup(AzureADGroup group);
	public delegate void FoundGroupCycle(SecurityGroupCycle cycle);

	public class SGCrawler
	{
		private readonly IGraphGroupRepository _sgRepo;
		public SGCrawler(IGraphGroupRepository securityGroupRepository)
		{
			_sgRepo = securityGroupRepository;
		}

		public FoundUser FoundUserAction { private get; set; } = (_) => { };
		public FoundGroup FoundGroupAction { private get; set; } = (_) => { };
		public FoundGroupCycle FoundGroupCycleAction { private get; set; } = (_) => { };

		// this is the number of concurrent tasks we have processing the queue
		// since this involves a lot of waiting on I/O with the Graph, it's good to
		// keep this number high- the runtime will manage the actual threads automatically.
		private const int DegreeOfParallelism = 1024;

		private ConcurrentDictionary<Guid, byte> _visitedGroups;
		private readonly ConcurrentQueue<(AzureADGroup group, ImmutableList<AzureADGroup> visited)> _toVisit = new ConcurrentQueue<(AzureADGroup group, ImmutableList<AzureADGroup> visited)>();
		public async Task CrawlGroup(AzureADGroup head)
		{
			_visitedGroups = new ConcurrentDictionary<Guid, byte>();
			var visited = ImmutableList.Create(head);
			FoundGroupAction(head);
			EnqueueRange(ProcessChildren(await _sgRepo.GetChildrenOfGroup(head.ObjectId)).Select(x => (x, visited)));

			await Task.WhenAll(Enumerable.Range(0, DegreeOfParallelism).Select(_ => ProcessQueue()));

			// no sense in keeping memory allocated for this.
			// the queue gets emptied naturally, but no sense in waiting if to be called again before we release this memory
			// just setting it to null should be fine, but clearing it first doesn't hurt anything and helps the garbage collector out
			_visitedGroups.Clear();
			_visitedGroups = null;
		}

		private async Task ProcessQueue()
		{
			while (_toVisit.TryDequeue(out var pair))
			{
				await Visit(pair.group, pair.visited);
			}
		}

		private async Task Visit(AzureADGroup group, ImmutableList<AzureADGroup> visited)
		{
			// if we've been here before, mark a cycle and return
			if (CheckVisited(group, visited)) { return; }

			// Try to mark that we've been here before. If it fails, it means someone beat us to it, and we should return.
			if (!_visitedGroups.TryAdd(group.ObjectId, 0)) { return; }
			FoundGroupAction(group);
			var getChildren = _sgRepo.GetChildrenOfGroup(group.ObjectId);

			visited = visited.Add(group);

			EnqueueRange(ProcessChildren(await getChildren).Select(x => (x, visited)));
		}

		private bool CheckVisited(AzureADGroup group, ImmutableList<AzureADGroup> visited)
		{
			var idx = visited.IndexOf(group);
			if (idx == -1) { return false; }
			FoundGroupCycleAction(new SecurityGroupCycle(group, visited.RemoveRange(0, idx)));
			return true;
		}

		private IEnumerable<AzureADGroup> ProcessChildren(IEnumerable<IAzureADObject> children)
		{
			foreach (var child in children)
			{
				switch (child)
				{
					case AzureADUser u:
						FoundUserAction(u);
						break;
					case AzureADGroup g:
						yield return g;
						break;
					default:
						throw new ArgumentException("Expected either an AzureADUser or Group, got " + child.GetType().Name);
				}
			}
		}

		private void EnqueueRange(IEnumerable<(AzureADGroup group, ImmutableList<AzureADGroup> visited)> range)
		{
			foreach (var pair in range)
			{
				_toVisit.Enqueue(pair);
			}
		}
	}


}
