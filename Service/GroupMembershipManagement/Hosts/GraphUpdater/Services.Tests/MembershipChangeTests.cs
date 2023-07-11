// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models.ServiceBus;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
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
                Assert.AreEqual(initial.SyncJobId, chunk.SyncJobId);
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
            Assert.AreEqual(initial.SyncJobId, rejoined.SyncJobId);
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
                Assert.AreEqual(initial.SyncJobId, chunk.SyncJobId);
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
            Assert.AreEqual(initial.SyncJobId, rejoined.SyncJobId);
            CollectionAssert.AreEqual(initial.SourceMembers, rejoined.SourceMembers);
		}

		[TestMethod]
		public void SplitsAndJoinsEmpty()
		{
			var initial = new GroupMembership()
			{
				Destination = new AzureADGroup { ObjectId = Guid.NewGuid() },
				SyncJobId = Guid.NewGuid()
			};

			var split = initial.Split();

			Assert.AreEqual(1, split.Length);
			Assert.AreEqual(0, split.Single().SourceMembers.Count);
			Assert.AreEqual(initial.Destination, split.Single().Destination);
			Assert.AreEqual(initial.SyncJobId, split.Single().SyncJobId);
			Assert.IsTrue(split.Single().IsLastMessage);

			var rejoined = GroupMembership.Merge(split);

			Assert.AreEqual(0, rejoined.SourceMembers.Count);
			Assert.AreEqual(initial.Destination, rejoined.Destination);
			Assert.AreEqual(initial.SyncJobId, rejoined.SyncJobId);
			CollectionAssert.AreEqual(initial.SourceMembers, rejoined.SourceMembers);
		}
	}
}
