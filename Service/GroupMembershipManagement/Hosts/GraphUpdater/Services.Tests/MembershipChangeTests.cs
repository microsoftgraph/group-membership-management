// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Entities.ServiceBus;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Services.Tests.Mocks;
using System;
using System.Linq;

namespace Services.Tests
{
    [TestClass]
	public class MembershipChangeTests
	{
		private const int ChunkSize = GroupMembership.MembersPerChunk;
		private const int UserCount = MockGroupMembershipHelper.UserCount;

		[TestMethod]
		public void SplitsAndJoins()
		{
			var initial = MockGroupMembershipHelper.MockGroupMembership();
			var split = initial.Split();

			Assert.AreEqual((UserCount / ChunkSize) + 1, split.Length);

			foreach (var chunk in split)
			{
				Assert.AreEqual(initial.Destination, chunk.Destination);
				Assert.AreEqual(initial.SyncJobRowKey, chunk.SyncJobRowKey);
				Assert.AreEqual(initial.SyncJobPartitionKey, chunk.SyncJobPartitionKey);
			}

			foreach (var nonlastChunk in split.Take(split.Length - 1))
			{
				Assert.IsFalse(nonlastChunk.IsLastMessage);
				Assert.AreEqual(ChunkSize, nonlastChunk.SourceMembers.Count);
			}

			Assert.IsTrue(split.Last().IsLastMessage);
			Assert.AreEqual(UserCount % ChunkSize, split.Last().SourceMembers.Count);

			var rejoined = GroupMembership.Merge(split);

			Assert.AreEqual(initial.Destination, rejoined.Destination);
			Assert.AreEqual(initial.SyncJobRowKey, rejoined.SyncJobRowKey);
			Assert.AreEqual(initial.SyncJobPartitionKey, rejoined.SyncJobPartitionKey);
			CollectionAssert.AreEqual(initial.SourceMembers, rejoined.SourceMembers);
		}

		[TestMethod]
		public void RoundTripSerialization()
		{
			var initial = MockGroupMembershipHelper.MockGroupMembership();

			var split = JsonConvert.DeserializeObject<GroupMembership[]>(JsonConvert.SerializeObject(initial.Split()));

			Assert.AreEqual((UserCount / ChunkSize) + 1, split.Length);

			foreach (var chunk in split)
			{
				Assert.AreEqual(initial.Destination, chunk.Destination);
				Assert.AreEqual(initial.SyncJobRowKey, chunk.SyncJobRowKey);
				Assert.AreEqual(initial.SyncJobPartitionKey, chunk.SyncJobPartitionKey);
			}

			foreach (var nonlastChunk in split.Take(split.Length - 1))
			{
				Assert.IsFalse(nonlastChunk.IsLastMessage);
				Assert.AreEqual(ChunkSize, nonlastChunk.SourceMembers.Count);
			}

			Assert.IsTrue(split.Last().IsLastMessage);
			Assert.AreEqual(UserCount % ChunkSize, split.Last().SourceMembers.Count);

			var rejoined = GroupMembership.Merge(split);

			Assert.AreEqual(initial.Destination, rejoined.Destination);
			Assert.AreEqual(initial.SyncJobRowKey, rejoined.SyncJobRowKey);
			Assert.AreEqual(initial.SyncJobPartitionKey, rejoined.SyncJobPartitionKey);
			CollectionAssert.AreEqual(initial.SourceMembers, rejoined.SourceMembers);
		}

		[TestMethod]
		public void SplitsAndJoinsEmpty()
		{
			var initial = new GroupMembership()
			{
				Destination = new AzureADGroup { ObjectId = Guid.NewGuid() },
				SyncJobPartitionKey = Guid.NewGuid().ToString(),
				SyncJobRowKey = Guid.NewGuid().ToString(),
			};

			var split = initial.Split();

			Assert.AreEqual(1, split.Length);
			Assert.AreEqual(0, split.Single().SourceMembers.Count);
			Assert.AreEqual(initial.Destination, split.Single().Destination);
			Assert.AreEqual(initial.SyncJobRowKey, split.Single().SyncJobRowKey);
			Assert.AreEqual(initial.SyncJobPartitionKey, split.Single().SyncJobPartitionKey);
			Assert.IsTrue(split.Single().IsLastMessage);

			var rejoined = GroupMembership.Merge(split);

			Assert.AreEqual(0, rejoined.SourceMembers.Count);
			Assert.AreEqual(initial.Destination, rejoined.Destination);
			Assert.AreEqual(initial.SyncJobRowKey, rejoined.SyncJobRowKey);
			Assert.AreEqual(initial.SyncJobPartitionKey, rejoined.SyncJobPartitionKey);
			CollectionAssert.AreEqual(initial.SourceMembers, rejoined.SourceMembers);
		}
	}
}
