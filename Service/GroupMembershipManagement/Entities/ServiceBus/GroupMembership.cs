// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;

namespace Entities.ServiceBus
{
	[ExcludeFromCodeCoverage]
	public class GroupMembership
	{
		public AzureADGroup[] Sources { get; set; }
		public AzureADGroup Destination { get; set; }
		public List<AzureADUser> SourceMembers { get; set; } = new List<AzureADUser>();
		public string SyncJobRowKey { get; set; }
		public string SyncJobPartitionKey { get; set; }

		/// <summary>
		/// Don't worry about setting this yourself, this is for Split and the serializer to set.
		/// </summary>
		public bool IsLastMessage { get; set; }

		/// <summary>
		/// This is made public mostly for testing, but you can use it to get an idea of how many GroupMemberships[] you'll get after calling Split if you want.
		/// </summary>
		public const int MembersPerChunk = 3766;
		public GroupMembership[] Split(int perChunk = MembersPerChunk)
		{
			var toReturn = ChunksOfSize(SourceMembers, perChunk).
				Select(x => new GroupMembership { Sources = Sources, Destination = Destination, SyncJobPartitionKey = SyncJobPartitionKey, SyncJobRowKey = SyncJobRowKey,
					SourceMembers = x, IsLastMessage = false }).ToArray();
			toReturn.Last().IsLastMessage = true;
			return toReturn;
		}

		public static GroupMembership Merge(IEnumerable<GroupMembership> groupMemberships)
		{
			return groupMemberships.Aggregate((acc, current) => { acc.SourceMembers.AddRange(current.SourceMembers); return acc; });
		}

		private static IEnumerable<List<T>> ChunksOfSize<T>(IEnumerable<T> enumerable, int chunkSize)
		{
			var toReturn = new List<T>();
			foreach (var item in enumerable)
			{
				if (toReturn.Count == chunkSize)
				{
					yield return toReturn;
					toReturn = new List<T>();
				}
				toReturn.Add(item);
			}
			yield return toReturn;
		}

	}
}

