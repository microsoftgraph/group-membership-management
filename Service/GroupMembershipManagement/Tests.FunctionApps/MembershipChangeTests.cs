// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Entities.ServiceBus;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Tests.FunctionApps.Mocks;

namespace Tests.FunctionApps
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
				Assert.AreEqual(initial.Sources, chunk.Sources);
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

			Assert.AreEqual(initial.Sources, rejoined.Sources);
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
				CollectionAssert.AreEqual(initial.Sources, chunk.Sources);
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

			CollectionAssert.AreEqual(initial.Sources, rejoined.Sources);
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
				Sources = MockGroupMembershipHelper.CreateMockGroups(),
				Destination = new AzureADGroup { ObjectId = Guid.NewGuid() },
				SyncJobPartitionKey = Guid.NewGuid().ToString(),
				SyncJobRowKey = Guid.NewGuid().ToString(),
			};

			var split = initial.Split();

			Assert.AreEqual(1, split.Length);
			Assert.AreEqual(0, split.Single().SourceMembers.Count);
			Assert.AreEqual(initial.Sources, split.Single().Sources);
			Assert.AreEqual(initial.Destination, split.Single().Destination);
			Assert.AreEqual(initial.SyncJobRowKey, split.Single().SyncJobRowKey);
			Assert.AreEqual(initial.SyncJobPartitionKey, split.Single().SyncJobPartitionKey);
			Assert.IsTrue(split.Single().IsLastMessage);

			var rejoined = GroupMembership.Merge(split);

			Assert.AreEqual(0, rejoined.SourceMembers.Count);
			Assert.AreEqual(initial.Sources, rejoined.Sources);
			Assert.AreEqual(initial.Destination, rejoined.Destination);
			Assert.AreEqual(initial.SyncJobRowKey, rejoined.SyncJobRowKey);
			Assert.AreEqual(initial.SyncJobPartitionKey, rejoined.SyncJobPartitionKey);
			CollectionAssert.AreEqual(initial.SourceMembers, rejoined.SourceMembers);
		}

		// DynamicData example from https://www.meziantou.net/mstest-v2-data-tests.htm
		[DataTestMethod]
		[DynamicData(nameof(SizesToTest), DynamicDataSourceType.Method)]
		public void SerializationSize(int chunkSize)
		{
			// https://docs.microsoft.com/en-us/azure/service-bus-messaging/service-bus-quotas
			const int maxMessageSize = 256 * 1024; // 256 kilobytes
			const int largestSafePayload = maxMessageSize - (64 * 1024); // minus the maximum header size

			var initial = MockGroupMembershipHelper.MockGroupMembership();
			foreach (var chunk in initial.Split(chunkSize))
			{
				var serialized = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(chunk));
				Assert.IsTrue(serialized.Length <= largestSafePayload);
			}
		}

		public static IEnumerable<object[]> SizesToTest()
		{
			for (int i = 100; i < 3700; i += 100)
			{
				yield return new object[] { i };
			}

			for (int i = 3700; i <= ChunkSize; i += 1)
			{
				yield return new object[] { i };
			}
		}

		[TestMethod]
		public void DefaultSerializationSizeWorks()
		{
			// https://docs.microsoft.com/en-us/azure/service-bus-messaging/service-bus-quotas
			const int maxMessageSize = 256 * 1024; // 256 kilobytes
			const int largestSafePayload = maxMessageSize - (64 * 1024); // minus the maximum header size

			var initial = MockGroupMembershipHelper.MockGroupMembership();

			foreach (var chunk in initial.Split())
			{
				var serialized = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(chunk));
				Assert.IsTrue(serialized.Length <= largestSafePayload);
			}
		}
	}
}

