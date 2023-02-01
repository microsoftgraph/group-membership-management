// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models.Entities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Repositories.MembershipDifference;
using System;
using System.Linq;

using Help = Repositories.Mocks.TestObjectHelpers;

namespace Services.Tests
{
	[TestClass]
	public class MembershipDifferenceCalculatorTests
	{
		private readonly Help _help = new Help();

		private readonly MembershipDifferenceCalculator<AzureADUser> _calc = new MembershipDifferenceCalculator<AzureADUser>();

		[TestMethod]
		public void HandlesDisjointGroups()
		{
			var source = new[] { _help.UserNamed(1), _help.UserNamed(2), _help.UserNamed(3) };
			var destination = new[] { _help.UserNamed(4), _help.UserNamed(5) };

			var result = _calc.CalculateDifference(source, destination);

			CollectionAssert.AreEquivalent(source, result.ToAdd.ToArray());
			CollectionAssert.AreEquivalent(destination, result.ToRemove.ToArray());
		}

		[TestMethod]
		public void HandlesOverlappingGroups()
		{
			var source = new[] { _help.UserNamed(1), _help.UserNamed(2), _help.UserNamed(3) };
			var destination = new[] { _help.UserNamed(1), _help.UserNamed(4), _help.UserNamed(5) };

			var result = _calc.CalculateDifference(source, destination);

			CollectionAssert.AreEquivalent(new[] { _help.UserNamed(2), _help.UserNamed(3) }, result.ToAdd.ToArray());
			CollectionAssert.AreEquivalent(new[] { _help.UserNamed(4), _help.UserNamed(5) }, result.ToRemove.ToArray());
		}

		[TestMethod]
		public void HandlesOverlappingGroupsWithDuplicates()
		{
			var source = new[] { _help.UserNamed(1), _help.UserNamed(1), _help.UserNamed(2), _help.UserNamed(2), _help.UserNamed(3) };
			var destination = new[] { _help.UserNamed(5), _help.UserNamed(1), _help.UserNamed(4), _help.UserNamed(5) };

			var result = _calc.CalculateDifference(source, destination);

			CollectionAssert.AreEquivalent(new[] { _help.UserNamed(2), _help.UserNamed(3) }, result.ToAdd.ToArray());
			CollectionAssert.AreEquivalent(new[] { _help.UserNamed(4), _help.UserNamed(5) }, result.ToRemove.ToArray());
		}

		[TestMethod]
		public void HandlesEmptyDestination()
		{
			var source = new[] { _help.UserNamed(1), _help.UserNamed(2), _help.UserNamed(3) };
			var destination = Enumerable.Empty<AzureADUser>();

			var result = _calc.CalculateDifference(source, destination);

			CollectionAssert.AreEquivalent(source, result.ToAdd.ToArray());
			Assert.IsFalse(result.ToRemove.Any());
		}

		[TestMethod]
		public void HandlesEmptySource()
		{
			var source = Enumerable.Empty<AzureADUser>();
			var destination= new[] { _help.UserNamed(1), _help.UserNamed(2), _help.UserNamed(3) };

			var result = _calc.CalculateDifference(source, destination);

			Assert.IsFalse(result.ToAdd.Any());
			CollectionAssert.AreEquivalent(destination, result.ToRemove.ToArray());
		}

		[TestMethod]
		[DataTestMethod]
		[DataRow(1000, 0)]
		[DataRow(1000, 1)]
		[DataRow(0, 1000)]
		[DataRow(1, 1000)]
		[DataRow(1000, 1000)]
		[DataRow(10000, 10000)]
		[DataRow(100000, 100000)]
		[DataRow(500000, 500000)]
		[DataRow(1000000, 1000000)]
		[DataRow(5000000, 5000000)]
		//[DataRow(10000000, 10000000)]
		public void HandlesBigDisjointInputs(int sourceSize, int destSize)
		{
			var source = MakeUsers(sourceSize, 0);
			var destination = MakeUsers(destSize, sourceSize);

			var result = _calc.CalculateDifference(source, destination);

			// this is taking advantage of the fact that ToAdd and ToRemove are secretly hashsets with this implementation
			// so this gets us a fast compare and avoids copying the set
			Assert.IsTrue(result.ToAdd.ToHashSet().SetEquals(source));
			Assert.IsTrue(result.ToRemove.ToHashSet().SetEquals(destination));
		}

		[TestMethod]
		[DataTestMethod]
		[DataRow(1000, 1000)]
		[DataRow(10000, 10000)]
		[DataRow(100000, 100000)]
		[DataRow(500000, 500000)]
		[DataRow(1000000, 1000000)]
		[DataRow(5000000, 5000000)]
		[DataRow(5000000, 1)]
		[DataRow(1, 5000000)]
		[DataRow(5000000, 2)]
		[DataRow(2, 5000000)]
		[DataRow(5000000, 100)]
		[DataRow(100, 5000000)]
	//	[DataRow(10000000, 10000000)] //this does work, but it takes a lot of memory
		public void HandlesBigOverlappingInputs(int sourceSize, int destSize)
		{
			// Can't have more overlapping users than there are users in the smaller set.
			// (for example, if one of these only has one user, the most users that can overlap is one)
			int numberOfOverlappingUsers = (int)(Math.Min(sourceSize, destSize) * Help.Rand.NextDouble());

			var source = MakeUsers(sourceSize, 0);
			var destination = MakeUsers(destSize, sourceSize - numberOfOverlappingUsers);

			var result = _calc.CalculateDifference(source, destination);

			/*
								B            A
				 +      C       +            +                    +
				 +--------------+            |                    | source size
			zero +------------------------------------------------+ + destination size
				 |              |            +--------------------+ - # of overlapping users
				 +              +            +         D          +

			This diagram shows the range of users for this test.
			Basically, think of this test as creating a bunch of users on a number line.
					* Point A is the end of users in the source group. [0, A] is the source group.
					* Point B is the point where the source and destination group begin to overlap. [B, Maximum] is the dest. group.
						* Note that B is always calculated from the end of the source group. 
							This ensures that the overlap always includes the end of the source group.
							This makes calculating which range is which for the test much easier and faster.
					* Range C is the users who are in the source group, but not in the destination group.
						* These are the ones who should be in ToAdd.
					* Range D is the users who are in the destination group, but not in the source group.
						* These are the ones who should be in ToRemove.
			
				The math is a little tricky here, but the key points to remember are:
					* we want to add everything from zero to when overlap starts
					* don't do anything with the overlap
					* remove everyone between the overlap and the maximum
			 */


			// this is taking advantage of the fact that ToAdd and ToRemove are secretly hashsets with this implementation
			Assert.IsTrue(result.ToAdd.ToHashSet().SetEquals(source.Take(sourceSize - numberOfOverlappingUsers)));
			Assert.IsTrue(result.ToRemove.ToHashSet().SetEquals(destination.Skip(numberOfOverlappingUsers)));

			// the two result sets should be disjoint
			Assert.IsFalse(result.ToAdd.ToHashSet().Overlaps(result.ToRemove));
		}

		private AzureADUser[] MakeUsers(int size, int startIdx)
		{
			var toreturn = new AzureADUser[size];
			for (int i = 0; i < size; i++)
			{
				int thisIdx = startIdx + i;
				toreturn[i] = _help.UserNamed(thisIdx);
			}
			return toreturn;
		}
	}
}
